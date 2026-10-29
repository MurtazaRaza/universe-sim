using System;
using System.Collections.Generic;
using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The register of style effects (A-03, SPEC FR-REN-13): every component that stylizes or exaggerates the view
    /// is listed here by the scene builder, and the HUD's "what is exaggerated" panel shows the entries of those
    /// active in the current view. No effect is active in the realistic style, so the list is empty there.
    /// </summary>
    public sealed class ExaggerationRegister : MonoBehaviour
    {
        [Tooltip("The effect components (each an IStyleEffect). Set by the scene builder.")]
        [SerializeField] private MonoBehaviour[] effects = Array.Empty<MonoBehaviour>();

        /// <summary>The registered effect components.</summary>
        public IReadOnlyList<MonoBehaviour> Effects => effects;

        /// <summary>True when <paramref name="effect"/> is registered.</summary>
        public bool Contains(IStyleEffect effect) => effect is MonoBehaviour b && Array.IndexOf(effects, b) >= 0;

        /// <summary>
        /// Fills <paramref name="into"/> (cleared first) with the entries of the effects that are enabled and
        /// active in the view of <paramref name="cam"/>, in register order.
        /// </summary>
        public void CollectActive(Camera cam, List<Exaggeration> into)
        {
            into.Clear();
            foreach (var behaviour in effects)
            {
                if (behaviour is IStyleEffect effect && behaviour.isActiveAndEnabled && effect.ActiveInView(cam))
                {
                    into.AddRange(effect.Exaggerations);
                }
            }
        }
    }
}
