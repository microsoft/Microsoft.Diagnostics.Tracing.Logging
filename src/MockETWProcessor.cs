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
    using System.Collections.Concurrent;
    using System.Diagnostics.Tracing;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A mocked ETW processor allowing the user to simulate an ETW session with injected data. The processor also
    /// allows for subscribing to an in-process EventSource to ease creation of events when the provider is already
    /// available.
    /// Calling ProcessAsync spawns a Task for you which does the processing. You can thus treat the processor as
    /// 'single threaded' (call ProcessAsync and subsequently call StopProcessing) and the behavior is guaranteed to
    /// be synchronous and complete up to the last event submitted before calling StopProcessing as long as it all
    /// occurs on the same thread.
    /// Calling Process itself will still lock the current thread for the duration of the processing task.
    /// </summary>
    public sealed class MockETWProcessor : ETWProcessor
    {
        private readonly ConcurrentQueue<ETWEvent> injectedEvents = new ConcurrentQueue<ETWEvent>();
        private readonly object injectionLock = new object();
        private readonly AutoResetEvent messageEvent = new AutoResetEvent(false);
        private EventSourceInjector eventSourceListener;
        private Task processingTask;
        private bool stopInjecting;
        private int stopProcessing;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="sessionName">Name of session (used in start+end session events).</param>
        public MockETWProcessor(string sessionName)
        {
            if (sessionName == null)
            {
                throw new ArgumentNullException("sessionName");
            }

            this.CurrentSessionName = sessionName;
            this.eventSourceListener = new EventSourceInjector(this);
        }

        /// <summary>
        /// Process() will exit after processing all the events in queue and not block if 
        /// this flag is set to true
        /// </summary>
        public bool StopProcessingWhenQueueEmpty { get; set; }

        public override void Process()
        {
            this.stopInjecting = false;
            this.stopProcessing = 0;

            this.OnSessionStart(this.CurrentSessionName, DateTime.Now);
            long eventCount = 0;
            try
            {
                ETWEvent ev;
                do
                {
                    while (this.injectedEvents.TryDequeue(out ev))
                    {
                        this.OnEvent(ev);
                        ++eventCount;
                    }
                    if (this.StopProcessingWhenQueueEmpty)
                    {
                        break;
                    }
                    this.messageEvent.WaitOne();
                } while (0 == Interlocked.CompareExchange(ref this.stopProcessing, 1, 1));

                while (this.injectedEvents.TryDequeue(out ev))
                {
                    this.OnEvent(ev);
                    ++eventCount;
                }
            }
            finally
            {
                this.OnSessionEnd(this.CurrentSessionName, DateTime.Now, eventCount, 0, 0);
            }
        }

        /// <summary>
        /// Start a Task to handle event processing asynchronously. The task will be cleaned up automatically. The
        /// method will return only after the processing Task has begun execution and it is safe to inject new events.
        /// </summary>
        public void ProcessAsync()
        {
            this.processingTask = new Task(this.Process);
            this.processingTask.Start();
            while (this.processingTask.Status != TaskStatus.Running)
            {
                Thread.Sleep(10);
            }
        }

        public override void StopProcessing()
        {
            // Guarantee drain by first halting the injection of new events, and subsequently waiting for the processing
            // task to complete execution if we have one.
            if (this.stopProcessing == 0)
            {
                lock (this.injectionLock)
                {
                    this.stopInjecting = true;
                }
                this.stopProcessing = 1;
                this.messageEvent.Set();

                if (this.processingTask != null)
                {
                    this.processingTask.Wait();
                    this.processingTask.Dispose();
                    this.processingTask = null;
                }
            }
        }

        /// <summary>
        /// Inject an event into the processor.
        /// </summary>
        /// <param name="ev">Event to inject.</param>
        public void InjectEvent(ETWEvent ev)
        {
            if (ev == null)
            {
                throw new ArgumentNullException("ev");
            }

            lock (this.injectionLock)
            {
                if (!this.stopInjecting)
                {
                    this.injectedEvents.Enqueue(ev);
                }
            }
        }

        /// <summary>
        /// Subscribe to events from an EventSource provider.
        /// </summary>
        /// <param name="source">The event provider to subscribe to</param>
        /// <param name="minimumLevel">The minimum level of event severity to receive events for</param>
        public void SubscribeToEvents(EventSource source, EventLevel minimumLevel)
        {
            this.eventSourceListener.SubscribeToEvents(source, minimumLevel);
        }

        /// <summary>
        /// Subscribe to events from an EventSource provider.
        /// </summary>
        /// <param name="source">The event provider to subscribe to</param>
        /// <param name="minimumLevel">The minimum level of event severity to receive events for</param>
        /// <param name="keywords">Keywords (if any) to match against</param>
        public void SubscribeToEvents(EventSource source, EventLevel minimumLevel, EventKeywords keywords)
        {
            this.eventSourceListener.SubscribeToEvents(source, minimumLevel, keywords);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && this.eventSourceListener != null)
            {
                this.eventSourceListener.Dispose();
                this.eventSourceListener = null;
            }
            base.Dispose(disposing);
        }

        private sealed class EventSourceInjector : EventListenerDispatcher
        {
            private readonly MockETWProcessor parentProcessor;

            public EventSourceInjector(MockETWProcessor parentProcessor)
            {
                this.parentProcessor = parentProcessor;
            }

            public override void Write(ETWEvent ev)
            {
                this.parentProcessor.InjectEvent(ev);
            }

            protected override void Dispose(bool disposing) { }
        }
    }
}