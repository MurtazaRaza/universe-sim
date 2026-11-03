using System.Collections.Generic;
using System.IO;
using UniverseSim.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace UniverseSim.Unity.Editor
{
    /// <summary>
    /// Composes the main scene (Phase 5) from the project's assets: the visual style setting, the simulation
    /// host, the camera rig, orbit lines, the star field, the rings, the style effects with their register (A-03:
    /// body shading, map treatment, camera, Sun, stars, rings, orbit lines, labels, atmospheres, sky), the spaceship
    /// (ADR-0016), the sky cues (ADR-0018), the play components of ADR-0019 (speed cues, the start near the Earth, the
    /// logbook, the sound) and the HUD, with their materials and panel settings. Running it again rebuilds the same scene, so the scene file never
    /// needs hand edits. Menu: UniverseSim > Build Main Scene; batch:
    /// -executeMethod UniverseSim.Unity.Editor.MainSceneBuilder.Build
    /// </summary>
    public static class MainSceneBuilder
    {
        private const string Root = "Assets/UniverseSim";
        public const string MainScenePath = Root + "/Scenes/Main.unity";

        [MenuItem("UniverseSim/Build Main Scene")]
        public static void Build()
        {
            // Open the scene first: opening it unloads assets nothing references yet, which would leave the
            // references below pointing at unloaded objects.
            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            var bodyMaterial = EnsureMaterial(Root + "/Materials/BodyLit.mat", "UniverseSim/BodyLit");
            var starMaterial = EnsureMaterial(Root + "/Materials/Star.mat", "UniverseSim/Star");
            var lineMaterial = EnsureMaterial(Root + "/Materials/Line.mat", "UniverseSim/Line");
            var starFieldMaterial = EnsureMaterial(Root + "/Materials/StarField.mat", "UniverseSim/StarField");
            var ringMaterial = EnsureMaterial(Root + "/Materials/Rings.mat", "UniverseSim/Rings");
            var shipMaterial = EnsureMaterial(Root + "/Materials/Ship.mat", "UniverseSim/Ship");
            var shipShadowMaterial = EnsureMaterial(Root + "/Materials/ShipShadow.mat", "UniverseSim/ShipShadow");
            var sunGlowMaterial = EnsureMaterial(Root + "/Materials/SunGlow.mat", "UniverseSim/SunGlow");
            var haloMaterial = EnsureMaterial(Root + "/Materials/AtmosphereHalo.mat", "UniverseSim/AtmosphereHalo");
            var skyMaterial = EnsureMaterial(Root + "/Materials/Sky.mat", "UniverseSim/Sky");
            var streakMaterial = EnsureMaterial(Root + "/Materials/SpeedStreaks.mat", "UniverseSim/SpeedStreaks");
            var panelSettings = EnsurePanelSettings(Root + "/UI/UniverseSimPanelSettings.asset");
            var hudLayout = Load<VisualTreeAsset>(Root + "/UI/UniverseSimHud.uxml");
            var controls = Load<InputActionAsset>(Root + "/Input/UniverseSimControls.inputactions");

            foreach (var root in scene.GetRootGameObjects())
            {
                // Lighting comes from the core's light direction per body, not from a scene light; the
                // objects this builder made last time are replaced.
                if (root.GetComponent<Light>() != null || root.name == "UniverseSim" || root.name == "HUD")
                {
                    Object.DestroyImmediate(root);
                }
            }

            var camera = Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            }
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.fieldOfView = 50f;
            camera.allowHDR = true;
            camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            // The listener for the synthesized sound (ADR-0019).
            if (camera.GetComponent<AudioListener>() == null)
            {
                camera.gameObject.AddComponent<AudioListener>();
            }
            MakeVolumeRealistic();

            var simulation = new GameObject("UniverseSim");
            var style = simulation.AddComponent<VisualStyleSettings>();
            // The effects tier (A-13): the style effects that cost the most read it.
            var quality = simulation.AddComponent<EffectsQuality>();
            var host = simulation.AddComponent<SimulationHost>();
            Set(host, "bodyMaterial", bodyMaterial);
            Set(host, "starMaterial", starMaterial);
            var (manifest, maps) = LoadSurfaceMaps();
            Set(host, "surfaceMapManifest", manifest);
            SetArray(host, "surfaceMapTextures", maps);
            // The sourced sky cues the pipeline wrote (ADR-0018): Data/SkyCues.json.
            Set(host, "skyCueManifest", Load<TextAsset>(Root + "/Data/SkyCues.json"));
            var orbits = simulation.AddComponent<OrbitLines>();
            Set(orbits, "host", host);
            Set(orbits, "lineMaterial", lineMaterial);
            var stars = new GameObject("Star field").AddComponent<StarField>();
            stars.transform.SetParent(simulation.transform, false);
            Set(stars, "host", host);
            Set(stars, "starFieldMaterial", starFieldMaterial);
            var rings = simulation.AddComponent<RingSystems>();
            Set(rings, "host", host);
            Set(rings, "ringMaterial", ringMaterial);

            // The style effects (Track A), each listed in the register that the "what is exaggerated" panel reads.
            var bodyShading = simulation.AddComponent<StylizedBodyShading>();
            Set(bodyShading, "host", host);
            Set(bodyShading, "style", style);
            var mapTreatment = simulation.AddComponent<SurfaceMapTreatment>();
            Set(mapTreatment, "host", host);
            Set(mapTreatment, "style", style);
            var stylizedCamera = simulation.AddComponent<StylizedCamera>();
            Set(stylizedCamera, "style", style);
            Set(stylizedCamera, "quality", quality);
            Set(mapTreatment, "quality", quality);
            var stylizedSun = simulation.AddComponent<StylizedSun>();
            Set(stylizedSun, "style", style);
            Set(stylizedSun, "host", host);
            Set(stylizedSun, "glowMaterial", sunGlowMaterial);
            var stylizedStars = simulation.AddComponent<StylizedStars>();
            Set(stylizedStars, "style", style);
            Set(stylizedStars, "starField", stars);
            var stylizedRings = simulation.AddComponent<StylizedRings>();
            Set(stylizedRings, "style", style);
            Set(stylizedRings, "rings", rings);
            var stylizedOrbits = simulation.AddComponent<StylizedOrbitLines>();
            Set(stylizedOrbits, "style", style);
            Set(stylizedOrbits, "orbitLines", orbits);
            var stylizedAtmospheres = simulation.AddComponent<StylizedAtmospheres>();
            Set(stylizedAtmospheres, "style", style);
            Set(stylizedAtmospheres, "host", host);
            Set(stylizedAtmospheres, "haloMaterial", haloMaterial);
            var stylizedSky = simulation.AddComponent<StylizedSky>();
            Set(stylizedSky, "style", style);
            Set(stylizedSky, "host", host);
            Set(stylizedSky, "atmospheres", stylizedAtmospheres);
            Set(stylizedSky, "skyMaterial", skyMaterial);

            var hud = new GameObject("HUD");
            var document = hud.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = hudLayout;

            var rig = camera.GetComponent<CameraRig>();
            if (rig == null)
            {
                rig = camera.gameObject.AddComponent<CameraRig>();
            }
            Set(rig, "host", host);
            Set(rig, "actions", controls);
            Set(rig, "hud", document);

            // The spaceship of the stylized style (ADR-0016): a style effect like the others, listed in the register.
            var ship = simulation.AddComponent<ShipPilot>();
            Set(ship, "host", host);
            Set(ship, "rig", rig);
            Set(ship, "style", style);
            Set(ship, "actions", controls);
            Set(ship, "hud", document);
            Set(ship, "shipMaterial", shipMaterial);
            Set(ship, "shadowMaterial", shipShadowMaterial);
            Set(ship, "sky", stylizedSky);

            var stylizedLabels = simulation.AddComponent<StylizedLabels>();
            Set(stylizedLabels, "style", style);
            Set(stylizedLabels, "hud", document);

            // Play (ADR-0019): the speed cues (a style effect), the start near the Earth, the logbook and the sound.
            var speedCues = simulation.AddComponent<SpeedCues>();
            Set(speedCues, "style", style);
            Set(speedCues, "ship", ship);
            Set(speedCues, "streakMaterial", streakMaterial);
            var start = simulation.AddComponent<StartExperience>();
            Set(start, "host", host);
            Set(start, "rig", rig);
            Set(start, "ship", ship);
            var logbook = simulation.AddComponent<Logbook>();
            Set(logbook, "host", host);
            Set(logbook, "rig", rig);
            Set(logbook, "ship", ship);
            var sound = new GameObject("Sound").AddComponent<SoundScape>();
            sound.transform.SetParent(simulation.transform, false);
            Set(sound, "ship", ship);
            Set(sound, "rig", rig);
            Set(sound, "logbook", logbook);
            Set(sound, "hud", document);
            Set(sound, "actions", controls);

            foreach (var effect in simulation.GetComponents<StyleEffect>())
            {
                Set(effect, "quality", quality);
            }
            var register = simulation.AddComponent<ExaggerationRegister>();
            SetArray(register, "effects", new Object[]
            {
                bodyShading, mapTreatment, stylizedCamera, stylizedSun, stylizedStars, stylizedRings, stylizedOrbits,
                stylizedLabels, ship, stylizedAtmospheres, stylizedSky, speedCues,
            });

            var controller = hud.AddComponent<HudController>();
            Set(controller, "host", host);
            Set(controller, "rig", rig);
            Set(controller, "orbitLines", orbits);
            Set(controller, "starField", stars);
            Set(controller, "style", style);
            Set(controller, "exaggerationRegister", register);
            Set(controller, "quality", quality);
            Set(controller, "actions", controls);
            var shipHud = hud.AddComponent<ShipHud>();
            Set(shipHud, "host", host);
            Set(shipHud, "rig", rig);
            Set(shipHud, "pilot", ship);
            var logbookHud = hud.AddComponent<LogbookHud>();
            Set(logbookHud, "host", host);
            Set(logbookHud, "rig", rig);
            Set(logbookHud, "ship", ship);
            Set(logbookHud, "logbook", logbook);
            Set(logbookHud, "start", start);
            Set(logbookHud, "actions", controls);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("UniverseSim: built " + MainScenePath);
        }

        // The scene's volume is the realistic camera (A-08): the URP template's bloom and vignette are turned off and
        // its Neutral tonemapping kept. The stylized camera adds its own volume at runtime (StylizedCamera).
        private static void MakeVolumeRealistic()
        {
            foreach (var volume in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include))
            {
                var profile = volume.sharedProfile;
                if (profile == null)
                {
                    continue;
                }
                if (profile.TryGet<Bloom>(out var bloom))
                {
                    bloom.active = false;
                }
                if (profile.TryGet<Vignette>(out var vignette))
                {
                    vignette.active = false;
                }
                EditorUtility.SetDirty(profile);
            }
        }

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new FileNotFoundException("Missing asset " + path);
            }
            return asset;
        }

        private static Material EnsureMaterial(string path, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new FileNotFoundException("Missing shader " + shaderName);
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static PanelSettings EnsurePanelSettings(string path)
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, path);
            }
            settings.themeStyleSheet = Load<ThemeStyleSheet>(Root + "/UI/UniverseSimTheme.tss");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            EditorUtility.SetDirty(settings);
            return settings;
        }

        // The surface maps the pipeline wrote (P6-07): Textures/Bodies/BodyTextures.json and the textures it names.
        // None before `usim-pipeline textures` has run; a named texture that is missing is an error.
        private static (TextAsset Manifest, Texture2D[] Textures) LoadSurfaceMaps()
        {
            var manifest = AssetDatabase.LoadAssetAtPath<TextAsset>(SurfaceMapFolder + "/BodyTextures.json");
            if (manifest == null)
            {
                return (null, new Texture2D[0]);
            }
            var textures = new List<Texture2D>();
            foreach (var map in SurfaceMaps.ReadManifest(manifest.text))
            {
                var path = SurfaceMapFolder + "/" + map.file;
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null)
                {
                    throw new System.IO.FileNotFoundException("The surface map manifest names a texture that is missing", path);
                }
                textures.Add(texture);
            }
            return (manifest, textures.ToArray());
        }

        private const string SurfaceMapFolder = Root + "/Textures/Bodies";

        // Sets a serialized (private) array field of object references.
        private static void SetArray(Object target, string field, Object[] values)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null || !property.isArray)
            {
                throw new System.MissingFieldException(target.GetType().Name, field);
            }
            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; ++i)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // Sets a serialized (private) field, as the Inspector would.
        private static void Set(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                throw new System.MissingFieldException(target.GetType().Name, field);
            }
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (value != null && serialized.FindProperty(field).objectReferenceValue != value)
            {
                throw new System.InvalidOperationException($"{target.GetType().Name}.{field} did not keep its reference");
            }
        }
    }
}
