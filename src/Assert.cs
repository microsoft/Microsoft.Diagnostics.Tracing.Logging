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
    using System.Diagnostics;

    /// <summary>
    /// Assert methods which utilize our internal logging
    /// </summary>
    /// <remarks>
    /// The single method provided (Assert) is intended to be used as a substitute for Trace.Assert or Debug.Assert.
    /// It provides a wrapper around these and also ensures that assertions are always "process killing" no matter
    /// what happens. It also ensures that "process killer" assertions get emitted to the console and to the ETW
    /// stream.
    /// </remarks>
    public static class LogAssert
    {
        /// <summary>
        /// Evaluate the given expression and raise a process-murdering error if it is false
        /// </summary>
        /// <param name="expr">Anything that evaluates to a boolean. Get creative!</param>
        public static void Assert(bool expr)
        {
            Assert(expr, string.Empty);
        }

        /// <summary>
        /// Evaluate the given expression and raise a process-murdering error if it is false
        /// </summary>
        /// <param name="expr">Anything that evaluates to a boolean. Get creative!</param>
        /// <param name="message">A message to log should expr be false. Express yourself!</param>
        public static void Assert(bool expr, string message)
        {
            if (expr == false)
            {
                InternalLogger.Write.AssertionFailed(message, Environment.StackTrace);
#if DEBUG
                Debug.Assert(false, message);
#else
                LogManager.Shutdown();
                Environment.Exit(-42);
#endif
            }
        }

        /// <summary>
        /// Evaluate the given expression and raise a process-murdering error if it is false
        /// </summary>
        /// <param name="expr">Anything that evaluates to a boolean. Get creative!</param>
        /// <param name="format">The format of the scintillating message which will crash your process</param>
        /// <param name="args">One or more captivating arguments for your format</param>
        public static void Assert(bool expr, string format, params object[] args)
        {
            if (expr == false)
            {
                try
                {
                    Assert(false, string.Format(format, args));
                }
                catch (FormatException)
                {
                    Assert(false,
                           string.Format(
                                         "somebody goofed up their string format but really wanted to assert! here 'tis: {0}",
                                         format));
                }
            }
        }
    }
}