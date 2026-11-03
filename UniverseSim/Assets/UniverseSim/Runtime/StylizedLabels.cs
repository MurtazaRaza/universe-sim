using UnityEngine;
using UnityEngine.UIElements;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The stylized labels (A-10, docs/ART_DIRECTION.md section 8): an icon for the kind of body beside each name
    /// (<see cref="BodyIcon"/>), in the colors of the stylized orbit lines. The HUD creates the icons hidden; this
    /// effect gives the labels layer the "stylized" class, which shows them, while it is on. The label text and its
    /// place are unchanged.
    /// </summary>
    public sealed class StylizedLabels : StyleEffect
    {
        /// <summary>The class the labels layer has while the icons show.</summary>
        public const string StylizedClass = "stylized";

        [SerializeField] private UIDocument hud;

        private VisualElement labels;

        protected override Exaggeration[] Describe() => new[]
        {
            new Exaggeration("Labels", "an icon for the kind of body beside each name", "a display choice"),
        };

        private VisualElement Labels => labels ??= hud != null ? hud.rootVisualElement?.Q<VisualElement>("labels") : null;

        public override bool ActiveInView(Camera cam)
        {
            var layer = Labels;
            if (!Applied || cam == null || layer == null || layer.resolvedStyle.display == DisplayStyle.None)
            {
                return false;
            }
            // Any label shown (the HUD hides an anchor with an inline display of None).
            for (var i = 0; i < layer.childCount; ++i)
            {
                if (layer[i].style.display.value != DisplayStyle.None && layer[i].childCount > 1)
                {
                    return true;
                }
            }
            return false;
        }

        protected override void Apply(bool on) => Labels?.EnableInClassList(StylizedClass, on);

        // The HUD's panel may be built after this component is enabled, so the class is kept in step every frame (a
        // no-op once it is).
        private void LateUpdate() => Labels?.EnableInClassList(StylizedClass, Applied);
    }
}
