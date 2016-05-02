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
    using System.Threading;

    using Microsoft.Diagnostics.Tracing.Session;

    using Newtonsoft.Json;

    using NUnit.Framework;

    [TestFixture]
    public class ConfigurationTests
    {
        internal class LateInstantiationLogger : EventSource
        {
            // don't follow the typical pattern here, instead we want to instantiate after configuring a listener
            public void SayHello(string message)
            {
                WriteEvent(1, message);
            }
        }

        private static IEnumerable<Configuration> SerializedConfigurations
        {
            get
            {
                var consoleLog = new LogConfiguration(null, LogType.Console, LogManager.DefaultSubscriptions);
                var fileLog = new LogConfiguration("somefile", LogType.Text, new[]
                                                                             {
                                                                                 new EventProviderSubscription(
                                                                                     InternalLogger.Write,
                                                                                     EventLevel.Verbose, 0xdeadbeef),
                                                                             });
                foreach (var value in Enum.GetValues(typeof(Configuration.AllowEtwLoggingValues)))
                {
                    var config = new Configuration(new[] {consoleLog, fileLog});
                    config.AllowEtwLogging = (Configuration.AllowEtwLoggingValues)value;
                    yield return config;
                }
            }
        }

        [Test, TestCaseSource(nameof(SerializedConfigurations))]
        public void CanSerialize(Configuration configuration)
        {
            var serializer = new JsonSerializer();
            var json = configuration.ToString(); // ToString returns the JSON representation itself.
            using (var reader = new StringReader(json))
            {
                using (var jsonReader = new JsonTextReader(reader))
                {
                    var deserialized = serializer.Deserialize<Configuration>(jsonReader);
                    Assert.AreEqual(configuration.AllowEtwLogging, deserialized.AllowEtwLogging);
                    Assert.AreEqual(configuration.Logs.Count(), deserialized.Logs.Count());
                    foreach (var log in configuration.Logs)
                    {
                        Assert.IsTrue(deserialized.Logs.Contains(log));
                    }
                }
            }
        }

        [Test]
        public void ConstructorValidation()
        {
            var validLogConfigs = new[] {new LogConfiguration(null, LogType.Console, LogManager.DefaultSubscriptions),};

            foreach (var value in Enum.GetValues(typeof(Configuration.AllowEtwLoggingValues)))
            {
                var validValue = (Configuration.AllowEtwLoggingValues)value;
                Assert.DoesNotThrow(() => new Configuration(validLogConfigs, validValue));
            }
            var invalidAllowValue =
                (Configuration.AllowEtwLoggingValues)((int)Configuration.AllowEtwLoggingValues.Enabled + 8675309);
            Assert.Throws<ArgumentOutOfRangeException>(() => new Configuration(validLogConfigs, invalidAllowValue));

            Assert.Throws<ArgumentNullException>(() => new Configuration(null));
            Assert.Throws<ArgumentException>(() => new Configuration(new LogConfiguration[0]));

            var invalidConfig = new LogConfiguration("foo", LogType.Network, LogManager.DefaultSubscriptions);
            Assert.IsFalse(invalidConfig.IsValid);
            var invalidLogConfigs = new List<LogConfiguration>(validLogConfigs);
            invalidLogConfigs.Add(invalidConfig);
            Assert.Throws<InvalidConfigurationException>(() => new Configuration(invalidLogConfigs));

            var duplicateLogConfigs = new List<LogConfiguration>(validLogConfigs);
            duplicateLogConfigs.AddRange(validLogConfigs);
            Assert.Throws<InvalidConfigurationException>(() => new Configuration(duplicateLogConfigs));

            var memoryLogs = new[]
                             {new LogConfiguration("memory", LogType.MemoryBuffer, LogManager.DefaultSubscriptions),};
            Assert.Throws<InvalidConfigurationException>(() => new Configuration(memoryLogs));
        }

        [Test]
        public void MergeCombinesLogsAndSettings()
        {
            var leftLogConfig = new[]
                                {
                                    new LogConfiguration("left", LogType.Text, LogManager.DefaultSubscriptions),
                                    new LogConfiguration("middle", LogType.EventTracing, LogManager.DefaultSubscriptions),
                                };
            var leftConfig = new Configuration(leftLogConfig, Configuration.AllowEtwLoggingValues.None);

            var rightLogConfig = new[]
                                 {
                                     new LogConfiguration("middle", LogType.Text,
                                                          LogManager.DefaultSubscriptions),
                                     new LogConfiguration("right", LogType.Network, LogManager.DefaultSubscriptions)
                                     {Hostname = "foo", Port = 5309},
                                 };
            var rightConfig = new Configuration(rightLogConfig, Configuration.AllowEtwLoggingValues.Enabled);

            leftConfig.Merge(rightConfig);
            Assert.AreEqual(rightConfig.AllowEtwLogging, leftConfig.AllowEtwLogging);
            Assert.AreEqual(3, leftConfig.Logs.Count());
            foreach (var log in leftConfig.Logs)
            {
                if (log.Name == "left")
                {
                    Assert.AreEqual(LogType.Text, log.Type);
                }
                else if (log.Name == "middle")
                {
                    Assert.AreEqual(LogType.Text, log.Type);
                }
                else if (log.Name == "right")
                {
                    Assert.AreEqual(LogType.Network, log.Type);
                }
                else
                {
                    Assert.Fail("Unexpected log encountered.");
                }
            }
        }

        [Test]
        public void MergeDisablingEtwLoggingModifiesExistingLogs()
        {
            var leftLogConfig = new[]
                                {
                                    new LogConfiguration("left", LogType.EventTracing, LogManager.DefaultSubscriptions),
                                };
            var leftConfig = new Configuration(leftLogConfig, Configuration.AllowEtwLoggingValues.None);

            var rightLogConfig = new[]
                                 {
                                     new LogConfiguration("right", LogType.Network, LogManager.DefaultSubscriptions)
                                     {Hostname = "foo", Port = 5309},
                                 };
            var rightConfig = new Configuration(rightLogConfig, Configuration.AllowEtwLoggingValues.Disabled);

            leftConfig.Merge(rightConfig);
            Assert.AreEqual(rightConfig.AllowEtwLogging, leftConfig.AllowEtwLogging);
            Assert.AreEqual(2, leftConfig.Logs.Count());
            var etwLog = leftConfig.Logs.First(l => l.Name == "left");
            Assert.AreEqual(LogType.Text, etwLog.Type);
        }

        [Test]
        public void ClearRemovesLogsButNotSettings()
        {
            var config =
                new Configuration(new[] {new LogConfiguration(null, LogType.Console, LogManager.DefaultSubscriptions),},
                                  Configuration.AllowEtwLoggingValues.Enabled);
            Assert.AreNotEqual(0, config.Logs.Count());
            Assert.AreEqual(Configuration.AllowEtwLoggingValues.Enabled, config.AllowEtwLogging);
            config.Clear();
            Assert.AreEqual(0, config.Logs.Count());
            Assert.AreEqual(Configuration.AllowEtwLoggingValues.Enabled, config.AllowEtwLogging);
        }

        #region legacy XML tests
#pragma warning disable 618
        [Test]
        public void CompoundConfiguration()
        {
            const string configFile = "testLoggingConfiguration2.xml";
            const string config = @"
<loggers>
  <etwlogging enabled=""true"" />
  <log name=""configFileLogger"" type=""text"">
    <source name=""Microsoft-Diagnostics-Tracing-Logging"" />
  </log>
</loggers>";
            LogManager.Start();
            Assert.AreEqual(0, LogManager.singleton.fileLoggers.Count);

            File.Delete(configFile);
            using (var file = new FileStream(configFile, FileMode.Create))
            {
                using (var writer = new StreamWriter(file))
                {
                    writer.WriteLine(config);
                }
            }

            Assert.IsTrue(LogManager.SetConfigurationFile(configFile));
            Assert.IsTrue(LogManager.SetConfiguration(config.Replace("text", "etw")));
            Assert.AreEqual(1, LogManager.singleton.fileLoggers.Count);
            Assert.IsNotNull(LogManager.GetLogger<ETLFileLogger>("configFileLogger"));

            LogManager.Shutdown();
        }

        [Test]
        public void ConfigurationFile()
        {
            const string configFile = "testLoggingConfiguration.xml";
            const string config = @"
<loggers>
  <log name=""configFileLogger"" type=""text"">
    <source name=""Microsoft-Diagnostics-Tracing-Logging"" />
  </log>
</loggers>";
            LogManager.Start();
            LogManager.SetConfiguration((string)null); // wipe any config
            Assert.AreEqual(0, LogManager.singleton.fileLoggers.Count);

            File.Delete(configFile);
            try
            {
                LogManager.SetConfigurationFile(configFile);
                Assert.Fail();
            }
            catch (FileNotFoundException) { }

            using (var file = new FileStream(configFile, FileMode.Create))
            {
                using (var writer = new StreamWriter(file))
                {
                    writer.WriteLine(config);
                }
            }

            Assert.IsTrue(LogManager.SetConfigurationFile(configFile));
            Assert.AreEqual(1, LogManager.singleton.fileLoggers.Count);
            Assert.IsNotNull(LogManager.GetLogger<TextFileLogger>("configFileLogger"));
            long currentReadCount = LogManager.singleton.configurationFileReloadCount;

            using (var file = new FileStream(configFile, FileMode.Create))
            {
                using (var writer = new StreamWriter(file))
                {
                    writer.WriteLine(config.Replace("configFileLogger", "configFileLogger2"));
                }
            }

            // wait for the logger to update
            while (true)
            {
                if (currentReadCount != LogManager.singleton.configurationFileReloadCount)
                {
                    currentReadCount = LogManager.singleton.configurationFileReloadCount;
                    break;
                }
                Thread.Sleep(100);
            }
            Assert.AreEqual(LogManager.singleton.configurationFileReloadCount, currentReadCount);
            Assert.AreEqual(1, LogManager.singleton.fileLoggers.Count);
            Assert.IsNull(LogManager.GetLogger("configFileLogger", LogType.Text));
            Assert.IsNotNull(LogManager.GetLogger("configFileLogger2", LogType.Text));

            LogManager.Shutdown();
        }

        [Test]
        public void CreateETWLogger()
        {
            const string config = @"
<loggers>
  <log name=""etwLogger"" type=""etl"">
    <source name=""Microsoft-Diagnostics-Tracing-Logging"" />
  </log>
</loggers>";

            LogManager.Configuration.AllowEtwLogging = Configuration.AllowEtwLoggingValues.Enabled;
            LogManager.Start();
            Assert.IsTrue(LogManager.SetConfiguration(config));
            Assert.AreEqual(1, LogManager.singleton.fileLoggers.Count);

            var theLogger = LogManager.GetLogger("etwLogger", LogType.EventTracing) as ETLFileLogger;
            Assert.IsNotNull(theLogger);
            string filename = Path.GetFileName(theLogger.Filename);
            Assert.AreEqual(filename, "etwLogger.etl");

            var session = new TraceEventSession(ETLFileLogger.SessionPrefix + "etwLogger",
                                                TraceEventSessionOptions.Attach);
            Assert.IsNotNull(session);
            Assert.AreEqual(LogManager.DefaultLogBufferSizeMB, session.BufferSizeMB);
            session.Dispose();
            LogManager.Shutdown();
        }

        [Test]
        public void CreateLoggerInSubdirectory()
        {
            const string config = @"
<loggers>
  <log name=""subTextLogger"" directory=""undercity"">
    <source name=""Microsoft-Diagnostics-Tracing-Logging"" />
  </log>
</loggers>";

            LogManager.Start();
            Assert.IsTrue(LogManager.SetConfiguration(config));
            Assert.AreEqual(1, LogManager.singleton.fileLoggers.Count);

            var theLogger = LogManager.GetLogger("subtextLogger", LogType.Text) as TextFileLogger;
            Assert.IsNotNull(theLogger);
            string filename = Path.GetFileName(theLogger.Filename);
            Assert.AreEqual(filename, "subTextLogger.log");
            string expectedPath = Path.Combine(LogManager.DefaultDirectory, "undercity");
            Assert.AreEqual(Path.GetDirectoryName(theLogger.Filename), expectedPath);
            LogManager.Shutdown();
        }

        [Test]
        public void CreateTextLogger()
        {
            const string config = @"
<loggers>
  <log name=""textLogger"">
    <source name=""Microsoft-Diagnostics-Tracing-Logging"" />
  </log>
</loggers>";

            LogManager.Start();
            Assert.IsTrue(LogManager.SetConfiguration(config));
            Assert.AreEqual(1, LogManager.singleton.fileLoggers.Count);

            var theLogger = LogManager.GetLogger<TextFileLogger>("textLogger");
            Assert.IsNotNull(theLogger);
            string dirname = Path.GetDirectoryName(theLogger.Filename);
            Assert.AreEqual(dirname, LogManager.DefaultDirectory);
            string filename = Path.GetFileName(theLogger.Filename);
            Assert.AreEqual(filename, "textLogger.log");
            LogManager.Shutdown();
        }

        [Test]
        public void TestConfiguredFilter()
        {
            LogManager.Start();
            string filename = Path.Combine(LogManager.DefaultDirectory, "filtered.log");
            File.Delete(filename);
            Assert.IsTrue(LogManager.SetConfiguration(
                                                      @"<loggers>
  <log name=""filtered"" type=""text"" directory=""."">
    <source name=""TestLogger"" minimumSeverity=""verbose"" />
    <filter>Oddball</filter>
  </log>
</loggers>"));

            Assert.IsNotNull(LogManager.GetLogger<TextFileLogger>("filtered"));
            for (int i = 0; i < 42; ++i)
            {
                TestLogger.Write.String((i % 2 == 1 ? "Oddball" : "Moneyball"));
            }
            LogManager.Shutdown();

            Assert.AreEqual(21, LoggerTests.CountFileLines(filename));
        }

        [Test]
        public void TestEtwControlKnob()
        {
            LogManager.Shutdown();
            LogManager.Start();
            Assert.IsTrue(LogManager.SetConfiguration(
                                                      @"<loggers>
<etwlogging enabled=""true"" />
<log name=""somelog""><source name=""Microsoft-Diagnostics-Tracing-Logging"" /></log>
</loggers>"));
            Assert.AreEqual(Configuration.AllowEtwLoggingValues.Enabled, LogManager.Configuration.AllowEtwLogging);

            LogManager.Shutdown();
            LogManager.Start();
            Assert.IsTrue(LogManager.SetConfiguration(
                                                      @"<loggers>
<etwlogging enabled=""False"" />
<log name=""somelog""><source name=""Microsoft-Diagnostics-Tracing-Logging"" /></log>
</loggers>"));
            Assert.AreEqual(Configuration.AllowEtwLoggingValues.Disabled, LogManager.Configuration.AllowEtwLogging);
            LogManager.Shutdown();
        }

        [Test]
        public void TestLateInstantiation()
        {
            LogManager.Start();
            string filename = Path.Combine(LogManager.DefaultDirectory, "latestart.log");
            File.Delete(filename);
            Assert.IsTrue(LogManager.SetConfiguration(
                                                      @"<loggers>
  <log name=""latestart"" type=""text"" directory=""."">
    <source name=""LateInstantiationLogger"" minimumSeverity=""verbose"" />
  </log>
</loggers>"));

            Assert.IsNotNull(LogManager.GetLogger<TextFileLogger>("latestart"));
            var writer = new LateInstantiationLogger();
            for (int i = 0; i < 42; ++i)
            {
                writer.SayHello("sup");
            }
            LogManager.Shutdown();

            Assert.AreEqual(42, LoggerTests.CountFileLines(filename));
        }

        [Test]
        public void Update()
        {
            const string config = @"
<loggers>
  <log name=""testLogger"" type=""text"">
    <source name=""Microsoft-Diagnostics-Tracing-Logging"" />
  </log>
</loggers>";
            LogManager.Configuration.AllowEtwLogging = Configuration.AllowEtwLoggingValues.Enabled;
            LogManager.Start();
            Assert.IsTrue(LogManager.SetConfiguration(config));
            Assert.IsNotNull(LogManager.GetLogger<TextFileLogger>("testLogger"));

            Assert.IsTrue(LogManager.SetConfiguration(config.Replace("text", "etl")));
            Assert.IsNotNull(LogManager.GetLogger<ETLFileLogger>("testLogger"));

            Assert.IsTrue(LogManager.SetConfiguration(""));
            Assert.IsNull(LogManager.GetLogger<TextFileLogger>("testLogger"));
            Assert.AreEqual(0, LogManager.singleton.fileLoggers.Count);
            LogManager.Shutdown();
        }

        [Test]
        public void Validation()
        {
            LogManager.Start();

            // emptiness
            Assert.IsTrue(LogManager.IsConfigurationValid(null));
            Assert.IsTrue(LogManager.IsConfigurationValid(""));

            // basic strings nonsense.
            Assert.IsFalse(LogManager.IsConfigurationValid("can haz xml?"));

            // Every log must have at least one source, but it need not exist at time of config parse.
            Assert.IsFalse(LogManager.IsConfigurationValid("<loggers><log name=\"nosource\" /></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"hazsource\"><source name=\"SomeRandomSource\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"hazsource\"><source name=\"Microsoft-Diagnostics-Tracing-Logging\" /></log></loggers>"));

            // Log types need to be valid.
            Assert.IsFalse(
                           LogManager.IsConfigurationValid(
                                                           "<loggers><log name=\"typed\" type=\"foo\"><source name=\"Microsoft-Diagnostics-Tracing-Logging\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"typed\" type=\"etl\"><source name=\"Microsoft-Diagnostics-Tracing-Logging\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"typed\" type=\"etw\"><source name=\"Microsoft-Diagnostics-Tracing-Logging\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"typed\" type=\"text\"><source name=\"Microsoft-Diagnostics-Tracing-Logging\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"typed\" type=\"txt\"><source name=\"Microsoft-Diagnostics-Tracing-Logging\" /></log></loggers>"));

            // All log names need to be valid filenames
            Assert.IsFalse(
                           LogManager.IsConfigurationValid(
                                                           "<loggers><log name=\"not\\valid\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"valid\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));

            // Buffer sizes should be rational
            Assert.IsFalse(
                           LogManager.IsConfigurationValid(
                                                           "<loggers><log name=\"buff\" bufferSizeMB=\"herpetyderpety\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));
            Assert.IsFalse(
                           LogManager.IsConfigurationValid(
                                                           "<loggers><log name=\"buff\" bufferSizeMB=\"-6237\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));
            Assert.IsFalse(
                           LogManager.IsConfigurationValid(
                                                           "<loggers><log name=\"buff\" bufferSizeMB=\"0\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"buff\" bufferSizMB=\"100\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));
            Assert.IsFalse(
                           LogManager.IsConfigurationValid(
                                                           "<loggers><log name=\"buff\" bufferSizeMB=\"1025\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"buff\" bufferSizeMB=\"1024\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));

            // Directories must be valid (or empty!)
            Assert.IsFalse(
                           LogManager.IsConfigurationValid(
                                                           "<loggers><log name=\"dirp\" directory=\"no|way\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"dirp\" directory=\"\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"dirp\" directory=\"Random.Ness\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"dirp\" directory=\"some\\dir\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"dirp\" directory=\"some\\dir\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));

            // Rotation values must be sane (numbers, and not stupid numbers)
            Assert.IsFalse(
                           LogManager.IsConfigurationValid(
                                                           "<loggers><log name=\"rotation\" rotationInterval=\"nahhhh\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));
            Assert.IsFalse(
                           LogManager.IsConfigurationValid(
                                                           "<loggers><log name=\"rotation\" rotationInterval=\"8675309\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"rotation\" rotationInterval=\"-60\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"rotation\" rotationInterval=\"60\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"rotation\" rotationInterval=\"600\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"rotation\" rotationInterval=\"86400\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));

            // Filename templates should likewise be rational
            Assert.IsFalse(
                           LogManager.IsConfigurationValid(
                                                           "<loggers><log name=\"template\" filenameTemplate=\"\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));
            Assert.IsFalse(
                           LogManager.IsConfigurationValid(
                                                           "<loggers><log name=\"template\" filenameTemplate=\"{1}\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));
            Assert.IsFalse(
                           LogManager.IsConfigurationValid(
                                                           "<loggers><log name=\"template\" filenameTemplate=\"{0}{5}\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));
            Assert.IsFalse(
                           LogManager.IsConfigurationValid(
                                                           "<loggers><log name=\"template\" filenameTemplate=\"{0}\\:badname\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"template\" filenameTemplate=\"{0}\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));

            // Timestamp use
            Assert.IsFalse(
                           LogManager.IsConfigurationValid(
                                                           "<loggers><log name=\"template\" timestampLocal=\"bagels\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));
            Assert.IsFalse(
                           LogManager.IsConfigurationValid(
                                                           "<loggers><log name=\"template\" timestampLocal=\"\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"template\" timestampLocal=\"true\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" /></log></loggers>"));

            // Source severity must be known
            Assert.IsFalse(
                           LogManager.IsConfigurationValid(
                                                           "<loggers><log name=\"source\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" minimumSeverity=\"whaaaa?\"/></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"source\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" minimumSeverity=\"Critical\"/></log></loggers>"));

            // Provider IDs must be well-formed but need not exist
            Assert.IsFalse(
                           LogManager.IsConfigurationValid(
                                                           "<loggers><log name=\"provid\"><source providerID=\"867-5309\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"provid\"><source providerID=\"{23D37DB3-E5D2-493E-0000-EE60234BA724}\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"provid\"><source providerID=\"{23D37DB3-E5D2-493E-8BDE-EE60234BA724}\" /></log></loggers>"));

            // Keywords must be in hex format (0x optional)
            Assert.IsFalse(
                           LogManager.IsConfigurationValid(
                                                           "<loggers><log name=\"source\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" keywords=\"\"/></log></loggers>"));
            Assert.IsFalse(
                           LogManager.IsConfigurationValid(
                                                           "<loggers><log name=\"source\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" keywords=\"brass monkey\"/></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"source\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" keywords=\" 0x42 \"/></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"source\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" keywords=\"0xbeef\"/></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"source\"><source name=\"Microsoft.Diagnostics.Tracing.logging\" keywords=\"beef\"/></log></loggers>"));

            Assert.IsFalse(LogManager.IsConfigurationValid("<loggers><etwlogging /></loggers>"));
            // etwlogging needs an attribute
            Assert.IsFalse(LogManager.IsConfigurationValid("<loggers><etwlogging foo=\"bar\"/></loggers>"));
            // and not just any
            Assert.IsFalse(LogManager.IsConfigurationValid("<loggers><etwlogging enabled=\"1\"/></loggers>"));
            // needs to be true or false
            Assert.IsTrue(LogManager.IsConfigurationValid("<loggers><etwlogging enabled=\"true\"/></loggers>"));
            // needs to be true or false

            // Console logger must be unnamed
            Assert.IsFalse(
                           LogManager.IsConfigurationValid(
                                                           "<loggers><log name=\"console\" type=\"console\"><source name=\"Microsoft.Diagnostics.Tracing.logging\"/></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log type=\"console\"><source name=\"Microsoft.Diagnostics.Tracing.logging\"/></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log type=\"cons\"><source name=\"Microsoft.Diagnostics.Tracing.logging\"/></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log type=\"con\"><source name=\"Microsoft.Diagnostics.Tracing.logging\"/></log></loggers>"));

            // Filters only apply to some log types and cannot be duplicated.
            Assert.IsFalse(
                           LogManager.IsConfigurationValid(
                                                           "<loggers><log type=\"con\"><source name=\"Microsoft.Diagnostics.Tracing.logging\"/><filter>hello</filter><filter>hello</filter></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log type=\"con\"><source name=\"Microsoft.Diagnostics.Tracing.logging\"/><filter>hello</filter></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"hazfilter\" type=\"text\"><source name=\"Microsoft.Diagnostics.Tracing.logging\"/><filter>hello</filter></log></loggers>"));
            Assert.IsFalse(
                           LogManager.IsConfigurationValid(
                                                           "<loggers><log name=\"hazfilter\" type=\"etw\"><source name=\"Microsoft.Diagnostics.Tracing.logging\"/><filter>hello</filter></log></loggers>"));
            LogManager.Shutdown();
        }
#pragma warning restore 618
        #endregion
    }
}