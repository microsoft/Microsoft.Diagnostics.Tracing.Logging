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

namespace Microsoft.Diagnostics.Tracing.Logging.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Tracing;
    using System.Globalization;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Runtime.Serialization;
    using System.Threading;

    using Microsoft.Diagnostics.Tracing.Session;

    using NUnit.Framework;

    internal class TestLogger : EventSource
    {
        public static TestLogger Write = new TestLogger();

        [Event(1, Level = EventLevel.Verbose)]
        public void String(string message)
        {
            WriteEvent(1, message);
        }

        public void Int(int message)
        {
            WriteEvent(2, message);
        }

        [Event(3, Keywords = Keywords.FirstKeyword)]
        public void First(string message)
        {
            WriteEvent(3, message);
        }

        [Event(4, Keywords = Keywords.FifthKeyword)]
        public void Fifth(string message)
        {
            WriteEvent(4, message);
        }

        [Event(5, Opcode = EventOpcode.Extension, Task = Tasks.OnlyTask)]
        public void Element(byte wind, int water, string earth, double fire, bool heart) // captain planet++
        {
            this.WriteEvent(5, wind, water, earth, fire, heart);
        }

        public sealed class Keywords
        {
            public const EventKeywords None = EventKeywords.None;
            public const EventKeywords FirstKeyword = (EventKeywords)0x1;
            public const EventKeywords FifthKeyword = (EventKeywords)0x10;
        }

        public sealed class Tasks
        {
            public const EventTask OnlyTask = (EventTask)1;
        }
    }

    internal class MultiWriterTestLogger : TestLogger { }

    [TestFixture]
    public class LoggerTests
    {
        internal static int CountFileLines(string filename)
        {
            int lines = 0;
            using (var reader = new StreamReader(new FileStream(filename, FileMode.Open)))
            {
                while (reader.ReadLine() != null)
                {
                    ++lines;
                }
            }
            return lines;
        }

        /// <summary>
        /// Internal class used for testing the network logger
        /// </summary>
        internal class NetworkLoggerListener
        {
            private readonly object receivedEventsLock = new object();
            private readonly AutoResetEvent waitHandle = new AutoResetEvent(false);
            private HttpListener httpListener;
            private bool isRunning;
            private int waitCount;

            public NetworkLoggerListener()
            {
                var socket = new TcpListener(IPAddress.Loopback, 0);
                socket.Start();
                this.Port = (ushort)((IPEndPoint)socket.LocalEndpoint).Port;
                socket.Stop();
                this.ReceivedEvents = new List<ETWEvent>();
            }

            public ushort Port { get; }

            public List<ETWEvent> ReceivedEvents { get; }

            /// <summary>
            /// Start listening to events
            /// </summary>
            public void Start()
            {
                if (!this.isRunning)
                {
                    this.ReceivedEvents.Clear();
                    this.httpListener = new HttpListener();
                    this.httpListener.Prefixes.Add($"http://+:{this.Port}/");
                    this.httpListener.Start();
                    this.isRunning = true;
                    this.ReceiveRequestAsync();
                }
            }

            /// <summary>
            /// Stop listening to events
            /// </summary>
            public void Stop()
            {
                if (this.isRunning)
                {
                    this.httpListener.Abort();
                    this.httpListener.Close();
                }
                this.isRunning = false;
            }

            /// <summary>
            /// Sets how many events to wait for
            /// </summary>
            /// <param name="count"></param>
            public void SetWaitReceivedEventsCount(int count)
            {
                this.waitCount = count;
            }

            /// <summary>
            /// Wait for the number of received events
            /// </summary>
            public bool WaitForReceivedEventsCount(int timeoutInMs)
            {
                return this.waitHandle.WaitOne(timeoutInMs);
            }

            private bool ReceiveRequestAsync()
            {
                if (!this.isRunning)
                {
                    return false;
                }

                try
                {
                    this.httpListener.BeginGetContext(this.ProcessHttpRequest, null);
                    return true;
                }
                catch (HttpListenerException) { }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                catch (ApplicationException) { }

                return false;
            }

            private void ProcessHttpRequest(IAsyncResult result)
            {
                if (!this.isRunning || !this.ReceiveRequestAsync())
                {
                    return;
                }

                HttpListenerContext context = this.httpListener.EndGetContext(result);
                var serializer = new DataContractSerializer(typeof(ETWEvent));
                var eventData = serializer.ReadObject(context.Request.InputStream) as ETWEvent;

                if (eventData != null)
                {
                    lock (this.receivedEventsLock)
                    {
                        this.ReceivedEvents.Add(eventData);
                    }
                }

                if (this.ReceivedEvents.Count >= this.waitCount)
                {
                    this.waitHandle.Set();
                }
            }
        }

        [Test]
        public void ActivityIDFilter()
        {
            Guid goodID = Guid.NewGuid();
            Guid skipID = Guid.NewGuid();
            Assert.AreNotEqual(goodID, skipID); // oh paranoia how I love.. loathe.. like thee.

            LogManager.Start();
            MemoryLogger memoryLog = new MemoryLogger(new MemoryStream());

            memoryLog.SubscribeToEvents(TestLogger.Write, EventLevel.Verbose);
            memoryLog.FormatOptions = TextLogFormatOptions.ShowActivityID; // no timestamps please
            Assert.AreEqual(memoryLog.FilterActivityID, Guid.Empty);
            LogManager.ClearActivityId();
            TestLogger.Write.String("not yet");
            memoryLog.FilterActivityID = goodID;
            TestLogger.Write.String("now filtered");
            LogManager.SetActivityId(goodID);
            TestLogger.Write.String("good ID");
            LogManager.ClearActivityId();
            TestLogger.Write.String("no ID");
            LogManager.SetActivityId(skipID);
            TestLogger.Write.String("skipped ID");
            memoryLog.FilterActivityID = Guid.Empty;
            TestLogger.Write.String("not anymore");
            LogManager.ClearActivityId();
            memoryLog.Disabled = true;

            memoryLog.Stream.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(memoryLog.Stream))
            {
                string line;
                int lines = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    ++lines;
                    switch (lines)
                    {
                    case 1:
                    {
                        Assert.IsTrue(0 == string.Compare(line, "[v:TestLogger String] message=\"not yet\"",
                                                          StringComparison.Ordinal));
                        break;
                    }
                    case 2:
                    {
                        string[] split = line.Split(new[] {' '}, 2);
                        Assert.AreEqual(2, split.Length);
                        Assert.AreEqual(34, split[0].Length);
                        Assert.AreEqual('(', split[0][0]);
                        Assert.AreEqual(')', split[0][33]);
                        Assert.IsTrue(new Guid(split[0].Substring(1, 32)) == goodID);
                        Assert.IsTrue(0 == string.Compare(split[1], "[v:TestLogger String] message=\"good ID\"",
                                                          StringComparison.Ordinal));
                        break;
                    }
                    case 3:
                    {
                        string[] split = line.Split(new[] {' '}, 2);
                        Assert.AreEqual(2, split.Length);
                        Assert.AreEqual(34, split[0].Length);
                        Assert.AreEqual('(', split[0][0]);
                        Assert.AreEqual(')', split[0][33]);
                        Assert.IsTrue(new Guid(split[0].Substring(1, 32)) == skipID);
                        Assert.IsTrue(0 == string.Compare(split[1], "[v:TestLogger String] message=\"not anymore\"",
                                                          StringComparison.Ordinal));
                        break;
                    }
                    }
                }
                Assert.AreEqual(3, lines);
            }
            memoryLog.Dispose();
            LogManager.Shutdown();
        }

        [Test]
        public void ActivityIDFormat()
        {
            const string activityID = "d00dfeedbeeffeedbeefd00dfeedbeef"; // I implore thee!
            LogManager.Start();
            var memoryLog = new MemoryLogger();

            memoryLog.FormatOptions &= ~TextLogFormatOptions.ProcessAndThreadData;

            memoryLog.SubscribeToEvents(TestLogger.Write, EventLevel.Verbose);
            // showing activity ID should be the default
            Assert.IsTrue(memoryLog.FormatOptions.HasFlag(TextLogFormatOptions.ShowActivityID));
            LogManager.ClearActivityId();
            TestLogger.Write.String("ID is empty");
            LogManager.SetActivityId(new Guid(activityID));
            TestLogger.Write.String("has ID");
            memoryLog.FormatOptions &= ~TextLogFormatOptions.ShowActivityID;
            TestLogger.Write.String("no ID");
            LogManager.ClearActivityId();
            memoryLog.Disabled = true;

            memoryLog.Stream.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(memoryLog.Stream))
            {
                string line;
                int lines = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    ++lines;
                    switch (lines)
                    {
                    case 1:
                    {
                        string[] split = line.Split(new[] {' '}, 2);
                        Assert.AreEqual(2, split.Length); // first split should be time, then the message
                        DateTime.ParseExact(split[0], EventStringFormatter.TimeFormat, CultureInfo.InvariantCulture);
                        Assert.IsTrue(0 == string.Compare(split[1], "[v:TestLogger String] message=\"ID is empty\"",
                                                          StringComparison.Ordinal));
                        break;
                    }
                    case 2:
                    {
                        string[] split = line.Split(new[] {' '}, 3);
                        Assert.AreEqual(3, split.Length);
                        DateTime.ParseExact(split[0], EventStringFormatter.TimeFormat, CultureInfo.InvariantCulture);
                        Assert.AreEqual(34, split[1].Length);
                        Assert.AreEqual('(', split[1][0]);
                        Assert.AreEqual(')', split[1][33]);
                        Assert.IsTrue(new Guid(split[1].Substring(1, 32)) == new Guid(activityID));
                        Assert.IsTrue(0 == string.Compare(split[2], "[v:TestLogger String] message=\"has ID\"",
                                                          StringComparison.Ordinal));
                        break;
                    }
                    case 3:
                    {
                        string[] split = line.Split(new[] {' '}, 2);
                        Assert.AreEqual(2, split.Length);
                        DateTime.ParseExact(split[0], EventStringFormatter.TimeFormat, CultureInfo.InvariantCulture);
                        Assert.IsTrue(0 == string.Compare(split[1], "[v:TestLogger String] message=\"no ID\"",
                                                          StringComparison.Ordinal));
                        break;
                    }
                    }
                }
                Assert.AreEqual(3, lines);
            }
            memoryLog.Dispose();
            LogManager.Shutdown();
        }

        [Test]
        public void BasicTextWriting()
        {
            LogManager.Start();
            var memoryLog = new MemoryLogger();

            memoryLog.SubscribeToEvents(TestLogger.Write, EventLevel.Verbose);
            TestLogger.Write.String("first message");
            TestLogger.Write.String("second\nmessage");
            // we used to test passing \0 characters here -- EventSource in .NET 4.5.1 correctly treats these as string
            // terminators, however in 4.5 it does not. We need to operate in both worlds for now, so we'll ignore this
            // for the time being.
            memoryLog.Disabled = true;

            memoryLog.Stream.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(memoryLog.Stream))
            {
                string line;
                int lines = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    ++lines;
                    var expectedMetaData = string.Format("[{0}/{1}/v:TestLogger String]", Process.GetCurrentProcess().Id,
                                                         NativeMethods.GetCurrentWin32ThreadId());
                    Assert.IsTrue(line.Contains(expectedMetaData));
                    // this is the pretty name of the event we should have deduced.
                    switch (lines)
                    {
                    case 1:
                        Assert.IsTrue(line.EndsWith(" message=\"first message\""));
                        break;
                    case 2:
                        Assert.IsTrue(line.EndsWith(" message=\"second\\nmessage\""));
                        break;
                    }
                }
                Assert.AreEqual(2, lines);
            }
            memoryLog.Dispose();
            LogManager.Shutdown();
        }

        [Test]
        public void DisabledFlag()
        {
            LogManager.Start();
            var memoryLog = new MemoryLogger();

            memoryLog.SubscribeToEvents(TestLogger.Write, EventLevel.Verbose);
            TestLogger.Write.String("bacon");
            TestLogger.Write.String("lettuce");
            memoryLog.Disabled = true;
            TestLogger.Write.String("avacado");
            memoryLog.Disabled = false;
            TestLogger.Write.String("tomato");
            memoryLog.Disabled = true;
            memoryLog.Stream.Seek(0, SeekOrigin.Begin);

            // Shouldn't be any avacado in there. S'weird.
            using (var reader = new StreamReader(memoryLog.Stream))
            {
                int lines = 0;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    ++lines;
                    Assert.IsFalse(line.Contains("avacado"));
                }
                Assert.AreEqual(3, lines);
            }
            memoryLog.Dispose();
            LogManager.Shutdown();
        }

        [Test]
        public void Filtering()
        {
            LogManager.Start();
            var memoryLog = new MemoryLogger();

            memoryLog.SubscribeToEvents(TestLogger.Write, EventLevel.Verbose);
            TestLogger.Write.String("first message");
            TestLogger.Write.String("bacon...bacon...bacon... OMG BACON");
            memoryLog.Disabled = true;
            memoryLog.Stream.Seek(0, SeekOrigin.Begin);

            // Nothing filtered.
            using (var reader = new StreamReader(memoryLog.Stream))
            {
                int lines = 0;
                while ((reader.ReadLine()) != null)
                {
                    ++lines;
                }
                Assert.AreEqual(2, lines);
            }
            memoryLog.Dispose();

            memoryLog = new MemoryLogger();
            memoryLog.SubscribeToEvents(TestLogger.Write, EventLevel.Verbose);
            memoryLog.AddRegexFilter("first");
            TestLogger.Write.String("first message");
            TestLogger.Write.String("bacon...bacon...bacon... OMG BACON");
            memoryLog.Disabled = true;
            memoryLog.Stream.Seek(0, SeekOrigin.Begin);

            // Should now get just the one message
            using (var reader = new StreamReader(memoryLog.Stream))
            {
                int lines = 0;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    Assert.IsFalse(line.Contains("bacon"));
                    ++lines;
                }
                Assert.AreEqual(1, lines);
            }
            memoryLog.Dispose();

            memoryLog = new MemoryLogger();
            memoryLog.SubscribeToEvents(TestLogger.Write, EventLevel.Verbose);
            memoryLog.AddRegexFilter("BaCoN"); // filter for bacon, ensure REs are not case sensitive.
            TestLogger.Write.String("first message");
            TestLogger.Write.String("bacon...bacon...bacon... OMG BACON");
            memoryLog.Disabled = true;
            memoryLog.Stream.Seek(0, SeekOrigin.Begin);

            // Now we should get the second only.
            using (var reader = new StreamReader(memoryLog.Stream))
            {
                int lines = 0;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    ++lines;
                    Assert.IsTrue(line.Contains("bacon"));
                }
                Assert.AreEqual(1, lines);
            }
            memoryLog.Dispose();
            LogManager.Shutdown();
        }

        [Test]
        public void NamedArguments()
        {
            LogManager.Start();
            var memoryLog = new MemoryLogger();

            memoryLog.SubscribeToEvents(TestLogger.Write, EventLevel.Verbose);
            memoryLog.FormatOptions &= ~TextLogFormatOptions.ProcessAndThreadData;
            TestLogger.Write.Element(0xbe, 0xef, "cake", 2.1, true);
            memoryLog.Disabled = true;

            memoryLog.Stream.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(memoryLog.Stream))
            {
                string line;
                int lines = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    ++lines;
                    Assert.IsTrue(
                                  line.Contains(
                                                "[i:TestLogger OnlyTask/Extension] wind=190 water=239 earth=\"cake\" fire=2.1 heart=True"));
                }
                Assert.AreEqual(1, lines);
            }
            memoryLog.Dispose();
            LogManager.Shutdown();
        }

        [Test]
        public void TestAllowEtwKnob()
        {
            // should be None on shutdown
            LogManager.Shutdown();
            Assert.AreEqual(Configuration.AllowEtwLoggingValues.None, LogManager.Configuration.AllowEtwLogging);

            LogManager.Start();
            Assert.AreNotEqual(Configuration.AllowEtwLoggingValues.None, LogManager.Configuration.AllowEtwLogging);
            LogManager.Shutdown();

            // If we give it a value the value must persist
            LogManager.Configuration.AllowEtwLogging = Configuration.AllowEtwLoggingValues.Enabled;
            LogManager.Start();
            Assert.AreEqual(Configuration.AllowEtwLoggingValues.Enabled, LogManager.Configuration.AllowEtwLogging);
            LogManager.Shutdown();
            Assert.AreEqual(Configuration.AllowEtwLoggingValues.None, LogManager.Configuration.AllowEtwLogging);
            LogManager.Configuration.AllowEtwLogging = Configuration.AllowEtwLoggingValues.Disabled;
            LogManager.Start();
            Assert.AreEqual(Configuration.AllowEtwLoggingValues.Disabled, LogManager.Configuration.AllowEtwLogging);
            LogManager.Shutdown();
            Assert.AreEqual(Configuration.AllowEtwLoggingValues.None, LogManager.Configuration.AllowEtwLogging);

            // Okay, now make sure if we give it config that it does override for us
            var logConfig =
                new LogConfiguration("etwLogger", LogType.EventTracing,
                                     new[] {new EventProviderSubscription(InternalLogger.Write)});
            var config = new Configuration(new[] { logConfig, });
            LogManager.Configuration.AllowEtwLogging = Configuration.AllowEtwLoggingValues.Disabled;
            LogManager.Start();
            LogManager.SetConfiguration(config);
            Assert.AreEqual(1, LogManager.singleton.fileLoggers.Count);

            var theLogger = LogManager.GetLogger<ETLFileLogger>("etwLogger");
            Assert.IsNull(theLogger);
            var theRealLogger = LogManager.GetLogger<TextFileLogger>("etwLogger");
            Assert.IsNotNull(theRealLogger);
            string filename = Path.GetFileName(theRealLogger.Filename);
            Assert.AreEqual(filename, "etwLogger.log");

            LogManager.Shutdown();
        }

        [Test]
        public void TestBufferSizeLimits()
        {
            LogManager.Start();
            LogManager.SetConfiguration((Configuration)null);

            foreach (var type in new[] {LogType.Text, LogType.EventTracing})
            {
                foreach (var bufferSize in new[] {LogManager.MinLogBufferSizeMB, LogManager.MaxLogBufferSizeMB})
                {
                    IEventLogger logger =
                        LogManager.CreateLogger(new LogConfiguration("test", type, LogManager.DefaultSubscriptions)
                                                {
                                                    Directory = ".",
                                                    BufferSizeMB = bufferSize,
                                                });
                    Assert.IsNotNull(logger);
                    LogManager.DestroyLogger(logger);
                }
            }

            LogManager.Shutdown();
        }

        [Test]
        public void TestKeywordFiltering()
        {
            LogManager.Start();
            string root = LogManager.DefaultDirectory;
            File.Delete(Path.Combine(root, "kwd1.log"));
            File.Delete(Path.Combine(root, "kwd2.log"));
            File.Delete(Path.Combine(root, "allkwd.log"));

            var logs =
                new[]
                {
                    new LogConfiguration("kwd1", LogType.Text,
                                         new[]
                                         {
                                             new EventProviderSubscription(TestLogger.Write, EventLevel.Verbose, 0x1),
                                         }),
                    new LogConfiguration("kwd2", LogType.Text,
                                         new[]
                                         {
                                             new EventProviderSubscription(TestLogger.Write, EventLevel.Verbose, 0x10),
                                         }),
                    new LogConfiguration("allkwd", LogType.Text,
                                         new[]
                                         {
                                             new EventProviderSubscription(TestLogger.Write, EventLevel.Verbose, 0x11),
                                         }),
                };
            LogManager.SetConfiguration(new Configuration(logs));

            for (int i = 0; i < 10; ++i)
            {
                TestLogger.Write.First("national bank");
            }
            for (int i = 0; i < 10; ++i)
            {
                TestLogger.Write.Fifth("of Jack");
            }

            LogManager.Shutdown();
            Assert.AreEqual(10, CountFileLines(Path.Combine(root, "kwd1.log")));
            Assert.AreEqual(10, CountFileLines(Path.Combine(root, "kwd2.log")));
            Assert.AreEqual(20, CountFileLines(Path.Combine(root, "allkwd.log")));
        }

        [Test]
        public void TestNetworkLogger()
        {
            const int eventsToWrite = 1;
            NetworkLoggerListener netListener;
            try
            {
                netListener = new NetworkLoggerListener();
                netListener.Start();
            }
            catch (HttpListenerException)
            {
                Assert.Inconclusive("Could not start HTTP server to validate network logger.");
                return;
            }

            var port = netListener.Port;

            try
            {
                netListener.SetWaitReceivedEventsCount(eventsToWrite);
                LogManager.Start();
                LogManager.DefaultRotate = false;

                var subs = new[] {new EventProviderSubscription(TestLogger.Write, EventLevel.Verbose),};
                var config = new LogConfiguration("NetLog", LogType.Network, subs)
                             {
                                 Hostname = IPAddress.Loopback.ToString(),
                                 Port = port
                             };
                var logger = LogManager.CreateLogger<NetworkLogger>(config);

                for (int i = 0; i < eventsToWrite; ++i)
                {
                    TestLogger.Write.Int(i);
                    Thread.Sleep(5);
                }

                LogManager.DestroyLogger(logger);
                Assert.IsTrue(netListener.WaitForReceivedEventsCount(5 * 1000));
                Assert.AreEqual(netListener.ReceivedEvents[0].Parameters.Count, 1);

                for (int i = 0; i < eventsToWrite; ++i)
                {
                    Assert.AreEqual(netListener.ReceivedEvents[i].Parameters[0], i);
                }
            }
            finally
            {
                LogManager.Shutdown();
                netListener.Stop();
            }
        }

        [Test]
        public void TestSessionClosing()
        {
            LogManager.Shutdown(); // not needed by us

            // set this just to make sure it isn't actually used.
            LogManager.Configuration.AllowEtwLogging = Configuration.AllowEtwLoggingValues.Disabled;

            File.Delete("fakesession.etl");
            var s1 = new TraceEventSession(ETLFileLogger.SessionPrefix + "fakeSession", "fakeSession.etl");
            s1.EnableProvider(TestLogger.Write.Guid, TraceEventLevel.Verbose);
            s1.StopOnDispose = false;
            TestLogger.Write.String("mc frontalot = nerdcore raps");
            Assert.IsTrue(File.Exists("fakesession.etl"));
            Assert.IsNotNull(TraceEventSession.GetActiveSession(ETLFileLogger.SessionPrefix + "fakeSession"));
            s1.Dispose();

            // so the session should still exist ...
            Assert.IsNotNull(TraceEventSession.GetActiveSession(ETLFileLogger.SessionPrefix + "fakeSession"));
            var s2 = new ETLFileLogger("fakeSession", "fakeSession.etl", 64);
            s2.SubscribeToEvents(TestLogger.Write.Guid, EventLevel.Verbose);
            TestLogger.Write.String("optimus rhyme = also rapping nerd");
            s2.Dispose();
            const int maxWait = 1000;
            int slept = 0;
            while (slept < maxWait)
            {
                if (TraceEventSession.GetActiveSession(ETLFileLogger.SessionPrefix + "fakeSession") == null)
                {
                    break;
                }
                Thread.Sleep(maxWait / 10);
                slept += maxWait / 10;
            }
            Assert.IsNull(TraceEventSession.GetActiveSession(ETLFileLogger.SessionPrefix + "fakeSession"));
        }

        [Test]
        public void TextFiles()
        {
            LogManager.Start();
            LogManager.DefaultRotate = false;

            const string logFilename = "testlog.log";
            string fullFilename = Path.Combine(LogManager.DefaultDirectory, logFilename);
            File.Delete(fullFilename);
            IEventLogger logger =
                LogManager.CreateLogger(new LogConfiguration(Path.GetFileNameWithoutExtension(logFilename), LogType.Text,
                                                             LogManager.DefaultSubscriptions)
                                        {Directory = "."});
            Assert.IsTrue(File.Exists(fullFilename));
            LogManager.DestroyLogger(logger);
            Assert.IsFalse(File.Exists(fullFilename)); // should delete empty files

            logger =
                LogManager.CreateLogger(new LogConfiguration(Path.GetFileNameWithoutExtension(logFilename), LogType.Text,
                                                             LogManager.DefaultSubscriptions)
                                        {Directory = "."});
            logger.SubscribeToEvents(TestLogger.Write, EventLevel.Verbose);
            const int linesToWrite = 10;
            for (int i = 0; i < linesToWrite; ++i)
            {
                TestLogger.Write.Int(i);
            }
            LogManager.DestroyLogger(logger);
            Assert.IsTrue(File.Exists(fullFilename));
            Assert.AreEqual(linesToWrite, CountFileLines(fullFilename));

            // we should append on re-open
            var subs = new[] {new EventProviderSubscription(TestLogger.Write, EventLevel.Verbose),};
            var config = new LogConfiguration(Path.GetFileNameWithoutExtension(logFilename), LogType.Text, subs)
                         {Directory = "."};
            logger = LogManager.CreateLogger(config);
            logger.SubscribeToEvents(TestLogger.Write, EventLevel.Verbose);
            const int moreLinesToWrite = 42;
            for (int i = 0; i < moreLinesToWrite; ++i)
            {
                TestLogger.Write.Int(i);
            }
            LogManager.DestroyLogger(logger);
            Assert.AreEqual(linesToWrite + moreLinesToWrite, CountFileLines(fullFilename));
            LogManager.Shutdown();
        }

        [Test]
        public void Timestamps()
        {
            LogManager.Start();
            var memoryLog = new MemoryLogger();

            memoryLog.SubscribeToEvents(TestLogger.Write, EventLevel.Verbose);
            memoryLog.FormatOptions &= ~TextLogFormatOptions.ProcessAndThreadData;
            // showing activity ID should be the default
            Assert.IsTrue(memoryLog.FormatOptions.HasFlag(TextLogFormatOptions.Timestamp));
            TestLogger.Write.String("normal timestamp");
            memoryLog.FormatOptions &= ~TextLogFormatOptions.Timestamp;
            memoryLog.FormatOptions |= TextLogFormatOptions.TimeOffset;
            Thread.Sleep(200); // wait a bit to ensure we get a decent offset time
            TestLogger.Write.String("with offset");
            memoryLog.FormatOptions &= ~TextLogFormatOptions.TimeOffset;
            TestLogger.Write.String("no time");
            memoryLog.Disabled = true;

            memoryLog.Stream.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(memoryLog.Stream))
            {
                string line;
                int lines = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    ++lines;
                    switch (lines)
                    {
                    case 1:
                    {
                        string[] split = line.Split(new[] {' '}, 2);
                        Assert.AreEqual(2, split.Length);
                        DateTime.ParseExact(split[0], EventStringFormatter.TimeFormat, CultureInfo.InvariantCulture);
                        Assert.IsTrue(0 ==
                                      string.Compare(split[1], "[v:TestLogger String] message=\"normal timestamp\"",
                                                     StringComparison.Ordinal));
                        break;
                    }
                    case 2:
                    {
                        string[] split = line.Split(new[] {' '}, 2);
                        Assert.AreEqual(2, split.Length);
                        float offset;
                        Assert.IsTrue(float.TryParse(split[0], NumberStyles.AllowDecimalPoint,
                                                     CultureInfo.InvariantCulture, out offset));
                        Assert.IsTrue(offset > 0 && offset < 100); // shouldn't take 100 seconds to test this :)
                        Assert.IsTrue(0 == string.Compare(split[1], "[v:TestLogger String] message=\"with offset\"",
                                                          StringComparison.Ordinal));
                        break;
                    }
                    case 3:
                    {
                        Assert.IsTrue(0 == string.Compare(line, "[v:TestLogger String] message=\"no time\"",
                                                          StringComparison.Ordinal));
                        break;
                    }
                    }
                }
                Assert.AreEqual(3, lines);
            }
            memoryLog.Dispose();
            LogManager.Shutdown();
        }

        [Test]
        public void TimeBasedRetention()
        {
            const string logName = "retention_time_test";

            LogManager.Start();
            var subscription = new[] {new EventProviderSubscription(TestLogger.Write, EventLevel.Verbose),};

            var config = new LogConfiguration(logName, LogType.Text, subscription)
                         {
                             RotationInterval = 86400,
                             Directory = ".",
                         };
            var files = new List<string>();
            var startTime = new DateTime(1776, 7, 4, 0, 0, 0, DateTimeKind.Utc);
            var endTime = startTime;
            const int rotations = 14;
            using (var log = new FileBackedLogger(config, startTime))
            {
                // XXX: this is cumbersome because of the way the file loggers are currently factored, needs fixing
                // in future refactor.
                var logger = (TextFileLogger)log.Logger;
                config.Logger = logger;
                files.Add(logger.Filename);
                for (var i = 0; i < rotations; ++i)
                {
                    TestLogger.Write.String("some string");

                    endTime = endTime + TimeSpan.FromSeconds(config.RotationInterval);
                    log.Rotate(endTime);
                    var newFile = logger.Filename;
                    Assert.IsFalse(files.Contains(newFile));
                    files.Add(logger.Filename);
                }
                TestLogger.Write.String("last string");
            }

            Assert.AreEqual(rotations + 1, files.Count);
            for (var i = 0; i < files.Count; ++i)
            {
                // XXX: this is a bit gross but for each file we need to set its creation time manually, in the normal
                // path we can trust wall time but that doesn't work here.
                var f = files[i];
                Assert.IsTrue(File.Exists(f));
                File.SetCreationTimeUtc(f, startTime + TimeSpan.FromSeconds(config.RotationInterval * i));
            }

            const int maxDays = rotations / 2;
            // XXX 2: have to re-create this because of the interplay between config + logger.
            config = new LogConfiguration(logName, LogType.Text, subscription)
                     {
                         RotationInterval = 86400,
                         Directory = ".",
                         MaximumAge = TimeSpan.FromDays(maxDays),
                     };
            startTime = endTime;
            using (var log = new FileBackedLogger(config, startTime))
            {
                var logger = (TextFileLogger)log.Logger;
                config.Logger = logger;
                // expect the first half of 'maxDays' files to be gone.
                for (var i = 0; i < files.Count; ++i)
                {
                    if (i < maxDays)
                    {
                        Assert.IsFalse(File.Exists(files[i]));
                    }
                    else
                    {
                        Assert.IsTrue(File.Exists(files[i]));
                    }
                }
            }
            LogManager.Shutdown();
        }

        [Test]
        public void SizeBasedRetention()
        {
            const string logName = "retention_size_test";
            const int maxSize = 5 * 1024 * 1024;
            const int stringBytes = 32 * 1024;
            var bigString = new string('.', stringBytes / 2); // 2 byte characters

            LogManager.Start();
            var subscription = new[] {new EventProviderSubscription(TestLogger.Write, EventLevel.Verbose),};

            var config = new LogConfiguration(logName, LogType.Text, subscription)
                         {
                             RotationInterval = LogManager.MinRotationInterval,
                             Directory = ".",
                         };
            var files = new List<string>();
            var startTime = new DateTime(1776, 7, 4, 0, 0, 0, DateTimeKind.Utc);
            var endTime = startTime;
            const int rotations = (maxSize / stringBytes) * 2;
            using (var log = new FileBackedLogger(config, startTime))
            {
                // XXX: this is cumbersome because of the way the file loggers are currently factored, needs fixing
                // in future refactor.
                var logger = (TextFileLogger)log.Logger;
                config.Logger = logger;
                files.Add(logger.Filename);
                for (var i = 0; i < rotations; ++i)
                {
                    TestLogger.Write.String(bigString);

                    endTime = endTime + TimeSpan.FromSeconds(config.RotationInterval);
                    log.Rotate(endTime);
                    var newFile = logger.Filename;
                    Assert.IsFalse(files.Contains(newFile));
                    files.Add(logger.Filename);
                }
                TestLogger.Write.String(bigString);
            }

            long actualSize = 0;
            Assert.AreEqual(rotations + 1, files.Count);
            for (var i = 0; i < files.Count; ++i)
            {
                // XXX: this is a bit gross but for each file we need to set its creation time manually, in the normal
                // path we can trust wall time but that doesn't work here.
                var f = files[i];
                var info = new FileInfo(f);
                Assert.IsTrue(info.Exists);
                actualSize += info.Length;
                File.SetCreationTimeUtc(f, startTime + TimeSpan.FromSeconds(config.RotationInterval * i));
            }

            // we want to determine the first file we expect to be gone by counting backwards over the size.
            long totalSize = 0;
            int firstRemaining = files.Count;
            for (var i = files.Count - 1; i >= 0; --i)
            {
                var info = new FileInfo(files[i]);
                totalSize += info.Length;
                if (totalSize > maxSize)
                {
                    firstRemaining = i + 1;
                    break;
                }
            }
            Assert.AreNotEqual(files.Count, firstRemaining);

            // XXX 2: have to re-create this because of the interplay between config + logger.
            config = new LogConfiguration(logName, LogType.Text, subscription)
                     {
                         RotationInterval = 86400,
                         Directory = ".",
                         MaximumSize = maxSize,
                     };
            startTime = endTime;
            using (var log = new FileBackedLogger(config, startTime))
            {
                var logger = (TextFileLogger)log.Logger;
                config.Logger = logger;
                // Using the first remaining file detected above to ensure files we expected to go away have.
                long remainingSize = 0;
                for (var i = 0; i < files.Count; ++i)
                {
                    var info = new FileInfo(files[i]);
                    if (i < firstRemaining)
                    {
                        Assert.IsFalse(info.Exists);
                    }
                    else
                    {
                        Assert.IsTrue(info.Exists);
                        remainingSize += info.Length;
                    }
                }

                Assert.IsTrue(remainingSize <= maxSize);
            }
            LogManager.Shutdown();
        }
    }
}