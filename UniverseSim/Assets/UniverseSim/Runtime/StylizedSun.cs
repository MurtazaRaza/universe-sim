using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The Sun in the stylized style (A-08, docs/ART_DIRECTION.md section 7): a warm disk above the bloom threshold,
    /// so it glows, with a corona and a glare of rays and a halo around it (SunGlow.shader). The disk keeps its
    /// true size in every scale mode: its mesh and transform are the snapshot's, the glow is depth-tested behind
    /// the disk's front surface, and nothing is drawn over the disk. The glare fades with the part of the disk that
    /// bodies cover, so a covered Sun keeps only its corona. Star.shader reads the disk's color from a global set
    /// here, zero (neutral) while this component is disabled or the style is realistic; the glow renderers are
    /// enabled only while the effect is on.
    /// </summary>
    [DefaultExecutionOrder(25)]
    public sealed class StylizedSun : StyleEffect
    {
        [SerializeField] private SimulationHost host;
        [SerializeField] private Material glowMaterial;

        // Display choices from docs/ART_DIRECTION.md (colors as Unity colors).
        [Header("Disk")]
        [SerializeField] private Color diskColor = new Color(1.00f, 0.93f, 0.80f);
        [Tooltip("Multiplies the disk's color: above the bloom threshold (about 1.9 linear), so the Sun blooms.")]
        [SerializeField, Range(1f, 8f)] private float diskIntensity = 4f;

        [Header("Corona")]
        [SerializeField, Range(0f, 4f)] private float coronaStrength = 1.6f;
        [Tooltip("How far the corona reaches from the Sun's center, solar radii.")]
        [SerializeField, Range(1.1f, 12f)] private float coronaReach = 4f;
        [SerializeField, Range(0.5f, 6f)] private float coronaFalloff = 2.2f;
        [SerializeField] private Color coronaColor = new Color(1.00f, 0.78f, 0.48f);

        [Header("Glare")]
        [Tooltip("The halo and rays fall off with the distance from the limb over lengths that grow with the disk's " +
                 "radius on screen (times the scale below), between a shortest and a longest length in pixels: a " +
                 "distant Sun sparkles like a bright star, a near one glares.")]
        [SerializeField, Range(0f, 4f)] private float haloStrength = 0.9f;
        [SerializeField, Range(0f, 4f)] private float haloScale = 0.5f;
        [SerializeField, Range(1f, 100f)] private float haloMinPixels = 4f;
        [SerializeField, Range(1f, 100f)] private float haloMaxPixels = 16f;
        [SerializeField, Range(0f, 4f)] private float rayStrength = 0.8f;
        [SerializeField, Range(0f, 10f)] private float rayScale = 3f;
        [SerializeField, Range(1f, 400f)] private float rayMinPixels = 12f;
        [SerializeField, Range(1f, 400f)] private float rayMaxPixels = 90f;
        [SerializeField, Range(2, 12)] private int rayCount = 6;
        [SerializeField, Range(1f, 200f)] private float raySharpness = 48f;
        [SerializeField] private Color glareColor = new Color(1.00f, 0.90f, 0.72f);

        // Points on the disk tested for cover: the center and a ring at three quarters of the radius.
        private const int RingSamples = 8;

        private static readonly int SunDiskId = Shader.PropertyToID("_UsimSunDisk");
        private static readonly int SunRadiusId = Shader.PropertyToID("_SunRadius");
        private static readonly int CoronaId = Shader.PropertyToID("_Corona");
        private static readonly int CoronaColorId = Shader.PropertyToID("_CoronaColor");
        private static readonly int HaloId = Shader.PropertyToID("_Halo");
        private static readonly int RaysId = Shader.PropertyToID("_Rays");
        private static readonly int RayLengthId = Shader.PropertyToID("_RayLength");
        private static readonly int GlareColorId = Shader.PropertyToID("_GlareColor");
        private static readonly int GlareId = Shader.PropertyToID("_Glare");

        private sealed class Glow
        {
            public BodyView Star;
            public Transform Transform;
            public MeshRenderer Renderer;
            public Material Material;
        }

        private readonly List<Glow> glows = new List<Glow>();
        private Mesh quad;

        /// <summary>The part of the first star's disk no body covers in the latest frame (1 without a star).</summary>
        public float Visibility { get; private set; } = 1f;

        /// <summary>The glow's renderer for <paramref name="star"/>, or null (for tests).</summary>
        public MeshRenderer GlowOf(BodyView star)
        {
            foreach (var glow in glows)
            {
                if (glow.Star == star)
                {
                    return glow.Renderer;
                }
            }
            return null;
        }

        protected override Exaggeration[] Describe() => new[]
        {
            new Exaggeration("The Sun", "brighter and warmer, so it glows; its disk keeps its true size",
                             $"x{Format(diskIntensity)} brightness"),
            new Exaggeration("Corona",
                             "a glow around the Sun, much brighter than the real corona, which the eye sees only in a " +
                             "total eclipse",
                             $"out to {Format(coronaReach)} solar radii"),
            new Exaggeration("Glare", "rays and a halo around the Sun, as a camera lens shows them; gone when a body " +
                                      "covers the Sun",
                             $"rays {Format(rayMinPixels)} to {Format(rayMaxPixels)} px"),
        };

        private static string Format(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

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
            foreach (var glow in glows)
            {
                Destroy(glow.Material);
            }
            if (quad != null)
            {
                Destroy(quad);
            }
        }

        private void OnLoaded()
        {
            quad = BuildQuad();
            foreach (var view in host.Views)
            {
                if (view.Info.Kind != BodyKindStar || !view.Info.HasMeanRadius)
                {
                    continue;
                }
                var go = new GameObject("Glow of " + view.Info.Name);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var glow = new Glow
                {
                    Star = view,
                    Transform = go.transform,
                    Renderer = go.AddComponent<MeshRenderer>(),
                    Material = new Material(glowMaterial) { name = "Sun glow (" + view.Info.Name + ")" },
                };
                glow.Renderer.sharedMaterial = glow.Material;
                glow.Renderer.shadowCastingMode = ShadowCastingMode.Off;
                glow.Renderer.receiveShadows = false;
                glow.Renderer.enabled = false;
                glows.Add(glow);
            }
            Refresh();
        }

        private const int BodyKindStar = 1; // usim::catalog::BodyKind

        // Four vertices at the origin; the shader spreads them over the glow's extent on screen. The bounds are
        // large, so the glow is never culled while its center is off screen.
        private static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "Sun glow quad" };
            mesh.SetVertices(new[] { Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero });
            mesh.SetUVs(0, new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) });
            mesh.SetIndices(new[] { 0, 2, 1, 0, 3, 2 }, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2.0e7f);
            return mesh;
        }

        protected override void Apply(bool on)
        {
            var linear = QualitySettings.activeColorSpace == ColorSpace.Linear ? diskColor.linear : diskColor;
            Shader.SetGlobalVector(SunDiskId,
                                   on ? new Vector4(linear.r * diskIntensity, linear.g * diskIntensity,
                                                    linear.b * diskIntensity, 1f)
                                      : Vector4.zero);
            foreach (var glow in glows)
            {
                var m = glow.Material;
                m.SetVector(CoronaId, new Vector4(coronaStrength, coronaReach, coronaFalloff, 0f));
                m.SetColor(CoronaColorId, coronaColor);
                m.SetVector(HaloId, new Vector4(haloStrength, haloScale, haloMinPixels, haloMaxPixels));
                m.SetVector(RaysId, new Vector4(rayStrength, rayCount, raySharpness, 0f));
                m.SetVector(RayLengthId, new Vector4(rayScale, rayMinPixels, rayMaxPixels, 0f));
                m.SetColor(GlareColorId, glareColor);
                if (!on)
                {
                    glow.Renderer.enabled = false;
                }
            }
        }

        private void LateUpdate()
        {
            var on = Applied;
            var cam = Camera.main;
            var first = true;
            foreach (var glow in glows)
            {
                var shown = on && cam != null && glow.Star.IsDrawn;
                glow.Renderer.enabled = shown;
                if (!shown)
                {
                    continue;
                }
                var visibility = DiskVisibility(cam.transform.position, glow.Star);
                glow.Transform.SetPositionAndRotation(glow.Star.transform.position, Quaternion.identity);
                glow.Material.SetFloat(SunRadiusId, glow.Star.MaxRenderRadius);
                glow.Material.SetVector(GlareId, new Vector4(visibility, 0f, 0f, 0f));
                if (first)
                {
                    Visibility = visibility;
                    first = false;
                }
            }
        }

        // The part of the star's disk that no drawn body covers, seen from `eye`: the center and a ring of points
        // at three quarters of the radius, each tested against the bodies' bounding spheres. Display geometry only.
        private float DiskVisibility(Vector3 eye, BodyView star)
        {
            var center = star.transform.position;
            var toStar = center - eye;
            var distance = toStar.magnitude;
            if (distance <= 0f)
            {
                return 1f;
            }
            var forward = toStar / distance;
            var right = Vector3.Cross(forward, Mathf.Abs(forward.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var up = Vector3.Cross(right, forward);
            var radius = 0.75f * star.MaxRenderRadius;
            var open = Covered(eye, center, star) ? 0 : 1;
            for (var k = 0; k < RingSamples; ++k)
            {
                var a = 2f * Mathf.PI * k / RingSamples;
                var point = center + radius * (Mathf.Cos(a) * right + Mathf.Sin(a) * up);
                if (!Covered(eye, point, star))
                {
                    ++open;
                }
            }
            return open / (float)(RingSamples + 1);
        }

        // True when a drawn body other than the star lies on the line of sight from `eye` to `point`.
        private bool Covered(Vector3 eye, Vector3 point, BodyView star)
        {
            var ray = point - eye;
            var length = ray.magnitude;
            var direction = ray / length;
            foreach (var view in host.Views)
            {
                if (view == star || !view.IsDrawn)
                {
                    continue;
                }
                var toCenter = view.transform.position - eye;
                var along = Vector3.Dot(toCenter, direction);
                if (along <= 0f || along >= length)
                {
                    continue;
                }
                var radius = view.MaxRenderRadius;
                if ((toCenter - along * direction).sqrMagnitude < radius * radius)
                {
                    return true;
                }
            }
            return false;
        }

        public override bool ActiveInView(Camera cam)
        {
            if (!Applied || cam == null)
            {
                return false;
            }
            var focalPixels = 0.5f * cam.pixelHeight / Mathf.Tan(0.5f * cam.fieldOfView * Mathf.Deg2Rad);
            foreach (var glow in glows)
            {
                if (!glow.Renderer.enabled)
                {
                    continue;
                }
                var center = glow.Star.transform.position;
                var p = cam.WorldToScreenPoint(center);
                if (p.z <= 0f)
                {
                    continue;
                }
                // The glow's extent on screen, as SunGlow.shader spreads it.
                var radius = SphereLods.PixelRadius(glow.Star.MaxRenderRadius, Vector3.Distance(center, cam.transform.position),
                                                    focalPixels);
                var halo = Mathf.Clamp(haloScale * radius, haloMinPixels, haloMaxPixels);
                var rays = Mathf.Clamp(rayScale * radius, rayMinPixels, rayMaxPixels);
                var extent = 1.25f * Mathf.Max(radius * coronaReach, radius + 5f * Mathf.Max(halo, rays));
                if (p.x > -extent && p.x < cam.pixelWidth + extent && p.y > -extent && p.y < cam.pixelHeight + extent)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
