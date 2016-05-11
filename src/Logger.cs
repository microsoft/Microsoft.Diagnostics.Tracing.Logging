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
    using System.Collections;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.Diagnostics.Tracing;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Runtime.Serialization;
    using System.Security;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;

    using Microsoft.Diagnostics.Tracing.Parsers;
    using Microsoft.Diagnostics.Tracing.Session;

    /// <summary>
    /// The common interfaces for an event logger which allow callers to subscribe to and unsubscribe from specific
    /// events, add filters, and get the backing filename.
    /// </summary>
    /// <remarks>
    /// Not all operations are supported by all event loggers, any unsupported operations should result in a
    /// NotSupportedException being thrown.
    /// </remarks>
    public interface IEventLogger
    {
        /// <summary>
        /// Full name of the log file for disk-backed loggers
        /// </summary>
        string Filename { get; set; }

        /// <summary>
        /// Subscribe to events from a particular provider.
        /// </summary>
        /// <param name="subscription">Subscription data.</param>
        void SubscribeToEvents(EventProviderSubscription subscription);

        /// <summary>
        /// Subscribe to events from a collection of event providers.
        /// </summary>
        /// <param name="subscriptions">Collection of subscription data.</param>
        void SubscribeToEvents(ICollection<EventProviderSubscription> subscriptions);

        /// <summary>
        /// Subscribe to events from a particular event provider
        /// </summary>
        /// <param name="source">The event provider to subscribe to</param>
        /// <param name="minimumLevel">The minimum level of event severity to receive events for</param>
        void SubscribeToEvents(EventSource source, EventLevel minimumLevel);

        /// <summary>
        /// Subscribe to events from a particular event provider
        /// </summary>
        /// <param name="source">The event provider to subscribe to</param>
        /// <param name="minimumLevel">The minimum level of event severity to receive events for</param>
        /// <param name="keywords">Keywords (if any) to match against</param>
        void SubscribeToEvents(EventSource source, EventLevel minimumLevel, EventKeywords keywords);

        /// <summary>
        /// Subscribe to events from a particular event provider by Guid
        /// </summary>
        /// <param name="providerId">The Guid of the event provider</param>
        /// <param name="minimumLevel">The minimum level of event severity to receive events for</param>
        void SubscribeToEvents(Guid providerId, EventLevel minimumLevel);

        /// <summary>
        /// Subscribe to events from a particular event provider by Guid
        /// </summary>
        /// <param name="providerId">The Guid of the event provider</param>
        /// <param name="minimumLevel">The minimum level of event severity to receive events for</param>
        /// <param name="keywords">Keywords (if any) to match against</param>
        void SubscribeToEvents(Guid providerId, EventLevel minimumLevel, EventKeywords keywords);

        /// <summary>
        /// Unsubscribe from a specified event provider
        /// </summary>
        /// <param name="source">The source to unsubscribe from</param>
        void UnsubscribeFromEvents(EventSource source);

        /// <summary>
        /// Unsubscribe from a specified event provider by Guid
        /// </summary>
        /// <param name="providerId"></param>
        void UnsubscribeFromEvents(Guid providerId);

        /// <summary>
        /// Add a regular expression filter for formatted output messages
        /// </summary>
        /// <param name="pattern">The pattern to be added</param>
        void AddRegexFilter(string pattern);
    }

    #region Formatters
    [Flags]
    public enum TextLogFormatOptions
    {
        None = 0,

        /// <summary>
        /// Controls whether the thread's Activity ID is shown.
        /// </summary>
        ShowActivityID = 0x1,

        /// <summary>
        /// Emit a full timestamp.
        /// </summary>
        Timestamp = 0x2,

        /// <summary>
        /// Produces an offset from the start of logging instead of a normal timestamp.
        /// </summary>
        TimeOffset = 0x4,

        /// <summary>
        /// Produces process and thread ID data.
        /// </summary>
        ProcessAndThreadData = 0x8,

        /// <summary>
        /// Writes timestamps using the local time instead of UTC.
        /// </summary>
        TimestampInLocalTime = 0x10,

        /// <summary>
        /// The default settings used by <see cref="EventStringFormatter">EventStringFormatter</see>
        /// </summary>
        Default = ShowActivityID | Timestamp | ProcessAndThreadData | TimestampInLocalTime
    }

    /// <summary>
    /// Interface for formatters of events
    /// </summary>
    public interface IEventFormatter
    {
        TextLogFormatOptions Options { get; set; }
        string Format(ETWEvent ev);
    }

    public sealed class EventStringFormatter : IEventFormatter
    {
        /// <summary>
        /// The format to use for timestamps in formatted messages.
        /// </summary>
        /// <remarks>
        /// The current value yeilds ISO 8601-compatible timestamps which sort well.
        /// This could become a configuration knob in the future.
        /// </remarks>
        public const string TimeFormat = "yyyy-MM-ddTHH:mm:ss.ffffff";

        private readonly StringBuilder builder = new StringBuilder(2048);
        private readonly long startTicks;

        public EventStringFormatter()
        {
            this.startTicks = Stopwatch.GetTimestamp();
            this.Options = TextLogFormatOptions.Default;
        }

        /// <summary>
        /// Options to apply when formatting the text
        /// </summary>
        public TextLogFormatOptions Options { get; set; }

        public string Format(ETWEvent ev)
        {
            this.builder.Clear();

            if ((int)(this.Options & TextLogFormatOptions.TimeOffset) != 0)
            {
                var timeDiff = (double)(Stopwatch.GetTimestamp() - this.startTicks);
                timeDiff /= Stopwatch.Frequency;
                this.builder.Append(timeDiff.ToString("F6"));
                this.builder.Append(' ');
            }
            else if ((int)(this.Options & TextLogFormatOptions.Timestamp) != 0)
            {
                if ((int)(this.Options & TextLogFormatOptions.TimestampInLocalTime) != 0)
                {
                    this.builder.Append(ev.Timestamp.ToString(TimeFormat));
                }
                else
                {
                    this.builder.Append(ev.Timestamp.ToUniversalTime().ToString(TimeFormat));
                }
                this.builder.Append(' ');
            }

            if ((int)(this.Options & TextLogFormatOptions.ShowActivityID) != 0 && ev.ActivityID != Guid.Empty)
            {
                this.builder.Append('(');
                this.builder.Append(ev.ActivityID.ToString("N"));
                this.builder.Append(") ");
            }

            this.builder.Append('[');
            if ((int)(this.Options & TextLogFormatOptions.ProcessAndThreadData) != 0)
            {
                this.builder.Append(ev.ProcessID);
                this.builder.Append('/');
                this.builder.Append(ev.ThreadID);
                this.builder.Append('/');
            }
            this.builder.Append(ETWEvent.EventLevelToChar(ev.Level));
            this.builder.Append(':');
            this.builder.Append(ev.ProviderName);
            this.builder.Append(' ');
            this.builder.Append(ev.EventName);
            this.builder.Append(']');

            if (ev.Parameters != null)
            {
                foreach (DictionaryEntry pair in ev.Parameters)
                {
                    var name = pair.Key as string;
                    object o = pair.Value;

                    var s = o as string;
                    var a = o as Array;
                    if (s != null)
                    {
                        // strings can't be trusted, welcome to costlytown.
                        this.builder.Append(' ');
                        this.builder.Append(name);
                        this.builder.Append(@"=""");
                        foreach (var ch in s)
                        {
                            switch (ch)
                            {
                            case '\0':
                                this.builder.Append(@"\0");
                                break;
                            case '\\':
                                this.builder.Append(@"\\");
                                break;
                            case '"':
                                this.builder.Append(@"\""");
                                break;
                            case '\n':
                                this.builder.Append(@"\n");
                                break;
                            case '\r':
                                this.builder.Append(@"\r");
                                break;
                            default:
                                this.builder.Append(ch);
                                break;
                            }
                        }
                        this.builder.Append('\"');
                    }
                    else if (a != null)
                    {
                        // This behavior may be too basic and could be changed in the future. For example
                        // it may be interesting to emit the raw contents of small arrays instead of the type.
                        // This was not done at present because nobody needed it, but anybody should feel free
                        // to make such a change if it is desirable.
                        this.builder.Append(' ');
                        this.builder.Append(name);
                        this.builder.Append('=');
                        this.builder.Append(a.GetType());
                        this.builder.Append('[');
                        this.builder.Append(a.Length);
                        this.builder.Append(']');
                    }
                    else
                    {
                        this.builder.Append(' ');
                        this.builder.Append(name);
                        this.builder.Append('=');
                        this.builder.Append(o);
                    }
                }
            }

            return this.builder.ToString();
        }
    }
    #endregion Formatters

    #region Loggers
    /// <summary>
    /// Base class which handles common work to safely dispatch individual events for all EventListener-based classes.
    /// </summary>
    public abstract class EventListenerDispatcher : EventListener, IEventLogger
    {
        [SuppressMessage("Microsoft.Design", "CA1051:DoNotDeclareVisibleInstanceFields",
            Justification = "Making this property adds no value and does not improve the code quality")]
        protected readonly object WriterLock = new object();

        private volatile bool disabled;

        protected EventListenerDispatcher()
        {
            this.Filters = new List<Regex>();
        }

        /// <summary>
        /// Whether this logger has been disabled (and should stop posting/writing new data)
        /// </summary>
        public bool Disabled
        {
            get { return this.disabled; }
            set
            {
                lock (this.WriterLock)
                {
                    this.disabled = value;
                }
            }
        }

        /// <summary>
        /// Specific activity ID to filter for. Any lines without this ID will be dropped. Set to
        /// <see cref="Guid.Empty">Guid.Empty</see> to disable activity ID filtering.
        /// </summary>
        public Guid FilterActivityID { get; set; }

        /// <summary>
        /// Regular expression filters for output.
        /// </summary>
        protected List<Regex> Filters { get; }

        public void SubscribeToEvents(EventProviderSubscription subscription)
        {
            if (subscription == null)
            {
                throw new ArgumentNullException("subscription");
            }

            if (subscription.Source != null)
            {
                this.SubscribeToEvents(subscription.Source, subscription.MinimumLevel, subscription.Keywords);
            }
            else
            {
                throw new NotSupportedException("Subscription to GUIDs is not supported");
            }
        }

        public void SubscribeToEvents(ICollection<EventProviderSubscription> subscriptions)
        {
            if (subscriptions == null)
            {
                throw new ArgumentNullException("subscriptions");
            }

            foreach (var sub in subscriptions)
            {
                this.SubscribeToEvents(sub);
            }
        }

        public void SubscribeToEvents(EventSource source, EventLevel minimumLevel)
        {
            this.SubscribeToEvents(source, minimumLevel, EventKeywords.None);
        }

        public void SubscribeToEvents(EventSource source, EventLevel minimumLevel, EventKeywords keywords)
        {
            if (source != null)
            {
                this.EnableEvents(source, minimumLevel, keywords);
            }
            else
            {
                throw new ArgumentNullException("source");
            }
        }

        public void SubscribeToEvents(Guid providerId, EventLevel minimumLevel)
        {
            this.SubscribeToEvents(providerId, minimumLevel, EventKeywords.None);
        }

        public void SubscribeToEvents(Guid providerId, EventLevel minimumLevel, EventKeywords keywords)
        {
            throw new NotSupportedException("Subscription to GUIDs is not supported");
        }

        public void UnsubscribeFromEvents(EventSource source)
        {
            this.DisableEvents(source);
        }

        public void UnsubscribeFromEvents(Guid providerId)
        {
            throw new NotSupportedException("Unsubscription from GUIDs is not supported");
        }

        public void AddRegexFilter(string pattern)
        {
            lock (this.WriterLock)
            {
                this.Filters.Add(new Regex(pattern, RegexOptions.IgnoreCase));
            }
        }

        public virtual string Filename
        {
            get { throw new NotSupportedException("This is not a file-backed logger"); }
            set { throw new NotSupportedException("This is not a file-backed logger"); }
        }

        /// <summary>
        /// Constructs an <see cref="ETWEvent"/> object from eventData and calls the overloadable Write method
        /// with the data.
        /// </summary>
        /// <param name="eventData">Event data passed up from the Event Source</param>
        [SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0",
            Justification =
                "eventData being null would be a catastrophic contract break by EventSource. This is not anticipated.")]
        protected sealed override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventId == (int)DynamicTraceEventParser.ManifestEventID)
            {
                return; // we do not want to write this as it's not really helpful for users
            }

            Guid currentActivityId;
            LogManager.GetActivityId(out currentActivityId);
            if (this.FilterActivityID != Guid.Empty && currentActivityId != this.FilterActivityID)
            {
                return;
            }

            LogManager.EventSourceInfo source = LogManager.GetEventSourceInfo(eventData.EventSource);
            LogManager.EventInfo eventInfo = source[eventData.EventId];
            OrderedDictionary payload = null;
            if (eventInfo.Arguments != null)
            {
                payload = new OrderedDictionary(eventInfo.Arguments.Length);
                int argCount = 0;
                foreach (var o in eventData.Payload)
                {
                    payload.Add(eventInfo.Arguments[argCount], o);
                    ++argCount;
                }
            }

            var ev = new ETWEvent(DateTime.Now, eventData.EventSource.Guid, eventData.EventSource.Name,
                                  (ushort)eventData.EventId, eventInfo.Name, eventData.Version, eventData.Keywords,
                                  eventData.Level, eventData.Opcode, currentActivityId, LogManager.ProcessID,
                                  NativeMethods.GetCurrentWin32ThreadId(),
                                  payload);

            lock (this.WriterLock)
            {
                if (!this.disabled)
                {
                    this.Write(ev);
                }
            }
        }

        /// <summary>
        /// Called when an ETWEvent has been constructed (via <see cref="OnEventWritten"/>).
        /// </summary>
        /// <param name="ev"></param>
        public abstract void Write(ETWEvent ev);

        public override void Dispose()
        {
            lock (this.WriterLock)
            {
                base.Dispose();
                this.Dispose(true);
            }
            GC.SuppressFinalize(this);
        }

        protected abstract void Dispose(bool disposing);
    }

    /// <summary>
    /// Base class which handles common work for all EventListener-based classes.
    /// </summary>
    /// <remarks>
    /// This class provides text serialization for EventSource events and is also responsible for processing new
    /// EventSources as they are seen and extracting some useful data from their manifests. This data assists in
    /// mapping EventId values to more meaningful names.
    /// </remarks>
    public abstract class BaseTextLogger : EventListenerDispatcher
    {
        private readonly EventStringFormatter formatter = new EventStringFormatter();

        /// <summary>
        /// The TextWriter object used to emit logged data
        /// </summary>
        protected TextWriter Writer { get; set; }

        /// <summary>
        /// Expose formatting options to the consumer.
        /// </summary>
        public TextLogFormatOptions FormatOptions
        {
            get { return this.formatter.Options; }
            set { this.formatter.Options = value; }
        }

        public sealed override void Write(ETWEvent ev)
        {
            if (this.Writer == null)
            {
                return;
            }

            var output = ev.ToString(this.formatter);
            if (this.Filters.Count > 0)
            {
                var matched = false;
                foreach (var filter in this.Filters)
                {
                    if (filter.IsMatch(output))
                    {
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    return;
                }
            }

            if (this.Writer != null)
            {
                try
                {
                    this.Writer.WriteLine(output);
                    this.Writer.Flush(); // flush after every line -- performance? meh
                }
                catch (ObjectDisposedException) { } // finalizer may come nuke the TextWriter in some edge cases.
            }
        }

        [SuppressMessage("Microsoft.Design", "CA1063:ImplementIDisposableCorrectly",
            Justification = "This code ends up cleaner than copying the Dispose() method to inheritors"),
         SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times",
             Justification =
                 "EventListener does not provide a protected Dispose(bool) method to correctly implement the pattern")]
        public sealed override void Dispose()
        {
            lock (this.WriterLock)
            {
                base.Dispose();
                this.Dispose(true);
                this.Writer = null;
            }
            GC.SuppressFinalize(this);
        }
    }

    internal sealed class ConsoleLogger : BaseTextLogger
    {
        public ConsoleLogger()
        {
            this.Writer = Console.Out;
            InternalLogger.Write.LoggerDestinationOpened(this.GetType().ToString(), LogManager.ConsoleLoggerName);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.Writer.Dispose();
                InternalLogger.Write.LoggerDestinationClosed(this.GetType().ToString(), LogManager.ConsoleLoggerName);
            }
        }
    }

    /// <summary>
    /// The MemoryLogger stores written events in an in-memory buffer which callers may retrieve and inspect.
    /// </summary>
    /// <remarks>
    /// The data in the memory stream is encoded in UTF8 with no BOM.
    /// </remarks>
    public sealed class MemoryLogger : BaseTextLogger
    {
        /// <summary>
        /// Construct a new in-memory logger using a provided stream.
        /// </summary>
        /// <param name="stream">The memory stream to write into.</param>
        public MemoryLogger(MemoryStream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
            this.Stream = stream;
            this.Writer = new StreamWriter(this.Stream, new UTF8Encoding(false, false));
            InternalLogger.Write.LoggerDestinationOpened(this.GetType().ToString(), ":memory:");
        }

        /// <summary>
        /// Construct a new in-memory logger using a newly created stream of the default size with no bound.
        /// </summary>
        public MemoryLogger()
            : this(new MemoryStream(LogManager.InitialMemoryStreamSize)) { }

        /// <summary>
        /// Retrieve the attached MemoryStream object being used by the logger.
        /// </summary>
        /// <remarks>
        /// The stream will continue updating unless you also set the <see cref="BaseTextLogger.Disabled">Disabled</see>
        /// property to true.
        /// </remarks>
        public MemoryStream Stream { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing && this.Stream != null)
            {
                this.Writer.Dispose(); // calls Dispose on the owned stream for us.
                this.Stream = null;
                InternalLogger.Write.LoggerDestinationClosed(this.GetType().ToString(), ":memory:");
            }
        }
    }

    /// <summary>
    /// Base class for loggers that write to files (ETW or text)
    /// </summary>
    internal sealed class FileBackedLogger : IDisposable
    {
        /// <summary>
        /// The filename extension used for text logs.
        /// </summary>
        public const string TextLogExtension = ".log";

        /// <summary>
        /// The filename extension used for ETW logs.
        /// </summary>
        public const string ETLExtension = ".etl";

        private readonly string baseFilename;
        private readonly string directoryName;
        private readonly string fileExtension;
        private readonly LogType logType;
        private readonly TimeSpan maximumAge;
        private readonly long maximumSize;
        private readonly SortedList<DateTime, LogInfo> existingFiles = new SortedList<DateTime, LogInfo>();

        private string currentFilename;
        private long currentSizeBytes;
        private DateTime oldestFileTimestamp = DateTime.MaxValue;
        private DateTime newestFileTimestamp = DateTime.MinValue;
        private DateTime intervalEnd;
        private DateTime intervalStart;

        /// <summary>
        /// Constructs a manager for a file-backed logger.
        /// </summary>
        /// <param name="configuration">Configuration for the logger.</param>
        /// <param name="utcStartTime">Start time for log files (UTC).</param>
        /// <remarks>
        /// Callers are expected to call CheckedRotate() periodically to cause file rotation to occur as desired.
        /// If rotationInterval is set to 0 the file will not be rotated and the filename will not contain timestamps.
        /// </remarks>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "We hold the logger internally and so do not want to dispose it")]
        public FileBackedLogger(LogConfiguration configuration, DateTime utcStartTime)
        {
            this.directoryName = Path.GetFullPath(configuration.Directory);
            if (!Directory.Exists(this.directoryName))
            {
                Directory.CreateDirectory(this.directoryName); // allowed to throw, caller should handle it
            }

            this.baseFilename = configuration.Name;
            this.directoryName = configuration.Directory;
            this.logType = configuration.Type;
            this.RotationInterval = configuration.RotationInterval;
            this.TimestampLocal = configuration.TimestampLocal;
            this.maximumAge = configuration.MaximumAge;
            this.maximumSize = configuration.MaximumSize;

            var now = this.AdjustUtcTime(utcStartTime);

            switch (this.logType)
            {
            case LogType.Text:
                this.fileExtension = TextLogExtension;
                this.FilenameTemplate = configuration.FilenameTemplate + this.fileExtension;
                this.UpdateCurrentFilename(now);
                var textFileLogger = new TextFileLogger(this.currentFilename, configuration.BufferSizeMB);
                if (!this.TimestampLocal)
                {
                    textFileLogger.FormatOptions &= ~TextLogFormatOptions.TimestampInLocalTime;
                }
                this.Logger = textFileLogger;
                break;
            case LogType.EventTracing:
                this.fileExtension = ETLExtension;
                this.FilenameTemplate = configuration.FilenameTemplate + this.fileExtension;
                this.UpdateCurrentFilename(now);
                this.Logger = new ETLFileLogger(this.baseFilename, this.currentFilename, configuration.BufferSizeMB);
                break;
            default:
                throw new ArgumentException($"log type {this.logType} not implemented", nameof(configuration));
            }

            this.SetRetentionData();

            InternalLogger.Write.CreateFileDestination(this.baseFilename, this.directoryName, this.RotationInterval,
                                                       configuration.FilenameTemplate,
                                                       (long)this.maximumAge.TotalSeconds, this.maximumSize);
        }

        // we don't want users to be able to tweak file/dir names, only filtering.
        public IEventLogger Logger { get; private set; }

        public int RotationInterval { get; }

        /// <summary>
        /// Whether we will defer to local time when setting a filename during rotation
        /// </summary>
        public bool TimestampLocal { get; }

        /// <summary>
        /// The template in use for filename rotation.
        /// </summary>
        public string FilenameTemplate { get; }

        public void Dispose()
        {
            this.Dispose(true);
        }

        /// <summary>
        /// Check to see whether a file rotation is due and rotate the file if necessary.
        /// </summary>
        /// <param name="now">The current UTC time (<see cref="DateTime.UtcNow"/>)</param>
        public void CheckedRotate(DateTime now)
        {
            now = this.AdjustUtcTime(now);

            if (this.RotationInterval > 0 && this.intervalEnd.Ticks <= now.Ticks)
            {
                this.Rotate(now);
            }
        }

        /// <summary>
        /// Immediately rotate/rename the file with the provided timestamp.
        /// </summary>
        /// <param name="now">The current UTC time (<see cref="DateTime.UtcNow"/>)</param>
        public void Rotate(DateTime now)
        {
            now = this.AdjustUtcTime(now);

            var previousFilename = this.currentFilename;
            this.UpdateCurrentFilename(now);
            this.Logger.Filename = this.currentFilename;
            this.AddExistingFile(previousFilename);
        }

        /// <summary>
        /// Ensure a filename template formats correctly and generates a valid filename
        /// </summary>
        /// <param name="template">The template to validate</param>
        /// <param name="rotationInterval">The interval at which rotation will occur (if any)</param>
        /// <returns>true if the template is valid, false otherwise</returns>
        public static bool IsValidFilenameTemplate(string template, TimeSpan rotationInterval)
        {
            const string baseName = "hamilton"; // do not throw away your shot
            const string someMachine = "ORTHANC";
            const long jenny = 8675309;

            if (!template.Contains("{0}"))
            {
                return false; // base filename MUST be represented without any goop in the formatting.
            }

            try
            {
                var generatedName = CreateFilename(template, baseName, DateTime.MinValue, DateTime.MaxValue, someMachine, jenny);
                if (generatedName.IndexOfAny(Path.GetInvalidFileNameChars()) != -1)
                 {
                    return false;
                 }

                if (rotationInterval != TimeSpan.Zero)
                 {
                    // changing the time by the desired must change the filename.
                    var rotatedName = CreateFilename(template, baseName, DateTime.MinValue + rotationInterval,
                                                     DateTime.MaxValue, someMachine, jenny);
                    if (generatedName.Equals(rotatedName))
                    {
                        return false;
                    }
                 }
            }
            catch (FormatException)
            {
                return false;
            }
            return true;
        }

        /// <summary>^M
        /// Determine if a given template is valid when combined with log retention rules.^M
        /// </summary>^M
        /// <param name="template">template to examine.</param>^M
        /// <returns>True if the template is valid for retention management.</returns>
        public static bool IsFilenameTemplateValidForRetention(string template)
        {
            const string baseName = "washington";
            if (!template.StartsWith("{0}"))
            {
                return false;
            }

            // extra parentheses seem to be required by the parser, even though they appear redundant. Leave 'em.
            if ((CreateFilename(template, baseName, DateTime.MinValue, DateTime.MaxValue, string.Empty, 0).Length) !=
                (CreateFilename(template, baseName, DateTime.MinValue, DateTime.MaxValue, baseName, 0).Length))
            {
                return false;
            }

            if ((CreateFilename(template, baseName, DateTime.MinValue, DateTime.MaxValue, string.Empty, 0).Length) !=
                (CreateFilename(template, baseName, DateTime.MinValue, DateTime.MaxValue, string.Empty, 42).Length))
            {
                return false;
            }

            var variableDates = new[]
                                {
                                    new DateTime(1, 1, 1, 0, 0, 0),
                                    new DateTime(2001, 1, 1, 0, 0, 0),
                                    new DateTime(2001, 10, 1, 0, 0, 0),
                                    new DateTime(2001, 10, 10, 0, 0, 0),
                                    new DateTime(2001, 10, 10, 10, 0, 0),
                                    new DateTime(2001, 10, 10, 10, 10, 0),
                                    new DateTime(2001, 10, 10, 10, 10, 10),
                                    new DateTime(2001, 10, 10, 10, 10, 10, 10),
                                };

            var len = CreateFilename(template, baseName, variableDates[0], DateTime.MaxValue, string.Empty, 0).Length;
            if (len != CreateFilename(template, baseName, DateTime.MinValue, variableDates[0], string.Empty, 0).Length)
            {
                return false;
            }

            for (var d = 1; d < variableDates.Length; ++d)
            {
                if (   len != CreateFilename(template, baseName, variableDates[d], DateTime.MaxValue, string.Empty, 0).Length
                    || len != CreateFilename(template, baseName, DateTime.MinValue, variableDates[d], string.Empty, 0).Length)
                {
                    return false;
                }
            }

            return true;
        }

        private struct LogInfo
        {
            public string Filename;
            public long SizeBytes;
        }

        private bool HasRetentionPolicy => this.RotationInterval != 0 && (this.maximumSize != 0 || this.maximumAge > TimeSpan.Zero);

        /// <summary>
        /// Adjust the given 'now' value from UTC to local time if we need that.
        /// </summary>
        /// <param name="utcNow">The current time (from <see cref="DateTime.UtcNow"/>)</param>
        /// <returns>The adjusted time value.</returns>
        private DateTime AdjustUtcTime(DateTime utcNow)
        {
            return this.TimestampLocal ? utcNow.ToLocalTime() : utcNow;
        }

        /// <summary>
        /// Conditionally update the current filename
        /// </summary>
        /// <param name="now">The current time</param>
        /// <returns>true if the filename required updating, false otherwise</returns>
        private void UpdateCurrentFilename(DateTime now)
        {
            string newFilename;
            if (this.RotationInterval <= 0)
            {
                this.intervalStart = this.intervalEnd = DateTime.MinValue;
                newFilename = this.baseFilename + this.fileExtension;
            }
            else
            {
                // calculate start / end times which we will use for the filename
                long startTicks = now.Ticks - (now.Ticks % (this.RotationInterval * TimeSpan.TicksPerSecond));
                long endTicks = startTicks + (this.RotationInterval * TimeSpan.TicksPerSecond);

                this.intervalStart = new DateTime(now.Ticks);
                this.intervalEnd = new DateTime(endTicks);
                newFilename = CreateFilename(this.FilenameTemplate, this.baseFilename, this.intervalStart,
                                             this.intervalEnd, Environment.MachineName, MillisecondsSinceMidnight(now));
            }

            string newFileName = Path.Combine(this.directoryName, newFilename);
            this.currentFilename = newFileName;
            InternalLogger.Write.UpdateFileRotationTimes(this.baseFilename, this.intervalStart.Ticks,
                                                         this.intervalEnd.Ticks);
        }

        private static string CreateFilename(string template, string baseFilename, DateTime start, DateTime end,
                                             string machineName, long millisecondsSinceMidnight)
        {
            return string.Format(template, baseFilename, start, end, machineName, millisecondsSinceMidnight);
        }

        /// <summary>
        /// Turn a timestamp into a "sequence number"-esque value for granular indication of log starts.
        /// </summary>
        /// <param name="now">The current time.</param>
        /// <returns>The number of milliseconds since midnight.</returns>
        /// <remarks>
        /// This exists for a specific consumer group in Bing and its use is not recommended.
        /// </remarks>
        private static long MillisecondsSinceMidnight(DateTime now)
        {
            long sequence = ((now.Hour * 60) + now.Minute) * 60000;
            sequence += (now.Second * 1000) + now.Millisecond;
            return sequence;
        }

        private void SetRetentionData()
        {
            if (!this.HasRetentionPolicy)
            {
                return;
            }

            var someFilename = CreateFilename(this.FilenameTemplate, this.baseFilename, DateTime.MinValue,
                                              DateTime.MaxValue, string.Empty, 0);
            var patternLength =
                someFilename.Substring(this.baseFilename.Length,
                                       someFilename.Length - this.baseFilename.Length - this.fileExtension.Length)
                            .Length;
            var pattern = this.baseFilename + new string('?', patternLength) + this.fileExtension;

            foreach (var f in Directory.GetFiles(this.directoryName, pattern, SearchOption.TopDirectoryOnly))
            {
                this.AddExistingFile(f);
            }
        }

        private void AddExistingFile(string fullFilename)
        {
            if (!this.HasRetentionPolicy)
            {
                return;
            }

            FileInfo fileInfo;
            try
            {
                fileInfo = new FileInfo(fullFilename);
            }
            catch (Exception e) when (e is SecurityException || e is UnauthorizedAccessException || e is PathTooLongException)
            {
                InternalLogger.Write.UnableToCatalogLogFile(fullFilename, e.GetType().ToString(), e.Message);
                return;
            }

            if (!fileInfo.Exists)
            {
                InternalLogger.Write.UnableToCatalogLogFile(fullFilename, string.Empty, "file does not exist.");
                return;
            }

            this.existingFiles.Add(fileInfo.CreationTimeUtc, new LogInfo { Filename = fullFilename, SizeBytes = fileInfo.Length});
            this.currentSizeBytes += fileInfo.Length;
            if (fileInfo.CreationTimeUtc > this.newestFileTimestamp)
            {
                this.newestFileTimestamp = fileInfo.CreationTimeUtc;
            }
            if (fileInfo.CreationTimeUtc < this.oldestFileTimestamp)
            {
                this.oldestFileTimestamp = fileInfo.CreationTimeUtc;
            }

            while (   (this.maximumSize != 0 && this.currentSizeBytes > this.maximumSize)
                   || (this.maximumAge != TimeSpan.Zero && this.newestFileTimestamp - this.oldestFileTimestamp > this.maximumAge))
            {
                // We don't want to expire the only file we currently know about. This can actually violate the user's expectations
                // although the user may reasonable expect that as well that we leave at least one log behind besides what we
                // presume to be our currently open log.
                if (this.existingFiles.Count == 1)
                {
                    break;
                }

                var info = this.existingFiles.Values[0];
                try
                {
                    File.Delete(info.Filename);
                }
                catch (Exception e)
                    when (
                        e is FileNotFoundException || e is DirectoryNotFoundException ||
                        e is UnauthorizedAccessException || e is IOException)
                {
                    // It could be argued that 'FileNotFound' is not worth logging as a warning (somebody probably cleaned this up for us?)
                    // but I'm erring on the side of verbosity, and users may wish to know if files are disappearing through other means
                    // when we don't expect that.
                    InternalLogger.Write.UnableToDeleteExpiredLogFile(info.Filename, e.GetType().ToString(), e.Message);
                }

                this.existingFiles.RemoveAt(0);
                this.currentSizeBytes -= info.SizeBytes;
                this.oldestFileTimestamp = this.existingFiles.Keys[0];
            }
        }

        private void Dispose(bool disposing)
        {
            if (disposing && this.Logger != null)
            {
                (this.Logger as IDisposable).Dispose();
                this.Logger = null;
            }
        }
    }

    internal sealed class TextFileLogger : BaseTextLogger
    {
        private readonly int outputBufferSize;
        private FileStream outputFile;

        public TextFileLogger(string filename, int bufferSizeMB)
        {
            this.outputBufferSize = bufferSizeMB * 1024 * 1024;
            this.Open(filename);
        }

        public override string Filename
        {
            get
            {
                lock (this.WriterLock)
                {
                    return this.outputFile.Name;
                }
            }
            set
            {
                lock (this.WriterLock)
                {
                    this.Open(value);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && this.outputFile != null)
            {
                this.Close();
            }
        }

        private void Open(string filename)
        {
            if (this.outputFile != null
                && string.Compare(this.outputFile.Name, filename, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return;
            }

            this.Close();
            this.outputFile = new FileStream(filename, FileMode.Append, FileAccess.Write, FileShare.Read,
                                             this.outputBufferSize);
            this.Writer = new StreamWriter(this.outputFile, new UTF8Encoding(false, false));
            InternalLogger.Write.LoggerDestinationOpened(this.GetType().ToString(), filename);
        }

        private void Close()
        {
            if (this.outputFile != null)
            {
                this.Writer.Flush(); // We must flush to get the actual length
                string filename = this.outputFile.Name;

                this.Writer.Dispose();
                this.Writer = null;

                this.outputFile.Close();
                this.outputFile = null;

                var fileInfo = new FileInfo(filename);
                long size = fileInfo.Exists ? fileInfo.Length : 0;
                if (size == 0)
                {
                    try
                    {
                        File.Delete(filename);
                        InternalLogger.Write.RemovedEmptyFile(filename);
                    }
                    // ignore these for now.
                    catch (Exception e)
                        when (
                            e is FileNotFoundException || e is DirectoryNotFoundException ||
                            e is UnauthorizedAccessException || e is IOException) { }
                }

                InternalLogger.Write.LoggerDestinationClosed(this.GetType().ToString(), filename);
            }
        }
    }

    internal sealed class ETLFileLogger : IEventLogger, IDisposable
    {
        internal const string SessionPrefix = "Microsoft.Diagnostics.Tracing.Logging.";
        private const int MaxRenameTries = 3;
        private const int RenameRetryWaitMS = 500;
        private const int MaxWaitForSessionChange = 5000; // in ms
        private readonly TraceEventSession session;
        private bool hasSubscription;

        public ETLFileLogger(string sessionName, string filename, int bufferSizeMB)
        {
            string fullSessionName = SessionPrefix + sessionName;
            this.session = null;

            // In the event of catastrophe (abnormal process termination) we may have a "dangling" session. In order
            // to establish a new session we must first close the previous session. These circumstances are expected
            // to be extremely rare and extremely unlikely to occur at any time other than process startup.
            if (TraceEventSession.GetActiveSession(fullSessionName) != null)
            {
                CloseDuplicateTraceSession(fullSessionName);
            }

            this.session = new TraceEventSession(fullSessionName, filename)
                           {
                               BufferSizeMB = bufferSizeMB,
                               BufferQuantumKB = GetIndividualBufferSizeKB(bufferSizeMB),
                               StopOnDispose = true,
                               CaptureStateOnSetFileName = true
                           };

            InternalLogger.Write.LoggerDestinationOpened(this.GetType().ToString(), filename);
        }

        public void Dispose()
        {
            string filename = this.session.FileName;
            this.session.Dispose();
            InternalLogger.Write.LoggerDestinationClosed(this.GetType().ToString(), filename);
        }

        public void SubscribeToEvents(EventProviderSubscription subscription)
        {
            if (subscription == null)
            {
                throw new ArgumentNullException("subscription");
            }

            if (subscription.Source != null)
            {
                this.SubscribeToEvents(subscription.Source.Guid, subscription.MinimumLevel, subscription.Keywords);
            }
            else
            {
                this.SubscribeToEvents(subscription.ProviderID, subscription.MinimumLevel, subscription.Keywords);
            }
        }

        public void SubscribeToEvents(ICollection<EventProviderSubscription> subscriptions)
        {
            if (subscriptions == null)
            {
                throw new ArgumentNullException("subscriptions");
            }

            // There is a fun Windows feature where, if you want to use mixed-mode kernel and userland ETW sessions,
            // you MUST subscribe to the kernel first.
            var kernelSub = subscriptions.FirstOrDefault(sub => sub.ProviderID == KernelTraceEventParser.ProviderGuid);
            if (kernelSub != null)
            {
                this.SubscribeToEvents(kernelSub.ProviderID, kernelSub.MinimumLevel, kernelSub.Keywords);
            }
            foreach (var sub in subscriptions)
            {
                if (kernelSub != null && sub == kernelSub)
                {
                    continue;
                }

                this.SubscribeToEvents(sub);
            }
        }

        public void SubscribeToEvents(EventSource source, EventLevel minimumLevel)
        {
            this.SubscribeToEvents(source, minimumLevel, EventKeywords.None);
        }

        public void SubscribeToEvents(EventSource source, EventLevel minimumLevel, EventKeywords keywords)
        {
            this.SubscribeToEvents(source.Guid, minimumLevel, keywords);
        }

        public void SubscribeToEvents(Guid providerId, EventLevel minimumLevel)
        {
            this.SubscribeToEvents(providerId, minimumLevel, EventKeywords.None);
        }

        public void SubscribeToEvents(Guid providerId, EventLevel minimumLevel, EventKeywords keywords)
        {
            try
            {
                if (providerId == KernelTraceEventParser.ProviderGuid)
                {
                    // No support for stack capture flags. Consider adding in future.
                    this.session.EnableKernelProvider((KernelTraceEventParser.Keywords)keywords);
                }
                else
                {
                    this.session.EnableProvider(providerId, EventLevelToTraceEventLevel(minimumLevel), (ulong)keywords);
                }
                if (!this.hasSubscription && !WaitForSessionChange(this.session.SessionName, true))
                {
                    throw new OperationCanceledException("Could not open session in time");
                }
                this.hasSubscription = true;
            }
            catch (Exception e)
            {
                if (!this.hasSubscription)
                {
                    InternalLogger.Write.UnableToOpenTraceSession(this.session.SessionName, e.GetType().ToString(),
                                                                  e.Message);
                }
                throw;
            }
        }

        public void UnsubscribeFromEvents(EventSource source)
        {
            // user needs to restart the trace (aka reconstruct this object)
            throw new NotSupportedException("Unsubscribing is not supported");
        }

        public void UnsubscribeFromEvents(Guid providerId)
        {
            throw new NotSupportedException("Unsubscribing is not supported");
        }

        public void AddRegexFilter(string pattern)
        {
            throw new NotSupportedException("Cannot use regex filters for binary traces");
        }

        public string Filename
        {
            get { return this.session.FileName; }
            set
            {
                if (string.Compare(this.session.FileName, value, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    string oldFilename = this.session.FileName;
                    for (var i = 0; i < MaxRenameTries; ++i)
                    {
                        try
                        {
                            this.session.SetFileName(value);
                            // Write 'close' event after setting the session's filename since that is what triggers actually
                            // closing.
                            InternalLogger.Write.LoggerDestinationClosed(this.GetType().ToString(), oldFilename);
                            InternalLogger.Write.LoggerDestinationOpened(this.GetType().ToString(), value);
                            return;
                        }
                        catch (FileLoadException e)
                        {
                            // This is thrown when the current file is in use and we cannot rename.
                            InternalLogger.Write.UnableToChangeTraceSessionFilename(this.session.SessionName, value,
                                                                                    e.GetType().ToString(), e.Message);
                            Thread.Sleep(RenameRetryWaitMS);
                        }
                        catch (Exception e)
                        {
                            InternalLogger.Write.UnableToChangeTraceSessionFilename(this.session.SessionName, value,
                                                                                    e.GetType().ToString(), e.Message);
                            throw;
                        }
                    }

                    throw new OperationCanceledException(
                        string.Format("Unable to rename file from {0} to {1}", oldFilename, value));
                }
            }
        }

        public static void CloseDuplicateTraceSession(string sessionName)
        {
            InternalLogger.Write.ConflictingTraceSessionFound(sessionName);

            TraceEventSession s = null;
            try
            {
                // we can't control this session so we need to stop it
                s = new TraceEventSession(sessionName); // might throw if it's in the midst of being shut down
                s.Stop();
            }
            catch (FileNotFoundException)
            {
                // well, okay, then it's probably gone now.
            }
            finally
            {
                if (s != null)
                {
                    s.Dispose();
                }
            }

            // Now we enter a brief waiting period to make sure it dies. We must do this because ControlTrace()
            // (the underlying win32 API) is asynchronous and our request to terminate the session may take
            // a small amount of time to complete.
            if (!WaitForSessionChange(sessionName, false))
            {
                InternalLogger.Write.ConflictingTraceSessionStuck(sessionName);
                throw new OperationCanceledException("could not tear down existing trace session");
            }
        }

        // Maps the total requested buffer size to the sizes we'll use for individual ETW buffers. The mentality here
        // is that requests for particularly large overall buffers indicate a need for overall higher throughput, in
        // those cases Windows performs best for both read and write operations if the buffers are larger.
        // Individual buffer sizes are not exposed to the end user because the other types of logs have no analogue,
        // and we can derive the user intent from the overall buffer size they request.
        // Mapping:
        // Below 4MB - 64KB buffers
        // Below 8MB - 128KB buffers
        // Below 16MB - 256KB buffers
        // Below 32MB - 512KB buffers
        // 32MB and up - 1024KB buffers
        private static int GetIndividualBufferSizeKB(int totalBufferSizeMB)
        {
            const int minBufferSizeMB = 2;
            const int maxIndividualBufferSizeKB = 1024;

            totalBufferSizeMB = Math.Max(minBufferSizeMB, totalBufferSizeMB);

            return Math.Min(maxIndividualBufferSizeKB, (totalBufferSizeMB >> 1) * 64);
        }

        private static TraceEventLevel EventLevelToTraceEventLevel(EventLevel level)
        {
            switch (level)
            {
            case EventLevel.Critical:
                return TraceEventLevel.Critical;
            case EventLevel.Error:
                return TraceEventLevel.Error;
            case EventLevel.Informational:
                return TraceEventLevel.Informational;
            case EventLevel.LogAlways:
                return TraceEventLevel.Always;
            case EventLevel.Verbose:
                return TraceEventLevel.Verbose;
            case EventLevel.Warning:
                return TraceEventLevel.Warning;

            default:
                throw new ArgumentException("level had unexpected value", "level");
            }
        }

        /// <summary>
        /// Wait for a pre-determined amount of time for the state of a session to change.
        /// </summary>
        /// <param name="sessionName">Name of the session.</param>
        /// <param name="open">Whether the session should *end* in the open or closed state.</param>
        /// <returns>True if the state changed successfully within the alotted time.</returns>
        private static bool WaitForSessionChange(string sessionName, bool open)
        {
            int slept = 0;
            TraceEventSession session = TraceEventSession.GetActiveSession(sessionName);

            while ((open ? session == null : session != null) && slept < MaxWaitForSessionChange)
            {
                const int sleepFor = MaxWaitForSessionChange / 10;
                Thread.Sleep(sleepFor);
                slept += sleepFor;
                session = TraceEventSession.GetActiveSession(sessionName);
            }

            return (open ? session != null : session == null);
        }
    }

    /// <summary>
    /// The NetworkLogger sends the events to a network endpoint which watchers may retrieve and inspect.
    /// </summary>
    public sealed class NetworkLogger : EventListenerDispatcher
    {
        private readonly MemoryStream serializationBuffer = new MemoryStream();
        private readonly DataContractSerializer serializer = new DataContractSerializer(typeof(ETWEvent));

        private readonly Uri serverUri;

        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="hostname">Hostname without scheme</param>
        /// <param name="port">Port</param>
        public NetworkLogger(string hostname, int port)
        {
            if (Uri.CheckHostName(hostname) == UriHostNameType.Unknown)
            {
                throw new ArgumentException("hostname");
            }

            if (port <= 0 || port > 65535)
            {
                throw new ArgumentException("port");
            }

            this.serverUri = new Uri(string.Format("http://{0}:{1}", hostname, port));
            InternalLogger.Write.LoggerDestinationOpened(this.GetType().ToString(), this.serverUri.ToString());
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                InternalLogger.Write.LoggerDestinationClosed(this.GetType().ToString(), this.serverUri.ToString());
            }
        }

        public override void Write(ETWEvent ev)
        {
            try
            {
                var request = WebRequest.Create(this.serverUri);
                request.Method = WebRequestMethods.Http.Post;
                request.ContentType = "text/xml";

                // We are guaranteed safe access to our serializer and its buffer, but we have to dupe the bytes off
                // before using async web request. It's possible we could be smarter about this with a distinct
                // serializer per request and use of its begin/end write code. Not trying for now.
                this.serializationBuffer.Position = 0;
                this.serializationBuffer.SetLength(0);
                this.serializer.WriteObject(this.serializationBuffer, ev);

                var postData = new byte[this.serializationBuffer.Length];
                Array.Copy(this.serializationBuffer.GetBuffer(), postData, postData.Length);
                request.ContentLength = postData.Length;

                request.BeginGetRequestStream(GetPostStreamCallback,
                                              new RequestState {WebRequest = request, PostData = postData});
            }
            catch (WebException) { } // Skip all of these since we don't currently care if the remote endpoint is down.
            catch (InvalidOperationException) { }
            catch (IOException) { }
        }

        /// <summary>
        /// Callback of BeginGetRequestStream (for post data). This will start off an async write to it.
        /// </summary>
        private static void GetPostStreamCallback(IAsyncResult ar)
        {
            try
            {
                var requestState = (RequestState)ar.AsyncState;
                WebRequest request = requestState.WebRequest;
                byte[] postData = requestState.PostData;

                using (Stream requestStream = request.EndGetRequestStream(ar))
                {
                    requestStream.Write(postData, 0, postData.Length);
                    requestStream.Close();
                }

                request.BeginGetResponse(GetResponseCallback, request);
            }
            catch (WebException) { }
            catch (InvalidOperationException) { }
            catch (IOException) { }
        }

        /// <summary>
        /// Callback of BeginGetResponse. Does not do anything with response.
        /// </summary>
        private static void GetResponseCallback(IAsyncResult asynchronousResult)
        {
            try
            {
                var request = (WebRequest)asynchronousResult.AsyncState;
                using (var response = (HttpWebResponse)request.EndGetResponse(asynchronousResult))
                {
                    response.Close();
                }
            }
            catch (WebException) { } // Skip all of these since we don't currently care if the remote endpoint is down.
            catch (InvalidOperationException) { }
            catch (IOException) { }
        }

        private class RequestState
        {
            public byte[] PostData;
            public WebRequest WebRequest;
        }
    }
    #endregion Loggers
}
