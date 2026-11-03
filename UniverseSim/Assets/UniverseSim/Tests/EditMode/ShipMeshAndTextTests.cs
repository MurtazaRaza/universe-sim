using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace UniverseSim.Unity.Tests.EditMode
{
    /// <summary>The spaceship's mesh and the HUD's formatting of the core's values (ADR-0016).</summary>
    public class ShipMeshAndTextTests
    {
        [Test]
        public void TheShipMeshFacesOutwardAndOnlyItsNozzlesGlow()
        {
            var mesh = ShipMesh.Create();
            try
            {
                var vertices = mesh.vertices;
                var normals = mesh.normals;
                var colors = mesh.colors;
                var triangles = mesh.triangles;
                Assert.That(triangles.Length % 3, Is.Zero);
                Assert.That(vertices.Length, Is.EqualTo(normals.Length).And.EqualTo(colors.Length));
                // About one unit long, nose toward +z.
                Assert.That(mesh.bounds.size.z, Is.EqualTo(1f).Within(0.05f));
                Assert.That(mesh.bounds.max.z, Is.EqualTo(0.5f).Within(0.01f));
                Assert.That(normals.All(n => Mathf.Abs(n.magnitude - 1f) < 1e-4f));

                // Every triangle winds clockwise as seen from the side its normals face (Unity's front faces), so
                // back-face culling keeps the outside.
                var inconsistent = 0;
                for (var i = 0; i < triangles.Length; i += 3)
                {
                    var a = vertices[triangles[i]];
                    var face = Vector3.Cross(vertices[triangles[i + 1]] - a, vertices[triangles[i + 2]] - a);
                    if (face.sqrMagnitude < 1e-14f)
                    {
                        continue; // degenerate at a pole of a lathe
                    }
                    var normal = normals[triangles[i]] + normals[triangles[i + 1]] + normals[triangles[i + 2]];
                    if (Vector3.Dot(face, normal) <= 0f)
                    {
                        ++inconsistent;
                    }
                }
                Assert.That(inconsistent, Is.Zero, "triangles wound against their normals");

                var glowing = Enumerable.Range(0, colors.Length).Where(i => colors[i].a > 0f).ToList();
                Assert.That(glowing, Is.Not.Empty);
                Assert.That(glowing.All(i => vertices[i].z < -0.47f), "only the nozzles at the back glow");

                // The baked occlusion darkens where parts meet, softly (never to black: no ink lines), and only the
                // canopy is glass.
                var surface = mesh.uv;
                Assert.That(surface.Length, Is.EqualTo(vertices.Length));
                Assert.That(surface.All(s => s.x >= 0.4f - 1e-6f && s.x <= 1f + 1e-6f));
                Assert.That(surface.Count(s => s.x < 0.8f), Is.GreaterThan(vertices.Length / 50), "occlusion is baked");
                var glass = Enumerable.Range(0, surface.Length).Where(i => surface[i].y > 0.5f).ToList();
                Assert.That(glass, Is.Not.Empty);
                Assert.That(glass.All(i => colors[i] == new Color(ShipMesh.Canopy.r, ShipMesh.Canopy.g, ShipMesh.Canopy.b, 0f)),
                            "only the canopy is glass");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void DurationsReadInFittingUnits()
        {
            Assert.That(ShipHud.Duration(1.28), Is.EqualTo("1.3 s"));
            Assert.That(ShipHud.Duration(42.4), Is.EqualTo("42 s"));
            Assert.That(ShipHud.Duration(709.0), Is.EqualTo("11 min 49 s"));
            Assert.That(ShipHud.Duration(86164.1), Is.EqualTo("23 h 56 min"));
            Assert.That(ShipHud.Duration(29.53 * 86400.0), Is.EqualTo("29.53 days"));
            Assert.That(ShipHud.Duration(258.87 * 86400.0), Is.EqualTo("258.9 days"));
            Assert.That(ShipHud.Duration(30.6 * 365.25 * 86400.0), Is.EqualTo("30.6 years"));
            Assert.That(ShipHud.Duration(double.NaN), Is.EqualTo("-"));
            Assert.That(ShipHud.Duration(-1.0), Is.EqualTo("-"));
        }

        [Test]
        public void TemperaturesAndPressuresReadInDisplayUnits()
        {
            // Synthetic values; the core keeps kelvins and pascals (ADR-0017).
            Assert.That(ShipHud.Celsius(208.15), Is.EqualTo("-65 °C (208.15 K)"));
            Assert.That(ShipHud.Celsius(300.0), Is.EqualTo("26.9 °C (300 K)"));
            Assert.That(ShipHud.Bars(9.2e6), Is.EqualTo("92 bar"));
            Assert.That(ShipHud.Bars(1000.0), Is.EqualTo("0.01 bar"));
            Assert.That(ShipHud.Bars(1.0), Is.EqualTo("0.00001 bar"));
            Assert.That(ShipHud.Bars(100000.0), Is.EqualTo("1 bar"));
        }

        [Test]
        public void DistancesAnglesAndClockTimesRead()
        {
            Assert.That(ShipHud.Distance(17.374), Is.EqualTo("17 km"));
            Assert.That(ShipHud.Distance(2.5), Is.EqualTo("2.5 km"));
            Assert.That(ShipHud.Distance(384400.0), Is.EqualTo("384,400 km"));
            Assert.That(ShipHud.Distance(212.49e6), Is.EqualTo("212.5 million km"));
            Assert.That(ShipHud.Distance(4.35e9), Is.EqualTo("4.35 billion km"));
            Assert.That(ShipHud.Latitude(-0.5 * Mathf.PI / 2.0), Is.EqualTo("45.0° S"));
            Assert.That(ShipHud.Longitude(Mathf.PI * 2.0 / 3.0), Is.EqualTo("120.0° E"));
            Assert.That(ShipHud.Clock(14.5333), Is.EqualTo("14:31"));
            Assert.That(ShipHud.Clock(0.0), Is.EqualTo("00:00"));
            Assert.That(ShipHud.SpeedOfLight(709.0, 14.0), Is.EqualTo("about 51 times the speed of light"));
            Assert.That(ShipHud.SpeedOfLight(1.3, 8.0), Is.EqualTo("about 16 % of the speed of light"));
        }
    }
}
