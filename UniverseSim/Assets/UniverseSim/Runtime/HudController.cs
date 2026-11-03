using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Unity.Profiling;
using UniverseSim.Unity.Native;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The heads-up display (UI Toolkit): time controls (P5-08), the body info panel (P5-09), toggles for
    /// orbits, labels, the scale mode and the visual style, each mode with a persistent indicator (P5-10, P5-11,
    /// A-02), the "what is exaggerated" panel (A-03), and body labels (each with a <see cref="BodyIcon"/> for the kind
    /// of body, hidden unless StylizedLabels shows it, A-10). It only shows what the core reports and sends user
    /// commands back (SPEC FR-REN-08). Since ADR-0019 the info panel leads with what the body is like in human terms
    /// (<see cref="BodyFacts"/>) above its data and sources, and time is set with a throttle over the rate presets
    /// (<see cref="TimeThrottle"/>), a timeline over the coverage and Tab held to rush, with the date field behind "Set
    /// date". It is driven by the mouse and keeps the keyboard for moving
    /// (<see cref="HudKeyboard"/>): only the date field takes the keyboard, on a click, until Enter, Escape or a click
    /// elsewhere.
    /// </summary>
    [DefaultExecutionOrder(20)]
    [RequireComponent(typeof(UIDocument))]
    public sealed class HudController : MonoBehaviour
    {
        [SerializeField] private SimulationHost host;
        [SerializeField] private CameraRig rig;
        [SerializeField] private OrbitLines orbitLines;
        [SerializeField] private StarField starField;
        [SerializeField] private VisualStyleSettings style;
        [SerializeField] private ExaggerationRegister exaggerationRegister;
        [SerializeField] private EffectsQuality quality;
        [SerializeField] private InputActionAsset actions;
        [SerializeField] private float textRefreshSeconds = 0.1f;

        // Rate presets in simulated seconds per real second: unit definitions (SI minute, hour and day; the
        // Julian year of 365.25 days), not physical values.
        private const double Minute = 60.0;
        private const double Hour = 3600.0;
        private const double Day = 86400.0;
        private static readonly (double Seconds, string Name)[] RatePresets =
        {
            (1.0, "real time"),
            (Minute, "1 min/s"),
            (Hour, "1 hour/s"),
            (Day, "1 day/s"),
            (7.0 * Day, "1 week/s"),
            (30.0 * Day, "30 days/s"),
            (365.25 * Day, "1 Julian year/s"),
        };

        private Camera cam;
        private VisualElement root;
        private Label timePrimary, timeSecondary, coverageLabel, scaleIndicator, styleIndicator, loadError, rateLabel,
            dateError, starCredit;
        private Label infoTitle, infoKind, infoWarning, infoFactsTitle, timelineYear;
        // The info panel (ADR-0019 decision 3): what the body is like, then its data and sources in a foldout.
        private VisualElement infoFacts, infoValues;
        private readonly List<BodyFact> facts = new List<BodyFact>();
        private Button playPause;
        private TextField dateEntry;
        private VisualElement dateRow;
        // Time controls (ADR-0019 decision 5): the throttle over the presets and the timeline over the coverage.
        private SliderInt rateSlider;
        private Slider timeline;
        private bool rushing, rushWasPaused;
        private int rushIndex, rushSign;
        private DropdownField focusField;
        private Toggle toggleOrbits, toggleLabels, toggleStars, toggleEnlarged, toggleRealistic, toggleFly;
        private VisualElement labelsLayer;
        // Hover and selection rings around bodies (A-12): moved by translate like the labels, sized to the body's disk.
        private VisualElement ringsLayer, hoverRing, selectionRing, infoPanel;
        private VisualElement exaggerationsPanel, exaggerationsList;
        private Button exaggerationsToggle;
        private readonly List<Exaggeration> activeExaggerations = new List<Exaggeration>();
        private readonly List<Exaggeration> shownExaggerations = new List<Exaggeration>();
        private bool exaggerationsShownOnce;
        private readonly List<Label> labels = new List<Label>();
        // Each label sits in a zero-size anchor moved by translate, which repaints without a layout pass (left
        // and top would lay out the panel every frame); the label centers itself on the anchor in USS.
        private readonly List<VisualElement> labelAnchors = new List<VisualElement>();
        private readonly List<bool> labelShown = new List<bool>();
        private InputAction togglePause, faster, slower, click, drag, point, rushTime;

        private int rateIndex;
        private int rateSign = 1;
        private float nextRefresh;
        private string coverageText = string.Empty;
        private readonly List<int> focusIndices = new List<int>();

        private void Awake()
        {
            cam = Camera.main;
            var map = actions.FindActionMap("Viewer", true);
            togglePause = map.FindAction("TogglePause", true);
            faster = map.FindAction("Faster", true);
            slower = map.FindAction("Slower", true);
            click = map.FindAction("Click", true);
            drag = map.FindAction("Drag", true);
            point = map.FindAction("Point", true);
            rushTime = map.FindAction("RushTime", true);
            host.Loaded += OnLoaded;
        }

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            timePrimary = root.Q<Label>("time-primary");
            timeSecondary = root.Q<Label>("time-secondary");
            coverageLabel = root.Q<Label>("coverage");
            scaleIndicator = root.Q<Label>("scale-indicator");
            styleIndicator = root.Q<Label>("style-indicator");
            loadError = root.Q<Label>("load-error");
            rateLabel = root.Q<Label>("rate");
            dateError = root.Q<Label>("date-error");
            starCredit = root.Q<Label>("star-credit");
            infoTitle = root.Q<Label>("info-title");
            infoKind = root.Q<Label>("info-kind");
            infoWarning = root.Q<Label>("info-warning");
            infoFactsTitle = root.Q<Label>("info-facts-title");
            infoFacts = root.Q<VisualElement>("info-facts");
            infoValues = root.Q<Foldout>("info-data");
            playPause = root.Q<Button>("play-pause");
            dateEntry = root.Q<TextField>("date-entry");
            dateRow = root.Q<VisualElement>("date-row");
            rateSlider = root.Q<SliderInt>("rate-slider");
            timeline = root.Q<Slider>("timeline");
            timelineYear = root.Q<Label>("timeline-year");
            focusField = root.Q<DropdownField>("focus");
            toggleOrbits = root.Q<Toggle>("toggle-orbits");
            toggleLabels = root.Q<Toggle>("toggle-labels");
            toggleStars = root.Q<Toggle>("toggle-stars");
            toggleEnlarged = root.Q<Toggle>("toggle-enlarged");
            toggleRealistic = root.Q<Toggle>("toggle-realistic");
            toggleFly = root.Q<Toggle>("toggle-fly");
            labelsLayer = root.Q<VisualElement>("labels");
            ringsLayer = root.Q<VisualElement>("rings");
            infoPanel = root.Q<VisualElement>("info-panel");
            if (hoverRing == null)
            {
                hoverRing = MakeRing("body-ring--hover");
                selectionRing = MakeRing("body-ring--selected");
                ringsLayer.Add(hoverRing);
                ringsLayer.Add(selectionRing);
            }
            exaggerationsPanel = root.Q<VisualElement>("exaggerations");
            exaggerationsList = root.Q<VisualElement>("exaggerations-list");
            exaggerationsToggle = root.Q<Button>("exaggerations-toggle");
            exaggerationsToggle.clicked += () => exaggerationsPanel.ToggleInClassList("expanded");

            playPause.clicked += TogglePause;
            rateSlider.lowValue = 0;
            rateSlider.highValue = TimeThrottle.Positions(RatePresets.Length) - 1;
            rateSlider.RegisterValueChangedCallback(e =>
            {
                (rateSign, rateIndex) = TimeThrottle.FromPosition(e.newValue, RatePresets.Length);
                EndRush(false);
                ApplyRate();
            });
            timeline.RegisterValueChangedCallback(e => JumpToFraction(e.newValue));
            root.Q<Button>("set-date").clicked += () => ShowDateEntry(!dateRow.ClassListContains("open"));
            root.Q<Button>("go").clicked += () =>
            {
                if (JumpToEntry())
                {
                    ShowDateEntry(false);
                }
            };
            root.Q<Button>("now").clicked += JumpToNow;
            root.Q<Button>("info-focus").clicked += () => rig.Focus(rig.SelectedIndex);
            // Enter jumps, closes the row and gives the keyboard back (a bad date keeps them, to fix the text); Escape
            // closes the row and gives the keyboard back.
            dateEntry.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    if (JumpToEntry())
                    {
                        ShowDateEntry(false);
                    }
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    ShowDateEntry(false);
                }
            });
            // The field starts from the time shown now, not the one it held when last edited.
            dateEntry.RegisterCallback<FocusInEvent>(_ =>
            {
                if (Sim != null && string.IsNullOrEmpty(dateError.text) &&
                    Sim.TryFormatTime(Sim.Clock.Now, NativeConstants.ScaleUtc, 0, out var nowText))
                {
                    dateEntry.SetValueWithoutNotify(nowText);
                }
            });
            toggleOrbits.RegisterValueChangedCallback(e => orbitLines.Visible = e.newValue);
            toggleStars.RegisterValueChangedCallback(e =>
            {
                if (starField != null)
                {
                    starField.Visible = e.newValue;
                }
            });
            toggleLabels.RegisterValueChangedCallback(e =>
            {
                labelsLayer.style.display = e.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                rig.SetLabelHover(-1);
            });
            toggleEnlarged.RegisterValueChangedCallback(e =>
            {
                rig.EnlargedBodies = e.newValue;
                RefreshScaleIndicator();
            });
            toggleRealistic.RegisterValueChangedCallback(e =>
                style.SetStyle(e.newValue ? VisualStyle.Realistic : VisualStyle.Stylized));
            toggleFly.RegisterValueChangedCallback(e => rig.SetFreeFly(e.newValue));
            focusField.RegisterValueChangedCallback(e =>
            {
                var i = focusField.index;
                if (i >= 0 && i < focusIndices.Count)
                {
                    rig.Focus(focusIndices[i]);
                    rig.Select(focusIndices[i]);
                }
            });
            rig.SelectionChanged += ShowBody;
            rig.SelectionChanged += PulseSelection;
            rig.FocusChanged += OnFocusChanged;
            style.Changed += OnStyleChanged;
            if (quality != null)
            {
                quality.Changed += OnTierChanged;
            }
            RefreshScaleIndicator();
            RefreshStyleIndicator();
            HudKeyboard.KeepForMovement(root);
        }

        private void OnDestroy()
        {
            if (host != null)
            {
                host.Loaded -= OnLoaded;
            }
            if (rig != null)
            {
                rig.SelectionChanged -= ShowBody;
                rig.SelectionChanged -= PulseSelection;
                rig.FocusChanged -= OnFocusChanged;
            }
            if (style != null)
            {
                style.Changed -= OnStyleChanged;
            }
            if (quality != null)
            {
                quality.Changed -= OnTierChanged;
            }
        }

        private void OnTierChanged(EffectsTier _)
        {
            RefreshStyleIndicator();
            RefreshExaggerations();
        }

        private void OnStyleChanged(VisualStyle _)
        {
            RefreshStyleIndicator();
            RefreshExaggerations();
        }

        private CoreSimulation Sim => host.Simulation;

        private void OnLoaded()
        {
            var sim = Sim;
            var coverage = sim.Coverage;
            sim.TryFormatTime(coverage.Start, NativeConstants.ScaleTdb, 0, out var start);
            sim.TryFormatTime(coverage.End, NativeConstants.ScaleTdb, 0, out var end);
            coverageText = $"Data {start.Substring(0, Math.Min(10, start.Length))} to {end}";

            focusIndices.Clear();
            var names = new List<string>();
            foreach (var body in sim.Bodies.Where(b => b.HasMeanRadius))
            {
                focusIndices.Add(body.Index);
                names.Add(body.Name);
            }
            focusField.choices = names;

            labels.Clear();
            labelAnchors.Clear();
            labelShown.Clear();
            labelsLayer.Clear();
            foreach (var body in sim.Bodies)
            {
                var anchor = new VisualElement { pickingMode = PickingMode.Ignore };
                anchor.AddToClassList("body-label-anchor");
                // A label picks its body like a click on the body does (A-12): hover, click to select, double-click
                // to focus.
                var label = new Label(body.Kind == 0 ? string.Empty : body.Name) { pickingMode = PickingMode.Position };
                label.AddToClassList("body-label");
                var index = body.Index;
                label.RegisterCallback<PointerEnterEvent>(_ => rig.SetLabelHover(index));
                label.RegisterCallback<PointerLeaveEvent>(_ => rig.SetLabelHover(-1));
                label.RegisterCallback<ClickEvent>(e =>
                {
                    rig.Select(index);
                    if (e.clickCount >= 2)
                    {
                        rig.Focus(index);
                    }
                });
                // The icon for the kind of body, hidden unless the stylized labels show it (A-10, StylizedLabels).
                var icon = BodyIcon.ShapeFor(body.Kind);
                if (icon != BodyIconShape.None)
                {
                    anchor.Add(new BodyIcon(icon));
                }
                anchor.Add(label);
                labelsLayer.Add(anchor);
                labels.Add(label);
                labelAnchors.Add(anchor);
                labelShown.Add(true);
            }
            sim.TryFormatTime(sim.Clock.Now, NativeConstants.ScaleUtc, 0, out var nowText);
            dateEntry.value = nowText;
            ApplyRate();
            // The rig chose its focus before these fields existed.
            OnFocusChanged(rig.FocusIndex);
        }

        private void OnFocusChanged(int index)
        {
            var i = focusIndices.IndexOf(index);
            if (i >= 0)
            {
                focusField.SetValueWithoutNotify(focusField.choices[i]);
            }
            toggleFly.SetValueWithoutNotify(rig.FreeFly);
        }

        private void Update()
        {
            if (Sim == null)
            {
                return;
            }
            ReleaseOnClickElsewhere();
            var typing = HudKeyboard.Typing(root);
            if (!typing)
            {
                if (togglePause.WasPressedThisFrame())
                {
                    TogglePause();
                }
                if (faster.WasPressedThisFrame())
                {
                    StepRate(1);
                }
                if (slower.WasPressedThisFrame())
                {
                    StepRate(-1);
                }
                if (rushTime.WasPressedThisFrame())
                {
                    StartRush();
                }
            }
            if (rushing && !rushTime.IsPressed())
            {
                EndRush(true);
            }
            toggleFly.SetValueWithoutNotify(rig.FreeFly);
        }

        // A click or drag anywhere but the date field (in space, or on another control) gives the keyboard back.
        private void ReleaseOnClickElsewhere()
        {
            if (!(click.WasPressedThisFrame() || drag.WasPressedThisFrame()) || root.panel == null ||
                root.focusController?.focusedElement == null)
            {
                return;
            }
            var screen = point.ReadValue<Vector2>();
            var picked = root.panel.Pick(RuntimePanelUtils.ScreenToPanel(root.panel,
                                                                         new Vector2(screen.x, Screen.height - screen.y)));
            if (picked == null || !(picked == dateEntry || dateEntry.Contains(picked)))
            {
                HudKeyboard.Release(root);
            }
        }

        private void LateUpdate()
        {
            if (Sim == null)
            {
                loadError.text = host.LoadError;
                return;
            }
            PlaceLabels();
            PlaceRings();
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + textRefreshSeconds;
                RefreshTexts();
                // The star field may load after the HUD (both listen for the host's Loaded event).
                starCredit.text = starField != null && starField.Visible ? starField.Attribution : string.Empty;
            }
        }

        // ---- Time controls (P5-08) ----

        private void ApplyRate()
        {
            Sim.SetRate(rateSign * RatePresets[rateIndex].Seconds);
            rateLabel.text = (rushing ? "Rushing: " : string.Empty) + (rateSign < 0 ? "Reverse, " : string.Empty) +
                             RatePresets[rateIndex].Name;
            rateSlider.SetValueWithoutNotify(TimeThrottle.ToPosition(rateSign, rateIndex, RatePresets.Length));
        }

        // The [ and ] keys: slower and faster in the current direction.
        private void StepRate(int step)
        {
            EndRush(false);
            rateIndex = Math.Clamp(rateIndex + step, 0, RatePresets.Length - 1);
            ApplyRate();
        }

        // Tab held (ADR-0019 decision 5): the fastest preset in the current direction, running, until it is released.
        private void StartRush()
        {
            if (rushing)
            {
                return;
            }
            rushing = true;
            rushIndex = rateIndex;
            rushSign = rateSign;
            rushWasPaused = Sim.Clock.Paused != 0;
            rateIndex = RatePresets.Length - 1;
            Sim.SetPaused(false);
            ApplyRate();
        }

        // Ends a rush; `restore` puts back the rate and the pause from before it (a release of Tab), otherwise the new
        // choice stands (the throttle or the keys were used during it).
        private void EndRush(bool restore)
        {
            if (!rushing)
            {
                return;
            }
            rushing = false;
            if (restore)
            {
                rateIndex = rushIndex;
                rateSign = rushSign;
                if (rushWasPaused)
                {
                    Sim.SetPaused(true);
                }
            }
            ApplyRate();
        }

        /// <summary>True while Tab is held to rush time (for tests).</summary>
        public bool Rushing => rushing;

        /// <summary>Starts or ends a rush as holding and releasing Tab does (for tests).</summary>
        public void SetRush(bool on)
        {
            if (on)
            {
                StartRush();
            }
            else
            {
                EndRush(true);
            }
        }

        // The timeline: a fraction of the pack's coverage, as an instant from the core.
        private void JumpToFraction(float fraction)
        {
            var coverage = Sim.Coverage;
            var span = CoreSimulation.SecondsBetween(coverage.Start, coverage.End);
            if (!(span > 0.0))
            {
                return;
            }
            Sim.SetTime(CoreSimulation.After(coverage.Start, Mathf.Clamp01(fraction) * span));
            dateError.text = string.Empty;
        }

        private void RefreshTimeline()
        {
            var clock = Sim.Clock;
            var coverage = Sim.Coverage;
            var span = CoreSimulation.SecondsBetween(coverage.Start, coverage.End);
            if (span > 0.0)
            {
                timeline.SetValueWithoutNotify((float)(CoreSimulation.SecondsBetween(coverage.Start, clock.Now) / span));
            }
            var text = timePrimary.text;
            timelineYear.text = text.Length >= 4 ? text.Substring(0, 4) : text;
        }

        /// <summary>Opens the date row and gives its field the keyboard, or closes it and gives the keyboard back.</summary>
        public void ShowDateEntry(bool open)
        {
            dateRow.EnableInClassList("open", open);
            if (open)
            {
                dateError.text = string.Empty;
                if (Sim != null && Sim.TryFormatTime(Sim.Clock.Now, NativeConstants.ScaleUtc, 0, out var nowText))
                {
                    dateEntry.SetValueWithoutNotify(nowText);
                }
                // Once the row is laid out, so the field can take the focus.
                dateEntry.schedule.Execute(() => dateEntry.Focus()).StartingIn(0);
            }
            else
            {
                HudKeyboard.Release(root);
            }
        }

        private void TogglePause()
        {
            var paused = Sim.Clock.Paused == 0;
            Sim.SetPaused(paused);
            playPause.text = paused ? "Play" : "Pause";
        }

        // True when the text was a time the core accepted.
        private bool JumpToEntry()
        {
            var text = dateEntry.value.Trim();
            // A scale is required by the core; read text without one as UTC.
            var last = text.Split(' ').LastOrDefault() ?? string.Empty;
            if (!(last == "UTC" || last == "TT" || last == "TAI" || last == "TDB"))
            {
                text += " UTC";
            }
            if (Sim.TryParseTime(text, out var t, out var error))
            {
                Sim.SetTime(t);
                dateError.text = string.Empty;
                return true;
            }
            dateError.text = error;
            return false;
        }

        private void JumpToNow()
        {
            try
            {
                Sim.SetTime(Sim.FromUtc(DateTime.UtcNow));
                dateError.text = string.Empty;
            }
            catch (CoreException e)
            {
                dateError.text = e.Message;
            }
        }

        private void RefreshTexts()
        {
            var clock = Sim.Clock;
            if (Sim.TryFormatTime(clock.Now, NativeConstants.ScaleUtc, 0, out var utc))
            {
                timePrimary.text = utc;
            }
            else if (Sim.TryFormatTime(clock.Now, NativeConstants.ScaleTt, 0, out var tt))
            {
                timePrimary.text = tt; // UTC exists from 1972 only
            }
            Sim.TryFormatTime(clock.Now, NativeConstants.ScaleTdb, 3, out var tdb);
            timeSecondary.text = tdb;
            RefreshTimeline();
            var limit = clock.Limit == NativeConstants.ClockLimitEnd ? "  End of data reached"
                : clock.Limit == NativeConstants.ClockLimitStart ? "  Start of data reached" : string.Empty;
            coverageLabel.text = coverageText + limit;
            coverageLabel.EnableInClassList("warning", limit.Length > 0);
            playPause.text = clock.Paused != 0 ? "Play" : "Pause";
            // The indicator always shows the mode actually in use (SPEC FR-REN-02), however it was set.
            RefreshScaleIndicator();
            toggleEnlarged.SetValueWithoutNotify(rig.EnlargedBodies);
            RefreshStyleIndicator();
            RefreshExaggerations();
            if (rig.SelectedIndex >= 0)
            {
                RefreshMotion();
            }
        }

        private void RefreshScaleIndicator()
        {
            scaleIndicator.text = rig.EnlargedBodies
                ? $"Bodies enlarged x{rig.BodyScale.ToString("0", CultureInfo.InvariantCulture)} (distances true)"
                : "True scale";
            scaleIndicator.EnableInClassList("enlarged", rig.EnlargedBodies);
        }

        // Like the scale mode, the style in use is always shown (SPEC FR-REN-12), however it was set.
        private void RefreshStyleIndicator()
        {
            var realistic = style.Style == VisualStyle.Realistic;
            // Below the high effects tier (A-13) the indicator says so, since some effects are lighter or off.
            var tier = quality != null ? quality.Tier : EffectsTier.High;
            styleIndicator.text = VisualStyleSettings.DisplayName(style.Style) +
                                  (!realistic && tier < EffectsTier.High
                                       ? $" (effects {EffectsQuality.DisplayName(tier)})"
                                       : string.Empty);
            styleIndicator.EnableInClassList("realistic", realistic);
            toggleRealistic.SetValueWithoutNotify(realistic);
        }

        // ---- What is exaggerated (A-03) ----

        // The entries of the effects active in the current view; the list is rebuilt only when they change.
        private void RefreshExaggerations()
        {
            exaggerationRegister.CollectActive(cam, activeExaggerations);
            var same = exaggerationsShownOnce && activeExaggerations.Count == shownExaggerations.Count;
            for (var i = 0; same && i < activeExaggerations.Count; ++i)
            {
                same = activeExaggerations[i].Subject == shownExaggerations[i].Subject &&
                       activeExaggerations[i].Amount == shownExaggerations[i].Amount;
            }
            if (same)
            {
                return;
            }
            exaggerationsShownOnce = true;
            shownExaggerations.Clear();
            shownExaggerations.AddRange(activeExaggerations);
            exaggerationsList.Clear();
            foreach (var entry in activeExaggerations)
            {
                exaggerationsList.Add(MakeLabel($"{entry.Subject}: {entry.Change}", "exaggeration"));
                exaggerationsList.Add(MakeLabel(entry.Amount, "exaggeration-amount"));
            }
            exaggerationsToggle.text = activeExaggerations.Count > 0
                ? $"What is exaggerated? ({activeExaggerations.Count})"
                : style.Style == VisualStyle.Realistic
                    ? "Nothing is exaggerated (realistic style)"
                    : "Nothing is exaggerated in this view";
        }

        // ---- Labels (P5-10) ----

        // A moon's label is shown once the moon is this far from its planet on screen (it would cover the
        // planet's label otherwise), and always when the moon is selected or in focus.
        private const float MoonLabelMinPixels = 28f;

        private static readonly ProfilerMarker LabelsMarker = new ProfilerMarker("UniverseSim.Labels");

        private void PlaceLabels()
        {
            if (labelsLayer.resolvedStyle.display == DisplayStyle.None || root.panel == null)
            {
                return;
            }
            using var marker = LabelsMarker.Auto();
            // Labels of bodies hidden behind the focus body are not shown: the camera is at the origin, so a
            // body is hidden if it is farther away than the focus and its line of sight passes through it.
            var focus = rig.FocusIndex >= 0 && rig.FocusIndex < host.Views.Count ? host.Views[rig.FocusIndex] : null;
            var focusCenter = focus != null ? focus.transform.position : Vector3.zero;
            var focusRadius = focus != null ? focus.MaxRenderRadius : 0f;
            for (var i = 0; i < labels.Count && i < host.Views.Count; ++i)
            {
                var view = host.Views[i];
                var label = labels[i];
                var position = view.transform.position;
                var p = cam.WorldToScreenPoint(position);
                var behindFocus = focus != null && view != focus && focusRadius > 0f &&
                                  position.magnitude > focusCenter.magnitude &&
                                  Vector3.Cross(position.normalized, focusCenter).magnitude < focusRadius;
                var chosen = i == rig.SelectedIndex || i == rig.FocusIndex;
                var visible = view.HasPosition && p.z > 0f && label.text.Length > 0 && !behindFocus &&
                              (chosen || view.StandsApart(cam, MoonLabelMinPixels));
                var anchor = labelAnchors[i];
                if (visible != labelShown[i])
                {
                    labelShown[i] = visible;
                    anchor.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                }
                if (!visible)
                {
                    continue;
                }
                // Screen coordinates of the camera's own target (the screen, or a texture when one is set).
                var panelPosition = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(p.x, cam.pixelHeight - p.y));
                anchor.style.translate = new Translate(panelPosition.x, panelPosition.y);
                label.EnableInClassList("selected", i == rig.SelectedIndex);
                label.EnableInClassList("hovered", i == rig.HoveredIndex);
            }
        }

        // ---- Hover and selection feedback (A-12) ----

        /// <summary>The ring around the body under the pointer (for tests).</summary>
        public VisualElement HoverRing => hoverRing;

        /// <summary>The ring around the selected body (for tests).</summary>
        public VisualElement SelectionRing => selectionRing;

        /// <summary>The label of the body at catalog index <paramref name="index"/> (for tests).</summary>
        public Label LabelOf(int index) => index >= 0 && index < labels.Count ? labels[index] : null;

        // The rings stay this far outside the body's disk, and are at least this wide, in screen pixels.
        private const float RingMarginPixels = 7f;
        private const float RingMinRadiusPixels = 9f;

        private static VisualElement MakeRing(string modifier)
        {
            var ring = new VisualElement { pickingMode = PickingMode.Ignore };
            ring.AddToClassList("body-ring");
            ring.AddToClassList(modifier);
            ring.style.display = DisplayStyle.None;
            return ring;
        }

        private void PlaceRings()
        {
            if (root.panel == null)
            {
                return;
            }
            var hovered = rig.HoveredIndex != rig.SelectedIndex ? rig.HoveredIndex : -1;
            PlaceRing(hoverRing, hovered);
            PlaceRing(selectionRing, rig.SelectedIndex);
        }

        // Centers `ring` on the body at `index` and sizes it to the body's disk on screen, or hides it.
        private void PlaceRing(VisualElement ring, int index)
        {
            var shown = false;
            if (index >= 0 && index < host.Views.Count)
            {
                var view = host.Views[index];
                var p = cam.WorldToScreenPoint(view.transform.position);
                if (view.HasPosition && p.z > 0f)
                {
                    var focalPixels = 0.5f * cam.pixelHeight / Mathf.Tan(0.5f * cam.fieldOfView * Mathf.Deg2Rad);
                    var distance = Vector3.Distance(view.transform.position, cam.transform.position);
                    var radiusPixels = Mathf.Max(SphereLods.PixelRadius(view.MaxRenderRadius, distance, focalPixels) +
                                                 RingMarginPixels, RingMinRadiusPixels);
                    // A ring around a body that fills the view would only frame the screen.
                    shown = radiusPixels < 0.45f * Mathf.Min(cam.pixelWidth, cam.pixelHeight);
                    if (shown)
                    {
                        var center = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(p.x, cam.pixelHeight - p.y));
                        var edge = RuntimePanelUtils.ScreenToPanel(root.panel,
                                                                   new Vector2(p.x + radiusPixels, cam.pixelHeight - p.y));
                        var radius = Mathf.Max(edge.x - center.x, 1f);
                        // A new size lays the panel out again, so only when it changes by more than half a pixel;
                        // the position moves by translate, which needs no layout.
                        if (!(ring.userData is float sized) || Mathf.Abs(sized - radius) > 0.5f)
                        {
                            ring.userData = radius;
                            ring.style.width = 2f * radius;
                            ring.style.height = 2f * radius;
                            ring.style.borderTopLeftRadius = radius;
                            ring.style.borderTopRightRadius = radius;
                            ring.style.borderBottomLeftRadius = radius;
                            ring.style.borderBottomRightRadius = radius;
                        }
                        var size = (float)ring.userData;
                        ring.style.translate = new Translate(center.x - size, center.y - size);
                    }
                }
            }
            var display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (ring.style.display != display)
            {
                ring.style.display = display;
            }
        }

        // A short pulse of the selection ring and a fade-in of the info panel when the selection changes: each starts
        // from its "entering" state and USS transitions take it to its resting state.
        private void PulseSelection(int index)
        {
            Enter(selectionRing);
            Enter(infoPanel);
        }

        private static void Enter(VisualElement element)
        {
            if (element == null)
            {
                return;
            }
            element.AddToClassList("entering");
            element.schedule.Execute(() => element.RemoveFromClassList("entering")).StartingIn(30);
        }

        // ---- Body info panel (P5-09) ----

        private Label motionLabel;

        private void ShowBody(int index)
        {
            infoValues.Clear();
            infoFacts.Clear();
            motionLabel = null;
            if (Sim == null || index < 0 || index >= Sim.Bodies.Count)
            {
                return;
            }
            var body = Sim.Bodies[index];
            infoTitle.text = body.Name;
            infoKind.text = $"{body.KindName}, NAIF ID {body.Id.Value}";
            infoWarning.text = body.IsApproximate ? "Approximate position: " + body.ApproximationReason : string.Empty;

            // What it's like here (ADR-0019 decision 3): the core's values in human terms, above the data.
            BodyFacts.Collect(Sim, index, host.Request, host.SkyCues, facts);
            infoFactsTitle.style.display = facts.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var fact in facts)
            {
                infoFacts.Add(MakeLabel(fact.Title, "fact-title"));
                infoFacts.Add(MakeLabel(fact.Text, "fact"));
            }

            var cited = new SortedSet<string>(StringComparer.Ordinal);
            AddQuantity(index, NativeConstants.FieldGm, "Gravitational parameter GM", cited);
            AddQuantity(index, NativeConstants.FieldMeanRadius, "Mean radius", cited);
            AddQuantity(index, NativeConstants.FieldRadiusA, "Equatorial radius a", cited);
            AddQuantity(index, NativeConstants.FieldRadiusB, "Equatorial radius b", cited);
            AddQuantity(index, NativeConstants.FieldRadiusC, "Polar radius c", cited);
            AddSurfaceConditions(index, cited);
            AddRotation(body, cited);
            AddRings(index, cited);
            AddSurfaceMap(body);

            infoValues.Add(MakeLabel("Motion", "value-name"));
            motionLabel = MakeLabel(string.Empty, "value");
            infoValues.Add(motionLabel);
            RefreshMotion();

            if (cited.Count > 0)
            {
                infoValues.Add(MakeLabel("Sources", "value-name"));
                foreach (var id in cited)
                {
                    var source = Sim.Sources.FirstOrDefault(s => s.Id == id);
                    if (source != null)
                    {
                        infoValues.Add(MakeLabel($"{source.Id}: {source.Citation} ({source.Url})", "value-source"));
                    }
                }
            }
        }

        // Ring regions and gaps of the body (P6-04): radii and optical depths with their sources; a Tier 2
        // source is named as such (DATA_SOURCES.md section 1).
        private void AddRings(int index, ISet<string> cited)
        {
            var rings = Sim.Rings.Where(r => r.BodyIndex == index).ToList();
            if (rings.Count == 0)
            {
                return;
            }
            infoValues.Add(MakeLabel($"Rings ({rings.Count(r => !r.IsGap)} regions, {rings.Count(r => r.IsGap)} gaps; drawn as visual-only geometry)", "value-name"));
            foreach (var ring in rings)
            {
                var radii = $"{ring.InnerRadiusKm.ToString("N1", CultureInfo.InvariantCulture)} to " +
                            $"{ring.OuterRadiusKm.ToString("N1", CultureInfo.InvariantCulture)} km";
                var depth = ring.HasOpticalDepth
                    ? $", optical depth {ring.OpticalDepthSmallest.ToString("G3", CultureInfo.InvariantCulture)} to " +
                      ring.OpticalDepthLargest.ToString("G3", CultureInfo.InvariantCulture) +
                      (ring.OpticalDepthSource != null && ring.OpticalDepthSource.Tier == 2 ? " (Tier 2 source)" : string.Empty)
                    : string.Empty;
                infoValues.Add(MakeLabel($"{ring.Name}{(ring.IsGap ? " (gap)" : string.Empty)}: {radii}{depth}", "value"));
                var sources = (ring.RadiiSource?.Id ?? "?") + (ring.OpticalDepthSource != null ? ", " + ring.OpticalDepthSource.Id : string.Empty);
                infoValues.Add(MakeLabel(sources, "value-source"));
                if (ring.RadiiSource != null)
                {
                    cited.Add(ring.RadiiSource.Id);
                }
                if (ring.OpticalDepthSource != null)
                {
                    cited.Add(ring.OpticalDepthSource.Id);
                }
            }
        }

        // The body's surface map (P6-07): a visual asset, so its credit is shown here rather than among the
        // pack's sources; ATTRIBUTION.md has the full record.
        private void AddSurfaceMap(BodyInfo body)
        {
            if (host.SurfaceMaps.TryGet(body.Id, out var map, out _))
            {
                AddRow("Surface map", map.content, $"{map.credit} ({map.sourceId}, {map.page})");
            }
            else if (body.HasMeanRadius && body.Kind != 1)
            {
                AddRow("Surface map", "none: plain lit sphere", null);
            }
        }

        // The kind of reference surface and the surface conditions, where the pack has them (ADR-0017); a Tier 2 source
        // is named as such (DATA_SOURCES.md section 1).
        private void AddSurfaceConditions(int index, ISet<string> cited)
        {
            var kind = Sim.SurfaceKind(index);
            if (kind == NativeConstants.SurfaceUnknown)
            {
                return;
            }
            AddRow("Reference surface", kind == NativeConstants.SurfaceSolid
                                            ? "solid ground"
                                            : "a pressure level of the atmosphere (no solid surface there)", null);
            var fields = new[]
            {
                (NativeConstants.FieldReferencePressure, "Reference pressure (the radii's level)"),
                (NativeConstants.FieldMeanTemperature, "Mean temperature (averaged over the body)"),
                (NativeConstants.FieldSurfacePressure, "Surface pressure"),
            };
            foreach (var (field, name) in fields)
            {
                var q = Sim.Quantity(index, field);
                if (!q.HasValue)
                {
                    continue;
                }
                var tier = Sim.Sources.Any(s => s.Id == q.SourceId && s.Tier == 2) ? " (Tier 2 source)" : string.Empty;
                AddRow(name, q.Value.ToString("G10", CultureInfo.InvariantCulture) + " " + q.UnitSymbol + tier,
                       $"{q.SourceId}: {q.Locator}" + (q.Derivation.Length > 0 ? $"; derived: {q.Derivation}" : string.Empty));
                cited.Add(q.SourceId);
            }
            AddAtmosphere(index);
        }

        // Whether the body has an atmosphere (the core, from the pressures above) and the sourced hues of its sky
        // (ADR-0018). The cues' sources are not in the pack, so each row carries its own citation.
        private void AddAtmosphere(int index)
        {
            switch (Sim.Atmosphere(index))
            {
                case NativeConstants.AtmospherePresent:
                    AddRow("Atmosphere", "yes (a positive surface pressure, or a reference level within it)", null);
                    break;
                case NativeConstants.AtmosphereNone:
                    AddRow("Atmosphere", "none at the source's precision (a surface pressure of 0 in its table)", null);
                    return;
                default:
                    return;
            }
            var id = Sim.Bodies[index].Id.Value;
            if (host.SkyCues.TryGet(id, SkyCues.DaytimeSky, out var day))
            {
                AddRow("Sky by day", day.HueName, day.Citation);
            }
            if (host.SkyCues.TryGet(id, SkyCues.Twilight, out var twilight))
            {
                AddRow("Twilight (near a low Sun, and at the limb along the terminator)", twilight.HueName, twilight.Citation);
            }
        }

        // The model that orients the body, from the core: IAU elements, or a binary PCK frame (P6-05).
        private void AddRotation(BodyInfo body, ISet<string> cited)
        {
            var value = body.RotationKind switch
            {
                NativeConstants.RotationIau => "IAU rotation elements",
                NativeConstants.RotationBinaryPck => $"binary PCK, frame {body.RotationFrameName}",
                _ => "none in the data",
            };
            var source = body.RotationSourceId.Length > 0 ? $"{body.RotationSourceId}: {body.RotationLocator}" : null;
            AddRow("Rotation model", value, source);
            if (body.RotationSourceId.Length > 0)
            {
                cited.Add(body.RotationSourceId);
            }
        }

        private void AddQuantity(int index, int field, string name, ISet<string> cited)
        {
            var q = Sim.Quantity(index, field);
            if (!q.HasValue)
            {
                AddRow(name, "unknown (no source provides it)", null);
                return;
            }
            var value = q.Value.ToString("G10", CultureInfo.InvariantCulture) + " " + q.UnitSymbol;
            if (q.Uncertainty.HasValue)
            {
                value += " +- " + q.Uncertainty.Value.ToString("G3", CultureInfo.InvariantCulture);
            }
            var source = $"{q.SourceId}: {q.Locator}" + (q.Derivation.Length > 0 ? $"; derived: {q.Derivation}" : string.Empty);
            AddRow(name, value, source);
            cited.Add(q.SourceId);
        }

        private void AddRow(string name, string value, string source)
        {
            infoValues.Add(MakeLabel(name, "value-name"));
            infoValues.Add(MakeLabel(value, "value"));
            if (!string.IsNullOrEmpty(source))
            {
                infoValues.Add(MakeLabel(source, "value-source"));
            }
        }

        private static Label MakeLabel(string text, string cssClass)
        {
            var label = new Label(text);
            label.AddToClassList(cssClass);
            return label;
        }

        /// <summary>Distance and speed relative to the body's usual orbit center, from the core's state.</summary>
        private void RefreshMotion()
        {
            if (motionLabel == null || rig.SelectedIndex < 0)
            {
                return;
            }
            var body = Sim.Bodies[rig.SelectedIndex];
            if (!Sim.TryDefaultOrbitCenter(body.Id, out var center) ||
                !Sim.TryState(body.Id, center, UsimFrame.EclipJ2000, Sim.Clock.Now, out var s))
            {
                motionLabel.text = "no orbit center";
                return;
            }
            var centerName = Sim.Bodies[Sim.IndexOf(center)].Name;
            var distance = Double3.From(s.PositionKm).Length;
            var speed = Double3.From(s.VelocityKmS).Length;
            var approximate = (s.Flags & NativeConstants.StateApproximate) != 0 ? " (approximate)" : string.Empty;
            motionLabel.text =
                $"{distance.ToString("N0", CultureInfo.InvariantCulture)} km from {centerName}, " +
                $"{speed.ToString("F3", CultureInfo.InvariantCulture)} km/s{approximate} (ECLIPJ2000)";
        }
    }
}
