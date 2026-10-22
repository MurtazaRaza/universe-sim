using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UniverseSim.Unity.Native;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The heads-up display (UI Toolkit): time controls (P5-08), the body info panel (P5-09), toggles for
    /// orbits, labels and the scale mode with a persistent indicator (P5-10, P5-11), and body labels.
    /// It only shows what the core reports and sends user commands back (SPEC FR-REN-08).
    /// </summary>
    [DefaultExecutionOrder(20)]
    [RequireComponent(typeof(UIDocument))]
    public sealed class HudController : MonoBehaviour
    {
        [SerializeField] private SimulationHost host;
        [SerializeField] private CameraRig rig;
        [SerializeField] private OrbitLines orbitLines;
        [SerializeField] private StarField starField;
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
        private Label timePrimary, timeSecondary, coverageLabel, scaleIndicator, loadError, rateLabel, dateError, starCredit;
        private Label infoTitle, infoKind, infoWarning;
        private ScrollView infoValues;
        private Button playPause;
        private TextField dateEntry;
        private DropdownField focusField;
        private Toggle toggleOrbits, toggleLabels, toggleStars, toggleEnlarged, toggleFly;
        private VisualElement labelsLayer;
        private readonly List<Label> labels = new List<Label>();
        private InputAction togglePause, faster, slower;

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
            host.Loaded += OnLoaded;
        }

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            timePrimary = root.Q<Label>("time-primary");
            timeSecondary = root.Q<Label>("time-secondary");
            coverageLabel = root.Q<Label>("coverage");
            scaleIndicator = root.Q<Label>("scale-indicator");
            loadError = root.Q<Label>("load-error");
            rateLabel = root.Q<Label>("rate");
            dateError = root.Q<Label>("date-error");
            starCredit = root.Q<Label>("star-credit");
            infoTitle = root.Q<Label>("info-title");
            infoKind = root.Q<Label>("info-kind");
            infoWarning = root.Q<Label>("info-warning");
            infoValues = root.Q<ScrollView>("info-values");
            playPause = root.Q<Button>("play-pause");
            dateEntry = root.Q<TextField>("date-entry");
            focusField = root.Q<DropdownField>("focus");
            toggleOrbits = root.Q<Toggle>("toggle-orbits");
            toggleLabels = root.Q<Toggle>("toggle-labels");
            toggleStars = root.Q<Toggle>("toggle-stars");
            toggleEnlarged = root.Q<Toggle>("toggle-enlarged");
            toggleFly = root.Q<Toggle>("toggle-fly");
            labelsLayer = root.Q<VisualElement>("labels");

            root.Q<Button>("slower").clicked += () => StepRate(-1);
            root.Q<Button>("faster").clicked += () => StepRate(1);
            root.Q<Button>("reverse").clicked += Reverse;
            playPause.clicked += TogglePause;
            root.Q<Button>("go").clicked += JumpToEntry;
            root.Q<Button>("now").clicked += JumpToNow;
            root.Q<Button>("info-focus").clicked += () => rig.Focus(rig.SelectedIndex);
            dateEntry.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    JumpToEntry();
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
            toggleLabels.RegisterValueChangedCallback(e => labelsLayer.style.display = e.newValue ? DisplayStyle.Flex : DisplayStyle.None);
            toggleEnlarged.RegisterValueChangedCallback(e =>
            {
                rig.EnlargedBodies = e.newValue;
                RefreshScaleIndicator();
            });
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
            rig.FocusChanged += OnFocusChanged;
            RefreshScaleIndicator();
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
                rig.FocusChanged -= OnFocusChanged;
            }
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
            labelsLayer.Clear();
            foreach (var body in sim.Bodies)
            {
                var label = new Label(body.Kind == 0 ? string.Empty : body.Name) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("body-label");
                labelsLayer.Add(label);
                labels.Add(label);
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
            if (togglePause.WasPressedThisFrame() && !(root.focusController?.focusedElement is TextField))
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
            toggleFly.SetValueWithoutNotify(rig.FreeFly);
        }

        private void LateUpdate()
        {
            if (Sim == null)
            {
                loadError.text = host.LoadError;
                return;
            }
            PlaceLabels();
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
            rateLabel.text = (rateSign < 0 ? "Reverse, " : string.Empty) + RatePresets[rateIndex].Name;
        }

        private void StepRate(int step)
        {
            rateIndex = Math.Clamp(rateIndex + step, 0, RatePresets.Length - 1);
            ApplyRate();
        }

        private void Reverse()
        {
            rateSign = -rateSign;
            ApplyRate();
        }

        private void TogglePause()
        {
            var paused = Sim.Clock.Paused == 0;
            Sim.SetPaused(paused);
            playPause.text = paused ? "Play" : "Pause";
        }

        private void JumpToEntry()
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
            }
            else
            {
                dateError.text = error;
            }
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
            var limit = clock.Limit == NativeConstants.ClockLimitEnd ? "  End of data reached"
                : clock.Limit == NativeConstants.ClockLimitStart ? "  Start of data reached" : string.Empty;
            coverageLabel.text = coverageText + limit;
            coverageLabel.EnableInClassList("warning", limit.Length > 0);
            playPause.text = clock.Paused != 0 ? "Play" : "Pause";
            // The indicator always shows the mode actually in use (SPEC FR-REN-02), however it was set.
            RefreshScaleIndicator();
            toggleEnlarged.SetValueWithoutNotify(rig.EnlargedBodies);
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

        // ---- Labels (P5-10) ----

        // A moon's label is shown once the moon is this far from its planet on screen (it would cover the
        // planet's label otherwise), and always when the moon is selected or in focus.
        private const float MoonLabelMinPixels = 28f;

        private void PlaceLabels()
        {
            if (labelsLayer.resolvedStyle.display == DisplayStyle.None || root.panel == null)
            {
                return;
            }
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
                label.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible)
                {
                    continue;
                }
                // Screen coordinates of the camera's own target (the screen, or a texture when one is set).
                var panelPosition = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(p.x, cam.pixelHeight - p.y));
                label.style.left = panelPosition.x;
                label.style.top = panelPosition.y;
                label.EnableInClassList("selected", i == rig.SelectedIndex);
            }
        }

        // ---- Body info panel (P5-09) ----

        private Label motionLabel;

        private void ShowBody(int index)
        {
            infoValues.Clear();
            motionLabel = null;
            if (Sim == null || index < 0 || index >= Sim.Bodies.Count)
            {
                return;
            }
            var body = Sim.Bodies[index];
            infoTitle.text = body.Name;
            infoKind.text = $"{body.KindName}, NAIF ID {body.Id.Value}";
            infoWarning.text = body.IsApproximate ? "Approximate position: " + body.ApproximationReason : string.Empty;

            var cited = new SortedSet<string>(StringComparer.Ordinal);
            AddQuantity(index, NativeConstants.FieldGm, "Gravitational parameter GM", cited);
            AddQuantity(index, NativeConstants.FieldMeanRadius, "Mean radius", cited);
            AddQuantity(index, NativeConstants.FieldRadiusA, "Equatorial radius a", cited);
            AddQuantity(index, NativeConstants.FieldRadiusB, "Equatorial radius b", cited);
            AddQuantity(index, NativeConstants.FieldRadiusC, "Polar radius c", cited);
            AddRow("Rotation model", body.HasRotation ? "IAU rotation elements (pck00011.tpc)" : "none in the data", null);
            AddRings(index, cited);

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
