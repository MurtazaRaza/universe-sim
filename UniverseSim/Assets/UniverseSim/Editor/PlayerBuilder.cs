using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UniverseSim.Unity.Editor
{
    /// <summary>
    /// Builds the viewer as a Windows x64 player into <c>UniverseSim/Builds/Windows/</c> (ignored by git), for the
    /// frame-rate measurement of A-13 (<see cref="PerformanceBenchmark"/>) and for trying the viewer outside the Editor.
    /// Copy the packs into StreamingAssets first (<c>tools/scripts/copy-pack-to-unity.sh</c>). Menu: UniverseSim >
    /// Build Windows Player; batch: <c>-executeMethod UniverseSim.Unity.Editor.PlayerBuilder.BuildWindows</c>.
    /// </summary>
    public static class PlayerBuilder
    {
        public const string MainScenePath = "Assets/UniverseSim/Scenes/Main.unity";

        /// <summary>The player's path, relative to the Unity project.</summary>
        public const string WindowsPlayerPath = "Builds/Windows/UniverseSim.exe";

        [MenuItem("UniverseSim/Build Windows Player")]
        public static void BuildWindows()
        {
            var packs = Path.Combine(Application.streamingAssetsPath, "UniverseSim");
            if (!Directory.Exists(packs) || Directory.GetFiles(packs, "*.uspack").Length == 0)
            {
                throw new FileNotFoundException("No pack in StreamingAssets/UniverseSim: run tools/scripts/copy-pack-to-unity.sh",
                                                packs);
            }
            var options = new BuildPlayerOptions
            {
                scenes = new[] { MainScenePath },
                locationPathName = WindowsPlayerPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"UniverseSim: player build {summary.result}, {summary.totalSize / (1024 * 1024)} MB, " +
                      $"{summary.totalErrors} errors, at {Path.GetFullPath(WindowsPlayerPath)}");
            if (summary.result != BuildResult.Succeeded)
            {
                throw new System.InvalidOperationException("The Windows player build failed: " + summary.result);
            }
        }
    }
}
