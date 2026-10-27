using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Profiling;
using UniverseSim.Unity.Native;
using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>
    /// Owns the core simulation (P5-02): loads the data pack, advances the clock with real time, fills the
    /// snapshot once per frame into a reused buffer and applies it to the body views. Physical state is
    /// never computed here (SPEC FR-REN-08); the camera rig sets <see cref="Request"/> each frame.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class SimulationHost : MonoBehaviour
    {
        /// <summary>Folder under StreamingAssets that holds the data pack.</summary>
        public const string StreamingFolder = "UniverseSim";

        [Tooltip("Pack file name in StreamingAssets/UniverseSim. Empty: the newest moons pack " +
                 "(solar-system-moons-*.uspack) if there is one, else the newest default pack.")]
        [SerializeField] private string packFileName = string.Empty;

        [SerializeField] private Material bodyMaterial;
        [SerializeField] private Material starMaterial;

        [Tooltip("Surface maps (P6-07): Textures/Bodies/BodyTextures.json and the textures it names. Set by the " +
                 "scene builder.")]
        [SerializeField] private TextAsset surfaceMapManifest;
        [SerializeField] private Texture2D[] surfaceMapTextures = Array.Empty<Texture2D>();

        [Tooltip("Start at the current UTC time (UC-01). Otherwise the clock starts at J2000.")]
        [SerializeField] private bool startAtNow = true;

        [Tooltip("Level of detail: how far, in pixels, a body's drawn outline may stray from a circle. Coarser " +
                 "spheres are drawn while they stay within it (SphereLods).")]
        [SerializeField] private float lodTolerancePixels = 0.5f;

        private static readonly ProfilerMarker SnapshotMarker = new ProfilerMarker("UniverseSim.Snapshot");

        private readonly List<BodyView> views = new List<BodyView>();
        private readonly List<SphereLods> spheres = new List<SphereLods>();
        private UsimBodySnapshot[] snapshots = Array.Empty<UsimBodySnapshot>();

        /// <summary>The simulation, or null if the pack could not be loaded (see <see cref="LoadError"/>).</summary>
        public CoreSimulation Simulation { get; private set; }

        /// <summary>Why loading failed, or empty.</summary>
        public string LoadError { get; private set; } = string.Empty;

        /// <summary>The pack file in use.</summary>
        public string PackPath { get; private set; } = string.Empty;

        /// <summary>The view for the next snapshot: center, frame, origin, scale, axes. Set by the camera rig.</summary>
        public UsimSnapshotRequest Request;

        /// <summary>The latest snapshot, in catalog order (valid up to <see cref="SnapshotCount"/>).</summary>
        public UsimBodySnapshot[] Snapshots => snapshots;

        /// <summary>Number of valid entries in <see cref="Snapshots"/>.</summary>
        public int SnapshotCount { get; private set; }

        /// <summary>One view per catalog body, in catalog order.</summary>
        public IReadOnlyList<BodyView> Views => views;

        /// <summary>The surface maps of the bodies that have one (P6-07).</summary>
        public SurfaceMaps SurfaceMaps { get; private set; } = SurfaceMaps.None;

        /// <summary>Raised once the pack is loaded and the views exist.</summary>
        public event Action Loaded;

        private void Awake()
        {
            Request = new UsimSnapshotRequest
            {
                Center = UsimBodyId.Naif(0),
                Frame = UsimFrame.EclipJ2000,
                KmPerRenderUnit = 1.0e6,
                Axes = NativeConstants.AxesUnity,
                ScaleMode = NativeConstants.ScaleModeTrue,
                BodyScale = 1.0,
            };
        }

        // Loading happens in Start, after every component's Awake, so all Loaded subscribers are attached.
        private void Start()
        {
            PackPath = FindPack(packFileName);
            if (string.IsNullOrEmpty(PackPath))
            {
                Fail("No data pack found in StreamingAssets/UniverseSim. Run tools/scripts/copy-pack-to-unity.sh.");
                return;
            }
            try
            {
                Simulation = CoreSimulation.LoadFile(PackPath);
            }
            catch (Exception e) when (e is CoreException || e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                Fail($"Cannot load {PackPath}: {e.Message}");
                return;
            }

            if (startAtNow)
            {
                try
                {
                    Simulation.SetTime(Simulation.FromUtc(DateTime.UtcNow));
                }
                catch (CoreException e)
                {
                    Debug.LogWarning($"UniverseSim: cannot start at the current time: {e.Message}");
                }
            }
            snapshots = new UsimBodySnapshot[Simulation.Bodies.Count];
            SpawnViews();
            Loaded?.Invoke();
        }

        private void Fail(string message)
        {
            LoadError = message;
            Debug.LogError("UniverseSim: " + message);
        }

        private void SpawnViews()
        {
            var plain = new SphereLods(SphereMesh.Create);
            spheres.Add(plain);
            // A mapped sphere's UVs depend on the ratios of the body's radii (SphereMesh.CreateMapped), so bodies
            // with the same ratios (every sphere) share one set of meshes.
            var mapped = new Dictionary<(double, double), SphereLods>();
            SurfaceMaps = SurfaceMaps.Parse(surfaceMapManifest != null ? surfaceMapManifest.text : null, surfaceMapTextures);
            var parent = new GameObject("Bodies").transform;
            parent.SetParent(transform, false);
            foreach (var body in Simulation.Bodies)
            {
                var go = new GameObject(body.Name);
                go.transform.SetParent(parent, false);
                var view = go.AddComponent<BodyView>();
                if (body.Kind != 1 && body.HasMeanRadius && SurfaceMaps.TryGet(body.Id, out var map, out var texture))
                {
                    var a = body.RadiusAKm > 0.0 ? body.RadiusAKm : body.MeanRadiusKm;
                    var b = body.RadiusBKm > 0.0 ? body.RadiusBKm : a;
                    var c = body.RadiusCKm > 0.0 ? body.RadiusCKm : a;
                    if (!mapped.TryGetValue((b / a, c / a), out var sphere))
                    {
                        var axes = Request.Axes;
                        sphere = new SphereLods((segments, rings) => SphereMesh.CreateMapped(segments, rings, a, b, c, axes));
                        mapped.Add((b / a, c / a), sphere);
                        spheres.Add(sphere);
                    }
                    view.Initialize(body, sphere, bodyMaterial, texture, map);
                }
                else
                {
                    view.Initialize(body, plain, body.Kind == 1 ? starMaterial : bodyMaterial);
                }
                views.Add(view);
            }
            // A moon's planet (its usual orbit center in the core), so labels and orbit lines can hide moons
            // that sit on top of their planet on screen.
            foreach (var view in views)
            {
                if (view.Info.Kind == BodyKindMoon && Simulation.TryDefaultOrbitCenter(view.Info.Id, out var center))
                {
                    var index = Simulation.IndexOf(center);
                    view.Planet = index >= 0 && index < views.Count ? views[index] : null;
                }
            }
        }

        private const int BodyKindMoon = 4; // usim::catalog::BodyKind

        private void Update()
        {
            Simulation?.Advance(Time.unscaledDeltaTime);
        }

        private void LateUpdate()
        {
            if (Simulation == null)
            {
                return;
            }
            using var marker = SnapshotMarker.Auto();
            try
            {
                SnapshotCount = Simulation.FillSnapshot(ref Request, snapshots);
            }
            catch (CoreException e)
            {
                SnapshotCount = 0;
                Debug.LogError("UniverseSim: snapshot failed: " + e.Message);
                return;
            }
            // The level of detail needs the camera that draws the view (the rig's, tagged MainCamera).
            var cam = Camera.main;
            var cameraPosition = cam != null ? cam.transform.position : Vector3.zero;
            var focalPixels = cam != null ? 0.5f * cam.pixelHeight / Mathf.Tan(0.5f * cam.fieldOfView * Mathf.Deg2Rad) : 0f;
            for (var i = 0; i < SnapshotCount; ++i)
            {
                views[i].Apply(snapshots[i]);
                if (cam != null)
                {
                    views[i].UpdateLevelOfDetail(cameraPosition, focalPixels, lodTolerancePixels);
                }
            }
        }

        private void OnDestroy()
        {
            foreach (var sphere in spheres)
            {
                sphere.Dispose();
            }
            spheres.Clear();
            Simulation?.Dispose();
            Simulation = null;
        }

        /// <summary>
        /// The pack to load: <paramref name="fileName"/>, or else the newest optional moons pack
        /// (solar-system-moons-*.uspack, P6-01; skipped when <paramref name="preferMoons"/> is false) and
        /// failing that the newest default pack (solar-system-&lt;version&gt;.uspack). Looks in
        /// StreamingAssets/UniverseSim and, in the Editor, the repository's data/packs. Git LFS pointers (small
        /// text files) are skipped. Empty if none is found.
        /// </summary>
        public static string FindPack(string fileName, bool preferMoons = true)
        {
            var folders = new List<string> { Path.Combine(Application.streamingAssetsPath, StreamingFolder) };
#if UNITY_EDITOR
            folders.Add(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "data", "packs")));
#endif
            folders = folders.Where(Directory.Exists).ToList();
            if (!string.IsNullOrEmpty(fileName))
            {
                return folders.Select(f => Path.Combine(f, fileName)).FirstOrDefault(IsPack) ?? string.Empty;
            }
            foreach (var prefix in preferMoons ? new[] { "solar-system-moons-", "solar-system-" } : new[] { "solar-system-" })
            {
                foreach (var folder in folders)
                {
                    var found = Directory.GetFiles(folder, prefix + "*.uspack")
                        .Where(p => char.IsDigit(Path.GetFileName(p)[prefix.Length]))
                        .OrderByDescending(p => p, StringComparer.Ordinal)
                        .FirstOrDefault(IsPack);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }
            return string.Empty;
        }

        private static bool IsPack(string path) => File.Exists(path) && new FileInfo(path).Length > 1024;
    }
}
