using System;
using System.Collections;
using System.Collections.Generic;
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
    /// The stylized body shading (A-05, SPEC FR-REN-07): the Moon at a known phase has its terminator on the same
    /// pixels in both styles, where the core's light direction puts it, and no stylized surface gets brighter than
    /// the cap that keeps airless bodies out of the bloom (docs/ART_DIRECTION.md sections 3 and 7). The Moon is drawn
    /// white for these checks, so its map's detail does not hide the light.
    /// </summary>
    public class BodyShadingTests
    {
        private const string FixedTime = "2026-10-21T12:00:00 UTC";
        private const int Width = 1600;
        private const int Height = 900;

        private StylePreference preference;
        private RenderTexture target;
        private SimulationHost host;
        private CameraRig rig;
        private VisualStyleSettings settings;

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
            Assert.That(host.Simulation, Is.Not.Null, host.LoadError);
            host.Simulation.SetPaused(true);
            Assert.That(host.Simulation.TryParseTime(FixedTime, out var t, out var error), error);
            host.Simulation.SetTime(t);
            Object.FindAnyObjectByType<OrbitLines>().Visible = false;
            Object.FindAnyObjectByType<StarField>().Visible = false;
            // The camera keeps this target for the whole test, so its pixel size is the image's when rays are cast.
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

        private int Index(long naif) => host.Simulation.IndexOf(UsimBodyId.Naif(naif));

        // The Moon, drawn white, seen from `phaseDegrees` away from the light at three radii.
        private IEnumerator ShowWhiteMoon(double phaseDegrees)
        {
            var moon = Index(301);
            var view = host.Views[moon];
            view.GetComponent<MeshRenderer>().sharedMaterial.SetTexture("_BaseMap", Texture2D.whiteTexture);
            var light = ((Vector3)view.LightDirection).normalized;
            var side = Vector3.Cross(light, Vector3.up).normalized;
            var phase = phaseDegrees * Math.PI / 180.0;
            var d = (light * (float)Math.Cos(phase) + side * (float)Math.Sin(phase)).normalized;
            rig.JumpTo(moon, host.Simulation.Bodies[moon].MaxRadiusKm * 3.0, Math.Atan2(d.x, d.z),
                       Math.Asin(Mathf.Clamp(d.y, -1f, 1f)));
            rig.Select(moon);
            yield return null;
            yield return null;
            yield return null;
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

        // n.l at the surface point a pixel's center sees, from the body's transform (its mesh is the unit sphere
        // scaled by its radii) and its light direction; null where the ray misses the body.
        private static float? NdotL(Camera cam, BodyView view, int x, int y)
        {
            var ray = cam.ScreenPointToRay(new Vector3(x + 0.5f, y + 0.5f, 0f));
            var t = view.transform;
            var origin = t.InverseTransformPoint(ray.origin);
            var direction = t.InverseTransformDirection(ray.direction);
            // InverseTransformDirection undoes only the rotation; dividing by the scale puts the ray in the unit
            // sphere's space, where InverseTransformPoint put its origin.
            var s = t.lossyScale;
            direction = new Vector3(direction.x / s.x, direction.y / s.y, direction.z / s.z);
            var a = Vector3.Dot(direction, direction);
            var b = 2f * Vector3.Dot(origin, direction);
            var c = Vector3.Dot(origin, origin) - 1f;
            var disc = b * b - 4f * a * c;
            if (disc < 0f)
            {
                return null;
            }
            var hit = origin + direction * ((-b - Mathf.Sqrt(disc)) / (2f * a));
            var normal = t.TransformDirection(new Vector3(hit.x / s.x, hit.y / s.y, hit.z / s.z)).normalized;
            return Vector3.Dot(normal, ((Vector3)view.LightDirection).normalized);
        }

        private const int LimbPixels = 3;

        // The rise that marks the terminator. The realistic night side is flat and Lambert light starts from zero, so
        // any rise counts; the stylized night side carries a rim that ripples a little with the sphere's facets, and
        // its terminator is a step up from the lifted night (about 0.05 in luminance).
        private static readonly Dictionary<VisualStyle, float> Rise = new Dictionary<VisualStyle, float>
        {
            { VisualStyle.Realistic, 1e-4f },
            { VisualStyle.Stylized, 2e-3f },
        };

        private static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        // Along each scan line across the middle of the disc (rows or columns, whichever runs closer to the light
        // on screen), from the night limb toward the light: the first pixel whose center sees n.l > 0, and the
        // first pixel brighter than the one before it by more than `rise`, the measured terminator. The comparison
        // starts a few pixels inside the disc, past the limb pixels that antialiasing blends with the sky.
        private static List<(int Expected, int Measured)> Terminator(Camera cam, BodyView view, Color[] pixels,
                                                                     float rise)
        {
            var center = cam.WorldToScreenPoint(view.transform.position);
            var lightPoint = view.transform.position + (Vector3)view.LightDirection * view.MaxRenderRadius;
            var toLight = cam.WorldToScreenPoint(lightPoint);
            var screenLight = new Vector2(toLight.x - center.x, toLight.y - center.y).normalized;
            var alongRows = Mathf.Abs(screenLight.x) >= Mathf.Abs(screenLight.y);
            var step = (alongRows ? screenLight.x : screenLight.y) >= 0f ? 1 : -1;
            var focalPixels = 0.5f * cam.pixelHeight / Mathf.Tan(0.5f * cam.fieldOfView * Mathf.Deg2Rad);
            var distance = Vector3.Distance(view.transform.position, cam.transform.position);
            var radius = SphereLods.PixelRadius(view.MaxRenderRadius, distance, focalPixels);
            var result = new List<(int, int)>();
            var c0 = Mathf.RoundToInt(alongRows ? center.y : center.x);
            var c1 = Mathf.RoundToInt(alongRows ? center.x : center.y);
            var span = Mathf.RoundToInt(0.5f * radius);
            for (var line = c0 - span; line <= c0 + span; line += 4)
            {
                int? expected = null, measured = null;
                var previous = float.NaN;
                var inside = 0;
                var start = c1 - step * Mathf.CeilToInt(radius + 2f);
                for (var k = 0; k <= 2 * Mathf.CeilToInt(radius + 2f); ++k)
                {
                    var along = start + step * k;
                    var x = alongRows ? along : line;
                    var y = alongRows ? line : along;
                    if (x < 0 || y < 0 || x >= Width || y >= Height)
                    {
                        continue;
                    }
                    var ndl = NdotL(cam, view, x, y);
                    if (ndl == null)
                    {
                        previous = float.NaN;
                        inside = 0;
                        continue;
                    }
                    if (++inside <= LimbPixels)
                    {
                        continue;
                    }
                    var lum = Luminance(pixels[y * Width + x]);
                    if (expected == null && ndl.Value > 0f)
                    {
                        expected = k;
                    }
                    if (measured == null && !float.IsNaN(previous) && lum > previous + rise)
                    {
                        measured = k;
                    }
                    previous = lum;
                }
                if (expected != null && measured != null)
                {
                    result.Add((expected.Value, measured.Value));
                }
            }
            return result;
        }

        [UnityTest]
        public IEnumerator TheTerminatorIsWhereTheLightPutsItInBothStyles()
        {
            yield return ShowWhiteMoon(60.0);
            var cam = Camera.main;
            var view = host.Views[Index(301)];
            var lines = new Dictionary<VisualStyle, List<(int Expected, int Measured)>>();
            foreach (var style in new[] { VisualStyle.Realistic, VisualStyle.Stylized })
            {
                settings.SetStyle(style);
                var pixels = new Color[1][];
                yield return Render(pixels);
                lines[style] = Terminator(cam, view, pixels[0], Rise[style]);
            }
            var realistic = lines[VisualStyle.Realistic];
            var stylized = lines[VisualStyle.Stylized];
            Assert.That(realistic.Count, Is.GreaterThan(20), "scan lines across the terminator");
            Assert.That(stylized.Count, Is.EqualTo(realistic.Count));
            var all = string.Join(", ",
                                  realistic.Select((r, i) => $"{r.Expected}/{r.Measured}/{stylized[i].Measured}"));
            TestContext.WriteLine("expected/realistic/stylized per line: " + all);
            Debug.Log($"UniverseSim terminator, {realistic.Count} lines, expected/realistic/stylized: {all}");
            for (var i = 0; i < realistic.Count; ++i)
            {
                var detail = $"line {i}: expected {realistic[i].Expected}, realistic {realistic[i].Measured}, " +
                             $"stylized {stylized[i].Measured} (pixels from the start of the scan); all lines: {all}";
                Assert.That(Math.Abs(realistic[i].Measured - realistic[i].Expected), Is.LessThanOrEqualTo(1), detail);
                Assert.That(Math.Abs(stylized[i].Measured - stylized[i].Expected), Is.LessThanOrEqualTo(1), detail);
                Assert.That(Math.Abs(stylized[i].Measured - realistic[i].Measured), Is.LessThanOrEqualTo(1), detail);
            }
        }

        [UnityTest]
        public IEnumerator NoSurfaceExceedsTheBrightnessCap()
        {
            // White, near full phase and under the rim light: the brightest a surface gets. Before post-processing,
            // so the values are the surface's own.
            yield return ShowWhiteMoon(20.0);
            var cam = Camera.main;
            var view = host.Views[Index(301)];
            var pixels = new Color[1][];
            yield return Render(pixels, postProcessing: false);
            var cap = Object.FindAnyObjectByType<StylizedBodyShading>().BrightnessCap;
            var brightest = 0f;
            var count = 0;
            for (var y = 0; y < Height; ++y)
            {
                for (var x = 0; x < Width; ++x)
                {
                    if (NdotL(cam, view, x, y) == null)
                    {
                        continue;
                    }
                    var c = pixels[0][y * Width + x];
                    brightest = Mathf.Max(brightest, Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
                    ++count;
                }
            }
            Assert.That(count, Is.GreaterThan(10000), "pixels on the Moon");
            Assert.That(brightest, Is.GreaterThan(0.8f * cap), "the white Moon in full light");
            Assert.That(brightest, Is.LessThanOrEqualTo(cap + 2e-3f));
        }
    }
}
