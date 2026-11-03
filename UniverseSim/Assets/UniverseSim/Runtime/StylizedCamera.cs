using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The stylized camera (A-08, docs/ART_DIRECTION.md section 7): bloom, a color grade, a vignette and a deep
    /// indigo sky. A global volume above the scene's, made at runtime, carries the post-processing; its weight is 1
    /// in the stylized style and 0 otherwise, and the sky returns to black. The scene's own volume is the realistic
    /// camera (Neutral tonemapping only; the scene builder turns off the URP template's bloom and vignette).
    /// The bloom threshold sits above the surfaces' brightness cap (StylizedBodyShading), so only atmospheres,
    /// stars and the Sun bloom, never an airless body. The Sun's corona and glare are separate (A-08).
    /// </summary>
    public sealed class StylizedCamera : MonoBehaviour, IStyleEffect
    {
        [SerializeField] private VisualStyleSettings style;
        [Tooltip("The effects tier (A-13): the bloom is lighter at the medium tier and off at the low one.")]
        [SerializeField] private EffectsQuality quality;

        // Display choices from docs/ART_DIRECTION.md.
        [Header("Bloom")]
        [SerializeField, Range(0f, 3f)] private float bloomIntensity = 0.9f;
        [Tooltip("Gamma, as URP's bloom takes it: 1.33 is about 1.9 linear, so with the fixed soft knee of half the " +
                 "threshold nothing below 0.95 blooms.")]
        [SerializeField, Range(0.5f, 2f)] private float bloomThreshold = 1.33f;
        [SerializeField, Range(0f, 1f)] private float bloomScatter = 0.7f;

        [Header("Grade and vignette")]
        [SerializeField, Range(-100f, 100f)] private float saturation = 15f;
        [SerializeField, Range(-100f, 100f)] private float contrast = 8f;
        [SerializeField, Range(0f, 1f)] private float vignette = 0.3f;
        [SerializeField, Range(0.01f, 1f)] private float vignetteSmoothness = 0.5f;

        [Header("Sky")]
        [Tooltip("The camera background in the stylized style; the realistic style keeps it black.")]
        [SerializeField] private Color sky = new Color(0.02f, 0.02f, 0.06f);

        private Volume volume;
        private VolumeProfile profile;
        private Exaggeration[] exaggerations;

        public IReadOnlyList<Exaggeration> Exaggerations => exaggerations ??= Describe();

        private EffectsTier Tier => quality != null ? quality.Tier : EffectsTier.High;

        private Exaggeration[] Describe()
        {
            var list = new List<Exaggeration>();
            if (Tier > EffectsTier.Low)
            {
                list.Add(new Exaggeration("Glow", "atmospheres, stars and the Sun bloom", "bloom " + Format(bloomIntensity)));
            }
            list.Add(new Exaggeration("Colors", "the whole view more saturated and contrasted",
                                      $"saturation +{Format(saturation)}, contrast +{Format(contrast)}"));
            list.Add(new Exaggeration("Corners", "darkened toward the edges of the view", "vignette " + Format(vignette)));
            list.Add(new Exaggeration("Sky", "deep indigo; space is black", "a display color"));
            return list.ToArray();
        }

        /// <summary>The stylized volume's bloom (null before Awake), for tests.</summary>
        public Bloom Bloom => bloom;

        private Bloom bloom;

        private static string Format(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>The volume that carries the stylized post-processing (null before Awake).</summary>
        public Volume Volume => volume;

        // The camera's own effects show in every view.
        public bool ActiveInView(Camera cam) => Applied && cam != null;

        private bool Applied => isActiveAndEnabled && style != null && style.Style == VisualStyle.Stylized;

        private void Awake()
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Stylized camera (runtime)";
            bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(bloomIntensity);
            bloom.threshold.Override(bloomThreshold);
            bloom.scatter.Override(bloomScatter);
            bloom.highQualityFiltering.Override(true);
            var grading = profile.Add<ColorAdjustments>(true);
            grading.saturation.Override(saturation);
            grading.contrast.Override(contrast);
            var corners = profile.Add<Vignette>(true);
            corners.intensity.Override(vignette);
            corners.smoothness.Override(vignetteSmoothness);

            var holder = new GameObject("Stylized camera volume");
            holder.transform.SetParent(transform, false);
            volume = holder.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.sharedProfile = profile;
            volume.weight = 0f;
        }

        private void OnEnable()
        {
            style.Changed += OnStyleChanged;
            if (quality != null)
            {
                quality.Changed += OnTierChanged;
            }
            Apply();
        }

        private void OnDisable()
        {
            if (style != null)
            {
                style.Changed -= OnStyleChanged;
            }
            if (quality != null)
            {
                quality.Changed -= OnTierChanged;
            }
            Apply();
        }

        private void OnTierChanged(EffectsTier _)
        {
            exaggerations = null;
            Apply();
        }

        private void OnDestroy()
        {
            if (profile != null)
            {
                Destroy(profile);
            }
        }

        private void OnStyleChanged(VisualStyle _) => Apply();

        private void Apply()
        {
            var on = Applied;
            if (volume != null)
            {
                volume.weight = on ? 1f : 0f;
            }
            if (bloom != null)
            {
                // The effects tier (A-13): the full bloom at the high tier; at the medium tier from a quarter-size
                // image without the high quality filter; none at the low tier.
                var tier = Tier;
                bloom.active = tier > EffectsTier.Low;
                bloom.highQualityFiltering.Override(tier == EffectsTier.High);
                bloom.downscale.Override(tier == EffectsTier.High ? BloomDownscaleMode.Half : BloomDownscaleMode.Quarter);
            }
            var cam = Camera.main;
            if (cam != null)
            {
                cam.backgroundColor = on ? sky : Color.black;
            }
        }
    }
}
