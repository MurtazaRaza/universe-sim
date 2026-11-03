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
    /// It flies freely near any body, travels to another in seconds, and goes down over a point of a body's reference
    /// surface: it lands where the core says the surface is solid, stops at its lowest hover height where it is a
    /// pressure level in the atmosphere, and hovers where the data does not say (ADR-0017). Every fact it shows comes
    /// from the core: where bodies are, the distance and light time, a two-body transfer estimate of the real trip,
    /// and at a surface point the horizon, the Sun, the gravity, the length of the day and the surface conditions.
    /// The ship itself is a display object: not in the catalog, never moving the clock or anything the core computes,
    /// drawn at a fixed size on screen, and gone in the realistic style.
    ///
    /// Near a surface the view is lowered onto the drawn sphere, which lies inside the true ellipsoid by up to a few
    /// kilometers between its vertices (<see cref="SphereMesh.DrawnRadiusFraction"/>), so a landed ship rests on the
    /// ground as drawn. The ship's position, and every value shown, stay those of the reference surface.
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
            /// <summary>Resting on solid ground (a body whose reference surface is solid, ADR-0017).</summary>
            Landed,
        }

        [SerializeField] private SimulationHost host;
        [SerializeField] private CameraRig rig;
        [SerializeField] private VisualStyleSettings style;
        [SerializeField] private InputActionAsset actions;
        [SerializeField] private UIDocument hud;
        [SerializeField] private Material shipMaterial;
        [SerializeField] private Material shadowMaterial;
        [SerializeField] private StylizedSky sky;

        // Display choices (docs/ART_DIRECTION.md), not physical values.
        [Header("Look")]
        [Tooltip("Ship length in render units; one unit is about the distance to the nearest surface.")]
        [SerializeField] private float shipSize = 0.075f;
        [SerializeField] private float chaseDistance = 0.32f;
        [SerializeField] private float chaseHeight = 0.07f;
        [Tooltip("In flight the view looks down at the ship by this much more than its heading, so its top shows.")]
        [SerializeField, Range(0f, 20f)] private float chasePitchDegrees = 8f;
        [Tooltip("How much of the sky's daytime color lights the ship under a sky, at its full strength.")]
        [SerializeField, Range(0f, 1f)] private float skyFill = 0.45f;
        [Tooltip("Darkening of the ground right under a landed ship, at its center.")]
        [SerializeField, Range(0f, 1f)] private float contactShade = 0.8f;
        [Tooltip("Height above the ground, in ship lengths, at which the contact shade is gone.")]
        [SerializeField] private float contactShadeLengths = 1.2f;

        [Header("Flight (navigation, not physics)")]
        [Tooltip("Cruise speed in heights per second: the ship covers its own height above the surface below it.")]
        [SerializeField] private double cruisePerSecond = 0.6;
        [SerializeField] private double boostFactor = 8.0;
        [SerializeField] private float turnDegreesPerSecond = 70f;
        [SerializeField] private float steerDegreesPerPixel = 0.15f;
        [Tooltip("Mouse steering (ADR-0019): the pointer's offset from the view's center, in half its shorter side, " +
                 "below which the ship flies straight, the offset of the fastest turn, and that turn's rate.")]
        [SerializeField, Range(0f, 0.5f)] private float mouseDeadZone = 0.12f;
        [SerializeField, Range(0.2f, 1.5f)] private float mouseFullTurn = 0.85f;
        [SerializeField] private float mouseTurnDegreesPerSecond = 60f;
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
        [Tooltip("Near solid ground, the view's km per render unit is at least this, so the camera stays about " +
                 "chaseHeight times it (1 km) above the ground, like the camera rig: below that float rounding shows.")]
        [SerializeField] private double landedKmPerUnit = 15.0;

        private const int BodyKindStar = 1; // usim::catalog::BodyKind

        private InputAction toggleShip, travel, land, move, moveVertical, look, drag, zoom, boost, point;
        private GameObject model;
        private Material material;
        private Mesh mesh;
        private MeshRenderer shadowRenderer;
        private Material shadow;
        private Mesh shadowQuad;
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

        // Landing (ADR-0017): whether the current descent ends on the ground, and whether the ship is near it (landing,
        // landed or lifting off), which sets the view's smallest scale.
        private bool landing, nearGround;
        private double viewFloorKmPerUnit = 1.0;
        // How far the view is lowered onto the drawn sphere, km, eased in and out with the surface modes.
        private double drawnCorrectionKm, correctionWeight;

        // This frame's camera pose.
        private Double3 poseOffsetKm;
        private double poseKmPerUnit = 1.0;

        private static readonly int LightDirectionId = Shader.PropertyToID("_LightDirection");
        private static readonly int SunlitId = Shader.PropertyToID("_Sunlit");
        private static readonly int EngineGlowId = Shader.PropertyToID("_EngineGlow");
        private static readonly int SkyLightId = Shader.PropertyToID("_SkyLight");
        private static readonly int SkyUpId = Shader.PropertyToID("_SkyUp");
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");

        // The contact shade's quad, in ship lengths: wider and longer than the ship's footprint, so the soft edge shows
        // around it from the chase camera.
        private const float ShadeHalfWidth = 0.85f;
        private const float ShadeHalfLength = 0.9f;

        /// <summary>What the ship is doing.</summary>
        public Mode State { get; private set; } = Mode.Off;

        /// <summary>Whether the pointer steers the flying ship without a button held. Tests turn it off.</summary>
        public static bool MouseSteering = true;

        /// <summary>This frame's mouse steering, -1 to 1 per axis (zero when the mouse is not steering), for the HUD.</summary>
        public Vector2 Steering { get; private set; }

        /// <summary>The mouse's dead zone and full-turn offset, in half the view's shorter side (for the HUD's reticle).</summary>
        public Vector2 SteeringZone => new Vector2(mouseDeadZone, mouseFullTurn);

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

        /// <summary>
        /// What the anchor's reference surface is (NativeConstants.Surface*, from the core, ADR-0017): where the ship
        /// lands, stops above a pressure level, or hovers.
        /// </summary>
        public int SurfaceKind => Sim != null && anchor >= 0 ? Sim.SurfaceKind(anchor) : NativeConstants.SurfaceUnknown;

        /// <summary>How far the drawn sphere lies below the reference surface at the site, km (a display correction).</summary>
        public double DrawnSurfaceDepthKm { get; private set; }

        /// <summary>The height above the reference surface where a landed ship rests: its lowest point on the ground, km.</summary>
        public double LandedAltitudeKm => Math.Max(-mesh.bounds.min.y * shipSize * landedKmPerUnit, 0.01);

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

            // The contact shade on the ground under a landed ship: a quad in the ship's ground plane.
            if (shadowMaterial != null)
            {
                shadowQuad = new Mesh { name = "Spaceship contact shade" };
                shadowQuad.SetVertices(new[]
                {
                    new Vector3(-ShadeHalfWidth, 0f, -ShadeHalfLength), new Vector3(-ShadeHalfWidth, 0f, ShadeHalfLength),
                    new Vector3(ShadeHalfWidth, 0f, ShadeHalfLength), new Vector3(ShadeHalfWidth, 0f, -ShadeHalfLength),
                });
                shadowQuad.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) });
                shadowQuad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
                shadowQuad.RecalculateBounds();
                var shade = new GameObject("Spaceship contact shade");
                shade.transform.SetParent(transform, false);
                shade.AddComponent<MeshFilter>().sharedMesh = shadowQuad;
                shadowRenderer = shade.AddComponent<MeshRenderer>();
                shadowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                shadowRenderer.receiveShadows = false;
                shadow = new Material(shadowMaterial) { name = "Spaceship contact shade (runtime)" };
                shadowRenderer.sharedMaterial = shadow;
                shadowRenderer.enabled = false;
            }
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
            if (shadow != null)
            {
                Destroy(shadow);
            }
            if (shadowQuad != null)
            {
                Destroy(shadowQuad);
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
                if (shadowRenderer != null)
                {
                    shadowRenderer.enabled = false;
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
            viewRotation = rig.transform.rotation;
            shipRotation = viewRotation * Quaternion.Euler(-chasePitchDegrees, 0f, 0f);
            speedKmS = 0.0;
            bank = 0f;
            HasSite = false;
            HasDay = false;
            landing = nearGround = false;
            viewFloorKmPerUnit = 1.0;
            correctionWeight = 0.0;
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
            landing = nearGround = false;
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

        /// <summary>
        /// Goes down over the point of the anchor's reference surface below the ship: to land on solid ground, to the
        /// lowest hover height above a pressure level, or to hover where the data does not say what the surface is.
        /// </summary>
        public void Descend()
        {
            if (!(State == Mode.Flying || State == Mode.Ascending) || !CanDescend || !TrySiteBelow(shipKm, out var below))
            {
                return;
            }
            siteLatitudeRad = below.LatitudeRad;
            siteLongitudeRad = below.LongitudeRad;
            var kind = SurfaceKind;
            landing = kind == NativeConstants.SurfaceSolid;
            nearGround = landing;
            hoverAltitudeKm = landing ? LandedAltitudeKm
                : kind == NativeConstants.SurfacePressureLevel ? minAltitudeKm
                : Math.Max(minAltitudeKm, hoverFraction * Sim.Bodies[anchor].MeanRadiusKm);
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

        /// <summary>Climbs from the ground or from hovering back to free flight, about one radius up.</summary>
        public void LiftOff()
        {
            if (!(State == Mode.Hovering || State == Mode.Descending || State == Mode.Landed))
            {
                return;
            }
            landing = false;
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

        private bool Typing => HudKeyboard.Typing(hud);

        // The view in flight: the ship's heading, looking down at it a little more so its top shows.
        private Quaternion ChaseView => shipRotation * Quaternion.Euler(chasePitchDegrees, 0f, 0f);

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
                    if (State == Mode.Hovering || State == Mode.Descending || State == Mode.Landed)
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
            Steering = Vector2.zero; // set by Fly
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
                case Mode.Landed:
                    Rest(dt);
                    break;
            }
            UpdatePose(dt);
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

                // Under a sky (StylizedSky), its daytime hue lights the ship, as it lights the scene around it.
                var skyLight = Vector4.zero;
                var up = shipRotation * Vector3.up;
                if (sky != null && sky.Strength > 0f)
                {
                    var hue = sky.DayColor.linear * (sky.Strength * sky.Daylight * skyFill);
                    skyLight = new Vector4(hue.r, hue.g, hue.b, 0f);
                    up = sky.Up;
                }
                material.SetVector(SkyLightId, skyLight);
                material.SetVector(SkyUpId, up);
            }
            PlaceContactShade(t);
        }

        // The contact shade on the ground under a ship on solid ground (landing, landed or lifting off): straight
        // below its lowest point, in the plane of the local horizon, fading out as the ship rises.
        private void PlaceContactShade(Transform ship)
        {
            if (shadowRenderer == null)
            {
                return;
            }
            var strength = 0f;
            if (nearGround && (State == Mode.Descending || State == Mode.Landed || State == Mode.Ascending))
            {
                var lengthKm = shipSize * poseKmPerUnit;
                var aboveGroundKm = Math.Max(siteAltitudeKm - LandedAltitudeKm, 0.0);
                strength = contactShade * (1f - Mathf.SmoothStep(0f, 1f, (float)(aboveGroundKm / (contactShadeLengths * lengthKm))));
            }
            shadowRenderer.enabled = strength > 1e-3f;
            if (!shadowRenderer.enabled)
            {
                return;
            }
            // The ship's center is the site's height above the ground as drawn (the view is lowered onto the drawn
            // sphere); the shade lies there, level with the horizon (the ship's rotation without its bank).
            var below = ship.position - shipRotation * Vector3.up * (float)(siteAltitudeKm / poseKmPerUnit);
            shadowRenderer.transform.SetPositionAndRotation(below, shipRotation);
            shadowRenderer.transform.localScale = Vector3.one * shipSize;
            shadow.SetFloat(StrengthId, strength);
        }

        // ---- Modes ----

        private Vector2 SteerInput(out bool steering)
        {
            steering = drag.IsPressed() && !PointerOverUi();
            return steering ? look.ReadValue<Vector2>() : Vector2.zero;
        }

        /// <summary>
        /// Mouse steering while flying (ADR-0019 decision 2): the pointer's offset from the center of the view, in
        /// half the view's shorter side, past a dead zone and eased (squared), as -1 to 1 per axis. Zero while the
        /// pointer is over the HUD or outside the window, while dragging (which steers instead), while typing, without
        /// a mouse, and when <see cref="MouseSteering"/> is off.
        /// </summary>
        private Vector2 MouseStick()
        {
            if (!MouseSteering || Typing || Mouse.current == null || !Application.isFocused || drag.IsPressed())
            {
                return Vector2.zero;
            }
            var screen = point.ReadValue<Vector2>();
            if (screen.x <= 1f || screen.y <= 1f || screen.x >= Screen.width - 1f || screen.y >= Screen.height - 1f ||
                PointerOverUi())
            {
                return Vector2.zero;
            }
            var half = 0.5f * new Vector2(Screen.width, Screen.height);
            var offset = (screen - half) / Mathf.Max(Mathf.Min(half.x, half.y), 1f);
            var length = offset.magnitude;
            if (length <= mouseDeadZone)
            {
                return Vector2.zero;
            }
            var s = Mathf.Clamp01((length - mouseDeadZone) / Mathf.Max(mouseFullTurn - mouseDeadZone, 1e-3f));
            return offset / length * (s * s);
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
            var stick = MouseStick();
            Steering = stick;

            // Turn: A and D yaw, Q and E pitch, the mouse or a drag steers; the ship banks into turns.
            var yaw = input.x * turnDegreesPerSecond * dt + steer.x * steerDegreesPerPixel +
                      stick.x * mouseTurnDegreesPerSecond * dt;
            var pitch = -vertical * turnDegreesPerSecond * dt - steer.y * steerDegreesPerPixel -
                        stick.y * mouseTurnDegreesPerSecond * dt;
            shipRotation = shipRotation * Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, Vector3.right);
            // Level the wings slowly to the local vertical (away from the anchor's center).
            var forward = shipRotation * Vector3.forward;
            var up = shipKm.Length > 0.0 ? shipKm.Normalized.ToVector3() : Vector3.up;
            if (Mathf.Abs(Vector3.Dot(forward, up)) < 0.98f)
            {
                shipRotation = Quaternion.Slerp(shipRotation, Quaternion.LookRotation(forward, up), 1f - Mathf.Exp(-dt / 1.5f));
            }
            bank = Mathf.Lerp(bank, -Mathf.Clamp(input.x + steer.x * 0.05f + stick.x, -1f, 1f) * 30f, 1f - Mathf.Exp(-dt / 0.25f));

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
            viewRotation = Quaternion.Slerp(viewRotation, ChaseView, 1f - Mathf.Exp(-dt / 0.2f));

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
            viewRotation = Quaternion.Slerp(viewRotation, ChaseView, 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.25f));
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
            viewRotation = Quaternion.Slerp(viewRotation, State == Mode.Ascending ? ChaseView : hover,
                                            1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.4f));
            if (s >= 1f)
            {
                if (State == Mode.Descending)
                {
                    SetMode(landing ? Mode.Landed : Mode.Hovering);
                }
                else
                {
                    // Free flight from here, pitched up away from the surface.
                    shipRotation *= Quaternion.AngleAxis(-25f, Vector3.right);
                    HasSite = false;
                    nearGround = false;
                    SetMode(Mode.Flying);
                }
            }
        }

        // On the ground: A and D turn the ship in place, dragging looks around; L lifts off.
        private void Rest(float dt)
        {
            var input = Typing ? Vector2.zero : move.ReadValue<Vector2>();
            var steer = SteerInput(out _);
            headingRad += input.x * turnDegreesPerSecond * Mathf.Deg2Rad * dt;
            viewYawDegrees += steer.x * steerDegreesPerPixel * 2f;
            viewPitchDegrees = Mathf.Clamp(viewPitchDegrees - steer.y * steerDegreesPerPixel * 2f, -45f, 75f);
            PlaceAtSite();
            viewRotation = shipRotation * Quaternion.Euler(viewPitchDegrees, viewYawDegrees, 0f);
            bank = Mathf.Lerp(bank, 0f, 1f - Mathf.Exp(-dt / 0.25f));
            speedKmS = 0.0;
        }

        private void Hover(float dt)
        {
            var input = Typing ? Vector2.zero : move.ReadValue<Vector2>();
            var vertical = Typing ? 0f : moveVertical.ReadValue<float>();
            var steer = SteerInput(out _);
            var radius = Sim.Bodies[anchor].MeanRadiusKm;

            // Height: Q and E or the scroll wheel, on a log scale, between the closest approach and half a radius.
            var scroll = PointerOverUi() ? 0f : zoom.ReadValue<float>();
            if (Typing && Mathf.Abs(scroll) <= 2f)
            {
                scroll = 0f; // the minus and equals keys, not the wheel
            }
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
            DrawnSurfaceDepthKm = DrawnDepthKm(site);
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

        // How far below the reference surface at `site` the anchor's sphere is drawn, km: the mesh lies inside the
        // ellipsoid between its vertices (SphereMesh.DrawnRadiusFraction). Display geometry only.
        private double DrawnDepthKm(in UsimSurfaceSite site)
        {
            var view = host.Views[anchor];
            var scale = view.transform.localScale;
            if (view.LodLevel < 0 || scale.x <= 0f || scale.y <= 0f || scale.z <= 0f)
            {
                return 0.0;
            }
            var direction = Quaternion.Inverse(view.transform.rotation) * Double3.From(site.PositionKm).Normalized.ToVector3();
            var local = new Double3(direction.x / scale.x, direction.y / scale.y, direction.z / scale.z);
            var fraction = SphereMesh.DrawnRadiusFraction(local, SphereLods.Segments[view.LodLevel], view.SurfaceMap != null,
                                                          host.Request.Axes);
            return (1.0 - fraction) * site.SurfaceRadiusKm;
        }

        // ---- Helpers ----

        private void UpdatePose(float dt = 0f)
        {
            if (anchor < 0)
            {
                return;
            }
            // One render unit is the ship's height above the surface, so the surface is about one unit away and the
            // ship, a third of a unit ahead, never meets it. Near solid ground the scale stays at landedKmPerUnit or
            // more, so the camera stays about a kilometer up; the change is eased so the view does not jump.
            var ease = dt > 0f ? 1.0 - Math.Exp(-dt / 0.5) : 1.0;
            viewFloorKmPerUnit += ((nearGround ? landedKmPerUnit : 1.0) - viewFloorKmPerUnit) * ease;
            poseKmPerUnit = Math.Max(altitudeKm, viewFloorKmPerUnit);
            // In the surface modes the view is lowered onto the drawn sphere below the site, eased in and out.
            var surfaceMode = State == Mode.Descending || State == Mode.Hovering || State == Mode.Ascending ||
                              State == Mode.Landed;
            correctionWeight += ((surfaceMode ? 1.0 : 0.0) - correctionWeight) * ease;
            if (surfaceMode)
            {
                drawnCorrectionKm = DrawnSurfaceDepthKm;
            }
            var down = shipKm.Length > 0.0 ? shipKm.Normalized * -(drawnCorrectionKm * correctionWeight) : default;
            poseOffsetKm = shipKm + down +
                           Double3.From(viewRotation * new Vector3(0f, chaseHeight, -chaseDistance)) * poseKmPerUnit;
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
