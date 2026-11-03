using System;
using UniverseSim.Unity.Native;
using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The first view (ADR-0019 decision 1): instead of the whole system seen from its edge, the viewer starts near the
    /// Earth with the Moon beside it, in the spaceship in the stylized style and with the orbit camera in the realistic
    /// one. The place is a display choice made from the core's positions: a few Earth radii out, on the far side of the
    /// Earth from the Moon, turned toward the Sun so the day side shows and raised a little above the ecliptic. The
    /// Moon is selected, so the ship's Travel goes there. Tests and the frame-rate benchmark keep the system view
    /// (<see cref="Enabled"/>).
    /// </summary>
    [DefaultExecutionOrder(200)] // subscribes to Loaded after the rig, the ship and the HUD, so it acts last
    public sealed class StartExperience : MonoBehaviour
    {
        /// <summary>Whether the next load starts near the Earth. Tests turn it off to start from the system view.</summary>
        public static bool Enabled = true;

        // NAIF IDs: identifiers, not physical values.
        private const long EarthNaif = 399;
        private const long MoonNaif = 301;
        private const int KindStar = 1;

        [SerializeField] private SimulationHost host;
        [SerializeField] private CameraRig rig;
        [SerializeField] private ShipPilot ship;

        // Display choices, not physical values.
        [Tooltip("Distance of the start from the Earth's center, in its largest radius.")]
        [SerializeField] private double startRadii = 8.0;
        [Tooltip("How far the start is turned from the line through the Moon and the Earth, degrees: the Moon shows " +
                 "beside the Earth instead of behind it.")]
        [SerializeField] private double turnDegrees = 25.0;
        [Tooltip("How far the start is raised above the ecliptic, degrees.")]
        [SerializeField] private double raiseDegrees = 12.0;

        /// <summary>True once this load started near the Earth.</summary>
        public bool StartedNearEarth { get; private set; }

        /// <summary>Raised when the viewer has been placed near the Earth (the HUD greets the viewer).</summary>
        public event Action Started;

        private void Awake()
        {
            if (host != null)
            {
                host.Loaded += OnLoaded;
            }
        }

        private void OnDestroy()
        {
            if (host != null)
            {
                host.Loaded -= OnLoaded;
            }
        }

        private static bool Benchmarking => Array.IndexOf(Environment.GetCommandLineArgs(), PerformanceBenchmark.Option) >= 0;

        private void OnLoaded()
        {
            StartedNearEarth = false;
            var sim = host.Simulation;
            if (!Enabled || Benchmarking || sim == null || rig == null)
            {
                return;
            }
            var earth = sim.IndexOf(UsimBodyId.Naif(EarthNaif));
            if (earth < 0 || !sim.Bodies[earth].HasMeanRadius)
            {
                return;
            }
            var now = sim.Clock.Now;
            var moon = sim.IndexOf(UsimBodyId.Naif(MoonNaif));
            var sun = -1;
            for (var i = 0; i < sim.Bodies.Count && sun < 0; ++i)
            {
                if (sim.Bodies[i].Kind == KindStar)
                {
                    sun = i;
                }
            }
            var toMoon = Direction(sim, moon, earth, now);
            var toSun = Direction(sim, sun, earth, now);
            if (toMoon.Length == 0.0)
            {
                toMoon = toSun.Length > 0.0 ? -toSun : new Double3(0.0, 0.0, 1.0);
            }

            var direction = Placement(toMoon, toSun, turnDegrees * Math.PI / 180.0, raiseDegrees * Math.PI / 180.0);
            rig.JumpTo(earth, startRadii * sim.Bodies[earth].MaxRadiusKm, Math.Atan2(direction.X, direction.Z),
                       Math.Asin(Math.Clamp(direction.Y, -1.0, 1.0)));
            rig.Select(moon >= 0 ? moon : earth);
            if (ship != null && ship.Available)
            {
                ship.SetActive(true);
            }
            StartedNearEarth = true;
            Started?.Invoke();
        }

        /// <summary>
        /// The direction from the Earth to the start (host axes, unit length): opposite the Moon, turned by
        /// <paramref name="turnRad"/> toward the Sun's side and raised by <paramref name="raiseRad"/> above the ecliptic
        /// (host Y). Both inputs are unit directions from the Earth, host axes; the Sun's may be zero.
        /// </summary>
        public static Double3 Placement(Double3 toMoon, Double3 toSun, double turnRad, double raiseRad)
        {
            var up = new Double3(0.0, 1.0, 0.0);
            // A direction in the ecliptic, across the line to the Moon, on the Sun's side.
            var across = new Double3(up.Y * toMoon.Z - up.Z * toMoon.Y, up.Z * toMoon.X - up.X * toMoon.Z,
                                     up.X * toMoon.Y - up.Y * toMoon.X);
            across = across.Length > 1e-9 ? across.Normalized : new Double3(1.0, 0.0, 0.0);
            if (toSun.Dot(across) < 0.0)
            {
                across = -across;
            }
            var flat = (-toMoon * Math.Cos(turnRad) + across * Math.Sin(turnRad)).Normalized;
            return (flat * Math.Cos(raiseRad) + up * Math.Sin(raiseRad)).Normalized;
        }

        // The unit direction from body `from` to body `to`, host axes (zero when either is missing).
        private static Double3 Direction(CoreSimulation sim, int to, int from, UsimInstant now)
        {
            if (to < 0 || from < 0 ||
                !sim.TryState(sim.Bodies[to].Id, sim.Bodies[from].Id, UsimFrame.EclipJ2000, now, out var state))
            {
                return default;
            }
            var v = Double3.From(CoreSimulation.AxesToHost(NativeConstants.AxesUnity, state.PositionKm));
            return v.Length > 0.0 ? v.Normalized : default;
        }
    }
}
