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
    using System.Collections.Specialized;
    using System.Diagnostics.Tracing;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.Diagnostics.Tracing.Logging.Reader;
    using Microsoft.Diagnostics.Tracing.Session;

    using NUnit.Framework;

    [TestFixture]
    public class ReaderTests
    {
        private static void ValidateEventArgs(ETWEvent entry)
        {
            if (entry.ProviderID != TestLogger.Write.Guid)
            {
                return;
            }

            switch (entry.ID)
            {
            case 1:
                Assert.IsTrue(entry.EventName == "String");
                Assert.AreEqual(EventLevel.Verbose, entry.Level);
                Assert.AreEqual(EventKeywords.None, (EventKeywords)entry.Keywords);
                Assert.AreEqual(1, entry.Parameters.Count);
                Assert.IsNotNull(entry.Parameter<string>(0));
                Assert.IsTrue("a string draws near" == entry.Parameters["message"] as string);
                break;
            case 2:
                Assert.IsTrue(entry.EventName == "Int");
                Assert.AreEqual(EventLevel.Informational, entry.Level);
                Assert.AreEqual((int)EventOpcode.Info, entry.OpCode);
                Assert.AreEqual(1, entry.Parameters.Count);
                Assert.AreEqual(42, entry.Parameter<int>("message"));
                break;
            case 3:
                Assert.IsTrue(entry.EventName == "First");
                Assert.AreEqual(EventLevel.Informational, entry.Level);
                Assert.AreEqual(TestLogger.Keywords.FirstKeyword, (EventKeywords)entry.Keywords);
                Assert.AreEqual(1, entry.Parameters.Count);
                Assert.IsNotNull(entry.Parameter<string>(0));
                Assert.IsTrue("base" == entry.Parameter<string>("message"));
                break;
            case 5:
                Assert.IsTrue(entry.EventName == "OnlyTask/Extension"); // combo of task+opcode
                Assert.AreEqual(EventLevel.Informational, entry.Level);
                Assert.AreEqual((int)EventOpcode.Extension, entry.OpCode);
                Assert.AreEqual(5, entry.Parameters.Count);
                Assert.AreEqual(0x86, entry.Parameter<byte>(0));
                Assert.AreEqual(0x86, entry.Parameter<byte>("wind"));
                Assert.AreEqual(75309, entry.Parameter<int>(1));
                Assert.AreEqual(75309, entry.Parameter<int>("water"));
                Assert.IsTrue("Jenny" == entry.Parameter<string>(2));
                Assert.IsTrue("Jenny" == entry.Parameter<string>("earth"));
                Assert.AreEqual(867.5309, entry.Parameter<double>(3));
                Assert.AreEqual(867.5309, entry.Parameter<double>("fire"));
                Assert.AreEqual(true, entry.Parameter<bool>(4));
                Assert.AreEqual(true, entry.Parameter<bool>("heart"));
                break;
            default:
                Assert.Fail("Entry with unknown ID!" + entry.ID);
                break;
            }
        }

        private static void WriteTestEvents()
        {
            TestLogger.Write.String("a string draws near");
            TestLogger.Write.Int(42);
            TestLogger.Write.First("base");
            TestLogger.Write.Element(0x86, 75309, "Jenny", 867.5309, true);
        }

        private static string WriteTestFile(string logFilename)
        {
            LogManager.Start();
            LogManager.SetConfiguration(null);
            LogManager.AllowEtwLogging = AllowEtwLoggingValues.Enabled;
            string fullFilename = Path.Combine(LogManager.DefaultDirectory, logFilename);
            try
            {
                File.Delete(fullFilename);
            }
            catch (DirectoryNotFoundException) { }

            string sessionName = Path.GetFileNameWithoutExtension(logFilename);
            IEventLogger logger = LogManager.CreateETWLogger(sessionName, ".");
            logger.SubscribeToEvents(TestLogger.Write.Guid, EventLevel.Verbose);
            while (TraceEventSession.GetActiveSession(ETLFileLogger.SessionPrefix + sessionName) == null)
            {
                // Ensure session starts...
                Thread.Sleep(100);
            }
            // Even after the session is listed it seemingly isn't "ready", periodic test failures seem to occur without
            // an enforced pause here.
            Thread.Sleep(100);
            WriteTestEvents();
            LogManager.DestroyLogger(logger);
            LogManager.Shutdown();

            Assert.IsTrue(File.Exists(fullFilename));

            return fullFilename;
        }

        private enum FakeSignedEnum
        {
            FakeValue = 867
        }

        private enum FakeUnsignedEnum : uint
        {
            FakeValue = 5309
        }

        [Test]
        public void CanReadEventsFromRealtimeSession()
        {
            using (var reader = new ETWRealtimeProcessor("testRealtimeSession"))
            {
                reader.SubscribeToProvider(TestLogger.Write.Guid, EventLevel.Verbose);
                reader.EventProcessed += delegate(ETWEvent entry)
                                         {
                                             ValidateEventArgs(entry);
                                             if (reader.Count == 4)
                                             {
                                                 reader.StopProcessing();
                                             }
                                         };
                var processorTask = reader.CreateProcessingTask();
                WriteTestEvents();
                processorTask.Wait();

                Assert.IsTrue(processorTask.IsCompleted);
                Assert.AreEqual(4, reader.Count);
                Assert.AreEqual(0, reader.UnreadableEvents);
            }
        }

        [Test]
        public void CanReadEventsWrittenToFile()
        {
            DateTime beforeStartTimestamp = DateTime.Now;
            string fullFilename = WriteTestFile("testReader.etl");
            DateTime afterEndTimestamp = DateTime.Now;

            var reader = new ETWFileProcessor(fullFilename);
            reader.EventProcessed += ValidateEventArgs;
            reader.Process();
            Assert.AreEqual(4, reader.Count);

            reader.ProcessEventTypes = EventTypes.All;
            reader.Process();
            Assert.AreEqual(5, reader.Count); // We also get the kernel event at the head of the file

            Assert.IsTrue(beforeStartTimestamp <= reader.StartTime);
            Assert.IsTrue(afterEndTimestamp >= reader.EndTime);
        }

        [Test]
        public void CanReuseFileProcessorForNewFiles()
        {
            DateTime beforeStartTimestamp = DateTime.Now;
            string firstFile = WriteTestFile("testMultipleFiles1.etl");
            DateTime afterFirstFileTimestamp = DateTime.Now;

            var reader = new ETWFileProcessor(firstFile);
            reader.EventProcessed += ValidateEventArgs;
            reader.Process();
            Assert.AreEqual(4, reader.Count);
            Assert.IsTrue(beforeStartTimestamp <= reader.StartTime);
            Assert.IsTrue(afterFirstFileTimestamp >= reader.EndTime);

            string secondFile = WriteTestFile("testMultipleFiles2.etl");
            DateTime afterSecondFileTimestamp = DateTime.Now;
            reader.SetFile(secondFile);
            File.Delete(firstFile);
            reader.Process();
            Assert.AreEqual(4, reader.Count);
            Assert.IsTrue(afterFirstFileTimestamp <= reader.StartTime);
            Assert.IsTrue(afterSecondFileTimestamp >= reader.EndTime);
        }

        [Test]
        public void CanSafelyCastEnumsToIntegerTypes()
        {
            var ev = new ETWEvent(DateTime.Now, Guid.NewGuid(), "fakeProvider", 1, "fakeEvent", 1,
                                  EventKeywords.None, EventLevel.Informational, EventOpcode.Info, Guid.Empty, 867,
                                  4309,
                                  new OrderedDictionary
                                  {
                                      {"signed", FakeSignedEnum.FakeValue},
                                      {"unsigned", FakeUnsignedEnum.FakeValue}
                                  });

            Assert.AreEqual((int)FakeSignedEnum.FakeValue, ev.Parameter<int>("signed"));
            Assert.AreEqual((uint)FakeSignedEnum.FakeValue, ev.Parameter<uint>("signed"));

            Assert.AreEqual((int)FakeUnsignedEnum.FakeValue, ev.Parameter<int>("unsigned"));
            Assert.AreEqual((uint)FakeUnsignedEnum.FakeValue, ev.Parameter<uint>("unsigned"));
        }

        [Test]
        public void CanSerializeAndDeserializeEventsProcessedInFiles()
        {
            string fullFilename = WriteTestFile("testSerialize.etl");

            var reader = new ETWFileProcessor(fullFilename);
            reader.EventProcessed += ValidateEventArgs;
            reader.EventProcessed +=
                ev =>
                {
                    var xmlSerializer = ETWEvent.GetXmlSerializer();
                    var jsonSerializer = ETWEvent.GetJsonSerializer();
                    var json = ev.ToJsonString();
                    var xml = ev.ToXmlString();

                    var jsonEv = jsonSerializer.ReadObject(new MemoryStream(Encoding.UTF8.GetBytes(json))) as ETWEvent;
                    // Note that we lose some precision in serialization... just check down to ms granularity by
                    // stripping the additional bits off.
                    var msResolutionTimestamp =
                        new DateTime(ev.Timestamp.Ticks - (ev.Timestamp.Ticks % TimeSpan.TicksPerMillisecond),
                                     ev.Timestamp.Kind);
                    Assert.AreEqual(msResolutionTimestamp, jsonEv.Timestamp);
                    Assert.AreEqual(ev.ProviderID, jsonEv.ProviderID);
                    Assert.AreEqual(ev.ProviderName, jsonEv.ProviderName);
                    Assert.AreEqual(ev.ActivityID, jsonEv.ActivityID);
                    Assert.AreEqual(ev.ID, jsonEv.ID);
                    Assert.AreEqual(ev.EventName, jsonEv.EventName);
                    Assert.AreEqual(ev.Version, jsonEv.Version);
                    Assert.AreEqual(ev.Level, jsonEv.Level);
                    Assert.AreEqual(ev.OpCode, jsonEv.OpCode);
                    Assert.AreEqual(ev.Keywords, jsonEv.Keywords);
                    Assert.AreEqual(ev.ThreadID, jsonEv.ThreadID);
                    Assert.AreEqual(ev.ProcessID, jsonEv.ProcessID);
                    Assert.AreEqual(ev.Parameters.Count, jsonEv.Parameters.Count);
                    ValidateEventArgs(ev);

                    // When testing XML deserialize just check a couple fields to ensure data was copied correctly,
                    // if JSON serialization/deserialization worked we expect no hiccups here.
                    var xmlEv = xmlSerializer.ReadObject(new MemoryStream(Encoding.UTF8.GetBytes(xml))) as ETWEvent;
                    Assert.AreEqual(ev.ProviderID, xmlEv.ProviderID);
                    Assert.AreEqual(ev.ThreadID, xmlEv.ThreadID);
                    ValidateEventArgs(ev);
                };
            reader.Process();
            Assert.AreEqual(4, reader.Count);
        }

        [Test]
        public void ChangingETLFilenameCausesManifestToBeEmittedInNewFile()
        {
            const string prefix = "rotateFile";
            var files = new[] {prefix + "1.etl", prefix + "2.etl", prefix + "3.etl"};
            var resultFiles = new List<string>();

            LogManager.Start();
            LogManager.SetConfiguration(null);
            LogManager.AllowEtwLogging = AllowEtwLoggingValues.Enabled;
            string sessionName = null;
            ETLFileLogger logger = null;
            foreach (var logFilename in files)
            {
                string fullFilename = Path.Combine(LogManager.DefaultDirectory, logFilename);
                try
                {
                    File.Delete(fullFilename);
                }
                catch (DirectoryNotFoundException) { }

                if (sessionName == null)
                {
                    sessionName = Path.GetFileNameWithoutExtension(logFilename);
                }
                if (logger == null)
                {
                    logger = LogManager.CreateETWLogger(sessionName, ".") as ETLFileLogger;
                    logger.SubscribeToEvents(TestLogger.Write.Guid, EventLevel.Verbose);
                    while (TraceEventSession.GetActiveSession(ETLFileLogger.SessionPrefix + sessionName) == null)
                    {
                        // Ensure session starts...
                        Thread.Sleep(100);
                    }
                    // Even after the session is listed it seemingly isn't "ready", periodic test failures seem to occur without
                    // an enforced pause here.
                    Thread.Sleep(100);
                }
                else
                {
                    logger.Filename = fullFilename;
                }

                WriteTestEvents();
                Assert.IsTrue(File.Exists(fullFilename));

                resultFiles.Add(fullFilename);
            }

            LogManager.DestroyLogger(logger);
            LogManager.Shutdown();

            foreach (var fullFilename in resultFiles)
            {
                // It is critical to ensure we create a new reader for each file so that any history across files is
                // not preserved
                using (var reader = new ETWFileProcessor(fullFilename))
                {
                    reader.EventProcessed += ValidateEventArgs;
                    reader.Process();
                    Assert.AreEqual(4, reader.Count);
                }
            }
        }

        [Test]
        public void CreatingDuplicateRealtimeSessionTaskResultsInFaultedTaskIfSessionReclaimIsOff()
        {
            const string duplicateSessionName = "testDuplicateRealtimeSession";

            using (var reader1 = new ETWRealtimeProcessor(duplicateSessionName, true))
            {
                reader1.SubscribeToProvider(TestLogger.Write.Guid, EventLevel.Verbose);
                var t1 = reader1.CreateProcessingTask();
                Assert.AreEqual(TaskStatus.Running, t1.Status);

                using (var reader2 = new ETWRealtimeProcessor(duplicateSessionName, false))
                {
                    reader2.SubscribeToProvider(TestLogger.Write.Guid, EventLevel.Verbose);
                    try
                    {
                        var t2 = reader2.CreateProcessingTask();
                        t2.Wait(); // expect this to fail
                    }
                    catch (AggregateException ex)
                    {
                        Assert.AreEqual(typeof(OperationCanceledException), ex.InnerException.GetType());
                    }
                }
                Assert.AreEqual(TaskStatus.Running, t1.Status);
                reader1.StopProcessing();
                t1.Wait();
            }
        }

        [Test]
        public void FileReaderConstructorThrowsArgumentExceptionForNullOrEmptyFilenames()
        {
            try
            {
                new ETWFileProcessor((string)null);
                Assert.Fail();
            }
            catch (ArgumentException) { }

            try
            {
                new ETWFileProcessor((ICollection<string>)null);
                Assert.Fail();
            }
            catch (ArgumentException) { }

            try
            {
                new ETWFileProcessor(string.Empty);
                Assert.Fail();
            }
            catch (ArgumentException) { }

            try
            {
                new ETWFileProcessor(new string[] {null});
                Assert.Fail();
            }
            catch (ArgumentException) { }

            try
            {
                new ETWFileProcessor(new[] {string.Empty});
                Assert.Fail();
            }
            catch (ArgumentException) { }
        }

        [Test]
        public void FileReaderConstructorThrowsFileNotFoundExceptionForNonexistentFiles()
        {
            const string nonexistentFile = "this file will never be found. NEVER I TELL YOU! NEEEEEVVVVAAARRRRR";
            try
            {
                new ETWFileProcessor(nonexistentFile);
                Assert.Fail();
            }
            catch (FileNotFoundException) { }

            try
            {
                new ETWFileProcessor(new[] {nonexistentFile});
                Assert.Fail();
            }
            catch (FileNotFoundException) { }
        }

        [Test]
        public void FileReaderConstructorWithNoParametersIsValid()
        {
            new ETWFileProcessor();
        }

        [Test]
        public void FileReaderProcessWithNoFilesThrowsOperationCanceledException()
        {
            var p = new ETWFileProcessor();
            try
            {
                p.Process();
                Assert.Fail();
            }
            catch (OperationCanceledException) { }
        }
    }
}