using System;
using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>
    /// How fast the ship seems to move (ADR-0019 decision 2), for the speed cues, the engine sound and the logbook: its
    /// speed in heights above the surface per second, measured from the ship's position frame to frame. That is the
    /// rate at which the view changes, the quantity the ship's own controls are scaled by (its cruise covers its height
    /// in under two seconds), not a physical speed. A change of anchor body resets the measure. Each user keeps its own.
    /// </summary>
    public sealed class ShipPace
    {
        private int lastAnchor = -1;
        private Double3 lastKm;
        private double smoothed;

        /// <summary>Heights per second, smoothed over about a tenth of a second (0 while the ship is not flying).</summary>
        public double HeightsPerSecond => smoothed;

        /// <summary>
        /// The share of a trip: 0 outside one, rising to 1 in its middle and back to 0 on arrival (sin of pi times the
        /// trip's progress).
        /// </summary>
        public float Trip { get; private set; }

        /// <summary>Takes this frame's measure; <paramref name="dt"/> in real seconds.</summary>
        public void Update(ShipPilot ship, float dt)
        {
            Trip = ship != null && ship.State == ShipPilot.Mode.Traveling ? Mathf.Sin(Mathf.PI * ship.TravelProgress) : 0f;
            if (ship == null || !ship.Active || ship.State != ShipPilot.Mode.Flying || dt <= 0f)
            {
                lastAnchor = ship != null ? ship.AnchorIndex : -1;
                lastKm = ship != null ? ship.PositionKm : default;
                smoothed = 0.0;
                return;
            }
            var raw = 0.0;
            if (ship.AnchorIndex == lastAnchor)
            {
                raw = (ship.PositionKm - lastKm).Length / dt / Math.Max(ship.AltitudeKm, 1.0);
            }
            lastAnchor = ship.AnchorIndex;
            lastKm = ship.PositionKm;
            smoothed += (raw - smoothed) * (1.0 - Math.Exp(-dt / 0.1));
        }
    }
}
