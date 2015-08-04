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
    using System.Collections.Specialized;
    using System.Diagnostics.Tracing;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.Diagnostics.Tracing.Logging.Reader;

    using NUnit.Framework;

    [TestFixture]
    public sealed class MockETWProcessorTests
    {
        [SetUp]
        public void SetUp()
        {
            LogManager.Start();
        }

        [TearDown]
        public void TearDown()
        {
            LogManager.Shutdown();
        }

        [Test]
        public void MockEtwProcessorStopsProcessingOnQueueEmptyAndStopProcessingWhenQueueEmptyFlagIsSet()
        {
            const string anySessionName = "session";
            int eventsProcessed = 0;
            int numEventsInjectedAfterProcess = 10;
            int numEventsInjectedBeforeProcess = 10;
            using (var processor = new MockETWProcessor(anySessionName))
            {
                processor.StopProcessingWhenQueueEmpty = true;
                for (int i = 0; i < numEventsInjectedBeforeProcess; i++)
                {
                    processor.InjectEvent(new ETWEvent(new DateTime(), new Guid(), "", ushort.MaxValue, "", new byte(),
                                                       EventKeywords.AuditFailure, EventLevel.Critical,
                                                       EventOpcode.DataCollectionStart, new Guid(), 1, 1,
                                                       new OrderedDictionary()));
                }
                processor.EventProcessed += ev => eventsProcessed++;
                processor.Process();
                for (int i = 0; i < numEventsInjectedAfterProcess; i++)
                {
                    processor.InjectEvent(new ETWEvent(new DateTime(), new Guid(), "", ushort.MaxValue, "", new byte(),
                                                       EventKeywords.AuditFailure, EventLevel.Critical,
                                                       EventOpcode.DataCollectionStart, new Guid(), 1, 1,
                                                       new OrderedDictionary()));
                }
            }
            Assert.AreEqual(numEventsInjectedBeforeProcess, eventsProcessed);
        }

        [Test]
        public void MockETWProcessorThrowsArgumentNullExceptionForNullSessionName()
        {
            try
            {
                new MockETWProcessor(null);
                Assert.Fail();
            }
            catch (ArgumentNullException) { }
        }

        [Test]
        public void MockETWProcessorTriggersEndEventWithSessionNameAndEventCount()
        {
            const string anySessionName = "session";
            const long anyEventCount = 42;
            bool eventTriggered = false;

            using (var processor = new MockETWProcessor(anySessionName))
            {
                processor.EventProcessed += ev => { };
                processor.SessionEnd += (name, end, count, lostCount, unreadableCount) =>
                                        {
                                            Assert.AreEqual(anySessionName, name);
                                            Assert.AreEqual(anyEventCount, count);
                                            eventTriggered = true;
                                        };
                processor.ProcessAsync();
                for (int i = 0; i < anyEventCount; ++i)
                {
                    processor.InjectEvent(new ETWEvent(DateTime.Now, Guid.Empty, string.Empty, 0, string.Empty, 0,
                                                       EventKeywords.None,
                                                       EventLevel.Verbose, EventOpcode.Info, Guid.Empty, 0, 0, null));
                }
                processor.StopProcessing();
                Assert.IsTrue(eventTriggered);
            }
        }

        [Test]
        public void MockETWProcessorTriggersEventProcessedWithSameETWEventObjectPassedToInjectEvent()
        {
            const string anySessionName = "session";
            var anyEvent = new ETWEvent(DateTime.Now, Guid.Empty, string.Empty, 0, string.Empty, 0, EventKeywords.None,
                                        EventLevel.Verbose, EventOpcode.Info, Guid.Empty, 0, 0, null);
            bool eventTriggered = false;

            using (var processor = new MockETWProcessor(anySessionName))
            {
                processor.EventProcessed += ev =>
                                            {
                                                Assert.AreSame(anyEvent, ev);
                                                eventTriggered = true;
                                            };
                processor.ProcessAsync();
                processor.InjectEvent(anyEvent);
                processor.StopProcessing();
                Assert.IsTrue(eventTriggered);
            }
        }

        [Test]
        public void MockETWProcessorTriggersForSubscribedEventSources()
        {
            const string anySessionName = "session";
            bool eventTriggered = false;

            using (var processor = new MockETWProcessor(anySessionName))
            {
                processor.EventProcessed += ev => eventTriggered = true;
                processor.SubscribeToEvents(TestLogger.Write, EventLevel.Verbose);

                processor.ProcessAsync();
                TestLogger.Write.String("Hi");
                processor.StopProcessing();
                Assert.IsTrue(eventTriggered);
            }
        }

        [Test]
        public void MockETWProcessorTriggersStartEventWithSessionName()
        {
            const string anySessionName = "session";
            bool eventTriggered = false;

            using (var processor = new MockETWProcessor(anySessionName))
            {
                processor.SessionStart += (name, start) =>
                                          {
                                              Assert.AreEqual(anySessionName, name);
                                              eventTriggered = true;
                                          };
                processor.ProcessAsync();
                processor.StopProcessing();
                Assert.IsTrue(eventTriggered);
            }
        }

        [Test]
        public void MockETWProcessWaitsForTaskToEnd()
        {
            const string anySessionName = "session";
            bool eventTriggered = false;

            using (var processor = new MockETWProcessor(anySessionName))
            {
                processor.EventProcessed += ev => eventTriggered = true;
                processor.SubscribeToEvents(TestLogger.Write, EventLevel.Verbose);

                var t = new Task(processor.Process);
                t.Start();
                while (t.Status != TaskStatus.Running) // we must wait for the task to actually start
                {
                    Thread.Sleep(10);
                }
                TestLogger.Write.String("Hi");
                processor.StopProcessing();
                t.Wait();
                Assert.IsTrue(eventTriggered);
            }
        }
    }
}