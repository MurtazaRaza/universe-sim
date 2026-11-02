using System;
using System.Collections.Generic;
using UniverseSim.Unity.Native;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The spaceship of the stylized style (ADR-0016, SPEC FR-REN-16): a fictional craft that carries the camera.
    /// It flies freely near any body, travels to another in seconds, and descends to hover over a point of a body's
    /// reference surface. Every fact it shows comes from the core: where bodies are, the distance and light time, a
    /// two-body transfer estimate of the real trip, and at a surface point the horizon, the Sun, the gravity and the
    /// length of the day. The ship itself is a display object: not in the catalog, never moving the clock or anything
    /// the core computes, drawn at a fixed size on screen, and gone in the realistic style.
    ///
    /// Like the camera rig, it keeps its position in double precision relative to a body center (its anchor, the
    /// nearest body), in host axes and the ECLIPJ2000 frame. Its motion is navigation, not physics: no gravity acts on
    /// it (SPEC section 3.3), and every change of reference body or surface coordinates goes through the core.
    /// </summary>
    [DefaultExecutionOrder(-110)] // before the camera rig, which takes the pose in its Update
    public sealed class ShipPilot : MonoBehaviour, IStyleEffect, ICameraPilot
    {
        /// <summary>What the ship is doing.</summary>
        public enum Mode
        {
            Off,
            Flying,
            Traveling,
            Descending,
            Hovering,
            Ascending,
        }

        [SerializeField] private SimulationHost host;
        [SerializeField] private CameraRig rig;
        [SerializeField] private VisualStyleSettings style;
        [SerializeField] private InputActionAsset actions;
        [SerializeField] private UIDocument hud;
        [SerializeField] private Material shipMaterial;

        // Display choices (docs/ART_DIRECTION.md), not physical values.
        [Header("Look")]
        [Tooltip("Ship length in render units; one unit is about the distance to the nearest surface.")]
        [SerializeField] private float shipSize = 0.075f;
        [SerializeField] private float chaseDistance = 0.32f;
        [SerializeField] private float chaseHeight = 0.07f;

        [Header("Flight (navigation, not physics)")]
        [Tooltip("Cruise speed in heights per second: the ship covers its own height above the surface below it.")]
        [SerializeField] private double cruisePerSecond = 0.6;
        [SerializeField] private double boostFactor = 8.0;
        [SerializeField] private float turnDegreesPerSecond = 70f;
        [SerializeField] private float steerDegreesPerPixel = 0.15f;
        [Tooltip("Closest approach to a reference surface, km (the camera rig's limit is 1 km).")]
        [SerializeField] private double minAltitudeKm = 2.0;

        [Header("Travel")]
        [SerializeField] private float travelMinSeconds = 4f;
        [SerializeField] private float travelMaxSeconds = 14f;
        [Tooltip("Seconds added per natural-log step of the ratio of start to arrival distance.")]
        [SerializeField] private float travelSecondsPerLog = 1f;
        [Tooltip("Arrival distance from the target's center, in its largest radius.")]
        [SerializeField] private double arrivalRadii = 3.0;

        [Header("Surface")]
        [Tooltip("Hover height above the reference surface, as a fraction of the mean radius: low enough to see the " +
                 "surface map, high enough that the drawn sphere's facets stay far below.")]
        [SerializeField] private double hoverFraction = 0.01;
        [SerializeField] private double maxHoverFraction = 0.5;

        private const int BodyKindStar = 1; // usim::catalog::BodyKind

        private InputAction toggleShip, travel, land, move, moveVertical, look, drag, zoom, boost, point;
        private GameObject model;
        private Material material;
        private Mesh mesh;
        private Exaggeration[] exaggerations;
        private readonly List<int> candidates = new List<int>();

        private int anchor = -1;
        private Double3 shipKm;
        private Quaternion shipRotation = Quaternion.identity;
        private Quaternion viewRotation = Quaternion.identity;
        private float bank;
        private double speedKmS;
        private double altitudeKm;
        private float nextReanchor;

        // Travel.
        private Double3 travelFromKm;
        private double travelArriveKm;
        private float travelStart;
        private Quaternion travelStartRotation;

        // Surface: the site's coordinates, the ship's height above it, its heading from north and the view.
        private double siteLatitudeRad, siteLongitudeRad, siteAltitudeKm, hoverAltitudeKm, descendFromKm;
        private double headingRad;
        private float verticalStart, verticalSeconds;
        private float viewYawDegrees, viewPitchDegrees = 12f;
        private float nextConditions;

        // This frame's camera pose.
        private Double3 poseOffsetKm;
        private double poseKmPerUnit = 1.0;

        private static readonly int LightDirectionId = Shader.PropertyToID("_LightDirection");
        private static readonly int SunlitId = Shader.PropertyToID("_Sunlit");
        private static readonly int EngineGlowId = Shader.PropertyToID("_EngineGlow");

        /// <summary>What the ship is doing.</summary>
        public Mode State { get; private set; } = Mode.Off;

        /// <summary>True while the ship is out (any mode but <see cref="Mode.Off"/>).</summary>
        public bool Active => State != Mode.Off;

        /// <summary>True when the ship can be used: the data is loaded and the style is stylized.</summary>
        public bool Available => Sim != null && style != null && style.Style == VisualStyle.Stylized;

        /// <summary>The body the ship's position is relative to (catalog index), or -1.</summary>
        public int AnchorIndex => anchor;

        /// <summary>The body of the last trip (catalog index), or -1.</summary>
        public int TargetIndex { get; private set; } = -1;

        /// <summary>The ship's position relative to the anchor's center, km, host axes, ECLIPJ2000.</summary>
        public Double3 PositionKm => shipKm;

        /// <summary>Height above the anchor's reference surface, km (from the core where the body has a site).</summary>
        public double AltitudeKm => altitudeKm;

        /// <summary>The core's estimate of the last trip, when <see cref="HasTransfer"/>.</summary>
        public UsimTransferEstimate Transfer { get; private set; }

        /// <summary>True when the core could estimate the last trip.</summary>
        public bool HasTransfer { get; private set; }

        /// <summary>Why the last trip has no estimate (the core's message), or empty.</summary>
        public string TransferNote { get; private set; } = string.Empty;

        /// <summary>The body the last trip started near (catalog index), or -1.</summary>
        public int TripFromIndex { get; private set; } = -1;

        /// <summary>How long the last flight lasts on screen, s (real time, not simulated).</summary>
        public float FlightSeconds { get; private set; }

        /// <summary>How far the current trip has come, 0 to 1.</summary>
        public float TravelProgress { get; private set; }

        /// <summary>The reference surface below the ship (altitude 0) while descending or hovering.</summary>
        public UsimSurfaceSite Site { get; private set; }

        /// <summary>True when <see cref="Site"/> holds the surface below the ship.</summary>
        public bool HasSite { get; private set; }

        /// <summary>The anchor's day length, when <see cref="HasDay"/>.</summary>
        public UsimDayLength Day { get; private set; }

        /// <summary>True when the core gave the anchor's day length.</summary>
        public bool HasDay { get; private set; }

        /// <summary>True when the anchor has what a site needs: a radius and a rotation model.</summary>
        public bool CanDescend => Sim != null && anchor >= 0 && Sim.Bodies[anchor].HasMeanRadius && Sim.Bodies[anchor].HasRotation;

        /// <summary>Raised when <see cref="State"/> changes.</summary>
        public event Action<Mode> ModeChanged;

        public IReadOnlyList<Exaggeration> Exaggerations => exaggerations ??= new[]
        {
            new Exaggeration("Spaceship", "a fictional craft for exploring; it is not in the data and moves nothing",
                             "drawn at a fixed size on screen, not to scale"),
            new Exaggeration("Flights", "compressed in time: seconds instead of months or years",
                             "the real distance, light time and a two-body trip estimate are shown"),
        };

        public bool ActiveInView(Camera cam) => Active && cam != null;

        private CoreSimulation Sim => host != null ? host.Simulation : null;

        private void Awake()
        {
            var map = actions.FindActionMap("Viewer", true);
            toggleShip = map.FindAction("ToggleShip", true);
            travel = map.FindAction("Travel", true);
            land = map.FindAction("Land", true);
            move = map.FindAction("Move", true);
            moveVertical = map.FindAction("MoveVertical", true);
            look = map.FindAction("Look", true);
            drag = map.FindAction("Drag", true);
            zoom = map.FindAction("Zoom", true);
            boost = map.FindAction("Boost", true);
            point = map.FindAction("Point", true);

            mesh = ShipMesh.Create();
            model = new GameObject("Spaceship");
            model.transform.SetParent(transform, false);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = model.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (shipMaterial != null)
            {
                material = new Material(shipMaterial) { name = "Spaceship (runtime)" };
                renderer.sharedMaterial = material;
            }
            model.SetActive(false);
        }

        private void OnEnable()
        {
            if (style != null)
            {
                style.Changed += OnStyleChanged;
            }
        }

        private void OnDisable()
        {
            if (style != null)
            {
                style.Changed -= OnStyleChanged;
            }
            SetActive(false);
        }

        private void OnDestroy()
        {
            if (material != null)
            {
                Destroy(material);
            }
            if (mesh != null)
            {
                Destroy(mesh);
            }
        }

        // The ship belongs to the stylized style; switching to the realistic one lands the camera back on the rig.
        private void OnStyleChanged(VisualStyle newStyle)
        {
            if (newStyle != VisualStyle.Stylized)
            {
                SetActive(false);
            }
        }

        /// <summary>Takes the ship out from where the camera is, or puts it away and hands the camera back.</summary>
        public void SetActive(bool on)
        {
            if (on == Active || (on && !Available))
            {
                return;
            }
            if (!on)
            {
                if (rig != null && ReferenceEquals(rig.Pilot, this))
                {
                    rig.Pilot = null;
                }
                if (model != null)
                {
                    model.SetActive(false);
                }
                SetMode(Mode.Off);
                return;
            }

            // True scale: the ship flies among the real sizes and distances.
            rig.EnlargedBodies = false;
            rig.SetFreeFly(false);
            anchor = rig.FocusIndex;
            shipKm = rig.OffsetKm;
            if (anchor < 0 || !Sim.Bodies[anchor].HasMeanRadius)
            {
                var start = anchor;
                anchor = FirstBodyWithRadius();
                if (anchor < 0)
                {
                    return;
                }
                if (start >= 0 && TryRelative(start, anchor, out var startRelative))
                {
                    shipKm += startRelative;
                }
            }
            shipRotation = rig.transform.rotation;
            viewRotation = shipRotation;
            speedKmS = 0.0;
            bank = 0f;
            HasSite = false;
            HasDay = false;
            UpdateAltitude();
            model.SetActive(true);
            rig.Pilot = this;
            SetMode(Mode.Flying);
            UpdatePose();
        }

        /// <summary>Flies to the body at <paramref name="index"/> (catalog order): an autopilot trip of seconds.</summary>
        public void TravelTo(int index)
        {
            if (!Active || Sim == null || index < 0 || index >= Sim.Bodies.Count || !Sim.Bodies[index].HasMeanRadius)
            {
                return;
            }
            var now = Sim.Clock.Now;
            TripFromIndex = anchor;
            TargetIndex = index;
            if (index != anchor)
            {
                HasTransfer = Sim.TryTransferEstimate(Sim.Bodies[anchor].Id, Sim.Bodies[index].Id, now, out var estimate,
                                                      out var error);
                Transfer = estimate;
                TransferNote = HasTransfer ? string.Empty : error;
                if (!TryRelative(anchor, index, out var relative))
                {
                    return;
                }
                shipKm += relative;
                anchor = index;
            }
            else
            {
                HasTransfer = false;
                TransferNote = "already near " + Sim.Bodies[index].Name;
            }
            HasSite = false;
            HasDay = false;
            travelFromKm = shipKm;
            travelArriveKm = Math.Max(arrivalRadii * Sim.Bodies[index].MaxRadiusKm,
                                      Sim.Bodies[index].MaxRadiusKm + minAltitudeKm);
            var ratio = Math.Max(travelFromKm.Length / travelArriveKm, 1.0);
            FlightSeconds = Mathf.Clamp(travelMinSeconds + travelSecondsPerLog * (float)Math.Log(ratio), travelMinSeconds,
                                        travelMaxSeconds);
            travelStart = Time.unscaledTime;
            travelStartRotation = shipRotation;
            TravelProgress = 0f;
            SetMode(Mode.Traveling);
        }

        /// <summary>Goes down to hover over the point of the anchor's reference surface below the ship.</summary>
        public void Descend()
        {
            if (!(State == Mode.Flying || State == Mode.Ascending) || !CanDescend || !TrySiteBelow(shipKm, out var below))
            {
                return;
            }
            siteLatitudeRad = below.LatitudeRad;
            siteLongitudeRad = below.LongitudeRad;
            hoverAltitudeKm = Math.Max(minAltitudeKm, hoverFraction * Sim.Bodies[anchor].MeanRadiusKm);
            descendFromKm = Math.Max(below.AltitudeKm, hoverAltitudeKm);
            siteAltitudeKm = descendFromKm;
            var forward = Double3.From(shipRotation * Vector3.forward);
            headingRad = Math.Atan2(forward.Dot(Double3.From(below.East)), forward.Dot(Double3.From(below.North)));
            verticalStart = Time.unscaledTime;
            verticalSeconds = Mathf.Clamp(3f + 0.8f * (float)Math.Log(descendFromKm / hoverAltitudeKm), 3f, 9f);
            HasDay = Sim.TryDayLength(Sim.Bodies[anchor].Id, Sim.Clock.Now, out var day);
            Day = day;
            viewYawDegrees = 0f;
            viewPitchDegrees = 12f;
            nextConditions = 0f;
            SetMode(Mode.Descending);
        }

        /// <summary>Climbs from hovering back to free flight, about one radius up.</summary>
        public void LiftOff()
        {
            if (!(State == Mode.Hovering || State == Mode.Descending))
            {
                return;
            }
            descendFromKm = siteAltitudeKm;
            hoverAltitudeKm = Math.Max(Sim.Bodies[anchor].MeanRadiusKm, siteAltitudeKm);
            verticalStart = Time.unscaledTime;
            verticalSeconds = Mathf.Clamp(2f + 0.8f * (float)Math.Log(hoverAltitudeKm / descendFromKm), 2f, 6f);
            SetMode(Mode.Ascending);
        }

        public void RequestFocus(int index) => TravelTo(index);

        public bool TryGetPose(out int focusIndex, out Double3 offsetKm, out Quaternion rotation, out double kmPerRenderUnit)
        {
            focusIndex = anchor;
            offsetKm = poseOffsetKm;
            rotation = viewRotation;
            kmPerRenderUnit = poseKmPerUnit;
            return Active && Sim != null && anchor >= 0;
        }

        private void SetMode(Mode mode)
        {
            if (mode == State)
            {
                return;
            }
            State = mode;
            ModeChanged?.Invoke(mode);
        }

        private bool Typing => hud != null && hud.rootVisualElement?.focusController?.focusedElement is TextField;

        private void Update()
        {
            if (Sim == null)
            {
                return;
            }
            if (!Typing)
            {
                if (toggleShip.WasPressedThisFrame())
                {
                    SetActive(!Active);
                }
                if (Active && travel.WasPressedThisFrame() && rig.SelectedIndex >= 0)
                {
                    TravelTo(rig.SelectedIndex);
                }
                if (Active && land.WasPressedThisFrame())
                {
                    if (State == Mode.Hovering || State == Mode.Descending)
                    {
                        LiftOff();
                    }
                    else
                    {
                        Descend();
                    }
                }
            }
            if (!Active)
            {
                return;
            }

            var dt = Time.unscaledDeltaTime;
            switch (State)
            {
                case Mode.Flying:
                    Fly(dt);
                    break;
                case Mode.Traveling:
                    Travel();
                    break;
                case Mode.Descending:
                case Mode.Ascending:
                    Vertical();
                    break;
                case Mode.Hovering:
                    Hover(dt);
                    break;
            }
            UpdatePose();
        }

        private void LateUpdate()
        {
            if (!Active || Sim == null)
            {
                return;
            }
            // The camera is at the origin looking along viewRotation; the ship sits ahead of and below it.
            var t = model.transform;
            t.SetPositionAndRotation(viewRotation * new Vector3(0f, -chaseHeight, chaseDistance),
                                     shipRotation * Quaternion.AngleAxis(bank, Vector3.forward));
            t.localScale = Vector3.one * shipSize;
            if (material != null)
            {
                var light = LightDirection(out var sunlit);
                material.SetVector(LightDirectionId, light);
                material.SetFloat(SunlitId, sunlit);
                var cruise = Math.Max(cruisePerSecond * Math.Max(altitudeKm, minAltitudeKm), 1e-9);
                var glow = State == Mode.Traveling ? 0.35f + 0.65f * Mathf.Sin(Mathf.PI * TravelProgress)
                    : 0.25f + 0.75f * Mathf.Clamp01((float)(Math.Abs(speedKmS) / cruise));
                material.SetFloat(EngineGlowId, glow);
            }
        }

        // ---- Modes ----

        private Vector2 SteerInput(out bool steering)
        {
            steering = drag.IsPressed() && !PointerOverUi();
            return steering ? look.ReadValue<Vector2>() : Vector2.zero;
        }

        private bool PointerOverUi()
        {
            var panel = hud != null ? hud.rootVisualElement?.panel : null;
            if (panel == null)
            {
                return false;
            }
            var screen = point.ReadValue<Vector2>();
            var position = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            return panel.Pick(position) != null;
        }

        private void Fly(float dt)
        {
            var input = Typing ? Vector2.zero : move.ReadValue<Vector2>();
            var vertical = Typing ? 0f : moveVertical.ReadValue<float>();
            var steer = SteerInput(out _);

            // Turn: A and D yaw, Q and E pitch, dragging steers; the ship banks into turns.
            var yaw = input.x * turnDegreesPerSecond * dt + steer.x * steerDegreesPerPixel;
            var pitch = -vertical * turnDegreesPerSecond * dt - steer.y * steerDegreesPerPixel;
            shipRotation = shipRotation * Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, Vector3.right);
            // Level the wings slowly to the local vertical (away from the anchor's center).
            var forward = shipRotation * Vector3.forward;
            var up = shipKm.Length > 0.0 ? shipKm.Normalized.ToVector3() : Vector3.up;
            if (Mathf.Abs(Vector3.Dot(forward, up)) < 0.98f)
            {
                shipRotation = Quaternion.Slerp(shipRotation, Quaternion.LookRotation(forward, up), 1f - Mathf.Exp(-dt / 1.5f));
            }
            bank = Mathf.Lerp(bank, -Mathf.Clamp(input.x + steer.x * 0.05f, -1f, 1f) * 30f, 1f - Mathf.Exp(-dt / 0.25f));

            // Thrust: a cruise speed that scales with the height above the surface, eased in and out.
            var target = input.y * cruisePerSecond * Math.Max(altitudeKm, minAltitudeKm) * (boost.IsPressed() ? boostFactor : 1.0);
            speedKmS += (target - speedKmS) * (1.0 - Math.Exp(-dt / 0.35));
            shipKm += Double3.From(shipRotation * Vector3.forward) * (speedKmS * dt);
            UpdateAltitude();
            if (altitudeKm < minAltitudeKm)
            {
                // Never below the closest approach: back out along the vertical.
                shipKm = shipKm.Normalized * (shipKm.Length + (minAltitudeKm - altitudeKm));
                speedKmS = Math.Min(speedKmS, 0.0);
                UpdateAltitude();
            }
            viewRotation = Quaternion.Slerp(viewRotation, shipRotation, 1f - Mathf.Exp(-dt / 0.2f));

            if (Time.unscaledTime >= nextReanchor)
            {
                nextReanchor = Time.unscaledTime + 0.25f;
                Reanchor();
            }
        }

        private void Travel()
        {
            var s = Mathf.Clamp01((Time.unscaledTime - travelStart) / Mathf.Max(FlightSeconds, 1e-3f));
            TravelProgress = s;
            // Distance on a log scale, eased in and out (smootherstep): the trip seems to speed up and slow down.
            var eased = s * s * s * (s * (6f * s - 15f) + 10f);
            var from = Math.Max(travelFromKm.Length, 1e-9);
            var distance = Math.Exp(Math.Log(from) + (Math.Log(travelArriveKm) - Math.Log(from)) * eased);
            var direction = travelFromKm.Normalized;
            shipKm = direction * Math.Max(distance, travelArriveKm);
            var toward = -direction.ToVector3();
            var up = Mathf.Abs(Vector3.Dot(toward, Vector3.up)) < 0.98f ? Vector3.up : Vector3.forward;
            shipRotation = Quaternion.Slerp(travelStartRotation, Quaternion.LookRotation(toward, up),
                                            Mathf.SmoothStep(0f, 1f, s / 0.2f));
            viewRotation = Quaternion.Slerp(viewRotation, shipRotation, 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.25f));
            bank = Mathf.Lerp(bank, 0f, 0.1f);
            speedKmS = 0.0;
            UpdateAltitude();
            if (s >= 1f)
            {
                SetMode(Mode.Flying);
            }
        }

        private void Vertical()
        {
            var s = Mathf.Clamp01((Time.unscaledTime - verticalStart) / Mathf.Max(verticalSeconds, 1e-3f));
            var eased = Mathf.SmoothStep(0f, 1f, s);
            siteAltitudeKm = Math.Exp(Math.Log(descendFromKm) + (Math.Log(hoverAltitudeKm) - Math.Log(descendFromKm)) * eased);
            PlaceAtSite();
            var hover = shipRotation * Quaternion.Euler(viewPitchDegrees, viewYawDegrees, 0f);
            viewRotation = Quaternion.Slerp(viewRotation, State == Mode.Ascending ? shipRotation : hover,
                                            1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.4f));
            if (s >= 1f)
            {
                if (State == Mode.Descending)
                {
                    SetMode(Mode.Hovering);
                }
                else
                {
                    // Free flight from here, pitched up away from the surface.
                    shipRotation *= Quaternion.AngleAxis(-25f, Vector3.right);
                    HasSite = false;
                    SetMode(Mode.Flying);
                }
            }
        }

        private void Hover(float dt)
        {
            var input = Typing ? Vector2.zero : move.ReadValue<Vector2>();
            var vertical = Typing ? 0f : moveVertical.ReadValue<float>();
            var steer = SteerInput(out _);
            var radius = Sim.Bodies[anchor].MeanRadiusKm;

            // Height: Q and E or the scroll wheel, on a log scale, between the closest approach and half a radius.
            var scroll = PointerOverUi() ? 0f : zoom.ReadValue<float>();
            var climb = vertical * dt * 1.2 + (Mathf.Abs(scroll) > 2f ? scroll * 0.0025 : scroll * 2.0 * dt);
            siteAltitudeKm = Math.Clamp(siteAltitudeKm * Math.Exp(climb), minAltitudeKm, Math.Max(minAltitudeKm, maxHoverFraction * radius));

            // A and D turn, W and S move along the heading; the core gives the coordinates of the new point.
            headingRad += input.x * turnDegreesPerSecond * Mathf.Deg2Rad * dt;
            if (Mathf.Abs(input.y) > 0f)
            {
                var speed = input.y * siteAltitudeKm * 0.8 * (boost.IsPressed() ? 5.0 : 1.0);
                var moved = shipKm + Double3.From(shipRotation * Vector3.forward) * (speed * dt);
                if (TrySiteBelow(moved, out var below))
                {
                    siteLatitudeRad = below.LatitudeRad;
                    siteLongitudeRad = below.LongitudeRad;
                }
            }
            // Dragging looks around the ship.
            viewYawDegrees += steer.x * steerDegreesPerPixel * 2f;
            viewPitchDegrees = Mathf.Clamp(viewPitchDegrees - steer.y * steerDegreesPerPixel * 2f, -45f, 75f);
            PlaceAtSite();
            viewRotation = shipRotation * Quaternion.Euler(viewPitchDegrees, viewYawDegrees, 0f);
            bank = Mathf.Lerp(bank, -input.x * 15f, 1f - Mathf.Exp(-dt / 0.25f));
        }

        // Puts the ship at the site's coordinates and height, level with the horizon there, from the core's site.
        private void PlaceAtSite()
        {
            var request = host.Request;
            if (!Sim.TrySurfaceSite(Sim.Bodies[anchor].Id, siteLatitudeRad, siteLongitudeRad, siteAltitudeKm,
                                    ref request, Sim.Clock.Now, out var site))
            {
                return;
            }
            shipKm = Double3.From(site.PositionKm);
            altitudeKm = siteAltitudeKm;
            var north = Double3.From(site.North);
            var east = Double3.From(site.East);
            var forward = (north * Math.Cos(headingRad) + east * Math.Sin(headingRad)).ToVector3();
            shipRotation = Quaternion.LookRotation(forward, Double3.From(site.Up).ToVector3());
            if (Time.unscaledTime >= nextConditions)
            {
                // The reference surface below (altitude 0): gravity, the Sun and the local time there.
                nextConditions = Time.unscaledTime + 0.2f;
                HasSite = Sim.TrySurfaceSite(Sim.Bodies[anchor].Id, siteLatitudeRad, siteLongitudeRad, 0.0, ref request,
                                             Sim.Clock.Now, out var surface);
                Site = surface;
            }
        }

        // ---- Helpers ----

        private void UpdatePose()
        {
            if (anchor < 0)
            {
                return;
            }
            // One render unit is the ship's height above the surface, so the surface is about one unit away and the
            // ship, a third of a unit ahead, never meets it.
            poseKmPerUnit = Math.Max(altitudeKm, 1.0);
            poseOffsetKm = shipKm + Double3.From(viewRotation * new Vector3(0f, chaseHeight, -chaseDistance)) * poseKmPerUnit;
        }

        private void UpdateAltitude()
        {
            if (anchor < 0)
            {
                return;
            }
            altitudeKm = TrySiteBelow(shipKm, out var below)
                ? below.AltitudeKm
                : shipKm.Length - Sim.Bodies[anchor].MaxRadiusKm;
        }

        private bool TrySiteBelow(Double3 positionKm, out UsimSurfaceSite site)
        {
            site = default;
            if (!CanDescend || positionKm.Length <= 0.0)
            {
                return false;
            }
            var request = host.Request;
            return Sim.TrySurfaceSiteBelow(Sim.Bodies[anchor].Id, positionKm, ref request, Sim.Clock.Now, out site);
        }

        // The position of body `from` relative to body `to`, km, host axes, ECLIPJ2000.
        private bool TryRelative(int from, int to, out Double3 relative)
        {
            relative = default;
            if (!Sim.TryState(Sim.Bodies[from].Id, Sim.Bodies[to].Id, UsimFrame.EclipJ2000, Sim.Clock.Now, out var state))
            {
                return false;
            }
            relative = Double3.From(CoreSimulation.AxesToHost(NativeConstants.AxesUnity, state.PositionKm));
            return true;
        }

        // Switches the anchor to a body the ship is clearly nearer to (by height above its largest radius).
        private void Reanchor()
        {
            var best = shipKm.Length - Sim.Bodies[anchor].MaxRadiusKm;
            var bestIndex = anchor;
            var bestKm = shipKm;
            for (var i = 0; i < Sim.Bodies.Count; ++i)
            {
                if (i == anchor || !Sim.Bodies[i].HasMeanRadius || !host.Views[i].HasPosition ||
                    !TryRelative(anchor, i, out var anchorFromBody))
                {
                    continue;
                }
                var relative = shipKm + anchorFromBody;
                var height = relative.Length - Sim.Bodies[i].MaxRadiusKm;
                if (height < 0.8 * best)
                {
                    best = height;
                    bestIndex = i;
                    bestKm = relative;
                }
            }
            if (bestIndex != anchor)
            {
                anchor = bestIndex;
                shipKm = bestKm;
                UpdateAltitude();
            }
        }

        private int FirstBodyWithRadius()
        {
            candidates.Clear();
            for (var i = 0; i < Sim.Bodies.Count; ++i)
            {
                if (Sim.Bodies[i].HasMeanRadius)
                {
                    if (Sim.Bodies[i].Kind == BodyKindStar)
                    {
                        return i;
                    }
                    candidates.Add(i);
                }
            }
            return candidates.Count > 0 ? candidates[0] : -1;
        }

        // The direction toward the light for the ship, and how much of it reaches the ship outside the anchor's shadow.
        private Vector3 LightDirection(out float sunlit)
        {
            sunlit = 1f;
            var snapshots = host.Snapshots;
            if (anchor >= host.SnapshotCount || (snapshots[anchor].Flags & NativeConstants.SnapshotNoLight) != 0)
            {
                // Near the star itself: light comes from its center.
                return shipKm.Length > 0.0 ? (-shipKm.Normalized).ToVector3() : Vector3.up;
            }
            var light = Double3.From(snapshots[anchor].LightDirection);
            var along = shipKm.Dot(light);
            if (along < 0.0)
            {
                // Behind the body: in its shadow cylinder, with a soft edge (a display choice).
                var radius = Sim.Bodies[anchor].MeanRadiusKm;
                var across = (shipKm - light * along).Length;
                sunlit = Mathf.SmoothStep(0f, 1f, (float)((across - 0.98 * radius) / (0.04 * radius)));
            }
            return light.ToVector3();
        }
    }
}
