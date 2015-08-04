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
    using System.Runtime.InteropServices;

    internal sealed class NativeMethods
    {
        internal const uint ERROR_SUCCESS = 0;

        [DllImport("Kernel32", EntryPoint = "GetCurrentThreadId", ExactSpelling = true)]
        public static extern Int32 GetCurrentWin32ThreadId();

        #region Activity ID
        [DllImport("advapi32.dll", ExactSpelling = true, EntryPoint = "EventActivityIdControl",
            CharSet = CharSet.Unicode)]
        internal static extern uint EventActivityIdControl([In] int ControlCode, [In, Out] ref Guid ActivityId);

        internal enum ActivityControl : uint
        {
            EVENT_ACTIVITY_CTRL_GET_ID = 1,
            EVENT_ACTIVITY_CTRL_SET_ID = 2,
            EVENT_ACTIVITY_CTRL_CREATE_ID = 3,
            EVENT_ACTIVITY_CTRL_GET_SET_ID = 4,
            EVENT_ACTIVITY_CTRL_CREATE_SET_ID = 5 // not actually exposed
        }
        #endregion

        #region UAC Query
        public const UInt32 TOKEN_QUERY = 0x0008;

        // see http://msdn.microsoft.com/en-us/library/windows/desktop/bb530718(v=vs.85).aspx
        public enum TOKEN_ELEVATION_TYPE
        {
            TokenElevationTypeDefault = 1,
            TokenElevationTypeFull,
            TokenElevationTypeLimited
        }

        // See http://msdn.microsoft.com/en-us/library/windows/desktop/bb530717(v=vs.85).aspx
        public struct TOKEN_ELEVATION
        {
            public uint TokenIsElevated;
        }

        // see http://msdn.microsoft.com/en-us/library/windows/desktop/aa379626(v=vs.85).aspx
        public enum TOKEN_INFORMATION_CLASS
        {
            TokenUser = 1,
            TokenGroups,
            TokenPrivileges,
            TokenOwner,
            TokenPrimaryGroup,
            TokenDefaultDacl,
            TokenSource,
            TokenType,
            TokenImpersonationLevel,
            TokenStatistics,
            TokenRestrictedSids,
            TokenSessionId,
            TokenGroupsAndPrivileges,
            TokenSessionReference,
            TokenSandBoxInert,
            TokenAuditPolicy,
            TokenOrigin,
            TokenElevationType,
            TokenLinkedToken,
            TokenElevation,
            TokenHasRestrictions,
            TokenAccessInformation,
            TokenVirtualizationAllowed,
            TokenVirtualizationEnabled,
            TokenIntegrityLevel,
            TokenUIAccess,
            TokenMandatoryPolicy,
            TokenLogonSid,
            MaxTokenInfoClass
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenProcessToken(IntPtr ProcessHandle, UInt32 DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool GetTokenInformation(IntPtr TokenHandle, TOKEN_INFORMATION_CLASS TokenInformationClass,
                                                      IntPtr TokenInformation, uint TokenInformationLength,
                                                      out uint ReturnLength);
        #endregion
    }
}