using UnityEngine;
using UnityEngine.UIElements;

namespace UniverseSim.Unity
{
    /// <summary>
    /// Keeps the keyboard for moving: the HUD is driven by the mouse. The project-wide input actions bind UI
    /// navigation to WASD and the arrow keys, so without this the movement keys walked the HUD's focus from button to
    /// button until it reached the date field, which then took every key (and Space or Enter clicked whichever button
    /// had the focus). Here only text fields take the focus, and only by a click: buttons, toggles, lists and sliders
    /// never hold it, keyboard navigation never moves it, and the HUD lets it go on Enter, Escape or a click outside.
    /// While a text field has it, hotkeys and movement keys wait (<see cref="Typing(UIDocument)"/>).
    /// </summary>
    public static class HudKeyboard
    {
        // The frame a text field last let go of the keyboard. The key that ended the typing (Enter) is still "pressed
        // this frame" for the input actions, so it counts as typing for this frame and the next.
        private static int releasedFrame = int.MinValue;

        /// <summary>True while a text field of <paramref name="hud"/> has the keyboard: hotkeys and movement keys wait.</summary>
        public static bool Typing(UIDocument hud) => hud != null && Typing(hud.rootVisualElement);

        /// <summary>True while a text field under the panel of <paramref name="root"/> has the keyboard.</summary>
        public static bool Typing(VisualElement root) =>
            InTextField(root?.focusController?.focusedElement as VisualElement) ||
            Time.frameCount - releasedFrame <= 1;

        /// <summary>
        /// Makes every element under <paramref name="root"/> except text fields unable to take the focus, and stops
        /// keyboard navigation (arrows, WASD, Tab) from moving it. Call it once the tree is built; elements added later
        /// are labels and panels, which never take the focus.
        /// </summary>
        public static void KeepForMovement(VisualElement root)
        {
            if (root == null)
            {
                return;
            }
            root.Query<VisualElement>().ForEach(element =>
            {
                if (!InTextField(element))
                {
                    element.focusable = false;
                }
            });
            // With nothing focused the event system sends navigation to the panel's own root, above the document's, so
            // the guard sits there. Method groups, so registering again (the HUD is enabled again) adds nothing.
            if (root.panel != null)
            {
                GuardPanel(root.panel);
            }
            root.RegisterCallback<AttachToPanelEvent>(OnAttach);
        }

        private static void OnAttach(AttachToPanelEvent e) => GuardPanel(e.destinationPanel);

        private static void GuardPanel(IPanel panel) =>
            panel?.visualTree.RegisterCallback<NavigationMoveEvent>(IgnoreNavigation, TrickleDown.TrickleDown);

        /// <summary>Gives the keyboard back to moving: whatever under the panel of <paramref name="root"/> has the focus lets it go.</summary>
        public static void Release(VisualElement root)
        {
            var focused = root?.focusController?.focusedElement;
            if (focused == null)
            {
                return;
            }
            if (InTextField(focused as VisualElement))
            {
                releasedFrame = Time.frameCount;
            }
            focused.Blur();
        }

        private static void IgnoreNavigation(NavigationMoveEvent e) =>
            (e.currentTarget as VisualElement)?.panel?.focusController?.IgnoreEvent(e);

        private static bool InTextField(VisualElement element)
        {
            for (var e = element; e != null; e = e.parent)
            {
                if (e is TextField)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
