using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The viewer's sound (ADR-0019 decision 6, SPEC FR-REN-18): ambient music, the spaceship's engine following its
    /// pace, and interface sounds for clicks, selections, logbook goals, departures and arrivals. All of it is
    /// synthesized here at run time (<see cref="AmbientMusic"/>, <see cref="EngineSound"/>, <see cref="UiSounds"/>), so
    /// there are no audio files to license. It reads the ship, the rig and the logbook and changes nothing. The Sound
    /// (M) and Music toggles are remembered in PlayerPrefs.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(AudioSource))]
    public sealed class SoundScape : MonoBehaviour
    {
        /// <summary>PlayerPrefs keys of the toggles.</summary>
        public const string MutedKey = "usim.sound.muted";
        public const string MusicKey = "usim.sound.music";

        /// <summary>Keeps every sound off for the next load whatever the toggles say. Tests set it.</summary>
        public static bool Silent;

        [SerializeField] private ShipPilot ship;
        [SerializeField] private CameraRig rig;
        [SerializeField] private Logbook logbook;
        [SerializeField] private UIDocument hud;
        [SerializeField] private InputActionAsset actions;

        // Mix levels: display choices.
        [SerializeField, Range(0f, 1f)] private float master = 0.8f;
        [SerializeField, Range(0f, 1f)] private float music = 0.5f;
        [SerializeField, Range(0f, 1f)] private float engine = 0.7f;
        [SerializeField, Range(0f, 1f)] private float interfaceSounds = 0.7f;

        private AmbientMusic musicSynth;
        private EngineSound engineSynth;
        private UiSounds uiSynth;
        private readonly ShipPace pace = new ShipPace();
        private AudioSource source;
        private InputAction toggleMute, boost;
        private Toggle soundToggle, musicToggle;
        private ShipPilot.Mode lastMode = ShipPilot.Mode.Off;

        // Set on the main thread, read on the audio thread.
        private volatile float masterTarget, musicTarget, engineTarget, uiTarget;
        private float masterNow, musicNow;

        /// <summary>True when every sound is off (M).</summary>
        public bool Muted { get; private set; }

        /// <summary>True when the music plays (with the sound on).</summary>
        public bool MusicOn { get; private set; } = true;

        /// <summary>The engine's targets this frame (for tests).</summary>
        public EngineSound Engine => engineSynth;

        /// <summary>The interface sounds (for tests).</summary>
        public UiSounds Interface => uiSynth;

        private void Awake()
        {
            var rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            musicSynth = new AmbientMusic(rate);
            engineSynth = new EngineSound(rate);
            uiSynth = new UiSounds(rate);
            var map = actions.FindActionMap("Viewer", true);
            toggleMute = map.FindAction("ToggleMute", true);
            boost = map.FindAction("Boost", true);
            Muted = PlayerPrefs.GetInt(MutedKey, 0) != 0;
            MusicOn = PlayerPrefs.GetInt(MusicKey, 1) != 0;
            ApplyGains();

            // The filter below writes the sound; the source plays a silent loop so that it runs.
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.clip = AudioClip.Create("Silence", rate, 2, rate, false);
            source.Play();
        }

        private void OnEnable()
        {
            var root = hud != null ? hud.rootVisualElement : null;
            if (root != null)
            {
                soundToggle = root.Q<Toggle>("toggle-sound");
                musicToggle = root.Q<Toggle>("toggle-music");
                soundToggle?.RegisterValueChangedCallback(OnSoundToggle);
                musicToggle?.RegisterValueChangedCallback(OnMusicToggle);
                root.RegisterCallback<ClickEvent>(OnHudClick, TrickleDown.TrickleDown);
                RefreshToggles();
            }
            if (rig != null)
            {
                rig.SelectionChanged += OnSelection;
            }
            if (logbook != null)
            {
                logbook.Completed += OnGoal;
            }
            if (ship != null)
            {
                ship.ModeChanged += OnShipMode;
            }
        }

        private void OnDisable()
        {
            soundToggle?.UnregisterValueChangedCallback(OnSoundToggle);
            musicToggle?.UnregisterValueChangedCallback(OnMusicToggle);
            hud?.rootVisualElement?.UnregisterCallback<ClickEvent>(OnHudClick, TrickleDown.TrickleDown);
            if (rig != null)
            {
                rig.SelectionChanged -= OnSelection;
            }
            if (logbook != null)
            {
                logbook.Completed -= OnGoal;
            }
            if (ship != null)
            {
                ship.ModeChanged -= OnShipMode;
            }
        }

        private void OnDestroy()
        {
            if (source != null && source.clip != null)
            {
                Destroy(source.clip);
            }
        }

        /// <summary>Turns every sound off or on, and remembers it.</summary>
        public void SetMuted(bool muted)
        {
            Muted = muted;
            PlayerPrefs.SetInt(MutedKey, muted ? 1 : 0);
            ApplyGains();
            RefreshToggles();
        }

        /// <summary>Turns the music off or on, and remembers it.</summary>
        public void SetMusic(bool on)
        {
            MusicOn = on;
            PlayerPrefs.SetInt(MusicKey, on ? 1 : 0);
            ApplyGains();
            RefreshToggles();
        }

        private void OnSoundToggle(ChangeEvent<bool> e) => SetMuted(!e.newValue);

        private void OnMusicToggle(ChangeEvent<bool> e) => SetMusic(e.newValue);

        private void RefreshToggles()
        {
            soundToggle?.SetValueWithoutNotify(!Muted);
            musicToggle?.SetValueWithoutNotify(MusicOn);
            musicToggle?.SetEnabled(!Muted);
        }

        private void ApplyGains()
        {
            masterTarget = Muted || Silent ? 0f : master;
            musicTarget = MusicOn ? music : 0f;
            engineTarget = engine;
            uiTarget = interfaceSounds;
        }

        private void Update()
        {
            if (!HudKeyboard.Typing(hud) && toggleMute.WasPressedThisFrame())
            {
                SetMuted(!Muted);
            }
            pace.Update(ship, Time.unscaledDeltaTime);
            var active = ship != null && ship.Active;
            var flying = Mathf.Clamp01((float)pace.HeightsPerSecond / 5f);
            var push = Mathf.Max(flying, pace.Trip);
            engineSynth.Level = !active ? 0f : ship.State == ShipPilot.Mode.Landed ? 0.12f : 0.3f + 0.7f * push;
            engineSynth.Pitch = active ? push : 0f;
            engineSynth.Boost = active && ship.State == ShipPilot.Mode.Flying && boost.IsPressed() ? 1f : 0f;
            engineSynth.Warp = pace.Trip;
        }

        // A click on a button or a toggle of the HUD.
        private void OnHudClick(ClickEvent e)
        {
            for (var element = e.target as VisualElement; element != null; element = element.parent)
            {
                if (element is Button || element is Toggle)
                {
                    uiSynth.Trigger(SoundCue.Click);
                    return;
                }
            }
        }

        private void OnSelection(int index)
        {
            if (index >= 0)
            {
                uiSynth.Trigger(SoundCue.Select);
            }
        }

        private void OnGoal(Logbook.Goal goal) => uiSynth.Trigger(SoundCue.Goal);

        private void OnShipMode(ShipPilot.Mode mode)
        {
            if (mode == ShipPilot.Mode.Traveling)
            {
                uiSynth.Trigger(SoundCue.Depart);
            }
            else if (lastMode == ShipPilot.Mode.Traveling && mode == ShipPilot.Mode.Flying)
            {
                uiSynth.Trigger(SoundCue.Arrive);
            }
            lastMode = mode;
        }

        // The audio thread: the sounds replace the silent loop's samples. Gains ease per buffer, so toggling never clicks.
        private void OnAudioFilterRead(float[] data, int channels)
        {
            for (var i = 0; i < data.Length; ++i)
            {
                data[i] = 0f;
            }
            var masterFrom = masterNow;
            masterNow += (masterTarget - masterNow) * 0.35f;
            musicNow += (musicTarget - musicNow) * 0.35f;
            if (masterFrom < 1e-4f && masterNow < 1e-4f)
            {
                return;
            }
            if (musicNow > 1e-4f)
            {
                musicSynth.Render(data, channels, musicNow);
            }
            engineSynth.Render(data, channels, engineTarget);
            uiSynth.Render(data, channels, uiTarget);
            var frames = data.Length / Mathf.Max(channels, 1);
            for (var f = 0; f < frames; ++f)
            {
                var g = masterFrom + (masterNow - masterFrom) * f / Mathf.Max(frames, 1);
                for (var c = 0; c < channels; ++c)
                {
                    var i = f * channels + c;
                    data[i] = Wave.SoftClip(data[i] * g);
                }
            }
        }
    }
}
