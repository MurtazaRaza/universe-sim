using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UniverseSim.Unity.Native;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace UniverseSim.Unity.Tests.EditMode
{
    /// <summary>
    /// Surface maps (P6-07): the mapped sphere puts each vertex's planetocentric longitude and latitude into its
    /// UV, in the body-fixed axes the core maps to the host (so the pipeline's maps land where the orientation
    /// says); the manifest lookup; and the import settings and attribution of the maps in the project.
    /// </summary>
    public class SurfaceMapTests
    {
        private const string Folder = "Assets/UniverseSim/Textures/Bodies";

        // The planetocentric longitude and latitude, radians, of the point a mesh vertex becomes once scaled by
        // the radii: the vertex back in the core's axes (the core's inverse mapping), then scaled.
        private static (double Longitude, double Latitude) Planetocentric(Vector3 vertex, double a, double b, double c)
        {
            var core = CoreSimulation.AxesFromHost(NativeConstants.AxesUnity,
                new UsimVec3d { X = vertex.x, Y = vertex.y, Z = vertex.z });
            double x = a * core.X, y = b * core.Y, z = c * core.Z;
            return (Math.Atan2(y, x), Math.Atan2(z, Math.Sqrt(x * x + y * y)));
        }

        [TestCase(1.0, 1.0, 1.0)]
        [TestCase(3.0, 2.0, 1.0)] // triaxial: the scaled point's longitude differs from the mesh parameter
        public void MappedSphereUvsArePlanetocentricLongitudeAndLatitude(double a, double b, double c)
        {
            const int segments = 32, rings = 16;
            var mesh = SphereMesh.CreateMapped(segments, rings, a, b, c, NativeConstants.AxesUnity);
            var vertices = mesh.vertices;
            var uv = mesh.uv;
            for (var r = 1; r < rings; ++r) // the poles have no longitude
            {
                for (var s = 1; s < segments; ++s) // the seam columns are pinned to u = 0 and 1
                {
                    var i = r * (segments + 1) + s;
                    var (longitude, latitude) = Planetocentric(vertices[i], a, b, c);
                    Assert.That(uv[i].x, Is.EqualTo((longitude + Math.PI) / (2 * Math.PI)).Within(1e-5), $"u of vertex {i}");
                    Assert.That(uv[i].y, Is.EqualTo((latitude + Math.PI / 2) / Math.PI).Within(1e-5), $"v of vertex {i}");
                }
            }
            Assert.That(uv[0].y, Is.EqualTo(1f).Within(1e-6)); // the first ring is the north pole
            Assert.That(uv[uv.Length - 1].y, Is.EqualTo(0f).Within(1e-6));
        }

        [Test]
        public void TheMapCenterFacesTheBodysPrimeMeridianInTheCoresAxes()
        {
            // u = 0.5, v = 0.5 is longitude 0, latitude 0: body-fixed +X as the core maps it to the host.
            const int segments = 32, rings = 16;
            var mesh = SphereMesh.CreateMapped(segments, rings, 1.0, 1.0, 1.0, NativeConstants.AxesUnity);
            var center = (rings / 2) * (segments + 1) + segments / 2;
            var x = CoreSimulation.AxesToHost(NativeConstants.AxesUnity, new UsimVec3d { X = 1.0 });
            Assert.That(mesh.uv[center].x, Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(mesh.uv[center].y, Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(Vector3.Distance(mesh.vertices[center], new Vector3((float)x.X, (float)x.Y, (float)x.Z)), Is.LessThan(1e-6f));
            // 90 degrees east (u = 0.75) is body-fixed +Y.
            var east = (rings / 2) * (segments + 1) + 3 * segments / 4;
            var y = CoreSimulation.AxesToHost(NativeConstants.AxesUnity, new UsimVec3d { Y = 1.0 });
            Assert.That(mesh.uv[east].x, Is.EqualTo(0.75f).Within(1e-6));
            Assert.That(Vector3.Distance(mesh.vertices[east], new Vector3((float)y.X, (float)y.Y, (float)y.Z)), Is.LessThan(1e-6f));
        }

        [Test]
        public void SurfaceMapsAreFoundByNaifIdWhenTheirTextureIsPresent()
        {
            var texture = new Texture2D(4, 2) { name = "body-1001" };
            try
            {
                const string json = "{\"textures\":[" +
                    "{\"naifId\":1001,\"name\":\"Test\",\"file\":\"body-1001.jpg\",\"content\":\"c\",\"credit\":\"k\",\"sourceId\":\"S\",\"page\":\"p\"}," +
                    "{\"naifId\":1002,\"name\":\"Gone\",\"file\":\"body-1002.jpg\",\"content\":\"c\",\"credit\":\"k\",\"sourceId\":\"S\",\"page\":\"p\"}]}";
                LogAssert.Expect(LogType.Warning, "UniverseSim: the surface map body-1002.jpg of Gone is missing");
                var maps = SurfaceMaps.Parse(json, new[] { texture });
                Assert.That(maps.Count, Is.EqualTo(1));
                Assert.That(maps.TryGet(UsimBodyId.Naif(1001), out var info, out var found), Is.True);
                Assert.That(found, Is.SameAs(texture));
                Assert.That(info.credit, Is.EqualTo("k"));
                Assert.That(maps.TryGet(UsimBodyId.Naif(1002), out _, out _), Is.False);
                var other = new UsimBodyId { IdNamespace = NativeConstants.IdNamespaceNaif + 1, Value = 1001 };
                Assert.That(maps.TryGet(other, out _, out _), Is.False);
                Assert.That(SurfaceMaps.Parse(null, null).Count, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void TheProjectsMapsAreListedAttributedAndImportedForSphericalMapping()
        {
            var manifest = AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "/BodyTextures.json");
            if (manifest == null)
            {
                Assert.Ignore("No surface maps yet: run tools/scripts/fetch-texture-sources.sh (usim-pipeline textures).");
            }
            var entries = SurfaceMaps.ReadManifest(manifest.text);
            Assert.That(entries, Is.Not.Empty);
            var attribution = File.ReadAllText("Assets/UniverseSim/ATTRIBUTION.md");
            var onDisk = Directory.GetFiles(Folder, "*.jpg").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal);
            Assert.That(onDisk, Is.EqualTo(entries.Select(e => e.file).OrderBy(n => n, StringComparer.Ordinal)));
            foreach (var entry in entries)
            {
                Assert.That(attribution, Does.Contain($"`Textures/Bodies/{entry.file}`"), entry.file);
                var importer = (TextureImporter)AssetImporter.GetAtPath($"{Folder}/{entry.file}");
                Assert.That(importer.sRGBTexture && importer.mipmapEnabled, Is.True, entry.file);
                Assert.That(importer.wrapModeU, Is.EqualTo(TextureWrapMode.Repeat), entry.file);
                Assert.That(importer.wrapModeV, Is.EqualTo(TextureWrapMode.Clamp), entry.file);
                Assert.That(importer.maxTextureSize, Is.EqualTo(2048), entry.file);
            }
        }
    }
}
