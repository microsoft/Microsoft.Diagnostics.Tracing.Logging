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
    using System.Diagnostics.CodeAnalysis;

    public sealed partial class LogManager
    {
        /// <summary>
        /// Get the activity ID of the current thread
        /// </summary>
        /// <param name="activityId">The activity ID (may be Guid.Empty if none is currently set)</param>
        /// <returns>true if the OS was able to retrieve the value, false otherwise</returns>
        [SuppressMessage("Microsoft.Design", "CA1021:AvoidOutParameters", MessageId = "0#")]
        public static bool GetActivityId(out Guid activityId)
        {
            activityId = Guid.Empty;
            return (NativeMethods.ERROR_SUCCESS == NativeMethods.EventActivityIdControl(
                                                                                        (int)
                                                                                        NativeMethods.ActivityControl
                                                                                                     .EVENT_ACTIVITY_CTRL_GET_ID,
                                                                                        ref activityId));
        }

        /// <summary>
        /// Set the activity ID of the current thread
        /// </summary>
        /// <param name="activityId">The new activity ID for this thread</param>
        /// <returns>true if the OS was able to assign the value, false otherwise</returns>
        public static bool SetActivityId(Guid activityId)
        {
            return (NativeMethods.ERROR_SUCCESS == NativeMethods.EventActivityIdControl(
                                                                                        (int)
                                                                                        NativeMethods.ActivityControl
                                                                                                     .EVENT_ACTIVITY_CTRL_SET_ID,
                                                                                        ref activityId));
        }

        /// <summary>
        /// Clear the activity ID of the current thread (sets it to Guid.Empty)
        /// </summary>
        /// <returns>true if the OS was able to assign the value, false otherwise</returns>
        public static bool ClearActivityId()
        {
            return SetActivityId(Guid.Empty);
        }

        /// <summary>
        /// Generate a new activity ID and assign it as the activity ID of the current thread
        /// </summary>
        /// <param name="activityId">Storage for the new activity ID</param>
        /// <returns>true if the OS was able to create and assign the value, false otherwise</returns>
        [SuppressMessage("Microsoft.Design", "CA1021:AvoidOutParameters", MessageId = "0#")]
        public static bool GetNewActivityId(out Guid activityId)
        {
            activityId = Guid.Empty;
            return (NativeMethods.ERROR_SUCCESS == NativeMethods.EventActivityIdControl(
                                                                                        (int)
                                                                                        NativeMethods.ActivityControl
                                                                                                     .EVENT_ACTIVITY_CTRL_CREATE_ID,
                                                                                        ref activityId));
        }

        /// <summary>
        /// Assign the passed in activity ID to the current thread, and pass back the previous activity ID
        /// </summary>
        /// <param name="activityId">The new activity ID to assign and the previous activity ID upon successful completion</param>
        /// <returns>true if the OS was able to retrieve the previous value and assign the new value, false otherwise</returns>
        [SuppressMessage("Microsoft.Design", "CA1045:DoNotPassTypesByReference", MessageId = "0#")]
        public static bool SwapActivityId(ref Guid activityId)
        {
            return (NativeMethods.ERROR_SUCCESS == NativeMethods.EventActivityIdControl(
                                                                                        (int)
                                                                                        NativeMethods.ActivityControl
                                                                                                     .EVENT_ACTIVITY_CTRL_GET_SET_ID,
                                                                                        ref activityId));
        }
    }
}