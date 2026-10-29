using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The stylized shading of bodies (A-05, SPEC FR-REN-07, docs/ART_DIRECTION.md section 3): a brighter day side
    /// with a cool-to-warm tint, a lifted and tinted night side, a satin sheen, rim light along the day and night
    /// limbs, and a brightness cap below the bloom threshold. BodyLit draws it in the stylized style from the
    /// global properties set here; they are zero (neutral) while this component is disabled or the style is
    /// realistic. The terminator stays where the core's light direction puts it.
    /// </summary>
    public sealed class StylizedBodyShading : MonoBehaviour, IStyleEffect
    {
        [SerializeField] private SimulationHost host;
        [SerializeField] private VisualStyleSettings style;

        // Display choices from docs/ART_DIRECTION.md (colors as Unity colors, as the mockups set them).
        [Header("Day side")]
        [Tooltip("The day side is 1 - (1 - n.l)^p; 1 is Lambert.")]
        [SerializeField, Range(1f, 4f)] private float dayCurve = 2.2f;
        [SerializeField, ColorUsage(false, true)] private Color lightTint = new Color(1.10f, 1.04f, 0.95f);
        [SerializeField, ColorUsage(false, true)] private Color shadeTint = new Color(0.75f, 0.80f, 1.05f);

        [Header("Night side")]
        [SerializeField, Range(0f, 0.5f)] private float nightLift = 0.12f;
        [SerializeField, ColorUsage(false, true)] private Color nightTint = new Color(0.40f, 0.45f, 1.00f);

        [Header("Highlights and rims")]
        [SerializeField, Range(0f, 1f)] private float sheenStrength = 0.2f;
        [SerializeField, Range(1f, 64f)] private float sheenPower = 8f;
        [SerializeField, Range(0f, 2f)] private float rimStrength = 0.55f;
        [SerializeField, Range(0.5f, 8f)] private float rimPower = 2.5f;
        [Tooltip("How far the day rim's color goes from white toward a bright shade of the surface.")]
        [SerializeField, Range(0f, 1f)] private float rimFromSurface = 0.7f;
        [SerializeField, Range(0f, 1f)] private float nightRimStrength = 0.3f;
        [SerializeField, Range(0.5f, 8f)] private float nightRimPower = 3.5f;
        [SerializeField, ColorUsage(false, true)] private Color nightRimColor = new Color(0.35f, 0.65f, 1.00f);

        [Tooltip("The largest channel a surface may reach, below the bloom threshold, so no airless body blooms.")]
        [SerializeField, Range(0.5f, 1f)] private float brightnessCap = 0.95f;

        private static readonly int DayCurveId = Shader.PropertyToID("_UsimDayCurve");
        private static readonly int DayTintId = Shader.PropertyToID("_UsimDayTint");
        private static readonly int ShadeTintId = Shader.PropertyToID("_UsimShadeTint");
        private static readonly int NightId = Shader.PropertyToID("_UsimNight");
        private static readonly int NightTintId = Shader.PropertyToID("_UsimNightTint");
        private static readonly int SheenId = Shader.PropertyToID("_UsimSheen");
        private static readonly int RimId = Shader.PropertyToID("_UsimRim");
        private static readonly int NightRimId = Shader.PropertyToID("_UsimNightRim");
        private static readonly int NightRimColorId = Shader.PropertyToID("_UsimNightRimColor");
        private static readonly int BodyCapId = Shader.PropertyToID("_UsimBodyCap");

        // Bodies smaller than this on screen do not count as showing the shading.
        private const float MinPixels = 3f;

        private Exaggeration[] exaggerations;

        /// <summary>The brightest a stylized surface may be (largest channel, linear).</summary>
        public float BrightnessCap => brightnessCap;

        public IReadOnlyList<Exaggeration> Exaggerations => exaggerations ??= Describe();

        private Exaggeration[] Describe()
        {
            string Percent(float f) => (100f * f).ToString("0", CultureInfo.InvariantCulture) + " %";
            return new[]
            {
                new Exaggeration("Day sides",
                                 "brighter across the disc, warm in full sun and cool toward the terminator",
                                 $"1 - (1 - cos)^{dayCurve.ToString("0.0", CultureInfo.InvariantCulture)} " +
                                 "instead of cos"),
                new Exaggeration("Night sides",
                                 "lifted and tinted blue so they stay readable; real night sides are dark",
                                 Percent(nightLift) + " of full light"),
                new Exaggeration("Surfaces", "a satin sheen toward the light", Percent(sheenStrength) + " at its peak"),
                new Exaggeration("Limbs", "rim light along the sunlit and the night edge",
                                 $"{Percent(rimStrength)} sunlit, {Percent(nightRimStrength)} night"),
            };
        }

        public bool ActiveInView(Camera cam)
        {
            if (!Applied || cam == null)
            {
                return false;
            }
            foreach (var view in host.Views)
            {
                if (view.Info.Kind != BodyKindStar && view.IsShown(cam, MinPixels))
                {
                    return true;
                }
            }
            return false;
        }

        private const int BodyKindStar = 1; // usim::catalog::BodyKind; the Sun draws with its own shader

        private bool Applied => isActiveAndEnabled && style != null && style.Style == VisualStyle.Stylized;

        private void OnEnable()
        {
            style.Changed += OnStyleChanged;
            Apply();
        }

        private void OnDisable()
        {
            if (style != null)
            {
                style.Changed -= OnStyleChanged;
            }
            Apply();
        }

        private void OnValidate()
        {
            exaggerations = null;
            if (Application.isPlaying && isActiveAndEnabled)
            {
                Apply();
            }
        }

        private void OnStyleChanged(VisualStyle _) => Apply();

        // Sets the global properties of the look, or zeroes them (neutral) when the look is off.
        private void Apply()
        {
            var on = Applied;
            Shader.SetGlobalFloat(DayCurveId, on ? dayCurve - 1f : 0f);
            Shader.SetGlobalVector(DayTintId, on ? Offset(lightTint) : Vector4.zero);
            Shader.SetGlobalVector(ShadeTintId, on ? Offset(shadeTint) : Vector4.zero);
            Shader.SetGlobalVector(NightId, new Vector4(on ? nightLift : 0f, 0f, 0f, 0f));
            Shader.SetGlobalColor(NightTintId, on ? nightTint : Color.clear);
            Shader.SetGlobalVector(SheenId, on ? new Vector4(sheenStrength, sheenPower) : Vector4.zero);
            Shader.SetGlobalVector(RimId, on ? new Vector4(rimStrength, rimPower, rimFromSurface) : Vector4.zero);
            Shader.SetGlobalVector(NightRimId, on ? new Vector4(nightRimStrength, nightRimPower) : Vector4.zero);
            Shader.SetGlobalColor(NightRimColorId, on ? nightRimColor : Color.clear);
            Shader.SetGlobalFloat(BodyCapId, on ? brightnessCap : 0f);
        }

        // A tint as the shader's offset from white: the color converted as Unity converts material colors (Linear
        // color space), minus 1.
        private static Vector4 Offset(Color tint)
        {
            var linear = QualitySettings.activeColorSpace == ColorSpace.Linear ? tint.linear : tint;
            return new Vector4(linear.r - 1f, linear.g - 1f, linear.b - 1f, 0f);
        }
    }
}
