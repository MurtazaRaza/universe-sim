using NUnit.Framework;
using UnityEditor;

namespace UniverseSim.Unity.Tests.EditMode
{
    public class NativePluginImportTests
    {
        private const string MacOSPluginPath = "Assets/UniverseSim/Plugins/macOS/universesim.dylib";
        private const string WindowsPluginPath = "Assets/UniverseSim/Plugins/Windows/x86_64/universesim.dll";

        [Test]
        public void MacOSPluginIsEnabledForEditorAndStandaloneOnly()
        {
            var importer = AssetImporter.GetAtPath(MacOSPluginPath) as PluginImporter;
            if (importer == null)
            {
                Assert.Ignore($"{MacOSPluginPath} not found. Run tools/scripts/build-core-for-unity.sh on macOS first.");
            }

            Assert.That(importer.GetCompatibleWithAnyPlatform(), Is.False);
            Assert.That(importer.GetCompatibleWithEditor(), Is.True);
            Assert.That(importer.GetEditorData("OS"), Is.EqualTo("OSX"));
            Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX), Is.True);
            Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64), Is.False);
        }

        [Test]
        public void WindowsPluginIsEnabledForEditorAndWindows64Only()
        {
            var importer = AssetImporter.GetAtPath(WindowsPluginPath) as PluginImporter;
            if (importer == null)
            {
                Assert.Ignore($"{WindowsPluginPath} not found. Run tools/scripts/build-core-for-unity.ps1 on Windows first.");
            }

            Assert.That(importer.GetCompatibleWithAnyPlatform(), Is.False);
            Assert.That(importer.GetCompatibleWithEditor(), Is.True);
            Assert.That(importer.GetEditorData("OS"), Is.EqualTo("Windows"));
            Assert.That(importer.GetEditorData("CPU"), Is.EqualTo("x86_64"));
            Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64), Is.True);
            Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows), Is.False);
            Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX), Is.False);
        }
    }
}
