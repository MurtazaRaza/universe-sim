using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
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
    /// The visual style switch (A-02, SPEC FR-REN-12): the viewer starts in the stylized style, the HUD toggle
    /// switches styles and the indicator shows the one in use, the choice is remembered between sessions, and
    /// switching changes nothing the core computes or the HUD reports.
    /// </summary>
    public class VisualStyleTests
    {
        private const string FixedTime = "2026-10-21T12:00:00 UTC";

        private StylePreference preference;
        private SimulationHost host;
        private CameraRig rig;
        private VisualStyleSettings settings;
        private VisualElement hud;

        [OneTimeSetUp]
        public void KeepPreference() => preference = StylePreference.Keep();

        [OneTimeTearDown]
        public void RestorePreference() => preference.Restore();

        // Starts a session: loads the main scene and stops the clock at a fixed time.
        private IEnumerator Load()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            host = Object.FindAnyObjectByType<SimulationHost>();
            rig = Object.FindAnyObjectByType<CameraRig>();
            settings = Object.FindAnyObjectByType<VisualStyleSettings>();
            hud = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
            Assert.That(settings, Is.Not.Null);
            Assert.That(host.Simulation, Is.Not.Null, host.LoadError);
            host.Simulation.SetPaused(true);
            Assert.That(host.Simulation.TryParseTime(FixedTime, out var t, out var error), error);
            host.Simulation.SetTime(t);
            yield return null;
        }

        private Toggle RealisticToggle => hud.Q<Toggle>("toggle-realistic");

        private Label Indicator => hud.Q<Label>("style-indicator");

        private void AssertShows(VisualStyle style)
        {
            Assert.That(settings.Style, Is.EqualTo(style));
            Assert.That(Shader.IsKeywordEnabled(VisualStyleSettings.StylizedKeyword),
                        Is.EqualTo(style == VisualStyle.Stylized));
            Assert.That(Indicator.text, Is.EqualTo(VisualStyleSettings.DisplayName(style)));
            Assert.That(Indicator.ClassListContains("realistic"), Is.EqualTo(style == VisualStyle.Realistic));
            Assert.That(RealisticToggle.value, Is.EqualTo(style == VisualStyle.Realistic));
        }

        [UnityTest]
        public IEnumerator TheViewerStartsInTheStylizedStyle()
        {
            PlayerPrefs.DeleteKey(VisualStyleSettings.PreferenceKey);
            yield return Load();
            AssertShows(VisualStyle.Stylized);
            Assert.That(Indicator.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
        }

        [UnityTest]
        public IEnumerator TheHudSwitchesTheStyleAndTheChoiceIsRemembered()
        {
            PlayerPrefs.DeleteKey(VisualStyleSettings.PreferenceKey);
            yield return Load();
            var changes = new List<VisualStyle>();
            settings.Changed += changes.Add;
            RealisticToggle.value = true; // as a click does
            yield return null;
            AssertShows(VisualStyle.Realistic);
            Assert.That(changes, Is.EqualTo(new[] { VisualStyle.Realistic }));

            // The next session starts in the realistic style, and one click goes back.
            yield return Load();
            AssertShows(VisualStyle.Realistic);
            RealisticToggle.value = false;
            yield return null;
            AssertShows(VisualStyle.Stylized);
            Assert.That(VisualStyleSettings.LoadPreference(), Is.EqualTo(VisualStyle.Stylized));
        }

        // Everything the style must not change: the snapshot and the request bit for bit, the body transforms
        // and light directions, the camera, and every HUD text, value and label position except what shows the
        // style itself (its indicator and toggle, and the "what is exaggerated" panel, A-03).
        private sealed class Observed
        {
            public byte[] Snapshots;
            public byte[] Request;
            public List<(Vector3 Position, Quaternion Rotation, Vector3 Scale, Vector4 Light)> Bodies;
            public (Vector3 Position, Quaternion Rotation) Camera;
            public List<string> Hud;
        }

        private Observed Observe()
        {
            var cam = Camera.main.transform;
            var request = new[] { host.Request };
            var snapshots = new ReadOnlySpan<UsimBodySnapshot>(host.Snapshots, 0, host.SnapshotCount);
            string Place(VisualElement anchor) =>
                $"{anchor.style.display.value} " +
                anchor.style.translate.value.x.value.ToString("R", CultureInfo.InvariantCulture) + " " +
                anchor.style.translate.value.y.value.ToString("R", CultureInfo.InvariantCulture);
            var exaggerations = hud.Q("exaggerations");
            var hudValues = hud.Query<Label>().ToList()
                .Where(l => l != Indicator && !exaggerations.Contains(l))
                .Select(l => "label: " + l.text)
                .Concat(hud.Query<Toggle>().ToList().Where(t => t != RealisticToggle)
                            .Select(t => $"{t.name}: {t.value}"))
                .Concat(hud.Query<TextField>().ToList().Select(f => $"{f.name}: {f.value}"))
                .Concat(hud.Query<DropdownField>().ToList().Select(f => $"{f.name}: {f.value}"))
                .Concat(hud.Q("labels").Children().Select(a => "anchor: " + Place(a)))
                .ToList();
            return new Observed
            {
                Snapshots = MemoryMarshal.AsBytes(snapshots).ToArray(),
                Request = MemoryMarshal.AsBytes(new ReadOnlySpan<UsimSnapshotRequest>(request)).ToArray(),
                Bodies = host.Views
                    .Select(v => (v.transform.position, v.transform.rotation, v.transform.localScale, v.LightDirection))
                    .ToList(),
                Camera = (cam.position, cam.rotation),
                Hud = hudValues,
            };
        }

        [UnityTest]
        public IEnumerator SwitchingStyleChangesNothingTheCoreOrTheHudReports()
        {
            PlayerPrefs.DeleteKey(VisualStyleSettings.PreferenceKey);
            yield return Load();
            var sim = host.Simulation;
            var earth = sim.IndexOf(UsimBodyId.Naif(399));
            rig.JumpTo(earth, sim.Bodies[earth].MaxRadiusKm * 4.0, 0.6, 0.3);
            rig.Select(earth);
            yield return new WaitForSecondsRealtime(0.3f); // the HUD refreshes its texts ten times a second
            var before = Observe();
            Assert.That(before.Snapshots.Length, Is.GreaterThan(0));
            // The info panel shows the Earth's values.
            Assert.That(hud.Q<ScrollView>("info-values").Query<Label>().ToList().Count, Is.GreaterThan(10));

            foreach (var next in new[] { VisualStyle.Realistic, VisualStyle.Stylized })
            {
                RealisticToggle.value = next == VisualStyle.Realistic;
                yield return new WaitForSecondsRealtime(0.3f);
                AssertShows(next);
                var after = Observe();
                Assert.That(after.Snapshots, Is.EqualTo(before.Snapshots), $"{next}: snapshot");
                Assert.That(after.Request, Is.EqualTo(before.Request), $"{next}: snapshot request");
                Assert.That(after.Bodies, Is.EqualTo(before.Bodies), $"{next}: body transforms and light directions");
                Assert.That(after.Camera, Is.EqualTo(before.Camera), $"{next}: camera");
                Assert.That(after.Hud, Is.EqualTo(before.Hud), $"{next}: HUD readouts");
            }
        }
    }
}
