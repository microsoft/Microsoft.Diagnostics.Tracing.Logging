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

namespace ETWRotationDemo
{
    using System;
    using System.Diagnostics.Tracing;
    using System.IO;
    using System.Threading;

    using Microsoft.Diagnostics.Tracing.Logging;

    [EventSource(Name = "Demo", Guid = "{5636f2a4-0394-410a-abaf-89080f8542ce}")]
    internal sealed class DemoEvents : EventSource
    {
        public static DemoEvents Write = new DemoEvents();

        [Event(1, Level = EventLevel.Informational)]
        public void Log(string message)
        {
            if (this.IsEnabled())
            {
                this.WriteEvent(1, message);
            }
        }

        protected override void OnEventCommand(EventCommandEventArgs command)
        {
            Console.WriteLine("Got Event Command: {0}", command.Command);
            foreach (var kvp in command.Arguments)
            {
                Console.WriteLine("{0}:{1}", kvp.Key, kvp.Value);
            }
        }
    }

    public sealed class Program
    {
        private static void Main(string[] args)
        {
            var dir = Path.GetFullPath(args[0]);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            Console.WriteLine("Writing logs in {0}", dir);

            var config = string.Format(
                                       @"
<loggers>
  <etwlogging enabled='true' />
  <log name='demo' rotationInterval='60' type='etl' directory='{0}' timestampLocal='true'>
    <source name='Demo' minimumSeverity='informational' />
  </log>
</loggers>
", dir);

            LogManager.Start();
            LogAssert.Assert(LogManager.SetConfiguration(config));
            LogManager.ConsoleLogger.SubscribeToEvents(DemoEvents.Write, EventLevel.Verbose);
            LogManager.ConsoleLogger.SubscribeToEvents(InternalLogger.Write, EventLevel.Verbose);

            var t = new Timer(_ => DemoEvents.Write.Log(DateTime.Now.ToString()), null, TimeSpan.Zero,
                              new TimeSpan(0, 0, 1));

            Console.CancelKeyPress +=
                (sender, eventArgs) =>
                {
                    Console.WriteLine("Shutting down...");
                    LogManager.Shutdown();
                    Environment.Exit(0);
                };

            while (true)
            {
                Thread.Sleep(100);
            }
        }
    }
}