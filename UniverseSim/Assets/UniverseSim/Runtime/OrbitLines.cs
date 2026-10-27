using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Profiling;
using UniverseSim.Unity.Native;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniverseSim.Unity
{
    /// <summary>
    /// Orbit lines (P5-10): one revolution of each planet, dwarf planet and moon around its usual center,
    /// as render positions from the core every frame (the core caches the sampled paths). All lines are one
    /// mesh, uploaded once and drawn in one call per frame by the UniverseSim/Line shader with a constant
    /// width in pixels; each line's color is in its vertices. Lines whose positions are approximate
    /// (ADR-0008) get their own color. A moon's orbit is hidden while the moon is drawn within a few pixels of
    /// its planet (it would only be a smudge around the planet).
    /// </summary>
    [DefaultExecutionOrder(10)]
    public sealed class OrbitLines : MonoBehaviour
    {
        private static readonly ProfilerMarker FillMarker = new ProfilerMarker("UniverseSim.OrbitLines");

        [SerializeField] private SimulationHost host;
        [SerializeField] private Material lineMaterial;
        [SerializeField] private Color color = new Color(0.45f, 0.6f, 0.85f, 0.55f);
        [SerializeField] private Color approximateColor = new Color(0.9f, 0.65f, 0.3f, 0.55f);
        [SerializeField] private Color moonColor = new Color(0.55f, 0.7f, 0.75f, 0.4f);

        [Tooltip("A moon's orbit is drawn once the moon is at least this many pixels from its planet on screen.")]
        [SerializeField] private float moonMinPixels = 10f;

        // One vertex of a line: the point, its color, and the neighbouring point with the side to move to
        // (Line.shader). Attributes in Unity's order (position, color, texture coordinates).
        [StructLayout(LayoutKind.Sequential)]
        private struct LineVertex
        {
            public Vector3 Position;
            public Color32 Color;
            public Vector4 Neighbour;
        }

        private static readonly VertexAttributeDescriptor[] VertexLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
        };

        private const MeshUpdateFlags UpdateFlags = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontResetBoneBounds |
                                                    MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontRecalculateBounds;

        // Render positions stay within the compression range, so one large box covers every line.
        private static readonly Bounds LineBounds = new Bounds(Vector3.zero, Vector3.one * 2.0e7f);

        private sealed class Orbit
        {
            public BodyInfo Body;
            public UsimBodyId Center;
        }

        private readonly List<Orbit> orbits = new List<Orbit>();
        private readonly UsimVec3d[] native = new UsimVec3d[16384];
        private LineVertex[] vertices = Array.Empty<LineVertex>();
        private int[] indices = Array.Empty<int>();
        // The point count of each line in the mesh, 0 for a line not drawn: the indices change only with it.
        private int[] drawnPoints = Array.Empty<int>();
        private int indexCount;
        private Mesh mesh;
        private MeshRenderer meshRenderer;

        /// <summary>Shows or hides every orbit line (UC-06).</summary>
        public bool Visible { get; set; } = true;

        /// <summary>The number of lines drawn in the latest frame.</summary>
        public int DrawnCount { get; private set; }

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
            foreach (var body in sim.Bodies)
            {
                var drawn = body.Kind == 2 || body.Kind == 3 || body.Kind == 4; // planet, dwarf planet, moon
                if (drawn && body.HasPosition && sim.TryDefaultOrbitCenter(body.Id, out var center))
                {
                    orbits.Add(new Orbit { Body = body, Center = center });
                }
            }
            drawnPoints = new int[orbits.Count];
            var go = new GameObject("Orbit lines");
            go.transform.SetParent(transform, false);
            mesh = new Mesh { name = "Orbit lines" };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = lineMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.enabled = false;
        }

        private void LateUpdate()
        {
            var sim = host.Simulation;
            if (sim == null || mesh == null)
            {
                return;
            }
            using var marker = FillMarker.Auto();
            var cam = Camera.main;
            // The host's container sits at the origin with no rotation, so render positions are world positions.
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var planetColor = VertexColor(color);
            var orbitingMoonColor = VertexColor(moonColor);
            var approximate = VertexColor(approximateColor);
            var vertexCount = 0;
            var drawn = 0;
            var layoutChanged = false;
            for (var k = 0; k < orbits.Count; ++k)
            {
                var orbit = orbits[k];
                var count = 0;
                if (Visible && (cam == null || host.Views[orbit.Body.Index].StandsApart(cam, moonMinPixels)))
                {
                    count = Math.Min(sim.OrbitPath(orbit.Body.Id, orbit.Center, ref host.Request, native, out var flags), native.Length);
                    if (count >= 2)
                    {
                        var lineColor = (flags & NativeConstants.OrbitApproximate) != 0 ? approximate
                            : orbit.Body.Kind == 4 ? orbitingMoonColor : planetColor;
                        Write(vertexCount, count, lineColor);
                        vertexCount += 2 * count;
                        ++drawn;
                    }
                    else
                    {
                        count = 0;
                    }
                }
                if (drawnPoints[k] != count)
                {
                    drawnPoints[k] = count;
                    layoutChanged = true;
                }
            }
            DrawnCount = drawn;
            meshRenderer.enabled = vertexCount > 0;
            if (vertexCount == 0)
            {
                return;
            }
            if (layoutChanged)
            {
                UpdateIndices();
            }
            mesh.SetVertexBufferData(vertices, 0, 0, vertexCount, 0, UpdateFlags);
        }

        // Two vertices per point, each knowing its neighbour and its side.
        private void Write(int start, int count, Color32 lineColor)
        {
            var needed = start + 2 * count;
            if (needed > vertices.Length)
            {
                Array.Resize(ref vertices, Math.Max(needed, 2 * vertices.Length));
                mesh.SetVertexBufferParams(vertices.Length, VertexLayout);
            }
            var p = ToVector3(native[0]);
            for (var i = 0; i < count; ++i)
            {
                // The last point looks back at the one before it, so its sides are mirrored.
                var last = i == count - 1;
                var n = ToVector3(native[last ? i - 1 : i + 1]);
                var side = last ? -1f : 1f;
                var v = start + 2 * i;
                vertices[v] = new LineVertex { Position = p, Color = lineColor, Neighbour = new Vector4(n.x, n.y, n.z, side) };
                vertices[v + 1] = new LineVertex { Position = p, Color = lineColor, Neighbour = new Vector4(n.x, n.y, n.z, -side) };
                p = n;
            }
        }

        // Two triangles per segment of each drawn line; rebuilt only when a line appears, disappears or changes
        // its point count (when the core samples a new path).
        private void UpdateIndices()
        {
            indexCount = 0;
            foreach (var count in drawnPoints)
            {
                indexCount += count > 1 ? 6 * (count - 1) : 0;
            }
            if (indexCount > indices.Length)
            {
                indices = new int[Math.Max(indexCount, 2 * indices.Length)];
                mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt32);
                mesh.subMeshCount = 1;
            }
            var t = 0;
            var first = 0;
            foreach (var count in drawnPoints)
            {
                for (var i = 0; i < count - 1; ++i)
                {
                    var v = first + 2 * i;
                    indices[t++] = v;
                    indices[t++] = v + 1;
                    indices[t++] = v + 2;
                    indices[t++] = v + 1;
                    indices[t++] = v + 3;
                    indices[t++] = v + 2;
                }
                first += 2 * count;
            }
            mesh.SetIndexBufferData(indices, 0, 0, indexCount, UpdateFlags);
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, indexCount) { bounds = LineBounds, vertexCount = first }, UpdateFlags);
            mesh.bounds = LineBounds;
        }

        // Vertex colors reach the shader as stored, while a material color is converted from sRGB in a linear
        // project; converting here keeps the colors the material gave the lines.
        private static Color32 VertexColor(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;

        private static Vector3 ToVector3(UsimVec3d v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);
    }
}
