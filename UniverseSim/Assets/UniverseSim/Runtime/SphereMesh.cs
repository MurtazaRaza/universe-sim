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

        /// <summary>
        /// Where the drawn sphere meets the ray from its center along <paramref name="direction"/> (mesh-local axes,
        /// any length), as a fraction of the unit sphere's radius: 1 at a vertex, less inside a facet. The mesh is the
        /// one <see cref="Create"/> or, with <paramref name="mapped"/>, <see cref="CreateMapped"/> builds with
        /// <paramref name="segments"/> around and half as many rings (<see cref="SphereLods"/>). Scaling the mesh by
        /// a body's radii keeps the fraction along each ray, so the drawn ground lies (1 - fraction) times the
        /// reference surface's distance from the center below it (ADR-0017). Display geometry only.
        /// </summary>
        public static double DrawnRadiusFraction(Double3 direction, int segments, bool mapped, int axes)
        {
            var rings = segments / 2;
            var p = direction.Normalized;
            // The point's grid coordinates, in rings from the north pole and segments around, and the vertex at
            // grid coordinates (r, s), with the same formulas as the mesh builders.
            double ringCoordinate, segmentCoordinate;
            Func<int, int, Double3> vertex;
            if (mapped)
            {
                var axisX = Double3.From(CoreSimulation.AxesToHost(axes, new UsimVec3d { X = 1.0 }));
                var axisY = Double3.From(CoreSimulation.AxesToHost(axes, new UsimVec3d { Y = 1.0 }));
                var axisZ = Double3.From(CoreSimulation.AxesToHost(axes, new UsimVec3d { Z = 1.0 }));
                var psi = Math.Asin(Math.Clamp(p.Dot(axisZ), -1.0, 1.0));
                var mu = Math.Atan2(p.Dot(axisY), p.Dot(axisX));
                ringCoordinate = (Math.PI / 2 - psi) / (Math.PI / rings);
                segmentCoordinate = (mu + Math.PI) / (2.0 * Math.PI / segments);
                vertex = (r, s) =>
                {
                    var vPsi = Math.PI / 2 - Math.PI * r / rings;
                    var vMu = -Math.PI + 2.0 * Math.PI * s / segments;
                    return axisX * (Math.Cos(vPsi) * Math.Cos(vMu)) + axisY * (Math.Cos(vPsi) * Math.Sin(vMu)) +
                           axisZ * Math.Sin(vPsi);
                };
            }
            else
            {
                var polar = Math.Acos(Math.Clamp(p.Y, -1.0, 1.0));
                var azimuth = Math.Atan2(p.Z, p.X);
                if (azimuth < 0.0)
                {
                    azimuth += 2.0 * Math.PI;
                }
                ringCoordinate = polar / (Math.PI / rings);
                segmentCoordinate = azimuth / (2.0 * Math.PI / segments);
                vertex = (r, s) =>
                {
                    var vPolar = Math.PI * r / rings;
                    var vAzimuth = 2.0 * Math.PI * s / segments;
                    return new Double3(Math.Sin(vPolar) * Math.Cos(vAzimuth), Math.Cos(vPolar),
                                       Math.Sin(vPolar) * Math.Sin(vAzimuth));
                };
            }
            // The drawn sphere is convex with its center inside, so the ray leaves it where it first crosses a facet's
            // plane: the smallest distance d / (n . p) over the facets it faces (n . p > 0). The facet it leaves by is
            // in the grid cell of the direction or a neighboring one (an edge between rings is a chord, and the plane
            // through it and the center bends away from the ring's latitude), so the 3 x 3 cells around it suffice.
            var r0 = Math.Clamp((int)Math.Floor(ringCoordinate), 0, rings - 1);
            var s0 = (int)Math.Floor(segmentCoordinate);
            var fraction = double.PositiveInfinity;
            for (var r = Math.Max(r0 - 1, 0); r <= Math.Min(r0 + 1, rings - 1); ++r)
            {
                for (var s = s0 - 1; s <= s0 + 1; ++s)
                {
                    // The quad's two triangles as Triangles() orders them: (a, a + 1, b) and (a + 1, b + 1, b). The
                    // vertex formulas are periodic in s, so neighbors across the seam need no wrapping.
                    var a = vertex(r, s);
                    var a1 = vertex(r, s + 1);
                    var b = vertex(r + 1, s);
                    var b1 = vertex(r + 1, s + 1);
                    fraction = Math.Min(fraction, ExitAlong(p, a, a1, b));
                    fraction = Math.Min(fraction, ExitAlong(p, a1, b1, b));
                }
            }
            return double.IsInfinity(fraction) ? 1.0 : Math.Min(1.0, fraction);
        }

        // Where the ray from the center along unit `p` crosses the plane of triangle (v0, v1, v2), in units of the
        // radius; infinity for a degenerate triangle (at a pole) or a plane the ray does not face.
        private static double ExitAlong(Double3 p, Double3 v0, Double3 v1, Double3 v2)
        {
            var normal = Cross(v1 - v0, v2 - v0);
            if (normal.Length < 1e-12)
            {
                return double.PositiveInfinity;
            }
            var distance = normal.Dot(v0);
            if (distance < 0.0)
            {
                normal = -normal; // outward, so the center is on the inner side
                distance = -distance;
            }
            var along = normal.Dot(p);
            return along > 1e-12 ? distance / along : double.PositiveInfinity;
        }

        private static Double3 Cross(Double3 u, Double3 v) =>
            new Double3(u.Y * v.Z - u.Z * v.Y, u.Z * v.X - u.X * v.Z, u.X * v.Y - u.Y * v.X);

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
