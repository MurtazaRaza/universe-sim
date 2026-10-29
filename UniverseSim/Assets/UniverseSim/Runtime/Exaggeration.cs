using System.Collections.Generic;
using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>
    /// One thing the stylized style exaggerates or stylizes (A-03, SPEC FR-REN-13), as the "what is exaggerated"
    /// panel lists it. Every value is a display choice, never data.
    /// </summary>
    public readonly struct Exaggeration
    {
        public Exaggeration(string subject, string change, string amount)
        {
            Subject = subject;
            Change = change;
            Amount = amount;
        }

        /// <summary>What looks different, for example "Night sides".</summary>
        public string Subject { get; }

        /// <summary>How it differs from the real view, for example "lifted so they stay readable".</summary>
        public string Change { get; }

        /// <summary>By how much, for example "12 % of the day-side light".</summary>
        public string Amount { get; }

        public override string ToString() => $"{Subject}: {Change} ({Amount})";
    }

    /// <summary>
    /// A component that stylizes or exaggerates the view (A-03). It declares what it changes and by how much, and
    /// says whether that shows in the current view. Every such component is listed in the scene's
    /// <see cref="ExaggerationRegister"/>; a test fails otherwise. Effects are off in the realistic style, so none is
    /// active there.
    /// </summary>
    public interface IStyleEffect
    {
        /// <summary>Everything the effect changes while it is on.</summary>
        IReadOnlyList<Exaggeration> Exaggerations { get; }

        /// <summary>
        /// True when the effect is on and shows in what <paramref name="cam"/> sees; always false in the realistic
        /// style.
        /// </summary>
        bool ActiveInView(Camera cam);
    }
}
