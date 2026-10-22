using System;
using UniverseSim.Unity.Native;
using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>
    /// Verifies at startup that the native core library loads and speaks the expected ABI.
    /// </summary>
    internal static class CoreStartupCheck
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Run()
        {
            try
            {
                if (CoreLibrary.IsCompatible)
                {
                    var mismatches = CoreLibrary.StructSizeMismatches();
                    if (mismatches.Count == 0)
                    {
                        Debug.Log($"UniverseSim core {CoreLibrary.Version} loaded (ABI {CoreLibrary.AbiVersion}).");
                    }
                    else
                    {
                        Debug.LogError("UniverseSim core struct layouts differ from the binding: " + string.Join("; ", mismatches));
                    }
                }
                else
                {
                    Debug.LogError(
                        $"UniverseSim core ABI mismatch: library reports {CoreLibrary.AbiVersion}, " +
                        $"binding expects {CoreLibrary.ExpectedAbiVersion}. Rebuild the core and update the binding.");
                }
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                Debug.LogError(
                    "UniverseSim core library could not be loaded. Run tools/scripts/build-core-for-unity.sh " +
                    $"and restart the Editor. ({e.Message})");
            }
        }
    }
}
