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

namespace Microsoft.Diagnostics.Tracing.Logging.Reader
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Represents an expirable composite event.
    /// </summary>
    public interface IExpirableCompositeEvent
    {
        /// <summary>
        /// The time when the event was last modified. Used to determine whether to expire an event.
        /// </summary>
        DateTime LastModified { get; }

        /// <summary>
        /// Whether the event is considered complete. A complete event has seen both its start and end events, and
        /// is typically held for only a brief time to ensure that no stray supplemental events are missed prior to
        /// being expired from the collection.
        /// </summary>
        bool IsComplete { get; }

        /// <summary>
        /// Process an ETWEvent for this composite event.
        /// </summary>
        /// <param name="ev">The event to process.</param>
        void ProcessEvent(ETWEvent ev);
    }

    /// <summary>
    /// Provides a collection which will automatically manage the expiration of "composite" events. These are generally
    /// larger events that are composed of many individual ETW events to represent them in their entirety. E.g. a CLR
    /// garbage collection has a start, stop, and other associated events.
    /// 
    /// This class will manage events in two different groups depending on whether they are complete or incomplete. In
    /// general greater tolerance in terms of expiration for incomplete events is recommended, whereas expiration for
    /// complete events may typically be performed within ~2 seconds of their last modification, depending on whether
    /// the standard ETW flush timer of 1 second is being used.
    /// 
    /// This class is NOT thread-safe.
    /// </summary>
    /// <typeparam name="TKey">The key to use for fast lookup of an event.</typeparam>
    /// <typeparam name="TValue">The value of the event.</typeparam>
    public sealed class ExpiringCompositeEventCollection<TKey, TValue>
        where TValue : IExpirableCompositeEvent, new()
    {
        public delegate void ExpiredEventHandler(TValue ev);

        private readonly ExpiredEventHandler completeExpiredEventHandler;
        private readonly Dictionary<TKey, TValue> completeRecords = new Dictionary<TKey, TValue>();
        private readonly ExpiredEventHandler incompleteExpiredEventHandler;
        private readonly Dictionary<TKey, TValue> incompleteRecords = new Dictionary<TKey, TValue>();
        private readonly TimeSpan maxCompleteAge;
        private readonly TimeSpan maxIncompleteAge;
        private DateTime oldestCompleteRecord = DateTime.MaxValue;
        private DateTime oldestIncompleteRecord = DateTime.MaxValue;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="maximumIncompleteRecordAge">
        /// Maximum age for incomplete records before expiration. Must be non-zero. Negative values are converted to
        /// positive values.
        /// </param>
        /// <param name="maximumCompleteRecordAge">
        /// Maximum age for complete records before expiration. Must be non-zero. Negative values are converted to
        /// positive values.
        /// </param>
        /// <param name="incompleteExpiredEventHandler">Method to handle expired incomplete records. Must not be null</param>
        /// <param name="completeExpiredEventHandler">Method to handle expired complete records. Must not be null</param>
        public ExpiringCompositeEventCollection(TimeSpan maximumIncompleteRecordAge, TimeSpan maximumCompleteRecordAge,
                                                ExpiredEventHandler incompleteExpiredEventHandler,
                                                ExpiredEventHandler completeExpiredEventHandler)
        {
            if (maximumIncompleteRecordAge == TimeSpan.Zero)
            {
                throw new ArgumentException("Timespan must be non-zero", "maximumIncompleteRecordAge");
            }

            if (maximumCompleteRecordAge == TimeSpan.Zero)
            {
                throw new ArgumentException("Timespan must be non-zero", "maximumCompleteRecordAge");
            }

            if (incompleteExpiredEventHandler == null)
            {
                throw new ArgumentNullException("incompleteExpiredEventHandler");
            }

            if (completeExpiredEventHandler == null)
            {
                throw new ArgumentNullException("completeExpiredEventHandler");
            }

            this.maxIncompleteAge = maximumIncompleteRecordAge.Duration();
            this.maxCompleteAge = maximumCompleteRecordAge.Duration();
            this.incompleteExpiredEventHandler = incompleteExpiredEventHandler;
            this.completeExpiredEventHandler = completeExpiredEventHandler;
        }

        /// <summary>
        /// Get the number of complete records currently queued.
        /// </summary>
        public int QueuedCompleteRecords
        {
            get { return this.completeRecords.Count; }
        }

        /// <summary>
        /// Get the number of incomplete records currently queued.
        /// </summary>
        public int QueuedIncompleteRecords
        {
            get { return this.incompleteRecords.Count; }
        }

        /// <summary>
        /// Method called to process an event for the given key. If the record does not exist yet it will be created.
        /// </summary>
        /// <param name="key">Key of the compositve event.</param>
        /// <param name="ev">Event to process.</param>
        public void ProcessEvent(TKey key, ETWEvent ev)
        {
            TValue val;
            if (this.incompleteRecords.TryGetValue(key, out val))
            {
                val.ProcessEvent(ev);
                if (val.IsComplete)
                {
                    this.incompleteRecords.Remove(key);
                    this.completeRecords.Add(key, val);
                }
            }
            else if (this.completeRecords.TryGetValue(key, out val))
            {
                val.ProcessEvent(ev);
            }
            else
            {
                val = new TValue();
                val.ProcessEvent(ev);
                if (val.IsComplete)
                {
                    this.completeRecords.Add(key, val);
                }
                else
                {
                    this.incompleteRecords.Add(key, val);
                }
            }

            // We make no effort here to see if this was previously our oldest value. It is conceivable (though
            // not particularly likely) that two separate records have identical ages. This may mean that we run
            // expiration more frequently than desirable, but the code should be both simpler and correct in all
            // cases as a result.
            if (val.IsComplete && val.LastModified < this.oldestCompleteRecord)
            {
                this.oldestCompleteRecord = val.LastModified;
            }
            else if (!val.IsComplete && val.LastModified < this.oldestIncompleteRecord)
            {
                this.oldestIncompleteRecord = val.LastModified;
            }

            this.Expire(ev.Timestamp);
        }

        /// <summary>
        /// Force expiration of records based on a given current time.
        /// </summary>
        /// <param name="latestEventTime">Time of the latest known event.</param>
        public void Expire(DateTime latestEventTime)
        {
            if (latestEventTime - this.oldestIncompleteRecord > this.maxIncompleteAge)
            {
                this.oldestIncompleteRecord = ExpireFromDictionary(this.incompleteRecords, this.maxIncompleteAge,
                                                                   latestEventTime, this.incompleteExpiredEventHandler);
            }
            if (latestEventTime - this.oldestCompleteRecord > this.maxCompleteAge)
            {
                this.oldestCompleteRecord = ExpireFromDictionary(this.completeRecords, this.maxCompleteAge,
                                                                 latestEventTime, this.completeExpiredEventHandler);
            }
        }

        /// <summary>
        /// Force all complete records to be flushed. Useful when a session has verifiably ended and you do not expect
        /// to see more data.
        /// </summary>
        public void FlushComplete()
        {
            foreach (var completedPairs in this.completeRecords)
            {
                this.completeExpiredEventHandler(completedPairs.Value);
            }
            this.completeRecords.Clear();
            this.oldestCompleteRecord = DateTime.MaxValue;
        }

        /// <summary>
        /// Attempt to retrieve a composite event that has not been completed/expired yet.
        /// </summary>
        /// <param name="key">The key to lookup.</param>
        /// <param name="value">Out parameter holding the value, if found.</param>
        /// <returns>True if the value was found, false otherwise.</returns>
        public bool TryGetValue(TKey key, out TValue value)
        {
            if (this.incompleteRecords.TryGetValue(key, out value))
            {
                return true;
            }
            if (this.completeRecords.TryGetValue(key, out value))
            {
                return true;
            }

            value = default(TValue);
            return false;
        }

        private static DateTime ExpireFromDictionary(IDictionary<TKey, TValue> dict, TimeSpan maxAge,
                                                     DateTime latestTime, ExpiredEventHandler handler)
        {
            var oldestUnexpired = DateTime.MaxValue;

            var removeList = new List<TKey>();
            foreach (var kvp in dict)
            {
                if (latestTime - kvp.Value.LastModified > maxAge)
                {
                    removeList.Add(kvp.Key);
                    handler(kvp.Value);
                }
                else if (oldestUnexpired > kvp.Value.LastModified)
                {
                    oldestUnexpired = kvp.Value.LastModified;
                }
            }
            foreach (var key in removeList)
            {
                dict.Remove(key);
            }

            return oldestUnexpired;
        }
    }
}