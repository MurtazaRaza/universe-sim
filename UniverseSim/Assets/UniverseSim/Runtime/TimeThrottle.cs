using System;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The time throttle of the HUD (ADR-0019 decision 5): one slider over the clock's rate presets, reverse on the
    /// left and forward on the right, slowest next to the middle. With <c>n</c> presets the slider has <c>2n</c>
    /// positions: 0 is the fastest reverse rate, <c>n - 1</c> the slowest reverse, <c>n</c> the slowest forward and
    /// <c>2n - 1</c> the fastest forward. Pausing is a separate control, so the middle is never "stopped".
    /// </summary>
    public static class TimeThrottle
    {
        /// <summary>The number of slider positions for <paramref name="presets"/> rate presets.</summary>
        public static int Positions(int presets) => 2 * presets;

        /// <summary>The direction (+1 forward, -1 reverse) and preset index of slider position <paramref name="position"/>.</summary>
        public static (int Sign, int Index) FromPosition(int position, int presets)
        {
            if (presets <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(presets));
            }
            position = Math.Clamp(position, 0, 2 * presets - 1);
            return position >= presets ? (1, position - presets) : (-1, presets - 1 - position);
        }

        /// <summary>The slider position of a direction (<paramref name="sign"/> below 0 for reverse) and preset index.</summary>
        public static int ToPosition(int sign, int index, int presets)
        {
            if (presets <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(presets));
            }
            index = Math.Clamp(index, 0, presets - 1);
            return sign < 0 ? presets - 1 - index : presets + index;
        }
    }
}
