using System.Globalization;
using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The stylized rings (A-09, SPEC FR-REN-10, docs/ART_DIRECTION.md section 6): the optical depth also sets the
    /// color, from a sparse to a dense palette color, and the rings are more opaque and brighter. The radii, the
    /// optical depth profile, the slant opacity, the lit and unlit sides and the planet's shadow are unchanged.
    /// Rings.shader draws it in the stylized style from the global properties set here; they are zero (neutral)
    /// while this component is disabled or the style is realistic. Every mapping rises with the optical depth, so
    /// a denser region never looks thinner (StarAndRingOrderTests).
    /// </summary>
    public sealed class StylizedRings : StyleEffect
    {
        [SerializeField] private RingSystems rings;

        // Display choices from docs/ART_DIRECTION.md (colors as Unity colors, as the mockups set them).
        [SerializeField] private Color sparse = new Color(0.70f, 0.58f, 0.45f);
        [SerializeField] private Color dense = new Color(1.00f, 0.90f, 0.68f);
        [Tooltip("The optical depth at which a region takes the dense color.")]
        [SerializeField, Range(0.1f, 5f)] private float denseAt = 2f;
        [Tooltip("Optical depth multiplier for the opacity.")]
        [SerializeField, Range(1f, 4f)] private float opacityGain = 1.5f;
        [SerializeField, Range(1f, 3f)] private float brightness = 1.2f;

        // Rings smaller than this on screen do not count as showing the treatment.
        private const float MinPixels = 3f;

        private static readonly int RingsId = Shader.PropertyToID("_UsimRings");
        private static readonly int SparseId = Shader.PropertyToID("_UsimRingSparse");
        private static readonly int DenseId = Shader.PropertyToID("_UsimRingDense");

        protected override Exaggeration[] Describe() => new[]
        {
            new Exaggeration("Rings",
                             "colored from sparse to dense regions, more opaque and brighter; denser regions still " +
                             "look denser",
                             $"optical depth x{Format(opacityGain)} for opacity, brightness x{Format(brightness)}"),
        };

        private static string Format(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        public override bool ActiveInView(Camera cam) => Applied && cam != null && rings != null && rings.IsShown(cam, MinPixels);

        protected override void Apply(bool on)
        {
            Shader.SetGlobalVector(RingsId, on ? new Vector4(denseAt, opacityGain - 1f, brightness - 1f, 0f) : Vector4.zero);
            // Converted like material colors (Linear color space), as the mockups set them.
            Shader.SetGlobalColor(SparseId, on ? sparse : Color.clear);
            Shader.SetGlobalColor(DenseId, on ? dense : Color.clear);
        }
    }
}
