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
    using System.Diagnostics.Tracing;

    /// <summary>
    /// EventSource for logging-related events. These are kept internal to the logging library, but the singleton is provided
    /// to enable in-process subscription to these events.
    /// </summary>
    [EventSource(Name = "Microsoft-Diagnostics-Tracing-Logging", Guid = "{23D37DB3-E5D2-493E-8BDE-EE60234BA724}")]
    public sealed class InternalLogger : EventSource
    {
        public static InternalLogger Write = new InternalLogger();

        [Event(1, Level = EventLevel.Informational)]
        internal void Startup()
        {
            if (this.IsEnabled())
            {
                this.WriteEvent(1);
            }
        }

        [Event(2, Level = EventLevel.Informational)]
        internal void Shutdown()
        {
            if (this.IsEnabled())
            {
                this.WriteEvent(2);
            }
        }

        [Event(3, Level = EventLevel.Verbose)]
        internal void NewEventSource(string sourceName, Guid sourceGuid)
        {
            if (this.IsEnabled())
            {
                WriteEvent(3, sourceName, sourceGuid);
            }
        }

        [Event(4, Level = EventLevel.Informational)]
        internal void LoggerDestinationOpened(string loggerType, string loggerDestination)
        {
            if (this.IsEnabled())
            {
                WriteEvent(4, loggerType, loggerDestination);
            }
        }

        [Event(5, Level = EventLevel.Informational)]
        internal void LoggerDestinationClosed(string loggerType, string loggerDestination)
        {
            if (this.IsEnabled())
            {
                WriteEvent(5, loggerType, loggerDestination);
            }
        }

        [Event(6, Level = EventLevel.Verbose)]
        internal void RemovedEmptyFile(string filename)
        {
            if (this.IsEnabled())
            {
                WriteEvent(6, filename);
            }
        }

        [Event(7, Level = EventLevel.Informational, Version = 2)]
        internal void DefaultLogDirectory(string rootDirectory)
        {
            if (this.IsEnabled())
            {
                WriteEvent(7, rootDirectory);
            }
        }

        [Event(8, Level = EventLevel.Error)]
        internal void InvalidConfiguration(string message)
        {
            if (this.IsEnabled())
            {
                WriteEvent(8, message);
            }
        }

        [Event(9, Level = EventLevel.Critical, Version = 2)]
        internal void AssertionFailed(string message, string stackTrace)
        {
            if (this.IsEnabled())
            {
                WriteEvent(9, message ?? string.Empty, stackTrace);
            }
        }

        [Event(10, Level = EventLevel.Verbose)]
        internal void RotatingFiles(long currentTicks)
        {
            if (this.IsEnabled())
            {
                WriteEvent(10, currentTicks);
            }
        }

        [Event(11, Level = EventLevel.Informational)]
        internal void CreateFileDestination(string baseFilename, string destinationDirectory, int rotationInterval,
                                          string filenameTemplate)
        {
            if (this.IsEnabled())
            {
                this.WriteEvent(11, baseFilename, destinationDirectory, rotationInterval, filenameTemplate);
            }
        }

        [Event(12, Level = EventLevel.Verbose)]
        internal void UpdateFileRotationTimes(string baseFilename, long startTicks, long endTicks)
        {
            if (this.IsEnabled())
            {
                WriteEvent(12, baseFilename, startTicks, endTicks);
            }
        }

        [Event(13, Level = EventLevel.Informational)]
        internal void SetConfigurationFile(string filename)
        {
            if (this.IsEnabled())
            {
                WriteEvent(13, filename ?? string.Empty);
            }
        }

        [Event(14, Level = EventLevel.Verbose)]
        internal void ProcessedConfigurationFile(string filename)
        {
            if (this.IsEnabled())
            {
                WriteEvent(14, filename);
            }
        }

        [Event(15, Level = EventLevel.Error)]
        internal void InvalidConfigurationFile(string filename)
        {
            if (this.IsEnabled())
            {
                WriteEvent(15, filename);
            }
        }

        [Event(16, Level = EventLevel.Warning)]
        internal void ConflictingTraceSessionFound(string sessionName)
        {
            if (this.IsEnabled())
            {
                WriteEvent(16, sessionName);
            }
        }

        [Event(17, Level = EventLevel.Error)]
        internal void ConflictingTraceSessionStuck(string sessionName)
        {
            if (this.IsEnabled())
            {
                WriteEvent(17, sessionName);
            }
        }

        [Event(18, Level = EventLevel.Verbose,
            Message = "ETW logging for destination {0} has been disabled, using text instead")]
        internal void OverridingEtwLogging(string logName)
        {
            if (this.IsEnabled())
            {
                WriteEvent(18, logName);
            }
        }

        [Event(19, Level = EventLevel.Error)]
        internal void UnableToOpenTraceSession(string sessionName, string exceptionType, string exceptionMessage)
        {
            if (this.IsEnabled())
            {
                WriteEvent(19, sessionName);
            }
        }

        [Event(20, Level = EventLevel.Error)]
        internal void UnableToChangeTraceSessionFilename(string sessionName, string newFilename, string exceptionType,
                                                       string exceptionMessage)
        {
            if (this.IsEnabled())
            {
                WriteEvent(20, sessionName, newFilename, exceptionType, exceptionMessage);
            }
        }
    }
}