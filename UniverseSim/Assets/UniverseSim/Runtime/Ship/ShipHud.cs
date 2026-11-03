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
        private VisualElement steer, steerCenter, steerDot;
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

        /// <summary>The text of the land button: "Land (L)", "Descend (L)" or "Lift off (L)" (for tests).</summary>
        public string LandButtonText => landButton != null ? landButton.text : string.Empty;

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
            steer = root.Q<VisualElement>("steer");
            steerCenter = root.Q<VisualElement>("steer-center");
            steerDot = root.Q<VisualElement>("steer-dot");
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
            PlaceSteering();
            if (Sim == null || Time.unscaledTime < nextRefresh)
            {
                return;
            }
            nextRefresh = Time.unscaledTime + refreshSeconds;
            Refresh();
        }

        // The mouse steering's reticle (ADR-0019 decision 2): a ring for the dead zone at the view's center and a dot
        // toward the pointer that grows as the turn does. Shown while the ship flies with mouse steering on.
        private void PlaceSteering()
        {
            if (steer == null)
            {
                return;
            }
            var shown = pilot.Active && pilot.State == ShipPilot.Mode.Flying && ShipPilot.MouseSteering &&
                        steer.panel != null;
            var display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (steer.style.display != display)
            {
                steer.style.display = display;
            }
            if (!shown)
            {
                return;
            }
            var panelSize = steer.panel.visualTree.layout.size;
            var half = 0.5f * Mathf.Min(panelSize.x, panelSize.y);
            var zone = pilot.SteeringZone;
            var ring = Mathf.Max(zone.x * half, 6f);
            if (!(steerCenter.userData is float sized) || Mathf.Abs(sized - ring) > 0.5f)
            {
                steerCenter.userData = ring;
                steerCenter.style.width = 2f * ring;
                steerCenter.style.height = 2f * ring;
                steerCenter.style.borderTopLeftRadius = ring;
                steerCenter.style.borderTopRightRadius = ring;
                steerCenter.style.borderBottomLeftRadius = ring;
                steerCenter.style.borderBottomRightRadius = ring;
                steerCenter.style.translate = new Translate(-ring, -ring);
            }
            // The dot sits where the turn points, from the dead zone's edge out to the full-turn offset (screen y is up,
            // panel y is down).
            var s = pilot.Steering;
            var amount = Mathf.Sqrt(Mathf.Clamp01(s.magnitude));
            var reach = (zone.x + (zone.y - zone.x) * amount) * half;
            var direction = s.sqrMagnitude > 0f ? s.normalized : Vector2.zero;
            steerDot.style.translate = new Translate(direction.x * reach - 4f, -direction.y * reach - 4f);
            steerDot.style.opacity = s.sqrMagnitude > 0f ? 0.35f + 0.65f * amount : 0f;
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
                ShipPilot.Mode.Landed => $"Landed on {anchorName}",
                ShipPilot.Mode.Ascending => $"Climbing away from {anchorName}",
                _ => $"Near {anchorName}, {Distance(Math.Max(pilot.AltitudeKm, 0.0))} up",
            };

            var selected = rig.SelectedIndex;
            var canTravel = selected >= 0 && selected < Sim.Bodies.Count && Sim.Bodies[selected].HasMeanRadius &&
                            (pilot.State == ShipPilot.Mode.Flying || pilot.State == ShipPilot.Mode.Hovering ||
                             pilot.State == ShipPilot.Mode.Landed);
            travelButton.text = canTravel ? $"Travel to {Sim.Bodies[selected].Name} (T)" : "Travel (T): select a body";
            travelButton.SetEnabled(canTravel && (selected != pilot.AnchorIndex || pilot.State == ShipPilot.Mode.Flying));
            var down = pilot.State == ShipPilot.Mode.Hovering || pilot.State == ShipPilot.Mode.Descending ||
                       pilot.State == ShipPilot.Mode.Landed;
            // Land on solid ground; descend elsewhere (ADR-0017).
            landButton.text = down ? "Lift off (L)"
                : pilot.CanDescend && pilot.SurfaceKind == NativeConstants.SurfaceSolid ? "Land (L)" : "Descend (L)";
            landButton.SetEnabled(down || (pilot.State == ShipPilot.Mode.Flying && pilot.CanDescend));

            used = 0;
            if (pilot.State == ShipPilot.Mode.Descending || pilot.State == ShipPilot.Mode.Hovering ||
                pilot.State == ShipPilot.Mode.Landed)
            {
                AddSurface(anchor);
            }
            else if (pilot.TargetIndex >= 0 && (pilot.State == ShipPilot.Mode.Traveling || pilot.TargetIndex == pilot.AnchorIndex))
            {
                AddTrip();
            }
            else
            {
                Row("value", "W and S thrust, Shift to boost. Move the mouse away from the center to steer (or A and D " +
                             "to turn, Q and E to pitch). Select a body and press T to fly there, or L to go down to the " +
                             "surface below: you land where it is solid ground.");
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
            AddConditions(body);
        }

        // ---- Surface conditions (core: usim_body_surface_kind and the SURF quantities, ADR-0017) ----

        // Unit definitions for display only (NIST Special Publication 811, Appendix B.8): T/K = t/degC + 273.15, and
        // 1 bar = 100 000 Pa. The core keeps kelvins and pascals.
        private const double KelvinAtZeroCelsius = 273.15;
        private const double PascalsPerBar = 100000.0;

        private void AddConditions(BodyInfo body)
        {
            var kind = Sim.SurfaceKind(body.Index);
            var temperature = Sim.Quantity(body.Index, NativeConstants.FieldMeanTemperature);
            var pressure = Sim.Quantity(body.Index, NativeConstants.FieldSurfacePressure);
            var level = Sim.Quantity(body.Index, NativeConstants.FieldReferencePressure);
            Row("value-name", "Surface");
            switch (kind)
            {
                case NativeConstants.SurfaceSolid:
                    Row("value", pilot.State == ShipPilot.Mode.Landed
                                     ? "Solid ground: the ship has landed on it."
                                     : "Solid ground: press L to land.");
                    Row("value-source", Cited(pressure) + "; it gives a pressure at the surface.");
                    break;
                case NativeConstants.SurfacePressureLevel:
                    Row("value", $"No solid surface here: the reference surface is the {Bars(level.Value)} level of the " +
                                 "atmosphere, where the radii are measured. The ship stays above it.");
                    Row("value-source", Cited(level));
                    break;
                default:
                    Row("value", "Not in the data: whether the reference surface is solid ground. The ship hovers.");
                    break;
            }
            if (temperature.HasValue)
            {
                Row("value-name", "Mean temperature");
                var where = kind == NativeConstants.SurfacePressureLevel ? "at that level" : "over the whole surface";
                Row("value", $"{Celsius(temperature.Value)}, averaged {where}, day and night: not the temperature here");
                Row("value-source", Cited(temperature));
            }
            if (pressure.HasValue)
            {
                Row("value-name", "Surface pressure");
                Row("value", pressure.Value > 0.0
                                 ? $"{Bars(pressure.Value)} ({pressure.Value.ToString("N0", CultureInfo.InvariantCulture)} Pa)"
                                 : "0 bar in the source's table");
                Row("value-source", Cited(pressure));
            }
            AddAtmosphere(body);
            if (!temperature.HasValue)
            {
                Row("value-name", "Not yet in the data");
                Row("value-source", "Surface temperature and pressure for this body.");
            }
        }

        // ---- Atmosphere and sky (core: usim_body_atmosphere; sky cues: Data/SkyCues.json, ADR-0018) ----

        private void AddAtmosphere(BodyInfo body)
        {
            Row("value-name", "Atmosphere");
            switch (Sim.Atmosphere(body.Index))
            {
                case NativeConstants.AtmospherePresent:
                    Row("value", "Yes, from the pressures above. Near the ground the sky brightens by day.");
                    AddSkyCue(body, SkyCues.DaytimeSky, "Sky by day",
                              "Not in the data: the sky is drawn in a neutral gray.");
                    AddSkyCue(body, SkyCues.Twilight, "Twilight", null);
                    break;
                case NativeConstants.AtmosphereNone:
                    Row("value", "None at the source's precision (0 bar in its table): the sky stays black.");
                    break;
                default:
                    Row("value", "Not in the data: whether this body has an atmosphere. The sky is drawn without one.");
                    break;
            }
        }

        private void AddSkyCue(BodyInfo body, string cue, string name, string missing)
        {
            if (host.SkyCues.TryGet(body.Id.Value, cue, out var info))
            {
                Row("value-name", name);
                Row("value", cue == SkyCues.Twilight
                                 ? $"{Capitalized(info.HueName)} near a low Sun and along the limb at the terminator."
                                 : Capitalized(info.HueName) + ".");
                Row("value-source", info.Citation);
            }
            else if (missing != null)
            {
                Row("value-name", name);
                Row("value", missing);
            }
        }

        // A value's source, its tier when it is a Tier 2 compilation (DATA_SOURCES.md section 1), and where in it.
        private string Cited(QuantityInfo q)
        {
            var tier = string.Empty;
            foreach (var s in Sim.Sources)
            {
                if (s.Id == q.SourceId && s.Tier == 2)
                {
                    tier = " (Tier 2)";
                }
            }
            return $"{q.SourceId}{tier}: {q.Locator}";
        }

        /// <summary>A temperature in degrees Celsius with its kelvins: "-65 °C (208.15 K)".</summary>
        public static string Celsius(double kelvin) =>
            (kelvin - KelvinAtZeroCelsius).ToString("0.#", CultureInfo.InvariantCulture) + " °C (" +
            kelvin.ToString("0.##", CultureInfo.InvariantCulture) + " K)";

        /// <summary>A pressure in bars, to the source's digits: "92 bar", "0.01 bar", "0.00001 bar".</summary>
        public static string Bars(double pascals) =>
            (pascals / PascalsPerBar).ToString("0.#########", CultureInfo.InvariantCulture) + " bar";

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
