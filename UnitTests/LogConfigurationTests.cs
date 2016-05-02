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
    using System.Collections.Generic;
    using System.Diagnostics.Tracing;
    using System.IO;
    using System.Linq;

    using Newtonsoft.Json;

    using NUnit.Framework;

    [TestFixture]
    public sealed class LogConfigurationTests
    {
        private static IEnumerable<LogConfiguration> Configurations
        {
            get
            {
                // HACK: this works around NUnit issue #54: https://github.com/nunit/docs/issues/54
                if (LogManager.DefaultDirectory == null)
                {
                    LogManager.Start();
                }

                var subs = LogManager.DefaultSubscriptions;

                foreach (var value in Enum.GetValues(typeof(LogType)))
                {
                    var filters = new[] {".*", "abc", "123"};
                    var logType = (LogType)value;
                    switch (logType)
                    {
                    case LogType.None:
                        continue;
                    case LogType.EventTracing:
                        filters = new string[0];
                        break;
                    }

                    var config = new LogConfiguration("somelog", logType, subs, filters);
                    yield return config;

                    config.BufferSizeMB = LogManager.MinLogBufferSizeMB;
                    yield return config;
                    config.BufferSizeMB = LogManager.MaxLogBufferSizeMB;
                    yield return config;

                    if (logType.HasFeature(LogConfiguration.Features.FileBacked))
                    {
                        config.Directory = null;
                        yield return config;
                        config.Directory = "something";
                        yield return config;

                        config.FilenameTemplate = "{0}_{1:YYYYmmddHHMMSS}blorp{2:YYYYmmddHHMMSS}";
                        yield return config;
                        config.TimestampLocal = !config.TimestampLocal;
                        yield return config;

                        config.RotationInterval = LogManager.MinRotationInterval;
                        yield return config;
                        config.RotationInterval = LogManager.MaxRotationInterval;
                        yield return config;
                    }
                    else if (logType == LogType.Network)
                    {
                        config.Hostname = "a.ham";
                        yield return config;
                        config.Hostname = "a.burr";
                        yield return config;
                        config.Port = 867;
                        yield return config;
                        config.Port = 5309;
                        yield return config;
                    }
                }
            }
        }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            LogManager.Start();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            LogManager.Shutdown();
        }

        public struct ConstructorArgs
        {
            public bool IsValid { get; }
            public string Name { get; }
            public LogType Type { get; }
            public IEnumerable<EventProviderSubscription> Subs { get; }
            public IEnumerable<string> Filters { get; }

            public ConstructorArgs(bool valid, string name, LogType type, IEnumerable<EventProviderSubscription> subs,
                                   IEnumerable<string> filters)
            {
                this.IsValid = valid;
                this.Name = name;
                this.Type = type;
                this.Subs = subs;
                this.Filters = filters;
            }

            public override string ToString()
            {
                return (this.IsValid ? "Valid" : "Invalid") +
                       " Name: " + (this.Name ?? "<null>") +
                       " Type: " + this.Type +
                       " Subs: " + (this.Subs?.Count().ToString() ?? "null") +
                       " Filters: " + (this.Filters?.Count().ToString() ?? "null");
            }
        }

        private static IEnumerable<ConstructorArgs> Constructors
        {
            get
            {
                foreach (var value in Enum.GetValues(typeof(LogType)))
                {
                    var logType = (LogType)value;
                    if (logType == LogType.None)
                    {
                        yield return new ConstructorArgs(false, null, logType, LogManager.DefaultSubscriptions, null);
                        continue;
                    }

                    yield return new ConstructorArgs(false, null, logType, null, null);
                    yield return new ConstructorArgs(false, null, logType, null, new[] {"abc", "123"});
                    yield return
                        new ConstructorArgs(logType == LogType.Console || logType == LogType.MemoryBuffer, null, logType,
                                            LogManager.DefaultSubscriptions, null);
                    yield return new ConstructorArgs(true, "foo", logType, LogManager.DefaultSubscriptions, null);

                    var duplicateSubscriptions = new List<EventProviderSubscription>();
                    duplicateSubscriptions.AddRange(LogManager.DefaultSubscriptions);
                    duplicateSubscriptions.AddRange(LogManager.DefaultSubscriptions);
                    yield return new ConstructorArgs(false, "foo", logType, duplicateSubscriptions, null);

                    if (logType.HasFeature(LogConfiguration.Features.RegexFilter))
                    {
                        yield return
                            new ConstructorArgs(false, "foo", logType, LogManager.DefaultSubscriptions,
                                                new[] {"abc", "abc"});
                        yield return
                            new ConstructorArgs(true, "foo", logType, LogManager.DefaultSubscriptions,
                                                new[] {"abc", "123"});
                    }
                    if (logType.HasFeature(LogConfiguration.Features.FileBacked))
                    {
                        yield return
                            new ConstructorArgs(false, "foo" + string.Join("", Path.GetInvalidFileNameChars()), logType,
                                                LogManager.DefaultSubscriptions, null);
                    }
                }
            }
        }

        private sealed class ProviderOne : EventSource
        {
            public static readonly ProviderOne Write = new ProviderOne();

            [Event(1)]
            public void One(string msg)
            {
                this.WriteEvent(1, msg);
            }
        }

        private sealed class ProviderTwo : EventSource
        {
            public static readonly ProviderTwo Write = new ProviderTwo();

            [Event(2)]
            public void Two(string msg)
            {
                this.WriteEvent(2, msg);
            }
        }

        [Test, TestCaseSource(nameof(Configurations))]
        public void CanSerialize(LogConfiguration configuration)
        {
            var serializer = new JsonSerializer();
            var json = configuration.ToString(); // ToString returns the JSON representation itself.
            using (var reader = new StringReader(json))
            {
                using (var jsonReader = new JsonTextReader(reader))
                {
                    var deserialized = serializer.Deserialize<LogConfiguration>(jsonReader);
                    Assert.AreEqual(configuration, deserialized);
                    Assert.AreEqual(configuration.Name, deserialized.Name);
                    Assert.AreEqual(configuration.Type, deserialized.Type);
                    foreach (var sub in configuration.Subscriptions)
                    {
                        Assert.IsTrue(deserialized.Subscriptions.Contains(sub));
                    }
                    foreach (var filter in configuration.Filters)
                    {
                        Assert.IsTrue(deserialized.Filters.Contains(filter));
                    }
                    Assert.AreEqual(configuration.BufferSizeMB, deserialized.BufferSizeMB);
                    Assert.AreEqual(configuration.Directory, deserialized.Directory);
                    Assert.AreEqual(configuration.FilenameTemplate, deserialized.FilenameTemplate);
                    Assert.AreEqual(configuration.TimestampLocal, deserialized.TimestampLocal);
                    Assert.AreEqual(configuration.RotationInterval, deserialized.RotationInterval);
                    Assert.AreEqual(configuration.Hostname, deserialized.Hostname);
                    Assert.AreEqual(configuration.Port, deserialized.Port);
                }
            }
        }

        [Test, TestCaseSource(nameof(Constructors))]
        public void ConstructorValidation(ConstructorArgs args)
        {
            if (args.IsValid)
            {
                Assert.IsNotNull(new LogConfiguration(args.Name, args.Type, args.Subs, args.Filters));
            }
            else
            {
                Assert.Throws<InvalidConfigurationException>(
                                                             () =>
                                                             new LogConfiguration(args.Name, args.Type, args.Subs,
                                                                                  args.Filters));
            }
        }

        [Test]
        public void MergeCombinesSourcesAndFilters()
        {
            var leftSubs = new[]
                           {
                               new EventProviderSubscription(ProviderOne.Write, EventLevel.Warning),
                               new EventProviderSubscription(InternalLogger.Write, EventLevel.Warning, 0xdead0000)
                           };
            var leftFilters = new[] {"abc", "123"};
            var leftConfig = new LogConfiguration("foo", LogType.Console, leftSubs, leftFilters);

            var rightSubs = new[]
                            {
                                new EventProviderSubscription(InternalLogger.Write, EventLevel.Informational, 0xbeef),
                                new EventProviderSubscription(ProviderTwo.Write, EventLevel.Verbose)
                            };
            var rightFilters = new[] {"def", "123"};
            var rightConfig = new LogConfiguration("foo", LogType.Console, rightSubs, rightFilters);

            leftConfig.Merge(rightConfig);
            Assert.AreEqual(3, leftConfig.Filters.Count());
            Assert.Contains("abc", leftConfig.Filters.ToList());
            Assert.Contains("def", leftConfig.Filters.ToList());
            Assert.Contains("123", leftConfig.Filters.ToList());

            Assert.AreEqual(3, leftConfig.Subscriptions.Count());
            foreach (var sub in leftConfig.Subscriptions)
            {
                if (sub.Source == ProviderOne.Write)
                {
                    Assert.AreEqual(EventLevel.Warning, sub.MinimumLevel);
                    Assert.AreEqual(0, (ulong)sub.Keywords);
                }
                else if (sub.Source == ProviderTwo.Write)
                {
                    Assert.AreEqual(EventLevel.Verbose, sub.MinimumLevel);
                    Assert.AreEqual(0, (ulong)sub.Keywords);
                }
                else if (sub.Source == InternalLogger.Write)
                {
                    Assert.AreEqual(EventLevel.Informational, sub.MinimumLevel);
                    Assert.AreEqual(0xdeadbeef, (ulong)sub.Keywords);
                }
                else
                {
                    Assert.Fail("unexpected provider.");
                }
            }
        }

        [Test]
        public void NetworkPropertiesRequiredForValidConfiguration()
        {
            var config = new LogConfiguration("net", LogType.Network, LogManager.DefaultSubscriptions);
            Assert.IsFalse(config.IsValid);
            config.Hostname = "foo";
            Assert.IsFalse(config.IsValid);

            config = new LogConfiguration("net", LogType.Network, LogManager.DefaultSubscriptions);
            config.Port = 5309;
            Assert.IsFalse(config.IsValid);
            config.Hostname = "foo";
            Assert.IsTrue(config.IsValid);
        }

        [Test]
        public void PropertyChangeFailsAfterLogCreated()
        {
            LogManager.Start();

            var fileExpr = new Action<LogConfiguration>[]
                           {
                               c => c.Type = LogType.Text,
                               c => c.BufferSizeMB = LogManager.DefaultLogBufferSizeMB,
                               c => c.Directory = ".",
                               c => c.FilenameTemplate = LogManager.DefaultFilenameTemplate,
                               c => c.RotationInterval = 3600,
                               c => c.TimestampLocal = true
                           };
            var netExpr = new Action<LogConfiguration>[]
                          {
                              c => c.Type = LogType.Network,
                              c => c.Hostname = "foo",
                              c => c.Port = 5309,
                              c => c.BufferSizeMB = LogManager.DefaultLogBufferSizeMB,
                              c => c.TimestampLocal = true
                          };

            var fileConfig = new LogConfiguration("foo", LogType.EventTracing, LogManager.DefaultSubscriptions);
            foreach (var expr in fileExpr)
            {
                Assert.DoesNotThrow(() => expr(fileConfig));
            }
            var myLog = LogManager.CreateLogger(fileConfig);
            foreach (var expr in fileExpr)
            {
                Assert.Throws<InvalidOperationException>(() => expr(fileConfig));
            }
            LogManager.DestroyLogger(myLog);

            var netConfig = new LogConfiguration("foo", LogType.Network, LogManager.DefaultSubscriptions);
            foreach (var expr in netExpr)
            {
                Assert.DoesNotThrow(() => expr(netConfig));
            }
            myLog = LogManager.CreateLogger(netConfig);
            foreach (var expr in netExpr)
            {
                Assert.Throws<InvalidOperationException>(() => expr(fileConfig));
            }
            LogManager.DestroyLogger(myLog);

            LogManager.Shutdown();
        }

        [Test]
        public void TypeSpecificPropertyModificationsValidated()
        {
            LogManager.Start();

            var filePropertyChanges = new Action<LogConfiguration>[]
                                      {
                                          config => config.Directory = "somedir",
                                          config => config.RotationInterval = LogManager.DefaultRotationInterval,
                                          config => config.FilenameTemplate = "{0}"
                                      };

            var networkPropertyChanges = new Action<LogConfiguration>[]
                                         {
                                             config => config.Hostname = "foo",
                                             config => config.Port = 5309
                                         };

            foreach (var value in Enum.GetValues(typeof(LogType)))
            {
                var logType = (LogType)value;
                if (logType == LogType.None)
                {
                    continue;
                }

                var config = new LogConfiguration("foo", logType, LogManager.DefaultSubscriptions);
                switch (logType)
                {
                case LogType.Console:
                case LogType.MemoryBuffer:
                    foreach (var expr in filePropertyChanges)
                    {
                        Assert.Throws<InvalidConfigurationException>(() => expr(config));
                    }
                    foreach (var expr in networkPropertyChanges)
                    {
                        Assert.Throws<InvalidConfigurationException>(() => expr(config));
                    }
                    break;
                case LogType.Text:
                case LogType.EventTracing:
                    foreach (var expr in filePropertyChanges)
                    {
                        Assert.DoesNotThrow(() => expr(config));
                    }
                    foreach (var expr in networkPropertyChanges)
                    {
                        Assert.Throws<InvalidConfigurationException>(() => expr(config));
                    }
                    break;
                case LogType.Network:
                    foreach (var expr in networkPropertyChanges)
                    {
                        Assert.DoesNotThrow(() => expr(config));
                    }
                    foreach (var expr in filePropertyChanges)
                    {
                        Assert.Throws<InvalidConfigurationException>(() => expr(config));
                    }
                    break;
                }
            }

            LogManager.Shutdown();
        }
    }
}