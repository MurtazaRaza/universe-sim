using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The feeling of speed in the spaceship (ADR-0019 decision 2, docs/ART_DIRECTION.md): pale streaks rushing past
    /// the camera and a slightly wider view while the ship goes fast (boosting, and in the middle of a trip). Nothing
    /// real is out there to streak past, so the effect is listed in the "what is exaggerated" panel, and like the ship
    /// it exists only in the stylized style. It reads the ship's pace (<see cref="ShipPace"/>) and moves nothing.
    /// </summary>
    [DefaultExecutionOrder(30)] // after the ship and the rig have placed the camera
    public sealed class SpeedCues : StyleEffect
    {
        [SerializeField] private ShipPilot ship;
        [SerializeField] private Material streakMaterial;

        // Display choices (docs/ART_DIRECTION.md), not physical values.
        [Header("Look")]
        [SerializeField, Range(16, 512)] private int streakCount = 180;
        [Tooltip("How much wider the view gets at full speed, degrees.")]
        [SerializeField, Range(0f, 20f)] private float extraFieldOfView = 9f;
        [Tooltip("Pace (heights per second) where the streaks start, and where they are full. The ship cruises at 0.6 " +
                 "and boosts to about 5.")]
        [SerializeField] private float paceStart = 1.2f;
        [SerializeField] private float paceFull = 4.5f;
        [SerializeField, Range(0f, 1f)] private float tripStrength = 1f;
        [SerializeField] private Color streakColor = new Color(0.75f, 0.85f, 1f, 1f);

        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int PhaseId = Shader.PropertyToID("_Phase");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly ShipPace pace = new ShipPace();
        private Camera cam;
        private float baseFieldOfView = 50f;
        private bool fieldOfViewChanged;
        private GameObject streaks;
        private MeshRenderer streakRenderer;
        private Mesh mesh;
        private Material material;
        private float intensity;
        private double phase;

        /// <summary>How strong the cues are now, 0 to 1 (for tests).</summary>
        public float Intensity => intensity;

        protected override Exaggeration[] Describe() => new[]
        {
            new Exaggeration("Speed streaks", "streaks and a wider view while the ship goes fast; nothing real is " +
                                              "out there to streak past",
                             "view up to " + extraFieldOfView.ToString("0", CultureInfo.InvariantCulture) + "° wider"),
        };

        public override bool ActiveInView(Camera c) => Applied && c != null && intensity > 0.01f;

        private void Awake()
        {
            cam = Camera.main;
            if (cam != null)
            {
                baseFieldOfView = cam.fieldOfView;
            }
            mesh = BuildMesh(streakCount);
            streaks = new GameObject("Speed streaks");
            if (cam != null)
            {
                streaks.transform.SetParent(cam.transform, false);
            }
            streaks.AddComponent<MeshFilter>().sharedMesh = mesh;
            streakRenderer = streaks.AddComponent<MeshRenderer>();
            streakRenderer.shadowCastingMode = ShadowCastingMode.Off;
            streakRenderer.receiveShadows = false;
            if (streakMaterial != null)
            {
                material = new Material(streakMaterial) { name = "Speed streaks (runtime)" };
                streakRenderer.sharedMaterial = material;
            }
            streaks.SetActive(false);
        }

        private void OnDestroy()
        {
            if (material != null)
            {
                Destroy(material);
            }
            if (mesh != null)
            {
                Destroy(mesh);
            }
            if (streaks != null)
            {
                Destroy(streaks);
            }
        }

        protected override void Apply(bool on)
        {
            if (!on)
            {
                intensity = 0f;
                RestoreFieldOfView();
                if (streaks != null)
                {
                    streaks.SetActive(false);
                }
            }
        }

        private void LateUpdate()
        {
            var dt = Time.unscaledDeltaTime;
            pace.Update(ship, dt);
            if (!Applied)
            {
                return;
            }
            var target = 0f;
            if (ship != null && ship.Active)
            {
                var flying = Mathf.Clamp01(((float)pace.HeightsPerSecond - paceStart) / Mathf.Max(paceFull - paceStart, 1e-3f));
                target = Mathf.Max(flying * flying * (3f - 2f * flying), tripStrength * pace.Trip);
            }
            // Quick to rise, slower to settle.
            var seconds = target > intensity ? 0.25f : 0.6f;
            intensity += (target - intensity) * (1f - Mathf.Exp(-dt / seconds));
            if (intensity < 1e-3f && target <= 0f)
            {
                intensity = 0f;
            }

            if (cam != null)
            {
                if (intensity > 0f)
                {
                    cam.fieldOfView = baseFieldOfView + extraFieldOfView * intensity;
                    fieldOfViewChanged = true;
                }
                else
                {
                    RestoreFieldOfView();
                }
            }
            var shown = intensity > 0.005f && material != null;
            if (streaks.activeSelf != shown)
            {
                streaks.SetActive(shown);
            }
            if (shown)
            {
                // The streaks' motion: faster with the intensity, accumulated so a change of speed never jumps.
                phase += dt * (0.35 + 1.9 * intensity);
                material.SetFloat(IntensityId, intensity);
                material.SetFloat(PhaseId, (float)(phase % 1000.0));
                material.SetColor(ColorId, streakColor);
            }
        }

        private void RestoreFieldOfView()
        {
            if (fieldOfViewChanged && cam != null)
            {
                cam.fieldOfView = baseFieldOfView;
            }
            fieldOfViewChanged = false;
        }

        /// <summary>
        /// The streaks: <paramref name="count"/> thin quads around the view axis in the camera's space, spread on a fixed
        /// pattern (a seeded generator, so every run looks the same). The vertex shader moves them toward the camera.
        /// UV0: angle around the axis (rad), distance from the axis, place along the streak (0 head, 1 tail), side (-1, 1).
        /// UV1: starting depth (0 to 1), speed factor, brightness.
        /// </summary>
        public static Mesh BuildMesh(int count)
        {
            var vertices = new Vector3[count * 4];
            var uv0 = new Vector4[count * 4];
            var uv1 = new Vector4[count * 4];
            var indices = new int[count * 6];
            var state = 0x2545F491u;
            float Next()
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return (state & 0xFFFFFF) / 16777216f;
            }
            for (var i = 0; i < count; ++i)
            {
                var angle = Next() * 2f * Mathf.PI;
                var radius = Mathf.Lerp(0.22f, 1.5f, Mathf.Sqrt(Next()));
                var depth = Next();
                var speed = Mathf.Lerp(0.7f, 1.3f, Next());
                var bright = Mathf.Lerp(0.35f, 1f, Next());
                for (var k = 0; k < 4; ++k)
                {
                    var along = k < 2 ? 0f : 1f;
                    var side = k % 2 == 0 ? -1f : 1f;
                    vertices[i * 4 + k] = Vector3.zero;
                    uv0[i * 4 + k] = new Vector4(angle, radius, along, side);
                    uv1[i * 4 + k] = new Vector4(depth, speed, bright, 0f);
                }
                indices[i * 6 + 0] = i * 4 + 0;
                indices[i * 6 + 1] = i * 4 + 2;
                indices[i * 6 + 2] = i * 4 + 1;
                indices[i * 6 + 3] = i * 4 + 1;
                indices[i * 6 + 4] = i * 4 + 2;
                indices[i * 6 + 5] = i * 4 + 3;
            }
            var mesh = new Mesh { name = "Speed streaks" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            // The shader places the vertices; the bounds cover where it can put them.
            mesh.bounds = new Bounds(new Vector3(0f, 0f, 3f), new Vector3(4f, 4f, 7f));
            return mesh;
        }
    }
}
