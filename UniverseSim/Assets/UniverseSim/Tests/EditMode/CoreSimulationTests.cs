using System;
using System.Linq;
using NUnit.Framework;
using UniverseSim.Unity.Native;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace UniverseSim.Unity.Tests.EditMode
{
    /// <summary>
    /// The managed binding (P5-01) on the default pack: catalog with provenance, time, errors as exceptions,
    /// and per-frame calls without managed allocations (NFR-03).
    /// </summary>
    public class CoreSimulationTests
    {
        private CoreSimulation sim;

        [SetUp]
        public void Load()
        {
            var path = SimulationHost.FindPack(string.Empty, preferMoons: false);
            if (string.IsNullOrEmpty(path))
            {
                Assert.Ignore("no default pack (run git lfs pull)");
            }
            sim = CoreSimulation.LoadFile(path);
        }

        [TearDown]
        public void Release() => sim?.Dispose();

        [Test]
        public void CatalogCarriesValuesWithProvenance()
        {
            var earth = sim.IndexOf(UsimBodyId.Naif(399));
            Assert.That(earth, NUnit.Framework.Is.GreaterThanOrEqualTo(0));
            Assert.That(sim.Bodies[earth].Name, NUnit.Framework.Is.EqualTo("Earth"));
            Assert.That(sim.Bodies[earth].HasRotation);
            var gm = sim.Quantity(earth, NativeConstants.FieldGm);
            Assert.That(gm.HasValue);
            Assert.That(gm.UnitSymbol, NUnit.Framework.Is.EqualTo("km^3/s^2"));
            StringAssert.StartsWith("S-", gm.SourceId);
            StringAssert.Contains("BODY399_GM", gm.Locator);
            Assert.That(sim.Sources.Any(s => s.Id == gm.SourceId));

            var mars = sim.Bodies[sim.IndexOf(UsimBodyId.Naif(499))];
            Assert.That(mars.IsApproximate);
            StringAssert.Contains("ADR-0008", mars.ApproximationReason);
            Assert.That(sim.Bodies[sim.IndexOf(UsimBodyId.Naif(0))].HasMeanRadius, NUnit.Framework.Is.False);
        }

        [Test]
        public void TimeParsesFormatsAndConvertsUtc()
        {
            Assert.That(sim.TryParseTime("2000-01-01T12:00:00 TDB", out var j2000, out _));
            Assert.That(j2000.Days, NUnit.Framework.Is.EqualTo(0));
            Assert.That(sim.TryFormatTime(j2000, NativeConstants.ScaleTdb, 0, out var text));
            Assert.That(text, NUnit.Framework.Is.EqualTo("2000-01-01T12:00:00 TDB"));
            var fromUtc = sim.FromUtc(new DateTime(2000, 1, 1, 11, 58, 55, DateTimeKind.Utc));
            Assert.That(sim.TryFormatTime(fromUtc, NativeConstants.ScaleUtc, 0, out var utc));
            Assert.That(utc, NUnit.Framework.Is.EqualTo("2000-01-01T11:58:55 UTC"));
            Assert.That(sim.TryParseTime("yesterday", out _, out var error), NUnit.Framework.Is.False);
            Assert.That(error, NUnit.Framework.Is.Not.Empty);
            Assert.That(sim.TryParseTime("1960-01-01T00:00:00 TDB", out var early, out _));
            Assert.That(sim.TryFormatTime(early, NativeConstants.ScaleUtc, 0, out _), NUnit.Framework.Is.False); // UTC from 1972
        }

        [Test]
        public void FailuresBecomeExceptionsWithTheCoreMessage()
        {
            var e = Assert.Throws<CoreException>(() =>
                sim.State(UsimBodyId.Naif(399), UsimBodyId.Naif(4242), UsimFrame.Icrf, sim.Clock.Now));
            Assert.That(e.Status, NUnit.Framework.Is.EqualTo(Status.NotFound));
            StringAssert.Contains("naif:4242", e.Message);
            Assert.Throws<CoreException>(() => CoreSimulation.LoadFile("/no/such/pack.uspack"));
        }

        [Test]
        public void AxesRoundTripThroughTheCore()
        {
            var host = CoreSimulation.AxesToHost(NativeConstants.AxesUnity, new UsimVec3d { X = 1, Y = 2, Z = 3 });
            Assert.That((host.X, host.Y, host.Z), NUnit.Framework.Is.EqualTo((1.0, 3.0, 2.0)));
            var core = CoreSimulation.AxesFromHost(NativeConstants.AxesUnity, host);
            Assert.That((core.X, core.Y, core.Z), NUnit.Framework.Is.EqualTo((1.0, 2.0, 3.0)));
        }

        [Test]
        public void PerFrameCallsAllocateNoManagedMemory()
        {
            var request = new UsimSnapshotRequest
            {
                Center = UsimBodyId.Naif(399),
                Frame = UsimFrame.EclipJ2000,
                KmPerRenderUnit = 1.0e4,
                Axes = NativeConstants.AxesUnity,
                ScaleMode = NativeConstants.ScaleModeTrue,
                BodyScale = 1.0,
                CompressionDistance = 1.0e4,
            };
            var snapshots = new UsimBodySnapshot[sim.Bodies.Count];
            var points = new UsimVec3d[16384];
            // Warm up: the first orbit path call samples and caches the path.
            sim.Advance(0.0);
            sim.FillSnapshot(ref request, snapshots);
            sim.OrbitPath(UsimBodyId.Naif(301), UsimBodyId.Naif(399), ref request, points, out _);

            // TestDelegate, not a value-returning lambda: the constraint runs the code itself.
            Assert.That(new TestDelegate(() => sim.Advance(1.0 / 60.0)), Is.Not.AllocatingGCMemory());
            Assert.That(new TestDelegate(() => sim.FillSnapshot(ref request, snapshots)), Is.Not.AllocatingGCMemory());
            Assert.That(new TestDelegate(() => sim.OrbitPath(UsimBodyId.Naif(301), UsimBodyId.Naif(399), ref request, points, out _)),
                        Is.Not.AllocatingGCMemory());
            Assert.That(snapshots.Count(s => (s.Flags & NativeConstants.SnapshotNoPosition) == 0),
                        NUnit.Framework.Is.EqualTo(sim.Bodies.Count));
        }

        [Test]
        public void StarsAndRingsCarryTheirSources()
        {
            Assert.That(sim.StarCount, NUnit.Framework.Is.GreaterThan(9000));
            Assert.That(sim.StarSource, NUnit.Framework.Is.Not.Null);
            Assert.That(sim.StarSource.Id, NUnit.Framework.Is.EqualTo("S-CDS-BSC5"));
            var request = new UsimSnapshotRequest
            {
                Center = UsimBodyId.Naif(0), Frame = UsimFrame.EclipJ2000, KmPerRenderUnit = 1.0,
                Axes = NativeConstants.AxesUnity, BodyScale = 1.0,
            };
            var stars = new UsimStar[sim.StarCount];
            Assert.That(sim.FillStars(ref request, stars), NUnit.Framework.Is.EqualTo(sim.StarCount));
            foreach (var s in stars)
            {
                var length = Math.Sqrt(s.Direction.X * s.Direction.X + s.Direction.Y * s.Direction.Y + s.Direction.Z * s.Direction.Z);
                Assert.That(length, NUnit.Framework.Is.EqualTo(1.0).Within(1e-12));
            }
            Assert.That((TestDelegate)(() => sim.FillStars(ref request, stars)), Is.Not.AllocatingGCMemory());

            var saturn = sim.IndexOf(UsimBodyId.Naif(699));
            var rings = sim.Rings.Where(r => r.BodyIndex == saturn).ToList();
            Assert.That(rings.Select(r => r.Name), Does.Contain("B Ring").And.Contain("Cassini Division").And.Contain("Encke Gap"));
            var b = rings.First(r => r.Name == "B Ring");
            Assert.That(b.RadiiSource.Id, NUnit.Framework.Is.EqualTo("S-IAU-WGPSN"));
            Assert.That(b.OpticalDepthSource.Tier, NUnit.Framework.Is.EqualTo(2));
            StringAssert.Contains("rings.json", b.RadiiLocator);
        }

        [Test]
        public void RingDisplayProfileUsesTheMiddleOfEachRangeAndEmptiesGaps()
        {
            var saturn = sim.IndexOf(UsimBodyId.Naif(699));
            var rings = sim.Rings.Where(r => r.BodyIndex == saturn).ToList();
            var b = rings.First(r => r.Name == "B Ring");
            var middle = 0.5 * (b.InnerRadiusKm + b.OuterRadiusKm);
            Assert.That(RingSystems.ProfileAt(rings, middle),
                        NUnit.Framework.Is.EqualTo(0.5 * (b.OpticalDepthSmallest + b.OpticalDepthLargest)));
            var encke = rings.First(r => r.Name == "Encke Gap");
            Assert.That(RingSystems.ProfileAt(rings, 0.5 * (encke.InnerRadiusKm + encke.OuterRadiusKm)), NUnit.Framework.Is.EqualTo(0.0));
            Assert.That(RingSystems.ProfileAt(rings, 1.0e6), NUnit.Framework.Is.EqualTo(0.0));
        }

        [Test]
        public void StarDisplayColorFollowsTheColorIndex()
        {
            var blue = StarField.DisplayColor(true, -0.3);
            var red = StarField.DisplayColor(true, 1.8);
            Assert.That(blue.b, NUnit.Framework.Is.GreaterThan(blue.r));
            Assert.That(red.r, NUnit.Framework.Is.GreaterThan(red.b));
            Assert.That(StarField.DisplayColor(false, 0.0), NUnit.Framework.Is.EqualTo(UnityEngine.Color.white));
            Assert.That(StarField.DisplayIntensity(1f), NUnit.Framework.Is.GreaterThan(StarField.DisplayIntensity(0f)));
        }
    }
}
