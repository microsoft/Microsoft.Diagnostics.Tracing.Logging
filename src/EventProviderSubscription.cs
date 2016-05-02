// The MIT License (MIT)
// 
// Copyright (c) 2015-2016 Microsoft
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
    using System.Diagnostics.Tracing;
    using System.Globalization;
    using System.IO;

    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    [JsonObject(MemberSerialization = MemberSerialization.OptIn), JsonConverter(typeof(Converter))]
    public sealed class EventProviderSubscription
    {
        public const EventLevel DefaultMinimumLevel = EventLevel.Informational;
        public const EventKeywords DefaultKeywords = EventKeywords.None;

        private EventProviderSubscription(string name, EventSource source, Guid providerID, EventLevel minimuLevel,
                                          EventKeywords keywords)
        {
            if (source != null)
            {
                this.UpdateSource(source);
            }
            else if (providerID != Guid.Empty)
            {
                this.ProviderID = providerID;
                if (this.Source == null)
                {
                    var eventSource = LogManager.FindEventSource(providerID);
                    if (eventSource != null)
                    {
                        this.UpdateSource(eventSource);
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(name))
            {
                this.Name = name;
                if (this.Source == null && this.ProviderID == Guid.Empty)
                {
                    var eventSource = LogManager.FindEventSource(name);
                    if (eventSource != null)
                    {
                        this.UpdateSource(eventSource);
                    }
                }
            }
            else
            {
                throw new ArgumentException(
                    "Must provider at least one of name / EventSource / ProviderID for subscription.");
            }

            this.MinimumLevel = minimuLevel;
            this.Keywords = keywords;
        }

        /// <summary>
        /// Construct a new EventSourceSubscription object based on a provided EventSource.
        /// </summary>
        /// <param name="source">Source object to use.</param>
        public EventProviderSubscription(EventSource source)
            : this(null, source, Guid.Empty, DefaultMinimumLevel, DefaultKeywords) { }

        /// <summary>
        /// Construct a new EventSourceSubscription object based on a provided EventSource.
        /// </summary>
        /// <param name="source">Source object to use.</param>
        /// <param name="minimumLevel">Minimum severity level of events to subscribe to.</param>
        public EventProviderSubscription(EventSource source, EventLevel minimumLevel)
            : this(null, source, source.Guid, minimumLevel, EventKeywords.None) { }

        /// <summary>
        /// Construct a new EventSourceSubscription object based on a provided EventSource.
        /// </summary>
        /// <param name="source">Source object to use.</param>
        /// <param name="minimumLevel">Minimum severity level of events to subscribe to.</param>
        /// <param name="keywords">Keywords to match events against.</param>
        public EventProviderSubscription(EventSource source, EventLevel minimumLevel, EventKeywords keywords)
            : this(null, source, source.Guid, minimumLevel, keywords) { }

        /// <summary>
        /// Construct a new EventSourceSubscription object based on a provided EventSource.
        /// </summary>
        /// <param name="source">Source object to use.</param>
        /// <param name="minimumLevel">Minimum severity level of events to subscribe to.</param>
        /// <param name="keywords">Keywords to match events against.</param>
        public EventProviderSubscription(EventSource source, EventLevel minimumLevel, ulong keywords)
            : this(null, source, source.Guid, minimumLevel, (EventKeywords)keywords) { }

        /// <summary>
        /// Construct a new EventSourceSubscription object based on an ETW provider GUID.
        /// </summary>
        /// <param name="providerID">GUID of the desired event provider.</param>
        public EventProviderSubscription(Guid providerID)
            : this(null, null, providerID, DefaultMinimumLevel, DefaultKeywords) { }

        /// <summary>
        /// Construct a new EventSourceSubscription object based on an ETW provider GUID.
        /// </summary>
        /// <param name="providerID">GUID of the desired event provider.</param>
        /// <param name="minimumLevel">Minimum severity level of events to subscribe to.</param>
        public EventProviderSubscription(Guid providerID, EventLevel minimumLevel)
            : this(null, null, providerID, minimumLevel, EventKeywords.None) { }

        /// <summary>
        /// Construct a new EventSourceSubscription object based on an ETW provider GUID.
        /// </summary>
        /// <param name="providerID">GUID of the desired event provider.</param>
        /// <param name="minimumLevel">Minimum severity level of events to subscribe to.</param>
        /// <param name="keywords">Keywords to match events against.</param>
        public EventProviderSubscription(Guid providerID, EventLevel minimumLevel, EventKeywords keywords)
            : this(null, null, providerID, minimumLevel, keywords) { }

        /// <summary>
        /// Construct a new EventSourceSubscription object based on an ETW provider GUID.
        /// </summary>
        /// <param name="providerID">GUID of the desired event provider.</param>
        /// <param name="minimumLevel">Minimum severity level of events to subscribe to.</param>
        /// <param name="keywords">Keywords to match events against.</param>
        public EventProviderSubscription(Guid providerID, EventLevel minimumLevel, ulong keywords)
            : this(null, null, providerID, minimumLevel, (EventKeywords)keywords) { }

        /// <summary>
        /// Construct a new EventSourceSubscription object based on the name of an EventSource provider.
        /// </summary>
        /// <param name="name">Name of the EventSource provider.</param>
        public EventProviderSubscription(string name)
            : this(name, null, Guid.Empty, DefaultMinimumLevel, DefaultKeywords) { }

        /// <summary>
        /// Construct a new EventSourceSubscription object based on the name of an EventSource provider.
        /// </summary>
        /// <param name="name">Name of the EventSource provider.</param>
        /// <param name="minimumLevel">Minimum severity level of events to subscribe to.</param>
        public EventProviderSubscription(string name, EventLevel minimumLevel)
            : this(name, null, Guid.Empty, minimumLevel, EventKeywords.None) { }

        /// <summary>
        /// Construct a new EventSourceSubscription object based on the name of an EventSource provider.
        /// </summary>
        /// <param name="name">Name of the EventSource provider.</param>
        /// <param name="minimumLevel">Minimum severity level of events to subscribe to.</param>
        /// <param name="keywords">Keywords to match events against.</param>
        public EventProviderSubscription(string name, EventLevel minimumLevel, EventKeywords keywords)
            : this(name, null, Guid.Empty, minimumLevel, keywords) { }

        /// <summary>
        /// Construct a new EventSourceSubscription object based on the name of an EventSource provider.
        /// </summary>
        /// <param name="name">Name of the EventSource provider.</param>
        /// <param name="minimumLevel">Minimum severity level of events to subscribe to.</param>
        /// <param name="keywords">Keywords to match events against.</param>
        public EventProviderSubscription(string name, EventLevel minimumLevel, ulong keywords)
            : this(name, null, Guid.Empty, minimumLevel, (EventKeywords)keywords) { }

        /// <summary>
        /// Keywords to match.
        /// </summary>
        public EventKeywords Keywords { get; set; }

        /// <summary>
        /// Minimum event level to record.
        /// </summary>
        public EventLevel MinimumLevel { get; set; }

        /// <summary>
        /// EventSource to subscribe to. May be null if ProviderID is provided.
        /// </summary>
        public EventSource Source { get; private set; }

        /// <summary>
        /// Guid to subscribe to. May be empty if Source is provided.
        /// </summary>
        public Guid ProviderID { get; private set; }

        /// <summary>
        /// Name of the EventSource to subscribe to. May be empty if ProviderID is provided.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// True if the subscription is considered "resolved" (i.e. if either the EventSource is known or a GUID is provided).
        /// </summary>
        public bool IsResolved => this.Source != null || this.ProviderID != Guid.Empty;

        internal void UpdateSource(EventSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (this.Source != null)
            {
                throw new InvalidOperationException("Multiple updates to source are not allowed.");
            }

            this.Source = source;
            this.Name = source.Name;
            this.ProviderID = source.Guid;
        }

        public override bool Equals(object obj)
        {
            var other = obj as EventProviderSubscription;
            return other != null && this.Equals(other);
        }

        private bool Equals(EventProviderSubscription other)
        {
            return this.ProviderID.Equals(other.ProviderID) &&
                   string.Equals(this.Name, other.Name, StringComparison.OrdinalIgnoreCase);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = this.ProviderID.GetHashCode();
                hashCode = (hashCode * 397) ^ (this.Name?.ToLower(CultureInfo.InvariantCulture).GetHashCode() ?? 0);
                return hashCode;
            }
        }

        public override string ToString()
        {
            var serializer = new JsonSerializer();
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, this);
                return writer.ToString();
            }
        }

        private sealed class Converter : JsonConverter
        {
            private const string NameProperty = "name";
            private const string ProviderIDProperty = "providerID";
            private const string MinimumLevelProperty = "minimumLevel";
            private const string KeywordsProperty = "keywords";

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var subscription = value as EventProviderSubscription;
                if (subscription == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                writer.WriteStartObject();
                if (subscription.Name != null)
                {
                    writer.WritePropertyName(NameProperty);
                    writer.WriteValue(subscription.Name);
                }
                else
                {
                    if (subscription.ProviderID == Guid.Empty)
                    {
                        throw new ArgumentException("Source without name or valid ProviderID provided.", nameof(value));
                    }

                    writer.WritePropertyName(ProviderIDProperty);
                    writer.WriteValue(subscription.ProviderID);
                }
                if (subscription.MinimumLevel != DefaultMinimumLevel)
                {
                    writer.WritePropertyName(MinimumLevelProperty);
                    writer.WriteValue(subscription.MinimumLevel.ToString());
                }
                if (subscription.Keywords != DefaultKeywords)
                {
                    writer.WritePropertyName(KeywordsProperty);
                    writer.WriteValue("0x" + ((ulong)subscription.Keywords).ToString("x"));
                }
                writer.WriteEndObject();
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
                                            JsonSerializer serializer)
            {
                var jObject = JObject.Load(reader);
                JToken token;
                string name = null;
                Guid providerID = Guid.Empty;
                EventLevel minimumLevel = DefaultMinimumLevel;
                EventKeywords keywords = DefaultKeywords;
                if (jObject.TryGetValue(NameProperty, StringComparison.OrdinalIgnoreCase, out token))
                {
                    name = token.Value<string>();
                }
                if (jObject.TryGetValue(ProviderIDProperty, StringComparison.OrdinalIgnoreCase, out token))
                {
                    providerID = token.Value<Guid>();
                }
                if (jObject.TryGetValue(MinimumLevelProperty, StringComparison.OrdinalIgnoreCase, out token))
                {
                    minimumLevel = (EventLevel)Enum.Parse(typeof(EventLevel), token.Value<string>(), true);
                }
                if (jObject.TryGetValue(KeywordsProperty, StringComparison.OrdinalIgnoreCase, out token))
                {
                    var value = token.Value<string>().Trim();
                    if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        value = value.Substring(2);
                    }
                    keywords = (EventKeywords)ulong.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }

                return new EventProviderSubscription(name, null, providerID, minimumLevel, keywords);
            }

            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(EventProviderSubscription);
            }
        }
    }
}