using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UniverseSim.Unity.Native;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UniverseSim.Unity.Tests.PlayMode
{
    /// <summary>
    /// Atmospheres in the stylized style (A-07, B-06, ADR-0018): halos only on the bodies the core reports with an
    /// atmosphere, tinted toward a twilight hue only where a source gives one, brightest on the sunlit side, and gone
    /// in the realistic style; the sky near the ground takes the sourced hue by day, clears at night, and does not
    /// exist on a body without an atmosphere; the ship's panel cites the cues. With USIM_SCREENSHOT_DIR set, it saves
    /// the views in an "atmospheres" folder.
    /// </summary>
    public class AtmosphereTests
    {
        private const string FixedTime = "2026-10-21T12:00:00 UTC";
        private const int Width = 1600;
        private const int Height = 900;

        private StylePreference preference;
        private SimulationHost host;
        private CameraRig rig;
        private ShipPilot pilot;
        private ShipHud shipHud;
        private VisualStyleSettings settings;
        private StylizedAtmospheres atmospheres;
        private StylizedSky sky;
        private RenderTexture target;

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
            pilot = Object.FindAnyObjectByType<ShipPilot>();
            shipHud = Object.FindAnyObjectByType<ShipHud>();
            settings = Object.FindAnyObjectByType<VisualStyleSettings>();
            atmospheres = Object.FindAnyObjectByType<StylizedAtmospheres>();
            sky = Object.FindAnyObjectByType<StylizedSky>();
            Assert.That(host.Simulation, Is.Not.Null, host.LoadError);
            Assert.That(atmospheres, Is.Not.Null);
            Assert.That(sky, Is.Not.Null);
            Sim.SetPaused(true);
            Assert.That(Sim.TryParseTime(FixedTime, out var t, out var error), error);
            Sim.SetTime(t);
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            Camera.main.targetTexture = target;
            yield return null;
        }

        [TearDown]
        public void ReleaseTarget()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                cam.targetTexture = null;
            }
            if (target != null)
            {
                target.Release();
                Object.Destroy(target);
            }
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

        // A direction from the body's center `degrees` away from the direction of its light, in a plane through it.
        private Vector3 FromLight(int index, double degrees)
        {
            var light = ((Vector3)host.Views[index].LightDirection).normalized;
            var side = Vector3.Cross(light, Vector3.up).normalized;
            var angle = degrees * Math.PI / 180.0;
            return (light * (float)Math.Cos(angle) + side * (float)Math.Sin(angle)).normalized;
        }

        private void JumpAlong(int index, Vector3 direction, double radii)
        {
            rig.JumpTo(index, Sim.Bodies[index].MaxRadiusKm * radii, Math.Atan2(direction.x, direction.z),
                       Math.Asin(Mathf.Clamp(direction.y, -1f, 1f)));
            rig.Select(index);
        }

        // Renders a frame into the linear half-float target and returns its pixels (row 0 at the bottom).
        private IEnumerator Render(Color[][] result, bool postProcessing = true)
        {
            var cam = Camera.main;
            var data = cam.GetUniversalAdditionalCameraData();
            var hadPost = data.renderPostProcessing;
            data.renderPostProcessing = postProcessing;
            yield return null;
            yield return null;
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(Width, Height, TextureFormat.RGBAHalf, false, true);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            data.renderPostProcessing = hadPost;
            result[0] = image.GetPixels();
            Object.Destroy(image);
        }

        [UnityTest]
        public IEnumerator HalosAreOnTheBodiesTheCoreGivesAnAtmosphere()
        {
            yield return Frames(2);
            var expected = host.Views
                .Where(v => v.Info.HasMeanRadius && Sim.Atmosphere(v.Info.Index) == NativeConstants.AtmospherePresent)
                .ToList();
            Assert.That(atmospheres.HaloBodies.ToList(), Is.EquivalentTo(expected));
            var names = expected.Select(v => v.Info.Name).ToList();
            Assert.That(names, Does.Contain("Earth").And.Contain("Mars").And.Contain("Jupiter").And.Contain("Venus"));
            Assert.That(names, Does.Not.Contain("Moon").And.Not.Contain("Mercury").And.Not.Contain("Sun"));
            foreach (var view in expected)
            {
                Assert.That(atmospheres.TryGetHalo(view, out _, out var material));
                var sourced = host.SkyCues.TryGet(view.Info.Id.Value, SkyCues.Twilight, out _);
                Assert.That(material.GetFloat("_TwilightStrength") > 0f, Is.EqualTo(sourced),
                            view.Info.Name + ": a twilight tint only with a sourced twilight hue");
            }

            var earth = Index(399);
            JumpAlong(earth, FromLight(earth, 90.0), 4.0);
            yield return Frames(3);
            Assert.That(atmospheres.TryGetHalo(host.Views[earth], out var renderer, out _));
            Assert.That(renderer.enabled);
            Assert.That(atmospheres.ActiveInView(Camera.main));

            settings.SetStyle(VisualStyle.Realistic);
            yield return Frames(2);
            foreach (var view in expected)
            {
                Assert.That(atmospheres.TryGetHalo(view, out var r, out _) && !r.enabled, view.Info.Name);
            }
            Assert.That(sky.Renderer.enabled, Is.False);
            Assert.That(atmospheres.ActiveInView(Camera.main), Is.False);
            settings.SetStyle(VisualStyle.Stylized);
        }

        [UnityTest]
        public IEnumerator TheHaloIsBrightestOnTheSunlitSideAndStaysNearTheLimb()
        {
            // The Earth from four radii, at right angles to its light: the terminator runs across the disk. The halo's
            // light is the difference between frames with and without the effect, before post-processing.
            var earth = Index(399);
            var view = host.Views[earth];
            Object.FindAnyObjectByType<OrbitLines>().Visible = false;
            Object.FindAnyObjectByType<StarField>().Visible = false;
            JumpAlong(earth, FromLight(earth, 90.0), 4.0);
            yield return Frames(3);
            var with = new Color[1][];
            yield return Render(with, postProcessing: false);
            atmospheres.enabled = false;
            var without = new Color[1][];
            yield return Render(without, postProcessing: false);
            atmospheres.enabled = true;

            var cam = Camera.main;
            var center = (Vector2)cam.WorldToScreenPoint(view.transform.position);
            var towardLight = (Vector2)cam.WorldToScreenPoint(view.transform.position +
                                                              ((Vector3)view.LightDirection).normalized *
                                                              view.MaxRenderRadius) - center;
            var radiusPixels = towardLight.magnitude; // the light is at right angles to the view
            towardLight.Normalize();
            double sunlit = 0.0, night = 0.0, far = 0.0;
            for (var y = 0; y < Height; ++y)
            {
                for (var x = 0; x < Width; ++x)
                {
                    var i = y * Width + x;
                    var added = Luminance(with[0][i]) - Luminance(without[0][i]);
                    var offset = new Vector2(x + 0.5f, y + 0.5f) - center;
                    if (offset.magnitude > radiusPixels * 1.3f)
                    {
                        far = Math.Max(far, Math.Abs(added));
                        continue;
                    }
                    if (Vector2.Dot(offset, towardLight) > 0.3f * radiusPixels)
                    {
                        sunlit += added;
                    }
                    else if (Vector2.Dot(offset, towardLight) < -0.3f * radiusPixels)
                    {
                        night += added;
                    }
                }
            }
            Debug.Log($"UniverseSim: halo light on the sunlit side {sunlit:F1}, on the night side {night:F1}, " +
                      $"largest beyond 1.3 radii {far:F4} (radius {radiusPixels:F0} px)");
            Assert.That(sunlit, Is.GreaterThan(50.0), "the halo adds light on the sunlit side");
            Assert.That(night, Is.LessThan(0.05 * sunlit), "and little on the night side");
            Assert.That(far, Is.LessThan(1e-3), "nothing beyond the shell (12 % of the radius)");
            yield return Screenshot("halo-earth");
        }

        private static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        // Puts the ship down on `naif` below a point `degreesFromSun` away from the subsolar point and waits until it
        // stops.
        private IEnumerator LandOn(long naif, double degreesFromSun)
        {
            var index = Index(naif);
            JumpAlong(index, FromLight(index, degreesFromSun), 1.5);
            yield return Frames(2);
            pilot.SetActive(true);
            yield return Frames(2);
            Assert.That(pilot.AnchorIndex, Is.EqualTo(index));
            pilot.Descend();
            yield return Seconds(9.5f);
            Assert.That(pilot.State, Is.EqualTo(ShipPilot.Mode.Landed).Or.EqualTo(ShipPilot.Mode.Hovering));
            yield return Frames(15); // the conditions refresh five times a second
        }

        // The median color of the top tenth of the frame: the sky above the ship's horizon.
        private static Color TopBand(Color[] pixels)
        {
            var r = new List<float>();
            var g = new List<float>();
            var b = new List<float>();
            for (var y = Height * 9 / 10; y < Height; ++y)
            {
                for (var x = 0; x < Width; x += 4)
                {
                    var c = pixels[y * Width + x];
                    r.Add(c.r);
                    g.Add(c.g);
                    b.Add(c.b);
                }
            }
            float Median(List<float> values)
            {
                values.Sort();
                return values[values.Count / 2];
            }
            return new Color(Median(r), Median(g), Median(b));
        }

        [UnityTest]
        public IEnumerator TheSkyTakesTheSourcedHueByDayAndClearsAtNight()
        {
            // Mars by day: its sky cue is brownish gray (NASA/JPL PIA01547), so red over blue.
            yield return LandOn(499, 40.0);
            var mars = Index(499);
            Assert.That(sky.SkyBody, Is.EqualTo(host.Views[mars]));
            var pressure = Sim.Quantity(mars, NativeConstants.FieldSurfacePressure);
            Assert.That(sky.Strength, Is.EqualTo(sky.PressureStrength(pressure.Value)).Within(1e-4));
            Assert.That(sky.SunElevationSine, Is.GreaterThan(0.3f));
            Assert.That(sky.ActiveInView(Camera.main));
            var frame = new Color[1][];
            yield return Render(frame);
            var marsDay = TopBand(frame[0]);
            Debug.Log($"UniverseSim: Mars sky by day {marsDay}, strength {sky.Strength:F3}");
            Assert.That(marsDay.r, Is.GreaterThan(marsDay.b), "a brownish sky, not a blue one");
            shipHud.Refresh();
            var facts = string.Join("\n", shipHud.FactTexts);
            Assert.That(host.SkyCues.TryGet(499, SkyCues.DaytimeSky, out var day));
            StringAssert.Contains("Brownish gray.", facts);
            StringAssert.Contains(day.Citation, facts);
            Assert.That(host.SkyCues.TryGet(499, SkyCues.Twilight, out var twilight));
            StringAssert.Contains(twilight.Citation, facts);
            yield return Screenshot("sky-mars-day");
            pilot.SetActive(false);
            yield return Frames(2);

            // The Earth by day: blue (Tier 2, labeled).
            yield return LandOn(399, 40.0);
            Assert.That(sky.SkyBody, Is.EqualTo(host.Views[Index(399)]));
            Assert.That(sky.Strength, Is.GreaterThan(0.95f), "about 1 bar: the full sky");
            yield return Render(frame);
            var earthDay = TopBand(frame[0]);
            Debug.Log($"UniverseSim: Earth sky by day {earthDay}");
            Assert.That(earthDay.b, Is.GreaterThan(earthDay.r), "a blue sky");
            shipHud.Refresh();
            StringAssert.Contains("S-NASA-SPACEPLACE (Tier 2)", string.Join("\n", shipHud.FactTexts));
            yield return Screenshot("sky-earth-day");
            pilot.SetActive(false);
            yield return Frames(2);

            // The Earth at night: the sky clears and the stars show.
            yield return LandOn(399, 150.0);
            Assert.That(sky.SunElevationSine, Is.LessThan(-0.2f));
            yield return Render(frame);
            var earthNight = TopBand(frame[0]);
            Debug.Log($"UniverseSim: Earth sky at night {earthNight}");
            Assert.That(Luminance(earthNight), Is.LessThan(0.25f * Luminance(earthDay)));
            yield return Screenshot("sky-earth-night");
            pilot.SetActive(false);
            yield return Frames(2);

            // The Moon: no atmosphere (0 bar in the source's table), so no sky.
            yield return LandOn(301, 40.0);
            Assert.That(sky.SkyBody, Is.Null);
            Assert.That(sky.Renderer.enabled, Is.False);
            shipHud.Refresh();
            StringAssert.Contains("None at the source's precision", string.Join("\n", shipHud.FactTexts));
        }

        // As MainSceneTests.Screenshot, into an "atmospheres" folder.
        private IEnumerator Screenshot(string name)
        {
            var root = Environment.GetEnvironmentVariable("USIM_SCREENSHOT_DIR");
            if (string.IsNullOrEmpty(root))
            {
                yield break;
            }
            var folder = Path.Combine(root, "atmospheres");
            var cam = Camera.main;
            var document = Object.FindAnyObjectByType<UnityEngine.UIElements.UIDocument>();
            var previousTarget = cam.targetTexture;
            var shot = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = shot;
            document.panelSettings.targetTexture = shot;
            yield return null;
            yield return null;
            var previous = RenderTexture.active;
            RenderTexture.active = shot;
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            cam.targetTexture = previousTarget;
            document.panelSettings.targetTexture = null;
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
            Object.Destroy(image);
            shot.Release();
            Object.Destroy(shot);
        }
    }
}
