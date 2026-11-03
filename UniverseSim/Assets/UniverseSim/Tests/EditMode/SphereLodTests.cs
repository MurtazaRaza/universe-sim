using NUnit.Framework;
using UniverseSim.Unity.Native;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniverseSim.Unity.Tests.EditMode
{
    /// <summary>
    /// The spheres' levels of detail (SphereLods): the level chosen for a size on screen keeps the outline
    /// within the tolerance, small spheres get the coarsest level, a sphere at a limit does not flicker
    /// between two levels, and every level's mesh has 16-bit indices.
    /// </summary>
    public class SphereLodTests
    {
        private const float Tolerance = 0.5f;
        private static int Coarsest => SphereLods.Segments.Length - 1;

        // How far the outline of `segments` segments strays from a circle of `pixelRadius`, pixels.
        private static float OutlineError(float pixelRadius, int segments) =>
            pixelRadius * (1f - Mathf.Cos(Mathf.PI / segments));

        [Test]
        public void TheChosenLevelIsTheCoarsestThatKeepsTheOutline()
        {
            for (var pixelRadius = 0.01f; pixelRadius < 5000f; pixelRadius *= 1.07f)
            {
                var level = SphereLods.SelectLevel(pixelRadius, Tolerance);
                if (level > 0)
                {
                    Assert.That(OutlineError(pixelRadius, SphereLods.Segments[level]), Is.LessThanOrEqualTo(Tolerance * 1.0001f),
                                $"{pixelRadius} px at level {level}");
                }
                if (level < Coarsest)
                {
                    Assert.That(OutlineError(pixelRadius, SphereLods.Segments[level + 1]), Is.GreaterThan(Tolerance),
                                $"{pixelRadius} px could use level {level + 1}");
                }
            }
        }

        [Test]
        public void DotsGetTheCoarsestLevelAndCloseViewsTheFinest()
        {
            Assert.That(SphereLods.SelectLevel(0.1f, Tolerance), Is.EqualTo(Coarsest));
            Assert.That(SphereLods.SelectLevel(5000f, Tolerance), Is.EqualTo(0));
            Assert.That(SphereLods.SelectLevel(float.PositiveInfinity, Tolerance), Is.EqualTo(0)); // camera inside
            Assert.That(SphereLods.SelectLevel(float.NaN, Tolerance), Is.EqualTo(0));
            Assert.That(SphereLods.PixelRadius(1f, 0.5f, 1000f), Is.EqualTo(float.PositiveInfinity));
            Assert.That(SphereLods.PixelRadius(1f, 1000f, 1000f), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void ASphereAtALimitDoesNotFlicker()
        {
            for (var level = 1; level < SphereLods.Segments.Length; ++level)
            {
                var limit = SphereLods.MaxPixelRadius(SphereLods.Segments[level], Tolerance);
                // From the finer level: just inside the coarser level's limit is not enough to switch...
                Assert.That(SphereLods.SelectLevel(limit * 0.95f, Tolerance, level - 1), Is.EqualTo(level - 1));
                // ...clearly inside is.
                Assert.That(SphereLods.SelectLevel(limit * SphereLods.Hysteresis * 0.99f, Tolerance, level - 1),
                            Is.GreaterThanOrEqualTo(level));
                // Growing past the limit switches to the finer level at once.
                Assert.That(SphereLods.SelectLevel(limit * 1.01f, Tolerance, level), Is.LessThan(level));
            }
        }

        [Test]
        public void EveryLevelHasSixteenBitIndices()
        {
            foreach (var segments in SphereLods.Segments)
            {
                var plain = SphereMesh.Create(segments, segments / 2);
                var mapped = SphereMesh.CreateMapped(segments, segments / 2, 3.0, 2.0, 1.0, NativeConstants.AxesUnity);
                foreach (var mesh in new[] { plain, mapped })
                {
                    Assert.That(mesh.indexFormat, Is.EqualTo(IndexFormat.UInt16));
                    Assert.That(mesh.GetIndexCount(0), Is.EqualTo(6u * (uint)(segments * (segments / 2))));
                    Object.DestroyImmediate(mesh);
                }
            }
        }

        // Where a ray from the center along `p` leaves the mesh, found by testing every triangle (Moller-Trumbore).
        private static double BruteForceFraction(Mesh mesh, Double3 p)
        {
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            var best = double.NaN;
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var v0 = Double3.From(vertices[triangles[i]]);
                var e1 = Double3.From(vertices[triangles[i + 1]]) - v0;
                var e2 = Double3.From(vertices[triangles[i + 2]]) - v0;
                var h = new Double3(p.Y * e2.Z - p.Z * e2.Y, p.Z * e2.X - p.X * e2.Z, p.X * e2.Y - p.Y * e2.X);
                var det = e1.Dot(h);
                if (System.Math.Abs(det) < 1e-15)
                {
                    continue;
                }
                var s = -v0; // the ray starts at the center
                var u = s.Dot(h) / det;
                var q = new Double3(s.Y * e1.Z - s.Z * e1.Y, s.Z * e1.X - s.X * e1.Z, s.X * e1.Y - s.Y * e1.X);
                var v = p.Dot(q) / det;
                const double edge = 1e-9;
                if (u < -edge || v < -edge || u + v > 1.0 + edge)
                {
                    continue;
                }
                var t = e2.Dot(q) / det;
                if (t > 0.0 && (double.IsNaN(best) || t < best))
                {
                    best = t;
                }
            }
            return best;
        }

        [Test]
        public void TheDrawnSurfaceDepthMatchesTheMesh()
        {
            // The fraction of the radius where the drawn sphere meets a ray (ADR-0017, the landed ship), against the
            // meshes themselves, for both kinds of mesh and several levels, in deterministic random directions.
            var random = new System.Random(20261102);
            foreach (var segments in new[] { 8, 32, 128 })
            {
                var plain = SphereMesh.Create(segments, segments / 2);
                var mapped = SphereMesh.CreateMapped(segments, segments / 2, 1.0, 1.0, 1.0, NativeConstants.AxesUnity);
                foreach (var (mesh, isMapped) in new[] { (plain, false), (mapped, true) })
                {
                    var smallest = 1.0;
                    for (var k = 0; k < 200; ++k)
                    {
                        var p = new Double3(random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1,
                                            random.NextDouble() * 2 - 1);
                        if (p.Length < 0.1)
                        {
                            continue;
                        }
                        p = p.Normalized;
                        var expected = BruteForceFraction(mesh, p);
                        var actual = SphereMesh.DrawnRadiusFraction(p, segments, isMapped, NativeConstants.AxesUnity);
                        Assert.That(actual, Is.EqualTo(expected).Within(2e-6),
                                    $"{(isMapped ? "mapped" : "plain")} sphere of {segments} segments, direction {p.X}, {p.Y}, {p.Z}");
                        smallest = System.Math.Min(smallest, actual);
                    }
                    // Inside the sphere between the vertices, never deeper than a facet's center, about cos^2 of half
                    // the grid step (rings and segments step by the same angle).
                    Assert.That(smallest, Is.LessThan(1.0));
                    Assert.That(smallest, Is.GreaterThan(System.Math.Pow(System.Math.Cos(System.Math.PI / segments), 2) - 0.01));
                    // At a vertex the drawn surface is the sphere itself.
                    var vertex = Double3.From(mesh.vertices[(segments / 4) * (segments + 1) + 3]);
                    Assert.That(SphereMesh.DrawnRadiusFraction(vertex, segments, isMapped, NativeConstants.AxesUnity),
                                Is.EqualTo(1.0).Within(1e-6));
                    Object.DestroyImmediate(mesh);
                }
            }
        }
    }
}
