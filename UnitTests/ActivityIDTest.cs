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
    using System;

    using NUnit.Framework;

    [TestFixture]
    public class ActivityIdTests
    {
        [Test]
        public void TheOnlyTest()
        {
            Guid id = Guid.NewGuid();
            Guid outId = Guid.Empty;

            LogManager.SetActivityId(id);
            LogManager.GetActivityId(out outId);
            Assert.AreEqual(id, outId);
            LogManager.GetNewActivityId(out outId);
            Assert.AreNotEqual(id, outId);

            LogManager.ClearActivityId();
            LogManager.GetActivityId(out outId);
            Assert.AreEqual(outId, Guid.Empty);

            id = Guid.NewGuid();
            LogManager.SwapActivityId(ref id);
            LogManager.GetActivityId(out outId);
            Assert.AreEqual(id, Guid.Empty);
            Assert.AreNotEqual(outId, Guid.Empty);
        }
    }
}