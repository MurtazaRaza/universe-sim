using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UniverseSim.Unity.Native;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UniverseSim.Unity.Tests.PlayMode
{
    /// <summary>
    /// Art direction mockups (A-01, OQ-09): renders the Earth, the Moon, Mars, Jupiter and Saturn's rings from
    /// the real scene, once in the current (realistic) look and once in each candidate stylized look, for the
    /// project owner to choose from. Runs only with USIM_MOCKUP_DIR set (the folder for the images). The
    /// candidates swap in the shaders of Mockups/ and add a post-processing volume; the halos, their twilight
    /// colors and the ocean glint are placed by hand for the pictures only (A-07 takes atmospheres from a
    /// registered source). Positions, orientations, the time and the light direction come from the core as in
    /// the viewer.
    /// </summary>
    public class ArtDirectionMockups
    {
        private const string FixedTime = "2026-10-21T12:00:00 UTC";

        private SimulationHost host;
        private CameraRig rig;

        private CoreSimulation Sim => host.Simulation;

        /// <summary>One candidate look: every value is a display choice.</summary>
        private sealed class Look
        {
            public string Id;
            public bool Realistic;
            // The map
            public float PaintRadius;
            public float PaintLevel = 1f;
            public float Saturation = 1f;
            public float Contrast = 1f;
            // Light and shade
            public float LitCurve = 1f;
            public Color DayTint = Color.white;
            public Color ShadeTint = Color.white;
            public float NightLift;
            public Color NightTint = Color.white;
            public float Sheen;
            public float SheenPower = 8f;
            public float Glint;
            public float GlintPower = 120f;
            public float RimStrength;
            public float RimPower = 3f;
            public float RimFromMap;
            public float NightRim;
            public float NightRimPower = 3f;
            public Color NightRimColor = new Color(0.4f, 0.6f, 1f);
            public float BodyMax = 100f;
            // Halo
            public float HaloIntensity;
            public float HaloThickness;
            public float HaloFalloff = 2f;
            public float HaloDiscReach = 12f;
            public float Twilight;
            // Rings
            public Color RingThin = new Color(0.75f, 0.72f, 0.66f);
            public Color RingDense = new Color(0.75f, 0.72f, 0.66f);
            public float RingGain = 1f;
            public float RingBrightness = 1f;
            // Sky and camera
            public Color Background = Color.black;
            public float StarExposure = 1f;
            public float StarSize = 1f;
            public float Sparkle;
            public float Bloom;
            public float BloomThreshold = 1.33f; // gamma; about 1.9 linear, above _BodyMax and its soft knee
            public float BloomScatter = 0.7f;
            public float PostSaturation;
            public float PostContrast;
            public float Vignette;
        }

        // Three steps from the realistic look toward a polished game look (bright clay-like surfaces, strong
        // rims, glossy highlights, thick luminous atmospheres, sparkling stars and bloom). The project owner
        // chose H on 2026-10-27; docs/ART_DIRECTION.md records its values.
        private static readonly Look[] Looks =
        {
            new Look { Id = "0-realistic", Realistic = true },
            // G: a third of the way. Cleaned-up maps, a brighter day side, a light sheen and ocean glint.
            new Look
            {
                Id = "g-console", PaintRadius = 1f, PaintLevel = 0.5f, Saturation = 1.35f, Contrast = 1.0f,
                LitCurve = 1.6f, DayTint = new Color(1.06f, 1.02f, 0.96f), ShadeTint = new Color(0.85f, 0.88f, 1.0f),
                NightLift = 0.08f, NightTint = new Color(0.45f, 0.55f, 1.0f), Sheen = 0.12f, SheenPower = 10f,
                Glint = 1.4f, GlintPower = 600f, RimStrength = 0.35f, RimPower = 3f, RimFromMap = 0.6f,
                NightRim = 0.15f, NightRimPower = 4f, BodyMax = 0.95f, HaloIntensity = 1.6f, HaloThickness = 0.08f,
                HaloFalloff = 2f,
                HaloDiscReach = 20f, Twilight = 0.45f, RingThin = new Color(0.6f, 0.55f, 0.5f),
                RingDense = new Color(1.0f, 0.9f, 0.75f), RingGain = 1.3f, RingBrightness = 1.1f,
                Background = new Color(0.01f, 0.012f, 0.03f), StarExposure = 1.5f, StarSize = 1.2f, Sparkle = 0.4f,
                Bloom = 0.6f, PostSaturation = 10f, PostContrast = 5f, Vignette = 0.25f,
            },
            // H (chosen, "Adventure"): halfway. Smoother clay-like maps with lifted darks, a vivid palette, a
            // satin sheen and ocean glint, bright map-colored rims, a cool night rim, thick atmospheres.
            new Look
            {
                Id = "h-adventure", PaintRadius = 1.5f, PaintLevel = 1f, Saturation = 1.6f, Contrast = 0.9f,
                LitCurve = 2.2f, DayTint = new Color(1.1f, 1.04f, 0.95f), ShadeTint = new Color(0.75f, 0.8f, 1.05f),
                NightLift = 0.12f, NightTint = new Color(0.4f, 0.45f, 1.0f), Sheen = 0.2f, SheenPower = 8f,
                Glint = 1.8f, GlintPower = 450f, RimStrength = 0.55f, RimPower = 2.5f, RimFromMap = 0.7f,
                NightRim = 0.3f, NightRimPower = 3.5f, NightRimColor = new Color(0.35f, 0.65f, 1.0f), BodyMax = 0.95f,
                HaloIntensity = 2.1f, HaloThickness = 0.12f, HaloFalloff = 2.2f, HaloDiscReach = 16f, Twilight = 0.6f,
                RingThin = new Color(0.7f, 0.58f, 0.45f), RingDense = new Color(1.0f, 0.9f, 0.68f), RingGain = 1.5f,
                RingBrightness = 1.2f, Background = new Color(0.02f, 0.02f, 0.06f), StarExposure = 1.9f,
                StarSize = 1.5f, Sparkle = 0.8f, Bloom = 0.9f, PostSaturation = 15f, PostContrast = 8f,
                Vignette = 0.3f,
            },
            // I: most of the way. Soft toy-like surfaces, saturated color, a broad gloss, strong rims on both
            // sides, the thickest atmospheres, big sparkling stars and the most bloom.
            new Look
            {
                Id = "i-toybox", PaintRadius = 2.5f, PaintLevel = 1.5f, Saturation = 1.85f, Contrast = 0.82f,
                LitCurve = 3f, DayTint = new Color(1.14f, 1.06f, 0.95f), ShadeTint = new Color(0.7f, 0.68f, 1.1f),
                NightLift = 0.17f, NightTint = new Color(0.45f, 0.4f, 1.0f), Sheen = 0.3f, SheenPower = 6f,
                Glint = 2.2f, GlintPower = 300f, RimStrength = 0.75f, RimPower = 2.2f, RimFromMap = 0.8f,
                NightRim = 0.45f, NightRimPower = 3f, NightRimColor = new Color(0.45f, 0.6f, 1.0f), BodyMax = 0.95f,
                HaloIntensity = 2.5f, HaloThickness = 0.17f, HaloFalloff = 2f, HaloDiscReach = 12f, Twilight = 0.75f,
                RingThin = new Color(0.85f, 0.6f, 0.42f), RingDense = new Color(1.0f, 0.93f, 0.7f), RingGain = 1.8f,
                RingBrightness = 1.3f, Background = new Color(0.035f, 0.025f, 0.09f), StarExposure = 2.3f,
                StarSize = 1.8f, Sparkle = 1.3f, Bloom = 1.2f, BloomScatter = 0.75f, PostSaturation = 20f,
                PostContrast = 10f, Vignette = 0.35f,
            },
        };

        // Halos for the pictures only (A-07 sources which bodies have an atmosphere): the outer color, the color
        // at the limb, the twilight color (the Earth's sunsets are red, Mars's blue; none on the giants), and
        // intensity and thickness relative to the look's.
        private static readonly (long Naif, Color Outer, Color Core, Color Twilight, float Intensity, float Thickness)[]
            Halos =
        {
            (399, new Color(0.2f, 0.45f, 1.0f), new Color(0.55f, 0.8f, 1.0f), new Color(1.0f, 0.5f, 0.25f), 1.0f, 1.0f),
            (499, new Color(0.85f, 0.8f, 0.8f), new Color(0.95f, 0.9f, 0.85f), new Color(0.45f, 0.6f, 1.0f), 0.35f,
             0.5f),
            (599, new Color(1.0f, 0.85f, 0.65f), new Color(1.0f, 0.95f, 0.85f), Color.clear, 0.55f, 0.5f),
            (699, new Color(1.0f, 0.88f, 0.62f), new Color(1.0f, 0.95f, 0.85f), Color.clear, 0.45f, 0.5f),
        };

        // The body with oceans, for the sun glint (pictures only).
        private const long OceanBody = 399;

        private StylePreference preference;

        [OneTimeSetUp]
        public void KeepPreference() => preference = StylePreference.Keep();

        [OneTimeTearDown]
        public void RestorePreference() => preference.Restore();

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            // The scene in the realistic style (A-02) is the "0-realistic" reference; each candidate look
            // brings its own shaders.
            VisualStyleSettings.SavePreference(VisualStyle.Realistic);
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

        [UnityTest]
        public IEnumerator RenderTheCandidateLooks()
        {
            var folder = Environment.GetEnvironmentVariable("USIM_MOCKUP_DIR");
            if (string.IsNullOrEmpty(folder))
            {
                Assert.Ignore("set USIM_MOCKUP_DIR to the folder for the mockup images");
            }
            Directory.CreateDirectory(folder);
            Object.FindAnyObjectByType<OrbitLines>().Visible = false;

            var bodyShader = Shader.Find("UniverseSim/Mockups/Body");
            var haloShader = Shader.Find("UniverseSim/Mockups/Halo");
            var ringShader = Shader.Find("UniverseSim/Mockups/Rings");
            var starShader = Shader.Find("UniverseSim/Mockups/StarField");
            Assert.That(bodyShader != null && haloShader != null && ringShader != null && starShader != null,
                        "the mockup shaders");

            var bodies = host.Views.Where(v => v.GetComponent<MeshRenderer>() != null)
                .Select(v => (Material: v.GetComponent<MeshRenderer>().sharedMaterial,
                              Shader: v.GetComponent<MeshRenderer>().sharedMaterial.shader,
                              HasOceans: v.Info.Id.Value == OceanBody))
                .ToList();
            var ringRenderer = Object.FindAnyObjectByType<RingSystems>().transform.Cast<Transform>()
                .First(t => t.name.StartsWith("Rings of")).GetComponent<MeshRenderer>();
            var ringOriginal = ringRenderer.sharedMaterial;
            var ringMockup = new Material(ringShader);
            var starRenderer = Object.FindAnyObjectByType<StarField>().GetComponent<MeshRenderer>();
            var starOriginal = starRenderer.sharedMaterial;
            var starMockup = new Material(starShader);
            var halos = new List<(MockupHalo Halo, float Intensity, float Thickness, bool Twilight)>();
            foreach (var (naif, outer, core, twilight, intensity, thickness) in Halos)
            {
                var index = Index(naif);
                var halo = new GameObject("Mockup halo of " + host.Views[index].Info.Name).AddComponent<MockupHalo>();
                var material = new Material(haloShader);
                material.SetColor("_HaloColor", outer);
                material.SetColor("_CoreColor", core);
                material.SetColor("_TwilightColor", twilight);
                halo.Initialize(host.Views[index], material);
                halos.Add((halo, intensity, thickness, twilight.a > 0f));
            }
            // A global volume above the scene's, so each candidate sets its own post-processing; the realistic
            // look keeps the scene's volume as it is.
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.highQualityFiltering.Override(true);
            var grading = profile.Add<ColorAdjustments>(true);
            var vignette = profile.Add<Vignette>(true);
            vignette.smoothness.Override(0.5f);
            var volume = new GameObject("Mockup volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.sharedProfile = profile;

            var cam = Camera.main;
            foreach (var look in Looks)
            {
                foreach (var (material, original, hasOceans) in bodies)
                {
                    if (!look.Realistic && original.name == "UniverseSim/BodyLit")
                    {
                        material.shader = bodyShader;
                        ApplyBody(material, look, hasOceans);
                    }
                    else
                    {
                        material.shader = original;
                    }
                }
                ringRenderer.sharedMaterial = look.Realistic ? ringOriginal : ringMockup;
                ApplyRings(ringMockup, look);
                foreach (var (halo, intensity, thickness, twilight) in halos)
                {
                    halo.Thickness = look.Realistic ? 0f : look.HaloThickness * thickness;
                    halo.Material.SetFloat("_Intensity", look.HaloIntensity * intensity);
                    halo.Material.SetFloat("_Falloff", look.HaloFalloff);
                    halo.Material.SetFloat("_DiscReach", look.HaloDiscReach);
                    halo.Material.SetFloat("_TwilightStrength", twilight ? look.Twilight : 0f);
                }
                starRenderer.sharedMaterial = look.Realistic ? starOriginal : starMockup;
                starMockup.SetFloat("_Exposure", look.StarExposure);
                starMockup.SetFloat("_SizeScale", look.StarSize);
                starMockup.SetFloat("_Sparkle", look.Sparkle);
                cam.backgroundColor = look.Background;
                volume.weight = look.Realistic ? 0f : 1f;
                bloom.intensity.Override(look.Bloom);
                bloom.threshold.Override(look.BloomThreshold);
                bloom.scatter.Override(look.BloomScatter);
                grading.saturation.Override(look.PostSaturation);
                grading.contrast.Override(look.PostContrast);
                vignette.intensity.Override(look.Vignette);

                yield return Place(Index(399), 3.4, PhaseFromSun(Index(399), 55.0, 0.22f));
                yield return Capture(Path.Combine(folder, $"earth-{look.Id}.png"));
                yield return Place(Index(301), 3.2, PhaseToward(Index(301), 80.0, Index(399)));
                yield return Capture(Path.Combine(folder, $"moon-{look.Id}.png"));
                yield return Place(Index(499), 3.2, PhaseFromSun(Index(499), 45.0, 0.15f));
                yield return Capture(Path.Combine(folder, $"mars-{look.Id}.png"));
                yield return Place(Index(599), 3.2, PhaseFromSun(Index(599), 40.0, 0.1f));
                yield return Capture(Path.Combine(folder, $"jupiter-{look.Id}.png"));
                var saturn = Index(699);
                rig.JumpTo(saturn, Sim.Bodies[saturn].MaxRadiusKm * 6.0, 0.9, 0.45);
                rig.Select(saturn);
                yield return Frames(3);
                yield return Capture(Path.Combine(folder, $"saturn-rings-{look.Id}.png"));
            }

            foreach (var (material, original, _) in bodies)
            {
                material.shader = original;
            }
            ringRenderer.sharedMaterial = ringOriginal;
            starRenderer.sharedMaterial = starOriginal;
            Object.Destroy(ringMockup);
            Object.Destroy(starMockup);
            Object.Destroy(volume.gameObject);
            Object.Destroy(profile);
            foreach (var (halo, _, _, _) in halos)
            {
                Object.Destroy(halo.Material);
                Object.Destroy(halo.gameObject);
            }
        }

        private static void ApplyBody(Material m, Look look, bool hasOceans)
        {
            m.SetFloat("_PaintRadius", look.PaintRadius);
            m.SetFloat("_PaintLevel", look.PaintLevel);
            m.SetFloat("_Saturation", look.Saturation);
            m.SetFloat("_Contrast", look.Contrast);
            m.SetFloat("_LitCurve", look.LitCurve);
            m.SetColor("_DayTint", look.DayTint);
            m.SetColor("_ShadeTint", look.ShadeTint);
            m.SetFloat("_NightLift", look.NightLift);
            m.SetColor("_NightTint", look.NightTint);
            m.SetFloat("_SheenStrength", look.Sheen);
            m.SetFloat("_SheenPower", look.SheenPower);
            m.SetFloat("_GlintStrength", hasOceans ? look.Glint : 0f);
            m.SetFloat("_GlintPower", look.GlintPower);
            m.SetFloat("_RimStrength", look.RimStrength);
            m.SetFloat("_RimPower", look.RimPower);
            m.SetFloat("_RimFromMap", look.RimFromMap);
            m.SetFloat("_NightRimStrength", look.NightRim);
            m.SetFloat("_NightRimPower", look.NightRimPower);
            m.SetColor("_NightRimColor", look.NightRimColor);
            m.SetFloat("_BodyMax", look.BodyMax);
        }

        private static void ApplyRings(Material m, Look look)
        {
            m.SetColor("_ThinColor", look.RingThin);
            m.SetColor("_DenseColor", look.RingDense);
            m.SetFloat("_DensityGain", look.RingGain);
            m.SetFloat("_Brightness", look.RingBrightness);
        }

        // The direction from a body toward the camera (render space) that shows it at a phase angle of
        // `phaseDeg`: the light direction turned about the ecliptic pole, then raised by `lift`.
        private Vector3 PhaseFromSun(int index, double phaseDeg, float lift)
        {
            var light = ((Vector3)host.Views[index].LightDirection).normalized;
            var side = Vector3.Cross(Vector3.up, light).normalized;
            var phase = (float)(phaseDeg * Math.PI / 180.0);
            return (Mathf.Cos(phase) * light + Mathf.Sin(phase) * side + lift * Vector3.up).normalized;
        }

        // As PhaseFromSun, turned toward another body (the Moon's near side faces the Earth).
        private Vector3 PhaseToward(int index, double phaseDeg, int toward)
        {
            var light = ((Vector3)host.Views[index].LightDirection).normalized;
            var to = (host.Views[toward].transform.position - host.Views[index].transform.position).normalized;
            var side = (to - Vector3.Dot(to, light) * light).normalized;
            var phase = (float)(phaseDeg * Math.PI / 180.0);
            return (Mathf.Cos(phase) * light + Mathf.Sin(phase) * side).normalized;
        }

        // The camera at `radii` body radii from the center, in `direction` (CameraRig: yaw = atan2(x, z),
        // pitch = asin(y)).
        private IEnumerator Place(int index, double radii, Vector3 direction)
        {
            rig.JumpTo(index, Sim.Bodies[index].MaxRadiusKm * radii, Math.Atan2(direction.x, direction.z),
                       Math.Asin(Mathf.Clamp(direction.y, -1f, 1f)));
            rig.Select(index);
            yield return Frames(3);
        }

        private static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; ++i)
            {
                yield return null;
            }
        }

        // Renders the camera (without the HUD) into a texture for one frame and saves it.
        private static IEnumerator Capture(string path)
        {
            var cam = Camera.main;
            var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = target;
            yield return null;
            yield return null;
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            cam.targetTexture = null;
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.Destroy(image);
            target.Release();
            Object.Destroy(target);
        }
    }
}
