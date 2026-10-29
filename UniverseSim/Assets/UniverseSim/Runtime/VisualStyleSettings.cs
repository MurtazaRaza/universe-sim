using System;
using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>How the viewer draws the data (SPEC FR-REN-12, ADR-0015).</summary>
    public enum VisualStyle
    {
        /// <summary>The default: the art direction of SPEC section 11.1 and docs/ART_DIRECTION.md.</summary>
        Stylized = 0,

        /// <summary>Every exaggeration and artist's impression off, imagery as its source publishes it.</summary>
        Realistic = 1,
    }

    /// <summary>
    /// The host-wide visual style (A-02): stylized by default, or realistic, remembered between sessions.
    /// Shaders read the global keyword <see cref="StylizedKeyword"/> (enabled in the stylized style), and
    /// components listen for <see cref="Changed"/>. The style changes only how things are drawn, never a
    /// position, an orientation, the light direction or a value the UI shows, and it is independent of the
    /// scale mode.
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public sealed class VisualStyleSettings : MonoBehaviour
    {
        /// <summary>The PlayerPrefs key that remembers the style (0 stylized, 1 realistic).</summary>
        public const string PreferenceKey = "UniverseSim.VisualStyle";

        /// <summary>Global shader keyword, enabled in the stylized style and disabled in the realistic one.</summary>
        public const string StylizedKeyword = "USIM_STYLE_STYLIZED";

        /// <summary>The style in use.</summary>
        public VisualStyle Style { get; private set; } = VisualStyle.Stylized;

        /// <summary>Raised after the style changes, with the new style.</summary>
        public event Action<VisualStyle> Changed;

        // In Awake, before any component's Start, so effects set themselves up in the remembered style.
        private void Awake()
        {
            Style = LoadPreference();
            ApplyKeyword();
        }

        /// <summary>Switches to <paramref name="style"/> and remembers it for the next session.</summary>
        public void SetStyle(VisualStyle style)
        {
            if (style == Style)
            {
                return;
            }
            Style = style;
            SavePreference(style);
            ApplyKeyword();
            Changed?.Invoke(style);
        }

        /// <summary>The indicator text for a style.</summary>
        public static string DisplayName(VisualStyle style) =>
            style == VisualStyle.Realistic ? "Realistic style" : "Stylized style";

        /// <summary>The remembered style; the stylized style when none is remembered or the value is unknown.</summary>
        public static VisualStyle LoadPreference() =>
            PlayerPrefs.GetInt(PreferenceKey, (int)VisualStyle.Stylized) == (int)VisualStyle.Realistic
                ? VisualStyle.Realistic
                : VisualStyle.Stylized;

        /// <summary>Remembers <paramref name="style"/> for the next session.</summary>
        public static void SavePreference(VisualStyle style)
        {
            PlayerPrefs.SetInt(PreferenceKey, (int)style);
            PlayerPrefs.Save();
        }

        private void ApplyKeyword()
        {
            if (Style == VisualStyle.Stylized)
            {
                Shader.EnableKeyword(StylizedKeyword);
            }
            else
            {
                Shader.DisableKeyword(StylizedKeyword);
            }
        }
    }
}
