using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    /// Play (ADR-0019, A-17 to A-20): the start near the Earth in the ship, the info panel's facts in human terms from
    /// the core's values, the throttle, timeline and rush of the time controls, a logbook goal reached once with its
    /// toast and saved progress, the speed cues mid-trip only, and the arrival banner. None of it moves a body. With
    /// USIM_SCREENSHOT_DIR set it saves views for review in a "play" folder.
    /// </summary>
    public class PlayTests
    {
        private const string FixedTime = "2026-10-21T12:00:00 UTC";
        private StylePreference preference;
        private SimulationHost host;
        private CameraRig rig;
        private ShipPilot pilot;
        private HudController hud;
        private LogbookHud logbookHud;
        private Logbook logbook;
        private SpeedCues cues;
        private StartExperience start;
        private VisualElement root;

        private CoreSimulation Sim => host.Simulation;

        [OneTimeSetUp]
        public void KeepPreference() => preference = StylePreference.Keep();

        [OneTimeTearDown]
        public void RestorePreference() => preference.Restore();

        [TearDown]
        public void RestoreDefaults()
        {
            StartExperience.Enabled = false;
            Logbook.Checking = false;
            PlayerPrefs.DeleteKey(PlayModeDefaults.LogbookKey);
        }

        private IEnumerator Load(bool startNearEarth, bool fixedTime = true)
        {
            VisualStyleSettings.SavePreference(VisualStyle.Stylized);
            StartExperience.Enabled = startNearEarth;
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            host = Object.FindAnyObjectByType<SimulationHost>();
            rig = Object.FindAnyObjectByType<CameraRig>();
            pilot = Object.FindAnyObjectByType<ShipPilot>();
            hud = Object.FindAnyObjectByType<HudController>();
            logbookHud = Object.FindAnyObjectByType<LogbookHud>();
            logbook = Object.FindAnyObjectByType<Logbook>();
            cues = Object.FindAnyObjectByType<SpeedCues>();
            start = Object.FindAnyObjectByType<StartExperience>();
            root = hud.GetComponent<UIDocument>().rootVisualElement;
            Assert.That(host.Simulation, Is.Not.Null, host.LoadError);
            Assert.That(new Object[] { pilot, logbookHud, logbook, cues, start }, Has.None.Null);
            Sim.SetPaused(true);
            if (fixedTime)
            {
                Assert.That(Sim.TryParseTime(FixedTime, out var t, out var error), error);
                Sim.SetTime(t);
            }
            yield return null;
        }

        private int Index(long naif) => Sim.IndexOf(UsimBodyId.Naif(naif));

        private static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; ++i)
            {
                yield return null;
            }
        }

        private static IEnumerator Seconds(float seconds)
        {
            var end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end)
            {
                yield return null;
            }
        }

        // Renders the camera and the HUD into one reused texture and saves it (batch mode has no screen).
        private static RenderTexture shotTarget;
        private static Texture2D shotImage;

        private static IEnumerator Screenshot(string name)
        {
            var folder = Environment.GetEnvironmentVariable("USIM_SCREENSHOT_DIR");
            if (string.IsNullOrEmpty(folder))
            {
                yield break;
            }
            folder = Path.Combine(folder, "play");
            var cam = Camera.main;
            var document = Object.FindAnyObjectByType<HudController>().GetComponent<UIDocument>();
            shotTarget ??= new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            shotImage ??= new Texture2D(1600, 900, TextureFormat.RGB24, false);
            cam.targetTexture = shotTarget;
            document.panelSettings.targetTexture = shotTarget;
            yield return null;
            yield return null;
            var previous = RenderTexture.active;
            RenderTexture.active = shotTarget;
            shotImage.ReadPixels(new Rect(0, 0, shotTarget.width, shotTarget.height), 0, 0);
            shotImage.Apply();
            RenderTexture.active = previous;
            cam.targetTexture = null;
            document.panelSettings.targetTexture = null;
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), shotImage.EncodeToPNG());
        }

        private static bool OnScreen(BodyView view)
        {
            var p = Camera.main.WorldToViewportPoint(view.transform.position);
            return p.z > 0f && p.x > 0f && p.x < 1f && p.y > 0f && p.y < 1f;
        }

        [UnityTest]
        public IEnumerator TheViewerStartsNearTheEarthInTheShipWithTheMoonInView()
        {
            yield return Load(true, false);
            yield return Frames(5);
            var earth = Index(399);
            var moon = Index(301);
            Assert.That(start.StartedNearEarth);
            Assert.That(pilot.Active, "the ship is out in the stylized style");
            Assert.That(rig.FocusIndex, Is.EqualTo(earth));
            Assert.That(rig.SelectedIndex, Is.EqualTo(moon), "the Moon is selected, so T flies there");
            var radius = Sim.Bodies[earth].MaxRadiusKm;
            Assert.That(rig.OffsetKm.Length, Is.InRange(5.0 * radius, 12.0 * radius), "a few Earth radii out");
            Assert.That(OnScreen(host.Views[earth]), "the Earth is in view");
            Assert.That(OnScreen(host.Views[moon]), "the Moon is in view");
            Assert.That(logbookHud.ToastTexts.Any(t => t.StartsWith("Welcome aboard", StringComparison.Ordinal)));
            yield return Seconds(1f);
            yield return Screenshot("01-start");
        }

        [UnityTest]
        public IEnumerator TestsKeepTheSystemView()
        {
            yield return Load(false);
            Assert.That(start.StartedNearEarth, Is.False);
            Assert.That(pilot.Active, Is.False);
            Assert.That(Sim.Bodies[rig.FocusIndex].Kind, Is.EqualTo(1), "the system view, centered on the star");
        }

        // The facts' title and text for the body at `index`, from the info panel.
        private Dictionary<string, string> Facts(int index)
        {
            rig.Select(index);
            var labels = root.Q<VisualElement>("info-facts").Query<Label>().ToList();
            var facts = new Dictionary<string, string>();
            for (var i = 0; i + 1 < labels.Count; i += 2)
            {
                facts[labels[i].text] = labels[i + 1].text;
            }
            return facts;
        }

        private double Gravity(int index)
        {
            var request = host.Request;
            Assert.That(Sim.TrySurfaceSite(Sim.Bodies[index].Id, 0.0, 0.0, 0.0, ref request, Sim.Clock.Now, out var site));
            return site.GravityKmS2;
        }

        [UnityTest]
        public IEnumerator TheInfoPanelLeadsWithFactsInHumanTerms()
        {
            yield return Load(false);
            var earth = Index(399);
            var moon = Index(301);
            var jupiter = Index(599);

            var home = Facts(earth);
            Assert.That(home.Keys, Is.SupersetOf(new[] { "Size", "Gravity", "A day", "A year", "Air" }));
            Assert.That(home["A day"], Does.Contain("24 h"), "noon to noon on the Earth, from the core's mean solar day");

            // The Moon's weight is the ratio of the core's own gravity at the two equators.
            var lunar = Facts(moon);
            var percent = (100.0 * Gravity(moon) / Gravity(earth)).ToString("0", CultureInfo.InvariantCulture);
            Assert.That(lunar["Your weight"], Does.Contain($"{percent} % of your Earth weight"));
            var width = (100.0 * Sim.Bodies[moon].MeanRadiusKm / Sim.Bodies[earth].MeanRadiusKm)
                .ToString("0", CultureInfo.InvariantCulture);
            Assert.That(lunar["Size"], Does.StartWith($"{width} % of the Earth's width"));
            Assert.That(lunar.Keys, Does.Contain("Getting there"));
            Assert.That(lunar.Keys, Does.Contain("Sunlight"));

            yield return Screenshot("02-moon-facts");

            var giant = Facts(jupiter);
            var times = (Gravity(jupiter) / Gravity(earth)).ToString("0.0", CultureInfo.InvariantCulture);
            Assert.That(giant["Your weight"], Does.Contain($"{times} times your Earth weight, at the cloud tops"));
            Assert.That(giant["Size"], Does.Contain("as wide as the Earth"));

            // The sourced values and their citations are still there, below.
            var data = root.Q<Foldout>("info-data").Query<Label>().ToList().Select(l => l.text).ToList();
            Assert.That(data, Has.Some.EqualTo("Gravitational parameter GM"));
            Assert.That(data, Has.Some.EqualTo("Sources"));
        }

        [UnityTest]
        public IEnumerator TheThrottleTimelineAndRushDriveTheClock()
        {
            yield return Load(false);
            var slider = root.Q<SliderInt>("rate-slider");
            var presets = slider.highValue + 1;
            Assert.That(presets % 2, Is.EqualTo(0));

            // The rightmost position is the fastest forward rate, the leftmost the fastest reverse.
            slider.value = slider.highValue;
            yield return null;
            var fastest = Sim.Clock.Rate;
            Assert.That(fastest, Is.GreaterThan(0.0));
            slider.value = 0;
            yield return null;
            Assert.That(Sim.Clock.Rate, Is.EqualTo(-fastest));
            slider.value = presets / 2;
            yield return null;
            Assert.That(Sim.Clock.Rate, Is.GreaterThan(0.0).And.LessThan(fastest), "the middle is slow, forward");
            var slow = Sim.Clock.Rate;

            // The timeline's middle is the middle of the coverage, through the core's instants.
            var coverage = Sim.Coverage;
            var span = CoreSimulation.SecondsBetween(coverage.Start, coverage.End);
            root.Q<Slider>("timeline").value = 0.5f;
            yield return null;
            Assert.That(CoreSimulation.SecondsBetween(coverage.Start, Sim.Clock.Now) / span, Is.EqualTo(0.5).Within(1e-6));

            // Rushing runs at the fastest rate and puts back the rate and the pause on release.
            Assert.That(Sim.Clock.Paused, Is.Not.EqualTo(0));
            hud.SetRush(true);
            Assert.That(hud.Rushing);
            Assert.That(Sim.Clock.Rate, Is.EqualTo(fastest));
            Assert.That(Sim.Clock.Paused, Is.EqualTo(0));
            hud.SetRush(false);
            Assert.That(Sim.Clock.Rate, Is.EqualTo(slow));
            Assert.That(Sim.Clock.Paused, Is.Not.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator ALogbookGoalIsReachedOnceWithAToastAndRemembered()
        {
            yield return Load(false);
            var moon = Index(301);
            Assert.That(logbook.Get("moon"), Is.Not.Null);
            logbook.ResetProgress();
            var completed = new List<string>();
            logbook.Completed += g => completed.Add(g.Id);
            Logbook.Checking = true;

            rig.JumpTo(moon, Sim.Bodies[moon].MaxRadiusKm * 3.0, 0.4, 0.2);
            yield return Seconds(0.8f);
            Assert.That(logbook.Get("moon").Done, "within 10 radii of the Moon");
            Assert.That(completed.Count(id => id == "moon"), Is.EqualTo(1));
            Assert.That(logbookHud.ToastTexts, Has.Some.EqualTo("Logbook: Fly to the Moon"));
            Assert.That(PlayerPrefs.GetString(PlayModeDefaults.LogbookKey), Does.Contain("moon"));
            Assert.That(root.Q<Button>("logbook-toggle").text, Does.StartWith($"Logbook {logbook.DoneCount}/"));

            // Staying there reaches nothing twice; the panel lists the goal as done.
            yield return Seconds(0.6f);
            Assert.That(completed.Count(id => id == "moon"), Is.EqualTo(1));
            logbookHud.TogglePanel();
            yield return Frames(2);
            Assert.That(logbookHud.PanelOpen);
            var rows = root.Q<VisualElement>("logbook-goals").Query<Label>(className: "goal-title").ToList();
            Assert.That(rows.Select(r => r.text), Has.Some.EqualTo("✓ Fly to the Moon"));
            yield return Screenshot("03-logbook");
        }

        [UnityTest]
        public IEnumerator SpeedCuesShowMidTripOnlyAndTheArrivalBannerNamesTheBody()
        {
            yield return Load(false);
            var earth = Index(399);
            var moon = Index(301);
            var cam = Camera.main;
            var baseFov = cam.fieldOfView;
            rig.JumpTo(earth, Sim.Bodies[earth].MaxRadiusKm * 6.0, 0.3, 0.2);
            yield return Frames(2);
            pilot.SetActive(true);
            yield return Seconds(0.5f);
            Assert.That(cues.Intensity, Is.LessThan(0.01f), "no cues while the ship is at rest");
            Assert.That(cues.ActiveInView(cam), Is.False);

            pilot.TravelTo(moon);
            while (pilot.State == ShipPilot.Mode.Traveling && pilot.TravelProgress < 0.5f)
            {
                yield return null;
            }
            Assert.That(cues.Intensity, Is.GreaterThan(0.5f), "strong mid-trip");
            Assert.That(cam.fieldOfView, Is.GreaterThan(baseFov));
            var listed = new List<Exaggeration>();
            Object.FindAnyObjectByType<ExaggerationRegister>().CollectActive(cam, listed);
            Assert.That(listed.Select(e => e.Subject), Has.Some.EqualTo("Speed streaks"));
            yield return Screenshot("04-mid-trip");

            while (pilot.State == ShipPilot.Mode.Traveling)
            {
                yield return null;
            }
            yield return Frames(2);
            Assert.That(logbookHud.ArrivalShown, "the banner on arrival");
            Assert.That(logbookHud.ArrivalTitle, Is.EqualTo("Moon"));
            yield return Seconds(0.7f);
            yield return Screenshot("05-arrival");
            yield return Seconds(2.5f);
            Assert.That(cues.Intensity, Is.LessThan(0.05f), "the cues settle after the trip");
            Assert.That(cam.fieldOfView, Is.EqualTo(baseFov).Within(0.5f));

            // The realistic style has no cues and puts the ship away.
            Object.FindAnyObjectByType<VisualStyleSettings>().SetStyle(VisualStyle.Realistic);
            yield return Frames(2);
            Assert.That(cues.Intensity, Is.EqualTo(0f));
            Assert.That(cam.fieldOfView, Is.EqualTo(baseFov));
        }
    }
}
