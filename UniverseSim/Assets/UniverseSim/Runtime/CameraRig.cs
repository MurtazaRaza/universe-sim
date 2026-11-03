using System;
using UniverseSim.Unity.Native;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace UniverseSim.Unity
{
    /// <summary>
    /// Something that drives the camera instead of its own controls: the spaceship (ADR-0016). It hands the rig a
    /// pose each frame; positions stay relative to a body center in double precision, as the rig keeps them.
    /// </summary>
    public interface ICameraPilot
    {
        /// <summary>
        /// The pose for this frame: the body the view is centered on (catalog index), the camera's offset from its
        /// center (km, host axes, ECLIPJ2000), the camera's rotation and the km per render unit (about the distance
        /// to the nearest surface, so that it sits one unit away). False hands the camera back to the rig.
        /// </summary>
        bool TryGetPose(out int focusIndex, out Double3 offsetKm, out Quaternion rotation, out double kmPerRenderUnit);

        /// <summary>The user asked to focus a body (double-click, Enter, the focus list) while piloting.</summary>
        void RequestFocus(int index);
    }

    /// <summary>
    /// The camera (P5-04 to P5-06). It focuses on and orbits a body, zooms logarithmically from near the
    /// surface to the whole system, flies freely, and moves smoothly between bodies.
    ///
    /// Precision (ARCHITECTURE section 5.2, ADR-0009): the camera stays at the Unity origin. Its offset from
    /// the focus body is kept in double precision (km, host axes) and handed to the core as the snapshot's
    /// view origin, with one render unit equal to the camera's altitude above the focus. Everything near
    /// the camera is therefore of order 1, and the core compresses far distances so no coordinate is large.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        [SerializeField] private SimulationHost host;
        [SerializeField] private InputActionAsset actions;
        [SerializeField] private UIDocument hud;

        [Tooltip("Closest approach to a surface, km. Below about 1 km float rounding of rotating meshes shows.")]
        [SerializeField] private double minAltitudeKm = 1.0;
        [SerializeField] private double maxDistanceKm = 1.0e11;
        [Tooltip("Render distance beyond which the core compresses distances (ADR-0009).")]
        [SerializeField] private double compressionDistance = 1.0e4;
        [SerializeField] private float orbitDegreesPerPixel = 0.2f;
        [SerializeField] private float zoomPerScrollUnit = 0.0025f;
        [Tooltip("A flight between bodies lasts this long at least (A-12) ...")]
        [SerializeField] private float flightMinSeconds = 0.9f;
        [Tooltip("... plus this per decade of distance it covers (on a log scale) ...")]
        [SerializeField] private float flightSecondsPerDecade = 0.45f;
        [Tooltip("... and never longer than this.")]
        [SerializeField] private float flightMaxSeconds = 3f;
        [Tooltip("How quickly a zoom step settles: the time constant of its ease, seconds (A-12).")]
        [SerializeField] private float zoomEaseSeconds = 0.08f;
        [SerializeField] private float clickPickPixels = 24f;

        private Camera cam;
        private InputAction look, drag, zoom, move, moveVertical, point, click, focusSelected, nextBody, previousBody,
            toggleFly, boost;

        // Orbit mode: direction from the focus to the camera (yaw around the host up axis, pitch above the
        // ecliptic) and the distance from the focus center.
        private double yaw;
        private double pitch = 0.45;
        private double distanceKm;
        private double targetDistanceKm; // where zoom steps are taking distanceKm (A-12)

        // Free-fly mode: offset from the focus and the camera's own rotation.
        private Double3 flyOffsetKm;
        private Quaternion flyRotation = Quaternion.identity;

        // A move between focus bodies: from an offset to a distance along the same direction.
        private bool transitioning;
        private float transitionStart;
        private Double3 transitionFromKm;
        private double transitionToDistanceKm;
        private Quaternion transitionFromRotation = Quaternion.identity;

        private bool dragging;
        private float dragTravel;
        private float lastClickTime = -1f;
        private int lastClickIndex = -1;

        /// <summary>The body the camera orbits and the snapshot is centered on.</summary>
        public int FocusIndex { get; private set; } = -1;

        /// <summary>The body shown in the info panel.</summary>
        public int SelectedIndex { get; private set; } = -1;

        /// <summary>Free-fly instead of orbiting the focus.</summary>
        public bool FreeFly { get; private set; }

        /// <summary>Enlarged-bodies scale mode (UC-06) and its factor.</summary>
        public bool EnlargedBodies { get; set; }

        public double BodyScale { get; set; } = 100.0;

        /// <summary>The camera's offset from the focus center, km, host axes.</summary>
        public Double3 OffsetKm { get; private set; }

        /// <summary>True during a flight to a new focus (A-12).</summary>
        public bool Transitioning => transitioning;

        /// <summary>How long the latest flight to a new focus lasts, seconds (<see cref="FlightDuration"/>).</summary>
        public float FlightSeconds { get; private set; }

        /// <summary>The body under the pointer, or under the hovered label, or -1 (A-12). Hovering selects nothing.</summary>
        public int HoveredIndex { get; private set; } = -1;

        /// <summary>Raised when <see cref="HoveredIndex"/> changes.</summary>
        public event Action<int> HoverChanged;

        private int labelHoverIndex = -1;

        /// <summary>
        /// The driver of the camera, or null when the rig's own controls drive it (orbit, zoom, free fly). While a
        /// pilot gives a pose, the rig only picks and selects bodies and forwards focus requests to it.
        /// </summary>
        public ICameraPilot Pilot { get; set; }

        /// <summary>True when the pilot drove the camera this frame.</summary>
        public bool Piloted { get; private set; }

        private int pilotFocus;
        private Double3 pilotOffsetKm;
        private Quaternion pilotRotation = Quaternion.identity;
        private double pilotKmPerUnit = 1.0;

        /// <summary>Raised when the selection changes (the HUD shows the body).</summary>
        public event Action<int> SelectionChanged;

        /// <summary>Raised when the focus changes.</summary>
        public event Action<int> FocusChanged;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            var map = actions.FindActionMap("Viewer", true);
            look = map.FindAction("Look", true);
            drag = map.FindAction("Drag", true);
            zoom = map.FindAction("Zoom", true);
            move = map.FindAction("Move", true);
            moveVertical = map.FindAction("MoveVertical", true);
            point = map.FindAction("Point", true);
            click = map.FindAction("Click", true);
            focusSelected = map.FindAction("FocusSelected", true);
            nextBody = map.FindAction("NextBody", true);
            previousBody = map.FindAction("PreviousBody", true);
            toggleFly = map.FindAction("ToggleFly", true);
            boost = map.FindAction("Boost", true);
            host.Loaded += OnLoaded;
        }

        private void OnEnable() => actions.FindActionMap("Viewer", true).Enable();

        private void OnDisable() => actions.FindActionMap("Viewer", true).Disable();

        private void OnDestroy()
        {
            if (host != null)
            {
                host.Loaded -= OnLoaded;
            }
        }

        private CoreSimulation Sim => host.Simulation;

        private void OnLoaded()
        {
            // Start on the star, far enough out to see every body: 1.5 times the farthest one's distance
            // (taken from the data at the start time, not a typed-in constant).
            var star = -1;
            for (var i = 0; i < Sim.Bodies.Count && star < 0; ++i)
            {
                if (Sim.Bodies[i].Kind == 1)
                {
                    star = i;
                }
            }
            FocusIndex = star >= 0 ? star : 0;
            SelectedIndex = FocusIndex;
            var now = Sim.Clock.Now;
            var farthest = 0.0;
            foreach (var body in Sim.Bodies)
            {
                if (Sim.TryState(body.Id, Sim.Bodies[FocusIndex].Id, UsimFrame.EclipJ2000, now, out var s))
                {
                    farthest = Math.Max(farthest, Double3.From(s.PositionKm).Length);
                }
            }
            distanceKm = farthest > 0.0 ? 1.5 * farthest : 1.0e9;
            targetDistanceKm = distanceKm;
            UpdateRequest();
            FocusChanged?.Invoke(FocusIndex);
            SelectionChanged?.Invoke(SelectedIndex);
        }

        /// <summary>The radius the camera keeps its distance from: the largest radius, enlarged if shown so.</summary>
        private double FocusRadiusKm()
        {
            if (Sim == null || FocusIndex < 0 || FocusIndex >= Sim.Bodies.Count)
            {
                return 0.0;
            }
            // From the catalog, not the snapshot: a snapshot radius is scaled down when compressed.
            return Sim.Bodies[FocusIndex].MaxRadiusKm * (EnlargedBodies ? BodyScale : 1.0);
        }

        private double MinDistanceKm() => FocusRadiusKm() + minAltitudeKm;

        private void Update()
        {
            if (Sim == null)
            {
                return;
            }
            UpdatePilot();
            HandleInput();
            UpdateRequest();
        }

        // Takes this frame's pose from the pilot, or gives the rig back its controls where the pilot left the camera.
        private void UpdatePilot()
        {
            var wasPiloted = Piloted;
            Piloted = Pilot != null &&
                      Pilot.TryGetPose(out pilotFocus, out pilotOffsetKm, out pilotRotation, out pilotKmPerUnit) &&
                      pilotFocus >= 0 && pilotFocus < Sim.Bodies.Count;
            if (Piloted)
            {
                transitioning = false;
                FreeFly = false;
                if (pilotFocus != FocusIndex)
                {
                    FocusIndex = pilotFocus;
                    FocusChanged?.Invoke(FocusIndex);
                }
            }
            else if (wasPiloted)
            {
                // Orbit the focus from where the pilot left the camera.
                var direction = OffsetKm.Normalized;
                distanceKm = Math.Max(OffsetKm.Length, MinDistanceKm());
                targetDistanceKm = distanceKm;
                yaw = Math.Atan2(direction.X, direction.Z);
                pitch = Math.Clamp(Math.Asin(Math.Clamp(direction.Y, -1.0, 1.0)), -1.55, 1.55);
            }
        }

        private bool PointerOverUi(Vector2 screen)
        {
            var panel = hud != null ? hud.rootVisualElement?.panel : null;
            if (panel == null)
            {
                return false;
            }
            var position = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            return panel.Pick(position) != null;
        }

        private void HandleInput()
        {
            var pointer = point.ReadValue<Vector2>();
            var overUi = PointerOverUi(pointer);
            var dt = Time.unscaledDeltaTime;
            // While the date field has the keyboard, keys type: no hotkeys, no key zoom, no flying.
            var typing = HudKeyboard.Typing(hud);
            UpdateHover(pointer, overUi);

            if (drag.WasPressedThisFrame() && !overUi)
            {
                dragging = true;
                dragTravel = 0f;
            }
            if (!drag.IsPressed())
            {
                dragging = false;
            }
            var delta = look.ReadValue<Vector2>();
            if (dragging && !Piloted)
            {
                dragTravel += delta.magnitude;
                if (FreeFly)
                {
                    flyRotation = Quaternion.AngleAxis(delta.x * orbitDegreesPerPixel, Vector3.up) * flyRotation *
                                  Quaternion.AngleAxis(-delta.y * orbitDegreesPerPixel, Vector3.right);
                }
                else
                {
                    yaw -= delta.x * orbitDegreesPerPixel * Mathf.Deg2Rad;
                    pitch = Math.Clamp(pitch - delta.y * orbitDegreesPerPixel * Mathf.Deg2Rad, -1.55, 1.55);
                }
            }

            var scroll = zoom.ReadValue<float>();
            if (typing && Mathf.Abs(scroll) <= 2f)
            {
                scroll = 0f; // the minus and equals keys, not the wheel
            }
            if (!overUi && Mathf.Abs(scroll) > 0f && !FreeFly && !Piloted)
            {
                // Mouse wheels report large steps, keys report +-1 per frame: scale keys by time.
                var amount = Mathf.Abs(scroll) > 2f ? scroll * zoomPerScrollUnit : scroll * 2f * dt;
                ZoomBy(Math.Exp(-amount));
            }

            if (click.WasReleasedThisFrame() && !overUi && dragTravel < 4f)
            {
                var picked = PickBody(pointer);
                if (picked >= 0)
                {
                    var now = Time.unscaledTime;
                    if (picked == lastClickIndex && now - lastClickTime < 0.35f)
                    {
                        Focus(picked);
                    }
                    Select(picked);
                    lastClickTime = now;
                    lastClickIndex = picked;
                }
            }
            if (!typing)
            {
                if (focusSelected.WasPressedThisFrame() && SelectedIndex >= 0)
                {
                    Focus(SelectedIndex);
                }
                if (nextBody.WasPressedThisFrame())
                {
                    StepFocus(1);
                }
                if (previousBody.WasPressedThisFrame())
                {
                    StepFocus(-1);
                }
                if (toggleFly.WasPressedThisFrame() && !Piloted)
                {
                    SetFreeFly(!FreeFly);
                }
            }

            if (FreeFly && !Piloted)
            {
                var input = typing ? Vector2.zero : move.ReadValue<Vector2>();
                var vertical = typing ? 0f : moveVertical.ReadValue<float>();
                var altitude = Math.Max(OffsetKm.Length - FocusRadiusKm(), minAltitudeKm);
                var speed = altitude * (boost.IsPressed() ? 5.0 : 0.5); // km per second, scaled by altitude
                var direction = flyRotation * new Vector3(input.x, vertical, input.y);
                flyOffsetKm += Double3.From(direction) * (speed * dt);
                // Never inside the focus body.
                var minimum = MinDistanceKm();
                if (flyOffsetKm.Length < minimum)
                {
                    flyOffsetKm = flyOffsetKm.Normalized * minimum;
                }
            }
        }

        /// <summary>
        /// Zooms the orbit by <paramref name="factor"/> on the altitude above the focus (below 1 nearer), as the wheel
        /// and keys do; the distance eases to the new value (A-12).
        /// </summary>
        public void ZoomBy(double factor)
        {
            var minimum = MinDistanceKm();
            // Steps move the target; the distance eases toward it (UpdateRequest).
            var altitude = Math.Max(targetDistanceKm - FocusRadiusKm(), minAltitudeKm) * factor;
            targetDistanceKm = Math.Clamp(FocusRadiusKm() + altitude, minimum, maxDistanceKm);
        }

        private void StepFocus(int step)
        {
            var count = host.Views.Count;
            for (var i = 1; i <= count; ++i)
            {
                var index = ((FocusIndex + step * i) % count + count) % count;
                if (host.Views[index].Info.HasMeanRadius)
                {
                    Focus(index);
                    Select(index);
                    return;
                }
            }
        }

        /// <summary>The body whose screen position is nearest to <paramref name="screen"/>, or -1.</summary>
        public int PickBody(Vector2 screen)
        {
            var best = -1;
            var bestDistance = clickPickPixels;
            for (var i = 0; i < host.Views.Count; ++i)
            {
                var view = host.Views[i];
                if (!view.HasPosition)
                {
                    continue;
                }
                var p = cam.WorldToScreenPoint(view.transform.position);
                if (p.z <= 0f)
                {
                    continue;
                }
                // A body covers its disk on screen; prefer real bodies over barycenters at the same spot.
                var radiusPixels = view.MaxRenderRadius / p.z * (Screen.height / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)));
                var distance = Vector2.Distance(screen, new Vector2(p.x, p.y)) - radiusPixels;
                if (!view.Info.HasMeanRadius)
                {
                    distance += 4f;
                }
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Shows <paramref name="index"/> in the info panel.</summary>
        public void Select(int index)
        {
            if (index == SelectedIndex)
            {
                return;
            }
            SelectedIndex = index;
            SelectionChanged?.Invoke(index);
        }

        /// <summary>
        /// Moves the camera to <paramref name="index"/> and orbits it from then on. While piloted, the request goes to
        /// the pilot instead.
        /// </summary>
        public void Focus(int index)
        {
            if (Sim == null || index < 0 || index >= host.Views.Count)
            {
                return;
            }
            if (Piloted && Pilot != null)
            {
                Pilot.RequestFocus(index);
                return;
            }
            if (index == FocusIndex)
            {
                return;
            }
            // The camera's current offset seen from the new focus: offset + (old focus relative to new).
            var oldFocus = Sim.Bodies[FocusIndex].Id;
            var newFocus = Sim.Bodies[index].Id;
            var from = OffsetKm;
            if (Sim.TryState(oldFocus, newFocus, UsimFrame.EclipJ2000, Sim.Clock.Now, out var relative))
            {
                from += Double3.From(CoreSimulation.AxesToHost(NativeConstants.AxesUnity, relative.PositionKm));
            }
            FocusIndex = index;
            var info = Sim.Bodies[index];
            var radius = info.MaxRadiusKm * (EnlargedBodies ? BodyScale : 1.0);
            transitionFromKm = from;
            transitionToDistanceKm = radius > 0.0 ? Math.Max(radius * 4.0, radius + minAltitudeKm) : from.Length * 0.25;
            transitionFromRotation = transform.rotation;
            FlightSeconds = FlightDuration(from.Length, transitionToDistanceKm, flightMinSeconds, flightSecondsPerDecade,
                                           flightMaxSeconds);
            transitionStart = Time.unscaledTime;
            transitioning = true;
            if (FreeFly)
            {
                SetFreeFly(false);
            }
            FocusChanged?.Invoke(index);
        }

        /// <summary>
        /// Places the camera at once (no transition): orbiting <paramref name="index"/> at
        /// <paramref name="distanceFromCenterKm"/> from its center, in the direction given by yaw and pitch
        /// (radians, host axes, pitch above the ecliptic). For tests and scripted views.
        /// </summary>
        public void JumpTo(int index, double distanceFromCenterKm, double yawRad, double pitchRad)
        {
            if (Sim == null || index < 0 || index >= Sim.Bodies.Count)
            {
                return;
            }
            var changed = index != FocusIndex;
            FocusIndex = index;
            transitioning = false;
            if (FreeFly)
            {
                SetFreeFly(false);
            }
            yaw = yawRad;
            pitch = Math.Clamp(pitchRad, -1.55, 1.55);
            distanceKm = Math.Max(distanceFromCenterKm, MinDistanceKm());
            targetDistanceKm = distanceKm;
            UpdateRequest();
            if (changed)
            {
                FocusChanged?.Invoke(index);
            }
        }

        /// <summary>Switches between orbiting the focus and flying freely.</summary>
        public void SetFreeFly(bool on)
        {
            if (on == FreeFly)
            {
                return;
            }
            FreeFly = on;
            if (on)
            {
                flyOffsetKm = OffsetKm;
                flyRotation = transform.rotation;
            }
            else
            {
                var direction = OffsetKm.Normalized;
                distanceKm = Math.Max(OffsetKm.Length, MinDistanceKm());
                targetDistanceKm = distanceKm;
                yaw = Math.Atan2(direction.X, direction.Z);
                pitch = Math.Asin(Math.Clamp(direction.Y, -1.0, 1.0));
            }
        }

        /// <summary>
        /// How long a flight from <paramref name="fromKm"/> to <paramref name="toKm"/> from the new focus lasts: the
        /// shortest time plus the time per decade of the distance's change, at most the longest (A-12). A hop to a
        /// neighbor is short; a flight from the edge of the system to a moon takes longer, but never long.
        /// </summary>
        public static float FlightDuration(double fromKm, double toKm, float minSeconds, float secondsPerDecade,
                                           float maxSeconds)
        {
            var decades = fromKm > 0.0 && toKm > 0.0 ? Math.Abs(Math.Log10(fromKm / toKm)) : 0.0;
            return Mathf.Clamp(minSeconds + secondsPerDecade * (float)decades, minSeconds, maxSeconds);
        }

        /// <summary>Smootherstep (6s^5 - 15s^4 + 10s^3): a slow start and a soft landing, with no jerk at either end.</summary>
        public static float Smootherstep(float s)
        {
            s = Mathf.Clamp01(s);
            return s * s * s * (s * (s * 6f - 15f) + 10f);
        }

        // Eases a distance toward its target on a log scale with time constant `seconds` (exact for any frame time).
        private static double EaseDistance(double current, double target, float dt, float seconds)
        {
            if (current <= 0.0 || target <= 0.0 || seconds <= 0f)
            {
                return target;
            }
            var k = 1.0 - Math.Exp(-dt / seconds);
            var eased = Math.Exp(Math.Log(current) + (Math.Log(target) - Math.Log(current)) * k);
            return Math.Abs(eased / target - 1.0) < 1e-6 ? target : eased;
        }

        /// <summary>The body whose label the pointer is over (from the HUD), or -1 when it leaves (A-12).</summary>
        public void SetLabelHover(int index)
        {
            labelHoverIndex = index;
        }

        // The body under the pointer (as a click would pick it), or the hovered label's; nothing while dragging or
        // over other UI. The cursor shows that a click picks something.
        private void UpdateHover(Vector2 pointer, bool overUi)
        {
            var onScreen = pointer.x >= 0f && pointer.y >= 0f && pointer.x < Screen.width && pointer.y < Screen.height;
            var hovered = labelHoverIndex >= 0 ? labelHoverIndex
                : !overUi && !dragging && onScreen ? PickBody(pointer) : -1;
            if (hovered == HoveredIndex)
            {
                return;
            }
            HoveredIndex = hovered;
            PickCursor.Show(hovered >= 0);
            HoverChanged?.Invoke(hovered);
        }

        private static Double3 Direction(double yawRad, double pitchRad) =>
            new Double3(Math.Cos(pitchRad) * Math.Sin(yawRad), Math.Sin(pitchRad), Math.Cos(pitchRad) * Math.Cos(yawRad));

        private void UpdateRequest()
        {
            if (Sim == null || FocusIndex < 0)
            {
                return;
            }
            Quaternion rotation;
            var altitude = 0.0;
            if (Piloted)
            {
                OffsetKm = pilotOffsetKm;
                rotation = pilotRotation;
                altitude = Math.Max(pilotKmPerUnit, minAltitudeKm);
            }
            else if (transitioning)
            {
                // Straight toward the new focus, distance interpolated on a log scale with a slow start and a soft
                // landing; the view turns from where it looked to the new focus over the first part (A-12).
                var s = Mathf.Clamp01((Time.unscaledTime - transitionStart) / Mathf.Max(FlightSeconds, 1e-3f));
                var eased = Smootherstep(s);
                var from = Math.Max(transitionFromKm.Length, 1e-9);
                var to = Math.Max(transitionToDistanceKm, MinDistanceKm());
                var d = Math.Exp(Math.Log(from) + (Math.Log(to) - Math.Log(from)) * eased);
                var direction = transitionFromKm.Normalized;
                OffsetKm = direction * d;
                var turn = Mathf.Clamp01(s / 0.45f);
                rotation = Quaternion.Slerp(transitionFromRotation,
                                            Quaternion.LookRotation(-direction.ToVector3(), Vector3.up),
                                            turn * turn * (3f - 2f * turn));
                if (s >= 1f)
                {
                    transitioning = false;
                    distanceKm = d;
                    targetDistanceKm = d;
                    yaw = Math.Atan2(direction.X, direction.Z);
                    pitch = Math.Asin(Math.Clamp(direction.Y, -1.0, 1.0));
                }
            }
            else if (FreeFly)
            {
                OffsetKm = flyOffsetKm;
                rotation = flyRotation;
            }
            else
            {
                targetDistanceKm = Math.Clamp(targetDistanceKm, MinDistanceKm(), maxDistanceKm);
                distanceKm = Math.Clamp(EaseDistance(distanceKm, targetDistanceKm, Time.unscaledDeltaTime, zoomEaseSeconds),
                                        MinDistanceKm(), maxDistanceKm);
                var direction = Direction(yaw, pitch);
                OffsetKm = direction * distanceKm;
                rotation = Quaternion.LookRotation(-direction.ToVector3(), Vector3.up);
            }

            if (!Piloted)
            {
                altitude = Math.Max(OffsetKm.Length - FocusRadiusKm(), minAltitudeKm);
            }
            var request = host.Request;
            request.Center = Sim.Bodies[FocusIndex].Id;
            request.Frame = UsimFrame.EclipJ2000;
            request.OriginKm = CoreSimulation.AxesFromHost(NativeConstants.AxesUnity, OffsetKm.ToUsim());
            request.KmPerRenderUnit = altitude; // the nearest surface is about one unit away
            request.Axes = NativeConstants.AxesUnity;
            request.ScaleMode = EnlargedBodies ? NativeConstants.ScaleModeEnlarged : NativeConstants.ScaleModeTrue;
            request.BodyScale = EnlargedBodies ? BodyScale : 1.0;
            request.CompressionDistance = compressionDistance;
            host.Request = request;

            // ADR-0009: the camera sits at the origin; the near plane is a small fraction of the altitude
            // (one render unit) and the far plane covers the compressed distances.
            var t = transform;
            t.position = Vector3.zero;
            t.rotation = rotation;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = (float)(compressionDistance * 60.0);
        }
    }
}
