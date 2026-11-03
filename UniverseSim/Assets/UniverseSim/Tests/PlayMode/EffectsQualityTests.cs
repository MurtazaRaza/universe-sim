using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UniverseSim.Unity.Native;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UniverseSim.Unity.Tests.PlayMode
{
    /// <summary>
    /// The effects tiers (A-13, NFR-11) in the main scene: batch runs keep the high tier; the low tier turns off the
    /// bloom, the map smoothing and the star sparkles, drops them from the "what is exaggerated" panel and says so in
    /// the style indicator; the medium tier keeps a lighter bloom; and no tier changes a position, the snapshot or a
    /// value. Halos and the surface sky, which show a sourced property, stay at every tier.
    /// </summary>
    public class EffectsQualityTests
    {
        private const string FixedTime = "2026-10-21T12:00:00 UTC";
        private StylePreference preference;
        private SimulationHost host;
        private CameraRig rig;
        private EffectsQuality quality;
        private ExaggerationRegister register;
        private StylizedCamera stylizedCamera;
        private StylizedAtmospheres atmospheres;

        private CoreSimulation Sim => host.Simulation;

        [OneTimeSetUp]
        public void KeepPreference() => preference = StylePreference.Keep();

        [OneTimeTearDown]
        public void RestorePreference() => preference.Restore();

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            VisualStyleSettings.SavePreference(VisualStyle.Stylized);
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            host = Object.FindAnyObjectByType<SimulationHost>();
            rig = Object.FindAnyObjectByType<CameraRig>();
            quality = Object.FindAnyObjectByType<EffectsQuality>();
            register = Object.FindAnyObjectByType<ExaggerationRegister>();
            stylizedCamera = Object.FindAnyObjectByType<StylizedCamera>();
            atmospheres = Object.FindAnyObjectByType<StylizedAtmospheres>();
            Assert.That(host.Simulation, Is.Not.Null, host.LoadError);
            Assert.That(quality, Is.Not.Null);
            Sim.SetPaused(true);
            Assert.That(Sim.TryParseTime(FixedTime, out var t, out var error), error);
            Sim.SetTime(t);
            yield return null;
        }

        [TearDown]
        public void BackToHigh()
        {
            if (quality != null)
            {
                quality.SetFixed(EffectsTier.High, remember: false);
            }
        }

        private List<string> ActiveSubjects()
        {
            var list = new List<Exaggeration>();
            register.CollectActive(Camera.main, list);
            return list.Select(e => e.Subject).ToList();
        }

        [UnityTest]
        public IEnumerator TheLowTierLightensTheCostlyEffectsAndChangesNoValue()
        {
            Assert.That(Application.isBatchMode, "these runs are batch runs");
            Assert.That(quality.Tier, Is.EqualTo(EffectsTier.High), "batch runs keep the high tier");
            Assert.That(quality.Automatic, Is.False);
            var earth = Sim.IndexOf(UsimBodyId.Naif(399));
            rig.JumpTo(earth, Sim.Bodies[earth].MaxRadiusKm * 4.0, 0.6, 0.3);
            rig.Select(earth);
            yield return new WaitForSecondsRealtime(0.3f);

            var high = ActiveSubjects();
            Assert.That(high, Does.Contain("Glow").And.Contain("Star sparkles").And.Contain("Atmosphere halos"));
            Assert.That(stylizedCamera.Bloom.active);
            Assert.That(stylizedCamera.Bloom.highQualityFiltering.value);
            Assert.That(Shader.GetGlobalVector("_UsimMapPaint").x, Is.GreaterThan(0f));
            Assert.That(Shader.GetGlobalVector("_UsimStarSparkle").x, Is.GreaterThan(0f));
            var request = host.Request;
            var snapshot = host.Snapshots.Take(host.SnapshotCount).ToArray();
            var positions = host.Views.Select(v => v.transform.position).ToArray();

            quality.SetFixed(EffectsTier.Low, remember: false);
            yield return new WaitForSecondsRealtime(0.3f);
            var low = ActiveSubjects();
            Assert.That(low, Does.Not.Contain("Glow").And.Not.Contain("Star sparkles"));
            Assert.That(low, Does.Contain("Atmosphere halos").And.Contain("Surface maps"),
                        "effects that show a sourced property, and cheap ones, stay");
            Assert.That(stylizedCamera.Bloom.active, Is.False);
            Assert.That(Shader.GetGlobalVector("_UsimMapPaint").x, Is.Zero, "no map smoothing");
            Assert.That(Shader.GetGlobalVector("_UsimMapGrade"), Is.Not.EqualTo(Vector4.zero), "the grade stays");
            Assert.That(Shader.GetGlobalVector("_UsimStarSparkle").x, Is.Zero);
            Assert.That(atmospheres.TryGetHalo(host.Views[earth], out var halo, out _) && halo.enabled);
            var indicator = Object.FindAnyObjectByType<UIDocument>().rootVisualElement.Q<Label>("style-indicator");
            Assert.That(indicator.text, Is.EqualTo("Stylized style (effects low)"));

            // Nothing the core gives changes with the tier.
            Assert.That(host.Request.OriginKm.X, Is.EqualTo(request.OriginKm.X));
            Assert.That(host.Request.KmPerRenderUnit, Is.EqualTo(request.KmPerRenderUnit));
            var after = host.Snapshots.Take(host.SnapshotCount).ToArray();
            Assert.That(after.Length, Is.EqualTo(snapshot.Length));
            for (var i = 0; i < after.Length; ++i)
            {
                Assert.That(after[i].RenderPosition.X, Is.EqualTo(snapshot[i].RenderPosition.X));
                Assert.That(after[i].LightDirection.Y, Is.EqualTo(snapshot[i].LightDirection.Y));
                Assert.That(host.Views[i].transform.position, Is.EqualTo(positions[i]));
            }

            // The medium tier keeps a lighter bloom and leaves out the map smoothing.
            quality.SetFixed(EffectsTier.Medium, remember: false);
            yield return null;
            Assert.That(stylizedCamera.Bloom.active);
            Assert.That(stylizedCamera.Bloom.highQualityFiltering.value, Is.False);
            Assert.That(Shader.GetGlobalVector("_UsimMapPaint").x, Is.Zero);
            Assert.That(Shader.GetGlobalVector("_UsimStarSparkle").x, Is.GreaterThan(0f));
            Assert.That(indicator.text, Is.EqualTo("Stylized style (effects medium)"));

            quality.SetFixed(EffectsTier.High, remember: false);
            yield return null;
            Assert.That(indicator.text, Is.EqualTo("Stylized style"));
            Assert.That(Shader.GetGlobalVector("_UsimMapPaint").x, Is.GreaterThan(0f));
        }
    }
}
