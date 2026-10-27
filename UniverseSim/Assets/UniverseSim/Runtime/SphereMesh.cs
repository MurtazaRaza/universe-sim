using System;
using UniverseSim.Unity.Native;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniverseSim.Unity
{
    /// <summary>A unit sphere (radius 1) with more segments than Unity's built-in one, for close views.</summary>
    public static class SphereMesh
    {
        /// <summary>A UV sphere with <paramref name="segments"/> around and <paramref name="rings"/> pole to pole.</summary>
        public static Mesh Create(int segments, int rings)
        {
            var vertices = new Vector3[(segments + 1) * (rings + 1)];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            for (var r = 0; r <= rings; ++r)
            {
                var polar = Mathf.PI * r / rings; // 0 at +Y (north), pi at -Y
                var y = Mathf.Cos(polar);
                var ring = Mathf.Sin(polar);
                for (var s = 0; s <= segments; ++s)
                {
                    var azimuth = 2f * Mathf.PI * s / segments;
                    var i = r * (segments + 1) + s;
                    var p = new Vector3(ring * Mathf.Cos(azimuth), y, ring * Mathf.Sin(azimuth));
                    vertices[i] = p;
                    normals[i] = p;
                    uv[i] = new Vector2((float)s / segments, 1f - (float)r / rings);
                }
            }
            var mesh = new Mesh { name = "UniverseSim Sphere", indexFormat = IndexFormatFor(vertices.Length) };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.triangles = Triangles(segments, rings);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return mesh;
        }

        /// <summary>
        /// A unit sphere for a body with a surface map (P6-07, ADR-0014). The vertex at parameters (mu, psi) lies
        /// in the body-fixed direction (cos psi cos mu, cos psi sin mu, sin psi), expressed in the host's
        /// <paramref name="axes"/> by the core (usim_axes_to_host), so the mesh's local axes are the body's axes
        /// as the snapshot's orientation and radii expect. Its UV is the planetocentric longitude and latitude of
        /// the point the vertex becomes once the mesh is scaled by the radii a, b and c: u = (lon + 180) / 360
        /// and v = (lat + 90) / 180, the layout of the pipeline's maps. Only the ratios of the radii matter.
        /// </summary>
        public static Mesh CreateMapped(int segments, int rings, double a, double b, double c, int axes)
        {
            var axisX = ToVector(CoreSimulation.AxesToHost(axes, new UsimVec3d { X = 1.0 }));
            var axisY = ToVector(CoreSimulation.AxesToHost(axes, new UsimVec3d { Y = 1.0 }));
            var axisZ = ToVector(CoreSimulation.AxesToHost(axes, new UsimVec3d { Z = 1.0 }));
            var vertices = new Vector3[(segments + 1) * (rings + 1)];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            for (var r = 0; r <= rings; ++r)
            {
                var psi = Math.PI / 2 - Math.PI * r / rings; // +90 degrees (north) first, as in Create
                var cosPsi = Math.Cos(psi);
                var sinPsi = Math.Sin(psi);
                for (var s = 0; s <= segments; ++s)
                {
                    var mu = -Math.PI + 2.0 * Math.PI * s / segments; // the seam at 180 degrees
                    var cosMu = Math.Cos(mu);
                    var sinMu = Math.Sin(mu);
                    var i = r * (segments + 1) + s;
                    var p = (float)(cosPsi * cosMu) * axisX + (float)(cosPsi * sinMu) * axisY + (float)sinPsi * axisZ;
                    vertices[i] = p;
                    normals[i] = p;
                    // The scaled point (a cos psi cos mu, b cos psi sin mu, c sin psi) and its planetocentric angles.
                    var longitude = Math.Atan2(b * sinMu, a * cosMu);
                    var latitude = Math.Atan2(c * sinPsi, Math.Sqrt(Square(a * cosPsi * cosMu) + Square(b * cosPsi * sinMu)));
                    var u = s == 0 ? 0.0 : s == segments ? 1.0 : (longitude + Math.PI) / (2.0 * Math.PI);
                    uv[i] = new Vector2((float)u, (float)((latitude + Math.PI / 2) / Math.PI));
                }
            }
            var mesh = new Mesh { name = "UniverseSim Mapped Sphere", indexFormat = IndexFormatFor(vertices.Length) };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.triangles = Triangles(segments, rings);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return mesh;
        }

        private static double Square(double x) => x * x;

        // 16-bit indices while they can address every vertex (all the levels of SphereLods), which halves the
        // index buffer.
        private static IndexFormat IndexFormatFor(int vertexCount) =>
            vertexCount <= ushort.MaxValue + 1 ? IndexFormat.UInt16 : IndexFormat.UInt32;

        private static Vector3 ToVector(UsimVec3d v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

        private static int[] Triangles(int segments, int rings)
        {
            var triangles = new int[segments * rings * 6];
            var t = 0;
            for (var r = 0; r < rings; ++r)
            {
                for (var s = 0; s < segments; ++s)
                {
                    var a = r * (segments + 1) + s;
                    var b = a + segments + 1;
                    triangles[t++] = a;
                    triangles[t++] = a + 1;
                    triangles[t++] = b;
                    triangles[t++] = a + 1;
                    triangles[t++] = b + 1;
                    triangles[t++] = b;
                }
            }
            return triangles;
        }
    }
}
