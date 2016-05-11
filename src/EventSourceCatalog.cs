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
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Diagnostics.Tracing;
    using System.Globalization;
    using System.Xml;

    public sealed partial class LogManager
    {
        #region Private
        private readonly Dictionary<EventSource, EventSourceInfo> eventSourceInfos =
            new Dictionary<EventSource, EventSourceInfo>();

        private readonly object eventSourceInfosLock = new object();

        /// <summary>
        /// Container class which encapsulates information about a specific Event
        /// </summary>
        internal sealed class EventInfo
        {
            public string[] Arguments;
            public string Name;
        }

        /// <summary>
        /// Container class which extracts information about a specific EventSource
        /// </summary>
        internal sealed class EventSourceInfo
        {
            private const string ArrayLengthArgumentSuffix = ".Length";
            private readonly Dictionary<int, EventInfo> eventIDs = new Dictionary<int, EventInfo>();

            /// <summary>
            /// ctor.
            /// </summary>
            /// <param name="source">EventSource object to instantiate from.</param>
            public EventSourceInfo(EventSource source)
            {
                this.Source = source;

                // DOM+XPath parsing for now, manifests should be very small and we shouldn't be doing this too frequently
                string manifestXML = EventSource.GenerateManifest(source.GetType(), "");
                var manifest = new XmlDocument();
                manifest.LoadXml(manifestXML);
                var namespaceMgr = new XmlNamespaceManager(manifest.NameTable);
                XmlElement root = manifest.DocumentElement; // instrumentationManifest
                namespaceMgr.AddNamespace("win", root.NamespaceURI);

                // keep track of event to template mapping (template name can differ from event name)
                var templateMapping = new Dictionary<string, EventInfo>(StringComparer.OrdinalIgnoreCase);

                // not verifying but we expect a single provider
                XmlNode provider = root.SelectSingleNode("//win:provider", namespaceMgr);
                this.Name = provider.Attributes["name"].Value;
                foreach (XmlNode ev in provider.SelectNodes("//win:events/win:event", namespaceMgr))
                {
                    // EventSource+TraceEvent provide for the following naming scheme:
                    // - Events with neither a task nor opcode will have a task generated for them automatically.
                    // - Events with named tasks and the default opcode have the name of the task. It is possible
                    //   as a result to have many events with the same name. We don't hedge against this today since
                    //   we key on ID.
                    // - Events with opcodes but not task names do not get generated task names, so we simply use
                    //   the event's ID as its name.

                    int eventID = int.Parse(ev.Attributes["value"].Value);
                    var taskName = ev.Attributes.GetNamedItem("task");
                    var opCodeName = ev.Attributes.GetNamedItem("opcode");

                    string eventName;
                    if (taskName != null && opCodeName != null)
                    {
                        int opCodeOffset = (opCodeName.Value.StartsWith("win:") ? 4 : 0);
                        eventName = string.Format("{0}/{1}", taskName.Value, opCodeName.Value.Substring(opCodeOffset));
                    }
                    else if (taskName != null)
                    {
                        eventName = taskName.Value;
                    }
                    else
                    {
                        eventName = eventID.ToString(CultureInfo.InvariantCulture);
                    }

                    var eventData = new EventInfo {Name = eventName};
                    this.eventIDs[eventID] = eventData;

                    if (ev.Attributes.GetNamedItem("template") != null)
                    {
                        templateMapping[ev.Attributes["template"].Value] = eventData;
                    }
                }

                // Each template has one or more 'data' tags whose attributes are the name of the argument and its
                // type. Since we only care about the name we ignore the type. For array arguments EventSource emits
                // two entries in the template, first a <foo>.Length entry specifying the length of the array,
                // followed by the actual array data itself (<foo>).  Since we're constructing this data only
                // for handling intra-application EventSource calls we don't need to care about the .Length
                // bit that comes in the manifest since it is specific to decoding ETW events.
                foreach (XmlNode template in provider.SelectNodes("//win:templates/win:template", namespaceMgr))
                {
                    string name = template.Attributes["tid"].Value;
                    // we want to throw right away if we somehow got a template for an unnamed event, that's a big
                    // contract breach.
                    EventInfo data = templateMapping[name];
                    XmlNodeList arguments = template.SelectNodes("win:data", namespaceMgr);

                    int numArgs = 0;
                    data.Arguments = new string[arguments.Count];
                    for (int i = 0; i < arguments.Count; ++i)
                    {
                        XmlNode node = arguments[i];
                        string dataName = node.Attributes["name"].Value;
                        if (!dataName.EndsWith(ArrayLengthArgumentSuffix, StringComparison.OrdinalIgnoreCase))
                        {
                            data.Arguments[numArgs] = dataName;
                            ++numArgs;
                        }
                    }
                }
            }

            public string Name { get; private set; }
            public EventSource Source { get; }

            public EventInfo this[int eventID] => this.eventIDs[eventID];
        }

        internal static EventSourceInfo GetEventSourceInfo(EventSource source)
        {
            lock (singleton.eventSourceInfosLock)
            {
                return singleton.eventSourceInfos[source];
            }
        }

        internal static EventSourceInfo GetEventSourceInfo(string name)
        {
            lock (singleton.eventSourceInfosLock)
            {
                foreach (var info in singleton.eventSourceInfos.Values)
                {
                    if (string.Equals(name, info.Source.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        return info;
                    }
                }
            }

            return null;
        }

        internal static EventSourceInfo GetEventSourceInfo(Guid providerID)
        {
            lock (singleton.eventSourceInfosLock)
            {
                foreach (var info in singleton.eventSourceInfos.Values)
                {
                    if (providerID == info.Source.Guid)
                    {
                        return info;
                    }
                }
            }

            return null;
        }
        #endregion

        #region EventListener
        /// <summary>
        /// Generates internal manifest data when new event sources are created and updates existing listeners based on configuration
        /// </summary>
        /// <param name="eventSource">The EventSource which was created</param>
        /// <remarks>
        /// When an EventListener is instantiated this function will be called once for each EventSource in the
        /// AppDomain by the EventSource constructor.
        /// </remarks>
        [SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0")]
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            // There is a possible race where we can get notified of the creation of our own internal logger during
            // construction of the log manager. The LogManager constructor helps handle this by explicitly calling
            // this function once it is safe to do so.
            if (eventSource is InternalLogger && InternalLogger.Write == null)
            {
                return;
            }

            lock (this.eventSourceInfosLock)
            {
                if (!this.eventSourceInfos.ContainsKey(eventSource))
                {
                    var updatedData = new EventSourceInfo(eventSource);
                    this.eventSourceInfos[eventSource] = updatedData;
                    InternalLogger.Write.NewEventSource(eventSource.Name, eventSource.Guid);
                }
                else
                {
                    return; // we should have already done the work for this source.
                }
            }
            lock (this.loggersLock)
            {
                foreach (var log in Configuration.Logs)
                {
                    log.UpdateForEventSource(eventSource);
                }
            }
        }

        /// <summary>
        /// Required by EventListener base class but this is a no-op
        /// </summary>
        protected override void OnEventWritten(EventWrittenEventArgs eventData) { }
        #endregion
    }
}