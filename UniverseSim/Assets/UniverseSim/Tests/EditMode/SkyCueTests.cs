using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace UniverseSim.Unity.Tests.EditMode
{
    /// <summary>
    /// Sky cues (ADR-0018): the manifest the pipeline wrote parses, every hue it names has a display color, every
    /// cue carries its source and the source's words, and the main scene's host reads it.
    /// </summary>
    public class SkyCueTests
    {
        private const string ManifestPath = "Assets/UniverseSim/Data/SkyCues.json";

        private static SkyCues LoadCommitted()
        {
            var manifest = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            Assert.That(manifest, Is.Not.Null, ManifestPath + " (run usim-pipeline sky-cues)");
            return SkyCues.Parse(manifest.text);
        }

        [Test]
        public void TheCommittedCuesParseWithTheirSources()
        {
            var cues = LoadCommitted();
            Assert.That(cues.All, Is.Not.Empty);
            foreach (var cue in cues.All)
            {
                Assert.That(SkyCues.Hues.ContainsKey(cue.hue), cue.body + ": " + cue.hue);
                Assert.That(cue.cue, Is.EqualTo(SkyCues.DaytimeSky).Or.EqualTo(SkyCues.Twilight));
                Assert.That(cue.sourceId, Does.StartWith("S-"));
                Assert.That(cue.tier, Is.EqualTo(1).Or.EqualTo(2));
                Assert.That(cue.quote, Is.Not.Empty);
                Assert.That(cue.url, Does.StartWith("https://"));
                Assert.That(cues.TryGet(cue.naifId, cue.cue, out var found) && found == cue);
                StringAssert.Contains(cue.quote, cue.Citation);
                StringAssert.StartsWith(cue.sourceId + (cue.tier == 2 ? " (Tier 2)" : ":"), cue.Citation);
            }
        }

        [Test]
        public void TheHueVocabularyIsThePipelines()
        {
            // tools/pipeline/usim_pipeline/sky_cues.py HUES: each needs a display color here.
            Assert.That(SkyCues.Hues.Keys.OrderBy(k => k),
                        Is.EqualTo(new[] { "blue", "brownish_gray", "orange", "red_and_yellow" }));
        }

        [Test]
        public void UnknownHuesAndEmptyManifestsGiveNoCues()
        {
            Assert.That(SkyCues.Parse(null).All, Is.Empty);
            Assert.That(SkyCues.Parse("  ").All, Is.Empty);
            LogAssert.Expect(LogType.Warning, "UniverseSim: sky cue for Test skipped (unknown cue or hue)");
            var cues = SkyCues.Parse(
                "{\"cues\":[{\"naifId\":1001,\"body\":\"Test\",\"cue\":\"twilight\",\"hue\":\"green\"}," +
                "{\"naifId\":1001,\"body\":\"Test\",\"cue\":\"daytime_sky\",\"hue\":\"blue\",\"sourceId\":\"S-TEST\"," +
                "\"tier\":2,\"reference\":\"A page\",\"quote\":\"Words.\"}]}");
            Assert.That(cues.All.Count, Is.EqualTo(1));
            Assert.That(cues.TryGet(1001, SkyCues.Twilight, out _), Is.False);
            Assert.That(cues.ColorOf(1001, SkyCues.Twilight, Color.black), Is.EqualTo(Color.black));
            Assert.That(cues.ColorOf(1001, SkyCues.DaytimeSky, Color.black), Is.EqualTo(SkyCues.Hues["blue"]));
            Assert.That(cues.All[0].Citation, Is.EqualTo("S-TEST (Tier 2): A page: \"Words.\""));
            Assert.That(cues.All[0].HueName, Is.EqualTo("blue"));
        }

        [Test]
        public void TheMainSceneHostReadsTheManifest()
        {
            var scene = EditorSceneManager.OpenScene("Assets/UniverseSim/Scenes/Main.unity", OpenSceneMode.Additive);
            try
            {
                var host = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<SimulationHost>(true))
                    .Single();
                var field = new SerializedObject(host).FindProperty("skyCueManifest");
                Assert.That(field.objectReferenceValue,
                            Is.EqualTo(AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath)));
            }
            finally
            {
                if (SceneManager.sceneCount > 1)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
    }
}
