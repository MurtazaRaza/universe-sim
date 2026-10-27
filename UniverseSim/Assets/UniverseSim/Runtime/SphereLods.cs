using System;
using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>
    /// One sphere layout at decreasing detail (levels of detail), and the choice between them by the sphere's
    /// size on screen. A level is good enough while its silhouette, a polygon with <see cref="Segments"/>
    /// sides, stays within a tolerance of the true outline; a body a few pixels across then costs tens of
    /// triangles instead of the finest level's sixteen thousand. Presentation only.
    /// </summary>
    public sealed class SphereLods : IDisposable
    {
        /// <summary>Segments around the sphere at each level, finest first; each level has half as many rings.</summary>
        public static readonly int[] Segments = { 128, 64, 32, 16, 8 };

        /// <summary>
        /// A coarser level is taken only once the sphere is this fraction of that level's largest radius, so a
        /// sphere at a limit does not switch every frame.
        /// </summary>
        public const float Hysteresis = 0.8f;

        private readonly Mesh[] meshes;

        /// <summary>
        /// The levels made by <paramref name="create"/> (segments, rings), uploaded to the GPU and released from
        /// CPU memory, since nothing reads them back.
        /// </summary>
        public SphereLods(Func<int, int, Mesh> create)
        {
            meshes = new Mesh[Segments.Length];
            for (var level = 0; level < Segments.Length; ++level)
            {
                meshes[level] = create(Segments[level], Segments[level] / 2);
                meshes[level].UploadMeshData(true);
            }
        }

        /// <summary>The mesh of a level (0 is the finest).</summary>
        public Mesh this[int level] => meshes[level];

        /// <summary>
        /// The largest radius on screen, pixels, at which a sphere of <paramref name="segments"/> segments keeps
        /// its silhouette within <paramref name="tolerancePixels"/> of a circle: the polygon's edges come closest
        /// to the center, at the radius times cos(pi / segments).
        /// </summary>
        public static float MaxPixelRadius(int segments, float tolerancePixels) =>
            tolerancePixels / (1f - Mathf.Cos(Mathf.PI / segments));

        /// <summary>
        /// The level for a sphere of <paramref name="pixelRadius"/> on screen: the coarsest within
        /// <paramref name="tolerancePixels"/>, or the finest if none is (or the radius is not finite, as when
        /// the camera is inside). From <paramref name="current"/>, coarser levels need the margin of
        /// <see cref="Hysteresis"/>; finer ones are taken at once.
        /// </summary>
        public static int SelectLevel(float pixelRadius, float tolerancePixels, int current = -1)
        {
            var level = Coarsest(pixelRadius, tolerancePixels);
            if (current >= 0 && level > current)
            {
                level = Math.Max(current, Coarsest(pixelRadius / Hysteresis, tolerancePixels));
            }
            return level;
        }

        private static int Coarsest(float pixelRadius, float tolerancePixels)
        {
            for (var level = Segments.Length - 1; level > 0; --level)
            {
                if (pixelRadius <= MaxPixelRadius(Segments[level], tolerancePixels))
                {
                    return level;
                }
            }
            return 0;
        }

        /// <summary>
        /// The radius in pixels of a sphere of <paramref name="radius"/> at <paramref name="distance"/> (same
        /// units) from a perspective camera with <paramref name="focalPixels"/> (half the pixel height over the
        /// tangent of half the vertical field of view). Infinite when the camera is inside the sphere.
        /// </summary>
        public static float PixelRadius(float radius, float distance, float focalPixels) =>
            distance > radius ? radius * focalPixels / Mathf.Sqrt(distance * distance - radius * radius) : float.PositiveInfinity;

        public void Dispose()
        {
            foreach (var mesh in meshes)
            {
                UnityEngine.Object.Destroy(mesh);
            }
        }
    }
}
