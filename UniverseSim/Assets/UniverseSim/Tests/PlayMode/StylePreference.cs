using UnityEngine;

namespace UniverseSim.Unity.Tests.PlayMode
{
    /// <summary>
    /// The remembered visual style (A-02) as it was before a fixture changed it. The tests share PlayerPrefs
    /// with the Editor, so a fixture that sets the style keeps the owner's choice and puts it back afterwards.
    /// </summary>
    internal readonly struct StylePreference
    {
        private readonly bool saved;
        private readonly int value;

        private StylePreference(bool saved, int value)
        {
            this.saved = saved;
            this.value = value;
        }

        public static StylePreference Keep() =>
            new StylePreference(PlayerPrefs.HasKey(VisualStyleSettings.PreferenceKey),
                                PlayerPrefs.GetInt(VisualStyleSettings.PreferenceKey));

        public void Restore()
        {
            if (saved)
            {
                PlayerPrefs.SetInt(VisualStyleSettings.PreferenceKey, value);
            }
            else
            {
                PlayerPrefs.DeleteKey(VisualStyleSettings.PreferenceKey);
            }
            PlayerPrefs.Save();
        }
    }
}
