using UnityEditor;

namespace UniverseSim.Unity.Editor
{
    /// <summary>
    /// Applies import settings to the native core library copied in by
    /// tools/scripts/build-core-for-unity.sh. The binaries and their .meta files are not committed,
    /// so the settings are enforced here instead of in a .meta file.
    /// </summary>
    /// <remarks>
    /// Two entry points are needed. OnPreprocessAsset covers every import that happens while this
    /// editor assembly is loaded. The InitializeOnLoad pass covers a plugin that was imported before
    /// this assembly was compiled (for example on the first open of a fresh clone), which no
    /// preprocessor saw.
    /// </remarks>
    [InitializeOnLoad]
    internal sealed class NativePluginImportSettings : AssetPostprocessor
    {
        private const string PluginFolder = "Assets/UniverseSim/Plugins";

        static NativePluginImportSettings()
        {
            EditorApplication.delayCall += ReapplyToImportedPlugins;
        }

        private void OnPreprocessAsset()
        {
            if (assetImporter is PluginImporter importer && assetPath.StartsWith(PluginFolder + "/"))
            {
                Apply(importer, assetPath);
            }
        }

        private static void ReapplyToImportedPlugins()
        {
            if (!AssetDatabase.IsValidFolder(PluginFolder))
            {
                return;
            }

            foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { PluginFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is PluginImporter importer && Apply(importer, path))
                {
                    importer.SaveAndReimport();
                }
            }
        }

        /// <summary>Applies the settings for the plugin at the given path. Returns true if anything changed.</summary>
        private static bool Apply(PluginImporter importer, string path)
        {
            // Universal binary (arm64 + x86_64) built by the unity-macos CMake preset.
            if (path.EndsWith(".dylib"))
            {
                var upToDate =
                    !importer.GetCompatibleWithAnyPlatform() &&
                    importer.GetCompatibleWithEditor() &&
                    importer.GetEditorData("OS") == "OSX" &&
                    importer.GetEditorData("CPU") == "AnyCPU" &&
                    importer.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX) &&
                    importer.GetPlatformData(BuildTarget.StandaloneOSX, "CPU") == "AnyCPU";
                if (upToDate)
                {
                    return false;
                }

                importer.SetCompatibleWithAnyPlatform(false);
                importer.SetCompatibleWithEditor(true);
                importer.SetEditorData("OS", "OSX");
                importer.SetEditorData("CPU", "AnyCPU");
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, true);
                importer.SetPlatformData(BuildTarget.StandaloneOSX, "CPU", "AnyCPU");
                return true;
            }

            return false;
        }
    }
}
