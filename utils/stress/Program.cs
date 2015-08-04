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

namespace Stress
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Tracing;
    using System.IO;
    using System.Text;
    using System.Threading;

    using Microsoft.Diagnostics.Tracing.Logging;

    internal sealed class Program
    {
        private const string memoryType = "memory";
        private const string textFileType = "textfile";
        private const string etlFileType = "etlfile";
        private static long eventsPerSecond = 100;
        private static long increment = 100;
        private static long duration = 60;
        private static int coreCount = Environment.ProcessorCount;
        private static int eventsLost;
        private static int fileRotation = -1;
        private static readonly List<int> fileBufferSizes = new List<int> {64};
        private static readonly List<int> messageSizes = new List<int> {64};
        private static readonly List<string> loggerTypes = new List<string>();
        private static readonly List<RunResults> results = new List<RunResults>();

        private static void Main(string[] argv)
        {
            // listen for info+ events
            IEventLogger cons = LogManager.ConsoleLogger;
            cons.SubscribeToEvents(Spammer.Log, EventLevel.Informational);

            if (!ProcessCommandLine(argv))
            {
                Usage();
                Environment.Exit(-1);
            }

            RunTests();

            LogManager.Shutdown();
        }

        private static void RunTests()
        {
            foreach (var bufferSize in fileBufferSizes)
            {
                foreach (var messageSize in messageSizes)
                {
                    RunStress(bufferSize, messageSize);
                }
            }

            Console.WriteLine("Events/Sec BufferSize MsgSize    Bandwidth  EventsLost");
            foreach (var result in results)
            {
                Console.WriteLine("{0,10} {1,10} {2,10} {3,10} {4,10}", result.TotalEPS, result.BufferSize,
                                  result.MessageSize, result.Bandwidth, result.EventsLost);
            }
        }

        private static void RunStress(int bufferSize, int messageSize)
        {
            Spammer.Log.FormatMessage("buffer size is {0}MB, message payload size is {1} bytes", bufferSize, messageSize);
            long eps = eventsPerSecond;
            bool success = true;
            while (success)
            {
                foreach (var t in loggerTypes)
                {
                    // remove any possible copies of the files
                    try
                    {
                        foreach (var filename in Directory.GetFiles(LogManager.DefaultDirectory, "*.log"))
                        {
                            Spammer.Log.FormatMessage("deleting stale log file {0}", filename);
                            File.Delete(filename);
                        }
                        foreach (var filename in Directory.GetFiles(LogManager.DefaultDirectory, "*.etl"))
                        {
                            Spammer.Log.FormatMessage("deleting stale log file {0}", filename);
                            try
                            {
                                File.Delete(filename);
                            }
                            catch (UnauthorizedAccessException)
                            {
                                // can't delete it, session's gotta be stopped (maybe somebody ctrl+C canceled us)
                            }
                        }
                    }
                    catch (DirectoryNotFoundException)
                    {
                        // don't care, this can happen at first run.. immaterial
                    }

                    IEventLogger logger = null;
                    MemoryLogger mem = null;
                    switch (t)
                    {
                    case memoryType:
                        logger = mem = LogManager.CreateMemoryLogger();
                        break;
                    case textFileType:
                        logger = LogManager.CreateTextLogger(textFileType, LogManager.DefaultDirectory, bufferSize,
                                                             fileRotation);
                        break;
                    case etlFileType:
                        logger = LogManager.CreateETWLogger(etlFileType, LogManager.DefaultDirectory, bufferSize,
                                                            fileRotation);
                        break;
                    }

                    logger.SubscribeToEvents(Spammer.Log, EventLevel.Verbose);
                    Thread.Sleep(500); // for ETL files subscription takes a little bit to hit the kernel
                    success = RunEvents(t, eps, messageSize);
                    if (mem != null)
                    {
                        Spammer.Log.Message(string.Format("memory stream is {0} bytes", mem.Stream.Length));
                    }

                    LogManager.DestroyLogger(logger);
                }
                results.Add(new RunResults((int)eps * coreCount, bufferSize, messageSize, eventsLost));

                eps += increment;
            }
        }

        private static bool RunEvents(string target, long eps, int messageSize)
        {
            var done = new ManualResetEvent[coreCount];

            Spammer.Log.Start(target, coreCount, eps, duration, eps * duration * coreCount);
            var timer = new Stopwatch();
            timer.Start();
            for (int i = 0; i < coreCount; ++i)
            {
                done[i] = new ManualResetEvent(false);
                var runner = new EventRunner(eps, duration, messageSize, done[i]);
                ThreadPool.QueueUserWorkItem(runner.Task);
            }
            WaitHandle.WaitAll(done);
            timer.Stop();
            bool stopped = false;
            while (!stopped)
            {
                try
                {
                    Spammer.Log.Stop(target, timer.ElapsedMilliseconds, eventsLost);
                    stopped = true;
                }
                catch (EventSourceException)
                {
                    ++eventsLost;
                    Thread.Sleep(10);
                }
            }

            if (eventsLost > 0)
            {
                eventsLost = 0;
                return false;
            }
            return true;
        }

        private static void Usage()
        {
            Spammer.Log.FormatMessage("usage: {0} <loggers:logger[,logger2,...]> [threads:N] [beginrate:N]",
                                      Process.GetCurrentProcess().ProcessName);
            Spammer.Log.Message("[endrate:N] [duration:N] [increment:N] [rotate:N] [buffersize:N]");
            Spammer.Log.Message("[messagesize:N]");
            Spammer.Log.FormatMessage("valid loggers: {0}, {1}, {2}", memoryType, textFileType, etlFileType);
        }

        private static bool ProcessCommandLine(string[] argv)
        {
            if (argv.Length < 1)
            {
                return false;
            }

            foreach (var arg in argv)
            {
                string[] split = arg.Split(':');
                if (split.Length != 2)
                {
                    Spammer.Log.FormatError("argument \"{0}\" is invalid", arg);
                    return false;
                }
                string left = split[0];
                string right = split[1];
                switch (left.ToLower())
                {
                case "threads":
                    if (!int.TryParse(right, out coreCount) || coreCount <= 0)
                    {
                        Spammer.Log.FormatError("invalid core count {0}", right);
                        return false;
                    }
                    break;
                case "loggers":
                    string[] types = right.Split(',');
                    if (types.Length == 0)
                    {
                        Spammer.Log.Error("empty loggers list");
                        return false;
                    }
                    foreach (var t in types)
                    {
                        string lt = t.ToLower();
                        if (loggerTypes.Contains(lt))
                        {
                            continue;
                        }

                        switch (lt)
                        {
                        case memoryType:
                        case textFileType:
                        case etlFileType:
                            loggerTypes.Add(lt);
                            break;
                        default:
                            Spammer.Log.FormatError("logger type {0} is unheard of!", t);
                            return false;
                        }
                    }
                    break;
                case "beginrate":
                    if (!long.TryParse(right, out eventsPerSecond) || eventsPerSecond < 100)
                    {
                        Spammer.Log.FormatError("{0} is not a valid rate", right);
                        return false;
                    }
                    break;
                case "rotate":
                    if (!int.TryParse(right, out fileRotation))
                    {
                        Spammer.Log.FormatError("{0} is not a valid rotation internval", right);
                        return false;
                    }
                    break;
                case "increment":
                    if (!long.TryParse(right, out increment) || increment < 100)
                    {
                        Spammer.Log.FormatError("{0} is not a valid increment", right);
                        return false;
                    }
                    break;
                case "duration":
                    if (!long.TryParse(right, out duration) || duration <= 0)
                    {
                        Spammer.Log.FormatError("{0} is not a valid duration", right);
                        return false;
                    }
                    break;
                case "buffersize":
                    fileBufferSizes.Clear();
                    foreach (var t in right.Split(','))
                    {
                        int size;
                        if (!int.TryParse(t, out size) || !LogManager.IsValidFileBufferSize(size))
                        {
                            Spammer.Log.FormatError("invalid buffer size {0}", size);
                            return false;
                        }
                        if (!fileBufferSizes.Contains(size))
                        {
                            fileBufferSizes.Add(size);
                        }
                    }
                    fileBufferSizes.Sort();
                    if (fileBufferSizes.Count == 0)
                    {
                        Spammer.Log.Error("empty buffersize list");
                        return false;
                    }
                    break;
                case "messagesize":
                    messageSizes.Clear();
                    foreach (var t in right.Split(','))
                    {
                        int size;
                        if (!int.TryParse(t, out size) || size <= 0 || size > 64000)
                        {
                            Spammer.Log.FormatError("invalid message size {0}", size);
                            return false;
                        }
                        if (!messageSizes.Contains(size))
                        {
                            messageSizes.Add(size);
                        }
                    }
                    messageSizes.Sort();
                    if (messageSizes.Count == 0)
                    {
                        Spammer.Log.Error("empty messagesize list");
                        return false;
                    }
                    break;
                default:
                    Spammer.Log.FormatError("unknown argument {0}", left);
                    return false;
                }
            }

            if (loggerTypes.Count == 0)
            {
                Spammer.Log.Error("invalid configuration: at least one log type is required");
                return false;
            }

            return true;
        }

        private sealed class EventRunner
        {
            private readonly long duration;
            private readonly long eventsPerSecond;
            private readonly ManualResetEvent finishedEvent;
            private readonly string message;

            public EventRunner(long eventsPerSecond, long duration, int messageSize, ManualResetEvent finishedEvent)
            {
                this.eventsPerSecond = eventsPerSecond;
                this.duration = duration;
                this.finishedEvent = finishedEvent;
                var sb = new StringBuilder();
                sb.Append('.', messageSize / sizeof(char));
                this.message = sb.ToString();
                sb.Clear();
            }

            public void Task(object context)
            {
                // a timeslice is 10ms, I just made that up on the spot.

                Guid myNewActivityId;
                LogManager.GetNewActivityId(out myNewActivityId);

                long startTicks = Stopwatch.GetTimestamp();
                long totalEvents = this.eventsPerSecond * this.duration;

                long eventsPerTimeslice = this.eventsPerSecond / 100;
                for (long i = 0; i < totalEvents; ++i)
                {
                    // give a rough approximation of how many events we should have written
                    long expectedCount = eventsPerTimeslice *
                                         ((Stopwatch.GetTimestamp() - startTicks) / (Stopwatch.Frequency / 100));
                    if (expectedCount < i)
                    {
                        Thread.Sleep(10); // brief pause
                    }
                    try
                    {
                        Spammer.Log.SpamMessage(this.message);
                    }
                    catch (EventSourceException)
                    {
                        Interlocked.Increment(ref eventsLost);
                    }
                }

                this.finishedEvent.Set();
            }
        }

        private class RunResults
        {
            public readonly int Bandwidth;
            public readonly int BufferSize;
            public readonly int EventsLost;
            public readonly int MessageSize;
            public readonly int TotalEPS;

            public RunResults(int eps, int bufferSize, int messageSize, int eventsLost)
            {
                this.TotalEPS = eps;
                this.BufferSize = bufferSize;
                this.MessageSize = messageSize;
                this.Bandwidth = this.TotalEPS * this.MessageSize;
                this.EventsLost = eventsLost;
            }
        }
    }

    [EventSource(Name = "Spammer")]
    internal sealed class Spammer : EventSource
    {
        public static Spammer Log = new Spammer();
        private Spammer() : base(true) { }

        [Event(1, Level = EventLevel.Informational, Opcode = EventOpcode.Start)]
        public void Start(string loggerName, int coreCount, long eventsPerSecondPerCore, long duration, long totalEvents)
        {
            if (this.IsEnabled())
            {
                this.WriteEvent(1, loggerName, coreCount, eventsPerSecondPerCore, duration, totalEvents);
            }
        }

        [Event(2, Level = EventLevel.Informational, Opcode = EventOpcode.Stop)]
        public void Stop(string loggerName, long timeInMs, int eventsLost)
        {
            if (this.IsEnabled())
            {
                WriteEvent(2, loggerName, timeInMs, eventsLost);
            }
        }

        [Event(3, Level = EventLevel.Verbose)]
        public void SpamMessage(string message)
        {
            if (this.IsEnabled())
            {
                WriteEvent(3, message);
            }
        }

        [Event(4, Level = EventLevel.Error)]
        public void Error(string message)
        {
            if (this.IsEnabled())
            {
                WriteEvent(4, message);
            }
        }

        [NonEvent]
        public void FormatError(string format, params object[] args)
        {
            this.Error(string.Format(format, args));
        }

        [Event(5, Level = EventLevel.Informational)]
        public void Message(string message)
        {
            if (this.IsEnabled())
            {
                WriteEvent(5, message);
            }
        }

        [NonEvent]
        public void FormatMessage(string format, params object[] args)
        {
            this.Message(string.Format(format, args));
        }
    }
}