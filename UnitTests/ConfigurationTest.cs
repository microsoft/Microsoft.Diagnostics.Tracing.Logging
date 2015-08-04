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

namespace Microsoft.Diagnostics.Tracing.Logging.UnitTests
{
    using System.Diagnostics.Tracing;
    using System.IO;
    using System.Threading;

    using Microsoft.Diagnostics.Tracing.Session;

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

        [Test]
        public void CompoundConfiguration()
        {
            const string configFile = "testLoggingConfiguration2.xml";
            const string config = @"
<loggers>
  <etwlogging enabled=""true"" />
  <log name=""configFileLogger"" type=""text"">
    <source name=""Microsoft.Diagnostics.Tracing.Logging"" />
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
            Assert.IsNotNull(LogManager.GetFileLogger("configFileLogger") as ETLFileLogger);

            LogManager.Shutdown();
        }

        [Test]
        public void ConfigurationFile()
        {
            const string configFile = "testLoggingConfiguration.xml";
            const string config = @"
<loggers>
  <log name=""configFileLogger"" type=""text"">
    <source name=""Microsoft.Diagnostics.Tracing.Logging"" />
  </log>
</loggers>";
            LogManager.Start();
            LogManager.SetConfiguration(null); // wipe any config
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
            Assert.IsNotNull(LogManager.GetFileLogger("configFileLogger"));
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
            Assert.IsNull(LogManager.GetFileLogger("configFileLogger"));
            Assert.IsNotNull(LogManager.GetFileLogger("configFileLogger2"));

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

            LogManager.AllowEtwLogging = AllowEtwLoggingValues.Enabled;
            LogManager.Start();
            Assert.IsTrue(LogManager.SetConfiguration(config));
            Assert.AreEqual(1, LogManager.singleton.fileLoggers.Count);

            var theLogger = LogManager.GetFileLogger("etwLogger") as ETLFileLogger;
            Assert.IsNotNull(theLogger);
            string filename = Path.GetFileName(theLogger.Filename);
            Assert.AreEqual(filename, "etwLogger.etl");

            var session = new TraceEventSession(ETLFileLogger.SessionPrefix + "etwLogger",
                                                TraceEventSessionOptions.Attach);
            Assert.IsNotNull(session);
            Assert.AreEqual(LogManager.DefaultFileBufferSizeMB, session.BufferSizeMB);
            session.Dispose();
            LogManager.Shutdown();
        }

        [Test]
        public void CreateLoggerInSubdirectory()
        {
            const string config = @"
<loggers>
  <log name=""subTextLogger"" directory=""undercity"">
    <source name=""Microsoft.Diagnostics.Tracing.Logging"" />
  </log>
</loggers>";

            LogManager.Start();
            Assert.IsTrue(LogManager.SetConfiguration(config));
            Assert.AreEqual(1, LogManager.singleton.fileLoggers.Count);

            var theLogger = LogManager.GetFileLogger("subtextLogger") as TextFileLogger;
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
    <source name=""Microsoft.Diagnostics.Tracing.Logging"" />
  </log>
</loggers>";

            LogManager.Start();
            Assert.IsTrue(LogManager.SetConfiguration(config));
            Assert.AreEqual(1, LogManager.singleton.fileLoggers.Count);

            var theLogger = LogManager.GetFileLogger("textLogger") as TextFileLogger;
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

            Assert.IsNotNull(LogManager.GetFileLogger("filtered"));
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
</loggers>"));
            Assert.AreEqual(AllowEtwLoggingValues.Enabled, LogManager.AllowEtwLogging);

            LogManager.Shutdown();
            LogManager.Start();
            Assert.IsTrue(LogManager.SetConfiguration(
                                                      @"<loggers>
<etwlogging enabled=""False"" />
</loggers>"));
            Assert.AreEqual(AllowEtwLoggingValues.Disabled, LogManager.AllowEtwLogging);
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

            Assert.IsNotNull(LogManager.GetFileLogger("latestart"));
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
    <source name=""Microsoft.Diagnostics.Tracing.Logging"" />
  </log>
</loggers>";
            LogManager.AllowEtwLogging = AllowEtwLoggingValues.Enabled;
            LogManager.Start();
            Assert.IsTrue(LogManager.SetConfiguration(config));
            Assert.IsNotNull(LogManager.GetFileLogger("testLogger") as TextFileLogger);

            Assert.IsTrue(LogManager.SetConfiguration(config.Replace("text", "etl")));
            Assert.IsNotNull(LogManager.GetFileLogger("testLogger") as ETLFileLogger);

            Assert.IsTrue(LogManager.SetConfiguration(""));
            Assert.IsNull(LogManager.GetFileLogger("testLogger"));
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

            // basic strings (it's okay to have no loggers but you still need to be valid XML!)
            Assert.IsFalse(LogManager.IsConfigurationValid("can haz xml?"));
            Assert.IsTrue(LogManager.IsConfigurationValid("<loggers />"));

            // Every log must have at least one source, but it need not exist at time of config parse.
            Assert.IsFalse(LogManager.IsConfigurationValid("<loggers><log name=\"nosource\" /></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"hazsource\"><source name=\"SomeRandomSource\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"hazsource\"><source name=\"Microsoft.Diagnostics.Tracing.Logging\" /></log></loggers>"));

            // Log types need to be valid.
            Assert.IsFalse(
                           LogManager.IsConfigurationValid(
                                                           "<loggers><log name=\"typed\" type=\"foo\"><source name=\"Microsoft.Diagnostics.Tracing.Logging\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"typed\" type=\"etl\"><source name=\"Microsoft.Diagnostics.Tracing.Logging\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"typed\" type=\"etw\"><source name=\"Microsoft.Diagnostics.Tracing.Logging\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"typed\" type=\"text\"><source name=\"Microsoft.Diagnostics.Tracing.Logging\" /></log></loggers>"));
            Assert.IsTrue(
                          LogManager.IsConfigurationValid(
                                                          "<loggers><log name=\"typed\" type=\"txt\"><source name=\"Microsoft.Diagnostics.Tracing.Logging\" /></log></loggers>"));

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
            Assert.IsFalse(
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
    }
}