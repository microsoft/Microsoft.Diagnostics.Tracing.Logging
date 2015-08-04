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
    using System.Diagnostics.Tracing;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Text.RegularExpressions;

    using CommandLine;
    using CommandLine.Text;

    using Microsoft.Diagnostics.Tracing.Logging;
    using Microsoft.Diagnostics.Tracing.Logging.Reader;
    using Microsoft.Diagnostics.Tracing.Parsers;

    using Newtonsoft.Json;

    internal sealed class Options
    {
        [Option('j', "json", Required = false, DefaultValue = false, HelpText = "output in JSON format")]
        public bool JsonOutput { get; set; }

        [Option('f', "filter", Required = false, DefaultValue = null, HelpText = "regular expression output filter")]
        public string OutputFilter { get; set; }

        [Option('h', "statistics", Required = false, DefaultValue = false,
            HelpText = "output statistics about various file events.")]
        public bool StatisticsOutput { get; set; }

        [Option('p', "providerFilter", Required = false, DefaultValue = null,
            HelpText = "ETW provider to filter to (GUID or full/partial name)")]
        public string ProviderFilter { get; set; }

        [Option('o', "outputFile", Required = false, DefaultValue = null, HelpText = "File to emit output to")]
        public string OutputFile { get; set; }

        [Option('r', "realtime", Required = false, DefaultValue = false,
            HelpText = "create a real-time listening session")]
        public bool Realtime { get; set; }

        [Option('s', "summary", Required = false, DefaultValue = false, HelpText = "emit summary information for files")
        ]
        public bool Summary { get; set; }

        [Option('v', "verbose", Required = false, DefaultValue = false, HelpText = "output additional information")]
        public bool Verbose { get; set; }

        [Option('x', "xml", Required = false, DefaultValue = false, HelpText = "output in XML format")]
        public bool XmlOutput { get; set; }

        [ValueList(typeof(List<string>))]
        public List<string> Arguments { get; set; }

        [HelpOption]
        public string GetUsage()
        {
            const string usageHeader =
                @"Usage: EtwLogTool [arguments] <file(s)>
       EtwLogTool [arguments] -r <providers>

Providers are a space-separated list of tuples formatted as:
<provider>[!level[!keywords]] where level is a single character indicating minimum
severity level (v, i, w, e, c) and keywords is a hexadecimal 64 bit integer value.

Provider must be a well-formed GUID, the special value ""CLR"", or a valid path to
an executable. If an executable path is given then the executable is scanned for
EventSources and all found sources will be subscribed to.
";

            return usageHeader + HelpText.AutoBuild(this);
        }
    }

    internal sealed class Program
    {
        private static Options options;
        private static readonly IEventFormatter eventFormatter = new EventStringFormatter();
        private static readonly MemoryStream streamBuffer = new MemoryStream();
        private static ETWProcessor processor;
        private static bool prefixWithFilename;
        private static Regex filter;
        private static Guid providerIDFilter;
        private static SessionStatistics currentStatistics;

        private static void ShowErrorAndExit(string errorMessage, params object[] args)
        {
            Console.WriteLine(options.GetUsage());
            Console.WriteLine();
            Console.WriteLine(errorMessage, args);
            Environment.Exit(1);
        }

        private static void Main(string[] args)
        {
            options = new Options();
            var argumentParser = new Parser();

            if (!argumentParser.ParseArguments(args, options))
            {
                ShowErrorAndExit("invalid command line arguments.");
            }

            if (options.OutputFilter != null)
            {
                filter = new Regex(options.OutputFilter, RegexOptions.IgnoreCase);
            }

            if (options.ProviderFilter != null)
            {
                Guid.TryParse(options.ProviderFilter, out providerIDFilter);
            }

            if (options.OutputFile != null)
            {
                Console.WriteLine("Sending all output to {0}", options.OutputFile);
                Console.SetOut(new StreamWriter(options.OutputFile, false));
            }

            if (options.Realtime)
            {
                SetupRealtimeProcessor();
            }
            else
            {
                SetupFileProcessor();
            }
            processor.ProcessEventTypes = EventTypes.All;

            // Make sure we stop processing on ctrl+c.
            Console.CancelKeyPress += (sender, eventArgs) =>
                                      {
                                          processor.StopProcessing();
                                          eventArgs.Cancel = true; // prevent process termination
                                      };

            if (options.StatisticsOutput)
            {
                var statistics = new EventStatistics(processor);
                processor.Process();
                statistics.DumpStatistics();
            }
            else
            {
                processor.SessionStart += HandleSessionStart;
                processor.SessionEnd += HandleSessionEnd;
                processor.EventProcessed += HandleEvent;
                processor.Process();

                if (options.Verbose || options.Summary)
                {
                    Console.WriteLine("Total events: {0}, lost: {1}, unreadable: {2}", processor.Count,
                                      processor.EventsLost, processor.UnreadableEvents);
                }
            }

            processor.Dispose();
            Console.Out.Flush();
        }

        private static void HandleSessionStart(string name, DateTime start)
        {
            currentStatistics = new SessionStatistics {Name = name, Start = start};
        }

        private static void HandleBuffersLost(string name, long buffersLost)
        {
            currentStatistics.BuffersLost = buffersLost;
        }

        private static void HandleSessionEnd(string name, DateTime end, long events, long lostEvents,
                                             long unreadableEvents)
        {
            currentStatistics.End = end;
            currentStatistics.Count = events;
            currentStatistics.Unreadable = unreadableEvents;
            currentStatistics.EventsLost = lostEvents;

            if (options.Summary)
            {
                Console.WriteLine("{0}: {1:s} - {2:s} ({3})", currentStatistics.Name, currentStatistics.Start,
                                  currentStatistics.End, currentStatistics.End - currentStatistics.Start);
                Console.Write("{0}  {1} events, {2} lost buffers, {3} lost events, {4} unreadable",
                              string.Empty.PadLeft(currentStatistics.Name.Length, ' '), currentStatistics.Count,
                              currentStatistics.BuffersLost, currentStatistics.EventsLost,
                              currentStatistics.Unreadable);
                if (filter != null || options.ProviderFilter != null)
                {
                    Console.Write(", filtered: {0}", currentStatistics.Count - currentStatistics.Emitted);
                }
                Console.WriteLine();
            }
        }

        private static void HandleEvent(ETWEvent ev)
        {
            // For summary runs we don't need to burn time formatting the output unless we are doing some filtering
            // (in which case we are interested in the filter count)
            if (options.Summary && filter == null && options.ProviderFilter == null)
            {
                return;
            }

            if (options.ProviderFilter != null)
            {
                if (providerIDFilter != Guid.Empty && ev.ProviderID != providerIDFilter)
                {
                    return;
                }
                if (0 > ev.ProviderName.IndexOf(options.ProviderFilter, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            string output;
            if (options.JsonOutput)
            {
                output = JsonConvert.SerializeObject(ev);
            }
            else if (options.XmlOutput)
            {
                output = ev.ToXmlString(streamBuffer);
            }
            else
            {
                output = ev.ToString(eventFormatter);
            }

            if (filter != null && !filter.IsMatch(output))
            {
                return;
            }

            if (!options.Summary)
            {
                if (prefixWithFilename)
                {
                    Console.Write("{0}:", ev.SourceFilename);
                }
                Console.WriteLine(output);
            }
            ++currentStatistics.Emitted;
        }

        private struct SessionStatistics
        {
            public long BuffersLost;
            public long Count;
            public long Emitted;
            public DateTime End;
            public long EventsLost;
            public string Name;
            public DateTime Start;
            public long Unreadable;
        }

        #region Realtime Processing Support
        private static void SetupRealtimeProcessor()
        {
            var sessionName = string.Format("{0}-ELT-realtime", Environment.UserName);
            var realtimeSession = new ETWRealtimeProcessor(sessionName);

            bool added = false;
            foreach (var arg in options.Arguments)
            {
                string[] split = arg.Split('!');
                Guid providerID;
                var providers = new List<Guid>();
                var minimumSeverity = EventLevel.Informational;
                long keywords = 0x0;

                if (split[0].Equals("CLR", StringComparison.OrdinalIgnoreCase))
                {
                    providers.Add(ClrTraceEventParser.ProviderGuid);
                }
                else if (split[0].Equals("Kernel", StringComparison.OrdinalIgnoreCase))
                {
                    providers.Add(KernelTraceEventParser.ProviderGuid);
                }
                else if (!Guid.TryParse(split[0], out providerID))
                {
                    // If it's not a GUID it might be an assembly -- we can try that...
                    if (!GetProvidersFromAssembly(split[0], providers))
                    {
                        ShowErrorAndExit("{0} is not a validly formatted GUID, and not a usable assembly", split[0]);
                    }
                }
                else
                {
                    providers.Add(providerID);
                }

                // Ignore if the string is null(won't be), empty, or whitespace -- all these will just mean that we
                // use default severity.
                if (split.Length > 1 && !string.IsNullOrWhiteSpace(split[1]))
                {
                    minimumSeverity = ETWEvent.CharToEventLevel(split[1][0]);
                }

                if (split.Length > 2)
                {
                    string num = split[2];
                    if (num.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        num = num.Substring(2);
                    }

                    if (!long.TryParse(num, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out keywords))
                    {
                        ShowErrorAndExit("Keyword value {0} is not a valid hexadecimal number", split[2]);
                    }
                }

                foreach (var id in providers)
                {
                    realtimeSession.SubscribeToProvider(id, minimumSeverity, keywords);
                }
                added = true;
            }

            if (!added)
            {
                ShowErrorAndExit("No providers given to subscribe to!");
            }

            processor = realtimeSession;
        }

        private static bool GetProvidersFromAssembly(string assemblyPath, List<Guid> providers)
        {
            if (!File.Exists(assemblyPath))
            {
                ShowErrorAndExit("Assembly {0} could not be found", assemblyPath);
            }

            var assemblies = new HashSet<Assembly>();
            try
            {
                Assembly assembly = Assembly.ReflectionOnlyLoadFrom(assemblyPath);
                GetChildAssemblies(assembly, assemblies);
            }
            catch (Exception e)
            {
                ShowErrorAndExit("Could not load assembly {0} -- {1}", assemblyPath, e);
            }

            bool gotProviders = false;
            foreach (var assembly in assemblies)
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.BaseType == null || type.BaseType.Name != "EventSource")
                        {
                            continue;
                        }

                        var providerID = Guid.Empty;
                        foreach (var attribute in CustomAttributeData.GetCustomAttributes(type))
                        {
                            // ICK! We can't tell what the attribute's name is in a reflection-only context (until 4.5,
                            // which we don't have). So... just look for a named Guid argument and hope that works out
                            // for us in types based on EventSource. This is highly likely to be true.
                            foreach (var arg in attribute.NamedArguments)
                            {
                                if (arg.MemberInfo.Name == "Guid")
                                {
                                    providerID = new Guid((string)arg.TypedValue.Value);
                                    break;
                                }
                            }
                        }

                        if (providerID == Guid.Empty)
                        {
                            // Note: EventSource actually is fine with generating GUIDs from names. However, its
                            // implementation for doing so is currently private. For now let's just target people who
                            // have opted to give us GUIDs.
                            Console.WriteLine("EventSource type {0} doesn't have an explicit Guid value", type.Name);
                            Console.WriteLine("We can't use this type as-is. Please ask the author to add a GUID.");
                            continue;
                        }

                        providers.Add(providerID);
                        gotProviders = true;
                    }
                }
                catch (ReflectionTypeLoadException) { } // Not interested in these, since they seem pretty common.
            }

            return gotProviders;
        }

        private static void GetChildAssemblies(Assembly parent, HashSet<Assembly> knownAssemblies)
        {
            knownAssemblies.Add(parent);
            string rootPath = Path.GetDirectoryName(parent.ManifestModule.FullyQualifiedName);

            // This is a really hacky way to do this. We don't care about the GAC, we don't care about any other
            // kind of pathing, etc. We expect all referenced assemblies to be DLLs and we expect them all to be
            // chilling out with the parent. Probably good enough for now.
            foreach (var childAssemblyName in parent.GetReferencedAssemblies())
            {
                string childPath = Path.Combine(rootPath, childAssemblyName.Name + ".dll");
                try
                {
                    if (File.Exists(childPath))
                    {
                        Assembly childAssembly = Assembly.ReflectionOnlyLoadFrom(childPath);
                        if (!knownAssemblies.Contains(childAssembly))
                        {
                            GetChildAssemblies(childAssembly, knownAssemblies);
                        }
                    }
                }
                catch (ReflectionTypeLoadException e)
                {
                    // Not going to consider this fatal for now, but we'll note that it happened.
                    Console.WriteLine("Failed to open child assembly of {0}: {1} -- {2}",
                                      parent.ManifestModule.FullyQualifiedName, childPath, e);
                }
            }
        }
        #endregion

        #region File Processing Support
        private static void SetupFileProcessor()
        {
            string[] files = ProcessFilenames(options.Arguments);
            if (files.Length < 1)
            {
                ShowErrorAndExit("No files specified to process.");
            }
            else if (files.Length > 1)
            {
                prefixWithFilename = true;
            }

            var fileProcessor = new ETWFileProcessor(files);
            fileProcessor.BuffersLost += HandleBuffersLost;
            processor = fileProcessor;
        }

        private static string[] ProcessFilenames(IEnumerable<string> filenames)
        {
            var expandedFilenames = new List<string>();
            var expandedFilenamesSet = new HashSet<string>();
            foreach (var filename in filenames)
            {
                string fullFilename;
                if (filename.Contains("*") || filename.Contains("?"))
                {
                    string dir = Path.GetDirectoryName(filename);
                    if (string.IsNullOrEmpty(dir))
                    {
                        dir = ".";
                    }
                    string pattern = Path.GetFileName(filename);
                    foreach (var fn in Directory.EnumerateFiles(dir, pattern))
                    {
                        fullFilename = Path.GetFullPath(fn);
                        if (!expandedFilenamesSet.Contains(fullFilename))
                        {
                            expandedFilenames.Add(fullFilename);
                            expandedFilenamesSet.Add(fullFilename);
                        }
                    }
                }
                else
                {
                    fullFilename = Path.GetFullPath(filename);
                    if (!expandedFilenamesSet.Contains(fullFilename) && CheckFileOpenable(fullFilename))
                    {
                        expandedFilenames.Add(fullFilename);
                        expandedFilenamesSet.Add(fullFilename);
                    }
                }
            }

            if (expandedFilenames.Count == 0)
            {
                Environment.Exit(1);
            }

            return expandedFilenames.ToArray();
        }

        private static bool CheckFileOpenable(string filename)
        {
            if (!File.Exists(filename))
            {
                Console.WriteLine("File {0} could not be found.", filename);
                return false;
            }

            try
            {
                var stream = new FileStream(filename, FileMode.Open, FileAccess.Read);
                stream.Dispose();
            }
            catch (IOException e)
            {
                Console.WriteLine("File {0} cannot be accessed: {1}", filename, e.Message);
                return false;
            }

            return true;
        }
        #endregion
    }
}