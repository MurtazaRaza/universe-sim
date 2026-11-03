using System;
using System.Collections.Generic;
using System.Globalization;
using UniverseSim.Unity.Native;

namespace UniverseSim.Unity
{
    /// <summary>One line of "what it's like here": a short title and a sentence.</summary>
    public readonly struct BodyFact
    {
        public readonly string Title;
        public readonly string Text;

        public BodyFact(string title, string text)
        {
            Title = title;
            Text = text;
        }

        public override string ToString() => $"{Title}: {Text}";
    }

    /// <summary>
    /// What a body is like, in human terms (ADR-0019 decision 3): its size and your weight there compared with the
    /// Earth, how long its day and year last, how warm it is, how long sunlight takes to get there and how long a real
    /// spacecraft from the Earth would need. Every value comes from the core (radii, the surface site's gravity, the day
    /// length, the surface conditions, the transfer estimate); like the ship panel, the host only takes ratios and
    /// converts units for reading. The sourced values and their citations stay in the info panel's data section.
    /// </summary>
    public static class BodyFacts
    {
        /// <summary>The body comparisons are made with: the Earth's NAIF ID (an identifier, not a physical value).</summary>
        public const long EarthNaif = 399;

        private const int KindBarycenter = 0; // usim::catalog::BodyKind
        private const int KindStar = 1;

        // The Julian year of 365.25 days, a unit definition.
        private const double JulianYearS = 365.25 * 86400.0;

        /// <summary>
        /// Adds the facts for the body at <paramref name="index"/> to <paramref name="into"/> (cleared first). The request
        /// gives the display frame the core's surface site uses; only its magnitudes are read here.
        /// </summary>
        public static void Collect(CoreSimulation sim, int index, UsimSnapshotRequest request, SkyCues skyCues,
                                   List<BodyFact> into)
        {
            into.Clear();
            if (sim == null || index < 0 || index >= sim.Bodies.Count)
            {
                return;
            }
            var body = sim.Bodies[index];
            if (body.Kind == KindBarycenter || !body.HasMeanRadius)
            {
                into.Add(new BodyFact("Not a body",
                                      "A barycenter: the balance point that the bodies of its system circle together."));
                return;
            }
            var now = sim.Clock.Now;
            var earth = sim.IndexOf(UsimBodyId.Naif(EarthNaif));
            var isEarth = index == earth;
            var star = body.Kind == KindStar;

            AddSize(sim, body, earth, into);
            AddWeight(sim, body, earth, request, now, star, into);
            AddDayAndYear(sim, body, now, star, into);
            AddTemperature(sim, body, into);
            if (!star)
            {
                AddSunlight(sim, body, request, now, into);
            }
            if (!star && !isEarth && earth >= 0)
            {
                AddTrip(sim, body, sim.Bodies[earth], now, into);
            }
            AddSky(sim, body, skyCues, into);
        }

        /// <summary>"3.4 times", "about the same as", "38 %": a ratio for reading.</summary>
        public static string Times(double ratio)
        {
            var c = CultureInfo.InvariantCulture;
            if (ratio >= 1.5)
            {
                return ratio.ToString(ratio < 10.0 ? "0.0" : "N0", c) + " times";
            }
            if (ratio > 0.95 && ratio < 1.05)
            {
                return "about the same as";
            }
            return (100.0 * ratio).ToString(ratio < 0.1 ? "0.0" : "0", c) + " % of";
        }

        private static void AddSize(CoreSimulation sim, BodyInfo body, int earth, List<BodyFact> into)
        {
            var across = ShipHud.Distance(2.0 * body.MeanRadiusKm) + " across";
            if (earth < 0 || earth == body.Index)
            {
                into.Add(new BodyFact("Size", across));
                return;
            }
            var ratio = body.MeanRadiusKm / sim.Bodies[earth].MeanRadiusKm;
            var compared = ratio >= 1.5 ? $"{Times(ratio)} as wide as the Earth"
                : ratio > 0.95 && ratio < 1.05 ? "About as wide as the Earth"
                : $"{Times(ratio)} the Earth's width";
            into.Add(new BodyFact("Size", $"{Capitalized(compared)}: {across}"));
        }

        private static void AddWeight(CoreSimulation sim, BodyInfo body, int earth, UsimSnapshotRequest request,
                                      UsimInstant now, bool star, List<BodyFact> into)
        {
            if (!Gravity(sim, body.Index, request, now, out var g))
            {
                return;
            }
            var where = star ? "at its visible surface"
                : sim.SurfaceKind(body.Index) == NativeConstants.SurfacePressureLevel ? "at the cloud tops"
                : "on the ground";
            var metric = (g * 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + " m/s^2";
            if (earth < 0 || earth == body.Index || !Gravity(sim, earth, request, now, out var gEarth) || gEarth <= 0.0)
            {
                into.Add(new BodyFact("Gravity", $"{metric} at the equator, {where}"));
                return;
            }
            var ratio = g / gEarth;
            var text = ratio > 0.95 && ratio < 1.05
                ? $"You would weigh about what you weigh on the Earth, {where}"
                : $"You would weigh {Times(ratio)} your Earth weight, {where}";
            into.Add(new BodyFact("Your weight", $"{text} ({metric} at the equator)"));
        }

        private static bool Gravity(CoreSimulation sim, int index, UsimSnapshotRequest request, UsimInstant now, out double g)
        {
            g = 0.0;
            var body = sim.Bodies[index];
            if (!body.HasRotation ||
                !sim.TrySurfaceSite(body.Id, 0.0, 0.0, 0.0, ref request, now, out var site) ||
                (site.Flags & NativeConstants.SiteHasGravity) == 0)
            {
                return false;
            }
            g = site.GravityKmS2;
            return g > 0.0;
        }

        private static void AddDayAndYear(CoreSimulation sim, BodyInfo body, UsimInstant now, bool star,
                                          List<BodyFact> into)
        {
            if (!sim.TryDayLength(body.Id, now, out var day))
            {
                return;
            }
            var backward = (day.Flags & NativeConstants.DayRetrograde) != 0 ? ", and it spins backward" : string.Empty;
            if ((day.Flags & NativeConstants.DayHasSolarDay) != 0 && day.MeanSolarDayS > 0.0)
            {
                into.Add(new BodyFact("A day", $"Noon to noon takes {Span(day.MeanSolarDayS)}{backward}"));
            }
            else if (day.SiderealRotationPeriodS > 0.0)
            {
                into.Add(new BodyFact("A turn", $"It spins once in {Span(day.SiderealRotationPeriodS)}{backward}"));
            }
            // The core's orbital period is that of the orbit around the star of the body's system (a moon's is its
            // planet system's), so it is the body's year.
            if (star || !(day.OrbitalPeriodS > 0.0))
            {
                return;
            }
            var years = day.OrbitalPeriodS / JulianYearS;
            // Shown in days below two years, so the Earth years are added there; longer ones read in years.
            var inEarthYears = years < 1.05 || years >= 2.0 ? string.Empty
                : $" ({years.ToString("0.00", CultureInfo.InvariantCulture)} Earth years)";
            into.Add(new BodyFact("A year", $"Once around the Sun takes {Span(day.OrbitalPeriodS)}{inEarthYears}"));
        }

        private static void AddTemperature(CoreSimulation sim, BodyInfo body, List<BodyFact> into)
        {
            var t = sim.Quantity(body.Index, NativeConstants.FieldMeanTemperature);
            if (!t.HasValue)
            {
                return;
            }
            var where = sim.SurfaceKind(body.Index) == NativeConstants.SurfacePressureLevel ? "at the cloud tops" : "over the surface";
            into.Add(new BodyFact("Temperature", $"About {ShipHud.Celsius(t.Value)} on average {where}, day and night"));
        }

        private static void AddSunlight(CoreSimulation sim, BodyInfo body, UsimSnapshotRequest request, UsimInstant now,
                                        List<BodyFact> into)
        {
            var sun = -1;
            for (var i = 0; i < sim.Bodies.Count && sun < 0; ++i)
            {
                if (sim.Bodies[i].Kind == KindStar)
                {
                    sun = i;
                }
            }
            if (sun < 0 || !sim.TryTransferEstimate(sim.Bodies[sun].Id, body.Id, now, out var fromSun, out _) ||
                !(fromSun.LightTimeS > 0.0))
            {
                return;
            }
            var text = $"Light from the Sun takes {Span(fromSun.LightTimeS)} to get here";
            if (body.HasRotation && sim.TrySurfaceSite(body.Id, 0.0, 0.0, 0.0, ref request, now, out var site) &&
                (site.Flags & NativeConstants.SiteHasSunSize) != 0 && site.SunlightRatio > 0.0)
            {
                var ratio = site.SunlightRatio;
                text += ratio > 0.95 && ratio < 1.05
                    ? ", about as strong as at the Earth's distance"
                    : $", {Times(ratio)} as strong as at the Earth's distance (1 au)".Replace(" % of as", " % as");
            }
            into.Add(new BodyFact("Sunlight", text));
        }

        private static void AddTrip(CoreSimulation sim, BodyInfo body, BodyInfo earth, UsimInstant now, List<BodyFact> into)
        {
            if (!sim.TryTransferEstimate(earth.Id, body.Id, now, out var trip, out _) || !(trip.TransferTimeS > 0.0))
            {
                return;
            }
            var between = (trip.Flags & NativeConstants.TransferBetweenSystems) != 0 ? " to its planet's orbit" : string.Empty;
            into.Add(new BodyFact("Getting there",
                                  $"A real spacecraft from the Earth needs about {Span(trip.TransferTimeS)}" +
                                  $"{between}; light, {Span(trip.LightTimeS)}"));
        }

        private static void AddSky(CoreSimulation sim, BodyInfo body, SkyCues skyCues, List<BodyFact> into)
        {
            switch (sim.Atmosphere(body.Index))
            {
                case NativeConstants.AtmospherePresent:
                    var sky = skyCues != null && skyCues.TryGet(body.Id.Value, SkyCues.DaytimeSky, out var cue)
                        ? $": the daytime sky is {cue.HueName}" : string.Empty;
                    into.Add(new BodyFact("Air", "It has an atmosphere" + sky));
                    break;
                case NativeConstants.AtmosphereNone:
                    into.Add(new BodyFact("Air", "None to speak of: the sky stays black by day"));
                    break;
            }
        }

        // The ship panel's durations, without a trailing "0 min": "24 h" rather than "24 h 0 min".
        private static string Span(double seconds)
        {
            var text = ShipHud.Duration(seconds);
            return text.EndsWith(" h 0 min", StringComparison.Ordinal) ? text.Substring(0, text.Length - 6) : text;
        }

        private static string Capitalized(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }
}
