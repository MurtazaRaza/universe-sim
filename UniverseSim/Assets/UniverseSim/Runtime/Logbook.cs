using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UniverseSim.Unity.Native;
using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The explorer's logbook (ADR-0019 decision 4, SPEC FR-REN-17): goals to chase, checked a few times a second from
    /// what the viewer does: where the camera or the ship is (relative to bodies, from the core's states), what the
    /// ship is doing, and the clock. The checks only compare core values (distances in body radii, the ship's mode, the
    /// Sun's elevation at the ship's site); nothing here computes motion or changes the simulation. A goal whose bodies
    /// are not in the pack is left out. Progress is kept in PlayerPrefs, outside the simulation.
    /// </summary>
    [DefaultExecutionOrder(40)]
    public sealed class Logbook : MonoBehaviour
    {
        /// <summary>The PlayerPrefs key of the progress. Tests use a key of their own.</summary>
        public static string StorageKey = "usim.logbook";

        /// <summary>Whether the goals are checked each few frames. Tests turn it off, so no toast changes the HUD.</summary>
        public static bool Checking = true;

        // NAIF IDs: identifiers, not physical values.
        private const long SunNaif = 10;
        private const long EarthNaif = 399;
        private const long MoonNaif = 301;
        private const long MarsNaif = 499;
        private const long JupiterNaif = 599;
        private const long SaturnNaif = 699;
        private const long PlutoNaif = 999;
        private const int KindStar = 1;
        private const int KindPlanet = 2;

        // Display choices for the goals, not physical values.
        private const double VisitRadii = 10.0;  // a visit: within this many radii of the body's center
        private const double SunVisitRadii = 20.0;
        private const double EarthriseRadii = 6.0;
        private const double EarthriseMaxDegrees = 8.0; // the Earth this close above the Moon's limb
        private const double CenturyS = 100.0 * 365.25 * 86400.0; // a century of Julian years, a unit definition

        [SerializeField] private SimulationHost host;
        [SerializeField] private CameraRig rig;
        [SerializeField] private ShipPilot ship;
        [SerializeField] private float checkSeconds = 0.25f;

        /// <summary>One goal of the logbook.</summary>
        public sealed class Goal
        {
            internal Goal(string id, string title, string hint, int target = 0)
            {
                Id = id;
                Title = title;
                Hint = hint;
                Target = target;
            }

            /// <summary>A stable identifier (the key of the saved progress).</summary>
            public string Id { get; }

            public string Title { get; }

            /// <summary>How to get there.</summary>
            public string Hint { get; }

            public bool Done { get; internal set; }

            /// <summary>Steps reached of <see cref="Target"/> (0 for a goal of one step).</summary>
            public int Progress { get; internal set; }

            public int Target { get; internal set; }

            internal Func<bool> Check;
        }

        private readonly List<Goal> goals = new List<Goal>();
        private readonly HashSet<string> done = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<long> visited = new HashSet<long>();
        private readonly List<int> planets = new List<int>();
        private float nextCheck;

        private int sun = -1, earth = -1, moon = -1, mars = -1, jupiter = -1, saturn = -1, pluto = -1;
        private double ringInnerKm, ringOuterKm;
        private int ringSide;
        // The angle the Moon swept around the Earth while watched, rad, and the last sample of its direction.
        private double moonSweptRad;
        private Double3 lastMoonDirection;
        private bool hasMoonDirection;
        private UsimInstant sessionStart, lastMoonClock;
        private float lastMoonTime;
        private int sunsetAnchor = -1;
        private double sunsetElevation;
        private int takeOffAnchor = -1;
        private Double3 takeOffKm;
        private ShipPilot.Mode lastMode = ShipPilot.Mode.Off;

        /// <summary>The goals in the order shown.</summary>
        public IReadOnlyList<Goal> Goals => goals;

        /// <summary>How many goals are done.</summary>
        public int DoneCount
        {
            get
            {
                var n = 0;
                foreach (var g in goals)
                {
                    n += g.Done ? 1 : 0;
                }
                return n;
            }
        }

        /// <summary>Raised when a goal is reached.</summary>
        public event Action<Goal> Completed;

        /// <summary>Raised when the goals or their progress change (also after a reset).</summary>
        public event Action Changed;

        private CoreSimulation Sim => host != null ? host.Simulation : null;

        private void Awake()
        {
            if (host != null)
            {
                host.Loaded += OnLoaded;
            }
        }

        private void OnEnable()
        {
            if (ship != null)
            {
                ship.ModeChanged += OnShipMode;
            }
        }

        private void OnDisable()
        {
            if (ship != null)
            {
                ship.ModeChanged -= OnShipMode;
            }
        }

        private void OnDestroy()
        {
            if (host != null)
            {
                host.Loaded -= OnLoaded;
            }
        }

        private int Find(long naif)
        {
            var i = Sim.IndexOf(UsimBodyId.Naif(naif));
            return i >= 0 && Sim.Bodies[i].HasMeanRadius ? i : -1;
        }

        private void OnLoaded()
        {
            var sim = Sim;
            sun = -1;
            for (var i = 0; i < sim.Bodies.Count && sun < 0; ++i)
            {
                if (sim.Bodies[i].Kind == KindStar && sim.Bodies[i].HasMeanRadius)
                {
                    sun = i;
                }
            }
            if (sun < 0)
            {
                sun = Find(SunNaif);
            }
            earth = Find(EarthNaif);
            moon = Find(MoonNaif);
            mars = Find(MarsNaif);
            jupiter = Find(JupiterNaif);
            saturn = Find(SaturnNaif);
            pluto = Find(PlutoNaif);
            planets.Clear();
            foreach (var body in sim.Bodies)
            {
                if (body.Kind == KindPlanet && body.HasMeanRadius)
                {
                    planets.Add(body.Index);
                }
            }
            ringInnerKm = double.MaxValue;
            ringOuterKm = 0.0;
            foreach (var ring in sim.Rings)
            {
                if (ring.BodyIndex == saturn && !ring.IsGap)
                {
                    ringInnerKm = Math.Min(ringInnerKm, ring.InnerRadiusKm);
                    ringOuterKm = Math.Max(ringOuterKm, ring.OuterRadiusKm);
                }
            }
            sessionStart = sim.Clock.Now;
            hasMoonDirection = false;
            BuildGoals();
            Load();
            Changed?.Invoke();
        }

        private void BuildGoals()
        {
            goals.Clear();
            Add(new Goal("take-off", "Take off", "Press V for the spaceship, then W to fly."), TookOff);
            if (moon >= 0)
            {
                Add(new Goal("moon", "Fly to the Moon", "Select the Moon and press T."), () => Near(moon, VisitRadii));
                Add(new Goal("land-moon", "Land on the Moon", "Near the Moon, press L."), () => Landed(moon));
            }
            if (moon >= 0 && earth >= 0)
            {
                Add(new Goal("earthrise", "Watch an Earthrise",
                             "Fly low over the Moon until the Earth peeks over its edge."), Earthrise);
            }
            if (sun >= 0)
            {
                Add(new Goal("sun", "Brave the Sun", "Get within 20 solar radii of the Sun."),
                    () => Near(sun, SunVisitRadii));
            }
            if (mars >= 0)
            {
                Add(new Goal("land-mars", "Land on Mars", "Fly to Mars and press L."), () => Landed(mars));
            }
            if (jupiter >= 0)
            {
                Add(new Goal("jupiter-clouds", "Skim Jupiter's clouds",
                             "Descend over Jupiter (L): there is no ground, so the ship stops above the clouds."),
                    () => ship != null && ship.State == ShipPilot.Mode.Hovering && ship.AnchorIndex == jupiter);
            }
            if (saturn >= 0 && ringOuterKm > ringInnerKm)
            {
                Add(new Goal("saturn-rings", "Fly through Saturn's rings",
                             "Cross the ring plane between the rings' inner and outer edge."), RingCrossing);
            }
            if (pluto >= 0)
            {
                Add(new Goal("pluto", "Reach Pluto", "The farthest world in the data. Select it and travel."),
                    () => Near(pluto, VisitRadii));
            }
            if (planets.Count > 0)
            {
                Add(new Goal("planets", $"Visit all {Words(planets.Count)} planets", "Get within 10 radii of each.",
                             planets.Count), PlanetsVisited);
            }
            Add(new Goal("outrun-light", "Outrun light",
                         "Finish a trip faster than light would: try one between planets."), () => false);
            Add(new Goal("century", "Travel a century in time", "Drag the timeline, or hold Tab to rush."),
                () => Math.Abs(CoreSimulation.SecondsBetween(sessionStart, Sim.Clock.Now)) >= CenturyS);
            if (moon >= 0 && earth >= 0)
            {
                Add(new Goal("moon-month", "Watch the Moon go around", "Near the Earth or the Moon, let time run for " +
                             "a whole orbit.", 100), MoonWatched);
            }
            Add(new Goal("sunset", "See a sunset on another world",
                         "Land or hover on another world and let time run until the Sun goes down."), Sunset);
        }

        private void Add(Goal goal, Func<bool> check)
        {
            goal.Check = check;
            goals.Add(goal);
        }

        private static string Words(int n) => n switch
        {
            1 => "one", 2 => "two", 3 => "three", 4 => "four", 5 => "five", 6 => "six", 7 => "seven", 8 => "eight",
            9 => "nine", 10 => "ten", _ => n.ToString(CultureInfo.InvariantCulture),
        };

        private void Update()
        {
            var sim = Sim;
            if (!Checking || sim == null || goals.Count == 0 || Time.unscaledTime < nextCheck)
            {
                return;
            }
            nextCheck = Time.unscaledTime + checkSeconds;
            CheckNow();
        }

        // Every frame, so a fast clock still gives small steps: the Moon's direction from the Earth (core states).
        private void LateUpdate()
        {
            if (Checking && Sim != null && goals.Count > 0)
            {
                SampleMoon();
            }
        }

        /// <summary>Checks every goal now (also done a few times a second).</summary>
        public void CheckNow()
        {
            if (Sim == null)
            {
                return;
            }
            var changed = false;
            foreach (var goal in goals)
            {
                if (goal.Done)
                {
                    continue;
                }
                var before = goal.Progress;
                if (goal.Check != null && goal.Check())
                {
                    Complete(goal, false);
                    changed = true;
                }
                else if (goal.Progress != before)
                {
                    changed = true;
                }
            }
            if (changed)
            {
                Save();
                Changed?.Invoke();
            }
        }

        private void Complete(Goal goal, bool notify)
        {
            if (goal.Done)
            {
                return;
            }
            goal.Done = true;
            if (goal.Target > 0)
            {
                goal.Progress = goal.Target;
            }
            done.Add(goal.Id);
            Completed?.Invoke(goal);
            if (notify)
            {
                Save();
                Changed?.Invoke();
            }
        }

        /// <summary>Forgets every goal reached (the panel's "Start over").</summary>
        public void ResetProgress()
        {
            done.Clear();
            visited.Clear();
            moonSweptRad = 0.0;
            foreach (var goal in goals)
            {
                goal.Done = false;
                goal.Progress = 0;
            }
            Save();
            Changed?.Invoke();
        }

        // ---- The checks ----

        // Within `radii` of the body's center (enlarged when the bodies are shown enlarged), with the camera or the ship.
        private bool Near(int index, double radii)
        {
            if (index < 0 || rig == null || rig.FocusIndex != index)
            {
                return false;
            }
            var radius = Sim.Bodies[index].MaxRadiusKm * (rig.EnlargedBodies ? rig.BodyScale : 1.0);
            return rig.OffsetKm.Length < radii * radius;
        }

        private bool Landed(int index) =>
            index >= 0 && ship != null && ship.State == ShipPilot.Mode.Landed && ship.AnchorIndex == index;

        // The ship flew under its own power: it moved by a twentieth of its height since the last check.
        private bool TookOff()
        {
            if (ship == null || !ship.Active || ship.State != ShipPilot.Mode.Flying)
            {
                takeOffAnchor = -1;
                return false;
            }
            var moved = ship.AnchorIndex == takeOffAnchor &&
                        (ship.PositionKm - takeOffKm).Length > 0.05 * Math.Max(ship.AltitudeKm, 1.0);
            takeOffAnchor = ship.AnchorIndex;
            takeOffKm = ship.PositionKm;
            return moved;
        }

        // The Earth just above the Moon's edge as seen from near the Moon, and in view.
        private bool Earthrise()
        {
            if (rig == null || rig.FocusIndex != moon)
            {
                return false;
            }
            var camera = rig.OffsetKm; // from the Moon's center, host axes
            var radius = Sim.Bodies[moon].MaxRadiusKm;
            if (camera.Length > EarthriseRadii * radius || camera.Length <= radius ||
                !Sim.TryState(Sim.Bodies[earth].Id, Sim.Bodies[moon].Id, UsimFrame.EclipJ2000, Sim.Clock.Now, out var s))
            {
                return false;
            }
            var earthKm = Double3.From(CoreSimulation.AxesToHost(NativeConstants.AxesUnity, s.PositionKm));
            var toEarth = (earthKm - camera).Normalized;
            var toMoon = (-camera).Normalized;
            var separation = Math.Acos(Math.Clamp(toEarth.Dot(toMoon), -1.0, 1.0));
            var edge = Math.Asin(Math.Min(1.0, radius / camera.Length));
            var above = (separation - edge) * 180.0 / Math.PI;
            return above > 0.0 && above < EarthriseMaxDegrees && OnScreen(earth);
        }

        private bool OnScreen(int index)
        {
            var cam = Camera.main;
            if (cam == null || index >= host.Views.Count || !host.Views[index].HasPosition)
            {
                return false;
            }
            var p = cam.WorldToViewportPoint(host.Views[index].transform.position);
            return p.z > 0f && p.x > 0f && p.x < 1f && p.y > 0f && p.y < 1f;
        }

        // The camera crossed Saturn's equatorial plane (the rings' plane) between the rings' inner and outer edge; the
        // core gives the latitude of the point below.
        private bool RingCrossing()
        {
            if (rig == null || rig.FocusIndex != saturn)
            {
                ringSide = 0;
                return false;
            }
            var offset = rig.OffsetKm;
            var request = host.Request;
            if (!Sim.TrySurfaceSiteBelow(Sim.Bodies[saturn].Id, offset, ref request, Sim.Clock.Now, out var site))
            {
                return false;
            }
            var side = site.LatitudeRad >= 0.0 ? 1 : -1;
            var within = offset.Length >= ringInnerKm && offset.Length <= ringOuterKm;
            var crossed = ringSide != 0 && side != ringSide && within;
            ringSide = side;
            return crossed;
        }

        private bool PlanetsVisited()
        {
            foreach (var p in planets)
            {
                if (Near(p, VisitRadii))
                {
                    visited.Add(Sim.Bodies[p].Id.Value);
                }
            }
            var count = 0;
            foreach (var p in planets)
            {
                count += visited.Contains(Sim.Bodies[p].Id.Value) ? 1 : 0;
            }
            var goal = Get("planets");
            if (goal != null)
            {
                goal.Progress = count;
            }
            return count >= planets.Count;
        }

        // One whole turn of the Moon around the Earth while the view was at either, as the clock ran: the angles between
        // the Moon's directions from the Earth (core states) frame to frame, added up. A jump of the date counts nothing.
        private void SampleMoon()
        {
            if (moon < 0 || earth < 0)
            {
                return;
            }
            var goal = Get("moon-month");
            var now = Sim.Clock.Now;
            var near = rig != null && (rig.FocusIndex == earth || rig.FocusIndex == moon);
            if (goal == null || goal.Done || !near || Sim.Clock.Paused != 0 ||
                !Sim.TryState(Sim.Bodies[moon].Id, Sim.Bodies[earth].Id, UsimFrame.EclipJ2000, now, out var s))
            {
                hasMoonDirection = false;
                return;
            }
            var direction = Double3.From(s.PositionKm).Normalized;
            if (hasMoonDirection)
            {
                var passed = Math.Abs(CoreSimulation.SecondsBetween(lastMoonClock, now));
                var expected = Math.Abs(Sim.Clock.Rate) * Math.Max(Time.unscaledTime - lastMoonTime, 0f);
                var angle = Math.Acos(Math.Clamp(direction.Dot(lastMoonDirection), -1.0, 1.0));
                if (passed <= 1.5 * expected + 1.0 && angle < 2.0)
                {
                    moonSweptRad += angle;
                }
            }
            lastMoonDirection = direction;
            lastMoonClock = now;
            lastMoonTime = Time.unscaledTime;
            hasMoonDirection = true;
        }

        private bool MoonWatched()
        {
            var goal = Get("moon-month");
            if (goal != null)
            {
                goal.Progress = (int)Math.Min(100.0, Math.Floor(100.0 * moonSweptRad / (2.0 * Math.PI)));
            }
            return moonSweptRad >= 2.0 * Math.PI;
        }

        // The Sun went below the horizon at the ship's site on a world other than the Earth (the core's site).
        private bool Sunset()
        {
            if (ship == null || !(ship.State == ShipPilot.Mode.Landed || ship.State == ShipPilot.Mode.Hovering) ||
                !ship.HasSite || ship.AnchorIndex == earth || (ship.Site.Flags & NativeConstants.SiteHasSun) == 0)
            {
                sunsetAnchor = -1;
                return false;
            }
            var elevation = ship.Site.SunElevationRad;
            var set = sunsetAnchor == ship.AnchorIndex && sunsetElevation > 0.0 && elevation <= 0.0;
            sunsetAnchor = ship.AnchorIndex;
            sunsetElevation = elevation;
            return set;
        }

        private void OnShipMode(ShipPilot.Mode mode)
        {
            // A trip ends: faster than light covers the same distance?
            if (Checking && lastMode == ShipPilot.Mode.Traveling && mode == ShipPilot.Mode.Flying && ship.HasTransfer &&
                ship.FlightSeconds > 0f && ship.FlightSeconds < ship.Transfer.LightTimeS)
            {
                var goal = Get("outrun-light");
                if (goal != null && !goal.Done)
                {
                    Complete(goal, true);
                }
            }
            lastMode = mode;
        }

        /// <summary>The goal with <paramref name="id"/>, or null when it is not in this logbook.</summary>
        public Goal Get(string id)
        {
            foreach (var g in goals)
            {
                if (g.Id == id)
                {
                    return g;
                }
            }
            return null;
        }

        // ---- Saved progress ----

        private void Load()
        {
            done.Clear();
            visited.Clear();
            Parse(PlayerPrefs.GetString(StorageKey, string.Empty), done, visited);
            foreach (var goal in goals)
            {
                goal.Done = done.Contains(goal.Id);
                goal.Progress = goal.Done && goal.Target > 0 ? goal.Target : 0;
            }
            PlanetsVisited();
        }

        private void Save()
        {
            PlayerPrefs.SetString(StorageKey, Format(done, visited));
            PlayerPrefs.Save();
        }

        /// <summary>The saved form: the goals reached, then the planets visited (NAIF IDs): "moon,sun|399,499".</summary>
        public static string Format(IEnumerable<string> goalsDone, IEnumerable<long> planetsVisited)
        {
            var ids = new List<string>(goalsDone);
            ids.Sort(StringComparer.Ordinal);
            var bodies = new List<long>(planetsVisited);
            bodies.Sort();
            var text = new StringBuilder();
            text.Append(string.Join(",", ids));
            text.Append('|');
            for (var i = 0; i < bodies.Count; ++i)
            {
                text.Append(i > 0 ? "," : string.Empty).Append(bodies[i].ToString(CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        /// <summary>Reads <see cref="Format"/>'s text; anything it cannot read is skipped.</summary>
        public static void Parse(string text, ISet<string> goalsDone, ISet<long> planetsVisited)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            var parts = text.Split('|');
            foreach (var id in parts[0].Split(','))
            {
                if (id.Length > 0)
                {
                    goalsDone.Add(id);
                }
            }
            if (parts.Length > 1)
            {
                foreach (var item in parts[1].Split(','))
                {
                    if (long.TryParse(item, NumberStyles.Integer, CultureInfo.InvariantCulture, out var naif))
                    {
                        planetsVisited.Add(naif);
                    }
                }
            }
        }
    }
}
