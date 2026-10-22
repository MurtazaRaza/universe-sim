using System.Collections.Generic;
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
        public const uint ExpectedAbiVersion = 3;

        /// <summary>ABI version reported by the loaded native library.</summary>
        public static uint AbiVersion => NativeMethods.AbiVersion();

        /// <summary>Core library version as "major.minor.patch".</summary>
        public static string Version => Marshal.PtrToStringUTF8(NativeMethods.VersionString()) ?? string.Empty;

        /// <summary>True when the loaded library speaks the ABI this binding expects.</summary>
        public static bool IsCompatible => AbiVersion == ExpectedAbiVersion;

        /// <summary>
        /// The core's name for a status code, for example "out_of_coverage", or "unknown" for a value the
        /// library does not define. Useful in log messages.
        /// </summary>
        public static string StatusName(Status status) =>
            Marshal.PtrToStringUTF8(NativeMethods.StatusName((int)status)) ?? string.Empty;

        /// <summary>The message of the last failing native call on this thread, or "" if none.</summary>
        public static string LastErrorMessage =>
            Marshal.PtrToStringUTF8(NativeMethods.LastErrorMessage()) ?? string.Empty;

        /// <summary>
        /// The structs whose C# size differs from the library's (usim_struct_size), as "name: C# n, native m".
        /// Empty when the binding's layouts match the loaded library.
        /// </summary>
        public static List<string> StructSizeMismatches()
        {
            var mismatches = new List<string>();
            void Check<T>(int structId) where T : struct
            {
                var managed = Marshal.SizeOf<T>();
                var native = NativeMethods.StructSize(structId);
                if (managed != native)
                {
                    mismatches.Add($"{typeof(T).Name}: C# {managed}, native {native}");
                }
            }

            Check<UsimVec3d>(NativeConstants.StructVec3d);
            Check<UsimQuatd>(NativeConstants.StructQuatd);
            Check<UsimBodyId>(NativeConstants.StructBodyId);
            Check<UsimInstant>(NativeConstants.StructInstant);
            Check<UsimFrame>(NativeConstants.StructFrame);
            Check<UsimBodyInfo>(NativeConstants.StructBodyInfo);
            Check<UsimQuantity>(NativeConstants.StructQuantity);
            Check<UsimSource>(NativeConstants.StructSource);
            Check<UsimClockState>(NativeConstants.StructClockState);
            Check<UsimState>(NativeConstants.StructState);
            Check<UsimSnapshotRequest>(NativeConstants.StructSnapshotRequest);
            Check<UsimBodySnapshot>(NativeConstants.StructBodySnapshot);
            Check<UsimCoverage>(NativeConstants.StructCoverage);
            Check<UsimStar>(NativeConstants.StructStar);
            Check<UsimRing>(NativeConstants.StructRing);
            return mismatches;
        }
    }
}
