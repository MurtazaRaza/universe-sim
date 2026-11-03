using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UniverseSim.Unity.Tests.PlayMode
{
    /// <summary>
    /// The relative order of stars and ring regions holds in both styles (A-09, SPEC section 11.1): a brighter star
    /// never looks fainter than a dimmer one, a bluer star never looks redder, and a denser ring region never looks
    /// thinner or more transparent. Synthetic stars (a ladder of magnitudes and color indices) and a synthetic ring
    /// profile (bands of rising optical depth), obviously not catalog data, are drawn through the viewer's own
    /// shaders, materials and display mapping by a probe camera that sees only them, without post-processing.
    /// </summary>
    public class StarAndRingOrderTests
    {
        private const int Width = 1600;
        private const int Height = 900;
        private const int ProbeLayer = 30;
        private const float Depth = 10f;

        private StylePreference preference;
        private VisualStyleSettings settings;
        private Camera probe;
        private RenderTexture target;
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
            settings = Object.FindAnyObjectByType<VisualStyleSettings>();
            Assert.That(Object.FindAnyObjectByType<SimulationHost>().Simulation, Is.Not.Null);
            Camera.main.cullingMask &= ~(1 << ProbeLayer);
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            probe = new GameObject("Order probe camera").AddComponent<Camera>();
            probe.cullingMask = 1 << ProbeLayer;
            probe.clearFlags = CameraClearFlags.SolidColor;
            probe.backgroundColor = Color.black;
            probe.fieldOfView = 50f;
            probe.nearClipPlane = 0.1f;
            probe.farClipPlane = 100f;
            probe.allowHDR = true;
            probe.allowMSAA = false;
            probe.targetTexture = target;
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            created.Clear();
            created.Add(probe.gameObject);
            yield return null;
        }

        [TearDown]
        public void CleanUp()
        {
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
        }

        private IEnumerator Render(Color background, Color[][] result)
        {
            probe.backgroundColor = background;
            yield return null;
            yield return null;
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(Width, Height, TextureFormat.RGBAHalf, false, true);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            result[0] = image.GetPixels();
            Object.Destroy(image);
        }

        private static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        private GameObject Probed(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name) { layer = ProbeLayer };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            created.Add(go);
            created.Add(mesh);
            return go;
        }

        // ---- Stars ----

        private sealed class SyntheticStar
        {
            public Vector2 Pixel;
            public double V;
            public double BMinusV;
        }

        // The stars as StarField draws them (its display mapping and quad layout), each at the center of a pixel.
        private void DrawStars(IReadOnlyList<SyntheticStar> stars)
        {
            var field = Object.FindAnyObjectByType<StarField>();
            var material = field.GetComponent<MeshRenderer>().sharedMaterial;
            var vertices = new Vector3[4 * stars.Count];
            var corners = new Vector4[4 * stars.Count];
            var colors = new Color[4 * stars.Count];
            var indices = new int[6 * stars.Count];
            for (var k = 0; k < stars.Count; ++k)
            {
                var s = stars[k];
                var (halfSize, color) = StarField.DisplayOf(s.V, true, s.BMinusV, 6.5f);
                StarField.WriteQuad(k, halfSize, color, corners, colors, indices);
                var p = probe.ScreenToWorldPoint(new Vector3(s.Pixel.x + 0.5f, s.Pixel.y + 0.5f, Depth));
                for (var c = 0; c < 4; ++c)
                {
                    vertices[4 * k + c] = p;
                }
            }
            var mesh = new Mesh { name = "Synthetic stars" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, corners);
            mesh.SetColors(colors);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100f);
            Probed("Synthetic stars", mesh, material);
        }

        // The light of a star: the sum over a box around it (stars are added to a black sky), per channel.
        private static Color Sum(Color[] pixels, Vector2 pixel, int half)
        {
            var sum = Color.clear;
            for (var y = (int)pixel.y - half; y <= (int)pixel.y + half; ++y)
            {
                for (var x = (int)pixel.x - half; x <= (int)pixel.x + half; ++x)
                {
                    if (x >= 0 && y >= 0 && x < Width && y < Height)
                    {
                        sum += pixels[y * Width + x];
                    }
                }
            }
            return sum;
        }

        [UnityTest]
        public IEnumerator ABrighterStarNeverLooksFainterAndABluerStarNeverLooksRedder()
        {
            // Two rows of nine stars from V 6.5 (the faintest drawn) to -1.5 (the brightest), and two rows of thirteen
            // stars from B-V -0.4 to 2.0, one at a magnitude without a sparkle and one with.
            var magnitudes = Enumerable.Range(0, 17).Select(i => 6.5 - 0.5 * i).ToList();
            var ladder = magnitudes.Select((v, i) => new SyntheticStar
            {
                Pixel = new Vector2(100 + 160 * (i % 9), i < 9 ? 150 : 330), V = v, BMinusV = 0.6,
            }).ToList();
            var colorIndices = Enumerable.Range(0, 13).Select(i => -0.4 + 0.2 * i).ToList();
            var colorRows = new[] { 3.0, -1.0 }.SelectMany((v, row) => colorIndices.Select((bv, i) => new SyntheticStar
            {
                Pixel = new Vector2(80 + 120 * i, row == 0 ? 540 : 750), V = v, BMinusV = bv,
            })).ToList();
            DrawStars(ladder.Concat(colorRows).ToList());

            foreach (var style in new[] { VisualStyle.Realistic, VisualStyle.Stylized })
            {
                settings.SetStyle(style);
                var pixels = new Color[1][];
                yield return Render(Color.black, pixels);
                var light = ladder.Select(s => Luminance(Sum(pixels[0], s.Pixel, 62))).ToList();
                var peaks = ladder.Select(s => Luminance(pixels[0][(int)s.Pixel.y * Width + (int)s.Pixel.x])).ToList();
                var report = $"{style}: light per star from V 6.5 to -1.5: " +
                             string.Join(", ", light.Select(l => l.ToString("0.00"))) + "; peaks: " +
                             string.Join(", ", peaks.Select(l => l.ToString("0.000")));
                TestContext.WriteLine(report);
                Debug.Log("UniverseSim star order, " + report);
                Assert.That(light[0], Is.GreaterThan(0f), report);
                for (var i = 1; i < ladder.Count; ++i)
                {
                    Assert.That(light[i], Is.GreaterThan(light[i - 1]), $"star {i}: {report}");
                    Assert.That(peaks[i], Is.GreaterThanOrEqualTo(peaks[i - 1] - 1e-3f), $"star {i}: {report}");
                }
                for (var row = 0; row < 2; ++row)
                {
                    var stars = colorRows.Skip(13 * row).Take(13).ToList();
                    var redness = stars.Select(s =>
                    {
                        var sum = Sum(pixels[0], s.Pixel, 58);
                        return sum.r / Math.Max(sum.b, 1e-6f);
                    }).ToList();
                    var detail = $"{style}, V {stars[0].V}: red / blue from B-V -0.4 to 2.0: " +
                                 string.Join(", ", redness.Select(r => r.ToString("0.000")));
                    TestContext.WriteLine(detail);
                    for (var i = 1; i < stars.Count; ++i)
                    {
                        Assert.That(redness[i], Is.GreaterThanOrEqualTo(redness[i - 1] - 1e-3f), $"star {i}: {detail}");
                    }
                    Assert.That(redness[stars.Count - 1], Is.GreaterThan(redness[0]), detail);
                }
            }
        }

        // ---- Rings ----

        // Bands of rising optical depth from the inner edge outward (synthetic, not ring data).
        private static readonly float[] Depths = { 0.02f, 0.05f, 0.1f, 0.2f, 0.4f, 0.7f, 1f, 1.5f, 2f, 3f, 5f };
        private const float InnerFraction = 0.4f;
        private const float OuterRadius = 4f; // world units at Depth: about 390 pixels

        private (Transform Ring, Material Material) DrawRing()
        {
            var ringMaterial = Object.FindAnyObjectByType<RingSystems>().transform.Cast<Transform>()
                .First(t => t.name.StartsWith("Rings of")).GetComponent<MeshRenderer>().sharedMaterial;
            var material = new Material(ringMaterial);
            created.Add(material);
            const int texelsPerBand = 64;
            var profile = new Texture2D(Depths.Length * texelsPerBand, 1, TextureFormat.RFloat, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            profile.SetPixelData(Depths.SelectMany(d => Enumerable.Repeat(d, texelsPerBand)).ToArray(), 0);
            profile.Apply(false, true);
            created.Add(profile);
            material.SetTexture("_Profile", profile);
            material.SetFloat("_InnerFraction", InnerFraction);
            // Lit from the camera's side, and the planet far off to the side so it casts no shadow on the probe.
            material.SetVector("_LightDirection", new Vector4(0f, 0f, -1f, 0f));
            material.SetMatrix("_PlanetWorldToLocal", Matrix4x4.Translate(new Vector3(1.0e4f, 0f, 0f)));
            var go = Probed("Synthetic ring", RingSystems.BuildAnnulus(InnerFraction, 720), material);
            go.transform.localScale = Vector3.one * OuterRadius;
            return (go.transform, material);
        }

        // The middle of each band along the ring's vertical diameter, upward from the center (it stays in place when
        // the ring turns about the vertical axis).
        private List<Vector2> BandCenters()
        {
            var center = probe.WorldToScreenPoint(new Vector3(0f, 0f, Depth));
            var top = probe.WorldToScreenPoint(new Vector3(0f, OuterRadius, Depth));
            var outerPixels = top.y - center.y;
            return Depths.Select((_, k) =>
            {
                var fraction = InnerFraction + (k + 0.5f) / Depths.Length * (1f - InnerFraction);
                return new Vector2(center.x, center.y + fraction * outerPixels);
            }).ToList();
        }

        private static Color Mean(Color[] pixels, Vector2 at)
        {
            var sum = Color.clear;
            for (var y = (int)at.y - 2; y <= (int)at.y + 2; ++y)
            {
                for (var x = (int)at.x - 2; x <= (int)at.x + 2; ++x)
                {
                    sum += pixels[y * Width + x];
                }
            }
            return sum / 25f;
        }

        [UnityTest]
        public IEnumerator ADenserRingRegionNeverLooksThinner()
        {
            var (ring, _) = DrawRing();
            var bands = BandCenters();
            foreach (var tilt in new[] { 0f, 60f })
            {
                // Face on (the ring's normal toward the camera), then turned 60 degrees about the vertical.
                ring.SetPositionAndRotation(new Vector3(0f, 0f, Depth),
                                            Quaternion.Euler(0f, tilt, 0f) * Quaternion.Euler(-90f, 0f, 0f));
                foreach (var style in new[] { VisualStyle.Realistic, VisualStyle.Stylized })
                {
                    settings.SetStyle(style);
                    var overBlack = new Color[1][];
                    var overWhite = new Color[1][];
                    yield return Render(Color.black, overBlack);
                    yield return Render(Color.white, overWhite);
                    // Over black the ring shows its light times its opacity; over white, plus the white it lets through.
                    var light = bands.Select(b => Luminance(Mean(overBlack[0], b))).ToList();
                    var opacity = bands.Select(b =>
                    {
                        var black = Mean(overBlack[0], b);
                        var white = Mean(overWhite[0], b);
                        return 1f - ((white.r - black.r) + (white.g - black.g) + (white.b - black.b)) / 3f;
                    }).ToList();
                    var report = $"{style}, tilt {tilt}: optical depth / light / opacity: " +
                                 string.Join(", ", Depths.Select((d, k) => $"{d}/{light[k]:0.000}/{opacity[k]:0.000}"));
                    TestContext.WriteLine(report);
                    Debug.Log("UniverseSim ring order, " + report);
                    Assert.That(opacity[0], Is.GreaterThan(0f), report);
                    for (var k = 1; k < Depths.Length; ++k)
                    {
                        Assert.That(light[k], Is.GreaterThanOrEqualTo(light[k - 1] - 1e-3f), $"band {k}: {report}");
                        Assert.That(opacity[k], Is.GreaterThanOrEqualTo(opacity[k - 1] - 1e-3f), $"band {k}: {report}");
                    }
                    Assert.That(light[Depths.Length - 1], Is.GreaterThan(light[0]), report);
                }
            }
        }
    }
}
