using System;
using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>How much of the stylized style's costlier work is drawn (A-13, NFR-11, docs/ART_DIRECTION.md section 13).</summary>
    public enum EffectsTier
    {
        /// <summary>No bloom, no map smoothing, no star sparkles.</summary>
        Low = 0,
        /// <summary>Bloom at lower quality, no map smoothing.</summary>
        Medium = 1,
        /// <summary>Everything as chosen in A-01.</summary>
        High = 2,
    }

    /// <summary>
    /// The frame-time rule that picks the tier in automatic mode (A-13): a smoothed frame time over the budget for a
    /// while steps the tier down, well under it for longer steps it back up, and nothing changes for a moment after a
    /// change or a start, while the frame times settle. Pure logic, fed one frame at a time.
    /// </summary>
    public sealed class AdaptiveTier
    {
        /// <summary>The frame time to stay under, seconds (1/60 s for NFR-11).</summary>
        public float BudgetSeconds = 1f / 60f;
        /// <summary>Over the budget this long steps the tier down, seconds.</summary>
        public float DownAfterSeconds = 1.5f;
        /// <summary>Under this share of the budget this long steps it up, seconds.</summary>
        public float UpBelowShare = 0.6f;
        public float UpAfterSeconds = 8f;
        /// <summary>No change this long after a start or a change, seconds.</summary>
        public float SettleSeconds = 2f;
        /// <summary>The smoothing time constant of the frame time, seconds.</summary>
        public float SmoothingSeconds = 0.5f;

        public EffectsTier Tier { get; private set; } = EffectsTier.High;

        /// <summary>The smoothed frame time, seconds (0 before the first frame).</summary>
        public float SmoothedSeconds { get; private set; }

        private float settle;
        private float over;
        private float under;

        /// <summary>Starts again at <paramref name="tier"/>, forgetting the frame times.</summary>
        public void Reset(EffectsTier tier)
        {
            Tier = tier;
            SmoothedSeconds = 0f;
            settle = SettleSeconds;
            over = 0f;
            under = 0f;
        }

        /// <summary>Takes one frame's duration; true when the tier changed.</summary>
        public bool Observe(float frameSeconds)
        {
            if (!(frameSeconds > 0f) || frameSeconds > 1f)
            {
                return false; // a pause, a hitch or a stalled window says nothing about the steady cost
            }
            var k = 1f - Mathf.Exp(-frameSeconds / SmoothingSeconds);
            SmoothedSeconds = SmoothedSeconds > 0f ? SmoothedSeconds + (frameSeconds - SmoothedSeconds) * k : frameSeconds;
            if (settle > 0f)
            {
                settle -= frameSeconds;
                return false;
            }
            over = SmoothedSeconds > BudgetSeconds ? over + frameSeconds : 0f;
            under = SmoothedSeconds < BudgetSeconds * UpBelowShare ? under + frameSeconds : 0f;
            if (over >= DownAfterSeconds && Tier > EffectsTier.Low)
            {
                Step(Tier - 1);
                return true;
            }
            if (under >= UpAfterSeconds && Tier < EffectsTier.High)
            {
                Step(Tier + 1);
                return true;
            }
            return false;
        }

        private void Step(EffectsTier tier)
        {
            Tier = tier;
            settle = SettleSeconds;
            over = 0f;
            under = 0f;
        }
    }

    /// <summary>
    /// The effects tier in use (A-13): automatic by default, from the frame times (<see cref="AdaptiveTier"/>), or
    /// fixed. Effects that cost the most read it and scale down before the frame rate drops; it never changes a
    /// value, a position or which bodies show an effect that implies a property. The choice is remembered.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class EffectsQuality : MonoBehaviour
    {
        public const string PreferenceKey = "UniverseSim.EffectsQuality";

        [Tooltip("The frame rate to keep (NFR-11).")]
        [SerializeField] private float targetFramesPerSecond = 60f;

        private readonly AdaptiveTier adaptive = new AdaptiveTier();

        /// <summary>The tier in use.</summary>
        public EffectsTier Tier { get; private set; } = EffectsTier.High;

        /// <summary>True when the tier follows the frame times.</summary>
        public bool Automatic { get; private set; } = true;

        /// <summary>The smoothed frame time the automatic mode sees, seconds.</summary>
        public float SmoothedFrameSeconds => adaptive.SmoothedSeconds;

        /// <summary>Raised when the tier changes.</summary>
        public event Action<EffectsTier> Changed;

        private void Awake()
        {
            adaptive.BudgetSeconds = 1f / Mathf.Max(targetFramesPerSecond, 1f);
            if (Application.isBatchMode)
            {
                // Automated runs (the test suites) render offscreen with irregular frame times, which say nothing about
                // the viewer's cost: they keep the high tier unless a test sets another.
                SetFixed(EffectsTier.High, remember: false);
                return;
            }
            var saved = LoadPreference();
            if (saved.HasValue)
            {
                SetFixed(saved.Value, remember: false);
            }
            else
            {
                SetAutomatic(remember: false);
            }
        }

        private void Update()
        {
            if (Automatic && adaptive.Observe(Time.unscaledDeltaTime))
            {
                SetTier(adaptive.Tier);
            }
        }

        /// <summary>Lets the frame times pick the tier, starting from the highest.</summary>
        public void SetAutomatic(bool remember = true)
        {
            Automatic = true;
            adaptive.Reset(EffectsTier.High);
            SetTier(EffectsTier.High);
            if (remember)
            {
                SavePreference(null);
            }
        }

        /// <summary>Uses <paramref name="tier"/> whatever the frame times.</summary>
        public void SetFixed(EffectsTier tier, bool remember = true)
        {
            Automatic = false;
            SetTier(tier);
            if (remember)
            {
                SavePreference(tier);
            }
        }

        private void SetTier(EffectsTier tier)
        {
            if (tier == Tier)
            {
                return;
            }
            Tier = tier;
            Changed?.Invoke(tier);
        }

        /// <summary>The remembered fixed tier, or null for automatic.</summary>
        public static EffectsTier? LoadPreference()
        {
            var value = PlayerPrefs.GetString(PreferenceKey, "auto");
            return Enum.TryParse<EffectsTier>(value, out var tier) ? tier : (EffectsTier?)null;
        }

        public static void SavePreference(EffectsTier? tier)
        {
            PlayerPrefs.SetString(PreferenceKey, tier.HasValue ? tier.Value.ToString() : "auto");
            PlayerPrefs.Save();
        }

        /// <summary>The tier's name for the HUD: "high", "medium" or "low".</summary>
        public static string DisplayName(EffectsTier tier) => tier.ToString().ToLowerInvariant();
    }
}
