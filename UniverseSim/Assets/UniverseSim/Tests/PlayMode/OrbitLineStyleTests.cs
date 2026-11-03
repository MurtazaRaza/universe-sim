using System;
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
    /// The stylized orbit lines and labels (A-10, SPEC FR-REN-06 and section 11.1): every line still follows the
    /// core's path in both styles, the fade runs back along the path from the body (earlier in time), the lines of
    /// the focused body are the highlighted ones, approximate orbits keep a color and dashes of their own, and the
    /// label icons show the kind of body in the stylized style only.
    /// </summary>
    public class OrbitLineStyleTests
    {
        private const string FixedTime = "2026-10-21T12:00:00 UTC";

        private StylePreference preference;
        private SimulationHost host;
        private CameraRig rig;
        private VisualStyleSettings settings;
        private OrbitLines lines;
        private StylizedOrbitLines lineStyle;

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
            settings = Object.FindAnyObjectByType<VisualStyleSettings>();
            lines = Object.FindAnyObjectByType<OrbitLines>();
            lineStyle = Object.FindAnyObjectByType<StylizedOrbitLines>();
            Assert.That(lineStyle, Is.Not.Null);
            Assert.That(Sim, Is.Not.Null, host.LoadError);
            Sim.SetPaused(true);
            Assert.That(Sim.TryParseTime(FixedTime, out var t, out var error), error);
            Sim.SetTime(t);
            // The Earth from far enough out that the Moon's orbit stands apart from it.
            var earth = Index(399);
            rig.JumpTo(earth, Sim.Bodies[earth].MaxRadiusKm * 300.0, 0.6, 0.5);
            rig.Select(earth);
            yield return null;
            yield return null;
        }

        private int Index(long naif) => Sim.IndexOf(UsimBodyId.Naif(naif));

        [UnityTest]
        public IEnumerator EveryLineFollowsTheCoresPathInBothStyles()
        {
            var stylized = lines.DrawnPositions();
            Assert.That(lines.DrawnCount, Is.GreaterThan(3));
            settings.SetStyle(VisualStyle.Realistic);
            yield return null;
            yield return null;
            var realistic = lines.DrawnPositions();
            Assert.That(realistic, Is.EqualTo(stylized), "the same vertices in both styles");

            // The Earth's line is the core's path, point for point (two vertices per point).
            var earth = Sim.Bodies[Index(399)];
            Assert.That(Sim.TryDefaultOrbitCenter(earth.Id, out var center));
            var path = new UsimVec3d[16384];
            var count = Sim.OrbitPath(earth.Id, center, ref host.Request, path, out _);
            Assert.That(lines.TryGetLine(earth.Id, out var points, out _, out _, out _));
            Assert.That(points, Is.EqualTo(count));
            var drawn = new HashSet<Vector3>(realistic);
            for (var i = 0; i < count; i += Math.Max(1, count / 50))
            {
                var p = new Vector3((float)path[i].X, (float)path[i].Y, (float)path[i].Z);
                Assert.That(drawn.Contains(p), $"point {i} of {count} of the Earth's path is drawn");
            }
        }

        [UnityTest]
        public IEnumerator TheFadeRunsBackAlongThePathFromTheBody()
        {
            var earth = Sim.Bodies[Index(399)];
            Assert.That(lines.TryGetLine(earth.Id, out var points, out var now, out var category, out _));
            Assert.That(category, Is.EqualTo(OrbitCategory.Planet));
            Assert.That(OrbitLines.TrailFraction(now, now, points), Is.Zero);
            Assert.That(OrbitLines.TrailFraction(now - 1, now, points), Is.LessThan(0.01f), "just behind the body");
            Assert.That(OrbitLines.TrailFraction(now + 1, now, points), Is.GreaterThan(0.99f), "just ahead of it");

            // Three days earlier the Earth sits at an earlier point of the same path (a shift the core's cache keeps
            // the path for), so lower points are earlier in time and the fade shows the direction of motion.
            Assert.That(Sim.TryParseTime("2026-10-18T12:00:00 UTC", out var earlier, out var error), error);
            Sim.SetTime(earlier);
            yield return null;
            yield return null;
            Assert.That(lines.TryGetLine(earth.Id, out var pointsBefore, out var before, out _, out _));
            Assert.That(pointsBefore, Is.EqualTo(points), "the same cached path");
            Assert.That(before, Is.LessThan(now), "three days earlier the Earth was at an earlier point");
            Assert.That(OrbitLines.TrailFraction(before, now, points), Is.LessThan(0.02f), "where it was is just behind it");
        }

        [UnityTest]
        public IEnumerator TheFocusedBodysLinesAreHighlighted()
        {
            var earth = Sim.Bodies[Index(399)].Id;
            var moon = Sim.Bodies[Index(301)].Id;
            var mars = Sim.Bodies[Index(499)].Id;
            bool Near(UsimBodyId id)
            {
                Assert.That(lines.TryGetLine(id, out _, out _, out _, out var near));
                return near;
            }
            Assert.That(Near(earth), "the Earth's own orbit");
            Assert.That(Near(moon), "the orbit around the Earth");
            Assert.That(Near(mars), Is.False);

            var marsIndex = Index(499);
            rig.JumpTo(marsIndex, Sim.Bodies[marsIndex].MaxRadiusKm * 300.0, 0.6, 0.5);
            yield return null;
            yield return null;
            Assert.That(Near(mars), "the focus moved to Mars");
            Assert.That(Near(earth), Is.False);
        }

        private static float Hue(Color c)
        {
            Color.RGBToHSV(c, out var h, out _, out _);
            return 360f * h;
        }

        [UnityTest]
        public IEnumerator ApproximateOrbitsKeepAColorAndDashesOfTheirOwn()
        {
            yield return null;
            var approximate = Hue(lineStyle.ColorOf(OrbitCategory.Approximate));
            foreach (var category in new[] { OrbitCategory.Planet, OrbitCategory.DwarfPlanet, OrbitCategory.Moon })
            {
                var d = Math.Abs(Hue(lineStyle.ColorOf(category)) - approximate);
                Assert.That(Math.Min(d, 360f - d), Is.GreaterThan(60f), $"{category} against approximate lines");
            }
            Assert.That(lineStyle.DashesApproximateLines);
            // A line is in the approximate category exactly when the core flags its path approximate (ADR-0008),
            // whatever the body.
            var path = new UsimVec3d[16384];
            var flagged = 0;
            foreach (var body in Sim.Bodies)
            {
                if (!lines.TryGetLine(body.Id, out var points, out _, out var category, out _) || points == 0 ||
                    !Sim.TryDefaultOrbitCenter(body.Id, out var center))
                {
                    continue;
                }
                Sim.OrbitPath(body.Id, center, ref host.Request, path, out var flags);
                var isApproximate = (flags & NativeConstants.OrbitApproximate) != 0;
                flagged += isApproximate ? 1 : 0;
                Assert.That(category == OrbitCategory.Approximate, Is.EqualTo(isApproximate), body.Name);
            }
            TestContext.WriteLine($"{flagged} drawn lines are approximate");
        }

        [UnityTest]
        public IEnumerator LabelsShowAnIconForTheKindOfBodyInTheStylizedStyleOnly()
        {
            var hud = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
            var layer = hud.Q("labels");
            yield return null;
            Assert.That(layer.ClassListContains(StylizedLabels.StylizedClass));
            var anchors = layer.Children().ToList();
            Assert.That(anchors.Count, Is.EqualTo(Sim.Bodies.Count));
            var checkedKinds = new HashSet<int>();
            for (var i = 0; i < anchors.Count; ++i)
            {
                var body = Sim.Bodies[i];
                var icon = anchors[i].Q<BodyIcon>();
                var shape = BodyIcon.ShapeFor(body.Kind);
                if (shape == BodyIconShape.None)
                {
                    Assert.That(icon, Is.Null, body.Name);
                    continue;
                }
                Assert.That(icon, Is.Not.Null, body.Name);
                Assert.That(icon.Shape, Is.EqualTo(shape), body.Name);
                Assert.That(icon.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), body.Name);
                // The icon's color is the color of the body's kind of orbit line.
                var category = body.Kind == 4 ? OrbitCategory.Moon
                    : body.Kind == 3 ? OrbitCategory.DwarfPlanet
                    : body.Kind == 2 ? OrbitCategory.Planet : (OrbitCategory?)null;
                if (category != null)
                {
                    var line = lineStyle.ColorOf(category.Value);
                    var color = icon.resolvedStyle.color;
                    Assert.That(Math.Abs(color.r - line.r) + Math.Abs(color.g - line.g) + Math.Abs(color.b - line.b),
                                Is.LessThan(3.5f / 255f), $"{body.Name}: icon {color}, line {line}");
                }
                checkedKinds.Add(body.Kind);
            }
            Assert.That(checkedKinds, Is.SupersetOf(new[] { 1, 2, 4 }), "the Sun, planets and moons");

            settings.SetStyle(VisualStyle.Realistic);
            yield return null;
            Assert.That(layer.ClassListContains(StylizedLabels.StylizedClass), Is.False);
            foreach (var icon in layer.Query<BodyIcon>().ToList())
            {
                Assert.That(icon.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            }
        }
    }
}
