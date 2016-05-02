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

namespace Microsoft.Diagnostics.Tracing.Logging.UnitTests
{
    using System;
    using System.Diagnostics.Tracing;
    using System.IO;

    using Newtonsoft.Json;

    using NUnit.Framework;

    [TestFixture]
    public sealed class EventProviderSubscriptionTests
    {
        private static readonly EventProviderSubscription[] Subscriptions =
        {
            new EventProviderSubscription("UnknownProvider"),
            new EventProviderSubscription(InternalLogger.Write.Name),
            new EventProviderSubscription(InternalLogger.Write.Name, EventLevel.Warning),
            new EventProviderSubscription(InternalLogger.Write.Name, EventLevel.Warning, (EventKeywords)0xdeadbeef),
            new EventProviderSubscription(InternalLogger.Write),
            new EventProviderSubscription(InternalLogger.Write, EventLevel.Warning),
            new EventProviderSubscription(InternalLogger.Write, EventLevel.Warning, (EventKeywords)0xdeadbeef),
            new EventProviderSubscription(InternalLogger.Write.Guid),
            new EventProviderSubscription(InternalLogger.Write.Guid, EventLevel.Warning),
            new EventProviderSubscription(InternalLogger.Write.Guid, EventLevel.Warning, (EventKeywords)0xdeadbeef)
        };

        [Test, TestCaseSource(nameof(Subscriptions))]
        public void CanSerialize(EventProviderSubscription subscription)
        {
            var serializer = new JsonSerializer();
            var json = subscription.ToString(); // ToString returns the JSON representation itself.
            using (var reader = new StringReader(json))
            {
                using (var jsonReader = new JsonTextReader(reader))
                {
                    var deserialized = serializer.Deserialize<EventProviderSubscription>(jsonReader);
                    Assert.AreEqual(subscription, deserialized);
                    Assert.AreEqual(subscription.Name, deserialized.Name);
                    Assert.AreEqual(subscription.Source, deserialized.Source);
                    Assert.AreEqual(subscription.ProviderID, deserialized.ProviderID);
                    Assert.AreEqual(subscription.Keywords, deserialized.Keywords);
                    Assert.AreEqual(subscription.MinimumLevel, deserialized.MinimumLevel);
                }
            }
        }

        [Test]
        public void InvalidConstructorParametersThrowException()
        {
            Assert.Throws<ArgumentException>(() => new EventProviderSubscription((string)null));
            Assert.Throws<ArgumentException>(() => new EventProviderSubscription(string.Empty));
            Assert.Throws<ArgumentException>(() => new EventProviderSubscription("   "));
            Assert.Throws<ArgumentException>(() => new EventProviderSubscription((EventSource)null));
            Assert.Throws<ArgumentException>(() => new EventProviderSubscription(Guid.Empty));
        }
    }
}