using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UniverseSim.Unity.Native;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UniverseSim.Unity.Tests.PlayMode
{
    /// <summary>
    /// The spaceship (ADR-0016) in the main scene: it exists only in the stylized style, trips end where the core puts
    /// the target and show the core's estimate, hovering puts the ship at the core's surface site with the core's
    /// values in the panel, and the ship never touches the clock. With USIM_SCREENSHOT_DIR set, it saves views of the
    /// ship in a "ship" folder.
    /// </summary>
    public class ShipTests
    {
        private const string FixedTime = "2026-10-21T12:00:00 UTC";
        private StylePreference preference;
        private SimulationHost host;
        private CameraRig rig;
        private ShipPilot pilot;
        private ShipHud shipHud;
        private VisualStyleSettings settings;

        private CoreSimulation Sim => host.Simulation;

        [OneTimeSetUp]
        public void KeepPreference() => preference = StylePreference.Keep();

        [OneTimeTearDown]
        public void RestorePreference() => preference.Restore();

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            VisualStyleSettings.SavePreference(VisualStyle.Stylized);
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            host = Object.FindAnyObjectByType<SimulationHost>();
            rig = Object.FindAnyObjectByType<CameraRig>();
            pilot = Object.FindAnyObjectByType<ShipPilot>();
            shipHud = Object.FindAnyObjectByType<ShipHud>();
            settings = Object.FindAnyObjectByType<VisualStyleSettings>();
            Assert.That(host.Simulation, Is.Not.Null, host.LoadError);
            Assert.That(pilot, Is.Not.Null);
            Assert.That(shipHud, Is.Not.Null);
            Sim.SetPaused(true);
            Assert.That(Sim.TryParseTime(FixedTime, out var t, out var error), error);
            Sim.SetTime(t);
            yield return null;
        }

        private int Index(long naif) => Sim.IndexOf(UsimBodyId.Naif(naif));

        private static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; ++i)
            {
                yield return null;
            }
        }

        private static IEnumerator Seconds(float seconds)
        {
            var end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end)
            {
                yield return null;
            }
        }

        private IEnumerator StartNear(long naif, double radii)
        {
            var index = Index(naif);
            rig.JumpTo(index, Sim.Bodies[index].MaxRadiusKm * radii, 0.6, 0.3);
            rig.Select(index);
            yield return Frames(2);
            pilot.SetActive(true);
            yield return Frames(2);
            Assert.That(pilot.Active);
            Assert.That(pilot.AnchorIndex, Is.EqualTo(index));
        }

        [UnityTest]
        public IEnumerator TheShipBelongsToTheStylizedStyle()
        {
            yield return StartNear(399, 6.0);
            var model = pilot.transform.Find("Spaceship");
            Assert.That(model != null && model.gameObject.activeSelf, "the ship is drawn while out");
            Assert.That(rig.Piloted, "the ship drives the camera");
            var register = Object.FindAnyObjectByType<ExaggerationRegister>();
            var active = new System.Collections.Generic.List<Exaggeration>();
            register.CollectActive(Camera.main, active);
            Assert.That(active.Select(e => e.Subject), Does.Contain("Spaceship").And.Contain("Flights"));

            settings.SetStyle(VisualStyle.Realistic);
            yield return Frames(2);
            Assert.That(pilot.Active, Is.False, "the realistic style puts the ship away");
            Assert.That(rig.Pilot, Is.Null);
            Assert.That(model.gameObject.activeSelf, Is.False);
            pilot.SetActive(true);
            Assert.That(pilot.Active, Is.False, "and it cannot be taken out there");
            settings.SetStyle(VisualStyle.Stylized);
        }

        [UnityTest]
        public IEnumerator ATripEndsAtTheTargetAndShowsTheCoreEstimate()
        {
            yield return StartNear(399, 6.0);
            var clock = Sim.Clock;
            var earth = Index(399);
            var moon = Index(301);
            Assert.That(Sim.TryTransferEstimate(Sim.Bodies[earth].Id, Sim.Bodies[moon].Id, clock.Now, out var expected,
                                                out var error), error);

            rig.Select(moon);
            pilot.TravelTo(moon);
            Assert.That(pilot.State, Is.EqualTo(ShipPilot.Mode.Traveling));
            Assert.That(pilot.AnchorIndex, Is.EqualTo(moon), "the trip is flown relative to the target");
            Assert.That(pilot.HasTransfer);
            Assert.That(pilot.Transfer.TransferTimeS, Is.EqualTo(expected.TransferTimeS));
            Assert.That(pilot.Transfer.LightTimeS, Is.EqualTo(expected.LightTimeS));
            Assert.That((pilot.Transfer.Flags & NativeConstants.TransferFromSurface) != 0, "the Moon orbits the Earth");
            yield return Seconds(pilot.FlightSeconds * 0.5f);
            yield return Screenshot("ship-travel-moon");
            shipHud.Refresh();
            var facts = string.Join("\n", shipHud.FactTexts);
            StringAssert.Contains(ShipHud.Duration(expected.TransferTimeS), facts);
            StringAssert.Contains(ShipHud.Duration(expected.LightTimeS), facts);
            StringAssert.Contains(ShipHud.Distance(expected.DistanceKm), facts);

            yield return Seconds(pilot.FlightSeconds * 0.5f + 0.5f);
            Assert.That(pilot.State, Is.EqualTo(ShipPilot.Mode.Flying));
            var arrival = 3.0 * Sim.Bodies[moon].MaxRadiusKm;
            Assert.That(pilot.PositionKm.Length, Is.EqualTo(arrival).Within(1e-6 * arrival));
            Assert.That(rig.FocusIndex, Is.EqualTo(moon), "the view follows the ship's anchor");
            // The ship never touches the clock.
            Assert.That(Sim.Clock.Paused, Is.EqualTo(clock.Paused));
            Assert.That(Sim.Clock.Rate, Is.EqualTo(clock.Rate));
            Assert.That(Sim.Clock.Now.Days, Is.EqualTo(clock.Now.Days));
            Assert.That(Sim.Clock.Now.Seconds, Is.EqualTo(clock.Now.Seconds));
            yield return Screenshot("ship-at-moon");
        }

        [UnityTest]
        public IEnumerator OnSolidGroundTheShipLandsAtTheCoreSiteWithItsValues()
        {
            // The Moon's reference surface is solid (it has a surface pressure, ADR-0017).
            yield return StartNear(301, 1.5);
            var moon = Index(301);
            Assert.That(pilot.SurfaceKind, Is.EqualTo(NativeConstants.SurfaceSolid));
            shipHud.Refresh();
            Assert.That(shipHud.LandButtonText, Is.EqualTo("Land (L)"));
            pilot.Descend();
            Assert.That(pilot.State, Is.EqualTo(ShipPilot.Mode.Descending));
            yield return Seconds(9.5f);
            Assert.That(pilot.State, Is.EqualTo(ShipPilot.Mode.Landed));
            yield return Frames(15); // the conditions refresh five times a second
            Assert.That(pilot.HasSite);
            var site = pilot.Site;
            var request = host.Request;
            Assert.That(Sim.TrySurfaceSite(Sim.Bodies[moon].Id, site.LatitudeRad, site.LongitudeRad, pilot.AltitudeKm,
                                           ref request, Sim.Clock.Now, out var expected));
            var position = Double3.From(expected.PositionKm);
            Assert.That((pilot.PositionKm - position).Length, Is.LessThan(1e-9 * position.Length));
            // On the ground: the ship's lowest point at the reference surface.
            Assert.That(pilot.AltitudeKm, Is.EqualTo(pilot.LandedAltitudeKm).Within(1e-9));
            // The view rests on the drawn sphere, which lies inside the ellipsoid between its vertices: lowered by that
            // depth, and still about a kilometer above the ground as drawn.
            Assert.That(pilot.DrawnSurfaceDepthKm, Is.GreaterThanOrEqualTo(0.0));
            Assert.That(pilot.DrawnSurfaceDepthKm, Is.LessThan(expected.SurfaceRadiusKm * (1.0 - Math.Cos(Math.PI / 64))));
            var drawnGround = expected.SurfaceRadiusKm - pilot.DrawnSurfaceDepthKm;
            Assert.That(rig.OffsetKm.Length - drawnGround, Is.GreaterThan(0.9), "the camera stays a kilometer up");

            shipHud.Refresh();
            var facts = string.Join("\n", shipHud.FactTexts);
            StringAssert.Contains((site.GravityKmS2 * 1000.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                                  " m/s^2", facts);
            StringAssert.Contains(ShipHud.Latitude(site.LatitudeRad), facts);
            StringAssert.Contains("Solid ground: the ship has landed", facts);
            var temperature = Sim.Quantity(moon, NativeConstants.FieldMeanTemperature);
            StringAssert.Contains(ShipHud.Celsius(temperature.Value), facts);
            StringAssert.Contains(temperature.SourceId + " (Tier 2)", facts);
            // No atmosphere: the source's table gives 0 bar (ADR-0018).
            StringAssert.Contains("None at the source's precision", facts);
            Assert.That(pilot.HasDay);
            StringAssert.Contains(ShipHud.Duration(pilot.Day.MeanSolarDayS), facts);
            Assert.That(shipHud.LandButtonText, Is.EqualTo("Lift off (L)"));
            yield return Screenshot("ship-landed-moon");

            pilot.LiftOff();
            Assert.That(pilot.State, Is.EqualTo(ShipPilot.Mode.Ascending));
            yield return Seconds(6.5f);
            Assert.That(pilot.State, Is.EqualTo(ShipPilot.Mode.Flying));
            pilot.SetActive(false);
            yield return Frames(2);
            Assert.That(rig.Piloted, Is.False, "the camera is the rig's again");
        }

        [UnityTest]
        public IEnumerator ALandedShipHasAContactShadeUnderIt()
        {
            // Mars, solid ground under a sky (docs/ART_DIRECTION.md section 11), approached from the sunlit side so
            // the ship lands by day.
            var mars = Index(499);
            var light = ((Vector3)host.Views[mars].LightDirection).normalized;
            rig.JumpTo(mars, Sim.Bodies[mars].MaxRadiusKm * 1.5, Math.Atan2(light.x, light.z),
                       Math.Asin(Mathf.Clamp(light.y, -1f, 1f)));
            rig.Select(mars);
            yield return Frames(2);
            pilot.SetActive(true);
            yield return Frames(2);
            var shade = pilot.transform.Find("Spaceship contact shade");
            Assert.That(shade, Is.Not.Null);
            var shadeRenderer = shade.GetComponent<MeshRenderer>();
            Assert.That(shadeRenderer.enabled, Is.False, "no shade in flight");
            pilot.Descend();
            yield return Seconds(9.5f);
            Assert.That(pilot.State, Is.EqualTo(ShipPilot.Mode.Landed));
            yield return Frames(3);
            Assert.That(shadeRenderer.enabled, "a landed ship has its contact shade");

            // On the ground as drawn, straight below the ship's lowest point.
            var model = pilot.transform.Find("Spaceship");
            var size = model.localScale.x;
            var bounds = model.GetComponent<MeshFilter>().sharedMesh.bounds;
            var up = shade.up;
            Assert.That(Vector3.Dot(model.position - shade.position, up), Is.EqualTo(-bounds.min.y * size).Within(1e-3f * size));
            Assert.That(Vector3.ProjectOnPlane(model.position - shade.position, up).magnitude, Is.LessThan(1e-3f * size),
                        "straight below");

            // It darkens the ground just beyond the wing tip against the ground beyond its edge, on the same side of
            // the ship and before post-processing (the vignette darkens toward the edges of the view).
            var cam = Camera.main;
            var data = cam.GetUniversalAdditionalCameraData();
            var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            cam.targetTexture = target;
            data.renderPostProcessing = false;
            yield return Frames(2);
            var near = cam.WorldToScreenPoint(shade.position + shade.right * (0.55f * size));
            var beyond = cam.WorldToScreenPoint(shade.position + shade.right * (1.1f * size));
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(1600, 900, TextureFormat.RGBAHalf, false, true);
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0, false);
            RenderTexture.active = previous;
            cam.targetTexture = null;
            data.renderPostProcessing = true;
            var shaded = image.GetPixel((int)near.x, (int)near.y);
            var open = image.GetPixel((int)beyond.x, (int)beyond.y);
            Object.Destroy(image);
            target.Release();
            Object.Destroy(target);
            float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            TestContext.WriteLine($"contact shade: {shaded} at {near}, {open} at {beyond}");
            Assert.That(Luminance(open), Is.GreaterThan(0.05f), "landed by day");
            Assert.That(Luminance(shaded), Is.LessThan(0.85f * Luminance(open)), "the ground is darker beside the ship");
            yield return Screenshot("ship-landed-mars-shade");
        }

        [UnityTest]
        public IEnumerator OverAGiantPlanetTheShipStopsAboveItsPressureLevel()
        {
            // Jupiter's reference surface is its 1 bar level, with no solid ground there (ADR-0017).
            yield return StartNear(599, 1.5);
            var jupiter = Index(599);
            Assert.That(pilot.SurfaceKind, Is.EqualTo(NativeConstants.SurfacePressureLevel));
            shipHud.Refresh();
            Assert.That(shipHud.LandButtonText, Is.EqualTo("Descend (L)"));
            pilot.Descend();
            yield return Seconds(9.5f);
            Assert.That(pilot.State, Is.EqualTo(ShipPilot.Mode.Hovering), "it never lands there");
            Assert.That(pilot.AltitudeKm, Is.EqualTo(2.0).Within(1e-9), "the lowest hover height");
            yield return Frames(15);
            shipHud.Refresh();
            var facts = string.Join("\n", shipHud.FactTexts);
            var level = Sim.Quantity(jupiter, NativeConstants.FieldReferencePressure);
            StringAssert.Contains($"No solid surface here: the reference surface is the {ShipHud.Bars(level.Value)} level", facts);
            StringAssert.Contains(level.SourceId, facts);
            StringAssert.Contains("averaged at that level", facts);
            yield return Screenshot("ship-over-jupiter");
        }

        [UnityTest]
        public IEnumerator ViewsOfTheShip()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("USIM_SCREENSHOT_DIR")))
            {
                Assert.Ignore("set USIM_SCREENSHOT_DIR to save the ship's views");
            }
            yield return StartNear(399, 3.0);
            yield return Screenshot("ship-near-earth");
            var mars = Index(499);
            rig.Select(mars);
            pilot.TravelTo(mars);
            yield return Seconds(pilot.FlightSeconds * 0.4f);
            yield return Screenshot("ship-travel-mars");
            yield return Seconds(pilot.FlightSeconds * 0.6f + 0.5f);
            pilot.Descend();
            yield return Seconds(9.5f);
            yield return Screenshot("ship-landed-mars");
        }

        // As MainSceneTests.Screenshot, into a "ship" folder.
        private IEnumerator Screenshot(string name)
        {
            var root = Environment.GetEnvironmentVariable("USIM_SCREENSHOT_DIR");
            if (string.IsNullOrEmpty(root))
            {
                yield break;
            }
            var folder = Path.Combine(root, "ship");
            var cam = Camera.main;
            var document = Object.FindAnyObjectByType<UIDocument>();
            var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = target;
            document.panelSettings.targetTexture = target;
            yield return null;
            yield return null;
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            cam.targetTexture = null;
            document.panelSettings.targetTexture = null;
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
            Object.Destroy(image);
            target.Release();
            Object.Destroy(target);
        }
    }
}
