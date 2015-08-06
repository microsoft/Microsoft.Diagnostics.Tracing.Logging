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
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.Diagnostics.Tracing;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Threading;

    /// <summary>
    /// Possible values for <see cref="LogManager.AllowEtwLogging"/>
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

    /// <summary>
    /// Handles configuration and management of logging facilities.
    /// </summary>
    /// <remarks>
    /// Most methods and properties are explicitly not thread-safe. It is an error to attempt to interface with the
    /// manager from multiple threads concurrently.
    /// </remarks>
    public sealed partial class LogManager : EventListener
    {
        #region Public
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
        public const int MinFileBufferSizeMB = 1;

        /// <summary>
        /// Maximum size of a file buffer.
        /// </summary>
        public const int MaxFileBufferSizeMB = 128;

        /// <summary>
        /// The default buffer size for log files.
        /// </summary>
        public const int DefaultFileBufferSizeMB = 2;

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
        /// Whether or not to allow ETW logging.
        /// </summary>
        /// <remarks>
        /// The default value (None) means that when the log manager is started it will determine based on its environment
        /// whether or not to allow ETW logging. If the value is set prior to calling <see cref="Start"/> then that value
        /// will be honored.
        /// 
        /// When the manager is shutdown the value will be reset to None.
        ///
        /// Why do this? Many users in a "single box" scenario aren't ready to deal with the overhead of ETW. ETW creates
        /// files locked to the kernel which, when a process is improperly terminated, just stay open. For many folks not
        /// interested in logging the behavior is incredibly disruptive to their iterative development process and, really,
        /// all they want is to notepad.exe some test logs without extra pain involved in dealing with a binary format.
        /// Additionally, for tests which fail and terminate early it's easier to deal with failures that do not happen
        /// to leave dangling logging sessions.
        /// </remarks>
        public static AllowEtwLoggingValues AllowEtwLogging { get; set; }

        /// <summary>
        /// Retrieve the console logger.
        /// </summary>
        public static IEventLogger ConsoleLogger
        {
            get { return singleton.consoleLogger; }
        }

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
            if (singleton != null)
            {
                singleton.Dispose();
            }

            AllowEtwLogging = AllowEtwLoggingValues.None;
        }

        /// <summary>
        /// Retrieve the named file logger.
        /// </summary>
        /// <param name="name">Name of the logger.</param>
        /// <returns>The logger, or null if no logger of the given name exists.</returns>
        public static IEventLogger GetFileLogger(string name)
        {
            FileBackedLogger logger = singleton.GetNamedFileLogger(name);
            if (logger != null)
            {
                return logger.Logger;
            }
            return null;
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
        public static IEventLogger CreateTextLogger(string name, string directory = null,
                                                    int bufferSizeMB = DefaultFileBufferSizeMB, int rotation = -1,
                                                    string filenameTemplate = FileBackedLogger.DefaultFilenameTemplate,
                                                    bool fileTimestampLocal = false)
        {
            return singleton.CreateFileLogger(LoggerType.TextLogFile, name, directory, bufferSizeMB, rotation,
                                              filenameTemplate, fileTimestampLocal);
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
        public static IEventLogger CreateETWLogger(string name, string directory = null,
                                                   int bufferSizeMB = DefaultFileBufferSizeMB, int rotation = -1,
                                                   string filenameTemplate = FileBackedLogger.DefaultFilenameTemplate,
                                                   bool fileTimestampLocal = false)
        {
            return singleton.CreateFileLogger(LoggerType.ETLFile, name, directory, bufferSizeMB, rotation,
                                              filenameTemplate, fileTimestampLocal);
        }

        /// <summary>
        /// Create a new memory logger.
        /// </summary>
        /// <returns>The memory logger.</returns>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Willing to allow MemoryLogger unhandled exceptions to result in somewhat bad behavior.")]
        public static MemoryLogger CreateMemoryLogger()
        {
            return new MemoryLogger(new MemoryStream(InitialMemoryStreamSize));
        }

        /// <summary>
        /// Create a new memory logger with a pre-allocated memory stream.
        /// </summary>
        /// <param name="stream">The MemoryStream to use.</param>
        /// <returns>The memory logger.</returns>
        public static MemoryLogger CreateMemoryLogger(MemoryStream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }

            return new MemoryLogger(stream);
        }

        /// <summary>
        /// Create a new network logger.
        /// </summary>
        /// <param name="baseName">Name of the logger.</param>
        /// <param name="hostname">Hostname to send events to.</param>
        /// <param name="port">Port to send events to.</param>
        /// <returns>The network logger.</returns>
        public static NetworkLogger CreateNetworkLogger(string baseName, string hostname, int port)
        {
            return singleton.CreateNetLogger(baseName, hostname, port);
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
            DateTime now = DateTime.UtcNow;
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
            string newDir = directory;
            try
            {
                GetQualifiedDirectory(ref newDir);
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
            try
            {
                CheckFileBufferSize(bufferSizeMB);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
        #endregion

        #region Private
        internal const string DataDirectoryEnvironmentVariable = "DATADIR";

        /// <summary>
        /// Minimum seconds between rotations (used to prevent over-aggressive requests to the public rotator.)
        /// </summary>
        internal const int MinDemandRotationDelta = 5;

        /// <summary>
        /// Singleton entry to insure only one manager exists at any given time.
        /// </summary>
        internal static LogManager singleton;

        // The name of the console logger (used to distinguish it from other file targets by using invalid file chars.)
        internal const string ConsoleLoggerName = ":console:";
        private ConsoleLogger consoleLogger;

        internal static int ProcessID = Process.GetCurrentProcess().Id;

        // 64kB memory streams for memory loggers. This number is totally arbitrary.
        private const int InitialMemoryStreamSize = 64 * 1024;

        internal readonly Dictionary<string, FileBackedLogger> fileLoggers =
            new Dictionary<string, FileBackedLogger>(StringComparer.OrdinalIgnoreCase);

        internal readonly Dictionary<string, NetworkLogger> networkLoggers =
            new Dictionary<string, NetworkLogger>(StringComparer.OrdinalIgnoreCase);

        private readonly object loggersLock = new object();

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

            this.CreateConsoleLogger();

            if (AllowEtwLogging == AllowEtwLoggingValues.None)
            {
                if (IsCurrentProcessElevated())
                {
                    AllowEtwLogging = AllowEtwLoggingValues.Enabled;
                }
                else
                {
                    AllowEtwLogging = AllowEtwLoggingValues.Disabled;
                }
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

            DateTime now = DateTime.UtcNow;
            this.rotationTimer = new Timer(this.RotateCallback, null,
                                           ((MinRotationInterval - now.Second) * 1000) - now.Millisecond,
                                           Timeout.Infinite);
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
                IntPtr tokenElevationPtr = Marshal.AllocHGlobal(tokenElevationSize);
                uint unusedSize;

                try
                {
                    Marshal.StructureToPtr(tokenElevation, tokenElevationPtr, true);
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

        private void CreateConsoleLogger()
        {
            if (this.consoleLogger != null)
            {
                this.consoleLogger.Dispose();
            }

            this.consoleLogger = new ConsoleLogger();
            this.consoleLogger.SubscribeToEvents(InternalLogger.Write, EventLevel.Critical);
            // means Assert will hit console.
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
                throw new ArgumentException("The console logger may not be closed", "logger");
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

            throw new ArgumentException("logger being closed doesn't belong to us", "logger");
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "No need to Dispose here since we keep the logger and will clean it out later.")]
        private IEventLogger CreateFileLogger(LoggerType type, string baseName, string directory, int bufferSizeMB,
                                              int rotation, string filenameTemplate, bool timestampLocal)
        {
            if (rotation < 0 && DefaultRotate)
            {
                rotation = this.defaultRotationInterval;
            }

            if (rotation > 0)
            {
                CheckRotationInterval(rotation);
            }

            CheckFileBufferSize(bufferSizeMB);
            GetQualifiedDirectory(ref directory);

            if (baseName.IndexOfAny(Path.GetInvalidFileNameChars()) != -1)
            {
                throw new ArgumentException("base filename contains invalid characters", "baseName");
            }

            lock (this.loggersLock)
            {
                if (this.GetNamedFileLogger(baseName) != null)
                {
                    throw new InvalidOperationException("logger named " + baseName + " already exists");
                }

                var logger = new FileBackedLogger(baseName, directory, type, bufferSizeMB, rotation, filenameTemplate,
                                                  timestampLocal);
                this.fileLoggers[baseName] = logger;
                return logger.Logger;
            }
        }

        private NetworkLogger CreateNetLogger(string baseName, string hostname, int port)
        {
            lock (this.loggersLock)
            {
                if (this.GetNamedNetworkLogger(baseName) != null)
                {
                    throw new InvalidOperationException("logger named " + baseName + " already exists");
                }

                var logger = new NetworkLogger(hostname, port);
                this.networkLoggers[baseName] = logger;
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

        private static void CheckRotationInterval(int rotationInterval)
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

        private static void GetQualifiedDirectory(ref string directory)
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
        }

        private static void CheckFileBufferSize(int bufferSizeMB)
        {
            if (bufferSizeMB < MinFileBufferSizeMB
                || (bufferSizeMB > MaxFileBufferSizeMB && bufferSizeMB % MaxFileBufferSizeMB != 0))
            {
                throw new ArgumentOutOfRangeException("bufferSizeMB",
                                                      "buffer size must be between MinFileBufferSizeMB and MaxFileBufferSizeMB");
            }
        }
        #endregion

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