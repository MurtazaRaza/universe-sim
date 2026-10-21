using System;
using System.Runtime.InteropServices;

namespace UniverseSim.Unity.Native
{
    /// <summary>
    /// Raw P/Invoke declarations for the core's C ABI (core/capi/include/usim/usim.h).
    /// Keep this file in sync with that header. Only blittable types cross the boundary.
    /// </summary>
    internal static class NativeMethods
    {
        /// <summary>
        /// Library name without prefix or extension. The core builds it as universesim.dylib,
        /// universesim.dll or universesim.so.
        /// </summary>
        private const string Library = "universesim";

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_abi_version")]
        internal static extern uint AbiVersion();

        // Returns a pointer to a static, null-terminated UTF-8 string owned by the library.
        // Marshalled as IntPtr on purpose: a string return type would make the runtime free it.
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_version_string")]
        internal static extern IntPtr VersionString();
    }
}
