using System;
using UniverseSim.Unity.Native;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The sky background (SPEC FR-REN-09): the pack's star catalog drawn as points around the camera, which
    /// stays at the origin. Directions come from the core in the display frame and Unity's axes, with proper
    /// motion for the clock's time; they are refreshed when the clock has moved by <see cref="refreshDays"/>.
    /// Point size, intensity and color are a display mapping of the V magnitude and the B-V color index
    /// (<see cref="DisplayColor"/>, <see cref="DisplayIntensity"/>): chosen for appearance, not photometry.
    /// </summary>
    [DefaultExecutionOrder(20)]
    public sealed class StarField : MonoBehaviour
    {
        [SerializeField] private SimulationHost host;
        [SerializeField] private Material starFieldMaterial;

        [Tooltip("Distance of the sky sphere in render units: inside the camera's far plane, beyond every body.")]
        [SerializeField] private float skyDistance = 2.0e5f;

        [Tooltip("Stars fainter than this V magnitude are not drawn.")]
        [SerializeField] private float faintestMagnitude = 6.5f;

        [Tooltip("Directions are computed again after the clock moves this many days (for proper motion).")]
        [SerializeField] private double refreshDays = 365.25;

        // Display mapping: the brightest star in the sky (V about -1.5) gets the largest point.
        private const float BrightestMagnitude = -1.5f;
        private const float SmallestHalfSizePixels = 0.9f;
        private const float LargestHalfSizePixels = 3.2f;

        private UsimStar[] stars = Array.Empty<UsimStar>();
        private Vector3[] vertices = Array.Empty<Vector3>();
        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private double filledAtSeconds = double.NaN;
        private int filledFrame = -1;

        /// <summary>Shows or hides the stars.</summary>
        public bool Visible { get; set; } = true;

        /// <summary>The number of stars drawn (those up to the faintest magnitude).</summary>
        public int DrawnCount { get; private set; }

        /// <summary>Citation line for the HUD, or empty without a star catalog.</summary>
        public string Attribution { get; private set; } = string.Empty;

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
            if (mesh != null)
            {
                Destroy(mesh);
            }
        }

        private void OnLoaded()
        {
            var sim = host.Simulation;
            if (sim.StarCount == 0)
            {
                return;
            }
            stars = new UsimStar[sim.StarCount];
            mesh = new Mesh { name = "Star field", indexFormat = IndexFormat.UInt32 };
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = starFieldMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            Fill();
            BuildTopology();
            // The catalog's title up to its first comma ("The Bright Star Catalogue"), and the registry id.
            Attribution = sim.StarSource != null
                ? $"{DrawnCount} stars: {sim.StarSource.Title.Split(',')[0]} ({sim.StarSource.Id})"
                : string.Empty;
        }

        // Corners, sizes and colors depend only on the magnitudes, so they are set once; positions follow the
        // directions.
        private void BuildTopology()
        {
            var drawn = 0;
            foreach (var s in stars)
            {
                if (s.VMagnitude <= faintestMagnitude)
                {
                    ++drawn;
                }
            }
            DrawnCount = drawn;
            vertices = new Vector3[4 * drawn];
            var corners = new Vector4[4 * drawn];
            var colors = new Color[4 * drawn];
            var indices = new int[6 * drawn];
            var k = 0;
            foreach (var s in stars)
            {
                if (s.VMagnitude > faintestMagnitude)
                {
                    continue;
                }
                var t = Mathf.Clamp01((faintestMagnitude - (float)s.VMagnitude) / (faintestMagnitude - BrightestMagnitude));
                var halfSize = Mathf.Lerp(SmallestHalfSizePixels, LargestHalfSizePixels, Mathf.Pow(t, 1.5f)) * 2f;
                var color = DisplayColor((s.Flags & NativeConstants.StarHasColor) != 0, s.BMinusV);
                color.a = DisplayIntensity(t);
                for (var c = 0; c < 4; ++c)
                {
                    corners[4 * k + c] = new Vector4(c == 0 || c == 3 ? -1f : 1f, c < 2 ? -1f : 1f, halfSize, 0f);
                    colors[4 * k + c] = color;
                }
                indices[6 * k] = 4 * k;
                indices[6 * k + 1] = 4 * k + 2;
                indices[6 * k + 2] = 4 * k + 1;
                indices[6 * k + 3] = 4 * k;
                indices[6 * k + 4] = 4 * k + 3;
                indices[6 * k + 5] = 4 * k + 2;
                ++k;
            }
            UpdatePositions();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, corners);
            mesh.SetColors(colors);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (2.5f * skyDistance));
        }

        private void Fill()
        {
            var sim = host.Simulation;
            // Directions use only the request's frame and axes.
            sim.FillStars(ref host.Request, stars);
            filledAtSeconds = SecondsOf(sim.Clock.Now);
            filledFrame = host.Request.Frame.Kind;
        }

        private void UpdatePositions()
        {
            var k = 0;
            foreach (var s in stars)
            {
                if (s.VMagnitude > faintestMagnitude)
                {
                    continue;
                }
                var p = new Vector3((float)s.Direction.X, (float)s.Direction.Y, (float)s.Direction.Z) * skyDistance;
                vertices[4 * k] = p;
                vertices[4 * k + 1] = p;
                vertices[4 * k + 2] = p;
                vertices[4 * k + 3] = p;
                ++k;
            }
        }

        private void LateUpdate()
        {
            var sim = host.Simulation;
            if (sim == null || meshRenderer == null)
            {
                return;
            }
            // The camera stays at the origin, so the sky sphere does too.
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            meshRenderer.enabled = Visible;
            var now = SecondsOf(sim.Clock.Now);
            if (Math.Abs(now - filledAtSeconds) > refreshDays * 86400.0 || filledFrame != host.Request.Frame.Kind)
            {
                Fill();
                UpdatePositions();
                mesh.SetVertices(vertices, 0, vertices.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            }
        }

        private static double SecondsOf(UsimInstant t) => t.Days * 86400.0 + t.Seconds;

        /// <summary>
        /// Display color for a B-V color index: a gradient from blue-white (B-V -0.4) through white and
        /// yellow-white to orange-red (B-V 2.0). Chosen for appearance, not a colorimetric conversion; a star
        /// without a measured color index is drawn white (a display fallback, FR-DATA-05).
        /// </summary>
        public static Color DisplayColor(bool hasColor, double bMinusV)
        {
            if (!hasColor)
            {
                return Color.white;
            }
            var stops = ColorStops;
            var x = (float)bMinusV;
            if (x <= stops[0].Key)
            {
                return stops[0].Value;
            }
            for (var i = 1; i < stops.Length; ++i)
            {
                if (x <= stops[i].Key)
                {
                    var f = (x - stops[i - 1].Key) / (stops[i].Key - stops[i - 1].Key);
                    return Color.Lerp(stops[i - 1].Value, stops[i].Value, f);
                }
            }
            return stops[stops.Length - 1].Value;
        }

        /// <summary>Display intensity for a star at <paramref name="t"/> from the faintest (0) to the brightest (1).</summary>
        public static float DisplayIntensity(float t) => Mathf.Lerp(0.35f, 1.6f, Mathf.Pow(t, 0.8f));

        private static readonly (float Key, Color Value)[] ColorStops =
        {
            (-0.4f, new Color(0.66f, 0.75f, 1.00f)),
            (0.0f, new Color(0.86f, 0.90f, 1.00f)),
            (0.6f, new Color(1.00f, 0.97f, 0.90f)),
            (1.2f, new Color(1.00f, 0.84f, 0.66f)),
            (2.0f, new Color(1.00f, 0.68f, 0.46f)),
        };
    }
}
