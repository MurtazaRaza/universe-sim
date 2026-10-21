using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UniverseSim.Unity.Tests.EditMode
{
    public class ProjectSetupTests
    {
        private const string MainScenePath = "Assets/UniverseSim/Scenes/Main.unity";

        [Test]
        public void MainSceneIsTheFirstEnabledBuildScene()
        {
            var first = EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled);

            Assert.That(first, Is.Not.Null, "No enabled scene in the build settings.");
            Assert.That(first.path, Is.EqualTo(MainScenePath));
        }

        [Test]
        public void MainSceneHasNoMissingScripts()
        {
            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Additive);
            try
            {
                var missing = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                    .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));

                Assert.That(missing, Is.Zero);
            }
            finally
            {
                if (SceneManager.sceneCount > 1)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
    }
}
