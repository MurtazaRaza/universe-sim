using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The stylized treatment of the mission surface maps (A-06, SPEC FR-REN-11 and section 11.1,
    /// docs/ART_DIRECTION.md section 4): a Kuwahara smoothing into soft patches along the map's real features, more
    /// saturation and a softer luminance contrast, each keeping the texel's hue. BodyLit applies it in the stylized
    /// style (Shaders/SurfaceMapTreatment.hlsl) from the global properties set here; they are zero (the map as
    /// published) while this component is disabled or the style is realistic.
    /// </summary>
    public sealed class SurfaceMapTreatment : MonoBehaviour, IStyleEffect
    {
        [SerializeField] private SimulationHost host;
        [SerializeField] private VisualStyleSettings style;

        // Display choices from docs/ART_DIRECTION.md.
        [Tooltip("Kuwahara radius in texels of the mip it reads (0 = no smoothing).")]
        [SerializeField, Range(0f, 3f)] private float paintRadius = 1.5f;
        [Tooltip("How many mips coarser than the screen needs the smoothing reads; also the finest mip it reads.")]
        [SerializeField, Range(0f, 3f)] private float paintLevel = 1f;
        [SerializeField, Range(0f, 3f)] private float saturation = 1.6f;
        [Tooltip("Exponent of the luminance about the pivot; below 1 lifts shadows and softens highlights.")]
        [SerializeField, Range(0.5f, 1.5f)] private float contrast = 0.9f;
        [SerializeField, Range(0.05f, 1f)] private float contrastPivot = 0.4f;

        private static readonly int PaintId = Shader.PropertyToID("_UsimMapPaint");
        private static readonly int GradeId = Shader.PropertyToID("_UsimMapGrade");

        private const float MinPixels = 3f;

        private Exaggeration[] exaggerations;

        /// <summary>The global <c>_UsimMapPaint</c> value of the look (for SurfaceMapTreatmentTests).</summary>
        public Vector4 PaintParameters => new Vector4(paintRadius, paintLevel, paintLevel, 0f);

        /// <summary>The global <c>_UsimMapGrade</c> value of the look (for SurfaceMapTreatmentTests).</summary>
        public Vector4 GradeParameters => new Vector4(saturation - 1f, contrast - 1f, contrastPivot, 0f);

        public IReadOnlyList<Exaggeration> Exaggerations => exaggerations ??= new[]
        {
            new Exaggeration("Surface maps",
                             "colors more vivid, shadows lifted and fine detail smoothed into patches; " +
                             "each color keeps its hue",
                             $"saturation x{saturation.ToString("0.0", CultureInfo.InvariantCulture)}, " +
                             $"contrast {contrast.ToString("0.0#", CultureInfo.InvariantCulture)}"),
        };

        public bool ActiveInView(Camera cam)
        {
            if (!Applied || cam == null)
            {
                return false;
            }
            foreach (var view in host.Views)
            {
                if (view.SurfaceMap != null && view.IsShown(cam, MinPixels))
                {
                    return true;
                }
            }
            return false;
        }

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

        private void Apply()
        {
            var on = Applied;
            Shader.SetGlobalVector(PaintId, on ? PaintParameters : Vector4.zero);
            Shader.SetGlobalVector(GradeId, on ? GradeParameters : Vector4.zero);
        }
    }
}
