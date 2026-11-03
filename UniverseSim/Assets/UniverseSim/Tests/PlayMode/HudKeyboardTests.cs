using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UniverseSim.Unity.Tests.PlayMode
{
    /// <summary>
    /// The keyboard stays with movement (<see cref="HudKeyboard"/>): the HUD's buttons, toggles and lists never take the
    /// focus, the navigation that WASD, the arrows and Tab send never moves it (it used to walk it into the date field,
    /// which then took every key), and the date field gives the keyboard back on Enter with a good date and on Escape.
    /// </summary>
    public class HudKeyboardTests
    {
        private StylePreference preference;
        private SimulationHost host;
        private UIDocument document;
        private VisualElement root;
        private TextField dateField;
        private HudController hud;

        [OneTimeSetUp]
        public void KeepPreference() => preference = StylePreference.Keep();

        [OneTimeTearDown]
        public void RestorePreference() => preference.Restore();

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            VisualStyleSettings.SavePreference(VisualStyle.Stylized);
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            host = Object.FindAnyObjectByType<SimulationHost>();
            Assert.That(host.Simulation, Is.Not.Null, host.LoadError);
            host.Simulation.SetPaused(true);
            hud = Object.FindAnyObjectByType<HudController>();
            document = hud.GetComponent<UIDocument>();
            root = document.rootVisualElement;
            dateField = root.Q<TextField>("date-entry");
            Assert.That(dateField, Is.Not.Null);
            yield return null;
        }

        private Focusable Focused => root.focusController.focusedElement;

        private static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; ++i)
            {
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator OnlyTheDateFieldTakesTheFocus()
        {
            var controls = 0;
            root.Query<Button>().ForEach(b => { ++controls; Assert.That(b.focusable, Is.False, b.name); });
            root.Query<Toggle>().ForEach(t => { ++controls; Assert.That(t.focusable, Is.False, t.name); });
            root.Query<DropdownField>().ForEach(d => { ++controls; Assert.That(d.focusable, Is.False, d.name); });
            root.Query<SliderInt>().ForEach(s => { ++controls; Assert.That(s.focusable, Is.False, s.name); });
            root.Query<Slider>().ForEach(s => { ++controls; Assert.That(s.focusable, Is.False, s.name); });
            Assert.That(controls, Is.GreaterThan(12), "the HUD's controls were found");
            Assert.That(Focused, Is.Null);
            Assert.That(HudKeyboard.Typing(document), Is.False);

            // The date field is behind "Set date" (ADR-0019), which opens its row and gives it the keyboard.
            Assert.That(root.Q("date-row").resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "the date row starts closed");
            hud.ShowDateEntry(true);
            yield return Frames(3);
            Assert.That(root.Q("date-row").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(HudKeyboard.Typing(document), "opening the date row gives its field the keyboard");
        }

        [UnityTest]
        public IEnumerator MovementKeysNeverMoveTheFocus()
        {
            // What the event system sends for W, S, A, D, the arrows and Tab: with nothing focused, to the panel's root.
            var moves = new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
            for (var i = 0; i < 12; ++i)
            {
                foreach (var move in moves)
                {
                    using (var e = NavigationMoveEvent.GetPooled(move))
                    {
                        root.panel.visualTree.SendEvent(e);
                    }
                }
                using (var tab = NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.Next))
                {
                    root.panel.visualTree.SendEvent(tab);
                }
                yield return null;
                Assert.That(Focused, Is.Null, $"step {i}: navigation focused {(Focused as VisualElement)?.name}");
            }
            yield return Frames(2);
            Assert.That(HudKeyboard.Typing(document), Is.False);
        }

        [UnityTest]
        public IEnumerator EscapeGivesTheKeyboardBack()
        {
            hud.ShowDateEntry(true);
            yield return Frames(3);
            Assert.That(HudKeyboard.Typing(document));
            SendKey(KeyCode.Escape);
            yield return Frames(3);
            Assert.That(Focused, Is.Null);
            Assert.That(root.Q("date-row").resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "Escape closes the date row");
            Assert.That(HudKeyboard.Typing(document), Is.False, "the movement keys work again");
        }

        [UnityTest]
        public IEnumerator EnterJumpsAndGivesTheKeyboardBackUnlessTheDateIsBad()
        {
            var sim = host.Simulation;
            var before = sim.Clock.Now;
            hud.ShowDateEntry(true);
            yield return Frames(3);

            // A bad date keeps the keyboard so it can be fixed, and moves nothing.
            dateField.value = "not a date";
            SendKey(KeyCode.Return);
            yield return Frames(3);
            Assert.That(HudKeyboard.Typing(document), "a bad date keeps the field");
            Assert.That(root.Q<Label>("date-error").text, Is.Not.Empty);
            Assert.That(sim.Clock.Now.Days, Is.EqualTo(before.Days));
            Assert.That(sim.Clock.Now.Seconds, Is.EqualTo(before.Seconds));

            // A date the core accepts jumps there and gives the keyboard back.
            dateField.value = "2026-10-22T00:00:00";
            SendKey(KeyCode.Return);
            yield return Frames(3);
            Assert.That(root.Q<Label>("date-error").text, Is.Empty);
            Assert.That(sim.Clock.Now.Days != before.Days || sim.Clock.Now.Seconds != before.Seconds, "the clock jumped");
            Assert.That(Focused, Is.Null);
            Assert.That(HudKeyboard.Typing(document), Is.False);
        }

        private void SendKey(KeyCode key)
        {
            using var e = KeyDownEvent.GetPooled('\0', key, EventModifiers.None);
            e.target = dateField;
            dateField.SendEvent(e);
        }
    }
}
