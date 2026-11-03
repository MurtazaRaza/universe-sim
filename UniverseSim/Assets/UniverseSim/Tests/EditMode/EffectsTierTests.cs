using NUnit.Framework;
using UnityEngine;

namespace UniverseSim.Unity.Tests.EditMode
{
    /// <summary>
    /// The automatic effects tier (A-13, NFR-11): frame times over the budget step the tier down after a while, never
    /// below low; frame times well under it step it back up after longer; nothing changes while the frame times
    /// settle after a start or a change; a hitch or a pause is ignored. The fixed choice is remembered.
    /// </summary>
    public class EffectsTierTests
    {
        // Feeds `seconds` of frames of `frameSeconds` each; returns how many times the tier changed.
        private static int Feed(AdaptiveTier adaptive, float frameSeconds, float seconds)
        {
            var changes = 0;
            for (var t = 0f; t < seconds; t += frameSeconds)
            {
                if (adaptive.Observe(frameSeconds))
                {
                    ++changes;
                }
            }
            return changes;
        }

        [Test]
        public void SlowFramesStepTheTierDownAfterTheSettlingTime()
        {
            var adaptive = new AdaptiveTier();
            adaptive.Reset(EffectsTier.High);
            const float slow = 0.025f; // 40 frames per second against a 60 per second budget
            Assert.That(Feed(adaptive, slow, adaptive.SettleSeconds * 0.9f), Is.Zero, "settling");
            Assert.That(adaptive.Tier, Is.EqualTo(EffectsTier.High));
            Assert.That(Feed(adaptive, slow, adaptive.SettleSeconds * 0.1f + adaptive.DownAfterSeconds + 0.1f), Is.EqualTo(1));
            Assert.That(adaptive.Tier, Is.EqualTo(EffectsTier.Medium));
            Assert.That(Feed(adaptive, slow, adaptive.SettleSeconds + adaptive.DownAfterSeconds + 0.2f), Is.EqualTo(1));
            Assert.That(adaptive.Tier, Is.EqualTo(EffectsTier.Low));
            Assert.That(Feed(adaptive, slow, 10f), Is.Zero, "nothing below low");
            Assert.That(adaptive.SmoothedSeconds, Is.EqualTo(slow).Within(1e-4f));
        }

        [Test]
        public void FastFramesStepItBackUpOnlyAfterLonger()
        {
            var adaptive = new AdaptiveTier();
            adaptive.Reset(EffectsTier.Low);
            const float fast = 0.005f; // well under 60 % of the budget
            Assert.That(Feed(adaptive, fast, adaptive.SettleSeconds + adaptive.UpAfterSeconds * 0.9f), Is.Zero);
            Assert.That(Feed(adaptive, fast, adaptive.UpAfterSeconds * 0.2f), Is.EqualTo(1));
            Assert.That(adaptive.Tier, Is.EqualTo(EffectsTier.Medium));

            // Frames between 60 % and 100 % of the budget hold the tier.
            Assert.That(Feed(adaptive, 0.8f / 60f, 30f), Is.Zero);
            Assert.That(adaptive.Tier, Is.EqualTo(EffectsTier.Medium));
        }

        [Test]
        public void HitchesAndPausesAreIgnored()
        {
            var adaptive = new AdaptiveTier();
            adaptive.Reset(EffectsTier.High);
            Feed(adaptive, 0.01f, adaptive.SettleSeconds + 1f);
            for (var i = 0; i < 20; ++i)
            {
                Assert.That(adaptive.Observe(2f), Is.False); // a stalled window
                Assert.That(adaptive.Observe(0f), Is.False); // a paused frame
            }
            Assert.That(adaptive.Tier, Is.EqualTo(EffectsTier.High));
            Assert.That(adaptive.SmoothedSeconds, Is.EqualTo(0.01f).Within(1e-4f));
        }

        [Test]
        public void TheFixedChoiceIsRemembered()
        {
            var had = PlayerPrefs.HasKey(EffectsQuality.PreferenceKey);
            var previous = PlayerPrefs.GetString(EffectsQuality.PreferenceKey, "auto");
            try
            {
                EffectsQuality.SavePreference(EffectsTier.Medium);
                Assert.That(EffectsQuality.LoadPreference(), Is.EqualTo(EffectsTier.Medium));
                EffectsQuality.SavePreference(null);
                Assert.That(EffectsQuality.LoadPreference(), Is.Null);
                Assert.That(EffectsQuality.DisplayName(EffectsTier.Low), Is.EqualTo("low"));
            }
            finally
            {
                if (had)
                {
                    PlayerPrefs.SetString(EffectsQuality.PreferenceKey, previous);
                }
                else
                {
                    PlayerPrefs.DeleteKey(EffectsQuality.PreferenceKey);
                }
                PlayerPrefs.Save();
            }
        }
    }
}
