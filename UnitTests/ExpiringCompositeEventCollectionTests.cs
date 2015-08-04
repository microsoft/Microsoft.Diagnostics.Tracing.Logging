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
    using System.Diagnostics.Tracing;

    using Microsoft.Diagnostics.Tracing.Logging.Reader;

    using NUnit.Framework;

    internal sealed class TestCompositeEvent : IExpirableCompositeEvent
    {
        private bool haveEnd;
        private bool haveStart;
        public DateTime LastModified { get; set; }

        public bool IsComplete
        {
            get { return this.haveStart && this.haveEnd; }
        }

        public void ProcessEvent(ETWEvent ev)
        {
            if (ev.ID == ExpiringCompositeEventCollectionTests.StartEventID)
            {
                this.haveStart = true;
            }
            else if (ev.ID == ExpiringCompositeEventCollectionTests.EndEventID)
            {
                this.haveEnd = true;
            }

            this.LastModified = ev.Timestamp;
        }
    }

    [TestFixture]
    public sealed class ExpiringCompositeEventCollectionTests
    {
        public const ushort StartEventID = 1;
        public const ushort EndEventID = 2;
        public const ushort AnyEventID = 3;

        private void IncompleteExpiredEventHandler(TestCompositeEvent ce)
        {
            Assert.IsFalse(ce.IsComplete);
        }

        private void CompleteExpiredEventHandler(TestCompositeEvent ce)
        {
            Assert.IsTrue(ce.IsComplete);
        }

        private static ETWEvent CreateStartEvent(DateTime timestamp)
        {
            return new ETWEvent(timestamp, Guid.Empty, string.Empty, StartEventID, string.Empty, 0, EventKeywords.None,
                                EventLevel.Verbose, EventOpcode.Info, Guid.Empty, 0, 0, null);
        }

        private static ETWEvent CreateEndEvent(DateTime timestamp)
        {
            return new ETWEvent(timestamp, Guid.Empty, string.Empty, EndEventID, string.Empty, 0, EventKeywords.None,
                                EventLevel.Verbose, EventOpcode.Info, Guid.Empty, 0, 0, null);
        }

        private static ETWEvent CreateAnyEvent(DateTime timestamp)
        {
            return new ETWEvent(timestamp, Guid.Empty, string.Empty, AnyEventID, string.Empty, 0, EventKeywords.None,
                                EventLevel.Verbose, EventOpcode.Info, Guid.Empty, 0, 0, null);
        }

        [Test]
        public void CanCreateBasicCollection()
        {
            var anyTimespan = new TimeSpan(0, 0, 1);
            new ExpiringCompositeEventCollection<string, TestCompositeEvent>(anyTimespan, anyTimespan,
                                                                             this.IncompleteExpiredEventHandler,
                                                                             this.CompleteExpiredEventHandler);
        }

        [Test]
        public void CompleteRecordsAreExpiredWhenEventsOccurBeyondMaxRecordAge()
        {
            var maxIncomplete = new TimeSpan(0, 0, 5);
            var maxComplete = new TimeSpan(0, 0, 1);

            int expiredRecords = 0;
            var coll = new ExpiringCompositeEventCollection<int, TestCompositeEvent>(maxIncomplete, maxComplete,
                                                                                     this.IncompleteExpiredEventHandler,
                                                                                     ec =>
                                                                                     {
                                                                                         ++expiredRecords;
                                                                                         Assert.IsTrue(ec.IsComplete);
                                                                                     });

            var start = DateTime.Now;
            coll.ProcessEvent(1, CreateStartEvent(start));
            coll.ProcessEvent(1, CreateEndEvent(start));
            coll.ProcessEvent(2, CreateAnyEvent(start + new TimeSpan(0, 0, 2)));

            Assert.AreEqual(1, expiredRecords);
        }

        [Test]
        public void CompleteRecordsAreNotExpiredIfEventsOccurAtExactlyMaxRecordAge()
        {
            var maxIncomplete = new TimeSpan(0, 0, 5);
            var maxComplete = new TimeSpan(0, 0, 1);

            int expiredRecords = 0;
            var coll = new ExpiringCompositeEventCollection<int, TestCompositeEvent>(maxIncomplete, maxComplete,
                                                                                     this.IncompleteExpiredEventHandler,
                                                                                     ec =>
                                                                                     {
                                                                                         ++expiredRecords;
                                                                                         Assert.IsTrue(ec.IsComplete);
                                                                                     });

            var start = DateTime.Now;
            coll.ProcessEvent(1, CreateStartEvent(start));
            coll.ProcessEvent(1, CreateEndEvent(start));
            coll.ProcessEvent(2, CreateAnyEvent(start + maxComplete));

            Assert.AreEqual(0, expiredRecords);
        }

        [Test]
        public void ConstructorDoesNotAllowNullHandlers()
        {
            var anyTimespan = new TimeSpan(0, 0, 1);
            try
            {
                new ExpiringCompositeEventCollection<string, TestCompositeEvent>(anyTimespan, anyTimespan,
                                                                                 null,
                                                                                 this.CompleteExpiredEventHandler);
                Assert.Fail();
            }
            catch (ArgumentNullException) { }
            try
            {
                new ExpiringCompositeEventCollection<string, TestCompositeEvent>(anyTimespan, anyTimespan,
                                                                                 this.IncompleteExpiredEventHandler,
                                                                                 null);
                Assert.Fail();
            }
            catch (ArgumentNullException) { }
        }

        [Test]
        public void ConstructorDoesNotAllowZeroTimespansForAges()
        {
            var anyTimespan = new TimeSpan(0, 0, 1);
            try
            {
                new ExpiringCompositeEventCollection<string, TestCompositeEvent>(TimeSpan.Zero, anyTimespan,
                                                                                 this.IncompleteExpiredEventHandler,
                                                                                 this.CompleteExpiredEventHandler);
                Assert.Fail();
            }
            catch (ArgumentException) { }
            try
            {
                new ExpiringCompositeEventCollection<string, TestCompositeEvent>(anyTimespan, TimeSpan.Zero,
                                                                                 this.IncompleteExpiredEventHandler,
                                                                                 this.CompleteExpiredEventHandler);
                Assert.Fail();
            }
            catch (ArgumentException) { }
        }

        [Test]
        public void ExpiredRecordsAreNotFoundByTryGetValue()
        {
            var maxIncomplete = new TimeSpan(0, 0, 5);
            var maxComplete = new TimeSpan(0, 0, 1);

            int expiredRecords = 0;
            var coll = new ExpiringCompositeEventCollection<int, TestCompositeEvent>(maxIncomplete, maxComplete,
                                                                                     ec => { ++expiredRecords; },
                                                                                     ec =>
                                                                                     {
                                                                                         ++expiredRecords;
                                                                                         Assert.IsTrue(ec.IsComplete);
                                                                                     });

            var start = DateTime.Now;
            coll.ProcessEvent(1, CreateStartEvent(start));
            coll.ProcessEvent(1, CreateEndEvent(start));
            coll.Expire(start + maxComplete + new TimeSpan(0, 0, 1));
            Assert.AreEqual(1, expiredRecords);
            TestCompositeEvent val;
            Assert.IsFalse(coll.TryGetValue(1, out val));

            coll.ProcessEvent(2, CreateAnyEvent(start));
            coll.Expire(start + maxIncomplete + new TimeSpan(0, 0, 1));
            Assert.AreEqual(2, expiredRecords);
            Assert.IsFalse(coll.TryGetValue(2, out val));
        }

        [Test]
        public void FlushCausesCompleteRecordsToBeExpiredExpiration()
        {
            int expiredComplete = 0;
            var coll = new ExpiringCompositeEventCollection<int, TestCompositeEvent>(new TimeSpan(0, 0, 5),
                                                                                     new TimeSpan(0, 0, 5),
                                                                                     ec => { },
                                                                                     ec =>
                                                                                     {
                                                                                         ++expiredComplete;
                                                                                         Assert.IsTrue(ec.IsComplete);
                                                                                     });

            var start = DateTime.Now;
            coll.ProcessEvent(1, CreateStartEvent(start));
            coll.ProcessEvent(1, CreateEndEvent(start));
            Assert.AreEqual(0, expiredComplete);

            coll.FlushComplete();
            Assert.AreEqual(1, expiredComplete);
        }

        [Test]
        public void IncompleteRecordsAreExpiredWhenEventsOccurBeyondMaxRecordAge()
        {
            var maxIncomplete = new TimeSpan(0, 0, 5);
            var maxComplete = new TimeSpan(0, 0, 1);

            int expiredRecords = 0;
            var coll = new ExpiringCompositeEventCollection<int, TestCompositeEvent>(maxIncomplete, maxComplete,
                                                                                     ec => { ++expiredRecords; },
                                                                                     this.CompleteExpiredEventHandler);

            var start = DateTime.Now;
            coll.ProcessEvent(1, CreateStartEvent(start));
            coll.ProcessEvent(2, CreateAnyEvent(start + new TimeSpan(0, 0, 6)));

            Assert.AreEqual(1, expiredRecords);
        }

        [Test]
        public void IncompleteRecordsAreNotExpiredIfEventsOccurAtExactlyMaxRecordAge()
        {
            var maxIncomplete = new TimeSpan(0, 0, 5);
            var maxComplete = new TimeSpan(0, 0, 1);

            int expiredRecords = 0;
            var coll = new ExpiringCompositeEventCollection<int, TestCompositeEvent>(maxIncomplete, maxComplete,
                                                                                     ec => { ++expiredRecords; },
                                                                                     this.CompleteExpiredEventHandler);

            var start = DateTime.Now;
            coll.ProcessEvent(1, CreateStartEvent(start));
            coll.ProcessEvent(2, CreateAnyEvent(start + maxIncomplete));

            Assert.AreEqual(0, expiredRecords);
        }

        [Test]
        public void ManualExpirationCausesRecordsToExpire()
        {
            var maxIncomplete = new TimeSpan(0, 0, 5);
            var maxComplete = new TimeSpan(0, 0, 1);

            int expiredIncomplete = 0;
            int expiredComplete = 0;
            var coll = new ExpiringCompositeEventCollection<int, TestCompositeEvent>(maxIncomplete, maxComplete,
                                                                                     ec => { ++expiredIncomplete; },
                                                                                     ec =>
                                                                                     {
                                                                                         ++expiredComplete;
                                                                                         Assert.IsTrue(ec.IsComplete);
                                                                                     });

            var start = DateTime.Now;
            coll.ProcessEvent(1, CreateStartEvent(start));
            coll.ProcessEvent(1, CreateEndEvent(start));
            coll.ProcessEvent(2, CreateAnyEvent(start));

            coll.Expire(start);
            Assert.AreEqual(0, expiredIncomplete);
            Assert.AreEqual(0, expiredComplete);

            coll.Expire(start + new TimeSpan(0, 0, 2));
            Assert.AreEqual(0, expiredIncomplete);
            Assert.AreEqual(1, expiredComplete);

            coll.Expire(start + new TimeSpan(0, 0, 6));
            Assert.AreEqual(1, expiredIncomplete);
            Assert.AreEqual(1, expiredComplete);
        }

        [Test]
        public void NegativeExpirationValuesAreTreatedAsPositive()
        {
            var maxIncomplete = new TimeSpan(0, 0, -5);
            Assert.IsTrue(0 > maxIncomplete.TotalSeconds);
            var maxComplete = new TimeSpan(0, 0, -1);
            Assert.IsTrue(0 > maxComplete.TotalSeconds);

            int expiredIncomplete = 0;
            int expiredComplete = 0;
            var coll = new ExpiringCompositeEventCollection<int, TestCompositeEvent>(maxIncomplete, maxComplete,
                                                                                     ec => { ++expiredIncomplete; },
                                                                                     ec =>
                                                                                     {
                                                                                         ++expiredComplete;
                                                                                         Assert.IsTrue(ec.IsComplete);
                                                                                     });

            var start = DateTime.Now;
            coll.ProcessEvent(1, CreateStartEvent(start));
            coll.ProcessEvent(1, CreateEndEvent(start));
            coll.ProcessEvent(2, CreateAnyEvent(start));

            coll.Expire(start);
            Assert.AreEqual(0, expiredIncomplete);
            Assert.AreEqual(0, expiredComplete);

            coll.Expire(start + new TimeSpan(0, 0, 2));
            Assert.AreEqual(0, expiredIncomplete);
            Assert.AreEqual(1, expiredComplete);

            coll.Expire(start + new TimeSpan(0, 0, 6));
            Assert.AreEqual(1, expiredIncomplete);
            Assert.AreEqual(1, expiredComplete);
        }

        [Test]
        public void RecordsAreFoundByTryGetValue()
        {
            var maxIncomplete = new TimeSpan(0, 0, 5);
            var maxComplete = new TimeSpan(0, 0, 1);
            var coll = new ExpiringCompositeEventCollection<int, TestCompositeEvent>(maxIncomplete, maxComplete,
                                                                                     this.IncompleteExpiredEventHandler,
                                                                                     this.CompleteExpiredEventHandler);

            var start = DateTime.Now;
            coll.ProcessEvent(1, CreateStartEvent(start));
            TestCompositeEvent val;
            Assert.IsTrue(coll.TryGetValue(1, out val));
            Assert.IsNotNull(val);
            coll.ProcessEvent(1, CreateEndEvent(start));
            TestCompositeEvent val2;
            Assert.IsTrue(coll.TryGetValue(1, out val2));
            Assert.AreSame(val, val2);
        }
    }
}