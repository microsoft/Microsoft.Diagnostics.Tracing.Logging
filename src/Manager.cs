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
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.Diagnostics.Tracing;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Threading;

    public sealed partial class LogManager
    {
        private string configurationFile;
        private long configurationFileLastWrite;
        internal int configurationFileReloadCount; // primarily a test hook.
        private FileSystemWatcher configurationFileWatcher;
        private Configuration fileConfiguration;
        private Configuration processConfiguration;

        /// <summary>
        /// Provide string-based configuration which will be applied additively after any file configuration.
        /// </summary>
        /// <remarks>
        /// Any change will force a full configuration reload. This function is not thread-safe.
        /// Using the string variant of this function will overwrite data provided using the <see cref="Configuration" />
        /// variant of the method.
        /// </remarks>
        /// <param name="configurationString">A string containing the JSON or XML configuration.</param>
        /// <returns>True if the configuration was successfully applied.</returns>
        [Obsolete("Provide a Configuration object instead.")]
        public static bool SetConfiguration(string configurationString)
        {
            Configuration newConfiguration;
            if (!Configuration.ParseConfiguration(configurationString, out newConfiguration))
            {
                return false;
            }

            singleton.processConfiguration = newConfiguration;
            singleton.ApplyConfiguration();
            return true;
        }

        /// <summary>
        /// Provide configuration which will be applied additively after any file configuration.
        /// </summary>
        /// <remarks>
        /// Any change will force a full configuration reload. This function is not thread-safe.
        /// </remarks>
        /// <param name="configuration">Data to use for additive configuration (may be null to remove existing data).</param>
        /// <returns>True if the configuration was successfully applied.</returns>
        public static void SetConfiguration(Configuration configuration)
        {
            singleton.processConfiguration = configuration;
            singleton.ApplyConfiguration();
        }

        /// <summary>
        /// Check if a configuration string is valid.
        /// </summary>
        /// <param name="configurationString">A string containing the JSON or XML configuration.</param>
        /// <returns>true if the configuration is valid, false otherwise.</returns>
        internal static bool IsConfigurationValid(string configurationString)
        {
            Configuration unused;
            return Configuration.ParseConfiguration(configurationString, out unused);
        }

        /// <summary>
        /// Assign a file to read configuration from. If the file is invalid existing configuration from a previous file (if any)
        /// will be removed.
        /// </summary>
        /// <param name="filename">The file to read configuration from (or null to remove use of the file).</param>
        /// <returns>true if the file was valid, false otherwise.</returns>
        public static bool SetConfigurationFile(string filename)
        {
            return singleton.UpdateConfigurationFile(filename);
        }

        private void ApplyConfiguration()
        {
            lock (this.loggersLock)
            {
                Configuration.Clear();
                Configuration.Merge(this.fileConfiguration);
                Configuration.Merge(this.processConfiguration);

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

                foreach (var logConfig in Configuration.Logs)
                {
                    CreateLogger(logConfig);
                }
            }
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
                this.configurationFileWatcher =
                    new FileSystemWatcher
                    {
                        Path = Path.GetDirectoryName(this.configurationFile),
                        Filter = Path.GetFileName(this.configurationFile),
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
                    };
                this.configurationFileWatcher.Changed += this.OnConfigurationFileChanged;
                this.configurationFileWatcher.EnableRaisingEvents = true;
                return ReadConfigurationFile(this.configurationFile);
            }

            singleton.fileConfiguration = null;
            singleton.ApplyConfiguration();
            return false;
        }

        private static bool ReadConfigurationFile(string filename)
        {
            Stream file = null;
            var success = false;
            try
            {
                file = new FileStream(filename, FileMode.Open, FileAccess.Read);
                using (var reader = new StreamReader(file))
                {
                    file = null;
                    Configuration configuration;
                    success = Configuration.ParseConfiguration(reader.ReadToEnd(), out configuration);
                    if (success)
                    {
                        singleton.fileConfiguration = configuration;
                        singleton.ApplyConfiguration();
                    }
                }
            }
            catch (IOException e)
            {
                InternalLogger.Write.InvalidConfiguration($"Could not open configuration: {e.GetType()} {e.Message}");
            }
            finally
            {
                file?.Dispose();
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
    }

    /// <summary>
    /// Handles configuration and management of logging facilities.
    /// </summary>
    /// <remarks>
    /// Most methods and properties are explicitly not thread-safe. It is an error to attempt to interface with the
    /// manager from multiple threads concurrently.
    /// </remarks>
    public sealed partial class LogManager : EventListener
    {
        /// <summary>
        /// Minimum rotationInterval for file rotation. Rotation at a faster rotationInterval is not allowed.
        /// </summary>
        public const int MinRotationInterval = 60; // The rotation timer currently expects this to be exactly 60.

        /// <summary>
        /// Maximum rotationInterval for file rotation. Rotation at a slower rotationInterval is not allowed.
        /// </summary>
        public const int MaxRotationInterval = 86400;

        /// <summary>
        /// Minimum size of a file buffer.
        /// </summary>
        public const int MinLogBufferSizeMB = 1;

        /// <summary>
        /// Maximum size of a file buffer.
        /// </summary>
        public const int MaxLogBufferSizeMB = 128;

        /// <summary>
        /// The default buffer size for log files.
        /// </summary>
        public const int DefaultLogBufferSizeMB = 2;

        /// <summary>The default filename template for rotated log files.</summary>
        /// <remarks>
        /// This yields a string like "foo_20110623T154000Z--T155000Z"
        /// This bit of goop is intended to be ISO 8601 compliant and sorts very nicely.
        /// </remarks>
        public const string DefaultFilenameTemplate = "{0}_{1:yyyyMMdd}T{1:HHmmss}Z--T{2:HHmmss}Z";

        /// <summary>The default filename template for rotated log files when using local timestamps.</summary>
        /// <remarks>
        /// This yields a string like "foo_20110623T154000-08--T155000-08". Note the zone offsets which help deal
        /// with timezone changes. HOWEVER, this template assumes a timezone with ONLY hour-based offsets and this
        /// code would not be suitable for use in areas where timezone offsets cross over into minutes (e.g. Tibet,
        /// India, etc).
        /// </remarks>
        public const string DefaultLocalTimeFilenameTemplate = "{0}_{1:yyyyMMdd}T{1:HHmmsszz}--T{2:HHmmsszz}";

        internal const string DataDirectoryEnvironmentVariable = "DATADIR";

        /// <summary>
        /// Minimum seconds between rotations (used to prevent over-aggressive requests to the public rotator.)
        /// </summary>
        internal const int MinDemandRotationDelta = 5;

        // The name of the console logger (used to distinguish it from other file targets by using invalid file chars.)
        internal const string ConsoleLoggerName = ":console:";

        // 64kB memory streams for memory loggers. This number is totally arbitrary.
        public const int InitialMemoryStreamSize = 64 * 1024;

        /// <summary>
        /// Current configuration for the log manager.
        /// </summary>
        public static readonly Configuration Configuration = new Configuration();

        /// <summary>
        /// Default subscriptioms for console and loggers created via the legacy (obsoleted) methods.
        /// </summary>
        public static readonly EventProviderSubscription[] DefaultSubscriptions =
        {
            new EventProviderSubscription(InternalLogger.Write, EventLevel.Critical)
        };

        /// <summary>
        /// Singleton entry to insure only one manager exists at any given time.
        /// </summary>
        internal static LogManager singleton;

        internal static int ProcessID = Process.GetCurrentProcess().Id;

        internal readonly Dictionary<string, FileBackedLogger> fileLoggers =
            new Dictionary<string, FileBackedLogger>(StringComparer.OrdinalIgnoreCase);

        private readonly object loggersLock = new object();

        internal readonly Dictionary<string, NetworkLogger> networkLoggers =
            new Dictionary<string, NetworkLogger>(StringComparer.OrdinalIgnoreCase);

        private ConsoleLogger consoleLogger;

        private int defaultRotationInterval = 600; // 10m
        private DateTime lastRotation = DateTime.MinValue;
        private Timer rotationTimer;

        private LogManager()
        {
            // Add any currently known EventSources to our catalog.
            foreach (var src in EventSource.GetSources())
            {
                this.OnEventSourceCreated(src);
            }

            this.CreateConsoleLogger(new LogConfiguration(null, LogType.Console, DefaultSubscriptions));
            if (Configuration.AllowEtwLogging == Configuration.AllowEtwLoggingValues.None)
            {
                Configuration.AllowEtwLogging = IsCurrentProcessElevated()
                                                    ? Configuration.AllowEtwLoggingValues.Enabled
                                                    : Configuration.AllowEtwLoggingValues.Disabled;
            }

            // Determine default directory
            const string DefaultRootPath = "logs";
            string dataDir = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
            if (dataDir != null && Path.IsPathRooted(dataDir))
            {
                DefaultDirectory = Path.Combine(dataDir, DefaultRootPath);
            }
            else
            {
                DefaultDirectory = Path.Combine(Path.GetFullPath("."), DefaultRootPath);
            }

            var now = DateTime.UtcNow;
            this.rotationTimer = new Timer(this.RotateCallback, null,
                                           ((MinRotationInterval - now.Second) * 1000) - now.Millisecond,
                                           Timeout.Infinite);
        }

        /// <summary>
        /// Where log files are written to by default.
        /// </summary>
        public static string DefaultDirectory { get; private set; }

        /// <summary>
        /// Whether to enable file rotation by default.
        /// </summary>
        public static bool DefaultRotate { get; set; }

        /// <summary>
        /// The default rotation rotationInterval for files.
        /// </summary>
        public static int DefaultRotationInterval
        {
            get { return singleton.defaultRotationInterval; }
            set
            {
                CheckRotationInterval(value);
                singleton.defaultRotationInterval = value;
            }
        }

        /// <summary>
        /// Retrieve the console log.
        /// </summary>
        [Obsolete("Use the GetLogger method to retrieve the console logger.")]
        public static IEventLogger ConsoleLogger => singleton?.consoleLogger;

        /// <summary>
        /// Start the logging system.
        /// </summary>
        /// <remarks>
        /// This function is not thread-safe.
        /// </remarks>
        public static void Start()
        {
            if (singleton == null)
            {
                // it's never possible to get this message inside the same process (which should be fine)
                InternalLogger.Write.Startup();

                singleton = new LogManager();

                InternalLogger.Write.DefaultLogDirectory(DefaultDirectory);
            }
        }

        /// <summary>
        /// Shut down the logging system.
        /// </summary>
        public static void Shutdown()
        {
            singleton?.Dispose();
            Configuration.Clear();
            Configuration.AllowEtwLogging = Configuration.AllowEtwLoggingValues.None;
        }

        /// <summary>
        /// Retrieve the named file logger.
        /// </summary>
        /// <param name="name">Name of the logger.</param>
        /// <returns>The logger, or null if no logger of the given name exists.</returns>
        [Obsolete("Use GetLogger method.")]
        public static IEventLogger GetFileLogger(string name)
        {
            // Historically we would get either/or. Emulate that here.
            return GetLogger(name, LogType.EventTracing) ?? GetLogger(name, LogType.Text);
        }

        /// <summary>
        /// Retrieve a logger of the explicitly provided type.
        /// </summary>
        /// <typeparam name="T">Desired logger type.</typeparam>
        /// <param name="name">Name of the logger.</param>
        /// <returns>The logger or null if it does not exist.</returns>
        public static T GetLogger<T>(string name) where T : class, IEventLogger
        {
            if (typeof(T) == typeof(ConsoleLogger))
            {
                return singleton?.consoleLogger as T;
            }
            if (typeof(T) == typeof(NetworkLogger))
            {
                return singleton.GetNamedNetworkLogger(name) as T;
            }
            if (typeof(T) == typeof(ETLFileLogger) || typeof(T) == typeof(TextFileLogger))
            {
                return singleton.GetNamedFileLogger(name)?.Logger as T;
            }

            return null;
        }

        /// <summary>
        /// Retrieve a logger of the given type. If ETW logging is disabled (directly or through configuration) both ETW and text
        /// logs will be looked up.
        /// </summary>
        /// <param name="name">Name of the logger.</param>
        /// <param name="logType">Type of the logger. Only the console, file-type, and network loggers can be retrieved.</param>
        /// <returns>The retrieved logger or null if it does not exist.</returns>
        public static IEventLogger GetLogger(string name, LogType logType)
        {
            switch (logType)
            {
            case LogType.Console:
                return singleton?.consoleLogger;
            case LogType.Network:
                return singleton.GetNamedNetworkLogger(name);
            case LogType.EventTracing:
                var logger = singleton.GetNamedFileLogger(name);
                if (Configuration.AllowEtwLogging != Configuration.AllowEtwLoggingValues.Disabled &&
                    !(logger?.Logger is ETLFileLogger))
                {
                    return null;
                }
                return logger?.Logger;
            case LogType.Text:
                return GetLogger<TextFileLogger>(name);
            default:
                throw new ArgumentException($"Cannot retrieve logs of type {logType}", nameof(logType));
            }
        }

        /// <summary>
        /// Creates a new logger from the provided configuration.
        /// </summary>
        /// <typeparam name="T">Desired logger type.</typeparam>
        /// <param name="configuration">Log configuration to use.</param>
        /// <returns>The newly created logger.</returns>
        public static T CreateLogger<T>(LogConfiguration configuration) where T : class, IEventLogger
        {
            IEventLogger logger;
            switch (configuration.Type)
            {
            case LogType.Console:
                singleton.CreateConsoleLogger(configuration);
                logger = singleton.consoleLogger;
                break;
            case LogType.MemoryBuffer:
                logger = new MemoryLogger(new MemoryStream(InitialMemoryStreamSize));
                break;
            case LogType.EventTracing:
            case LogType.Text:
                logger = singleton.CreateFileLogger(configuration);
                break;
            case LogType.Network:
                logger = singleton.CreateNetworkLogger(configuration);
                break;
            default:
                throw new ArgumentException($"Unknown log type {configuration.Type}", nameof(configuration));
            }

            configuration.Logger = logger;
            return logger as T;
        }

        /// <summary>
        /// Creates a new logger from the provided configuration.
        /// </summary>
        /// <typeparam name="T">Desired logger type.</typeparam>
        /// <param name="configuration">Log configuration to use.</param>
        /// <returns>The newly created logger.</returns>
        public static IEventLogger CreateLogger(LogConfiguration configuration)
        {
            return CreateLogger<IEventLogger>(configuration);
        }

        /// <summary>
        /// Create a new text logger.
        /// </summary>
        /// <param name="name">Base name of the file (without extension).</param>
        /// <param name="directory">Directory to store log file(s) in, null means use default.</param>
        /// <param name="bufferSizeMB">Size of memory buffer for this file.</param>
        /// <param name="rotation">File rotation interval. 0 means no rotation, &lt; 0 means use default.</param>
        /// <param name="filenameTemplate">Template for the filename (only applies if rotation is on.)</param>
        /// <param name="fileTimestampLocal">Whether to use local time for the file timestamp (if it uses rotation.</param>
        /// <returns>An interface to the logger which was created.</returns>
        [SuppressMessage("Microsoft.Design", "CA1026:DefaultParametersShouldNotBeUsed",
            Justification = "Adding overloads for all permutations would needlessly complicate this code")]
        [Obsolete("Use CreateLogger method.")]
        public static IEventLogger CreateTextLogger(string name, string directory = null,
                                                    int bufferSizeMB = DefaultLogBufferSizeMB, int rotation = -1,
                                                    string filenameTemplate = DefaultFilenameTemplate,
                                                    bool fileTimestampLocal = false)
        {
            var config = new LogConfiguration(name, LogType.Text, DefaultSubscriptions)
                         {
                             Directory = directory ?? DefaultDirectory,
                             BufferSizeMB = bufferSizeMB,
                             RotationInterval = rotation,
                             FilenameTemplate = filenameTemplate,
                             TimestampLocal = fileTimestampLocal
                         };
            return singleton.CreateFileLogger(config);
        }

        /// <summary>
        /// Create a new ETW (binary) logger.
        /// </summary>
        /// <param name="name">Base name of the file (without extension).</param>
        /// <param name="directory">Directory to store log file(s) in, null means use default.</param>
        /// <param name="bufferSizeMB">Size of an individual ETW buffer for the log session (note: many buffers are typically used).</param>
        /// <param name="rotation">File rotation interval. 0 means no rotation, &lt; 0 means use default.</param>
        /// <param name="filenameTemplate">Template for the filename (only applies if rotation is on.)</param>
        /// <param name="fileTimestampLocal">Whether to use local time for the file timestamp (if it uses rotation).</param>
        /// <returns>An interface to the logger which was created.</returns>
        /// <remarks>
        /// This call is not impacted by the value of <see cref="AllowEtwLogging"/>. It is assumed that callers have
        /// a good reason to always want ETW loggers.
        /// </remarks>
        [SuppressMessage("Microsoft.Design", "CA1026:DefaultParametersShouldNotBeUsed",
            Justification = "Adding overloads for all permutations would needlessly complicate this code")]
        [Obsolete("Use CreateLogger method.")]
        public static IEventLogger CreateETWLogger(string name, string directory = null,
                                                   int bufferSizeMB = DefaultLogBufferSizeMB, int rotation = -1,
                                                   string filenameTemplate = DefaultFilenameTemplate,
                                                   bool fileTimestampLocal = false)
        {
            var config = new LogConfiguration(name, LogType.EventTracing, DefaultSubscriptions)
                         {
                             Directory = directory ?? DefaultDirectory,
                             BufferSizeMB = bufferSizeMB,
                             RotationInterval = rotation,
                             FilenameTemplate = filenameTemplate,
                             TimestampLocal = fileTimestampLocal
                         };
            return singleton.CreateFileLogger(config);
        }

        /// <summary>
        /// Create a new memory logger.
        /// </summary>
        /// <returns>The memory logger.</returns>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Willing to allow MemoryLogger unhandled exceptions to result in somewhat bad behavior.")]
        [Obsolete("Use either CreateLogger or manually instantiate a MemoryLogger object.")]
        public static MemoryLogger CreateMemoryLogger()
        {
            return new MemoryLogger(new MemoryStream(InitialMemoryStreamSize));
        }

        /// <summary>
        /// Create a new memory logger with a pre-allocated memory stream.
        /// </summary>
        /// <param name="stream">The MemoryStream to use.</param>
        /// <returns>The memory logger.</returns>
        [Obsolete("Manually instantiate a MemoryLogger object instead.")]
        public static MemoryLogger CreateMemoryLogger(MemoryStream stream)
        {
            return new MemoryLogger(stream);
        }

        /// <summary>
        /// Create a new network logger.
        /// </summary>
        /// <param name="baseName">Name of the logger.</param>
        /// <param name="hostname">Hostname to send events to.</param>
        /// <param name="port">Port to send events to.</param>
        /// <returns>The network logger.</returns>
        [Obsolete("Use CreateLogger method.")]
        public static NetworkLogger CreateNetworkLogger(string baseName, string hostname, int port)
        {
            var configuration = new LogConfiguration(baseName, LogType.Network, DefaultSubscriptions)
                                {
                                    Hostname = hostname,
                                    Port = (ushort)port
                                };
            return singleton.CreateNetworkLogger(configuration);
        }

        /// <summary>
        /// Destroy the given event logger (close/dispose).
        /// </summary>
        /// <param name="logger">The logger to destroy.</param>
        /// <remarks>
        /// Destroying the console logger is not allowed. Destroying a logger not owned by the current incarnation
        /// of the manager is not allowed.
        /// </remarks>
        public static void DestroyLogger(IEventLogger logger)
        {
            singleton.CloseLogger(logger);
        }

        /// <summary>
        /// Cause immediate file rotation, ignoring normal rotation timer for files set to rotate on a time basis.
        /// </summary>
        /// <returns>True if rotation occured, false otherwise (see remarks for conditions where rotation can fail.)</returns>
        /// <remarks>
        /// The log manager sets an internal rate limit on rotation to protect from accidental misuse.
        /// </remarks>
        public static bool RotateFiles()
        {
            var now = DateTime.UtcNow;
            if (((now.Ticks - singleton.lastRotation.Ticks) / TimeSpan.TicksPerSecond) >= MinDemandRotationDelta)
            {
                singleton.lastRotation = now;

                foreach (var logger in singleton.fileLoggers.Values)
                {
                    logger.Rotate(now);
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Get the event source with a given name.
        /// </summary>
        /// <param name="name">Name of the event source.</param>
        /// <returns>The EventSource, if it exists, or null.</returns>
        public static EventSource FindEventSource(string name)
        {
            foreach (var src in EventSource.GetSources())
            {
                if (string.Compare(name, src.Name, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return src;
                }
            }

            return null;
        }

        /// <summary>
        /// Get the event source with a given GUID.
        /// </summary>
        /// <param name="providerId">GUID of the event source.</param>
        /// <returns>The EventSource, if it exists, or null.</returns>
        public static EventSource FindEventSource(Guid providerId)
        {
            foreach (var src in EventSource.GetSources())
            {
                if (providerId == src.Guid)
                {
                    return src;
                }
            }

            return null;
        }

        /// <summary>
        /// Determine if a rotation interval is valid for use.
        /// </summary>
        /// <param name="interval">Rotation interval to validate.</param>
        /// <returns>true if the interval is valid, false otherwise.</returns>
        public static bool IsValidRotationInterval(int interval)
        {
            try
            {
                CheckRotationInterval(interval);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Determine if a directory name is valid for use.
        /// </summary>
        public static bool IsValidDirectory(string directory)
        {
            try
            {
                GetQualifiedDirectory(directory);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Determine if a buffer size is acceptable.
        /// </summary>
        public static bool IsValidFileBufferSize(int bufferSizeMB)
        {
            return bufferSizeMB >= MinLogBufferSizeMB &&
                   (bufferSizeMB <= MaxLogBufferSizeMB || bufferSizeMB % MaxLogBufferSizeMB == 0);
        }

        internal static bool IsCurrentProcessElevated()
        {
            var currentProcess = Process.GetCurrentProcess();
            IntPtr token;

            if (NativeMethods.OpenProcessToken(currentProcess.Handle, NativeMethods.TOKEN_QUERY, out token))
            {
                NativeMethods.TOKEN_ELEVATION tokenElevation;
                tokenElevation.TokenIsElevated = 0;
                var tokenElevationSize = Marshal.SizeOf(tokenElevation);
                var tokenElevationPtr = Marshal.AllocHGlobal(tokenElevationSize);

                try
                {
                    Marshal.StructureToPtr(tokenElevation, tokenElevationPtr, true);
                    uint unusedSize;
                    if (NativeMethods.GetTokenInformation(token, NativeMethods.TOKEN_INFORMATION_CLASS.TokenElevation,
                                                          tokenElevationPtr, (uint)tokenElevationSize, out unusedSize))
                    {
                        tokenElevation = (NativeMethods.TOKEN_ELEVATION)
                                         Marshal.PtrToStructure(tokenElevationPtr, typeof(NativeMethods.TOKEN_ELEVATION));
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(tokenElevationPtr);
                }

                return (tokenElevation.TokenIsElevated != 0);
            }

            return false;
        }

        internal static void CheckRotationInterval(int rotationInterval)
        {
            if (rotationInterval < MinRotationInterval || rotationInterval > MaxRotationInterval)
            {
                throw new ArgumentOutOfRangeException("rotationInterval");
            }

            if (rotationInterval % MinRotationInterval != 0)
            {
                throw new ArgumentException("rotation interval is not evenly divisible by minimum interval",
                                            "rotationInterval");
            }

            if (MaxRotationInterval % rotationInterval != 0)
            {
                throw new ArgumentException("rotation interval does not evenly divide into maximum interval",
                                            "rotationInterval");
            }
        }

        internal static string GetQualifiedDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory))
            {
                directory = DefaultDirectory;
            }
            else if (!Path.IsPathRooted(directory))
            {
                directory = Path.Combine(DefaultDirectory, directory);
            }

            if (directory.IndexOfAny(Path.GetInvalidPathChars()) != -1)
            {
                throw new ArgumentException("directory name contains invalid characters");
            }

            return directory;
        }

        private void CreateConsoleLogger(LogConfiguration configuration)
        {
            this.consoleLogger?.Dispose();

            this.consoleLogger = new ConsoleLogger();
            configuration.Logger = this.consoleLogger;
        }

        private FileBackedLogger GetNamedFileLogger(string name)
        {
            FileBackedLogger logger;
            lock (this.loggersLock)
            {
                this.fileLoggers.TryGetValue(name, out logger);
            }
            return logger;
        }

        private NetworkLogger GetNamedNetworkLogger(string name)
        {
            NetworkLogger logger;
            lock (this.loggersLock)
            {
                this.networkLoggers.TryGetValue(name, out logger);
            }
            return logger;
        }

        private void CloseLogger(IEventLogger logger)
        {
            if (logger is ConsoleLogger)
            {
                throw new ArgumentException("The console logger may not be closed", nameof(logger));
            }

            var mem = logger as MemoryLogger;
            if (mem != null)
            {
                mem.Dispose();
                return;
            }

            var net = logger as NetworkLogger;
            if (net != null)
            {
                lock (this.loggersLock)
                {
                    foreach (var kvp in this.networkLoggers)
                    {
                        if (ReferenceEquals(logger, kvp.Value))
                        {
                            kvp.Value.Dispose();
                            this.networkLoggers.Remove(kvp.Key);
                            return;
                        }
                    }
                }
                return;
            }

            // otherwise it must be a file logger
            lock (this.loggersLock)
            {
                foreach (var kvp in this.fileLoggers)
                {
                    if (ReferenceEquals(logger, kvp.Value.Logger))
                    {
                        kvp.Value.Dispose();
                        this.fileLoggers.Remove(kvp.Key);
                        return;
                    }
                }
            }

            throw new ArgumentException("logger being closed doesn't belong to us", nameof(logger));
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "No need to Dispose here since we keep the logger and will clean it out later.")]
        private IEventLogger CreateFileLogger(LogConfiguration configuration)
        {
            lock (this.loggersLock)
            {
                if (this.GetNamedFileLogger(configuration.Name) != null)
                {
                    throw new InvalidOperationException($"logger {configuration.Name} already exists");
                }

                var logger = new FileBackedLogger(configuration, DateTime.UtcNow);
                this.fileLoggers[configuration.Name] = logger;
                return logger.Logger;
            }
        }

        private NetworkLogger CreateNetworkLogger(LogConfiguration configuration)
        {
            lock (this.loggersLock)
            {
                if (this.GetNamedNetworkLogger(configuration.Name) != null)
                {
                    throw new InvalidOperationException($"Logger named {configuration.Name} already exists.");
                }

                var logger = new NetworkLogger(configuration.Hostname, configuration.Port);
                this.networkLoggers[configuration.Name] = logger;
                return logger;
            }
        }

        private void RotateCallback(object context)
        {
            DateTime now = DateTime.UtcNow;
            InternalLogger.Write.RotatingFiles(now.Ticks);
            lock (this.loggersLock)
            {
                if (this.rotationTimer == null)
                {
                    return; // we were disposed
                }

                foreach (var logger in this.fileLoggers.Values)
                {
                    logger.CheckedRotate(now);
                }
            }

            now = DateTime.UtcNow; // We may have had a context switch after acquiring the lock, so re-get time.
            this.rotationTimer.Change(((MinRotationInterval - now.Second) * 1000) - now.Millisecond,
                                      Timeout.Infinite);
        }

        #region IDisposable
        [SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times",
            Justification =
                "EventListener does not provide a protected Dispose(bool) method to correctly implement the pattern")]
        public override void Dispose()
        {
            base.Dispose();
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (this.loggersLock) // need to protect ourselves from the rotation timer
                {
                    this.consoleLogger.Dispose();

                    foreach (var item in this.fileLoggers.Values)
                    {
                        item.Dispose();
                    }
                    this.fileLoggers.Clear();

                    foreach (var item in this.networkLoggers.Values)
                    {
                        item.Dispose();
                    }
                    this.networkLoggers.Clear();

                    this.rotationTimer.Dispose();
                    this.rotationTimer = null;

                    if (this.configurationFileWatcher != null)
                    {
                        this.configurationFileWatcher.Dispose();
                        this.configurationFileWatcher = null;
                    }
                    // we no longer have a singleton to refer to...
                    singleton = null;
                }
            }

            // safe to do this here no matter what because all this does is call WriteEvent on an object
            // which will exist for the entire lifetime of the host, this enables external ETW tracers
            // to see that we have shut down.
            InternalLogger.Write.Shutdown();
        }
        #endregion
    }
}
