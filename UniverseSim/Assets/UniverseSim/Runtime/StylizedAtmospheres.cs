using System;
using System.Collections.Generic;
using System.Globalization;
using UniverseSim.Unity.Native;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniverseSim.Unity
{
    /// <summary>
    /// Atmosphere halos of the stylized style (A-07, ADR-0018, docs/ART_DIRECTION.md section 5): a glow at the limb
    /// of every body the core reports with an atmosphere (<see cref="CoreSimulation.Atmosphere"/>, from the pack's
    /// surface conditions), brightest on the sunlit side and following the core's light direction, tinted toward
    /// the body's twilight hue where the limb meets the terminator when a source gives that hue
    /// (<see cref="SkyCues"/>). Color, thickness and intensity are display choices per body. The halo is a shell
    /// around the body (AtmosphereHalo.shader); the body keeps its size. Its renderers are enabled only while the
    /// effect is on.
    /// </summary>
    [DefaultExecutionOrder(25)]
    public sealed class StylizedAtmospheres : StyleEffect
    {
        /// <summary>The display choices for one body's halo (docs/ART_DIRECTION.md section 5).</summary>
        [Serializable]
        public struct HaloStyle
        {
            public long naifId;
            [Tooltip("Color at the top of the shell.")] public Color outer;
            [Tooltip("Color at the limb.")] public Color core;
            [Tooltip("Shell thickness relative to the look's (the Earth's is 1).")] public float thickness;
            [Tooltip("Intensity relative to the look's (the Earth's is 1).")] public float intensity;

            public HaloStyle(long naifId, Color outer, Color core, float thickness, float intensity)
            {
                this.naifId = naifId;
                this.outer = outer;
                this.core = core;
                this.thickness = thickness;
                this.intensity = intensity;
            }
        }

        [SerializeField] private SimulationHost host;
        [SerializeField] private Material haloMaterial;

        // Display choices from docs/ART_DIRECTION.md section 5 (the A-01 "Adventure" look).
        [Header("Look")]
        [SerializeField, Range(0f, 4f)] private float intensity = 2.1f;
        [Tooltip("The Earth's shell thickness, body radii; other bodies scale it by their style's thickness.")]
        [SerializeField, Range(0f, 0.5f)] private float thickness = 0.12f;
        [SerializeField, Range(0.5f, 6f)] private float falloff = 2.2f;
        [SerializeField, Range(0f, 64f)] private float discReach = 16f;
        [Tooltip("How far the color moves toward the body's sourced twilight hue where the limb meets the terminator.")]
        [SerializeField, Range(0f, 1f)] private float twilight = 0.6f;

        [Header("Bodies")]
        [SerializeField] private HaloStyle[] styles =
        {
            new HaloStyle(299, new Color(1.00f, 0.88f, 0.62f), new Color(1.00f, 0.95f, 0.82f), 0.6f, 0.6f),  // Venus
            new HaloStyle(399, new Color(0.20f, 0.45f, 1.00f), new Color(0.55f, 0.80f, 1.00f), 1.0f, 1.0f),  // Earth
            new HaloStyle(499, new Color(0.85f, 0.80f, 0.80f), new Color(0.95f, 0.90f, 0.85f), 0.5f, 0.35f), // Mars
            new HaloStyle(599, new Color(1.00f, 0.85f, 0.65f), new Color(1.00f, 0.95f, 0.85f), 0.5f, 0.55f), // Jupiter
            new HaloStyle(699, new Color(1.00f, 0.88f, 0.62f), new Color(1.00f, 0.95f, 0.85f), 0.5f, 0.45f), // Saturn
            new HaloStyle(799, new Color(0.55f, 0.85f, 0.95f), new Color(0.80f, 0.95f, 1.00f), 0.5f, 0.45f), // Uranus
            new HaloStyle(899, new Color(0.30f, 0.50f, 1.00f), new Color(0.60f, 0.75f, 1.00f), 0.5f, 0.5f),  // Neptune
            new HaloStyle(999, new Color(0.35f, 0.55f, 1.00f), new Color(0.60f, 0.75f, 1.00f), 0.35f, 0.3f), // Pluto
        };
        [Tooltip("For a body with an atmosphere and no style above.")]
        [SerializeField] private HaloStyle fallback =
            new HaloStyle(0, new Color(0.80f, 0.85f, 0.95f), new Color(0.92f, 0.94f, 1.00f), 0.4f, 0.4f);

        [Tooltip("A halo counts as shown in the view (the exaggeration panel) when its body spans this many pixels.")]
        [SerializeField] private float minPixels = 2f;

        private static readonly int HaloColorId = Shader.PropertyToID("_HaloColor");
        private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
        private static readonly int TwilightColorId = Shader.PropertyToID("_TwilightColor");
        private static readonly int TwilightStrengthId = Shader.PropertyToID("_TwilightStrength");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
        private static readonly int FalloffId = Shader.PropertyToID("_Falloff");
        private static readonly int DiscReachId = Shader.PropertyToID("_DiscReach");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static readonly int LightDirectionId = Shader.PropertyToID("_LightDirection");
        private static readonly int CullId = Shader.PropertyToID("_Cull");
        private static readonly int BodyWorldToLocalId = Shader.PropertyToID("_BodyWorldToLocal");

        private sealed class Halo
        {
            public BodyView View;
            public HaloStyle Style;
            public bool HasTwilight;
            public Transform Transform;
            public MeshRenderer Renderer;
            public Material Material;
        }

        private readonly List<Halo> halos = new List<Halo>();
        private Mesh shell;

        /// <summary>The bodies that have a halo: those the core reports with an atmosphere.</summary>
        public IEnumerable<BodyView> HaloBodies
        {
            get
            {
                foreach (var halo in halos)
                {
                    yield return halo.View;
                }
            }
        }

        /// <summary>The halo's renderer and material for <paramref name="view"/> (for tests).</summary>
        public bool TryGetHalo(BodyView view, out MeshRenderer renderer, out Material material)
        {
            foreach (var halo in halos)
            {
                if (halo.View == view)
                {
                    renderer = halo.Renderer;
                    material = halo.Material;
                    return true;
                }
            }
            renderer = null;
            material = null;
            return false;
        }

        /// <summary>The shell's thickness around <paramref name="view"/>, body radii (0 without a halo).</summary>
        public float ShellThickness(BodyView view)
        {
            foreach (var halo in halos)
            {
                if (halo.View == view)
                {
                    return thickness * halo.Style.thickness;
                }
            }
            return 0f;
        }

        public override bool ActiveInView(Camera cam)
        {
            // The style first: listeners of the style's change may ask before LateUpdate has updated the renderers.
            if (!Applied || cam == null)
            {
                return false;
            }
            foreach (var halo in halos)
            {
                if (halo.View.IsDrawn && halo.View.IsShown(cam, minPixels))
                {
                    return true;
                }
            }
            return false;
        }

        protected override Exaggeration[] Describe()
        {
            var thickest = 0f;
            foreach (var style in styles)
            {
                thickest = Mathf.Max(thickest, style.thickness);
            }
            return new[]
            {
                new Exaggeration("Atmosphere halos",
                                 "a glow at the limb of each body whose surface conditions show an atmosphere, much " +
                                 "thicker and brighter than a real one; its color is a display choice",
                                 $"up to {Format(100f * thickness * thickest)}% of the radius"),
                new Exaggeration("Twilight colors",
                                 "where the limb meets the terminator, the halo takes the twilight hue a source gives " +
                                 "for the body (see its info panel)",
                                 $"{Format(100f * twilight)}% toward it"),
            };
        }

        private static string Format(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

        private void Awake()
        {
            host.Loaded += OnLoaded;
        }

        private void OnDestroy()
        {
            if (host != null)
            {
                host.Loaded -= OnLoaded;
            }
            foreach (var halo in halos)
            {
                Destroy(halo.Material);
            }
            if (shell != null)
            {
                Destroy(shell);
            }
        }

        private void OnLoaded()
        {
            var sim = host.Simulation;
            shell = SphereMesh.Create(64, 32);
            shell.name = "Atmosphere shell";
            foreach (var view in host.Views)
            {
                if (!view.Info.HasMeanRadius || sim.Atmosphere(view.Info.Index) != NativeConstants.AtmospherePresent)
                {
                    continue;
                }
                var id = view.Info.Id.Value;
                var go = new GameObject("Atmosphere of " + view.Info.Name);
                go.transform.SetParent(view.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = shell;
                var halo = new Halo
                {
                    View = view,
                    Style = StyleOf(id),
                    HasTwilight = host.SkyCues.TryGet(id, SkyCues.Twilight, out _),
                    Transform = go.transform,
                    Renderer = go.AddComponent<MeshRenderer>(),
                    Material = new Material(haloMaterial) { name = "Atmosphere halo (" + view.Info.Name + ")" },
                };
                halo.Material.SetColor(TwilightColorId, host.SkyCues.ColorOf(id, SkyCues.Twilight, Color.black));
                halo.Renderer.sharedMaterial = halo.Material;
                halo.Renderer.shadowCastingMode = ShadowCastingMode.Off;
                halo.Renderer.receiveShadows = false;
                halo.Renderer.enabled = false;
                halos.Add(halo);
            }
            Refresh();
        }

        private HaloStyle StyleOf(long naifId)
        {
            foreach (var style in styles)
            {
                if (style.naifId == naifId)
                {
                    return style;
                }
            }
            return fallback;
        }

        protected override void Apply(bool on)
        {
            foreach (var halo in halos)
            {
                var shellThickness = thickness * halo.Style.thickness;
                halo.Transform.localScale = Vector3.one * (1f + shellThickness);
                var m = halo.Material;
                m.SetColor(HaloColorId, halo.Style.outer);
                m.SetColor(CoreColorId, halo.Style.core);
                m.SetFloat(IntensityId, intensity * halo.Style.intensity);
                m.SetFloat(ThicknessId, shellThickness);
                m.SetFloat(FalloffId, falloff);
                m.SetFloat(DiscReachId, discReach);
                m.SetFloat(TwilightStrengthId, halo.HasTwilight ? twilight : 0f);
                if (!on)
                {
                    halo.Renderer.enabled = false;
                }
            }
        }

        private void LateUpdate()
        {
            var on = Applied;
            var cam = Camera.main;
            foreach (var halo in halos)
            {
                var shown = on && cam != null && halo.View.IsDrawn;
                halo.Renderer.enabled = shown;
                if (!shown)
                {
                    continue;
                }
                var body = halo.View.transform;
                var m = halo.Material;
                m.SetMatrix(BodyWorldToLocalId, body.worldToLocalMatrix);
                m.SetVector(LightDirectionId, halo.View.LightDirection);
                // Seen from inside the shell, its back faces carry the glow, and the halo hands over to the sky.
                var shellThickness = thickness * halo.Style.thickness;
                var height = body.InverseTransformPoint(cam.transform.position).magnitude - 1f;
                var inside = height < shellThickness;
                m.SetFloat(CullId, (float)(inside ? CullMode.Front : CullMode.Back));
                m.SetFloat(FadeId, inside ? SkyHandover(height, shellThickness) : 1f);
            }
        }

        /// <summary>
        /// How much of the halo shows at <paramref name="height"/> (body radii above the drawn surface) inside a shell
        /// of <paramref name="shellThickness"/>: none near the ground, all at the top. The sky takes the rest.
        /// </summary>
        public static float SkyHandover(float height, float shellThickness) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f * shellThickness, shellThickness, height));
    }
}
