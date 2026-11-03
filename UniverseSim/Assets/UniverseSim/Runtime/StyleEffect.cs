using System.Collections.Generic;
using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>
    /// Base of a style effect that is a look switched on and off with the visual style (Track A, ADR-0015): the
    /// look is applied while the component is enabled and the style is stylized, and removed otherwise (its shader
    /// globals back to neutral, the renderers it owns disabled). A subclass describes what it exaggerates, says
    /// whether that shows in a view, and applies or removes its look in <see cref="Apply"/>.
    /// </summary>
    public abstract class StyleEffect : MonoBehaviour, IStyleEffect
    {
        [SerializeField] private VisualStyleSettings style;
        [Tooltip("The effects tier (A-13); without one the effect draws at the high tier.")]
        [SerializeField] private EffectsQuality quality;

        private Exaggeration[] exaggerations;

        public IReadOnlyList<Exaggeration> Exaggerations => exaggerations ??= Describe();

        /// <summary>The effects tier in use (A-13): costly parts of the look scale down below the high tier.</summary>
        protected EffectsTier Tier => quality != null ? quality.Tier : EffectsTier.High;

        /// <inheritdoc/>
        public abstract bool ActiveInView(Camera cam);

        /// <summary>True while the look is on: the component is enabled and the style is stylized.</summary>
        protected bool Applied => isActiveAndEnabled && style != null && style.Style == VisualStyle.Stylized;

        /// <summary>The entries for the "what is exaggerated" panel, built from the current parameters.</summary>
        protected abstract Exaggeration[] Describe();

        /// <summary>Applies the look (<paramref name="on"/>) or removes it, leaving the realistic view.</summary>
        protected abstract void Apply(bool on);

        protected virtual void OnEnable()
        {
            if (style != null)
            {
                style.Changed += OnStyleChanged;
            }
            if (quality != null)
            {
                quality.Changed += OnTierChanged;
            }
            Refresh();
        }

        protected virtual void OnDisable()
        {
            if (style != null)
            {
                style.Changed -= OnStyleChanged;
            }
            if (quality != null)
            {
                quality.Changed -= OnTierChanged;
            }
            Refresh();
        }

        // A new tier may change the look and what it exaggerates.
        private void OnTierChanged(EffectsTier _)
        {
            exaggerations = null;
            Refresh();
        }

        protected virtual void OnValidate()
        {
            exaggerations = null;
            if (Application.isPlaying && isActiveAndEnabled)
            {
                Refresh();
            }
        }

        private void OnStyleChanged(VisualStyle _) => Refresh();

        /// <summary>Applies or removes the look to match <see cref="Applied"/>.</summary>
        protected void Refresh() => Apply(Applied);
    }
}
