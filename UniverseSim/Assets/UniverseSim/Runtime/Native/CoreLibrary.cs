using System.Runtime.InteropServices;

namespace UniverseSim.Unity.Native
{
    /// <summary>Identity and compatibility of the native core library.</summary>
    public static class CoreLibrary
    {
        /// <summary>
        /// ABI version this binding was written against. Must equal USIM_ABI_VERSION in
        /// core/capi/include/usim/usim.h.
        /// </summary>
        public const uint ExpectedAbiVersion = 1;

        /// <summary>ABI version reported by the loaded native library.</summary>
        public static uint AbiVersion => NativeMethods.AbiVersion();

        /// <summary>Core library version as "major.minor.patch".</summary>
        public static string Version => Marshal.PtrToStringUTF8(NativeMethods.VersionString()) ?? string.Empty;

        /// <summary>True when the loaded library speaks the ABI this binding expects.</summary>
        public static bool IsCompatible => AbiVersion == ExpectedAbiVersion;
    }
}
