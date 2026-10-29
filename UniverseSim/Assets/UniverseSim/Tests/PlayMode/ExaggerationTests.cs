using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UniverseSim.Unity.Native;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UniverseSim.Unity.Tests.PlayMode
{
    /// <summary>
    /// The exaggeration register and the "what is exaggerated" panel (A-03, SPEC FR-REN-12 and FR-REN-13): every
    /// effect component is registered, the panel lists the active effects in the stylized style and nothing in the
    /// realistic one, and with every registered effect off the stylized style draws the realistic view, so no
    /// stylization can show without being listed.
    /// </summary>
    public class ExaggerationTests
    {
        private const string FixedTime = "2026-10-21T12:00:00 UTC";

        private StylePreference preference;
        private SimulationHost host;
        private CameraRig rig;
        private VisualStyleSettings settings;
        private ExaggerationRegister register;
        private VisualElement hud;

        [OneTimeSetUp]
        public void KeepPreference() => preference = StylePreference.Keep();

        [OneTimeTearDown]
        public void RestorePreference() => preference.Restore();

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            VisualStyleSettings.SavePreference(VisualStyle.Stylized);
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            host = Object.FindAnyObjectByType<SimulationHost>();
            rig = Object.FindAnyObjectByType<CameraRig>();
            settings = Object.FindAnyObjectByType<VisualStyleSettings>();
            register = Object.FindAnyObjectByType<ExaggerationRegister>();
            hud = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
            Assert.That(register, Is.Not.Null);
            Assert.That(host.Simulation, Is.Not.Null, host.LoadError);
            host.Simulation.SetPaused(true);
            Assert.That(host.Simulation.TryParseTime(FixedTime, out var t, out var error), error);
            host.Simulation.SetTime(t);
            var earth = host.Simulation.IndexOf(UsimBodyId.Naif(399));
            rig.JumpTo(earth, host.Simulation.Bodies[earth].MaxRadiusKm * 4.0, 0.6, 0.3);
            yield return null;
        }

        private static readonly Assembly Runtime = typeof(SimulationHost).Assembly;

        [UnityTest]
        public IEnumerator EveryEffectComponentIsRegistered()
        {
            yield return null;
            var inScene = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include)
                .OfType<IStyleEffect>().ToList();
            foreach (var effect in inScene)
            {
                Assert.That(register.Contains(effect), $"{effect.GetType().Name} is not in the exaggeration register");
                Assert.That(effect.Exaggerations.Count, Is.GreaterThan(0), $"{effect.GetType().Name} declares nothing");
            }
            // Every effect type is in the scene, and every component that reads the style is an effect (the HUD only
            // shows the style).
            var components = Runtime.GetTypes().Where(t => typeof(MonoBehaviour).IsAssignableFrom(t) && !t.IsAbstract);
            foreach (var type in components)
            {
                if (typeof(IStyleEffect).IsAssignableFrom(type))
                {
                    Assert.That(inScene.Any(e => e.GetType() == type), $"{type.Name} is not in the main scene");
                }
                var readsStyle = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Any(f => f.FieldType == typeof(VisualStyleSettings));
                if (readsStyle && type != typeof(HudController))
                {
                    Assert.That(typeof(IStyleEffect).IsAssignableFrom(type),
                                $"{type.Name} reads the style but is no IStyleEffect");
                }
            }
        }

        private List<Exaggeration> Active()
        {
            var list = new List<Exaggeration>();
            register.CollectActive(Camera.main, list);
            return list;
        }

        [UnityTest]
        public IEnumerator ThePanelListsTheActiveEffectsAndNothingInTheRealisticStyle()
        {
            yield return new WaitForSecondsRealtime(0.3f); // the HUD refreshes ten times a second
            var stylized = Active();
            // The Earth up close: the shading, its map and the camera.
            Assert.That(stylized.Select(e => e.Subject),
                        Does.Contain("Night sides").And.Contain("Surface maps").And.Contain("Glow"));
            var toggle = hud.Q<Button>("exaggerations-toggle");
            var list = hud.Q("exaggerations-list");
            Assert.That(toggle.text, Is.EqualTo($"What is exaggerated? ({stylized.Count})"));
            Assert.That(list.Query<Label>(className: "exaggeration").ToList().Count, Is.EqualTo(stylized.Count));
            Assert.That(list.resolvedStyle.display, Is.EqualTo(DisplayStyle.None)); // collapsed until clicked
            using (var click = NavigationSubmitEvent.GetPooled())
            {
                click.target = toggle;
                toggle.SendEvent(click);
            }
            yield return null;
            Assert.That(list.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));

            settings.SetStyle(VisualStyle.Realistic);
            yield return null;
            Assert.That(Active(), Is.Empty);
            Assert.That(list.childCount, Is.Zero);
            Assert.That(toggle.text, Is.EqualTo("Nothing is exaggerated (realistic style)"));
        }

        [UnityTest]
        public IEnumerator WithEveryEffectOffTheStylizedStyleDrawsTheRealisticView()
        {
            var realistic = new Texture2D[1];
            settings.SetStyle(VisualStyle.Realistic);
            yield return Capture(realistic);
            settings.SetStyle(VisualStyle.Stylized);
            foreach (var effect in register.Effects)
            {
                effect.enabled = false;
            }
            var bare = new Texture2D[1];
            yield return Capture(bare);
            Assert.That(Active(), Is.Empty);
            var a = realistic[0].GetPixels32();
            var b = bare[0].GetPixels32();
            var largest = 0;
            var over = 0;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            long total = 0;
            for (var i = 0; i < a.Length; ++i)
            {
                var d = Math.Max(Math.Abs(a[i].r - b[i].r),
                                 Math.Max(Math.Abs(a[i].g - b[i].g), Math.Abs(a[i].b - b[i].b)));
                largest = Math.Max(largest, d);
                total += d;
                if (d > 4)
                {
                    ++over;
                    var x = i % realistic[0].width;
                    var y = i / realistic[0].width;
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }
            var summary = $"largest difference {largest} levels, mean {(double)total / a.Length:0.0000}, " +
                          $"{over} pixels " +
                          $"over 4 levels in x {minX} to {maxX}, y {minY} to {maxY}";
            TestContext.WriteLine(summary);
            Debug.Log("UniverseSim effects off against realistic: " + summary);
            // Measured 2026-10-29: at most 2 levels apart (mean 0.0007), from rounding; the bound leaves room for it.
            Assert.That(largest, Is.LessThanOrEqualTo(4), summary);
            Assert.That((double)total / a.Length, Is.LessThan(0.05));
            Object.Destroy(realistic[0]);
            Object.Destroy(bare[0]);
        }

        private static IEnumerator Capture(Texture2D[] result)
        {
            var cam = Camera.main;
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = target;
            yield return null;
            yield return null;
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            result[0] = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            result[0].ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            result[0].Apply();
            RenderTexture.active = previous;
            cam.targetTexture = null;
            target.Release();
            Object.Destroy(target);
        }
    }
}
