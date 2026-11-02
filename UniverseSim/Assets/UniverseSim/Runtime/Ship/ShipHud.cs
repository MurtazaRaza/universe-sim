using System;
using System.Collections.Generic;
using System.Globalization;
using UniverseSim.Unity.Native;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The spaceship's part of the HUD (ADR-0016): the Spaceship toggle, and a panel with the ship's controls, the
    /// facts of the trip in progress and the conditions at the surface below. Every value comes from the core and is
    /// only formatted here (units converted for reading: m/s^2, degrees, days); nothing is computed beyond ratios and
    /// unit conversions.
    /// </summary>
    [DefaultExecutionOrder(25)]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ShipHud : MonoBehaviour
    {
        [SerializeField] private SimulationHost host;
        [SerializeField] private CameraRig rig;
        [SerializeField] private ShipPilot pilot;
        [SerializeField] private float refreshSeconds = 0.1f;

        // Unit definitions (the SI day, the Julian year of 365.25 days), not physical values.
        private const double Day = 86400.0;
        private const double JulianYear = 365.25 * Day;

        private Toggle toggleShip, toggleEnlarged, toggleFly;
        private VisualElement panel;
        private Label status, note;
        private Button travelButton, landButton, leaveButton;
        private ScrollView facts;
        private readonly List<Label> rows = new List<Label>();
        private int used;
        private float nextRefresh;

        /// <summary>The fact rows shown, in order (for tests): "name", "value", "source" lines.</summary>
        public IEnumerable<string> FactTexts
        {
            get
            {
                for (var i = 0; i < used; ++i)
                {
                    yield return rows[i].text;
                }
            }
        }

        private CoreSimulation Sim => host != null ? host.Simulation : null;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            toggleShip = root.Q<Toggle>("toggle-ship");
            toggleEnlarged = root.Q<Toggle>("toggle-enlarged");
            toggleFly = root.Q<Toggle>("toggle-fly");
            panel = root.Q<VisualElement>("ship-panel");
            status = root.Q<Label>("ship-status");
            note = root.Q<Label>("ship-note");
            travelButton = root.Q<Button>("ship-travel");
            landButton = root.Q<Button>("ship-land");
            leaveButton = root.Q<Button>("ship-leave");
            facts = root.Q<ScrollView>("ship-facts");
            toggleShip.tooltip = "The spaceship belongs to the stylized style.";

            toggleShip.RegisterValueChangedCallback(e => pilot.SetActive(e.newValue));
            travelButton.clicked += () => pilot.TravelTo(rig.SelectedIndex);
            landButton.clicked += () =>
            {
                if (pilot.State == ShipPilot.Mode.Hovering || pilot.State == ShipPilot.Mode.Descending)
                {
                    pilot.LiftOff();
                }
                else
                {
                    pilot.Descend();
                }
            };
            leaveButton.clicked += () => pilot.SetActive(false);
            pilot.ModeChanged += OnModeChanged;
            note.text = "A fictional craft: not in the data, and its flights are compressed in time. Every distance, " +
                        "time and value shown is real.";
            panel.style.display = DisplayStyle.None;
        }

        private void OnDisable()
        {
            if (pilot != null)
            {
                pilot.ModeChanged -= OnModeChanged;
            }
        }

        // The panel appears, changes and goes at once with the ship's mode, not at the next refresh.
        private void OnModeChanged(ShipPilot.Mode mode) => Refresh();

        private void LateUpdate()
        {
            if (Sim == null || Time.unscaledTime < nextRefresh)
            {
                return;
            }
            nextRefresh = Time.unscaledTime + refreshSeconds;
            Refresh();
        }

        /// <summary>Updates the toggle and the panel now (also done ten times a second).</summary>
        public void Refresh()
        {
            if (Sim == null || toggleShip == null)
            {
                return;
            }
            // Unavailable in the realistic style: disabled, with the same text, so switching style changes no readout.
            toggleShip.SetEnabled(pilot.Available || pilot.Active);
            toggleShip.SetValueWithoutNotify(pilot.Active);
            // The ship flies at true scale and drives the camera itself.
            toggleEnlarged.SetEnabled(!pilot.Active);
            toggleFly.SetEnabled(!pilot.Active);
            panel.style.display = pilot.Active ? DisplayStyle.Flex : DisplayStyle.None;
            if (!pilot.Active)
            {
                return;
            }

            var anchor = pilot.AnchorIndex >= 0 ? Sim.Bodies[pilot.AnchorIndex] : null;
            var anchorName = Named(pilot.AnchorIndex);
            status.text = pilot.State switch
            {
                ShipPilot.Mode.Traveling => $"Flying to {Named(pilot.TargetIndex)}, " +
                                            $"{(100f * pilot.TravelProgress).ToString("0", CultureInfo.InvariantCulture)} %",
                ShipPilot.Mode.Descending => $"Descending toward {anchorName}",
                ShipPilot.Mode.Hovering => $"Hovering {Distance(pilot.AltitudeKm)} above {anchorName}",
                ShipPilot.Mode.Ascending => $"Climbing away from {anchorName}",
                _ => $"Near {anchorName}, {Distance(Math.Max(pilot.AltitudeKm, 0.0))} up",
            };

            var selected = rig.SelectedIndex;
            var canTravel = selected >= 0 && selected < Sim.Bodies.Count && Sim.Bodies[selected].HasMeanRadius &&
                            (pilot.State == ShipPilot.Mode.Flying || pilot.State == ShipPilot.Mode.Hovering);
            travelButton.text = canTravel ? $"Travel to {Sim.Bodies[selected].Name} (T)" : "Travel (T): select a body";
            travelButton.SetEnabled(canTravel && (selected != pilot.AnchorIndex || pilot.State == ShipPilot.Mode.Flying));
            var down = pilot.State == ShipPilot.Mode.Hovering || pilot.State == ShipPilot.Mode.Descending;
            landButton.text = down ? "Lift off (L)" : "Descend (L)";
            landButton.SetEnabled(down || (pilot.State == ShipPilot.Mode.Flying && pilot.CanDescend));

            used = 0;
            if (pilot.State == ShipPilot.Mode.Descending || pilot.State == ShipPilot.Mode.Hovering)
            {
                AddSurface(anchor);
            }
            else if (pilot.TargetIndex >= 0 && (pilot.State == ShipPilot.Mode.Traveling || pilot.TargetIndex == pilot.AnchorIndex))
            {
                AddTrip();
            }
            else
            {
                Row("value", "W and S thrust, A and D turn, Q and E pitch, drag to steer, Shift to boost. Select a body " +
                             "and press T to fly there, or L to descend to the surface below.");
            }
            for (var i = used; i < rows.Count; ++i)
            {
                rows[i].style.display = DisplayStyle.None;
            }
        }

        private string Name(int index) => index >= 0 && index < Sim.Bodies.Count ? Sim.Bodies[index].Name : "-";

        // A body's name as it reads in a sentence: "the Sun" and "the Moon" take the article, other names do not.
        private string Named(int index)
        {
            var name = Name(index);
            var star = index >= 0 && index < Sim.Bodies.Count && Sim.Bodies[index].Kind == 1; // usim BodyKind star
            return star || name == "Moon" ? "the " + name : name;
        }

        // ---- The trip (core: usim_transfer_estimate_at) ----

        private void AddTrip()
        {
            var to = Named(pilot.TargetIndex);
            var from = Named(pilot.TripFromIndex);
            Row("value-name", $"Trip from {from} to {to}");
            if (!pilot.HasTransfer)
            {
                Row("value", "No trip estimate: " + pilot.TransferNote);
                return;
            }
            var e = pilot.Transfer;
            Row("value-name", "Distance when you left");
            Row("value", Distance(e.DistanceKm));
            Row("value-name", "Light needs");
            Row("value", Duration(e.LightTimeS));
            Row("value-name", "This flight");
            Row("value", $"{Duration(pilot.FlightSeconds)}, {SpeedOfLight(e.LightTimeS, pilot.FlightSeconds)}");

            var center = Sim.IndexOf(e.Center);
            var departure = Sim.IndexOf(e.Departure);
            var arrival = Sim.IndexOf(e.Arrival);
            var fromSurface = (e.Flags & NativeConstants.TransferFromSurface) != 0;
            var toSurface = (e.Flags & NativeConstants.TransferToSurface) != 0;
            var leave = fromSurface ? $"{Named(center)}'s surface" : $"{Named(departure)}'s orbit";
            var reach = toSurface ? $"{Named(center)}'s surface" : $"{Named(arrival)}'s orbit";
            Row("value-name", "A real spacecraft");
            Row("value", $"{Duration(e.TransferTimeS)} on a Hohmann transfer around {Named(center)}, from {leave} to {reach}");
            Row("value", $"Speed changes: {Speed(e.DepartureDeltaVKmS)} to leave, {Speed(e.ArrivalDeltaVKmS)} to arrive");
            Row("value-source",
                "A two-body estimate by the core: circular, coplanar orbits as large as the bodies' orbits at this " +
                "moment; no launch window, eccentricity, inclination, or the planets' own gravity at either end.");
            if ((e.Flags & NativeConstants.TransferBetweenSystems) != 0)
            {
                Row("value-source", $"Between the systems: the last leg to {to} is not included.");
            }
            if (fromSurface || toSurface)
            {
                Row("value-source", $"{Capitalized(Named(center))}'s surface here is a sphere of its mean radius, " +
                                    "without its atmosphere or rotation.");
            }
            if ((e.Flags & NativeConstants.TransferApproximate) != 0)
            {
                Row("value-source", "Uses a position placed at its system's barycenter (ADR-0008).");
            }
        }

        // ---- The surface below (core: usim_surface_site_at, usim_day_length_at) ----

        private void AddSurface(BodyInfo body)
        {
            if (!pilot.HasSite || body == null)
            {
                Row("value", "Finding the surface below...");
                return;
            }
            var s = pilot.Site;
            Row("value-name", $"Over {Named(body.Index)}");
            Row("value", $"{Latitude(s.LatitudeRad)}, {Longitude(s.LongitudeRad)}, {Distance(pilot.AltitudeKm)} above the " +
                         "reference surface");
            if ((s.Flags & NativeConstants.SiteHasSun) != 0)
            {
                Row("value-name", "The Sun");
                var elevation = s.SunElevationRad * Mathf.Rad2Deg;
                Row("value", elevation >= 0.0
                                 ? $"{Degrees(elevation)} above the horizon, azimuth {Degrees(s.SunAzimuthRad * Mathf.Rad2Deg)}"
                                 : $"{Degrees(-elevation)} below the horizon: night");
                Row("value", "Local solar time " + Clock(s.LocalSolarTimeH));
                if ((s.Flags & NativeConstants.SiteHasSunSize) != 0)
                {
                    Row("value", $"Looks {Degrees(s.SunAngularDiameterRad * Mathf.Rad2Deg, "0.00")} wide; sunlight " +
                                 $"{Percent(s.SunlightRatio)} of its strength at 1 au");
                }
            }
            if ((s.Flags & NativeConstants.SiteHasGravity) != 0)
            {
                Row("value-name", "Gravity");
                Row("value", $"{(s.GravityKmS2 * 1000.0).ToString("0.00", CultureInfo.InvariantCulture)} m/s^2 at the " +
                             "reference surface");
                Row("value", "Escape speed " + Speed(s.EscapeSpeedKmS));
                Row("value-source", "GM / r^2 and sqrt(2 GM / r) at the reference surface, without rotation. GM: " +
                                    SourceOf(body, NativeConstants.FieldGm) + ", radii: " +
                                    SourceOf(body, NativeConstants.FieldRadiusA, NativeConstants.FieldMeanRadius) + ".");
            }
            if (pilot.HasDay)
            {
                var d = pilot.Day;
                Row("value-name", "Day");
                var turn = "One turn in " + Duration(d.SiderealRotationPeriodS);
                Row("value", (d.Flags & NativeConstants.DayHasSolarDay) != 0
                                 ? $"{turn}; noon to noon {Duration(d.MeanSolarDayS)}" +
                                   ((d.Flags & NativeConstants.DayRetrograde) != 0 ? " (it turns backward)" : string.Empty)
                                 : turn);
                Row("value-source", "From the rotation model's mean rate and the orbit around the Sun; rotation: " +
                                    (string.IsNullOrEmpty(body.RotationSourceId) ? "-" : body.RotationSourceId) + ".");
            }
            Row("value-name", "Not yet in the data");
            Row("value-source", "Surface temperature and pressure, and whether the reference surface is solid ground or " +
                                "cloud tops. The sky is drawn without an atmosphere.");
        }

        private string SourceOf(BodyInfo body, params int[] fields)
        {
            foreach (var field in fields)
            {
                var q = Sim.Quantity(body.Index, field);
                if (q.HasValue && !string.IsNullOrEmpty(q.SourceId))
                {
                    return q.SourceId;
                }
            }
            return "-";
        }

        private void Row(string className, string text)
        {
            if (used == rows.Count)
            {
                var label = new Label { pickingMode = PickingMode.Ignore };
                rows.Add(label);
                facts.Add(label);
            }
            var row = rows[used++];
            row.text = text;
            row.ClearClassList();
            row.AddToClassList(className);
            row.style.display = DisplayStyle.Flex;
        }

        // ---- Formatting (units for reading; the values are the core's) ----

        /// <summary>A duration for reading: "12 s", "11 min 49 s", "5 h 12 min", "258.9 days", "4.5 years".</summary>
        public static string Duration(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.0)
            {
                return "-";
            }
            var c = CultureInfo.InvariantCulture;
            if (seconds < 60.0)
            {
                return seconds.ToString(seconds < 10.0 ? "0.0" : "0", c) + " s";
            }
            if (seconds < 3600.0)
            {
                var whole = (long)Math.Round(seconds);
                return $"{whole / 60} min {whole % 60} s";
            }
            if (seconds < 2.0 * Day)
            {
                var minutes = (long)Math.Round(seconds / 60.0);
                return $"{minutes / 60} h {minutes % 60} min";
            }
            if (seconds < 2.0 * JulianYear)
            {
                return (seconds / Day).ToString(seconds < 100.0 * Day ? "0.00" : "0.0", c) + " days";
            }
            return (seconds / JulianYear).ToString("0.0", c) + " years";
        }

        /// <summary>A distance for reading: "384,400 km", "212.5 million km", "4.35 billion km".</summary>
        public static string Distance(double km)
        {
            if (double.IsNaN(km) || double.IsInfinity(km))
            {
                return "-";
            }
            var c = CultureInfo.InvariantCulture;
            var a = Math.Abs(km);
            if (a < 10.0)
            {
                return km.ToString("0.0", c) + " km";
            }
            if (a < 1.0e7)
            {
                return km.ToString("N0", c) + " km";
            }
            return a < 1.0e9 ? (km / 1.0e6).ToString("0.0", c) + " million km" : (km / 1.0e9).ToString("0.00", c) + " billion km";
        }

        /// <summary>A speed in km/s for reading.</summary>
        public static string Speed(double kmPerS) =>
            kmPerS.ToString(kmPerS < 10.0 ? "0.00" : "0.0", CultureInfo.InvariantCulture) + " km/s";

        /// <summary>
        /// How a flight of <paramref name="flightSeconds"/> compares with light covering the same distance in
        /// <paramref name="lightSeconds"/>: "about 51 times the speed of light", or a percentage below it.
        /// </summary>
        public static string SpeedOfLight(double lightSeconds, double flightSeconds)
        {
            if (!(flightSeconds > 0.0) || !(lightSeconds > 0.0))
            {
                return "-";
            }
            var ratio = lightSeconds / flightSeconds;
            var c = CultureInfo.InvariantCulture;
            if (ratio >= 1.5)
            {
                return $"about {ratio.ToString("N0", c)} times the speed of light";
            }
            return ratio >= 0.95 ? "about the speed of light" : $"about {(100.0 * ratio).ToString("0", c)} % of the speed of light";
        }

        /// <summary>Planetocentric latitude: "12.3° N".</summary>
        public static string Latitude(double rad) =>
            Degrees(Math.Abs(rad * Mathf.Rad2Deg), "0.0") + (rad >= 0.0 ? " N" : " S");

        /// <summary>East longitude, as east or west: "45.6° E", "120.0° W".</summary>
        public static string Longitude(double rad) =>
            Degrees(Math.Abs(rad * Mathf.Rad2Deg), "0.0") + (rad >= 0.0 ? " E" : " W");

        /// <summary>Hours of a 24-hour day as "hh:mm".</summary>
        public static string Clock(double hours)
        {
            var minutes = (long)Math.Floor(hours * 60.0) % (24 * 60);
            return $"{minutes / 60:00}:{minutes % 60:00}";
        }

        private static string Capitalized(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        private static string Degrees(double degrees, string format = "0") =>
            degrees.ToString(format, CultureInfo.InvariantCulture) + "°";

        private static string Percent(double ratio) => (100.0 * ratio).ToString("0", CultureInfo.InvariantCulture) + " %";
    }
}
