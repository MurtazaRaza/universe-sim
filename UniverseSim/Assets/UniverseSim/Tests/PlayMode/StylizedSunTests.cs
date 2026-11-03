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
    /// The Sun in the stylized style (A-08, docs/ART_DIRECTION.md section 7): its disk keeps its true size in each
    /// scale mode with the glow around it, the glow shows only in the stylized style, and the glare goes when a body
    /// covers the Sun.
    /// </summary>
    public class StylizedSunTests
    {
        private const string FixedTime = "2026-10-21T12:00:00 UTC";
        private const int Width = 1600;
        private const int Height = 900;

        private StylePreference preference;
        private RenderTexture target;
        private SimulationHost host;
        private CameraRig rig;
        private VisualStyleSettings settings;
        private StylizedSun sun;

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
            sun = Object.FindAnyObjectByType<StylizedSun>();
            Assert.That(sun, Is.Not.Null);
            Assert.That(host.Simulation, Is.Not.Null, host.LoadError);
            host.Simulation.SetPaused(true);
            Assert.That(host.Simulation.TryParseTime(FixedTime, out var t, out var error), error);
            host.Simulation.SetTime(t);
            Object.FindAnyObjectByType<OrbitLines>().Visible = false;
            Object.FindAnyObjectByType<StarField>().Visible = false;
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

        private static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        // Renders a frame into the linear half-float target and returns its pixels (row 0 at the bottom).
        private IEnumerator Render(Color[][] result, bool postProcessing)
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

        // The disk along the row through its center: the pixels at least 97 % as bright as the center (pixels the
        // disk only partly covers, blended with what is around it, do not count). Its half length is the radius; the
        // brightest pixel of the row outside it, and the mean of the ring from 1.2 to 1.6 radii, are returned too.
        private static (float Radius, float Center, float BrightestOutside, float Around) Disk(Color[] pixels, Vector3 center)
        {
            var cx = Mathf.FloorToInt(center.x);
            var cy = Mathf.FloorToInt(center.y);
            var row = cy * Width;
            var middle = Luminance(pixels[row + cx]);
            var left = cx;
            var right = cx;
            while (left > 0 && Luminance(pixels[row + left - 1]) >= 0.97f * middle)
            {
                --left;
            }
            while (right < Width - 1 && Luminance(pixels[row + right + 1]) >= 0.97f * middle)
            {
                ++right;
            }
            var radius = 0.5f * (right - left + 1);
            var brightest = 0f;
            for (var x = 0; x < Width; ++x)
            {
                if (x < left - 1 || x > right + 1)
                {
                    brightest = Mathf.Max(brightest, Luminance(pixels[row + x]));
                }
            }
            var sum = 0f;
            var count = 0;
            for (var y = Mathf.Max(0, cy - 2 * (int)radius); y < Mathf.Min(Height, cy + 2 * (int)radius); ++y)
            {
                for (var x = Mathf.Max(0, cx - 2 * (int)radius); x < Mathf.Min(Width, cx + 2 * (int)radius); ++x)
                {
                    var d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / radius;
                    if (d >= 1.2f && d <= 1.6f)
                    {
                        sum += Luminance(pixels[y * Width + x]);
                        ++count;
                    }
                }
            }
            return (radius, middle, brightest, count > 0 ? sum / count : 0f);
        }

        [UnityTest]
        public IEnumerator TheDiskKeepsItsTrueSizeInEachScaleMode()
        {
            var index = Index(10);
            var view = host.Views[index];
            var cam = Camera.main;
            var report = new List<string>();
            foreach (var enlarged in new[] { false, true })
            {
                rig.EnlargedBodies = enlarged;
                rig.JumpTo(index, host.Simulation.Bodies[index].MaxRadiusKm * 6.0, 0.4, 0.2);
                rig.Select(index);
                yield return null;
                yield return null;
                var measured = new Dictionary<VisualStyle, (float Radius, float Center, float BrightestOutside, float Around)>();
                foreach (var style in new[] { VisualStyle.Realistic, VisualStyle.Stylized })
                {
                    settings.SetStyle(style);
                    // Before post-processing, so the values are the disk's and the glow's own.
                    var pixels = new Color[1][];
                    yield return Render(pixels, postProcessing: false);
                    measured[style] = Disk(pixels[0], cam.WorldToScreenPoint(view.transform.position));
                }
                var focalPixels = 0.5f * cam.pixelHeight / Mathf.Tan(0.5f * cam.fieldOfView * Mathf.Deg2Rad);
                var expected = SphereLods.PixelRadius(view.MaxRenderRadius,
                                                      Vector3.Distance(view.transform.position, cam.transform.position),
                                                      focalPixels);
                var realistic = measured[VisualStyle.Realistic];
                var stylized = measured[VisualStyle.Stylized];
                var line = $"enlarged {enlarged}: expected radius {expected:0.0} px, realistic {realistic.Radius:0.0}, " +
                           $"stylized {stylized.Radius:0.0}; stylized center {stylized.Center:0.00}, brightest " +
                           $"outside {stylized.BrightestOutside:0.00}, glow at 1.2 to 1.6 radii {stylized.Around:0.00} " +
                           $"(realistic {realistic.Around:0.000})";
                report.Add(line);
                Assert.That(Math.Abs(realistic.Radius - expected), Is.LessThanOrEqualTo(1.5f), line);
                Assert.That(Math.Abs(stylized.Radius - realistic.Radius), Is.LessThanOrEqualTo(1f), line);
                // The glow is there and stays below the disk, so the disk's edge is where the realistic one is.
                Assert.That(stylized.Around, Is.GreaterThan(0.1f), line);
                Assert.That(realistic.Around, Is.LessThan(0.01f), line);
                Assert.That(stylized.BrightestOutside, Is.LessThan(0.97f * stylized.Center), line);
            }
            TestContext.WriteLine(string.Join("\n", report));
            Debug.Log("UniverseSim Sun disk: " + string.Join("; ", report));
            rig.EnlargedBodies = false;
        }

        private List<string> ActiveSubjects()
        {
            var list = new List<Exaggeration>();
            Object.FindAnyObjectByType<ExaggerationRegister>().CollectActive(Camera.main, list);
            return list.Select(e => e.Subject).ToList();
        }

        [UnityTest]
        public IEnumerator TheGlowShowsOnlyInTheStylizedStyleAndItsGlareGoesWhenABodyCoversTheSun()
        {
            var index = Index(10);
            var view = host.Views[index];
            rig.JumpTo(index, host.Simulation.Bodies[index].MaxRadiusKm * 6.0, 0.4, 0.2);
            yield return null;
            yield return null;
            Assert.That(sun.GlowOf(view), Is.Not.Null);
            Assert.That(sun.GlowOf(view).enabled, "the glow in the stylized style");
            Assert.That(sun.Visibility, Is.EqualTo(1f));
            Assert.That(ActiveSubjects(), Does.Contain("The Sun").And.Contain("Corona").And.Contain("Glare"));

            settings.SetStyle(VisualStyle.Realistic);
            yield return null;
            Assert.That(sun.GlowOf(view).enabled, Is.False, "no glow in the realistic style");
            Assert.That(ActiveSubjects(), Is.Empty);

            // From the Earth's night side, looking back at the Earth: it covers the Sun.
            settings.SetStyle(VisualStyle.Stylized);
            var earth = Index(399);
            var light = ((Vector3)host.Views[earth].LightDirection).normalized;
            rig.JumpTo(earth, host.Simulation.Bodies[earth].MaxRadiusKm * 4.0, Math.Atan2(-light.x, -light.z),
                       Math.Asin(Mathf.Clamp(-light.y, -1f, 1f)));
            yield return null;
            yield return null;
            Assert.That(sun.Visibility, Is.EqualTo(0f), "the Earth covers the Sun");
        }
    }
}
