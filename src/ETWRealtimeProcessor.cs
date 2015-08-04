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
    using System.Diagnostics.Tracing;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.Diagnostics.Tracing.Parsers;
    using Microsoft.Diagnostics.Tracing.Session;

    public sealed class ETWRealtimeProcessor : ETWProcessor
    {
        private readonly List<Tuple<Guid, EventLevel, ulong>> enabledProviders =
            new List<Tuple<Guid, EventLevel, ulong>>();

        private readonly bool reclaimSession;
        private readonly string sessionName;
        private TraceEventSession session;

        /// <summary>
        /// Creates a realtime ETW session processor. If the session already exists it will be "reclaimed" for use by
        /// this processor.
        /// </summary>
        /// <param name="sessionName">The name of the realtime session.</param>
        public ETWRealtimeProcessor(string sessionName)
            : this(sessionName, true) { }

        /// <summary>
        /// Creates a realtime ETW session processor. If the session should not be reclaimed an
        /// OperationCanceledException will be thrown if it already exists. Otherwise, the existing session will be
        /// reclaimed for use by this processor.
        /// </summary>
        /// <param name="sessionName">The name of the realtime session.</param>
        /// <param name="reclaimSession">Whether or not to reclaim the session.</param>
        public ETWRealtimeProcessor(string sessionName, bool reclaimSession)
        {
            if (string.IsNullOrEmpty(sessionName))
            {
                throw new ArgumentException("Session name must be provided", "sessionName");
            }

            this.sessionName = sessionName;
            this.reclaimSession = reclaimSession;
        }

        public void SubscribeToProvider(Guid providerID, EventLevel minimumSeverity = EventLevel.Informational,
                                        long keywords = 0)
        {
            if (providerID == Guid.Empty)
            {
                throw new ArgumentException("Must specify a valid provider ID", "providerID");
            }

            this.enabledProviders.Add(new Tuple<Guid, EventLevel, ulong>(providerID, minimumSeverity, (ulong)keywords));
            if (this.session != null)
            {
                if (providerID == KernelTraceEventParser.ProviderGuid)
                {
                    // No support for stack capture flags. Consider adding in future.
                    this.session.EnableKernelProvider((KernelTraceEventParser.Keywords)keywords);
                }
                else
                {
                    this.session.EnableProvider(providerID, (TraceEventLevel)minimumSeverity, (ulong)keywords);
                }
            }
        }

        public override void Process()
        {
            if (TraceEventSession.GetActiveSession(this.sessionName) != null)
            {
                if (!this.reclaimSession)
                {
                    throw new OperationCanceledException("Session already exists with name " + this.sessionName);
                }

                ETLFileLogger.CloseDuplicateTraceSession(this.sessionName);
            }

            this.CurrentSessionName = this.sessionName;
            this.session = new TraceEventSession(this.sessionName, null);
            foreach (var subscription in this.enabledProviders)
            {
                Guid providerID = subscription.Item1;
                var minimumSeverity = (TraceEventLevel)subscription.Item2;
                ulong keywords = subscription.Item3;

                if (providerID == ClrTraceEventParser.ProviderGuid)
                {
                    this.ProcessEventTypes |= EventTypes.Clr;
                }
                else if (providerID == KernelTraceEventParser.ProviderGuid)
                {
                    this.ProcessEventTypes |= EventTypes.Kernel;
                }
                else
                {
                    this.ProcessEventTypes |= EventTypes.EventSource;
                }

                this.session.EnableProvider(providerID, minimumSeverity, keywords);
            }

            // Ordering seems to matter here! If we create the source before there's something happening in the
            // session we end up with invalid timestamps and no events get emitted. Keep this here.
            this.TraceEventSource = new ETWTraceEventSource(this.sessionName, TraceEventSourceType.Session);
            this.ProcessEvents();
        }

        public override void StopProcessing()
        {
            this.session.Stop();

            base.StopProcessing();
        }

        /// <summary>
        /// Creates a Task with no return value that hosts the event processing loop. Ensures the session is started
        /// prior to returning.
        /// </summary>
        /// <returns>A Task to handle the event processing.</returns>
        public Task CreateProcessingTask()
        {
            var task = new Task(this.Process);
            task.Start();

            int waitTime = 5000;
            while (waitTime > 0 && TraceEventSession.GetActiveSession(this.sessionName) == null)
            {
                if (task.IsFaulted)
                {
                    throw new OperationCanceledException("Unable to start realtime session.", task.Exception);
                }
                Thread.Sleep(100);
                waitTime -= 100;
            }

            if (TraceEventSession.GetActiveSession(this.sessionName) == null)
            {
                throw new OperationCanceledException("Unable to start realtime session.");
            }

            return task;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (this.session != null && disposing)
            {
                this.StopProcessing();
                this.session.Dispose();
                this.session = null;
            }
        }
    }
}