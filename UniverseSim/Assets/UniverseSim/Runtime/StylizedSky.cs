using System;
using System.Collections.Generic;
using System.Globalization;
using UniverseSim.Unity.Native;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The sky at a surface site in the stylized style (B-06, ADR-0018, docs/ART_DIRECTION.md section 5). When the
    /// camera is within the halo of a body the core reports with an atmosphere (<see cref="StylizedAtmospheres"/>),
    /// the sky brightens by day in the body's sourced daytime hue (a neutral gray where no source gives one) and
    /// hides the stars, and near a low Sun it takes the body's sourced twilight hue around the Sun. Its strength
    /// grows from nothing at the top of the halo to full near the ground, times a display mapping of the surface
    /// pressure (the reference pressure for a body without solid ground) on a log scale, so a thin atmosphere gives
    /// a faint sky. The Sun's elevation comes from the core's light direction; the camera's height above the drawn
    /// body is display geometry. The panels keep the core's values. Sky.shader draws it.
    /// </summary>
    [DefaultExecutionOrder(26)]
    public sealed class StylizedSky : StyleEffect
    {
        [SerializeField] private SimulationHost host;
        [SerializeField] private StylizedAtmospheres atmospheres;
        [SerializeField] private Material skyMaterial;

        // Display choices from docs/ART_DIRECTION.md section 5.
        [Tooltip("The largest opacity of the sky over the stars.")]
        [SerializeField, Range(0f, 1f)] private float opacity = 0.97f;
        [Tooltip("The pressure at which the sky starts to show, Pa (a display mapping, not a physical threshold).")]
        [SerializeField] private double faintestPressurePa = 1.0;
        [Tooltip("Decades of pressure above the faintest one over which the sky reaches full strength.")]
        [SerializeField, Range(1f, 10f)] private float fullAfterDecades = 3f;

        private static readonly int DayColorId = Shader.PropertyToID("_SkyDayColor");
        private static readonly int TwilightColorId = Shader.PropertyToID("_SkyTwilightColor");
        private static readonly int UpId = Shader.PropertyToID("_SkyUp");
        private static readonly int SunId = Shader.PropertyToID("_SkySun");
        private static readonly int ParamsId = Shader.PropertyToID("_SkyParams");

        private sealed class Sky
        {
            public BodyView View;
            public float PressureStrength;
            public Color DayColor;
            public Color TwilightColor;
            public bool HasTwilight;
        }

        private readonly List<Sky> skies = new List<Sky>();
        private Transform skyTransform;
        private MeshRenderer skyRenderer;
        private Material material;
        private Mesh triangle;

        /// <summary>The body whose sky is drawn in the latest frame, or null.</summary>
        public BodyView SkyBody { get; private set; }

        /// <summary>The sky's strength in the latest frame: 0 (none) to 1.</summary>
        public float Strength { get; private set; }

        /// <summary>The sine of the Sun's elevation at the camera in the latest frame, from the light direction.</summary>
        public float SunElevationSine { get; private set; }

        /// <summary>How far the sky has come from night (0) to day (1) in the latest frame, as Sky.shader draws it.</summary>
        public float Daylight { get; private set; }

        /// <summary>The daytime hue of the latest frame's sky (sRGB, as the cues give it).</summary>
        public Color DayColor { get; private set; }

        /// <summary>The local vertical below the camera in the latest frame, render space.</summary>
        public Vector3 Up { get; private set; } = Vector3.up;

        /// <summary>The sky's renderer (for tests).</summary>
        public MeshRenderer Renderer => skyRenderer;

        // The style first: listeners of the style's change may ask before Apply has cleared the strength.
        public override bool ActiveInView(Camera cam) => Applied && skyRenderer != null && Strength > 0.01f;

        protected override Exaggeration[] Describe() => new[]
        {
            new Exaggeration("Sky at the surface",
                             "near a body with an atmosphere the sky brightens by day and hides the stars, in the " +
                             "hues its sources give (neutral gray where none does); it reaches up to the top of the " +
                             "halo, far higher than a real sky, and is fainter for thinner atmospheres",
                             $"full from {Format(faintestPressurePa * Math.Pow(10.0, fullAfterDecades))} Pa"),
        };

        private static string Format(double value) => value.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>
        /// The sky's strength for a surface or reference pressure, 0 to 1: the display mapping (log scale) that keeps
        /// the order of the pressures.
        /// </summary>
        public float PressureStrength(double pressurePa)
        {
            if (!(pressurePa > faintestPressurePa))
            {
                return 0f;
            }
            return Mathf.Clamp01((float)(Math.Log10(pressurePa / faintestPressurePa) / fullAfterDecades));
        }

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
            if (material != null)
            {
                Destroy(material);
            }
            if (triangle != null)
            {
                Destroy(triangle);
            }
        }

        private void OnLoaded()
        {
            var sim = host.Simulation;
            foreach (var view in host.Views)
            {
                var index = view.Info.Index;
                if (!view.Info.HasMeanRadius || sim.Atmosphere(index) != NativeConstants.AtmospherePresent)
                {
                    continue;
                }
                var surface = sim.Quantity(index, NativeConstants.FieldSurfacePressure);
                var level = sim.Quantity(index, NativeConstants.FieldReferencePressure);
                var pressure = surface.HasValue ? surface.Value : level.HasValue ? level.Value : 0.0;
                var id = view.Info.Id.Value;
                skies.Add(new Sky
                {
                    View = view,
                    PressureStrength = PressureStrength(pressure),
                    DayColor = host.SkyCues.ColorOf(id, SkyCues.DaytimeSky, SkyCues.NeutralSky),
                    TwilightColor = host.SkyCues.ColorOf(id, SkyCues.Twilight, Color.black),
                    HasTwilight = host.SkyCues.TryGet(id, SkyCues.Twilight, out _),
                });
            }

            // One triangle that covers the screen (the shader places it); large bounds, so it is never culled.
            triangle = new Mesh { name = "Sky triangle" };
            triangle.SetVertices(new[] { Vector3.zero, Vector3.zero, Vector3.zero });
            triangle.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0, false);
            triangle.bounds = new Bounds(Vector3.zero, Vector3.one * 2.0e7f);
            var go = new GameObject("Sky");
            go.transform.SetParent(transform, false);
            skyTransform = go.transform;
            go.AddComponent<MeshFilter>().sharedMesh = triangle;
            skyRenderer = go.AddComponent<MeshRenderer>();
            material = new Material(skyMaterial) { name = "Sky" };
            skyRenderer.sharedMaterial = material;
            skyRenderer.shadowCastingMode = ShadowCastingMode.Off;
            skyRenderer.receiveShadows = false;
            skyRenderer.enabled = false;
            Refresh();
        }

        protected override void Apply(bool on)
        {
            if (!on && skyRenderer != null)
            {
                skyRenderer.enabled = false;
                SkyBody = null;
                Strength = 0f;
            }
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (!Applied || cam == null || skyRenderer == null)
            {
                return;
            }
            // The body whose halo the camera is deepest in (relative to the halo's thickness).
            Sky best = null;
            var bestReach = 0f;
            var bestLocal = Vector3.zero;
            var eye = cam.transform.position;
            foreach (var sky in skies)
            {
                if (!sky.View.IsDrawn)
                {
                    continue;
                }
                var shell = atmospheres.ShellThickness(sky.View);
                var local = sky.View.transform.InverseTransformPoint(eye);
                var reach = 1f - StylizedAtmospheres.SkyHandover(local.magnitude - 1f, shell);
                if (shell > 0f && reach > bestReach)
                {
                    best = sky;
                    bestReach = reach;
                    bestLocal = local;
                }
            }
            SkyBody = best?.View;
            Strength = best != null ? bestReach * best.PressureStrength : 0f;
            skyRenderer.enabled = Strength > 0f;
            if (best == null)
            {
                return;
            }
            // The ellipsoid's normal below the camera: the local position through the inverse transpose of the scale.
            var body = best.View.transform;
            var scale = body.lossyScale;
            var normal = new Vector3(bestLocal.x / scale.x, bestLocal.y / scale.y, bestLocal.z / scale.z);
            var up = (body.rotation * normal).normalized;
            var sun = ((Vector3)best.View.LightDirection).normalized;
            SunElevationSine = Vector3.Dot(up, sun);
            Daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, 0.2f, SunElevationSine)); // Sky.shader's day
            DayColor = best.DayColor;
            Up = up;
            skyTransform.position = eye;
            material.SetColor(DayColorId, best.DayColor);
            material.SetColor(TwilightColorId, best.TwilightColor);
            material.SetVector(UpId, up);
            material.SetVector(SunId, sun);
            material.SetVector(ParamsId, new Vector4(Strength, best.HasTwilight ? 1f : 0f, opacity, 0f));
        }
    }
}
