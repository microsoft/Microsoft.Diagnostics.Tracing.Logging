// The MIT License (MIT)
// 
// Copyright (c) 2015-2016 Microsoft
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

    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    /// <summary>
    /// Different varieties of log type.
    /// </summary>
    public enum LogType
    {
        /// <summary>
        /// No specified type (do not use).
        /// </summary>
        None,

        /// <summary>
        /// Console output log.
        /// </summary>
        Console,

        /// <summary>
        /// Memory buffer log.
        /// </summary>
        MemoryBuffer,

        /// <summary>
        /// Text log.
        /// </summary>
        Text,

        /// <summary>
        /// ETW log.
        /// </summary>
        EventTracing,

        /// <summary>
        /// Network (http) based log.
        /// </summary>
        Network
    }

    internal static class LogTypeExtensions
    {
        public static string ToConfigurationString(this LogType type)
        {
            switch (type)
            {
            case LogType.Console:
                return "console";
            case LogType.MemoryBuffer:
                return "memory";
            case LogType.Text:
                return "text";
            case LogType.EventTracing:
                return "etw";
            case LogType.Network:
                return "network";
            default:
                throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        public static LogType ToLogType(this string type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            switch (type.ToLower(CultureInfo.InvariantCulture))
            {
            case "con":
            case "cons":
            case "console":
                return LogType.Console;
            case "mem":
            case "memory":
                return LogType.MemoryBuffer;
            case "text":
            case "txt":
                return LogType.Text;
            case "etw":
            case "etl":
                return LogType.EventTracing;
            case "net":
            case "network":
                return LogType.Network;
            default:
                return LogType.None;
            }
        }

        public static bool HasFeature(this LogType logType, LogConfiguration.Features flags)
        {
            LogConfiguration.Features caps;
            switch (logType)
            {
            case LogType.Console:
            case LogType.MemoryBuffer:
            case LogType.Network:
                caps = (LogConfiguration.Features.EventSourceSubscription | LogConfiguration.Features.Unsubscription |
                        LogConfiguration.Features.RegexFilter);
                break;
            case LogType.Text:
                caps = (LogConfiguration.Features.EventSourceSubscription | LogConfiguration.Features.Unsubscription |
                        LogConfiguration.Features.FileBacked | LogConfiguration.Features.RegexFilter);
                break;
            case LogType.EventTracing:
                caps = (LogConfiguration.Features.EventSourceSubscription | LogConfiguration.Features.GuidSubscription |
                        LogConfiguration.Features.FileBacked);
                break;
            default:
                throw new InvalidOperationException($"features for type {logType} are unknowable");
            }

            return ((caps & flags) != 0);
        }
    }

    /// <summary>
    /// A small holder for the parsed out logging configuration of a single log
    /// </summary>
    [JsonObject(MemberSerialization = MemberSerialization.OptIn), JsonConverter(typeof(Converter))]
    public sealed class LogConfiguration
    {
        private readonly HashSet<string> filters;
        private readonly HashSet<EventProviderSubscription> subscriptions;
        private int bufferSizeMB;
        private string directory;
        private string filenameTemplate;
        private string hostname;
        private IEventLogger logger;
        private ushort port;
        private int rotationInterval;
        private bool? timestampLocal;
        private LogType type;

        /// <summary>
        /// Construct a new LogConfiguration object.
        /// </summary>
        /// <param name="name">Name of the log.</param>
        /// <param name="logType">Type of the log.</param>
        /// <param name="subscriptions">One or more subscriptions to use.</param>
        /// <param name="regexfilters">Zero or more regular expression filters to use for messages.</param>
        public LogConfiguration(string name, LogType logType, IEnumerable<EventProviderSubscription> subscriptions,
                                IEnumerable<string> regexfilters)
        {
            if (logType == LogType.None || !Enum.IsDefined(typeof(LogType), logType))
            {
                throw new InvalidConfigurationException($"Log type {logType} is invalid.");
            }
            this.Type = logType;

            switch (logType)
            {
            case LogType.Console:
            case LogType.MemoryBuffer:
                break;
            default:
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidConfigurationException("Log name must be specified.");
                }
                if (this.Type.HasFeature(Features.FileBacked) && name.IndexOfAny(Path.GetInvalidFileNameChars()) != -1)
                {
                    throw new InvalidConfigurationException($"base name {name} of log is invalid.");
                }
                break;
            }

            // We use a special name for the console logger that is invalid for file loggers so we can track
            // it along with them.
            this.Name = logType == LogType.Console ? LogManager.ConsoleLoggerName : name;

            this.subscriptions = new HashSet<EventProviderSubscription>();
            if (subscriptions != null)
            {
                foreach (var sub in subscriptions)
                {
                    if (!this.AddSubscription(sub))
                    {
                        throw new InvalidConfigurationException("Duplicate subscriptions may not be provided.");
                    }
                }
            }
            if (this.subscriptions.Count == 0)
            {
                throw new InvalidConfigurationException("Logs may not be created with zero subscriptions.");
            }

            this.filters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (regexfilters != null)
            {
                foreach (var filter in regexfilters)
                {
                    if (!this.AddFilter(filter))
                    {
                        throw new InvalidConfigurationException("Duplicate regex filters may not be provided.");
                    }
                }
            }
        }

        /// <summary>
        /// Construct a new LogConfiguration object.
        /// </summary>
        /// <param name="name">Name of the log.</param>
        /// <param name="logType">Type of the log.</param>
        /// <param name="subscriptions">One or more subscriptions to use.</param>
        public LogConfiguration(string name, LogType logType, IEnumerable<EventProviderSubscription> subscriptions)
            : this(name, logType, subscriptions, new string[] {}) { }

        /// <summary>
        /// The name of the log.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The type of the log.
        /// </summary>
        public LogType Type
        {
            get { return this.type; }
            set
            {
                this.CheckPropertyChange();
                this.type = value;
            }
        }

        /// <summary>
        /// The size of buffer to use (in megabytes) while logging.
        /// </summary>
        public int BufferSizeMB
        {
            get { return this.bufferSizeMB > 0 ? this.bufferSizeMB : LogManager.DefaultLogBufferSizeMB; }
            set
            {
                this.CheckPropertyChange();
                if (!LogManager.IsValidFileBufferSize(value))
                {
                    throw new InvalidConfigurationException($"Buffer size {value} is outside of acceptable range.");
                }

                this.bufferSizeMB = value;
            }
        }

        /// <summary>
        /// Directory to emit logs to. When set the directory may be relative (in which case it will be qualified using the LogManager).
        /// </summary>
        public string Directory
        {
            get { return this.directory ?? LogManager.DefaultDirectory; }
            set
            {
                this.CheckPropertyChange();
                if (!this.Type.HasFeature(Features.FileBacked))
                {
                    throw new InvalidConfigurationException("Directories are not valid for non-file loggers.");
                }
                try
                {
                    this.directory = LogManager.GetQualifiedDirectory(value);
                }
                catch (ArgumentException e)
                {
                    throw new InvalidConfigurationException($"Directory {value} is not valid.", e);
                }
            }
        }

        /// <summary>
        /// Template to use when rotating files.
        /// </summary>
        public string FilenameTemplate
        {
            get
            {
                if (this.filenameTemplate == null)
                {
                    return (this.TimestampLocal
                                ? LogManager.DefaultLocalTimeFilenameTemplate
                                : LogManager.DefaultFilenameTemplate);
                }

                return this.filenameTemplate;
            }
            set
            {
                this.CheckPropertyChange();
                if (!this.Type.HasFeature(Features.FileBacked))
                {
                    throw new InvalidConfigurationException("Filename templates are not valid for non-file loggers.");
                }
                if (!FileBackedLogger.IsValidFilenameTemplate(value))
                {
                    throw new InvalidConfigurationException($"Filename template '{value}' is invalid.");
                }

                this.filenameTemplate = value;
            }
        }

        /// <summary>
        /// The interval in seconds to perform rotation on file loggers. Must not be set for non-file loggers.
        /// </summary>
        public int RotationInterval
        {
            get { return this.rotationInterval; }
            set
            {
                this.CheckPropertyChange();
                if (!this.Type.HasFeature(Features.FileBacked))
                {
                    throw new InvalidConfigurationException("Rotation intervals are not valid for non-file loggers.");
                }
                if (value < 0)
                {
                    value = LogManager.DefaultRotate ? LogManager.DefaultRotationInterval : 0;
                }
                try
                {
                    if (value > 0)
                    {
                        LogManager.CheckRotationInterval(value);
                    }
                }
                catch (ArgumentException e)
                {
                    throw new InvalidConfigurationException($"Rotation interval {value} is invalid.", e);
                }

                this.rotationInterval = value;
            }
        }

        /// <summary>
        /// Whether to provide local timestamps in filenames and log output (where applicable).
        /// </summary>
        public bool TimestampLocal
        {
            get { return this.timestampLocal ?? false; }
            set
            {
                this.CheckPropertyChange();
                this.timestampLocal = value;
            }
        }

        /// <summary>
        /// Hostname to write log messages to (for network-based loggers).
        /// </summary>
        public string Hostname
        {
            get { return this.hostname; }
            set
            {
                this.CheckPropertyChange();
                if (this.Type != LogType.Network)
                {
                    throw new InvalidConfigurationException("Hostnames are not valid for non-network loggers.");
                }
                if (Uri.CheckHostName(value) == UriHostNameType.Unknown)
                {
                    InternalLogger.Write.InvalidConfiguration($"invalid hostname '{value}'");
                }

                this.hostname = value;
            }
        }

        /// <summary>
        /// Port to connect to when writing log messages (for network-based loggers).
        /// </summary>
        public ushort Port
        {
            get { return this.port; }
            set
            {
                this.CheckPropertyChange();
                if (this.Type != LogType.Network)
                {
                    throw new InvalidConfigurationException("Ports are not valid for non-network loggers.");
                }
                if (value == 0)
                {
                    throw new InvalidConfigurationException($"Port {value} is invalid.");
                }

                this.port = value;
            }
        }

        /// <summary>
        /// Regular expression filters for the log.
        /// </summary>
        public IEnumerable<string> Filters => this.filters;

        /// <summary>
        /// Subscriptions for the log.
        /// </summary>
        public IEnumerable<EventProviderSubscription> Subscriptions => this.subscriptions;

        /// <summary>
        /// Whether the configuration is complete and usable.
        /// </summary>
        public bool IsValid
        {
            get
            {
                // XXX: not hugely in love with this design, it is very specific right now to 'Network' type and
                // isn't true RAII.
                switch (this.type)
                {
                case LogType.Network:
                    return (this.hostname != null && this.port > 0);
                default:
                    return true;
                }
            }
        }

        internal IEventLogger Logger
        {
            get { return this.logger; }
            set
            {
                this.CheckPropertyChange();

                this.logger = value;
                foreach (var f in this.Filters)
                {
                    this.logger.AddRegexFilter(f);
                }

                // Build a collection of all desired subscriptions so that we can subscribe in bulk at the end.
                // We do this because ordering may matter to specific types of loggers and they are best suited to
                // manage that internally.
                var supportedSubscriptions = new List<EventProviderSubscription>();
                foreach (var sub in this.subscriptions)
                {
                    if (!sub.IsResolved)
                    {
                        continue;
                    }
                    if (sub.Source != null ||
                        (this.Type.HasFeature(Features.GuidSubscription) && sub.ProviderID != Guid.Empty))
                    {
                        supportedSubscriptions.Add(sub);
                    }
                }

                this.logger.SubscribeToEvents(supportedSubscriptions);
            }
        }

        public override bool Equals(object obj)
        {
            var other = obj as LogConfiguration;
            return other != null && this.Equals(other);
        }

        private bool Equals(LogConfiguration other)
        {
            return string.Equals(this.Name, other.Name, StringComparison.OrdinalIgnoreCase);
        }

        public override int GetHashCode()
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(this.Name);
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

        private bool AddSubscription(EventProviderSubscription subscription)
        {
            if (subscription.Name == null && subscription.ProviderID == Guid.Empty)
            {
                throw new InvalidConfigurationException("Provided subscription missing name and GUID values");
            }

            var newSubscription = true;
            if (this.subscriptions.Contains(subscription))
            {
                newSubscription = false;
                var currentSubscription = this.subscriptions.First(s => s.Equals(subscription));
                currentSubscription.MinimumLevel =
                    (EventLevel)Math.Max((int)currentSubscription.MinimumLevel, (int)subscription.MinimumLevel);
                currentSubscription.Keywords |= subscription.Keywords;
            }
            else
            {
                this.subscriptions.Add(subscription);
            }

            return newSubscription;
        }

        private bool AddFilter(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                throw new InvalidConfigurationException("empty/invalid filter value.");
            }

            if (!this.Type.HasFeature(Features.RegexFilter))
            {
                throw new InvalidConfigurationException(
                    $"Log type {this.Type} does not support regular expression filters.");
            }

            filter = filter.Trim();

            return this.filters.Add(filter);
        }

        private void CheckPropertyChange()
        {
            if (this.logger != null)
            {
                throw new InvalidOperationException("May not modify configuration after it has been applied to a log.");
            }
        }

        /// <summary>
        /// Update the underlying logger for an EventSource object that has been introduced within the process.
        /// </summary>
        /// <param name="eventSource">The new EventSource to apply configuration for.</param>
        internal void UpdateForEventSource(EventSource eventSource)
        {
            // We need to update our logger any time a config shows up where they had a dependency
            // that probably wasn't resolved. This will be the case either when it was a named subscription
            // or it was a GUID subscription on a type that can't directly subscribe to GUIDs (i.e. not an ETW
            // trace session)
            var subs = from s in this.subscriptions
                       where !s.IsResolved &&
                             ((!this.Type.HasFeature(Features.GuidSubscription) && s.ProviderID == eventSource.Guid) ||
                              string.Equals(s.Name, eventSource.Name, StringComparison.OrdinalIgnoreCase))
                       select s;
            foreach (var subscription in subs)
            {
                subscription.UpdateSource(eventSource);
                if (this.Type.HasFeature(Features.EventSourceSubscription))
                {
                    this.logger.SubscribeToEvents(eventSource, subscription.MinimumLevel, subscription.Keywords);
                }
                else if (this.Type.HasFeature(Features.GuidSubscription))
                {
                    this.logger.SubscribeToEvents(eventSource.Guid, subscription.MinimumLevel, subscription.Keywords);
                }
            }
        }

        internal void Merge(LogConfiguration otherLog)
        {
            foreach (var sub in otherLog.subscriptions)
            {
                this.AddSubscription(sub);
            }
            foreach (var filter in otherLog.Filters)
            {
                this.AddFilter(filter);
            }
        }

        /// <summary>
        /// The set of capabilities an event log provides.
        /// </summary>
        [Flags]
        internal enum Features
        {
            None = 0x0,
            EventSourceSubscription = 0x1,
            GuidSubscription = 0x2,
            Unsubscription = 0x4,
            FileBacked = 0x8,
            RegexFilter = 0x10
        }

        private sealed class Converter : JsonConverter
        {
            private const string NameProperty = "name";
            private const string TypeProperty = "type";
            private const string SourcesProperty = "sources";
            private const string FiltersProperty = "filters";
            private const string BufferSizeMBProperty = "bufferSizeMB";
            private const string DirectoryProperty = "directory";
            private const string FilenameTemplateProperty = "filenameTemplate";
            private const string TimestampLocalProperty = "timestampLocal";
            private const string RotationIntervalProperty = "rotationInterval";
            private const string HostnameProperty = "hostname";
            private const string PortProperty = "port";

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var log = value as LogConfiguration;
                if (log == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                writer.WriteStartObject();
                writer.WritePropertyName(NameProperty);
                writer.WriteValue(log.Name);
                writer.WritePropertyName(TypeProperty);
                writer.WriteValue(log.Type.ToConfigurationString());

                if (log.bufferSizeMB > 0)
                {
                    writer.WritePropertyName(BufferSizeMBProperty);
                    writer.WriteValue(log.BufferSizeMB);
                }
                if (log.directory != null)
                {
                    writer.WritePropertyName(DirectoryProperty);
                    writer.WriteValue(log.Directory);
                }
                if (log.filenameTemplate != null)
                {
                    writer.WritePropertyName(FilenameTemplateProperty);
                    writer.WriteValue(log.filenameTemplate);
                }
                if (log.timestampLocal.HasValue)
                {
                    writer.WritePropertyName(TimestampLocalProperty);
                    writer.WriteValue(log.timestampLocal);
                }
                if (log.rotationInterval > 0)
                {
                    writer.WritePropertyName(RotationIntervalProperty);
                    writer.WriteValue(log.rotationInterval);
                }
                if (log.hostname != null)
                {
                    writer.WritePropertyName(HostnameProperty);
                    writer.WriteValue(log.hostname);
                }
                if (log.port != 0)
                {
                    writer.WritePropertyName(PortProperty);
                    writer.WriteValue(log.port);
                }

                writer.WritePropertyName(SourcesProperty);
                serializer.Serialize(writer, log.subscriptions);

                writer.WritePropertyName(FiltersProperty);
                serializer.Serialize(writer, log.filters);

                writer.WriteEndObject();
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
                                            JsonSerializer serializer)
            {
                var jObject = JObject.Load(reader);
                JToken token;
                string name = null;
                LogType type = LogType.None;
                if (jObject.TryGetValue(NameProperty, StringComparison.OrdinalIgnoreCase, out token))
                {
                    name = token.Value<string>();
                }
                if (jObject.TryGetValue(TypeProperty, StringComparison.OrdinalIgnoreCase, out token))
                {
                    type = token.Value<string>().ToLogType();
                }

                var sourcesArray = jObject.GetValue(SourcesProperty, StringComparison.OrdinalIgnoreCase);
                var sources = serializer.Deserialize<List<EventProviderSubscription>>(sourcesArray.CreateReader());

                var filtersArray = jObject.GetValue(FiltersProperty, StringComparison.OrdinalIgnoreCase);
                var filters = filtersArray != null && filtersArray.HasValues
                                  ? serializer.Deserialize<List<string>>(filtersArray.CreateReader())
                                  : new List<string>();

                var log = new LogConfiguration(name, type, sources, filters);

                if (jObject.TryGetValue(BufferSizeMBProperty, StringComparison.OrdinalIgnoreCase, out token))
                {
                    log.BufferSizeMB = token.Value<int>();
                }
                if (jObject.TryGetValue(DirectoryProperty, StringComparison.OrdinalIgnoreCase, out token))
                {
                    log.Directory = token.Value<string>();
                }
                if (jObject.TryGetValue(FilenameTemplateProperty, StringComparison.OrdinalIgnoreCase, out token))
                {
                    log.FilenameTemplate = token.Value<string>();
                }
                if (jObject.TryGetValue(TimestampLocalProperty, StringComparison.OrdinalIgnoreCase, out token))
                {
                    log.TimestampLocal = token.Value<bool>();
                }
                if (jObject.TryGetValue(RotationIntervalProperty, StringComparison.OrdinalIgnoreCase, out token))
                {
                    log.RotationInterval = token.Value<int>();
                }
                if (jObject.TryGetValue(HostnameProperty, StringComparison.OrdinalIgnoreCase, out token))
                {
                    log.Hostname = token.Value<string>();
                }
                if (jObject.TryGetValue(PortProperty, StringComparison.OrdinalIgnoreCase, out token))
                {
                    log.Port = token.Value<ushort>();
                }

                return log;
            }

            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(LogConfiguration);
            }
        }
    }
}