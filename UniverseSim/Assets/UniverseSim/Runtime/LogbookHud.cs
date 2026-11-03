using System.Collections.Generic;
using UniverseSim.Unity.Native;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The logbook's part of the HUD (ADR-0019 decision 4) and the moments that make exploring feel like getting
    /// somewhere: the logbook button and panel (J), short toasts when a goal is reached or the viewer arrives near the
    /// Earth, and a banner with the body's name and one of its facts (<see cref="BodyFacts"/>) when a trip ends. It only
    /// shows what the logbook and the core report.
    /// </summary>
    [DefaultExecutionOrder(30)]
    [RequireComponent(typeof(UIDocument))]
    public sealed class LogbookHud : MonoBehaviour
    {
        [SerializeField] private SimulationHost host;
        [SerializeField] private CameraRig rig;
        [SerializeField] private ShipPilot ship;
        [SerializeField] private Logbook logbook;
        [SerializeField] private StartExperience start;
        [SerializeField] private InputActionAsset actions;
        [SerializeField] private float toastSeconds = 4.5f;
        [SerializeField] private float arrivalSeconds = 5f;

        private const int MaxToasts = 3;

        private VisualElement root, panel, goalList, toasts, arrival;
        private Label progress, arrivalTitle, arrivalSubtitle, arrivalFact;
        private Button toggle, reset;
        private InputAction toggleLogbook;
        private ShipPilot.Mode lastMode = ShipPilot.Mode.Off;
        private readonly List<BodyFact> facts = new List<BodyFact>();
        private readonly List<string> toastTexts = new List<string>();
        private IVisualElementScheduledItem hideArrival;

        /// <summary>True while the logbook panel is open.</summary>
        public bool PanelOpen => panel != null && panel.resolvedStyle.display == DisplayStyle.Flex;

        /// <summary>The texts of the toasts shown since the scene loaded, oldest first (for tests).</summary>
        public IReadOnlyList<string> ToastTexts => toastTexts;

        /// <summary>The arrival banner's title, and whether it is shown (for tests).</summary>
        public string ArrivalTitle => arrivalTitle != null ? arrivalTitle.text : string.Empty;

        public bool ArrivalShown => arrival != null && arrival.ClassListContains("arrival--shown");

        private CoreSimulation Sim => host != null ? host.Simulation : null;

        private void Awake()
        {
            toggleLogbook = actions.FindActionMap("Viewer", true).FindAction("ToggleLogbook", true);
        }

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            panel = root.Q<VisualElement>("logbook-panel");
            goalList = root.Q<VisualElement>("logbook-goals");
            progress = root.Q<Label>("logbook-progress");
            toggle = root.Q<Button>("logbook-toggle");
            reset = root.Q<Button>("logbook-reset");
            toasts = root.Q<VisualElement>("toasts");
            arrival = root.Q<VisualElement>("arrival");
            arrivalTitle = root.Q<Label>("arrival-title");
            arrivalSubtitle = root.Q<Label>("arrival-subtitle");
            arrivalFact = root.Q<Label>("arrival-fact");
            panel.style.display = DisplayStyle.None;
            toggle.clicked += TogglePanel;
            reset.clicked += () => logbook.ResetProgress();
            logbook.Completed += OnCompleted;
            logbook.Changed += Rebuild;
            if (ship != null)
            {
                ship.ModeChanged += OnShipMode;
            }
            if (start != null)
            {
                start.Started += Welcome;
            }
            Rebuild();
        }

        private void OnDisable()
        {
            if (logbook != null)
            {
                logbook.Completed -= OnCompleted;
                logbook.Changed -= Rebuild;
            }
            if (ship != null)
            {
                ship.ModeChanged -= OnShipMode;
            }
            if (start != null)
            {
                start.Started -= Welcome;
            }
        }

        private void Update()
        {
            if (Sim != null && !HudKeyboard.Typing(root) && toggleLogbook.WasPressedThisFrame())
            {
                TogglePanel();
            }
        }

        /// <summary>Opens or closes the logbook panel.</summary>
        public void TogglePanel()
        {
            var open = !PanelOpen;
            panel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (open)
            {
                Rebuild();
            }
        }

        // The button's count and, while it is open, the panel's list.
        private void Rebuild()
        {
            if (toggle == null || logbook == null)
            {
                return;
            }
            var total = logbook.Goals.Count;
            var reached = logbook.DoneCount;
            toggle.text = total > 0 ? $"Logbook {reached}/{total} (J)" : "Logbook (J)";
            progress.text = total > 0 ? $"{reached} of {total} reached" : "Loading...";
            goalList.Clear();
            foreach (var goal in logbook.Goals)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("goal");
                row.EnableInClassList("goal--done", goal.Done);
                var steps = goal.Target > 0 && !goal.Done
                    ? goal.Id == "moon-month" ? $" ({goal.Progress} %)" : $" ({goal.Progress}/{goal.Target})"
                    : string.Empty;
                var title = new Label((goal.Done ? "✓ " : "○ ") + goal.Title + steps) { pickingMode = PickingMode.Ignore };
                title.AddToClassList("goal-title");
                var hint = new Label(goal.Hint) { pickingMode = PickingMode.Ignore };
                hint.AddToClassList("goal-hint");
                row.Add(title);
                row.Add(hint);
                goalList.Add(row);
            }
        }

        private void OnCompleted(Logbook.Goal goal) => Toast("Logbook", goal.Title);

        private void Welcome()
        {
            var shipOut = ship != null && ship.Active;
            Toast("Welcome aboard",
                  shipOut ? "W to fly, the mouse to steer, Shift to boost. The Moon is selected: press T to fly there."
                          : "Drag to look around, scroll to zoom. Double-click a body to fly to it.");
        }

        /// <summary>Shows a short message under the top bar for a few seconds.</summary>
        public void Toast(string title, string text)
        {
            toastTexts.Add($"{title}: {text}");
            while (toasts.childCount >= MaxToasts)
            {
                toasts.RemoveAt(0);
            }
            var toast = new VisualElement { pickingMode = PickingMode.Ignore };
            toast.AddToClassList("toast");
            var heading = new Label(title) { pickingMode = PickingMode.Ignore };
            heading.AddToClassList("toast-title");
            var body = new Label(text) { pickingMode = PickingMode.Ignore };
            body.AddToClassList("toast-text");
            toast.Add(heading);
            toast.Add(body);
            toasts.Add(toast);
            // In from its "entering" state through the USS transitions, out the same way, then removed.
            toast.schedule.Execute(() => toast.AddToClassList("toast--shown")).StartingIn(30);
            toast.schedule.Execute(() => toast.RemoveFromClassList("toast--shown"))
                .StartingIn((long)(toastSeconds * 1000f));
            toast.schedule.Execute(() => toast.RemoveFromHierarchy()).StartingIn((long)(toastSeconds * 1000f) + 400);
        }

        private void OnShipMode(ShipPilot.Mode mode)
        {
            if (lastMode == ShipPilot.Mode.Traveling && mode == ShipPilot.Mode.Flying)
            {
                ShowArrival(ship.TargetIndex);
            }
            lastMode = mode;
        }

        /// <summary>The banner for arriving at the body at <paramref name="index"/>: its name, its kind and a fact.</summary>
        public void ShowArrival(int index)
        {
            var sim = Sim;
            if (sim == null || index < 0 || index >= sim.Bodies.Count)
            {
                return;
            }
            var body = sim.Bodies[index];
            arrivalTitle.text = body.Name;
            arrivalSubtitle.text = Describe(sim, body) + (body.IsApproximate ? " (approximate position)" : string.Empty);
            BodyFacts.Collect(sim, index, host.Request, host.SkyCues, facts);
            arrivalFact.text = Pick(facts);
            arrival.AddToClassList("arrival--shown");
            hideArrival?.Pause();
            hideArrival = arrival.schedule.Execute(() => arrival.RemoveFromClassList("arrival--shown"))
                .StartingIn((long)(arrivalSeconds * 1000f));
        }

        // The fact the banner shows: your weight there reads best, then the day, then the first one.
        private static string Pick(List<BodyFact> list)
        {
            foreach (var title in new[] { "Your weight", "A day", "Temperature" })
            {
                foreach (var fact in list)
                {
                    if (fact.Title == title)
                    {
                        return fact.Text;
                    }
                }
            }
            return list.Count > 0 ? list[0].Text : string.Empty;
        }

        // "A moon circling the Earth", "A planet circling the Sun", "A star": the kind and the usual orbit center (core).
        private static string Describe(CoreSimulation sim, BodyInfo body)
        {
            var kind = body.KindName;
            var article = "aeiou".IndexOf(kind[0]) >= 0 ? "An " : "A ";
            if (body.Kind == 1 || !sim.TryDefaultOrbitCenter(body.Id, out var center) || sim.IndexOf(center) < 0)
            {
                return article + kind;
            }
            var around = sim.Bodies[sim.IndexOf(center)];
            var name = around.Kind == 1 || around.Name == "Earth" || around.Name == "Moon" ? "the " + around.Name : around.Name;
            return $"{article}{kind} circling {name}";
        }

    }
}
