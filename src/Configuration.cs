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
    using System.Diagnostics.Tracing;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Xml;

    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    public sealed class InvalidConfigurationException : Exception
    {
        public InvalidConfigurationException() { }

        public InvalidConfigurationException(string message) : base(message) { }

        public InvalidConfigurationException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    /// <summary>
    /// Represents configuration of a set of log destinations along with global options.
    /// </summary>
    /// <remarks>
    /// Once constructed Configuration objects are considered to be immutable. Logs may not be added or removed,
    /// however the <see cref="AllowEtwLogging"/> value may be changed.
    /// </remarks>
    [JsonObject(MemberSerialization = MemberSerialization.OptIn), JsonConverter(typeof(Converter))]
    public sealed class Configuration
    {
        /// <summary>
        /// Possible values for <see cref="Configuration.AllowEtwLogging"/>
        /// </summary>
        public enum AllowEtwLoggingValues
        {
            /// <summary>
            /// Allow the log manager to decide what to do.
            /// </summary>
            None = 0,

            /// <summary>
            /// Write text logs instead of ETW when files are configured for ETW.
            /// </summary>
            Disabled,

            /// <summary>
            /// Write ETW when files are configured to do so. If kernel mode ETW is not available the logger will fail
            /// to initialize.
            /// </summary>
            Enabled
        }

        private readonly HashSet<LogConfiguration> logs;

        /// <summary>
        /// Construct a new Configuration object.
        /// </summary>
        /// <param name="logs">Enumeration of one or more LogConfiguration objects.</param>
        /// <param name="allowLogging">Specification for enabling/disabling ETW logging.</param>
        public Configuration(IEnumerable<LogConfiguration> logs, AllowEtwLoggingValues allowLogging)
        {
            if (!Enum.IsDefined(typeof(AllowEtwLoggingValues), allowLogging))
            {
                throw new ArgumentOutOfRangeException(nameof(allowLogging), "Invalid value for allowing ETW logging.");
            }

            if (logs == null)
            {
                throw new ArgumentNullException(nameof(logs));
            }

            this.AllowEtwLogging = allowLogging;
            this.logs = new HashSet<LogConfiguration>();
            foreach (var log in logs)
            {
                log.Validate();
                if (log.Type == LogType.MemoryBuffer)
                {
                    // TODO: enable named memory logs in LogManager.
                    throw new InvalidConfigurationException("Memory logs are not currently supported.");
                }
                if (!this.logs.Add(log))
                {
                    throw new InvalidConfigurationException($"Duplicate log {log.Name}, {log.Type} not allowed.");
                }
            }
            if (this.logs.Count == 0)
            {
                throw new ArgumentException("No log configurations provided.", nameof(logs));
            }

            this.ApplyEtwLoggingSettings();
        }

        /// <summary>
        /// Construct a new Configuration object.
        /// </summary>
        /// <param name="logs">Enumeration of one or more LogConfiguration objects.</param>
        public Configuration(IEnumerable<LogConfiguration> logs) : this(logs, AllowEtwLoggingValues.None) { }

        /// <summary>
        /// Construct an empty Configuration object (useful to maintain a single global Configuration state).
        /// </summary>
        internal Configuration()
        {
            this.AllowEtwLogging = AllowEtwLoggingValues.None;
            this.logs = new HashSet<LogConfiguration>();
        }

        /// <summary>
        /// Whether or not to allow ETW logging.
        /// </summary>
        /// <remarks>
        /// The priority of values is: disabled OR enabled &gt; none. When configurations are combined internally the
        /// most recent value will be used.
        /// 
        /// Why do this? Many users in a "single box" scenario aren't ready to deal with the overhead of ETW. ETW creates
        /// files locked to the kernel which, when a process is improperly terminated, just stay open. For many folks not
        /// interested in logging the behavior is incredibly disruptive to their iterative development process and, really,
        /// all they want is to notepad.exe some test logs without extra pain involved in dealing with a binary format.
        /// Additionally, for tests which fail and terminate early it's easier to deal with failures that do not happen
        /// to leave dangling logging sessions.
        /// </remarks>
        public AllowEtwLoggingValues AllowEtwLogging { get; set; }

        /// <summary>
        /// Enumeration of configured logs.
        /// </summary>
        public IEnumerable<LogConfiguration> Logs => this.logs;

        internal void Merge(Configuration other)
        {
            if (other == null)
            {
                return;
            }

            if (other.AllowEtwLogging != AllowEtwLoggingValues.None)
            {
                this.AllowEtwLogging = other.AllowEtwLogging;
            }

            foreach (var otherLog in other.logs)
            {
                var log = this.logs.FirstOrDefault(l => l.Equals(otherLog));
                if (log != null)
                {
                    this.logs.Remove(log);
                    otherLog.Merge(log);
                }
                this.logs.Add(otherLog);
            }

            this.ApplyEtwLoggingSettings();
        }

        public override string ToString()
        {
            var serializer = new JsonSerializer();
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, this);
                return writer.ToString();
            }
        }

        /// <summary>
        /// Clear all existing logs (useful during reloads / shutdown scenarios)
        /// </summary>
        internal void Clear()
        {
            this.logs.Clear();
        }

        private void ApplyEtwLoggingSettings()
        {
            if (this.AllowEtwLogging == AllowEtwLoggingValues.Disabled)
            {
                foreach (var log in this.logs)
                {
                    if (log.Type == LogType.EventTracing)
                    {
                        log.Type = LogType.Text;
                    }
                }
            }
        }

        internal static bool ParseConfiguration(string configurationString, out Configuration configuration)
        {
            bool clean = true; // used to track whether any errors were encountered
            configuration = null;

            if (string.IsNullOrEmpty(configurationString))
            {
                return true; // it's okay to have nothing at all
            }

            configurationString = configurationString.Trim();

            try
            {
                var xDocument = new XmlDocument();
                xDocument.LoadXml(configurationString);
                clean = ParseXmlConfiguration(xDocument, out configuration);
            }
            catch (XmlException)
            {
                InternalLogger.Write.InvalidConfiguration("Configuration was not valid XML.");
                return false;
            }
            catch (InvalidConfigurationException e)
            {
                InternalLogger.Write.InvalidConfiguration(e.Message);
                configuration = null;
                clean = false;
            }

            return clean;
        }

        private sealed class Converter : JsonConverter
        {
            private const string AllowEtwLoggingProperty = "allowEtwLogging";
            private const string LogsProperty = "logs";

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var configuration = value as Configuration;
                if (configuration == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                writer.WriteStartObject();
                if (configuration.AllowEtwLogging != AllowEtwLoggingValues.None)
                {
                    writer.WritePropertyName(AllowEtwLoggingProperty);
                    writer.WriteValue(configuration.AllowEtwLogging == AllowEtwLoggingValues.Enabled);
                }
                writer.WritePropertyName(LogsProperty);
                serializer.Serialize(writer, configuration.Logs);
                writer.WriteEndObject();
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
                                            JsonSerializer serializer)
            {
                var jObject = JObject.Load(reader);
                JToken token;
                var allowEtwLogging = AllowEtwLoggingValues.None;
                if (jObject.TryGetValue(AllowEtwLoggingProperty, StringComparison.OrdinalIgnoreCase, out token))
                {
                    allowEtwLogging = token.Value<bool>()
                                          ? AllowEtwLoggingValues.Enabled
                                          : AllowEtwLoggingValues.Disabled;
                }
                var logsArray = jObject.GetValue(LogsProperty, StringComparison.OrdinalIgnoreCase);
                var logs = serializer.Deserialize<List<LogConfiguration>>(logsArray.CreateReader());

                return new Configuration(logs, allowEtwLogging);
            }

            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(EventProviderSubscription);
            }
        }

        #region XML configuration parsing
        private static bool ParseXmlConfiguration(XmlDocument xDocument, out Configuration configuration)
        {
            var clean = true;
            var allowEtwLogging = AllowEtwLoggingValues.None;
            var logs = new HashSet<LogConfiguration>();

            XmlNode node = xDocument.SelectSingleNode(EtwOverrideXpath);
            if (node != null)
            {
                XmlNode setting = node.Attributes.GetNamedItem(EtwOverrideEnabledAttribute);
                bool isEnabled;
                if (setting == null || !bool.TryParse(setting.Value, out isEnabled))
                {
                    InternalLogger.Write.InvalidConfiguration(EtwOverrideXpath + " tag has invalid " +
                                                              EtwOverrideEnabledAttribute + " attribute");
                    clean = false;
                }
                else
                {
                    allowEtwLogging = isEnabled ? AllowEtwLoggingValues.Enabled : AllowEtwLoggingValues.Disabled;
                }
            }

            foreach (XmlNode log in xDocument.SelectNodes(LogTagXpath))
            {
                string name = null;
                LogType type;
                if (log.Attributes[LogNameAttribute] != null)
                {
                    name = log.Attributes[LogNameAttribute].Value.Trim();
                }

                // If no type is provided we currently default to text.
                if (log.Attributes[LogTypeAttribute] == null)
                {
                    type = LogType.Text;
                }
                else
                {
                    type = log.Attributes[LogTypeAttribute].Value.ToLogType();
                }

                if (type == LogType.None)
                {
                    InternalLogger.Write.InvalidConfiguration("invalid log type " +
                                                              log.Attributes[LogTypeAttribute].Value);
                    clean = false;
                    continue;
                }

                if (type == LogType.Console)
                {
                    if (name != null)
                    {
                        InternalLogger.Write.InvalidConfiguration("console log should not have a name");
                        clean = false;
                    }
                }

                // If a log is listed in duplicates we will discard the previous data entirely. This is a change from historic
                // (pre OSS-release) behavior which was... quasi-intentional shall we say. The author is unaware of anybody
                // using this capability and, since it confusing at best, would like for it to go away.
                try
                {
                    List<EventProviderSubscription> subscriptions;
                    List<string> filters;

                    clean &= ParseLogSources(log, out subscriptions);
                    ParseLogFilters(log, out filters);
                    var config = new LogConfiguration(name, type, subscriptions, filters);

                    clean &= ParseLogNode(log, config);

                    config.Validate();
                    if (!logs.Add(config))
                    {
                        InternalLogger.Write.InvalidConfiguration($"duplicate log {log.Name} discarded.");
                        clean = false;
                    }
                }
                catch (InvalidConfigurationException e)
                {
                    InternalLogger.Write.InvalidConfiguration(e.Message);
                    clean = false;
                }
            }

            configuration = logs.Count > 0 ? new Configuration(logs, allowEtwLogging) : null;
            return clean;
        }

        private static bool ParseLogNode(XmlNode xmlNode, LogConfiguration config)
        {
            var clean = true;
            foreach (XmlAttribute logAttribute in xmlNode.Attributes)
            {
                try
                {
                    switch (logAttribute.Name.ToLower(CultureInfo.InvariantCulture))
                    {
                    case LogBufferSizeAttribute:
                        config.BufferSizeMB = int.Parse(logAttribute.Value);
                        break;
                    case LogDirectoryAttribute:
                        config.Directory = logAttribute.Value;
                        break;
                    case LogFilenameTemplateAttribute:
                        config.FilenameTemplate = logAttribute.Value;
                        break;
                    case LogTimestampLocal:
                        config.TimestampLocal = bool.Parse(logAttribute.Value);
                        break;
                    case LogRotationAttribute:
                        config.RotationInterval = int.Parse(logAttribute.Value);
                        break;
                    case LogHostnameAttribute:
                        config.Hostname = logAttribute.Value;
                        break;
                    case LogPortAttribute:
                        config.Port = ushort.Parse(logAttribute.Value);
                        break;
                    }
                }
                catch (Exception e) when (e is FormatException || e is OverflowException)
                {
                    InternalLogger.Write.InvalidConfiguration($"Attribute {logAttribute.Name} has invalid value {logAttribute.Value} ({e.GetType()}: {e.Message})");
                    clean = false;
                }
            }

            return clean;
        }

        private static bool ParseLogSources(XmlNode xmlNode, out List<EventProviderSubscription> subscriptions)
        {
            var clean = true;
            subscriptions = new List<EventProviderSubscription>();
            foreach (XmlNode source in xmlNode.SelectNodes(SourceTag))
            {
                string sourceName = null;
                Guid sourceProvider = Guid.Empty;
                var level = EventLevel.Informational;
                var keywords = (long)EventKeywords.None;
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
                                           out keywords))
                        {
                            InternalLogger.Write.InvalidConfiguration("invalid keywords value " + sourceAttribute.Value);
                            clean = false;
                        }
                        break;
                    case SourceMinSeverityAttribute:
                        if (!Enum.TryParse(sourceAttribute.Value, true, out level))
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

                if (sourceProvider != Guid.Empty)
                {
                    subscriptions.Add(new EventProviderSubscription(sourceProvider, level, (EventKeywords)keywords));
                }
                else if (!string.IsNullOrEmpty(sourceName))
                {
                    subscriptions.Add(new EventProviderSubscription(sourceName, level, (EventKeywords)keywords));
                }
                else
                {
                    InternalLogger.Write.InvalidConfiguration("source has neither name nor guid");
                    clean = false;
                }
            }

            return clean;
        }

        private static void ParseLogFilters(XmlNode xmlNode, out List<string> regexFilters)
        {
            regexFilters = new List<string>();
            foreach (XmlNode filter in xmlNode.SelectNodes(LogFilterTag))
            {
                regexFilters.Add(filter.InnerText);
            }
        }

        // HEY! HEY YOU! Are you adding stuff here? You're adding stuff, it's cool. Just go update
        // the 'configuration.md' file in doc with what you've added. Santa will bring you bonus gifts.
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
        #endregion
    }
}
