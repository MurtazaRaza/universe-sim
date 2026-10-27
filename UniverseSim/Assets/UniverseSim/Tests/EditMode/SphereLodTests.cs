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
    }
}
