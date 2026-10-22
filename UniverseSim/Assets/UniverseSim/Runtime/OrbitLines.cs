using System;
using System.Collections.Generic;
using UniverseSim.Unity.Native;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniverseSim.Unity
{
    /// <summary>
    /// Orbit lines (P5-10): one revolution of each planet, dwarf planet and moon around its usual center,
    /// as render positions from the core every frame (the core caches the sampled paths). The lines are
    /// meshes drawn by the UniverseSim/Line shader with a constant width in pixels. Lines whose positions
    /// are approximate (ADR-0008) get their own color. A moon's orbit is hidden while the moon is drawn
    /// within a few pixels of its planet (it would only be a smudge around the planet).
    /// </summary>
    [DefaultExecutionOrder(10)]
    public sealed class OrbitLines : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private SimulationHost host;
        [SerializeField] private Material lineMaterial;
        [SerializeField] private Color color = new Color(0.45f, 0.6f, 0.85f, 0.55f);
        [SerializeField] private Color approximateColor = new Color(0.9f, 0.65f, 0.3f, 0.55f);
        [SerializeField] private Color moonColor = new Color(0.55f, 0.7f, 0.75f, 0.4f);

        [Tooltip("A moon's orbit is drawn once the moon is at least this many pixels from its planet on screen.")]
        [SerializeField] private float moonMinPixels = 10f;

        private sealed class Orbit
        {
            public BodyInfo Body;
            public UsimBodyId Center;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public int PointCount = -1;
            public Vector3[] Vertices = Array.Empty<Vector3>();
            public Vector4[] Neighbours = Array.Empty<Vector4>();
        }

        private readonly List<Orbit> orbits = new List<Orbit>();
        private readonly UsimVec3d[] native = new UsimVec3d[16384];
        private MaterialPropertyBlock block;

        /// <summary>Shows or hides every orbit line (UC-06).</summary>
        public bool Visible { get; set; } = true;

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
            foreach (var orbit in orbits)
            {
                Destroy(orbit.Mesh);
            }
        }

        private void OnLoaded()
        {
            var sim = host.Simulation;
            foreach (var body in sim.Bodies)
            {
                var drawn = body.Kind == 2 || body.Kind == 3 || body.Kind == 4; // planet, dwarf planet, moon
                if (!drawn || !body.HasPosition || !sim.TryDefaultOrbitCenter(body.Id, out var center))
                {
                    continue;
                }
                var go = new GameObject("Orbit of " + body.Name);
                go.transform.SetParent(transform, false);
                var mesh = new Mesh { name = "Orbit of " + body.Name, indexFormat = IndexFormat.UInt32 };
                mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var meshRenderer = go.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = lineMaterial;
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
                meshRenderer.enabled = false;
                orbits.Add(new Orbit { Body = body, Center = center, Mesh = mesh, Renderer = meshRenderer });
            }
        }

        private void LateUpdate()
        {
            var sim = host.Simulation;
            var cam = Camera.main;
            if (sim == null)
            {
                return;
            }
            // The host's container sits at the origin with no rotation, so render positions are world positions.
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var orbit in orbits)
            {
                orbit.Renderer.enabled = false;
                if (!Visible)
                {
                    continue;
                }
                var view = host.Views[orbit.Body.Index];
                if (cam != null && !view.StandsApart(cam, moonMinPixels))
                {
                    continue;
                }
                var count = sim.OrbitPath(orbit.Body.Id, orbit.Center, ref host.Request, native, out var flags);
                if (count < 2)
                {
                    continue;
                }
                count = Math.Min(count, native.Length);
                Fill(orbit, count);
                block.SetColor(ColorId, (flags & NativeConstants.OrbitApproximate) != 0 ? approximateColor
                                        : orbit.Body.Kind == 4 ? moonColor : color);
                orbit.Renderer.SetPropertyBlock(block);
                orbit.Renderer.enabled = true;
            }
        }

        // Two vertices per point, each knowing its neighbour and its side; the topology is rebuilt only when
        // the point count changes (when the core samples a new path).
        private void Fill(Orbit orbit, int count)
        {
            var mesh = orbit.Mesh;
            if (orbit.PointCount != count)
            {
                orbit.PointCount = count;
                orbit.Vertices = new Vector3[2 * count];
                orbit.Neighbours = new Vector4[2 * count];
                var indices = new int[6 * (count - 1)];
                for (var i = 0; i < count - 1; ++i)
                {
                    var v = 2 * i;
                    indices[6 * i] = v;
                    indices[6 * i + 1] = v + 1;
                    indices[6 * i + 2] = v + 2;
                    indices[6 * i + 3] = v + 1;
                    indices[6 * i + 4] = v + 3;
                    indices[6 * i + 5] = v + 2;
                }
                mesh.Clear();
                mesh.SetVertices(orbit.Vertices);
                mesh.SetUVs(0, orbit.Neighbours);
                mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            }
            for (var i = 0; i < count; ++i)
            {
                var p = ToVector3(native[i]);
                // The last point looks back at the one before it, so its sides are mirrored.
                var last = i == count - 1;
                var n = ToVector3(native[last ? i - 1 : i + 1]);
                var side = last ? -1f : 1f;
                orbit.Vertices[2 * i] = p;
                orbit.Vertices[2 * i + 1] = p;
                orbit.Neighbours[2 * i] = new Vector4(n.x, n.y, n.z, side);
                orbit.Neighbours[2 * i + 1] = new Vector4(n.x, n.y, n.z, -side);
            }
            mesh.SetVertices(orbit.Vertices, 0, orbit.Vertices.Length,
                             MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            mesh.SetUVs(0, orbit.Neighbours, 0, orbit.Neighbours.Length,
                        MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            // Render positions stay within the compression range, so one large box covers every line.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2.0e7f);
        }

        private static Vector3 ToVector3(UsimVec3d v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);
    }
}
