// The MIT License (MIT)
// 
// Copyright (c) 2015 Microsoft
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

namespace Microsoft.Diagnostics.Tracing.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Diagnostics.Tracing;
    using System.Globalization;
    using System.IO;
    using System.Threading;
    using System.Xml;

    public sealed partial class LogManager
    {
        #region Public
        /// <summary>
        /// Provide string-based configuration which will be applied additively after any file configuration.
        /// </summary>
        /// <remarks>
        /// Any change will force a full configuration reload. This function is not thread-safe with regards to
        /// concurrent callers.
        /// </remarks>
        /// <param name="configurationXml">A string containing the XML configuration</param>
        /// <returns>True if the configuration was successfully applied</returns>
        public static bool SetConfiguration(string configurationXml)
        {
            if (!IsConfigurationValid(configurationXml))
            {
                return false;
            }

            singleton.configurationData = configurationXml;
            return singleton.ApplyConfiguration();
        }

        /// <summary>
        /// Check if a configuration string is valid
        /// </summary>
        /// <param name="configurationXml">A string containing the XML configuration</param>
        /// <returns>true if the configuration is valid, false otherwise</returns>
        public static bool IsConfigurationValid(string configurationXml)
        {
            var unused = new Dictionary<string, LogConfiguration>(StringComparer.OrdinalIgnoreCase);
            return ParseConfiguration(configurationXml, unused);
        }

        /// <summary>
        /// Assign a file to read configuration from
        /// </summary>
        /// <param name="filename">The file to read configuration from (or null to remove use of the file)</param>
        /// <returns>true if the file was valid, false otherwise</returns>
        public static bool SetConfigurationFile(string filename)
        {
            return singleton.UpdateConfigurationFile(filename);
        }
        #endregion

        #region Private
        // HEY! HEY YOU! Are you adding stuff here? You're adding stuff, it's cool. Just go update
        // http://bing/wiki/Managed_ETW_Logging#Logging_Configuration with what you've added. Santa
        // will bring you bonus gifts.
        private const string EtwOverrideXpath = "/loggers/etwlogging";
        private const string EtwOverrideEnabledAttribute = "enabled";
        private const string LogTagXpath = "/loggers/log";
        private const string LogBufferSizeAttribute = "buffersizemb";
        private const string LogDirectoryAttribute = "directory";
        private const string LogFilenameTemplateAttribute = "filenametemplate";
        private const string LogTimestampLocal = "timestamplocal";
        private const string LogFilterTag = "filter";
        private const string LogNameAttribute = "name";
        private const string LogRotationAttribute = "rotationinterval";
        private const string LogTypeAttribute = "type";
        private const string LogHostnameAttribute = "hostname";
        private const string LogPortAttribute = "port";
        private const string SourceTag = "source";
        private const string SourceKeywordsAttribute = "keywords";
        private const string SourceMinSeverityAttribute = "minimumseverity";
        private const string SourceProviderIDAttribute = "providerid";
        private const string SourceProviderNameAttribute = "name";
        private string configurationFile;
        private long configurationFileLastWrite;
        private FileSystemWatcher configurationFileWatcher;
        internal int configurationFileReloadCount; // primarily a test hook.
        private string configurationFileData;
        private string configurationData;
        private Dictionary<string, LogConfiguration> logConfigurations;

        private static bool ParseConfiguration(string configurationXml, Dictionary<string, LogConfiguration> loggers)
        {
            bool clean = true; // used to track whether any errors were encountered

            if (string.IsNullOrEmpty(configurationXml))
            {
                return true; // it's okay to have nothing at all
            }

            var configuration = new XmlDocument();
            try
            {
                configuration.LoadXml(configurationXml);
            }
            catch (XmlException)
            {
                InternalLogger.Write.InvalidConfiguration("Configuration was not valid XML");
                return false;
            }

            XmlNode node = configuration.SelectSingleNode(EtwOverrideXpath);
            if (node != null)
            {
                XmlNode setting = node.Attributes.GetNamedItem(EtwOverrideEnabledAttribute);
                bool isEnabled = (AllowEtwLogging == AllowEtwLoggingValues.Enabled);
                if (setting == null || !bool.TryParse(setting.Value, out isEnabled))
                {
                    InternalLogger.Write.InvalidConfiguration(EtwOverrideXpath + " tag has invalid " +
                                                              EtwOverrideEnabledAttribute + " attribute");
                    clean = false;
                }

                AllowEtwLogging = isEnabled ? AllowEtwLoggingValues.Enabled : AllowEtwLoggingValues.Disabled;
            }

            foreach (XmlNode log in configuration.SelectNodes(LogTagXpath))
            {
                string name = GetLogNameFromNode(log);
                LoggerType type = GetLogTypeFromNode(log);

                if (type == LoggerType.None)
                {
                    // GetLogTypeFromNode logs this particular error.
                    clean = false;
                    continue;
                }

                if (type == LoggerType.Console)
                {
                    if (name != null)
                    {
                        InternalLogger.Write.InvalidConfiguration("console log should not have a name");
                        clean = false;
                    }

                    // We use a special name for the console logger that is invalid for file loggers so we can track
                    // it along with them.
                    name = ConsoleLoggerName;
                }
                else
                {
                    if (string.IsNullOrEmpty(name))
                    {
                        InternalLogger.Write.InvalidConfiguration("cannot configure a log with no name");
                        clean = false;
                        continue;
                    }
                    if (name.IndexOfAny(Path.GetInvalidFileNameChars()) != -1)
                    {
                        InternalLogger.Write.InvalidConfiguration("base name of log is invalid " + name);
                        clean = false;
                        continue;
                    }

                    if (type == LoggerType.ETLFile && AllowEtwLogging == AllowEtwLoggingValues.Disabled)
                    {
                        InternalLogger.Write.OverridingEtwLogging(name);
                        type = LoggerType.TextLogFile;
                    }
                }

                // We wish to update existing configuration where possible.
                LogConfiguration config;
                if (!loggers.TryGetValue(name, out config))
                {
                    config = new LogConfiguration();
                }

                config.FileType = type;
                clean &= ParseLogNode(log, config);

                if (config.NamedSources.Count + config.GuidSources.Count == 0)
                {
                    InternalLogger.Write.InvalidConfiguration("log destination " + name + " has no valid sources");
                    clean = false;
                    continue;
                }

                // Ensure what we got has been sanitized. We currently don't do stringent checks on the console logger
                // to see if useless stuff like a rotation interval or buffer size is set, but could in the future get
                // more picky.
                if (config.Filters.Count > 0 && !config.HasFeature(LogConfiguration.Features.RegexFilter))
                {
                    InternalLogger.Write.InvalidConfiguration("log destination " + name + " has filters but type " +
                                                              type + " does not support this feature.");
                    clean = false;
                    config.Filters.Clear();
                }

                loggers[name] = config;
            }

            return clean;
        }

        private bool ApplyConfiguration()
        {
            var newConfig = new Dictionary<string, LogConfiguration>(StringComparer.OrdinalIgnoreCase);
            if (!ParseConfiguration(this.configurationFileData, newConfig)
                || !ParseConfiguration(this.configurationData, newConfig))
            {
                return false;
            }

            lock (this.loggersLock)
            {
                foreach (var logger in this.fileLoggers.Values)
                {
                    logger.Dispose();
                }
                foreach (var logger in this.networkLoggers.Values)
                {
                    logger.Dispose();
                }
                this.fileLoggers.Clear();
                this.networkLoggers.Clear();
                this.logConfigurations = newConfig;

                foreach (var kvp in this.logConfigurations)
                {
                    string loggerName = kvp.Key;
                    LogConfiguration loggerConfig = kvp.Value;
                    IEventLogger logger;
                    if (loggerConfig.FileType == LoggerType.Console)
                    {
                        // We re-create the console logger to clear its config (since we don't have a better way
                        // to do that right now).
                        this.CreateConsoleLogger();
                        logger = this.consoleLogger;
                    }
                    else if (loggerConfig.FileType == LoggerType.Network)
                    {
                        logger = this.CreateNetLogger(loggerName, loggerConfig.Hostname, loggerConfig.Port);
                    }
                    else
                    {
                        logger = this.CreateFileLogger(loggerConfig.FileType, loggerName, loggerConfig.Directory,
                                                       loggerConfig.BufferSize, loggerConfig.RotationInterval,
                                                       loggerConfig.FilenameTemplate,
                                                       loggerConfig.TimestampLocal);
                    }

                    foreach (var f in loggerConfig.Filters)
                    {
                        logger.AddRegexFilter(f);
                    }

                    // Build a collection of all desired subscriptions so that we can subscribe in bulk at the end.
                    // We do this because ordering may matter to specific types of loggers and they are best suited to
                    // manage that internally.
                    var subscriptions = new List<EventProviderSubscription>();
                    foreach (var ns in loggerConfig.NamedSources)
                    {
                        EventSourceInfo sourceInfo;
                        string name = ns.Key;
                        LogSourceLevels levels = ns.Value;
                        if ((sourceInfo = GetEventSourceInfo(name)) != null)
                        {
                            subscriptions.Add(new EventProviderSubscription(sourceInfo.Source)
                                              {
                                                  MinimumLevel = levels.Level,
                                                  Keywords = levels.Keywords
                                              });
                        }
                    }

                    foreach (var gs in loggerConfig.GuidSources)
                    {
                        EventSourceInfo sourceInfo;
                        Guid guid = gs.Key;
                        LogSourceLevels levels = gs.Value;
                        if (loggerConfig.HasFeature(LogConfiguration.Features.GuidSubscription))
                        {
                            subscriptions.Add(new EventProviderSubscription(guid)
                                              {
                                                  MinimumLevel = levels.Level,
                                                  Keywords = levels.Keywords
                                              });
                        }
                        else if (loggerConfig.HasFeature(LogConfiguration.Features.EventSourceSubscription) &&
                                 (sourceInfo = GetEventSourceInfo(guid)) != null)
                        {
                            subscriptions.Add(new EventProviderSubscription(sourceInfo.Source)
                                              {
                                                  MinimumLevel = levels.Level,
                                                  Keywords = levels.Keywords
                                              });
                        }
                    }

                    logger.SubscribeToEvents(subscriptions);
                }
            }

            return true;
        }

        private static void ApplyConfigForEventSource(LogConfiguration config, IEventLogger logger, EventSource source,
                                                      LogSourceLevels levels)
        {
            if (config.HasFeature(LogConfiguration.Features.EventSourceSubscription))
            {
                logger.SubscribeToEvents(source, levels.Level, levels.Keywords);
            }
            else if (config.HasFeature(LogConfiguration.Features.GuidSubscription))
            {
                logger.SubscribeToEvents(source.Guid, levels.Level, levels.Keywords);
            }
        }

        private static string GetLogNameFromNode(XmlNode xmlNode)
        {
            if (xmlNode.Attributes[LogNameAttribute] != null)
            {
                return xmlNode.Attributes[LogNameAttribute].Value.Trim();
            }

            return null;
        }

        private static LoggerType GetLogTypeFromNode(XmlNode xmlNode)
        {
            // If no type is provided we currently default to text.
            if (xmlNode.Attributes[LogTypeAttribute] == null)
            {
                return LoggerType.TextLogFile;
            }

            switch (xmlNode.Attributes[LogTypeAttribute].Value.ToLower(CultureInfo.InvariantCulture))
            {
            case "con":
            case "cons":
            case "console":
                return LoggerType.Console;
            case "text":
            case "txt":
                return LoggerType.TextLogFile;
            case "etw":
            case "etl":
                return LoggerType.ETLFile;
            case "net":
            case "network":
                return LoggerType.Network;
            default:
                InternalLogger.Write.InvalidConfiguration("invalid log type " +
                                                          xmlNode.Attributes[LogTypeAttribute].Value);
                return LoggerType.None;
            }
        }

        private static bool ParseLogNode(XmlNode xmlNode, LogConfiguration config)
        {
            bool clean = true;
            foreach (XmlAttribute logAttribute in xmlNode.Attributes)
            {
                switch (logAttribute.Name.ToLower(CultureInfo.InvariantCulture))
                {
                case LogBufferSizeAttribute:
                    if (!int.TryParse(logAttribute.Value, out config.BufferSize)
                        || !IsValidFileBufferSize(config.BufferSize))
                    {
                        InternalLogger.Write.InvalidConfiguration("invalid buffer size " + logAttribute.Value);
                        config.BufferSize = DefaultFileBufferSizeMB;
                        clean = false;
                    }
                    break;
                case LogDirectoryAttribute:
                    if (!IsValidDirectory(logAttribute.Value))
                    {
                        InternalLogger.Write.InvalidConfiguration("invalid directory name " + logAttribute.Value);
                        clean = false;
                    }
                    else
                    {
                        config.Directory = logAttribute.Value;
                    }
                    break;
                case LogFilenameTemplateAttribute:
                    if (!FileBackedLogger.IsValidFilenameTemplate(logAttribute.Value))
                    {
                        InternalLogger.Write.InvalidConfiguration("invalid filename template " + logAttribute.Value);
                        clean = false;
                    }
                    else
                    {
                        config.FilenameTemplate = logAttribute.Value;
                    }
                    break;
                case LogTimestampLocal:
                    if (!bool.TryParse(logAttribute.Value, out config.TimestampLocal))
                    {
                        InternalLogger.Write.InvalidConfiguration("invalid timestamplocal value " + logAttribute.Value);
                        config.TimestampLocal = false;
                        clean = false;
                    }
                    break;
                case LogRotationAttribute:
                    if (!int.TryParse(logAttribute.Value, out config.RotationInterval)
                        || !IsValidRotationInterval(config.RotationInterval))
                    {
                        InternalLogger.Write.InvalidConfiguration("invalid rotation interval " + logAttribute.Value);
                        config.RotationInterval = DefaultRotationInterval;
                        clean = false;
                    }
                    break;
                case LogHostnameAttribute:
                    if (Uri.CheckHostName(logAttribute.Value) == UriHostNameType.Unknown)
                    {
                        InternalLogger.Write.InvalidConfiguration("invalid hostname name " + logAttribute.Value);
                        clean = false;
                    }
                    else
                    {
                        config.Hostname = logAttribute.Value;
                    }
                    break;
                case LogPortAttribute:
                    if (!int.TryParse(logAttribute.Value, out config.Port)
                        || config.Port <= 0)
                    {
                        InternalLogger.Write.InvalidConfiguration("invalid port " + logAttribute.Value);
                        config.Port = 80;
                        clean = false;
                    }
                    break;
                }
            }

            clean &= ParseLogSources(xmlNode, config);
            clean &= ParseLogFilters(xmlNode, config);
            return clean;
        }

        private static bool ParseLogSources(XmlNode xmlNode, LogConfiguration config)
        {
            bool clean = true;

            foreach (XmlNode source in xmlNode.SelectNodes(SourceTag))
            {
                string sourceName = null;
                Guid sourceProvider = Guid.Empty;
                var sourceLevel = EventLevel.Informational;
                var sourceKeywords = (long)EventKeywords.None;
                foreach (XmlAttribute sourceAttribute in source.Attributes)
                {
                    switch (sourceAttribute.Name.ToLower(CultureInfo.InvariantCulture))
                    {
                    case SourceKeywordsAttribute:
                        // Yes, really. The .NET integer TryParse methods will get PISSED if they see 0x in front of
                        // hex digits. Dumb hack is dumb.
                        string value = sourceAttribute.Value.Trim();
                        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        {
                            value = value.Substring(2);
                        }

                        if (!long.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                                           out sourceKeywords))
                        {
                            InternalLogger.Write.InvalidConfiguration("invalid keywords value " + sourceAttribute.Value);
                            clean = false;
                        }
                        break;
                    case SourceMinSeverityAttribute:
                        if (!Enum.TryParse(sourceAttribute.Value, true, out sourceLevel))
                        {
                            InternalLogger.Write.InvalidConfiguration("invalid severity value " + sourceAttribute.Value);
                            clean = false;
                        }
                        break;
                    case SourceProviderIDAttribute:
                        if (!Guid.TryParse(sourceAttribute.Value, out sourceProvider))
                        {
                            InternalLogger.Write.InvalidConfiguration("invalid providerID GUID " + sourceAttribute.Value);
                            clean = false;
                        }
                        break;
                    case SourceProviderNameAttribute:
                        sourceName = sourceAttribute.Value.Trim();
                        break;
                    }
                }

                var levels = new LogSourceLevels(sourceLevel, (EventKeywords)sourceKeywords);
                if (sourceProvider != Guid.Empty)
                {
                    config.GuidSources[sourceProvider] = levels;
                }
                else if (!string.IsNullOrEmpty(sourceName))
                {
                    config.NamedSources[sourceName] = levels;
                }
                else
                {
                    InternalLogger.Write.InvalidConfiguration("source has neither name nor guid");
                    clean = false;
                }
            }

            return clean;
        }

        private static bool ParseLogFilters(XmlNode xmlNode, LogConfiguration config)
        {
            bool clean = true;

            foreach (XmlNode source in xmlNode.SelectNodes(LogFilterTag))
            {
                string filterValue = source.InnerText.Trim();
                if (string.IsNullOrEmpty(filterValue))
                {
                    InternalLogger.Write.InvalidConfiguration("empty/invalid filter value");
                    clean = false;
                    continue;
                }

                if (config.Filters.Contains(filterValue))
                {
                    InternalLogger.Write.InvalidConfiguration("duplicate filter value " + filterValue);
                    clean = false;
                    continue;
                }

                config.Filters.Add(filterValue);
            }

            return clean;
        }

        [SuppressMessage("Microsoft.Security", "CA2122:DoNotIndirectlyExposeMethodsWithLinkDemands")]
        private bool UpdateConfigurationFile(string filename)
        {
            if (filename != null && !File.Exists(filename))
            {
                throw new FileNotFoundException("configuration file does not exist", filename);
            }

            if (this.configurationFileWatcher != null)
            {
                this.configurationFileWatcher.Dispose();
                this.configurationFileWatcher = null;
            }

            InternalLogger.Write.SetConfigurationFile(filename);
            if (filename != null)
            {
                this.configurationFile = Path.GetFullPath(filename);
                this.configurationFileWatcher = new FileSystemWatcher();
                this.configurationFileWatcher.Path = Path.GetDirectoryName(this.configurationFile);
                this.configurationFileWatcher.Filter = Path.GetFileName(this.configurationFile);
                this.configurationFileWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size |
                                                             NotifyFilters.CreationTime;
                this.configurationFileWatcher.Changed += this.OnConfigurationFileChanged;
                this.configurationFileWatcher.EnableRaisingEvents = true;
                return ReadConfigurationFile(this.configurationFile);
            }

            singleton.configurationFileData = null;
            return singleton.ApplyConfiguration();
        }

        private static bool ReadConfigurationFile(string filename)
        {
            Stream file = null;
            bool success = false;
            try
            {
                file = new FileStream(filename, FileMode.Open, FileAccess.Read);
                using (var reader = new StreamReader(file))
                {
                    file = null;
                    singleton.configurationFileData = reader.ReadToEnd();
                    success = singleton.ApplyConfiguration();
                }
            }
            catch (IOException e)
            {
                InternalLogger.Write.InvalidConfiguration(string.Format("Could not open configuration: {0} {1}",
                                                                        e.GetType(), e.Message));
            }
            finally
            {
                if (file != null)
                {
                    file.Dispose();
                }
            }

            if (success)
            {
                InternalLogger.Write.ProcessedConfigurationFile(filename);
                ++singleton.configurationFileReloadCount;
            }
            else
            {
                InternalLogger.Write.InvalidConfigurationFile(filename);
            }
            return success;
        }

        private void OnConfigurationFileChanged(object source, FileSystemEventArgs e)
        {
            long writeTime = new FileInfo(e.FullPath).LastWriteTimeUtc.ToFileTimeUtc();

            if (writeTime !=
                Interlocked.CompareExchange(ref this.configurationFileLastWrite, writeTime,
                                            this.configurationFileLastWrite))
            {
                ReadConfigurationFile(e.FullPath);
            }
        }

        private sealed class LogSourceLevels
        {
            public readonly EventKeywords Keywords;
            public readonly EventLevel Level;

            public LogSourceLevels(EventLevel level, EventKeywords keywords)
            {
                this.Level = level;
                this.Keywords = keywords;
            }
        }

        /// <summary>
        /// A small holder for the parsed out logging configuration of a single log
        /// </summary>
        private sealed class LogConfiguration
        {
            /// <summary>
            /// The set of capabilities an event logger provides
            /// </summary>
            [Flags]
            public enum Features
            {
                None = 0x0,
                EventSourceSubscription = 0x1,
                GuidSubscription = 0x2,
                Unsubscription = 0x4,
                FileBacked = 0x8,
                RegexFilter = 0x10
            }

            public readonly HashSet<string> Filters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public readonly Dictionary<Guid, LogSourceLevels> GuidSources =
                new Dictionary<Guid, LogSourceLevels>();

            public readonly Dictionary<string, LogSourceLevels> NamedSources =
                new Dictionary<string, LogSourceLevels>(StringComparer.OrdinalIgnoreCase);

            public int BufferSize = DefaultFileBufferSizeMB;
            public string Directory;
            public string FilenameTemplate = FileBackedLogger.DefaultFilenameTemplate;
            public LoggerType FileType = LoggerType.None;
            public string Hostname = string.Empty;
            public int Port;
            public int RotationInterval = -1;
            public bool TimestampLocal;

            public bool HasFeature(Features flags)
            {
                Features caps;
                switch (this.FileType)
                {
                case LoggerType.Console:
                case LoggerType.MemoryBuffer:
                case LoggerType.Network:
                    caps = (Features.EventSourceSubscription | Features.Unsubscription |
                            Features.RegexFilter);
                    break;
                case LoggerType.TextLogFile:
                    caps = (Features.EventSourceSubscription | Features.Unsubscription |
                            Features.FileBacked | Features.RegexFilter);
                    break;
                case LoggerType.ETLFile:
                    caps = (Features.EventSourceSubscription | Features.GuidSubscription | Features.FileBacked);
                    break;
                default:
                    throw new InvalidOperationException("features for type " + this.FileType + " are unknowable");
                }

                return ((caps & flags) != 0);
            }
        }
        #endregion
    }
}