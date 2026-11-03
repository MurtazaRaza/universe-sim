using System;
using System.Collections;
using System.Collections.Generic;
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
    /// Interaction feel (A-12, FR-REN-15, docs/ART_DIRECTION.md section 12): flights between bodies follow a
    /// smootherstep on a log scale of distance, take longer for longer flights, and turn the view without a snap; zoom
    /// steps settle smoothly; hovering a label highlights its body without selecting it; the selection ring follows
    /// the selected body and pulses; a click on a label selects its body. None of it moves a body or the clock. The
    /// owner's hands-on check closes the task.
    /// </summary>
    public class InteractionTests
    {
        private const string FixedTime = "2026-10-21T12:00:00 UTC";
        private StylePreference preference;
        private SimulationHost host;
        private CameraRig rig;
        private HudController hud;

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
            hud = Object.FindAnyObjectByType<HudController>();
            Assert.That(host.Simulation, Is.Not.Null, host.LoadError);
            Sim.SetPaused(true);
            Assert.That(Sim.TryParseTime(FixedTime, out var t, out var error), error);
            Sim.SetTime(t);
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

        [UnityTest]
        public IEnumerator AFlightEasesOnALogScaleAndTurnsWithoutASnap()
        {
            // From four Earth radii to the Moon: the Moon is far off the view's axis, so the old flight turned the
            // view at once.
            var earth = Index(399);
            var moon = Index(301);
            rig.JumpTo(earth, Sim.Bodies[earth].MaxRadiusKm * 4.0, 0.3, 0.2);
            yield return Frames(3);
            var r0 = Camera.main.transform.rotation;
            var now = Sim.Clock.Now;
            Assert.That(Sim.TryState(Sim.Bodies[moon].Id, Sim.Bodies[earth].Id, UsimFrame.EclipJ2000, now, out var moonBefore));

            rig.Focus(moon);
            var start = Time.unscaledTime;
            Assert.That(rig.Transitioning);
            Assert.That(rig.FlightSeconds, Is.InRange(0.9f, 3f));
            yield return null;
            // The view the flight turns to: toward the new focus along the flight's line, which does not change.
            var r1 = Quaternion.LookRotation(-rig.OffsetKm.Normalized.ToVector3(), Vector3.up);
            var total = Quaternion.Angle(r0, r1);
            Assert.That(total, Is.GreaterThan(20f), "the new focus is well off the view's axis");

            // Each frame (the rig and this test read the same frame time): the distance follows smootherstep on a log
            // scale, and the view turns along a slerp eased over the first 45 % of the flight.
            var to = Math.Max(Sim.Bodies[moon].MaxRadiusKm * 4.0, Sim.Bodies[moon].MaxRadiusKm + 1.0);
            var samples = new List<(float Elapsed, double Distance)>();
            var worstTurn = 0f;
            var firstTurn = -1f;
            while (rig.Transitioning)
            {
                var elapsed = (Time.unscaledTime - start) / rig.FlightSeconds;
                samples.Add((elapsed, rig.OffsetKm.Length));
                var f = Mathf.Clamp01(elapsed / 0.45f);
                var turned = Quaternion.Angle(r0, Camera.main.transform.rotation);
                if (firstTurn < 0f)
                {
                    firstTurn = turned;
                }
                worstTurn = Mathf.Max(worstTurn, Mathf.Abs(turned - f * f * (3f - 2f * f) * total));
                yield return null;
            }
            Debug.Log($"UniverseSim: a flight of {rig.FlightSeconds:F2} s in {samples.Count} frames turned the view " +
                      $"{firstTurn:F2} of {total:F1} degrees in its first frame; largest turn error {worstTurn:F3} degrees");
            Assert.That(samples.Count, Is.GreaterThan(3));
            Assert.That(worstTurn, Is.LessThan(0.5f), "the view turns along the eased slerp, without a snap");
            // The flight's start distance: back out from the first sample.
            var (e0, d0) = samples[0];
            var s0 = CameraRig.Smootherstep(Mathf.Clamp01(e0));
            var startDistance = Math.Exp((Math.Log(d0) - Math.Log(to) * s0) / (1.0 - s0));
            var worst = 0.0;
            var previous = double.PositiveInfinity;
            foreach (var (elapsed, distance) in samples)
            {
                var progress = Math.Log(startDistance / distance) / Math.Log(startDistance / to);
                worst = Math.Max(worst, Math.Abs(progress - CameraRig.Smootherstep(Mathf.Clamp01(elapsed))));
                Assert.That(distance, Is.LessThanOrEqualTo(previous * (1.0 + 1e-9)), "the distance only shrinks");
                previous = distance;
            }
            Assert.That(worst, Is.LessThan(1e-3), "log-distance progress against smootherstep");
            Assert.That(rig.OffsetKm.Length, Is.EqualTo(to).Within(to * 1e-9));
            Assert.That(Vector3.Angle(Camera.main.transform.forward, host.Views[moon].transform.position),
                        Is.LessThan(0.05f), "it ends looking at the new focus");

            // The flight moved neither the clock nor any body.
            Assert.That(Sim.Clock.Now.Days, Is.EqualTo(now.Days));
            Assert.That(Sim.Clock.Now.Seconds, Is.EqualTo(now.Seconds));
            Assert.That(Sim.TryState(Sim.Bodies[moon].Id, Sim.Bodies[earth].Id, UsimFrame.EclipJ2000, Sim.Clock.Now,
                                     out var moonAfter));
            Assert.That((Double3.From(moonAfter.PositionKm) - Double3.From(moonBefore.PositionKm)).Length, Is.Zero);

            // Longer flights take longer, within the bounds.
            Assert.That(CameraRig.FlightDuration(1e9, 1e4, 0.9f, 0.45f, 3f), Is.EqualTo(3f));
            Assert.That(CameraRig.FlightDuration(1e5, 1e4, 0.9f, 0.45f, 3f), Is.EqualTo(1.35f).Within(1e-5));
            Assert.That(CameraRig.FlightDuration(1e4, 1e4, 0.9f, 0.45f, 3f), Is.EqualTo(0.9f));
        }

        [UnityTest]
        public IEnumerator ZoomStepsSettleSmoothly()
        {
            var earth = Index(399);
            var radius = Sim.Bodies[earth].MaxRadiusKm;
            rig.JumpTo(earth, radius * 10.0, 0.3, 0.2);
            yield return Frames(2);
            var before = rig.OffsetKm.Length;
            var target = radius + (before - radius) * 0.5;
            rig.ZoomBy(0.5);
            yield return null;
            var first = rig.OffsetKm.Length;
            Assert.That(first, Is.LessThan(before).And.GreaterThan(target), "on its way, not there at once");
            // With a time constant of 0.08 s the remaining log distance falls below the rig's snap (1e-6) after about
            // 1.1 s, whatever the frame times.
            var end = Time.unscaledTime + 1.5f;
            while (Time.unscaledTime < end)
            {
                yield return null;
            }
            Assert.That(rig.OffsetKm.Length, Is.EqualTo(target).Within(target * 1e-9));
        }

        [UnityTest]
        public IEnumerator HoveringALabelHighlightsItsBodyWithoutSelectingIt()
        {
            var earth = Index(399);
            var moon = Index(301);
            ViewEarthAndMoon(earth, moon);
            rig.Select(earth);
            yield return Frames(3);
            rig.SetLabelHover(moon);
            yield return Frames(2);
            Assert.That(rig.HoveredIndex, Is.EqualTo(moon));
            Assert.That(rig.SelectedIndex, Is.EqualTo(earth), "hovering selects nothing");
            Assert.That(rig.FocusIndex, Is.EqualTo(earth));
            Assert.That(PickCursor.Shown, "the cursor shows that a click picks something");
            Assert.That(hud.LabelOf(moon).ClassListContains("hovered"));
            Assert.That(hud.LabelOf(earth).ClassListContains("hovered"), Is.False);

            // The hover ring is centered on the Moon on screen.
            var ring = hud.HoverRing;
            Assert.That(ring.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            AssertCenteredOn(ring, host.Views[moon]);

            rig.SetLabelHover(-1);
            yield return Frames(2);
            Assert.That(rig.HoveredIndex, Is.Not.EqualTo(moon));
            Assert.That(hud.LabelOf(moon).ClassListContains("hovered"), Is.False);
        }

        [UnityTest]
        public IEnumerator TheSelectionRingFollowsTheSelectionAndALabelClickSelects()
        {
            var earth = Index(399);
            var moon = Index(301);
            ViewEarthAndMoon(earth, moon);
            yield return Frames(3);

            // A new selection's ring starts large and clear and is drawn in.
            rig.Select(earth);
            Assert.That(hud.SelectionRing.ClassListContains("entering"));
            yield return Frames(2);

            // A click on the Moon's label selects the Moon.
            var label = hud.LabelOf(moon);
            using (var click = ClickEvent.GetPooled())
            {
                click.target = label;
                label.SendEvent(click);
            }
            yield return null;
            Assert.That(rig.SelectedIndex, Is.EqualTo(moon));
            Assert.That(rig.FocusIndex, Is.EqualTo(earth), "one click selects, it does not fly");
            var end = Time.unscaledTime + 0.6f;
            while (Time.unscaledTime < end)
            {
                yield return null;
            }
            Assert.That(hud.SelectionRing.ClassListContains("entering"), Is.False);
            Assert.That(hud.SelectionRing.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            AssertCenteredOn(hud.SelectionRing, host.Views[moon]);
            Assert.That(hud.HoverRing.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
        }

        // Orbits the Earth from 60 of its radii, 30 degrees off the line from the Moon through the Earth, so both are in
        // view and apart: the Moon about 15 degrees from the Earth, behind it.
        private void ViewEarthAndMoon(int earth, int moon)
        {
            Assert.That(Sim.TryState(Sim.Bodies[moon].Id, Sim.Bodies[earth].Id, UsimFrame.EclipJ2000, Sim.Clock.Now,
                                     out var state));
            var toMoon = Double3.From(CoreSimulation.AxesToHost(NativeConstants.AxesUnity, state.PositionKm))
                .Normalized.ToVector3();
            var away = -toMoon;
            var side = Vector3.Cross(away, Vector3.up).normalized;
            var d = (away * Mathf.Cos(30f * Mathf.Deg2Rad) + side * Mathf.Sin(30f * Mathf.Deg2Rad)).normalized;
            rig.JumpTo(earth, Sim.Bodies[earth].MaxRadiusKm * 60.0, Math.Atan2(d.x, d.z),
                       Math.Asin(Mathf.Clamp(d.y, -1f, 1f)));
        }

        // The ring's center (its translate plus its radius, in panel units) is the body's position on screen.
        private static void AssertCenteredOn(VisualElement ring, BodyView view)
        {
            var cam = Camera.main;
            var p = cam.WorldToScreenPoint(view.transform.position);
            var expected = RuntimePanelUtils.ScreenToPanel(ring.panel, new Vector2(p.x, cam.pixelHeight - p.y));
            var translate = ring.style.translate.value;
            var radius = ring.style.width.value.value / 2f;
            var center = new Vector2(translate.x.value + radius, translate.y.value + radius);
            Assert.That(Vector2.Distance(center, expected), Is.LessThan(1f), view.Info.Name);
        }
    }
}
