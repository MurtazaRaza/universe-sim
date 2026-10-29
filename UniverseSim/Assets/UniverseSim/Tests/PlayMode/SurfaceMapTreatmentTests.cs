using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UniverseSim.Unity.Tests.PlayMode
{
    /// <summary>
    /// The stylized map treatment keeps each map's hue (A-06, docs/ART_DIRECTION.md section 4): every surface map is
    /// drawn as published and treated through MapTreatmentProbe.shader, which runs BodyLit's own treatment code with
    /// the scene's SurfaceMapTreatment values, and the average hues are compared. The average hue is the HSV hue of
    /// the area-weighted mean linear color, converted to sRGB; maps whose mean color has a chroma below 0.02 are grey
    /// and must stay grey.
    /// </summary>
    public class SurfaceMapTreatmentTests
    {
        // From docs/ART_DIRECTION.md section 4 (decided in A-01).
        private const double HueToleranceDegrees = 5.0;
        private const double GreyChroma = 0.02;

        private const int Width = 1024;
        private const int Height = 512;

        private StylePreference preference;

        [OneTimeSetUp]
        public void KeepPreference() => preference = StylePreference.Keep();

        [OneTimeTearDown]
        public void RestorePreference() => preference.Restore();

        [UnityTest]
        public IEnumerator EachMapKeepsItsHue()
        {
            VisualStyleSettings.SavePreference(VisualStyle.Stylized);
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            var host = Object.FindAnyObjectByType<SimulationHost>();
            var treatment = Object.FindAnyObjectByType<SurfaceMapTreatment>();
            Assert.That(host.Simulation, Is.Not.Null, host.LoadError);
            Assert.That(treatment, Is.Not.Null);
            var maps = host.SurfaceMaps.All.ToList();
            Assert.That(maps.Count, Is.GreaterThan(0), "no surface maps (usim-pipeline textures)");

            var shader = Shader.Find("Hidden/UniverseSim/MapTreatmentProbe");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            var target = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBFloat,
                                           RenderTextureReadWrite.Linear);
            var image = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true);
            Shader.SetGlobalVector("_UsimMapPaint", treatment.PaintParameters);
            Shader.SetGlobalVector("_UsimMapGrade", treatment.GradeParameters);

            var report = new StringBuilder("Average hue of each surface map, published and treated (degrees)\n");
            var failures = new StringBuilder();
            foreach (var (info, texture) in maps)
            {
                material.SetVector("_MapTexelSize", new Vector4(1f / texture.width, 1f / texture.height,
                                                                texture.width, texture.height));
                material.SetFloat("_ProbeMip", Mathf.Max(0f, Mathf.Log(texture.width / (float)Width, 2f)));
                var published = MeanColor(texture, material, 0f, target, image);
                var treated = MeanColor(texture, material, 1f, target, image);
                var (hue0, chroma0) = HueAndChroma(published);
                var (hue1, chroma1) = HueAndChroma(treated);
                if (chroma0 < GreyChroma)
                {
                    report.AppendLine($"{info.name}: grey (chroma {Format(chroma0)} -> {Format(chroma1)})");
                    if (chroma1 >= GreyChroma)
                    {
                        failures.AppendLine($"{info.name} is grey but its treated mean has chroma {Format(chroma1)}");
                    }
                    continue;
                }
                var change = Math.Abs(hue1 - hue0);
                change = Math.Min(change, 360.0 - change);
                report.AppendLine($"{info.name}: {Format(hue0)} -> {Format(hue1)}, change {Format(change)}");
                if (change > HueToleranceDegrees)
                {
                    failures.AppendLine($"{info.name}: hue changed by {Format(change)} degrees");
                }
            }
            TestContext.WriteLine(report.ToString());
            Debug.Log(report.ToString());

            Object.Destroy(material);
            Object.Destroy(image);
            target.Release();
            Object.Destroy(target);
            Assert.That(failures.Length, Is.Zero, failures.ToString());
        }

        private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        // The mean linear color of the map drawn through the probe, each row weighted by the cosine of its latitude
        // (the maps are equirectangular, so that is each row's share of the area).
        private static Color MeanColor(Texture texture, Material material, float treat, RenderTexture target,
                                       Texture2D image)
        {
            material.SetFloat("_Treat", treat);
            Graphics.Blit(texture, target, material);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            var pixels = image.GetPixels();
            double r = 0, g = 0, b = 0, weights = 0;
            for (var y = 0; y < Height; ++y)
            {
                var weight = Math.Cos(((y + 0.5) / Height - 0.5) * Math.PI);
                for (var x = 0; x < Width; ++x)
                {
                    var c = pixels[y * Width + x];
                    r += weight * c.r;
                    g += weight * c.g;
                    b += weight * c.b;
                }
                weights += weight * Width;
            }
            Assert.That(r + g + b, Is.GreaterThan(0.0), "the probe drew nothing");
            return new Color((float)(r / weights), (float)(g / weights), (float)(b / weights));
        }

        // The HSV hue (degrees) and the chroma (largest minus smallest channel) of a linear color, in sRGB.
        private static (double Hue, double Chroma) HueAndChroma(Color linear)
        {
            var srgb = linear.gamma;
            Color.RGBToHSV(srgb, out var h, out _, out _);
            var chroma = Mathf.Max(srgb.r, Mathf.Max(srgb.g, srgb.b)) - Mathf.Min(srgb.r, Mathf.Min(srgb.g, srgb.b));
            return (h * 360.0, chroma);
        }
    }
}
