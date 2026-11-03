using System.Globalization;
using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The stylized stars (A-09, SPEC FR-REN-09, docs/ART_DIRECTION.md section 6): the ADR-0011 mapping of V and
    /// B-V, then brighter and larger, and a four-point sparkle on the brightest. StarField.shader draws it in the
    /// stylized style from the global properties set here; they are zero (neutral) while this component is disabled
    /// or the style is realistic. Every step grows with the star's display intensity and scales its color as a
    /// whole, so the order of brightness and color is kept (StarAndRingOrderTests).
    /// </summary>
    public sealed class StylizedStars : StyleEffect
    {
        [SerializeField] private StarField starField;

        // Display choices from docs/ART_DIRECTION.md.
        [SerializeField, Range(1f, 4f)] private float exposure = 1.9f;
        [SerializeField, Range(1f, 3f)] private float size = 1.5f;
        [SerializeField, Range(0f, 2f)] private float sparkleStrength = 0.8f;
        [Tooltip("The display intensity where sparkles start (StarField.DisplayIntensity: 0.35 for the faintest " +
                 "stars drawn, 1.6 for the brightest).")]
        [SerializeField, Range(0f, 2f)] private float sparkleFrom = 0.9f;
        [Tooltip("The length of a sparkle at full strength, star radii.")]
        [SerializeField, Range(0f, 12f)] private float sparkleReach = 5f;

        private static readonly int StarsId = Shader.PropertyToID("_UsimStars");
        private static readonly int SparkleId = Shader.PropertyToID("_UsimStarSparkle");

        // The sparkles are the part the low effects tier leaves out (A-13).
        private bool Sparkles => Tier > EffectsTier.Low;

        protected override Exaggeration[] Describe()
        {
            var stars = new Exaggeration("Stars",
                                         "brighter and larger than the eye sees them; brighter stars still look brighter",
                                         $"brightness x{Format(exposure)}, size x{Format(size)}");
            return Sparkles
                ? new[]
                {
                    stars,
                    new Exaggeration("Star sparkles", "four-point sparkles on the brightest stars",
                                     $"up to {Format(sparkleReach)} star radii long"),
                }
                : new[] { stars };
        }

        private static string Format(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        // The sky is behind every view while the stars are shown.
        public override bool ActiveInView(Camera cam) =>
            Applied && cam != null && starField != null && starField.Visible && starField.DrawnCount > 0;

        protected override void Apply(bool on)
        {
            Shader.SetGlobalVector(StarsId,
                                   on ? new Vector4(exposure - 1f, size - 1f, Sparkles ? sparkleStrength : 0f, sparkleFrom)
                                      : Vector4.zero);
            Shader.SetGlobalVector(SparkleId, on && Sparkles ? new Vector4(sparkleReach, 0f, 0f, 0f) : Vector4.zero);
        }
    }
}
