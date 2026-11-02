using NUnit.Framework;
using UniverseSim.Unity.Native;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace UniverseSim.Unity.Tests.EditMode
{
    /// <summary>
    /// The exploration values of the C ABI (ADR-0016, ABI 5) through the managed binding on the default pack: the
    /// binding passes them through unchanged, and the per-frame site calls allocate no managed memory. The values
    /// themselves are checked in the core's tests.
    /// </summary>
    public class ExplorationBindingTests
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

        private static UsimSnapshotRequest Request => new UsimSnapshotRequest
        {
            Center = UsimBodyId.Naif(399),
            Frame = UsimFrame.EclipJ2000,
            KmPerRenderUnit = 1.0,
            Axes = NativeConstants.AxesUnity,
            ScaleMode = NativeConstants.ScaleModeTrue,
            BodyScale = 1.0,
        };

        [Test]
        public void ATripEstimateNamesTheCenterAndTheOrbits()
        {
            var now = sim.Clock.Now;
            Assert.That(sim.TryTransferEstimate(UsimBodyId.Naif(399), UsimBodyId.Naif(499), now, out var e, out var error),
                        error);
            Assert.That(e.Center.Value, NUnit.Framework.Is.EqualTo(10));
            Assert.That(e.Departure.Value, NUnit.Framework.Is.EqualTo(399));
            Assert.That(e.Arrival.Value, NUnit.Framework.Is.EqualTo(499));
            Assert.That(e.TransferTimeS, NUnit.Framework.Is.GreaterThan(0.0));
            Assert.That(e.LightTimeS, NUnit.Framework.Is.GreaterThan(0.0));

            Assert.That(sim.TryTransferEstimate(UsimBodyId.Naif(399), UsimBodyId.Naif(399), now, out _, out error),
                        NUnit.Framework.Is.False);
            StringAssert.Contains("same body", error);
        }

        [Test]
        public void ASiteRoundTripsAndItsCallsAllocateNothing()
        {
            var request = Request;
            var now = sim.Clock.Now;
            var moon = UsimBodyId.Naif(301);
            Assert.That(sim.TrySurfaceSite(moon, 0.2, -0.7, 15.0, ref request, now, out var site));
            Assert.That(sim.TrySurfaceSiteBelow(moon, Double3.From(site.PositionKm), ref request, now, out var below));
            Assert.That(below.LatitudeRad, NUnit.Framework.Is.EqualTo(0.2).Within(1e-12));
            Assert.That(below.LongitudeRad, NUnit.Framework.Is.EqualTo(-0.7).Within(1e-12));
            Assert.That(below.AltitudeKm, NUnit.Framework.Is.EqualTo(15.0).Within(1e-9));
            Assert.That((site.Flags & NativeConstants.SiteHasGravity) != 0);
            Assert.That((site.Flags & NativeConstants.SiteHasSun) != 0);

            Assert.That(sim.TryDayLength(moon, now, out var day));
            Assert.That((day.Flags & NativeConstants.DayHasSolarDay) != 0);
            Assert.That(day.MeanSolarDayS, NUnit.Framework.Is.GreaterThan(day.SiderealRotationPeriodS));

            // The ship calls these every frame.
            var position = Double3.From(site.PositionKm);
            Assert.That(new TestDelegate(() => sim.TrySurfaceSite(moon, 0.2, -0.7, 15.0, ref request, now, out _)),
                        Is.Not.AllocatingGCMemory());
            Assert.That(new TestDelegate(() => sim.TrySurfaceSiteBelow(moon, position, ref request, now, out _)),
                        Is.Not.AllocatingGCMemory());

            // Without a radius there is no site.
            Assert.That(sim.TrySurfaceSite(UsimBodyId.Naif(3), 0.0, 0.0, 0.0, ref request, now, out _),
                        NUnit.Framework.Is.False);
        }
    }
}
