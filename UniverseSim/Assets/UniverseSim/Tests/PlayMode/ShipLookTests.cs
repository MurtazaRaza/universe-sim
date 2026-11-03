using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UniverseSim.Unity.Tests.PlayMode
{
    /// <summary>
    /// The spaceship's look (docs/ART_DIRECTION.md section 11): the ship's mesh with the scene's ship material and the
    /// bodies' stylized shading, drawn by a probe camera that sees only it. Its paint stays below the bodies'
    /// brightness cap from every side and under every light, so only the engines reach the bloom; in a body's shadow
    /// it stays readable. With USIM_SCREENSHOT_DIR set it saves views of the model alone, with post-processing, in a
    /// "ship-look" folder.
    /// </summary>
    public class ShipLookTests
    {
        // Measured at a modest size and read back into one texture kept for the whole test, so no render allocates
        // much (large allocations per render stalled the next scene load). The saved views are larger.
        private const int Width = 600;
        private const int Height = 400;
        private const int ShotWidth = 1200;
        private const int ShotHeight = 800;
        private const int ProbeLayer = 30;
        private const float Distance = 3f;

        private static readonly int LightDirectionId = Shader.PropertyToID("_LightDirection");
        private static readonly int SunlitId = Shader.PropertyToID("_Sunlit");
        private static readonly int EngineGlowId = Shader.PropertyToID("_EngineGlow");
        private static readonly int SkyLightId = Shader.PropertyToID("_SkyLight");
        private static readonly int SkyUpId = Shader.PropertyToID("_SkyUp");

        private StylePreference preference;
        private Camera probe;
        private RenderTexture target;
        private Texture2D readback;
        private GameObject ship;
        private Material material;
        private readonly List<Object> created = new List<Object>();

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
            Assert.That(Object.FindAnyObjectByType<SimulationHost>().Simulation, Is.Not.Null);
            var pilot = Object.FindAnyObjectByType<ShipPilot>();
            var source = pilot.transform.Find("Spaceship").GetComponent<MeshRenderer>().sharedMaterial;
            Assert.That(source, Is.Not.Null, "the scene's ship material");
            Camera.main.cullingMask &= ~(1 << ProbeLayer);

            probe = new GameObject("Ship probe camera").AddComponent<Camera>();
            probe.cullingMask = 1 << ProbeLayer;
            probe.clearFlags = CameraClearFlags.SolidColor;
            probe.backgroundColor = Color.black;
            probe.fieldOfView = 30f;
            probe.nearClipPlane = 0.1f;
            probe.farClipPlane = 100f;
            probe.allowHDR = true;
            probe.allowMSAA = false;
            // One target for the whole test, as the other probe tests keep: linear half floats without
            // post-processing, so the values read are the surface's own.
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            probe.targetTexture = target;
            probe.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            readback = new Texture2D(Width, Height, TextureFormat.RGBAHalf, false, true);
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            created.Clear();
            created.Add(probe.gameObject);

            material = new Material(source) { name = "Ship look probe" };
            created.Add(material);
            var mesh = ShipMesh.Create();
            created.Add(mesh);
            ship = new GameObject("Ship look probe") { layer = ProbeLayer };
            ship.AddComponent<MeshFilter>().sharedMesh = mesh;
            ship.AddComponent<MeshRenderer>().sharedMaterial = material;
            ship.transform.position = new Vector3(0f, 0f, Distance);
            created.Add(ship);
            yield return null;
        }

        [TearDown]
        public void CleanUp()
        {
            if (probe != null)
            {
                probe.targetTexture = null;
            }
            foreach (var o in created)
            {
                if (o != null)
                {
                    Object.Destroy(o);
                }
            }
            created.Clear();
            if (target != null)
            {
                target.Release();
                Object.Destroy(target);
            }
            if (readback != null)
            {
                Object.Destroy(readback);
            }
        }

        // Points the ship (nose +z, top +y) as `rotation` and lights it from `light` (render space, toward the light).
        private void Pose(Quaternion rotation, Vector3 light, float sunlit = 1f, float glow = 0f)
        {
            ship.transform.rotation = rotation;
            material.SetVector(LightDirectionId, light.normalized);
            material.SetFloat(SunlitId, sunlit);
            material.SetFloat(EngineGlowId, glow);
            material.SetVector(SkyLightId, Vector4.zero);
            material.SetVector(SkyUpId, Vector3.up);
        }

        // Renders a frame with the probe and reads it back: the largest channel of the brightest pixel (linear), how many
        // pixels the ship covers (the background is black) and their mean luminance.
        private IEnumerator Measure(float[] brightest, int[] count, float[] meanLuminance = null)
        {
            yield return null;
            yield return null;
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            RenderTexture.active = previous;
            var data = readback.GetPixelData<ushort>(0); // half floats, RGBA
            var peak = 0f;
            var covered = 0;
            var sum = 0.0;
            for (var i = 0; i < data.Length; i += 4)
            {
                var r = Mathf.HalfToFloat(data[i]);
                var g = Mathf.HalfToFloat(data[i + 1]);
                var b = Mathf.HalfToFloat(data[i + 2]);
                var c = Mathf.Max(r, Mathf.Max(g, b));
                if (c > 0f)
                {
                    ++covered;
                    sum += 0.2126 * r + 0.7152 * g + 0.0722 * b;
                }
                peak = Mathf.Max(peak, c);
            }
            brightest[0] = peak;
            count[0] = covered;
            if (meanLuminance != null)
            {
                meanLuminance[0] = covered > 0 ? (float)(sum / covered) : 0f;
            }
        }

        // Views from behind and above (as the chase camera sees it), from the front and above, the side and below,
        // each lit from in front of the camera, from the side, from behind the ship (the rims' strongest case) and
        // from below. Euler angles turn about x before y: a yaw past 90 degrees turns the pitched top away, so the
        // views from the front pitch the other way.
        private static IEnumerable<(string Name, Quaternion Rotation)> Views()
        {
            yield return ("chase", Quaternion.Euler(-20f, 0f, 0f));
            yield return ("front", Quaternion.Euler(20f, 150f, 0f));
            yield return ("side", Quaternion.Euler(-10f, 90f, 0f));
            yield return ("below", Quaternion.Euler(35f, 30f, 0f));
        }

        private static readonly Vector3[] Lights =
        {
            new Vector3(0.5f, 0.6f, -0.6f), new Vector3(1f, 0.2f, 0f), new Vector3(-0.2f, 0.4f, 1f), new Vector3(0f, -1f, 0f),
        };

        [UnityTest]
        public IEnumerator ThePaintStaysBelowTheBrightnessCapAndOnlyTheEnginesGlow()
        {
            var cap = Object.FindAnyObjectByType<StylizedBodyShading>().BrightnessCap;
            var peak = new float[1];
            var count = new int[1];
            var brightest = 0f;
            foreach (var (name, rotation) in Views())
            {
                foreach (var light in Lights)
                {
                    Pose(rotation, light);
                    yield return Measure(peak, count);
                    Assert.That(count[0], Is.GreaterThan(5000), $"pixels on the ship ({name})");
                    Assert.That(peak[0], Is.LessThanOrEqualTo(cap + 2e-3f), $"{name}, light {light}");
                    brightest = Mathf.Max(brightest, peak[0]);
                }
            }
            Assert.That(brightest, Is.GreaterThan(0.8f * cap), "the hull in full light");

            // The engines glow past the cap, into the bloom (threshold about 1.9, linear).
            Pose(Quaternion.Euler(-20f, 0f, 0f), Lights[0], 1f, 1f);
            yield return Measure(peak, count);
            Assert.That(peak[0], Is.GreaterThan(1.9f), "the nozzles bloom");
        }

        [UnityTest]
        public IEnumerator InABodysShadowTheShipStaysReadable()
        {
            // All night side: the lifted night and the night rim, far darker than the day side but outlined.
            var peak = new float[1];
            var count = new int[1];
            var mean = new float[1];
            Pose(Quaternion.Euler(-20f, 0f, 0f), Lights[0]);
            yield return Measure(peak, count, mean);
            var day = mean[0];
            var dayCount = count[0];
            Pose(Quaternion.Euler(-20f, 0f, 0f), Lights[0], 0f);
            yield return Measure(peak, count, mean);
            TestContext.WriteLine($"mean luminance: day {day:0.000}, in shadow {mean[0]:0.000}; brightest in shadow {peak[0]:0.000}");
            Assert.That(count[0], Is.EqualTo(dayCount).Within(dayCount / 100), "the same silhouette");
            Assert.That(peak[0], Is.GreaterThan(0.1f), "the night rim outlines it");
            Assert.That(mean[0], Is.LessThan(0.4f * day), "far darker than in sunlight");
        }

        [UnityTest]
        public IEnumerator ViewsOfTheShipModel()
        {
            var root = Environment.GetEnvironmentVariable("USIM_SCREENSHOT_DIR");
            if (string.IsNullOrEmpty(root))
            {
                Assert.Ignore("set USIM_SCREENSHOT_DIR to save the views of the ship model");
            }
            var folder = Path.Combine(root, "ship-look");
            Directory.CreateDirectory(folder);
            var sunny = new Vector3(0.5f, 0.6f, -0.6f);
            var marsSky = new Color(0.74f, 0.62f, 0.52f).linear * 0.45f;
            var shots = new (string Name, Quaternion Rotation, Vector3 Light, float Sunlit, float Glow, Color Sky)[]
            {
                ("chase", Quaternion.Euler(-20f, 0f, 0f), sunny, 1f, 0.6f, Color.clear),
                ("chase-low-sun", Quaternion.Euler(-20f, 0f, 0f), new Vector3(1f, 0.15f, 0.3f), 1f, 0.6f, Color.clear),
                ("front", Quaternion.Euler(20f, 150f, 0f), sunny, 1f, 0.4f, Color.clear),
                ("side", Quaternion.Euler(-10f, 90f, 0f), sunny, 1f, 0.4f, Color.clear),
                ("top", Quaternion.Euler(65f, 200f, 0f), sunny, 1f, 0.4f, Color.clear),
                ("shadow", Quaternion.Euler(-20f, 0f, 0f), sunny, 0f, 0.6f, Color.clear),
                ("mars-sky", Quaternion.Euler(-20f, 0f, 0f), sunny, 1f, 0.3f, marsSky),
            };

            // An 8-bit target with post-processing for this test, as the viewer shows it, on the viewer's sky.
            probe.backgroundColor = Camera.main.backgroundColor;
            probe.targetTexture = null;
            target.Release();
            Object.Destroy(target);
            target = new RenderTexture(ShotWidth, ShotHeight, 24, RenderTextureFormat.ARGB32);
            probe.targetTexture = target;
            probe.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var image = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
            created.Add(image);
            foreach (var shot in shots)
            {
                Pose(shot.Rotation, shot.Light, shot.Sunlit, shot.Glow);
                material.SetVector(SkyLightId, new Vector4(shot.Sky.r, shot.Sky.g, shot.Sky.b, 0f));
                yield return null;
                yield return null;
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0, false);
                RenderTexture.active = previous;
                File.WriteAllBytes(Path.Combine(folder, shot.Name + ".png"), image.EncodeToPNG());
            }
        }
    }
}
