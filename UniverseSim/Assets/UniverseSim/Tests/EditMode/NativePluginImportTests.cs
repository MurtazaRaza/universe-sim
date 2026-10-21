using NUnit.Framework;
using UnityEditor;

namespace UniverseSim.Unity.Tests.EditMode
{
    public class NativePluginImportTests
    {
        private const string MacOSPluginPath = "Assets/UniverseSim/Plugins/macOS/universesim.dylib";

        [Test]
        public void MacOSPluginIsEnabledForEditorAndStandaloneOnly()
        {
            var importer = AssetImporter.GetAtPath(MacOSPluginPath) as PluginImporter;
            if (importer == null)
            {
                Assert.Ignore($"{MacOSPluginPath} not found. Run tools/scripts/build-core-for-unity.sh first.");
            }

            Assert.That(importer.GetCompatibleWithAnyPlatform(), Is.False);
            Assert.That(importer.GetCompatibleWithEditor(), Is.True);
            Assert.That(importer.GetEditorData("OS"), Is.EqualTo("OSX"));
            Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX), Is.True);
            Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64), Is.False);
        }
    }
}
