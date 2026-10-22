using System.Collections.Generic;
using System.Linq;
using UniverseSim.Unity.Native;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniverseSim.Unity
{
    /// <summary>
    /// Planetary rings (SPEC FR-REN-10, P6-04): one flat annulus per ringed body, in its equatorial plane,
    /// placed, oriented and scaled with the body every frame. Radii and optical depths come from the core's
    /// ring data with their sources; nothing physical is computed here. The radial profile drawn is a display
    /// rule: each ring region shows the middle of its optical depth range (<see cref="DisplayOpticalDepth"/>),
    /// gaps are empty, and regions without an optical depth are not drawn. The body's shader gets the same
    /// profile for the rings' shadow.
    /// </summary>
    [DefaultExecutionOrder(15)]
    public sealed class RingSystems : MonoBehaviour
    {
        private static readonly int InnerFractionId = Shader.PropertyToID("_InnerFraction");
        private static readonly int LightDirectionId = Shader.PropertyToID("_LightDirection");
        private static readonly int PlanetWorldToLocalId = Shader.PropertyToID("_PlanetWorldToLocal");
        private static readonly int ProfileId = Shader.PropertyToID("_Profile");

        [SerializeField] private SimulationHost host;
        [SerializeField] private Material ringMaterial;

        [Tooltip("Texels of the radial optical depth profile.")]
        [SerializeField] private int profileResolution = 4096;

        [Tooltip("Segments around the annulus.")]
        [SerializeField] private int segments = 720;

        private sealed class RingSystem
        {
            public BodyView Planet;
            public double OuterKm;
            public float InnerFraction;
            public Texture2D Profile;
            public Transform Transform;
            public MeshRenderer Renderer;
            public Mesh Mesh;
        }

        private readonly List<RingSystem> systems = new List<RingSystem>();
        private MaterialPropertyBlock block;

        /// <summary>Shows or hides the rings (their shadows stay on the planets).</summary>
        public bool Visible { get; set; } = true;

        /// <summary>The number of ringed bodies.</summary>
        public int Count => systems.Count;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            host.Loaded += OnLoaded;
        }

        private void OnDestroy()
        {
            if (host != null)
            {
                host.Loaded -= OnLoaded;
            }
            foreach (var system in systems)
            {
                Destroy(system.Profile);
                Destroy(system.Mesh);
            }
        }

        /// <summary>
        /// The optical depth a ring region is drawn with: the middle of the range its source gives (a display
        /// rule; the sources give ranges, for example 0.4 to 5 for Saturn's B ring).
        /// </summary>
        public static double DisplayOpticalDepth(RingInfo ring) =>
            ring.HasOpticalDepth ? 0.5 * (ring.OpticalDepthSmallest + ring.OpticalDepthLargest) : 0.0;

        /// <summary>
        /// The drawn optical depth at <paramref name="radiusKm"/> from the body's center: the ring region
        /// that contains it, zero in a gap or outside every region.
        /// </summary>
        public static double ProfileAt(IReadOnlyList<RingInfo> rings, double radiusKm)
        {
            var tau = 0.0;
            foreach (var ring in rings)
            {
                if (radiusKm < ring.InnerRadiusKm || radiusKm > ring.OuterRadiusKm)
                {
                    continue;
                }
                if (ring.IsGap)
                {
                    return 0.0;
                }
                tau = DisplayOpticalDepth(ring);
            }
            return tau;
        }

        private void OnLoaded()
        {
            var sim = host.Simulation;
            foreach (var group in sim.Rings.GroupBy(r => r.BodyIndex))
            {
                var rings = group.ToList();
                var drawn = rings.Where(r => !r.IsGap && r.HasOpticalDepth).ToList();
                if (drawn.Count == 0 || group.Key < 0 || group.Key >= host.Views.Count)
                {
                    continue;
                }
                var innerKm = drawn.Min(r => r.InnerRadiusKm);
                var outerKm = drawn.Max(r => r.OuterRadiusKm);
                var system = new RingSystem
                {
                    Planet = host.Views[group.Key],
                    OuterKm = outerKm,
                    InnerFraction = (float)(innerKm / outerKm),
                    Profile = BuildProfile(rings, innerKm, outerKm),
                };
                var go = new GameObject("Rings of " + system.Planet.Info.Name);
                go.transform.SetParent(transform, false);
                system.Transform = go.transform;
                system.Mesh = BuildAnnulus(system.InnerFraction);
                go.AddComponent<MeshFilter>().sharedMesh = system.Mesh;
                system.Renderer = go.AddComponent<MeshRenderer>();
                system.Renderer.sharedMaterial = ringMaterial;
                system.Renderer.shadowCastingMode = ShadowCastingMode.Off;
                system.Renderer.receiveShadows = false;
                system.Renderer.enabled = false;
                system.Planet.SetRings(system.Profile, system.InnerFraction, outerKm);
                systems.Add(system);
            }
        }

        private Texture2D BuildProfile(IReadOnlyList<RingInfo> rings, double innerKm, double outerKm)
        {
            var texture = new Texture2D(profileResolution, 1, TextureFormat.RFloat, false, true)
            {
                name = "Ring optical depth profile",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var texels = new float[profileResolution];
            for (var i = 0; i < profileResolution; ++i)
            {
                var radius = innerKm + (i + 0.5) / profileResolution * (outerKm - innerKm);
                texels[i] = (float)ProfileAt(rings, radius);
            }
            texture.SetPixelData(texels, 0);
            texture.Apply(false, true);
            return texture;
        }

        // A flat annulus in the XZ plane from innerFraction to 1, double-sided through the shader (Cull Off).
        private Mesh BuildAnnulus(float innerFraction)
        {
            var vertices = new Vector3[2 * (segments + 1)];
            var indices = new int[6 * segments];
            for (var i = 0; i <= segments; ++i)
            {
                var angle = 2f * Mathf.PI * i / segments;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                // The outer vertices sit slightly outside radius 1 so the polygon covers the whole circle; the
                // shader cuts the exact edge from the radius.
                vertices[2 * i] = direction * innerFraction * Mathf.Cos(Mathf.PI / segments);
                vertices[2 * i + 1] = direction / Mathf.Cos(Mathf.PI / segments);
            }
            for (var i = 0; i < segments; ++i)
            {
                var v = 2 * i;
                indices[6 * i] = v;
                indices[6 * i + 1] = v + 1;
                indices[6 * i + 2] = v + 2;
                indices[6 * i + 3] = v + 1;
                indices[6 * i + 4] = v + 3;
                indices[6 * i + 5] = v + 2;
            }
            var mesh = new Mesh { name = "Ring annulus" };
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void LateUpdate()
        {
            if (host.Simulation == null)
            {
                return;
            }
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var system in systems)
            {
                var planet = system.Planet;
                var shown = Visible && planet.HasPosition && planet.RenderRadius > 0f &&
                            (planet.Flags & NativeConstants.SnapshotNoOrientation) == 0;
                system.Renderer.enabled = shown;
                if (!shown)
                {
                    continue;
                }
                // Render units per km from the snapshot (scale mode and distance compression included).
                var scale = (float)(system.OuterKm * planet.RenderRadius / planet.Info.MeanRadiusKm);
                system.Transform.SetPositionAndRotation(planet.transform.position, planet.transform.rotation);
                system.Transform.localScale = new Vector3(scale, scale, scale);
                block.SetFloat(InnerFractionId, system.InnerFraction);
                block.SetVector(LightDirectionId, planet.LightDirection);
                block.SetMatrix(PlanetWorldToLocalId, planet.transform.worldToLocalMatrix);
                block.SetTexture(ProfileId, system.Profile);
                system.Renderer.SetPropertyBlock(block);
            }
        }
    }
}
