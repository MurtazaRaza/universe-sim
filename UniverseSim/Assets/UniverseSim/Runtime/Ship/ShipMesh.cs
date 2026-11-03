using System;
using System.Collections.Generic;
using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The spaceship's mesh (ADR-0016): an original design made from code in the Adventure look of
    /// docs/ART_DIRECTION.md section 11. Like the bodies it is smooth and clay-like: every part is a rounded form with
    /// smooth normals (a plump hull with a rounded nose, a bubble canopy, thick wings and fins with rounded tips,
    /// capsule engines with recessed nozzles), and soft occlusion is baked where the parts meet, so it reads as one
    /// toy-like craft rather than flat plates. It is one unit long, nose toward +z and top toward +y.
    /// Vertex colors carry the paint (rgb, sRGB) and the engine glow (alpha, 0 to 1); the first texture coordinate
    /// carries how open the surface is (x, 1 = nothing nearby, from the baked occlusion) and its finish (y: 0 satin
    /// paint, 1 glass). The Ship shader reads them. Being code, not a modeled or licensed asset, it needs no
    /// ATTRIBUTION.md entry; a model file replacing it would (A-04).
    /// </summary>
    public static class ShipMesh
    {
        // Display colors (sRGB, as docs/ART_DIRECTION.md section 11 gives them).
        public static readonly Color Hull = new Color(0.88f, 0.85f, 0.80f);
        public static readonly Color Accent = new Color(1.00f, 0.50f, 0.32f);
        public static readonly Color Canopy = new Color(0.20f, 0.55f, 1.00f);
        public static readonly Color Trim = new Color(0.30f, 0.33f, 0.42f);
        public static readonly Color Nozzle = new Color(0.45f, 0.85f, 1.00f);

        /// <summary>Finish of glass in the first texture coordinate's y (paint is 0).</summary>
        public const float Glass = 1f;

        // The hull: an egg-like body of revolution from the tail to the rounded nose, a little flatter than wide.
        private const float HullTail = -0.40f;
        private const float HullNose = 0.50f;
        private const float HullRadius = 0.165f;
        private const float HullFlatten = 0.9f;
        private const float NoseSeam = 0.30f;

        // Engines: capsules beside the rear of the hull, slung under the wings.
        private const float EngineX = 0.205f;
        private const float EngineY = -0.075f;

        /// <summary>A new ship mesh (the caller owns and destroys it).</summary>
        public static Mesh Create()
        {
            var b = new Builder();
            AddHull(b);
            b.Ellipsoid(new Vector3(0f, 0.115f, 0.09f), new Vector3(0.088f, 0.088f, 0.175f), 32, 18, Canopy, Glass);
            b.Occluder(new Vector3(0f, 0.115f, 0.02f), 0.075f);
            b.Occluder(new Vector3(0f, 0.115f, 0.15f), 0.075f);
            foreach (var side in new[] { -1f, 1f })
            {
                AddWing(b, side);
                AddEngine(b, side);
                AddFin(b, side);
            }
            b.BakeOcclusion();
            return b.ToMesh("Spaceship");
        }

        // The hull's radius at z (before flattening): a teardrop, fullest a little ahead of the middle, with a round,
        // blunt nose and a tail that tapers between the engines; both ends close in round caps.
        private static float HullRadiusAt(float z)
        {
            var x = Mathf.Clamp((2f * z - (HullNose + HullTail)) / (HullNose - HullTail), -1f, 1f);
            var power = 0.5f + 0.25f * Mathf.Max(-x, 0f) + 0.08f * Mathf.Max(x, 0f);
            return HullRadius * Mathf.Pow(Mathf.Max(1f - x * x, 0f), power) * (1f + 0.12f * x);
        }

        private static void AddHull(Builder b)
        {
            var profile = new List<Ring>();
            const int steps = 40;
            for (var i = 0; i <= steps; ++i)
            {
                // Cosine spacing: more rings toward the round caps at both ends.
                var z = Mathf.Lerp(HullTail, HullNose, 0.5f - 0.5f * Mathf.Cos(Mathf.PI * i / steps));
                if (profile.Count > 0 && profile[profile.Count - 1].Z < NoseSeam && z >= NoseSeam)
                {
                    // The paint changes on a ring of its own, so the coral nose has a clean edge.
                    profile.Add(new Ring(NoseSeam, HullRadiusAt(NoseSeam), Hull));
                    profile.Add(new Ring(NoseSeam, HullRadiusAt(NoseSeam), Accent));
                }
                var r = i == 0 || i == steps ? 0f : HullRadiusAt(z);
                profile.Add(new Ring(z, r, z >= NoseSeam ? Accent : Hull));
            }
            b.Lathe(profile, Vector3.zero, new Vector2(1f, HullFlatten), 40);
            for (var z = HullTail + 0.06f; z < HullNose - 0.05f; z += 0.04f)
            {
                b.Occluder(new Vector3(0f, 0f, z), 0.85f * HullFlatten * HullRadiusAt(z));
            }
        }

        // A wing: thick near the hull, swept back with a little dihedral, and a rounded tip.
        private static void AddWing(Builder b, float side)
        {
            Section At(float s) => Builder.Plate(
                new Vector3(side * Mathf.Lerp(0.08f, 0.47f, s), Mathf.Lerp(-0.035f, 0.012f, s), Mathf.Lerp(-0.06f, -0.20f, s)),
                new Vector3(side * 0.39f, 0.047f, -0.14f), Vector3.up, Mathf.Lerp(0.17f, 0.085f, s),
                Mathf.Lerp(0.042f, 0.026f, s),
                Builder.RoundTip(s, 0.68f));
            b.Loft(At, 22, 36, Accent);
            for (var s = 0f; s <= 0.85f; s += 0.12f)
            {
                var section = At(s);
                foreach (var u in new[] { -0.55f, 0f, 0.55f })
                {
                    b.Occluder(section.Center + section.Chord * (u * section.HalfChord), section.HalfThick);
                }
            }
        }

        // An engine: a capsule with a round coral intake at the front and, at the back, a dark bell around a
        // recessed nozzle that glows.
        private static void AddEngine(Builder b, float side)
        {
            const float radius = 0.080f;
            const float front = 0.10f;
            var profile = new List<Ring>
            {
                new Ring(-0.485f, 0.000f, Nozzle, 1f),
                new Ring(-0.485f, 0.046f, Nozzle, 1f),
                new Ring(-0.497f, 0.054f, Trim),
                new Ring(-0.500f, 0.064f, Trim),
                new Ring(-0.496f, 0.073f, Trim),
                new Ring(-0.484f, radius - 0.001f, Trim),
                new Ring(-0.455f, radius, Trim),
                new Ring(-0.430f, radius - 0.002f, Trim),
                new Ring(-0.430f, radius - 0.002f, Hull),
                new Ring(-0.400f, radius - 0.003f, Hull),
                new Ring(-0.250f, radius, Hull),
                new Ring(-0.050f, radius, Hull),
                new Ring(0.000f, radius, Hull),
                new Ring(0.000f, radius, Accent),
            };
            // A round cap at the front.
            const int capSteps = 9;
            for (var i = 1; i <= capSteps; ++i)
            {
                var angle = 0.5f * Mathf.PI * i / capSteps;
                profile.Add(new Ring(front * Mathf.Sin(angle), i == capSteps ? 0f : radius * Mathf.Cos(angle), Accent));
            }
            var axis = new Vector3(side * EngineX, EngineY, 0f);
            b.Lathe(profile, axis, Vector2.one, 32);
            for (var z = -0.46f; z <= 0.06f; z += 0.04f)
            {
                b.Occluder(axis + new Vector3(0f, 0f, z), 0.9f * radius);
            }
        }

        // A tail fin on top of each engine, canted outward, with a rounded tip.
        private static void AddFin(Builder b, float side)
        {
            Section At(float s) => Builder.Plate(
                new Vector3(side * Mathf.Lerp(EngineX - 0.005f, EngineX + 0.075f, s), Mathf.Lerp(-0.02f, 0.17f, s),
                            Mathf.Lerp(-0.34f, -0.42f, s)),
                new Vector3(side * 0.075f, 0.19f, -0.08f), Vector3.right, Mathf.Lerp(0.10f, 0.055f, s),
                Mathf.Lerp(0.024f, 0.017f, s),
                Builder.RoundTip(s, 0.62f));
            b.Loft(At, 16, 28, Accent);
            for (var s = 0.1f; s <= 0.8f; s += 0.2f)
            {
                var section = At(s);
                b.Occluder(section.Center + section.Chord * (0.4f * section.HalfChord), section.HalfThick);
                b.Occluder(section.Center - section.Chord * (0.4f * section.HalfChord), section.HalfThick);
            }
        }

        /// <summary>A ring of a body of revolution: its place along the axis, its radius, paint and glow.</summary>
        private readonly struct Ring
        {
            public readonly float Z;
            public readonly float R;
            public readonly Color Paint;
            public readonly float Glow;

            public Ring(float z, float r, Color paint, float glow = 0f)
            {
                Z = z;
                R = r;
                Paint = paint;
                Glow = glow;
            }
        }

        /// <summary>
        /// A cross-section of a plate (a wing or a fin): its center, the unit directions of the chord (toward the
        /// leading edge) and of the thickness, the half chord and half thickness, and the span direction.
        /// </summary>
        private struct Section
        {
            public Vector3 Center;
            public Vector3 Chord;
            public Vector3 Thick;
            public Vector3 Span;
            public float HalfChord;
            public float HalfThick;
        }

        private sealed class Builder
        {
            private readonly List<Vector3> positions = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Color> colors = new List<Color>();
            private readonly List<Vector2> surface = new List<Vector2>();
            private readonly List<int> partOf = new List<int>();
            private readonly List<int> triangles = new List<int>();
            private readonly List<(Vector3 Center, float Radius, int Part)> occluders =
                new List<(Vector3 Center, float Radius, int Part)>();
            private int part = -1;

            // The baked occlusion never darkens a visible surface below this, so meeting parts get a soft shade, not
            // an ink line (the Adventure look has none).
            private const float MinOpen = 0.4f;

            private int Add(Vector3 position, Vector3 normal, Color color, float glow, float finish)
            {
                positions.Add(position);
                normals.Add(normal.normalized);
                colors.Add(new Color(color.r, color.g, color.b, glow));
                surface.Add(new Vector2(1f, finish));
                partOf.Add(part);
                return positions.Count - 1;
            }

            /// <summary>A sphere that stands in for the current part when the occlusion of the others is baked.</summary>
            public void Occluder(Vector3 center, float radius) => occluders.Add((center, radius, part));

            // A triangle wound clockwise as seen from the side its normals face (Unity's front faces):
            // Vector3.Cross(b - a, c - a) points toward the side from which a, b, c run clockwise.
            private void Triangle(int a, int b, int c)
            {
                var face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                var normal = normals[a] + normals[b] + normals[c];
                triangles.Add(a);
                if (Vector3.Dot(face, normal) >= 0f)
                {
                    triangles.Add(b);
                    triangles.Add(c);
                }
                else
                {
                    triangles.Add(c);
                    triangles.Add(b);
                }
            }

            // Triangles over a grid of rows by columns of vertices from `first`, row by row.
            private void Grid(int first, int rows, int columns)
            {
                for (var r = 0; r + 1 < rows; ++r)
                {
                    for (var c = 0; c + 1 < columns; ++c)
                    {
                        var a = first + r * columns + c;
                        var below = a + columns;
                        Triangle(a, a + 1, below);
                        Triangle(a + 1, below + 1, below);
                    }
                }
            }

            /// <summary>
            /// A body of revolution about the z axis through <paramref name="center"/>, as a new part. The profile
            /// runs from the back to the front (it may turn back, as into a nozzle) and ends on the axis at both ends;
            /// the radius is scaled across by <paramref name="scale"/> (x, y). Two rings at the same place change the
            /// paint along a clean edge.
            /// </summary>
            public void Lathe(IReadOnlyList<Ring> profile, Vector3 center, Vector2 scale, int segments)
            {
                ++part;
                var first = positions.Count;
                for (var i = 0; i < profile.Count; ++i)
                {
                    // The profile's slope, from the nearest rings at other places, tilts the normal along z.
                    var previous = i;
                    while (previous > 0 && Same(profile[previous], profile[i]))
                    {
                        --previous;
                    }
                    var next = i;
                    while (next < profile.Count - 1 && Same(profile[next], profile[i]))
                    {
                        ++next;
                    }
                    var tangent = new Vector2(profile[next].Z - profile[previous].Z, profile[next].R - profile[previous].R).normalized;
                    for (var s = 0; s <= segments; ++s)
                    {
                        var angle = 2f * Mathf.PI * s / segments;
                        var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                        var position = center + new Vector3(direction.x * profile[i].R * scale.x,
                                                            direction.y * profile[i].R * scale.y, profile[i].Z);
                        var normal = new Vector3(direction.x / scale.x * tangent.x, direction.y / scale.y * tangent.x, -tangent.y);
                        if (profile[i].R <= 0f)
                        {
                            normal = new Vector3(0f, 0f, i == 0 ? -1f : 1f);
                        }
                        Add(position, normal, profile[i].Paint, profile[i].Glow, 0f);
                    }
                }
                Grid(first, profile.Count, segments + 1);
            }

            private static bool Same(Ring a, Ring b) => Mathf.Approximately(a.Z, b.Z) && Mathf.Approximately(a.R, b.R);

            /// <summary>An ellipsoid centered at <paramref name="center"/> with semi-axes <paramref name="radii"/>, as a new part.</summary>
            public void Ellipsoid(Vector3 center, Vector3 radii, int segments, int rings, Color color, float finish)
            {
                ++part;
                var first = positions.Count;
                for (var r = 0; r <= rings; ++r)
                {
                    var polar = Mathf.PI * r / rings;
                    for (var s = 0; s <= segments; ++s)
                    {
                        var azimuth = 2f * Mathf.PI * s / segments;
                        var unit = new Vector3(Mathf.Sin(polar) * Mathf.Cos(azimuth), Mathf.Cos(polar),
                                               Mathf.Sin(polar) * Mathf.Sin(azimuth));
                        var normal = new Vector3(unit.x / radii.x, unit.y / radii.y, unit.z / radii.z);
                        Add(center + Vector3.Scale(unit, radii), normal, color, 0f, finish);
                    }
                }
                Grid(first, rings + 1, segments + 1);
            }

            /// <summary>
            /// The cross-section of a plate with its center at <paramref name="center"/>, spanning along
            /// <paramref name="span"/>, thick toward <paramref name="across"/> as near as the span allows and with its
            /// chord toward +z, scaled by <paramref name="tip"/> (1 along the plate, falling to 0 at a rounded tip).
            /// </summary>
            public static Section Plate(Vector3 center, Vector3 span, Vector3 across, float halfChord, float halfThick,
                                        float tip)
            {
                var along = span.normalized;
                var thick = across - Vector3.Dot(across, along) * along;
                thick.Normalize();
                var chord = Vector3.Cross(thick, along);
                if (Vector3.Dot(chord, Vector3.forward) < 0f)
                {
                    chord = -chord;
                }
                return new Section
                {
                    Center = center,
                    Chord = chord,
                    Thick = thick,
                    Span = along,
                    HalfChord = halfChord * tip,
                    HalfThick = halfThick * tip,
                };
            }

            /// <summary>1 up to <paramref name="from"/> along a plate, then a quarter ellipse to 0 at its end.</summary>
            public static float RoundTip(float s, float from)
            {
                var k = Mathf.Clamp01((s - from) / (1f - from));
                return Mathf.Sqrt(Mathf.Max(1f - k * k, 0f));
            }

            // A point of a plate's surface: a rounded slab (a superellipse) that is thickest toward the leading edge.
            private static Vector3 PlatePoint(Func<float, Section> section, float s, float angle)
            {
                var c = section(Mathf.Clamp01(s));
                var cos = Mathf.Cos(angle);
                var sin = Mathf.Sin(angle);
                const float roundness = 0.8f; // 1 is an ellipse; lower is fuller, toward a slab with round edges
                var a = Mathf.Sign(cos) * Mathf.Pow(Mathf.Abs(cos), roundness);
                var b = Mathf.Sign(sin) * Mathf.Pow(Mathf.Abs(sin), roundness);
                var airfoil = Mathf.Lerp(0.7f, 1f, 0.5f + 0.5f * cos);
                return c.Center + c.Chord * (a * c.HalfChord) + c.Thick * (b * c.HalfThick * airfoil);
            }

            /// <summary>
            /// A plate (a wing or a fin) as a new part: cross-sections from <paramref name="section"/> along its span
            /// (0 at the root, inside the part it grows from, to 1 at the rounded tip), with smooth normals.
            /// </summary>
            public void Loft(Func<float, Section> section, int steps, int segments, Color color)
            {
                ++part;
                var first = positions.Count;
                const float h = 1e-3f;
                for (var j = 0; j <= steps; ++j)
                {
                    // More sections toward the tip, where the plate rounds off.
                    var s = Mathf.Sin(0.5f * Mathf.PI * j / steps);
                    var c = section(s);
                    for (var k = 0; k <= segments; ++k)
                    {
                        var angle = 2f * Mathf.PI * k / segments;
                        var position = PlatePoint(section, s, angle);
                        Vector3 normal;
                        if (j == steps)
                        {
                            normal = c.Span;
                        }
                        else
                        {
                            // Unit tangents first: the differences are tiny, below what Vector3.normalized handles.
                            var around = PlatePoint(section, s, angle + h) - PlatePoint(section, s, angle - h);
                            var along = PlatePoint(section, s + h, angle) - PlatePoint(section, s - h, angle);
                            normal = Vector3.Cross(around / around.magnitude, along / along.magnitude);
                            normal /= normal.magnitude;
                            if (Vector3.Dot(normal, position - c.Center) < 0f)
                            {
                                normal = -normal;
                            }
                        }
                        Add(position, normal, color, 0f, 0f);
                    }
                }
                Grid(first, steps + 1, segments + 1);
            }

            /// <summary>
            /// Bakes how open each vertex is into the first texture coordinate's x: the occlusion by the other parts'
            /// stand-in spheres (Quilez's sphere occlusion, with his approximation where a sphere crosses the horizon),
            /// so the surface darkens softly where parts meet.
            /// </summary>
            public void BakeOcclusion()
            {
                for (var i = 0; i < positions.Count; ++i)
                {
                    var open = 1f;
                    foreach (var (center, radius, owner) in occluders)
                    {
                        if (owner != partOf[i])
                        {
                            open *= 1f - SphereOcclusion(positions[i], normals[i], center, radius);
                        }
                    }
                    surface[i] = new Vector2(Mathf.Lerp(MinOpen, 1f, open), surface[i].y);
                }
            }

            private static float SphereOcclusion(Vector3 position, Vector3 normal, Vector3 center, float radius)
            {
                var toCenter = center - position;
                var distance = toCenter.magnitude;
                var h = distance / radius;
                if (h <= 1f)
                {
                    return 1f;
                }
                var nl = Vector3.Dot(normal, toCenter / distance);
                var h2 = h * h;
                if (1f - h2 * nl * nl > 1e-3f)
                {
                    var partial = (nl * h + 1f) / h2;
                    return Mathf.Clamp01(0.33f * partial * partial);
                }
                return Mathf.Clamp01(Mathf.Max(nl, 0f) / h2);
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(positions);
                mesh.SetNormals(normals);
                mesh.SetColors(colors);
                mesh.SetUVs(0, surface);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
