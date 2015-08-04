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
    using System.Collections;
    using System.Collections.Specialized;
    using System.Diagnostics.Tracing;
    using System.IO;
    using System.Runtime.Serialization;
    using System.Runtime.Serialization.Json;
    using System.Text;

    using Newtonsoft.Json;

    /// <summary>
    /// Represents an individual ETW event.
    /// </summary>
    /// <remarks>
    /// Supports serialization via use of DataContract and associated attributes.
    /// </remarks>
    [DataContract]
    public sealed class ETWEvent : ICloneable
    {
        /// <summary>
        /// Bit-mask of 0 or more keywords categorizing the event. Note that only user keywords (the first 44 bits)
        /// are provided.
        /// </summary>
        public const long UserKeywordsMask = 0xfffffffffff; // first 44 bits.

        [IgnoreDataMember]
        private bool isFromSerializedData;

        [IgnoreDataMember]
        private EventData sourceData;

        [IgnoreDataMember]
        private OrderedDictionary sourceEventParameters; // deserialized on demand.

        public ETWEvent(DateTime timestamp, Guid providerGuid, string providerName, ushort eventId,
                        string eventName, byte eventVersion, EventKeywords keywords, EventLevel level,
                        EventOpcode opcode, Guid activityID, int processID, int threadID, OrderedDictionary parameters)
            : this(timestamp, providerGuid, providerName, eventId, eventName, eventVersion, keywords, level,
                   opcode, activityID, Guid.Empty, processID, threadID, parameters) { }

        public ETWEvent(DateTime timestamp, Guid providerGuid, string providerName, ushort eventId,
                        string eventName, byte eventVersion, EventKeywords keywords, EventLevel level,
                        EventOpcode opcode, Guid activityID, Guid relatedActivityID, int processID, int threadID,
                        OrderedDictionary parameters)
        {
            this.SourceEvent = null;
            this.isFromSerializedData = false;

            this.sourceData = new EventData();
            this.Timestamp = timestamp;
            this.ProviderID = providerGuid;
            this.ProviderName = providerName;
            this.ActivityID = activityID;
            this.RelatedActivityID = relatedActivityID;
            this.ID = eventId;
            this.EventName = eventName;
            this.Version = eventVersion;
            this.Level = level;
            this.OpCode = (byte)opcode;
            this.Keywords = (long)keywords;
            this.Parameters = parameters;
            this.ProcessID = processID;
            this.ThreadID = threadID;
        }

        internal ETWEvent(string filename, TraceEvent ev)
        {
            // null filename is valid for realtime sessions.
            if (ev == null)
            {
                throw new ArgumentNullException("ev");
            }

            this.sourceData = null;
            this.isFromSerializedData = false;

            this.SourceEvent = ev;
            this.SourceFilename = filename;
        }

        [JsonConstructor]
        public ETWEvent()
        {
            this.sourceData = new EventData();
            this.SourceEvent = null;
            this.isFromSerializedData = true;
        }

        [IgnoreDataMember]
        /// <summary>
        /// TraceEvent source object for this wrapped data. Useful for access to events with unknown data formats.
        /// </summary>
        public TraceEvent SourceEvent { get; }

        /// <summary>
        /// Name of the file containing this event.
        /// </summary>
        [IgnoreDataMember]
        public string SourceFilename { get; private set; }

        /// <summary>
        /// Time at which the event occured.
        /// </summary>
        [DataMember(Order = 1)]
        public DateTime Timestamp
        {
            get
            {
                if (this.SourceEvent != null)
                {
                    return this.SourceEvent.TimeStamp;
                }

                return this.sourceData.Timestamp;
            }
            set
            {
                this.CheckPropertySet();
                this.sourceData.Timestamp = value;
            }
        }

        /// <summary>
        /// GUID of the ETW provider which created the event.
        /// </summary>
        [DataMember(EmitDefaultValue = false, Order = 2)]
        public Guid ProviderID
        {
            get
            {
                if (this.SourceEvent != null)
                {
                    return this.SourceEvent.ProviderGuid;
                }

                return this.sourceData.ProviderID;
            }
            set
            {
                this.CheckPropertySet();
                this.sourceData.ProviderID = value;
            }
        }

        /// <summary>
        /// Name of the ETW provider which created the event.
        /// </summary>
        [DataMember(Order = 3)]
        public string ProviderName
        {
            get
            {
                if (this.SourceEvent != null)
                {
                    return this.SourceEvent.ProviderName;
                }

                return this.sourceData.ProviderName;
            }
            set
            {
                this.CheckPropertySet();
                this.sourceData.ProviderName = value;
            }
        }

        /// <summary>
        /// GUID of the activity of the thread which emitted the event.
        /// </summary>
        [DataMember(EmitDefaultValue = false, Order = 4)]
        public Guid ActivityID
        {
            get
            {
                if (this.SourceEvent != null)
                {
                    return this.SourceEvent.ActivityID;
                }

                return this.sourceData.ActivityID;
            }
            set
            {
                this.CheckPropertySet();
                this.sourceData.ActivityID = value;
            }
        }

        /// <summary>
        /// GUID of the related activity of the thread which emitted the event.
        /// </summary>
        [DataMember(EmitDefaultValue = false, Order = 5)]
        public Guid RelatedActivityID
        {
            get
            {
                if (this.SourceEvent != null)
                {
                    return this.SourceEvent.RelatedActivityID;
                }

                return this.sourceData.RelatedActivityID;
            }
            set
            {
                this.CheckPropertySet();
                this.sourceData.RelatedActivityID = value;
            }
        }

        /// <summary>
        /// ID of the event.
        /// </summary>
        [DataMember(Order = 6)]
        public ushort ID
        {
            get
            {
                if (this.SourceEvent != null)
                {
                    return (ushort)this.SourceEvent.ID;
                }

                return this.sourceData.ID;
            }
            set
            {
                this.CheckPropertySet();
                this.sourceData.ID = value;
            }
        }

        /// <summary>
        /// Name of the event.
        /// </summary>
        [DataMember(Order = 7)]
        public string EventName
        {
            get
            {
                if (this.SourceEvent != null)
                {
                    return this.SourceEvent.EventName;
                }

                return this.sourceData.EventName;
            }
            set
            {
                this.CheckPropertySet();
                this.sourceData.EventName = value;
            }
        }

        /// <summary>
        /// Version of the event.
        /// </summary>
        [DataMember(EmitDefaultValue = false, Order = 8)]
        public byte Version
        {
            get
            {
                if (this.SourceEvent != null)
                {
                    return (byte)this.SourceEvent.Version;
                }

                return this.sourceData.Version;
            }
            set
            {
                this.CheckPropertySet();
                this.sourceData.Version = value;
            }
        }

        /// <summary>
        /// Severity level of the event.
        /// </summary>
        [DataMember(Order = 9)]
        public EventLevel Level
        {
            get
            {
                if (this.SourceEvent != null)
                {
                    return (EventLevel)this.SourceEvent.Level;
                }

                return this.sourceData.Level;
            }
            set
            {
                this.CheckPropertySet();
                this.sourceData.Level = value;
            }
        }

        /// <summary>
        /// Operation code of the event.
        /// </summary>
        [DataMember(EmitDefaultValue = false, Order = 10)]
        public byte OpCode
        {
            get
            {
                if (this.SourceEvent != null)
                {
                    return (byte)this.SourceEvent.Opcode;
                }

                return this.sourceData.OpCode;
            }
            set
            {
                this.CheckPropertySet();
                this.sourceData.OpCode = value;
            }
        }

        [DataMember(EmitDefaultValue = false, Order = 11)]
        public long Keywords
        {
            get
            {
                if (this.SourceEvent != null)
                {
                    return ((long)this.SourceEvent.Keywords) & UserKeywordsMask;
                }

                return this.sourceData.Keywords;
            }
            set
            {
                this.CheckPropertySet();
                this.sourceData.Keywords = value;
            }
        }

        /// <summary>
        /// ID of the thread which emitted the event.
        /// </summary>
        [DataMember(Order = 12)]
        public int ThreadID
        {
            get
            {
                if (this.SourceEvent != null)
                {
                    return this.SourceEvent.ThreadID;
                }

                return this.sourceData.ThreadID;
            }
            set
            {
                this.CheckPropertySet();
                this.sourceData.ThreadID = value;
            }
        }

        /// <summary>
        /// ID of the process which emitted the event.
        /// </summary>
        [DataMember(Order = 13)]
        public int ProcessID
        {
            get
            {
                if (this.SourceEvent != null)
                {
                    return this.SourceEvent.ProcessID;
                }

                return this.sourceData.ProcessID;
            }
            set
            {
                this.CheckPropertySet();
                this.sourceData.ProcessID = value;
            }
        }

        /// <summary>
        /// Parameters for the event (may be null).
        /// </summary>
        [DataMember(EmitDefaultValue = false, Order = 14)]
        public OrderedDictionary Parameters
        {
            get
            {
                if (this.SourceEvent != null)
                {
                    // We want to only unbundle this on demand since it can be very expensive.
                    if (this.sourceEventParameters == null && this.SourceEvent.PayloadNames.Length > 0)
                    {
                        this.sourceEventParameters = new OrderedDictionary(this.SourceEvent.PayloadNames.Length);
                        for (int i = 0; i < this.SourceEvent.PayloadNames.Length; ++i)
                        {
                            this.sourceEventParameters.Add(this.SourceEvent.PayloadNames[i],
                                                           this.SourceEvent.PayloadValue(i));
                        }
                    }

                    return this.sourceEventParameters;
                }

                return this.sourceData.Parameters;
            }
            set
            {
                this.CheckPropertySet();
                this.sourceData.Parameters = value;
            }
        }

        public object Clone()
        {
            return this.Clone(null);
        }

        /// <summary>
        /// Convert an EventLevel enum to a single character suitable for logging/printing.
        /// </summary>
        /// <param name="level">The EventLevel.</param>
        /// <returns>A character representation of the level.</returns>
        public static char EventLevelToChar(EventLevel level)
        {
            switch (level)
            {
            case EventLevel.Verbose:
                return 'v';
            case EventLevel.Informational:
                return 'i';
            case EventLevel.Warning:
                return 'w';
            case EventLevel.Error:
                return 'e';
            case EventLevel.LogAlways: // folding these together intentionally
            case EventLevel.Critical:
            default:
                return 'c';
            }
        }

        /// <summary>
        /// Convert a character representation of an event level to the EventLevel enum.
        /// </summary>
        /// <param name="characterLevel">A character representation of the level.</param>
        /// <returns>The EventLevel.</returns>
        public static EventLevel CharToEventLevel(char characterLevel)
        {
            switch (characterLevel)
            {
            case 'v':
                return EventLevel.Verbose;
            case 'i':
                return EventLevel.Informational;
            case 'w':
                return EventLevel.Warning;
            case 'e':
                return EventLevel.Error;
            case 'a':
            case 'c':
                return EventLevel.Critical;
            default:
                throw new ArgumentException("Unknown level", "characterLevel");
            }
        }

        public TValue Parameter<TValue>(int ordinal)
        {
            // If we have a TraceEvent object, the caller may get better performance by never using Parameters above,
            // and simply asking for each desired parameter by ordinal.
            if (this.SourceEvent != null && this.sourceEventParameters == null)
            {
                return (TValue)this.SourceEvent.PayloadValue(ordinal);
            }
            return this.CastParameter<TValue>(this.Parameters[ordinal]);
        }

        public TValue Parameter<TValue>(string name)
        {
            return this.CastParameter<TValue>(this.Parameters[name]);
        }

        // DataContract serialization is "type-lossy" for integers, which means a user expecting a uint might get an int
        // or vice versa. We handle casting these around by interpreting the types, casting them as needed (unboxing)
        // back into a new box object with the desired type, then returning the unboxed value of the boxed object.
        // This is as awful as it sounds.
        // For uses where we were never serialized, if the caller asks for a cast and that isn't the actual object type,
        // we allow the cast to fail as it normally would.
        private TValue CastParameter<TValue>(object parameter)
        {
            // For enums we need to allow casting to/from integer types to let folks match the behavior of serialized
            // data better.
            if (!this.isFromSerializedData && !parameter.GetType().IsEnum)
            {
                return (TValue)parameter;
            }

            if (parameter is TValue)
            {
                return (TValue)parameter;
            }

            // We endeavor here to cover all possibilities where a larger-than-needed value would be used to hold a
            // smaller value. The behavior does generally seem to be "if I don't know what this is, make it an int"
            // unless the value overflows, so we assume for non-overflowable-stuff (8/16 bit) we only need to go up to
            // int, not long, with int being our first and best guess for all integer-type values.
            Type desiredType = typeof(TValue);
            object cast = null;

            // For enums to be cast reasonably we get their underlying type.
            var parameterType = parameter.GetType();
            if (parameterType.IsEnum)
            {
                parameterType = Enum.GetUnderlyingType(parameterType);
            }

            // Because of the enum handling this can now be true where it was not before.
            if (parameterType == desiredType)
            {
                return (TValue)parameter;
            }

            if (desiredType == typeof(sbyte))
            {
                if (parameterType == typeof(byte))
                {
                    cast = (sbyte)((byte)(parameter));
                }
                else if (parameterType == typeof(int))
                {
                    cast = (sbyte)((int)(parameter));
                }
                else if (parameterType == typeof(uint))
                {
                    cast = (sbyte)((uint)(parameter));
                }
                else if (parameterType == typeof(short))
                {
                    cast = (sbyte)((short)(parameter));
                }
                else if (parameterType == typeof(ushort))
                {
                    cast = (sbyte)((ushort)(parameter));
                }
            }
            if (desiredType == typeof(byte))
            {
                if (parameterType == typeof(sbyte))
                {
                    cast = (byte)((sbyte)(parameter));
                }
                else if (parameterType == typeof(int))
                {
                    cast = (byte)((int)(parameter));
                }
                else if (parameterType == typeof(uint))
                {
                    cast = (byte)((uint)(parameter));
                }
                else if (parameterType == typeof(short))
                {
                    cast = (byte)((short)(parameter));
                }
                else if (parameterType == typeof(ushort))
                {
                    cast = (byte)((ushort)(parameter));
                }
            }
            if (desiredType == typeof(short))
            {
                if (parameterType == typeof(ushort))
                {
                    cast = (short)((ushort)(parameter));
                }
                else if (parameterType == typeof(int))
                {
                    cast = (short)((int)(parameter));
                }
                else if (parameterType == typeof(uint))
                {
                    cast = (short)((uint)(parameter));
                }
            }
            else if (desiredType == typeof(ushort))
            {
                if (parameterType == typeof(short))
                {
                    cast = (ushort)((short)(parameter));
                }
                else if (parameterType == typeof(int))
                {
                    cast = (ushort)((int)(parameter));
                }
                else if (parameterType == typeof(uint))
                {
                    cast = (ushort)((uint)(parameter));
                }
            }
            else if (desiredType == typeof(int))
            {
                if (parameterType == typeof(uint))
                {
                    cast = (int)((uint)(parameter));
                }
                else if (parameterType == typeof(long))
                {
                    cast = (int)((long)(parameter));
                }
                else if (parameterType == typeof(ulong))
                {
                    cast = (int)((ulong)(parameter));
                }
            }
            else if (desiredType == typeof(uint))
            {
                if (parameterType == typeof(int))
                {
                    cast = (uint)((int)(parameter));
                }
                else if (parameterType == typeof(long))
                {
                    cast = (uint)((long)(parameter));
                }
                else if (parameterType == typeof(ulong))
                {
                    cast = (uint)((ulong)(parameter));
                }
            }
            else if (desiredType == typeof(long))
            {
                if (parameterType == typeof(int))
                {
                    cast = (long)((int)(parameter));
                }
                else if (parameterType == typeof(uint))
                {
                    cast = (long)((uint)(parameter));
                }
                else if (parameterType == typeof(ulong))
                {
                    cast = (long)((ulong)(parameter));
                }
            }
            else if (desiredType == typeof(ulong))
            {
                if (parameterType == typeof(int))
                {
                    cast = (ulong)((int)(parameter));
                }
                else if (parameterType == typeof(uint))
                {
                    cast = (ulong)((uint)(parameter));
                }
                else if (parameterType == typeof(long))
                {
                    cast = (ulong)((long)(parameter));
                }
            }
            else if (desiredType == typeof(float))
            {
                if (parameterType == typeof(double))
                {
                    cast = (float)((double)parameter);
                }
            }
            else if (desiredType == typeof(double))
            {
                if (parameterType == typeof(float))
                {
                    cast = (double)((float)parameter);
                }
            }

            if (cast == null)
            {
                throw new InvalidOperationException("Don't know how to cast to " + typeof(TValue));
            }
            return (TValue)cast;
        }

        /// <summary>
        /// Produces a string-ified version of the event that looks like the text logger entries
        /// </summary>
        /// <returns>A string</returns>
        public override string ToString()
        {
            return this.ToString(new EventStringFormatter());
        }

        /// <summary>
        /// Produces a string-ified version of the event that uses the text logger format.
        /// </summary>
        /// <param name="formatter">A previously created EventStringFormatter which will be overwritten with the contents
        /// of the entry</param>
        /// <returns>A string</returns>
        public string ToString(IEventFormatter formatter)
        {
            if (formatter == null)
            {
                throw new ArgumentNullException("formatter");
            }

            return formatter.Format(this);
        }

        /// <summary>
        /// Serialize to JSON
        /// </summary>
        /// <returns>String containing a JSON representation of the entry</returns>
        public string ToJsonString()
        {
            using (var buffer = new MemoryStream())
            {
                return this.ToJsonString(buffer);
            }
        }

        /// <summary>
        /// Serialize to XML. Always writes in the beginning of the provided buffer.
        /// </summary>
        /// <param name="buffer">A previously created MemoryStream (will not be Disposed)</param>
        /// <returns>String containing a JSON representation of the entry</returns>
        public string ToJsonString(MemoryStream buffer)
        {
            buffer.Position = 0;
            var written = this.Serialize(buffer, GetJsonSerializer());
            buffer.SetLength(written);
            return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }

        /// <summary>
        /// Serialize to XML
        /// </summary>
        /// <returns>String containing a XML representation of the entry</returns>
        public string ToXmlString()
        {
            using (var buffer = new MemoryStream())
            {
                return this.ToXmlString(buffer);
            }
        }

        /// <summary>
        /// Serialize to XML. Always writes in the beginning of the provided buffer.
        /// </summary>
        /// <param name="buffer">A previously created MemoryStream (will not be Disposed)</param>
        /// <returns>String containing a XML representation of the entry</returns>
        public string ToXmlString(MemoryStream buffer)
        {
            buffer.Position = 0;
            var written = this.Serialize(buffer, GetXmlSerializer());
            buffer.SetLength(written);
            return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }

        /// <summary>
        /// Retrieve an object suitable for use with the <see cref="Serialize"/> method to serialize events into JSON.
        /// </summary>
        /// <returns>The serializer object.</returns>
        public static DataContractJsonSerializer GetJsonSerializer()
        {
            return new DataContractJsonSerializer(typeof(ETWEvent));
        }

        /// <summary>
        /// Retrieve an object suitable for use with the <see cref="Serialize"/> method to serialize events into XML.
        /// </summary>
        /// <returns>The serializer object.</returns>
        public static DataContractSerializer GetXmlSerializer()
        {
            return new DataContractSerializer(typeof(ETWEvent));
        }

        /// <summary>
        /// Serialize the event using a given XmlObjectSerializer (e.g. DataContractJsonSerializer) to a pre-created
        /// memory stream.
        /// </summary>
        /// <param name="buffer">The MemoryStream to serialize in to.</param>
        /// <param name="serializer">The serializer object to use.</param>
        /// <returns>The number of bytes written into the stream.</returns>
        public long Serialize(MemoryStream buffer, XmlObjectSerializer serializer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException("buffer");
            }

            if (serializer == null)
            {
                throw new ArgumentNullException("serializer");
            }

            var startPos = buffer.Position;
            try
            {
                serializer.WriteObject(buffer, this);
            }
            catch (SerializationException)
            {
                // payload must be re-written.

                // if it throws this time we have some other nasty problem.
                var newEvent = this.SanitizeParametersForSerialization();
                buffer.Position = startPos;
                serializer.WriteObject(buffer, newEvent);
            }

            return (buffer.Position - startPos);
        }

        private ETWEvent Clone(OrderedDictionary parameters)
        {
            var newEvent = new ETWEvent(this.Timestamp, this.ProviderID, this.ProviderName, this.ID, this.EventName,
                                        this.Version, (EventKeywords)this.Keywords, this.Level, (EventOpcode)this.OpCode,
                                        this.ActivityID,
                                        this.RelatedActivityID, this.ProcessID, this.ThreadID,
                                        parameters ?? this.Parameters);
            newEvent.isFromSerializedData = this.isFromSerializedData;
            newEvent.SourceFilename = this.SourceFilename;

            return newEvent;
        }

        private ETWEvent SanitizeParametersForSerialization()
        {
            var newParameters = new OrderedDictionary(this.Parameters.Count);
            foreach (DictionaryEntry pair in this.Parameters)
            {
                Type valueType = pair.Value.GetType();
                if (valueType.IsPrimitive || (valueType.IsArray && valueType.GetElementType().IsPrimitive))
                {
                    newParameters.Add(pair.Key, pair.Value);
                }
                else if (valueType == typeof(string))
                {
                    newParameters.Add(pair.Key, pair.Value);
                }
                else if (valueType == typeof(DateTime))
                {
                    newParameters.Add(pair.Key, pair.Value);
                }
                else if (valueType == typeof(Guid))
                {
                    newParameters.Add(pair.Key, pair.Value);
                }
                else if (valueType.IsEnum)
                {
                    // The serializer seems to choke extremely hard on enums -- and we actually just wanted the int
                    // values anyway. So let's get those.
                    if (pair.Value is byte)
                    {
                        newParameters.Add(pair.Key, (byte)pair.Value);
                    }
                    else if (pair.Value is sbyte)
                    {
                        newParameters.Add(pair.Key, (sbyte)pair.Value);
                    }
                    else if (pair.Value is ushort)
                    {
                        newParameters.Add(pair.Key, (ushort)pair.Value);
                    }
                    else if (pair.Value is short)
                    {
                        newParameters.Add(pair.Key, (short)pair.Value);
                    }
                    else if (pair.Value is uint)
                    {
                        newParameters.Add(pair.Key, (uint)pair.Value);
                    }
                    else if (pair.Value is int)
                    {
                        newParameters.Add(pair.Key, (int)pair.Value);
                    }
                    else if (pair.Value is ulong)
                    {
                        newParameters.Add(pair.Key, (ulong)pair.Value);
                    }
                    else if (pair.Value is long)
                    {
                        newParameters.Add(pair.Key, (long)pair.Value);
                    }
                }
                else
                {
                    newParameters.Add(pair.Key, pair.Value.ToString());
                }
            }

            return this.Clone(newParameters);
        }

        private void CheckPropertySet()
        {
            if (this.SourceEvent != null)
            {
                throw new NotSupportedException("Cannot assign values to ETWEvent backed by TraceEvent");
            }
        }

        // Necessary because DataContract doesn't call any ctors when instantiating these objects.
        [OnDeserializing]
        private void OnDeserialize(StreamingContext unused)
        {
            this.sourceData = new EventData();
            this.isFromSerializedData = true;
        }

        /// <summary>
        /// Container to hold event data if there is no source trace event.
        /// </summary>
        private sealed class EventData
        {
            public Guid ActivityID;
            public string EventName;
            public ushort ID;
            public long Keywords;
            public EventLevel Level;
            public byte OpCode;
            public OrderedDictionary Parameters;
            public int ProcessID;
            public Guid ProviderID;
            public string ProviderName;
            public Guid RelatedActivityID;
            public int ThreadID;
            public DateTime Timestamp;
            public byte Version;
        }
    }
}