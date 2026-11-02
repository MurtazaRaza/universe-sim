using System.Collections.Generic;
using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The spaceship's mesh (ADR-0016): an original design made from code, in the Adventure palette of
    /// docs/ART_DIRECTION.md. It is about one unit long, nose toward +z and top toward +y. Vertex colors carry the
    /// paint (rgb) and the engine glow (alpha, 0 to 1), which the Ship shader reads. Being code, not a modeled or
    /// licensed asset, it needs no ATTRIBUTION.md entry; a model file replacing it would (A-04).
    /// </summary>
    public static class ShipMesh
    {
        // Display colors (sRGB, as the palette gives them).
        public static readonly Color Hull = new Color(0.93f, 0.91f, 0.86f);
        public static readonly Color Accent = new Color(1.00f, 0.52f, 0.30f);
        public static readonly Color Canopy = new Color(0.25f, 0.70f, 1.00f);
        public static readonly Color Trim = new Color(0.30f, 0.33f, 0.42f);
        public static readonly Color Nozzle = new Color(0.45f, 0.85f, 1.00f);

        /// <summary>A new ship mesh (the caller owns and destroys it).</summary>
        public static Mesh Create()
        {
            var b = new Builder();
            // Fuselage: a surface of revolution along z, flattened a little vertically.
            b.Lathe(new[]
            {
                new Vector2(-0.50f, 0.000f), new Vector2(-0.50f, 0.085f), new Vector2(-0.44f, 0.120f),
                new Vector2(-0.25f, 0.140f), new Vector2(0.05f, 0.135f), new Vector2(0.25f, 0.105f),
                new Vector2(0.40f, 0.060f), new Vector2(0.50f, 0.000f),
            }, Vector3.zero, new Vector3(1f, 0.82f, 1f), 24, z => z > 0.30f ? Accent : Hull);
            // Canopy: a glassy ellipsoid on top, toward the nose.
            b.Ellipsoid(new Vector3(0f, 0.085f, 0.10f), new Vector3(0.065f, 0.055f, 0.15f), 16, 10, Canopy);
            // Wings, swept back with a little dihedral, and a tail fin.
            foreach (var side in new[] { -1f, 1f })
            {
                b.Slab(new[]
                {
                    new Vector3(side * 0.12f, -0.02f, 0.10f), new Vector3(side * 0.12f, -0.02f, -0.28f),
                    new Vector3(side * 0.50f, 0.03f, -0.38f), new Vector3(side * 0.50f, 0.03f, -0.22f),
                }, 0.022f, Vector3.up, Accent);
                // Engine nacelles with glowing nozzles.
                var axis = new Vector3(side * 0.19f, -0.035f, 0f);
                b.Lathe(new[]
                {
                    new Vector2(-0.48f, 0.000f), new Vector2(-0.48f, 0.055f), new Vector2(-0.40f, 0.062f),
                    new Vector2(-0.18f, 0.058f), new Vector2(-0.10f, 0.035f), new Vector2(-0.06f, 0.000f),
                }, axis, Vector3.one, 16, z => z < -0.42f ? Trim : Hull);
                b.Disc(axis + new Vector3(0f, 0f, -0.481f), 0.045f, Vector3.back, 16, Nozzle, 1f);
            }
            b.Slab(new[]
            {
                new Vector3(0f, 0.09f, -0.22f), new Vector3(0f, 0.09f, -0.46f),
                new Vector3(0f, 0.30f, -0.50f), new Vector3(0f, 0.30f, -0.40f),
            }, 0.02f, Vector3.right, Accent);
            return b.ToMesh("Spaceship");
        }

        private sealed class Builder
        {
            private readonly List<Vector3> positions = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Color> colors = new List<Color>();
            private readonly List<int> triangles = new List<int>();

            // Paint in rgb; alpha is the engine glow (0 for everything but the nozzles).
            private int Add(Vector3 position, Vector3 normal, Color color, float glow = 0f)
            {
                positions.Add(position);
                normals.Add(normal.normalized);
                colors.Add(new Color(color.r, color.g, color.b, glow));
                return positions.Count - 1;
            }

            // Unity's front faces wind clockwise as seen from outside.
            private void Triangle(int a, int b, int c)
            {
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }

            /// <summary>
            /// A surface of revolution about the z axis through <paramref name="center"/>: each profile point is
            /// (z, radius), ending on the axis at both ends; the radius is scaled per axis by <paramref name="scale"/>.
            /// </summary>
            public void Lathe(Vector2[] profile, Vector3 center, Vector3 scale, int segments,
                              System.Func<float, Color> paint)
            {
                var first = positions.Count;
                for (var i = 0; i < profile.Length; ++i)
                {
                    // The profile's slope gives the normal's tilt along z.
                    var previous = profile[Mathf.Max(i - 1, 0)];
                    var next = profile[Mathf.Min(i + 1, profile.Length - 1)];
                    var tangent = (next - previous).normalized;
                    var radial = tangent.x;
                    var along = -tangent.y;
                    for (var s = 0; s <= segments; ++s)
                    {
                        var angle = 2f * Mathf.PI * s / segments;
                        var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                        var position = center + new Vector3(direction.x * profile[i].y * scale.x,
                                                            direction.y * profile[i].y * scale.y, profile[i].x);
                        var normal = new Vector3(direction.x / scale.x * radial, direction.y / scale.y * radial, along);
                        if (profile[i].y <= 0f)
                        {
                            normal = new Vector3(0f, 0f, i == 0 ? -1f : 1f);
                        }
                        Add(position, normal, paint(profile[i].x));
                    }
                }
                var stride = segments + 1;
                for (var i = 0; i + 1 < profile.Length; ++i)
                {
                    for (var s = 0; s < segments; ++s)
                    {
                        var a = first + i * stride + s;
                        var b = a + stride;
                        Triangle(a, a + 1, b);
                        Triangle(a + 1, b + 1, b);
                    }
                }
            }

            /// <summary>An ellipsoid centered at <paramref name="center"/> with semi-axes <paramref name="radii"/>.</summary>
            public void Ellipsoid(Vector3 center, Vector3 radii, int segments, int rings, Color color)
            {
                var first = positions.Count;
                for (var r = 0; r <= rings; ++r)
                {
                    var polar = Mathf.PI * r / rings;
                    for (var s = 0; s <= segments; ++s)
                    {
                        var azimuth = 2f * Mathf.PI * s / segments;
                        var unit = new Vector3(Mathf.Sin(polar) * Mathf.Cos(azimuth), Mathf.Cos(polar),
                                               Mathf.Sin(polar) * Mathf.Sin(azimuth));
                        var position = center + Vector3.Scale(unit, radii);
                        var normal = new Vector3(unit.x / radii.x, unit.y / radii.y, unit.z / radii.z);
                        Add(position, normal, color);
                    }
                }
                var stride = segments + 1;
                for (var r = 0; r < rings; ++r)
                {
                    for (var s = 0; s < segments; ++s)
                    {
                        // Rings run from the top (+y) down and the azimuth turns from +x toward +z; this order is
                        // clockwise seen from outside.
                        var a = first + r * stride + s;
                        var b = a + stride;
                        Triangle(a, a + 1, b);
                        Triangle(a + 1, b + 1, b);
                    }
                }
            }

            /// <summary>
            /// A flat plate: the quadrilateral <paramref name="outline"/> extruded by <paramref name="thickness"/>
            /// along <paramref name="across"/>, with flat faces.
            /// </summary>
            public void Slab(Vector3[] outline, float thickness, Vector3 across, Color color)
            {
                var half = across.normalized * (0.5f * thickness);
                var top = new Vector3[outline.Length];
                var bottom = new Vector3[outline.Length];
                for (var i = 0; i < outline.Length; ++i)
                {
                    top[i] = outline[i] + half;
                    bottom[i] = outline[i] - half;
                }
                Quad(top[0], top[1], top[2], top[3], color);
                Quad(bottom[3], bottom[2], bottom[1], bottom[0], color);
                for (var i = 0; i < outline.Length; ++i)
                {
                    var j = (i + 1) % outline.Length;
                    Quad(bottom[i], bottom[j], top[j], top[i], color);
                }
            }

            // A flat quadrilateral. Vector3.Cross(b - a, c - a) points toward the side from which a, b, c run
            // clockwise, Unity's front faces. Both sides are drawn, each with its own normal (the ship's shader
            // culls back faces), so the outline's order does not matter.
            private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
            {
                var normal = Vector3.Cross(b - a, c - a).normalized;
                var i = Add(a, normal, color);
                Add(b, normal, color);
                Add(c, normal, color);
                Add(d, normal, color);
                Triangle(i, i + 1, i + 2);
                Triangle(i, i + 2, i + 3);
                var j = Add(a, -normal, color);
                Add(b, -normal, color);
                Add(c, -normal, color);
                Add(d, -normal, color);
                Triangle(j, j + 2, j + 1);
                Triangle(j, j + 3, j + 2);
            }

            /// <summary>A flat disc facing <paramref name="facing"/>, glowing by <paramref name="glow"/>.</summary>
            public void Disc(Vector3 center, float radius, Vector3 facing, int segments, Color color, float glow)
            {
                var normal = facing.normalized;
                var u = Vector3.Cross(normal, Vector3.up).normalized;
                if (u.sqrMagnitude < 1e-6f)
                {
                    u = Vector3.right;
                }
                var v = Vector3.Cross(normal, u);
                var middle = Add(center, normal, color, glow);
                for (var s = 0; s <= segments; ++s)
                {
                    var angle = 2f * Mathf.PI * s / segments;
                    Add(center + radius * (Mathf.Cos(angle) * u + Mathf.Sin(angle) * v), normal, color, glow);
                }
                // Cross(u, v) is the normal, so this order is clockwise seen from where the disc faces.
                for (var s = 0; s < segments; ++s)
                {
                    Triangle(middle, middle + 1 + s, middle + 2 + s);
                }
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(positions);
                mesh.SetNormals(normals);
                mesh.SetColors(colors);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
