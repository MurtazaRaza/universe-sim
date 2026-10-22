using System.IO;
using UniverseSim.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace UniverseSim.Unity.Editor
{
    /// <summary>
    /// Composes the main scene (Phase 5) from the project's assets: the simulation host, the camera rig,
    /// orbit lines, the star field, the rings and the HUD, with their materials and panel settings. Running it again rebuilds the
    /// same scene, so the scene file never needs hand edits. Menu: UniverseSim > Build Main Scene; batch:
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

            var simulation = new GameObject("UniverseSim");
            var host = simulation.AddComponent<SimulationHost>();
            Set(host, "bodyMaterial", bodyMaterial);
            Set(host, "starMaterial", starMaterial);
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

            var controller = hud.AddComponent<HudController>();
            Set(controller, "host", host);
            Set(controller, "rig", rig);
            Set(controller, "orbitLines", orbits);
            Set(controller, "starField", stars);
            Set(controller, "actions", controls);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("UniverseSim: built " + MainScenePath);
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
