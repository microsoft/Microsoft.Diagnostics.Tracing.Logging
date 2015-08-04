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

namespace EtwLogTool
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Microsoft.Diagnostics.Tracing.Logging;
    using Microsoft.Diagnostics.Tracing.Logging.Reader;

    internal sealed class EventStatistics
    {
        private readonly Dictionary<Guid, ProviderStatistics> statistics = new Dictionary<Guid, ProviderStatistics>();

        public EventStatistics(ETWProcessor processor)
        {
            processor.EventProcessed += this.EventProcessed;
        }

        public void DumpStatistics()
        {
            foreach (var providerData in this.statistics.Values)
            {
                providerData.DumpStatistics();
            }
        }

        private void EventProcessed(ETWEvent ev)
        {
            ProviderStatistics providerData;
            if (!this.statistics.TryGetValue(ev.ProviderID, out providerData))
            {
                providerData = new ProviderStatistics(ev.ProviderID, ev.ProviderName);
                this.statistics[ev.ProviderID] = providerData;
            }

            providerData.ProcessEvent(ev);
        }

        private sealed class ProviderStatistics
        {
            private readonly Dictionary<ushort, ulong> eventCounts = new Dictionary<ushort, ulong>();
            private readonly Dictionary<ushort, string> eventNames = new Dictionary<ushort, string>();
            private readonly Dictionary<ushort, ulong> eventSizes = new Dictionary<ushort, ulong>();
            private readonly string name;
            private readonly Guid providerID;

            public ProviderStatistics(Guid providerID, string name)
            {
                this.providerID = providerID;
                this.name = name;
            }

            public void ProcessEvent(ETWEvent ev)
            {
                if (!this.eventNames.ContainsKey(ev.ID))
                {
                    this.eventNames[ev.ID] = ev.EventName;
                    this.eventCounts[ev.ID] = 0;
                    this.eventSizes[ev.ID] = 0;
                }

                ++this.eventCounts[ev.ID];

                ulong payloadSize = 0; // Note: best effort, not always right.
                if (ev.Parameters != null) // Parameters is null for parameter-less events.
                {
                    foreach (var value in ev.Parameters.Values)
                    {
                        if (value is string)
                        {
                            payloadSize += (ulong)((value as string).Length * 2); // always assume UTF-16.
                        }
                        else if (value is byte || value is sbyte)
                        {
                            payloadSize += 1;
                        }
                        else if (value is short || value is ushort)
                        {
                            payloadSize += 2;
                        }
                        else if (value is double || value is long || value is ulong)
                        {
                            payloadSize += 8;
                        }
                        else
                        {
                            payloadSize += 4; // int, uint, float, enums
                        }
                    }
                }

                this.eventSizes[ev.ID] += payloadSize;
            }

            public void DumpStatistics()
            {
                Console.WriteLine("Provider {0} ({1:n})", this.name, this.providerID);
                foreach (var eventID in from id in this.eventNames.Keys orderby id select id)
                {
                    Console.WriteLine("{0}: {1} events, {2} bytes/event", this.eventNames[eventID],
                                      this.eventCounts[eventID], this.eventSizes[eventID] / this.eventCounts[eventID]);
                }
            }
        }
    }
}