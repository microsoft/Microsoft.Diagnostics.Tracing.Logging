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

namespace Microsoft.Diagnostics.Tracing.Logging.Reader
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// Reads one or more ETW log (ETL) files and provides events which may be examined.
    /// </summary>
    public sealed class ETWFileProcessor : ETWProcessor
    {
        #region Public
        /// <summary>
        /// Constructor with no files configured.
        /// </summary>
        public ETWFileProcessor()
        {
            this.ProcessEventTypes = EventTypes.EventSource;
            this.StartTime = DateTime.MaxValue;
            this.EndTime = DateTime.MinValue;
        }

        /// <summary>
        /// Constructor to read one or more ETW logs together.
        /// </summary>
        /// <param name="files">File names of the logs to process.</param>
        public ETWFileProcessor(ICollection<string> files) : this()
        {
            this.SetFiles(files);
        }

        /// <summary>
        /// Constructor to read a single ETW log.
        /// </summary>
        /// <param name="filename">Filename of the ETW log.</param>
        public ETWFileProcessor(string filename) : this(new[] {filename}) { }

        /// <summary>
        /// Update the files to process with a single new file.
        /// </summary>
        /// <param name="filename">File name of the file to process.</param>
        public void SetFile(string filename)
        {
            this.SetFiles(new[] {filename});
        }

        /// <summary>
        /// Update the list of files to process.
        /// </summary>
        /// <param name="files">File names of the logs to process.</param>
        public void SetFiles(ICollection<string> files)
        {
            if (files == null || files.Count == 0)
            {
                throw new ArgumentException("Must specify at least one file to read.", "files");
            }

            foreach (var f in files)
            {
                if (string.IsNullOrEmpty(f))
                {
                    throw new ArgumentException("Must not specify a null or empty filename.", "files");
                }
                if (!File.Exists(f))
                {
                    throw new FileNotFoundException("File does not exist", f);
                }
            }

            this.filenames = files;
        }

        public override void Process()
        {
            this.StartTime = DateTime.MaxValue;
            this.EndTime = DateTime.MinValue;
            this.Count = 0;
            this.EventsLost = 0;
            this.UnreadableEvents = 0;
            this.TotalBuffersLost = 0;

            if (this.filenames == null)
            {
                throw new OperationCanceledException("No files provided to process.");
            }

            foreach (var fn in this.filenames)
            {
                this.ReadFile(fn);
            }
        }

        /// <summary>
        /// Earliest seen start time for sessions in all files read.
        /// </summary>
        public DateTime StartTime { get; private set; }

        /// <summary>
        /// Latest seen end time for sessions in all files read.
        /// </summary>
        public DateTime EndTime { get; private set; }

        /// <summary>
        /// Number of buffers lost.
        /// </summary>
        public long TotalBuffersLost { get; private set; }

        /// <summary>
        /// Delegate to handle lost buffers in a file.
        /// </summary>
        /// <param name="sessionName">Name of the file where the lost buffers occured.</param>
        /// <param name="buffersLost">Number of lost buffers.</param>
        public delegate void BuffersLostHandler(string sessionName, long buffersLost);

        public event BuffersLostHandler BuffersLost;
        #endregion

        #region Private
        private void ReadFile(string filename)
        {
            this.CurrentSessionName = filename;
            using (this.TraceEventSource = new ETWTraceEventSource(filename, TraceEventSourceType.FileOnly))
            {
                if (this.TraceEventSource.SessionStartTime < this.StartTime)
                {
                    this.StartTime = this.TraceEventSource.SessionStartTime;
                }
                if (this.TraceEventSource.SessionEndTime > this.EndTime)
                {
                    this.EndTime = this.TraceEventSource.SessionEndTime;
                }

                if (this.TraceEventSource.Kernel != null)
                {
                    this.TraceEventSource.Kernel.EventTraceHeader +=
                        headerData =>
                        {
                            if (headerData.BuffersLost > 0)
                            {
                                this.TotalBuffersLost += headerData.BuffersLost;
                                if (this.BuffersLost != null)
                                {
                                    this.BuffersLost(this.CurrentSessionName, headerData.BuffersLost);
                                }
                            }
                        };
                }

                this.ProcessEvents();
            }
        }

        private ICollection<string> filenames;
        #endregion
    }
}