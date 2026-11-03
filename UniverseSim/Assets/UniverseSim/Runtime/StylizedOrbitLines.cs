using System.Globalization;
using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The stylized orbit lines (A-10, SPEC FR-REN-06 and FR-REN-13, docs/ART_DIRECTION.md section 8): bolder lines
    /// colored by the kind of orbit, brightest just behind each body and fading back along its path so the
    /// direction of motion shows, brighter for the focused body's orbit and the orbits around it, and dashed in their
    /// own color where the positions are approximate (ADR-0008). Every line still follows the core's path; only its
    /// width, color and opacity change. Line.shader draws it in the stylized style from the global properties set
    /// here; they are zero (neutral) while this component is disabled or the style is realistic.
    /// </summary>
    public sealed class StylizedOrbitLines : StyleEffect
    {
        [SerializeField] private OrbitLines orbitLines;

        // Display choices from docs/ART_DIRECTION.md (colors as Unity colors; alpha is the line's opacity).
        [Tooltip("Half the line width, pixels (the realistic lines are 0.75).")]
        [SerializeField, Range(0.5f, 4f)] private float halfWidthPixels = 1.3f;
        [SerializeField] private Color planet = new Color(0.40f, 0.72f, 1.00f, 0.80f);
        [SerializeField] private Color dwarfPlanet = new Color(0.78f, 0.60f, 1.00f, 0.80f);
        [SerializeField] private Color moon = new Color(0.40f, 0.92f, 0.80f, 0.65f);
        [SerializeField] private Color approximate = new Color(1.00f, 0.62f, 0.28f, 0.80f);
        [Tooltip("Opacity at the far end of a line's fade, just ahead of its body, relative to just behind it.")]
        [SerializeField, Range(0f, 1f)] private float fadeTo = 0.25f;
        [Tooltip("Opacity multiplier of the focused body's orbit and the orbits around the focused body.")]
        [SerializeField, Range(1f, 3f)] private float focusBoost = 1.35f;
        [Tooltip("Opacity multiplier of the other lines.")]
        [SerializeField, Range(0f, 1f)] private float otherLines = 0.75f;
        [Tooltip("Points per dash of an approximate line (the core samples a revolution in about 0.5 degree steps).")]
        [SerializeField, Range(2f, 64f)] private float dashPoints = 8f;
        [SerializeField, Range(0.1f, 0.9f)] private float dashDrawn = 0.6f;

        private static readonly int StyleId = Shader.PropertyToID("_UsimOrbitStyle");
        private static readonly int DashId = Shader.PropertyToID("_UsimOrbitDash");
        private static readonly int PlanetId = Shader.PropertyToID("_UsimOrbitPlanet");
        private static readonly int DwarfId = Shader.PropertyToID("_UsimOrbitDwarf");
        private static readonly int MoonId = Shader.PropertyToID("_UsimOrbitMoon");
        private static readonly int ApproximateId = Shader.PropertyToID("_UsimOrbitApproximate");

        /// <summary>The color of a line of <paramref name="category"/> (a Unity color; alpha is its opacity).</summary>
        public Color ColorOf(OrbitCategory category) => category switch
        {
            OrbitCategory.DwarfPlanet => dwarfPlanet,
            OrbitCategory.Moon => moon,
            OrbitCategory.Approximate => approximate,
            _ => planet,
        };

        /// <summary>True when approximate lines are dashed.</summary>
        public bool DashesApproximateLines => dashPoints > 0f && dashDrawn < 1f;

        protected override Exaggeration[] Describe() => new[]
        {
            new Exaggeration("Orbit lines",
                             "bolder, colored by the kind of body, fading behind each body to show which way it " +
                             "moves, and brighter around the focused body",
                             $"{(2f * halfWidthPixels).ToString("0.#", CultureInfo.InvariantCulture)} px wide"),
        };

        public override bool ActiveInView(Camera cam) =>
            Applied && cam != null && orbitLines != null && orbitLines.Visible && orbitLines.DrawnCount > 0;

        protected override void Apply(bool on)
        {
            Shader.SetGlobalVector(StyleId, on ? new Vector4(halfWidthPixels, fadeTo, focusBoost, otherLines) : Vector4.zero);
            Shader.SetGlobalVector(DashId, on ? new Vector4(dashPoints, dashDrawn, 0f, 0f) : Vector4.zero);
            // Converted like material colors (Linear color space); alpha stays as given.
            Shader.SetGlobalColor(PlanetId, on ? planet : Color.clear);
            Shader.SetGlobalColor(DwarfId, on ? dwarfPlanet : Color.clear);
            Shader.SetGlobalColor(MoonId, on ? moon : Color.clear);
            Shader.SetGlobalColor(ApproximateId, on ? approximate : Color.clear);
        }
    }
}
