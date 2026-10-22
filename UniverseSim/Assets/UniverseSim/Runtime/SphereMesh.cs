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
            var mesh = new Mesh { name = "UniverseSim Sphere", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return mesh;
        }
    }
}
