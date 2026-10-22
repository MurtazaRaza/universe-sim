using System;
using System.Collections;
using System.Diagnostics;
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
    /// The main scene end to end (P5-13): it loads the pack and shows the bodies, positions match the usim
    /// CLI for the same instant, and nothing jitters at extreme zooms (P5-04). With USIM_SCREENSHOT_DIR set,
    /// it also saves screenshots of a few views for review.
    /// </summary>
    public class MainSceneTests
    {
        private const string FixedTime = "2026-10-21T12:00:00 UTC";

        private SimulationHost host;
        private CameraRig rig;

        private CoreSimulation Sim => host.Simulation;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            host = Object.FindAnyObjectByType<SimulationHost>();
            rig = Object.FindAnyObjectByType<CameraRig>();
            Assert.That(host, Is.Not.Null);
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

        // Renders the camera and the HUD into a texture for one frame and saves it (batch mode has no screen,
        // and WaitForEndOfFrame never fires there).
        private static IEnumerator Screenshot(string name)
        {
            var folder = Environment.GetEnvironmentVariable("USIM_SCREENSHOT_DIR");
            if (string.IsNullOrEmpty(folder))
            {
                yield break;
            }
            var cam = Camera.main;
            var document = Object.FindAnyObjectByType<UIDocument>();
            var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = target;
            document.panelSettings.targetTexture = target;
            yield return null;
            yield return null;
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            cam.targetTexture = null;
            document.panelSettings.targetTexture = null;
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
            Object.Destroy(image);
            target.Release();
            Object.Destroy(target);
        }

        [UnityTest]
        public IEnumerator TheSceneShowsEveryBody()
        {
            yield return Frames(3);
            Assert.That(host.SnapshotCount, Is.EqualTo(Sim.Bodies.Count));
            foreach (var view in host.Views)
            {
                Assert.That(view.HasPosition, view.Info.Name);
            }
            var renderers = host.Views.Where(v => v.Info.HasMeanRadius).Select(v => v.GetComponent<MeshRenderer>());
            Assert.That(renderers.All(r => r != null && r.enabled));
            yield return Screenshot("01-overview");
        }

        [UnityTest]
        public IEnumerator ViewsAcrossScales()
        {
            var earth = Index(399);
            var earthRadius = Sim.Bodies[earth].MaxRadiusKm;
            rig.JumpTo(earth, earthRadius * 4.0, 0.6, 0.3);
            rig.Select(earth);
            yield return Frames(3);
            yield return Screenshot("02-earth");

            rig.JumpTo(earth, earthRadius + 1.0, 0.6, 0.0);
            rig.Select(earth);
            yield return Frames(3);
            yield return Screenshot("03-earth-1km");

            var moon = Index(301);
            rig.JumpTo(moon, Sim.Bodies[moon].MaxRadiusKm * 3.0, 2.0, 0.2);
            rig.Select(moon);
            yield return Frames(3);
            yield return Screenshot("04-moon");

            var saturn = Index(699);
            rig.JumpTo(saturn, Sim.Bodies[saturn].MaxRadiusKm * 3.0, 0.3, 0.25);
            rig.Select(saturn);
            yield return Frames(3);
            yield return Screenshot("05-saturn");

            var pluto = Index(999);
            rig.JumpTo(pluto, Sim.Bodies[pluto].MaxRadiusKm * 3.0, 0.3, 0.2);
            rig.Select(pluto);
            yield return Frames(3);
            yield return Screenshot("06-pluto");

            rig.EnlargedBodies = true;
            var sun = Index(10);
            rig.JumpTo(sun, 1.2e9, 0.4, 0.5);
            rig.Select(sun);
            yield return new WaitForSecondsRealtime(0.3f); // the HUD refreshes its texts ten times a second
            yield return Screenshot("07-enlarged-inner-system");
            rig.EnlargedBodies = false;

            // Saturn's rings from above the ring plane, lit, with the planet's shadow on them (P6-04).
            rig.JumpTo(saturn, Sim.Bodies[saturn].MaxRadiusKm * 6.0, 0.9, 0.45);
            rig.Select(saturn);
            yield return Frames(3);
            yield return Screenshot("08-saturn-rings");

            // Jupiter and the Galilean moons, when the moons pack is loaded (P6-01).
            var jupiter = Index(599);
            rig.JumpTo(jupiter, 4.0e6, 0.5, 0.35);
            rig.Select(jupiter);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Screenshot("09-jupiter-system");

            // The sky: the Earth from far away against the stars (FR-REN-09).
            rig.JumpTo(earth, earthRadius * 40.0, 2.4, 0.1);
            rig.Select(earth);
            yield return Frames(3);
            yield return Screenshot("10-earth-and-stars");
        }

        [UnityTest]
        public IEnumerator StarsAndRingsAreDrawn()
        {
            yield return Frames(3);
            var stars = Object.FindAnyObjectByType<StarField>();
            Assert.That(stars, Is.Not.Null);
            Assert.That(stars.DrawnCount, Is.GreaterThan(8000)); // naked-eye stars of the catalog
            Assert.That(stars.GetComponent<MeshRenderer>().enabled);
            var rings = Object.FindAnyObjectByType<RingSystems>();
            Assert.That(rings, Is.Not.Null);
            Assert.That(rings.Count, Is.EqualTo(1)); // Saturn
            var saturn = Index(699);
            rig.JumpTo(saturn, Sim.Bodies[saturn].MaxRadiusKm * 6.0, 0.9, 0.45);
            yield return Frames(3);
            var ring = rings.transform.Cast<Transform>().First(t => t.name.StartsWith("Rings of"));
            Assert.That(ring.GetComponent<MeshRenderer>().enabled);
            // The ring's outer edge is Saturn's outer ring radius in the same render scale as the planet.
            var view = host.Views[saturn];
            var outer = Sim.Rings.Where(r => r.BodyIndex == saturn && !r.IsGap).Max(r => r.OuterRadiusKm);
            Assert.That(ring.localScale.x, Is.EqualTo(outer * view.RenderRadius / view.Info.MeanRadiusKm).Within(1e-3 * ring.localScale.x));
            Assert.That(Vector3.Distance(ring.position, view.transform.position), Is.LessThan(1e-3f * ring.localScale.x));
        }

        [UnityTest]
        public IEnumerator MoonLabelsGiveWayToTheirPlanetWhenFarAway()
        {
            var io = Index(501);
            if (io < 0)
            {
                Assert.Ignore("the moons pack is not built (usim-pipeline build --pack solar-system-moons)");
            }
            var jupiter = Index(599);
            Assert.That(host.Views[io].Planet, Is.SameAs(host.Views[jupiter]));
            var sun = Index(10);
            rig.JumpTo(sun, 3.0e9, 0.4, 0.5);
            yield return Frames(3);
            Assert.That(host.Views[io].StandsApart(Camera.main, 28f), Is.False);
            rig.JumpTo(jupiter, 4.0e6, 0.5, 0.35);
            yield return Frames(3);
            Assert.That(host.Views[io].StandsApart(Camera.main, 28f), Is.True);
        }

        [UnityTest]
        public IEnumerator TheCameraMovesBetweenBodiesAndStaysOutsideThem()
        {
            // A focus change flies to the new body and ends four radii from its center (UC-04).
            var earth = Index(399);
            rig.Focus(earth);
            yield return new WaitForSecondsRealtime(1.8f);
            Assert.That(host.Request.Center.Value, Is.EqualTo(399));
            var radius = Sim.Bodies[earth].MaxRadiusKm;
            Assert.That(rig.OffsetKm.Length, Is.EqualTo(radius * 4.0).Within(radius * 1e-6));
            // The view is centered on the focus: the Earth is in front of the camera.
            var toEarth = host.Views[earth].transform.position;
            Assert.That(Vector3.Angle(Camera.main.transform.forward, toEarth), Is.LessThan(0.01f));

            // The closest approach is 1 km above the largest radius, whatever is asked for.
            rig.JumpTo(earth, radius * 0.5, 0.0, 0.0);
            yield return Frames(2);
            Assert.That(rig.OffsetKm.Length, Is.EqualTo(radius + 1.0).Within(1e-6));
            Assert.That(host.Request.KmPerRenderUnit, Is.EqualTo(1.0).Within(1e-9));

            // Free fly keeps the position and hands back an orbit at the same distance.
            rig.JumpTo(earth, radius * 10.0, 0.3, 0.2);
            yield return Frames(2);
            var before = rig.OffsetKm;
            rig.SetFreeFly(true);
            yield return Frames(2);
            Assert.That((rig.OffsetKm - before).Length, Is.LessThan(1e-6 * before.Length));
            rig.SetFreeFly(false);
            yield return Frames(2);
            Assert.That(rig.OffsetKm.Length, Is.EqualTo(before.Length).Within(1e-6 * before.Length));
        }

        private static string FindCli()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            // Release first: the optional moons pack (300 MB) loads in seconds there, much slower in dev.
            foreach (var preset in new[] { "release", "dev" })
            {
                var path = Path.Combine(root, "core", "build", preset, "apps", "usim-cli", "usim");
                if (File.Exists(path))
                {
                    return path;
                }
            }
            return null;
        }

        [UnityTest]
        public IEnumerator PositionsMatchTheCli()
        {
            var cli = FindCli();
            if (cli == null)
            {
                Assert.Ignore("the usim CLI is not built (cd core && cmake --build --preset dev)");
            }
            var earth = Index(399);
            rig.JumpTo(earth, Sim.Bodies[earth].MaxRadiusKm * 4.0, 1.0, 0.4);
            yield return Frames(2);

            Assert.That(Sim.TryFormatTime(Sim.Clock.Now, NativeConstants.ScaleTdb, 9, out var tdb));
            var request = host.Request;
            foreach (var target in new long[] { 301, 10, 499 })
            {
                var start = new ProcessStartInfo(cli,
                    $"state {target} --center 399 --frame eclipj2000 --time \"{tdb}\" --format csv --pack \"{host.PackPath}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                using var process = Process.Start(start);
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                Assert.That(process.ExitCode, Is.EqualTo(0), process.StandardError.ReadToEnd());
                var row = output.Split('\n')[1].Split(',');
                double Field(int i) => double.Parse(row[i], CultureInfo.InvariantCulture);
                var stateKm = new Double3(Field(5), Field(6), Field(7));

                // The documented mapping (docs/API.md): subtract the view origin, scale, map the axes, and
                // compress beyond the horizon along the line of sight.
                var relative = stateKm - Double3.From(request.OriginKm);
                var host_ = Double3.From(CoreSimulation.AxesToHost(NativeConstants.AxesUnity, (relative * (1.0 / request.KmPerRenderUnit)).ToUsim()));
                var distance = host_.Length;
                var c = request.CompressionDistance;
                var factor = distance > c ? c * (1.0 + Math.Log(distance / c)) / distance : 1.0;
                var expected = (host_ * factor).ToVector3();
                var actual = host.Views[Index(target)].transform.position;
                Assert.That(Vector3.Distance(actual, expected), Is.LessThan(1e-5f * Mathf.Max(1f, expected.magnitude)),
                            $"naif:{target}: Unity {actual}, CLI {expected}");
            }
        }

        // Positions of a body over consecutive frames, as Unity sees them, with the clock running.
        private IEnumerator Track(int body, int frames, Vector3[] positions)
        {
            Sim.SetRate(60.0);
            Sim.SetPaused(false);
            for (var i = 0; i < frames; ++i)
            {
                yield return null;
                positions[i] = host.Views[body].transform.position;
            }
            Sim.SetPaused(true);
        }

        // What the eye sees is direction: each frame's direction from the camera stays within a small fraction
        // of a pixel of the midpoint of its neighbours (a pixel is about 8e-4 rad at 50 degrees over 1080
        // lines). Float jitter would show up as directions jumping around that midpoint.
        private static void AssertSmooth(Vector3[] positions, string what)
        {
            const double maxDeviationRad = 1.0e-5;
            for (var i = 1; i + 1 < positions.Length; ++i)
            {
                var midpoint = (Double3.From(positions[i - 1]) + Double3.From(positions[i + 1])) * 0.5;
                var here = Double3.From(positions[i]);
                var a = here.Normalized;
                var b = midpoint.Normalized;
                var cross = new Double3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
                var angle = Math.Atan2(cross.Length, a.X * b.X + a.Y * b.Y + a.Z * b.Z);
                Assert.That(angle, Is.LessThan(maxDeviationRad), $"{what}: frame {i}");
            }
        }

        [UnityTest]
        public IEnumerator NoJitterNearTheSurfaceOrFarAway()
        {
            // 1 km above the Earth: the Moon moves across the sky smoothly.
            var earth = Index(399);
            rig.JumpTo(earth, Sim.Bodies[earth].MaxRadiusKm + 1.0, 0.0, 0.0);
            var moonPath = new Vector3[60];
            yield return Track(Index(301), moonPath.Length, moonPath);
            AssertSmooth(moonPath, "Moon seen from 1 km above the Earth");

            // Near Pluto: the Sun, billions of km away and compressed, moves smoothly too.
            var pluto = Index(999);
            rig.JumpTo(pluto, Sim.Bodies[pluto].MaxRadiusKm * 3.0, 0.0, 0.0);
            var sunPath = new Vector3[60];
            yield return Track(Index(10), sunPath.Length, sunPath);
            AssertSmooth(sunPath, "Sun seen from Pluto");
        }
    }
}
