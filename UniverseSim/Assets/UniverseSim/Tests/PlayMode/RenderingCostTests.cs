using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UniverseSim.Unity.Native;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UniverseSim.Unity.Tests.PlayMode
{
    /// <summary>
    /// What the main scene costs to draw: bodies use the coarsest sphere their size on screen allows, the
    /// orbit lines are one mesh, and a table of Unity's render counters (triangles, SetPass calls) and the CPU
    /// time of the main thread and the per-frame scripts at typical views, logged for comparisons. It renders
    /// into an offscreen 1920 x 1080 target, since batch mode has no screen. Timings depend on the machine and
    /// are only logged.
    /// </summary>
    public class RenderingCostTests
    {
        private const string FixedTime = "2026-10-21T12:00:00 UTC";
        private const int Frames = 120;

        private SimulationHost host;
        private CameraRig rig;

        private CoreSimulation Sim => host.Simulation;

        private static readonly (ProfilerCategory Category, string Name, double Scale, string Unit)[] Counters =
        {
            (ProfilerCategory.Render, "Triangles Count", 1.0, ""),
            (ProfilerCategory.Render, "Vertices Count", 1.0, ""),
            (ProfilerCategory.Render, "SetPass Calls Count", 1.0, ""),
            (ProfilerCategory.Internal, "Main Thread", 1e-6, " ms"),
            (ProfilerCategory.Scripts, "UniverseSim.Snapshot", 1e-6, " ms"),
            (ProfilerCategory.Scripts, "UniverseSim.OrbitLines", 1e-6, " ms"),
            (ProfilerCategory.Scripts, "UniverseSim.Labels", 1e-6, " ms"),
        };

        private StylePreference preference;

        [OneTimeSetUp]
        public void KeepPreference() => preference = StylePreference.Keep();

        [OneTimeTearDown]
        public void RestorePreference() => preference.Restore();

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            // Measured in the default style (A-02); A-13 extends the measurements to both.
            VisualStyleSettings.SavePreference(VisualStyle.Stylized);
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            host = Object.FindAnyObjectByType<SimulationHost>();
            rig = Object.FindAnyObjectByType<CameraRig>();
            Assert.That(host, Is.Not.Null);
            Assert.That(host.Simulation, Is.Not.Null, host.LoadError);
            Sim.SetPaused(true);
            Assert.That(Sim.TryParseTime(FixedTime, out var t, out var error), error);
            Sim.SetTime(t);
            yield return null;
        }

        private int Index(long naif) => Sim.IndexOf(UsimBodyId.Naif(naif));

        // The host's default level of detail tolerance, pixels (SimulationHost.lodTolerancePixels).
        private const float LodTolerancePixels = 0.5f;

        [UnityTest]
        public IEnumerator BodiesDrawTheLevelTheirSizeOnScreenNeeds()
        {
            var cam = Camera.main;
            var earth = Index(399);
            var earthRadius = Sim.Bodies[earth].MaxRadiusKm;
            var views = new (string Name, System.Action Place)[]
            {
                ("system (start view)", () => { }),
                ("Jupiter system", () => rig.JumpTo(Index(599), 4.0e6, 0.5, 0.35)),
                ("Earth, 4 radii", () => rig.JumpTo(earth, earthRadius * 4.0, 0.6, 0.3)),
                ("Earth, 1 km", () => rig.JumpTo(earth, earthRadius + 1.0, 0.6, 0.0)),
            };
            foreach (var (name, place) in views)
            {
                place();
                yield return null;
                yield return null;
                var focalPixels = 0.5f * cam.pixelHeight / Mathf.Tan(0.5f * cam.fieldOfView * Mathf.Deg2Rad);
                foreach (var view in host.Views.Where(v => v.LodLevel >= 0 && v.HasPosition))
                {
                    var distance = Vector3.Distance(view.transform.position, cam.transform.position);
                    var pixelRadius = SphereLods.PixelRadius(view.MaxRenderRadius, distance, focalPixels);
                    if (view.LodLevel > 0)
                    {
                        // The outline of the level drawn strays from the circle by at most the tolerance.
                        var segments = SphereLods.Segments[view.LodLevel];
                        Assert.That(pixelRadius * (1f - Mathf.Cos(Mathf.PI / segments)), Is.LessThanOrEqualTo(LodTolerancePixels * 1.001f),
                                    $"{name}: {view.Info.Name}, {pixelRadius} px at {segments} segments");
                    }
                    if (name.StartsWith("system"))
                    {
                        Assert.That(view.LodLevel, Is.EqualTo(SphereLods.Segments.Length - 1), $"{name}: {view.Info.Name}");
                    }
                }
            }
            // Up close, the Earth gets the finest sphere.
            Assert.That(host.Views[earth].LodLevel, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator OrbitLinesAreOneMesh()
        {
            yield return null;
            yield return null;
            var lines = Object.FindAnyObjectByType<OrbitLines>();
            Assert.That(lines, Is.Not.Null);
            Assert.That(lines.DrawnCount, Is.GreaterThanOrEqualTo(8)); // at least the planets from the start view
            // Every renderer that draws with the line shader: one for all the lines.
            var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include)
                .Where(r => r.sharedMaterial != null && r.sharedMaterial.shader.name == "UniverseSim/Line").ToArray();
            Assert.That(renderers.Length, Is.EqualTo(1));
            Assert.That(renderers[0].enabled);
            lines.Visible = false;
            yield return null;
            Assert.That(renderers[0].enabled, Is.False);
            Assert.That(lines.DrawnCount, Is.EqualTo(0));
        }

        // The average of each counter over `Frames` frames, rendered into `target`.
        private static IEnumerator Measure(Dictionary<string, double> result)
        {
            var recorders = Counters.Select(c => ProfilerRecorder.StartNew(c.Category, c.Name, Frames)).ToArray();
            for (var i = 0; i < Frames + 2; ++i)
            {
                yield return null;
            }
            for (var i = 0; i < Counters.Length; ++i)
            {
                var recorder = recorders[i];
                if (recorder.Valid && recorder.Count > 0)
                {
                    var sum = 0.0;
                    for (var k = 0; k < recorder.Count; ++k)
                    {
                        sum += recorder.GetSample(k).Value;
                    }
                    result[Counters[i].Name] = sum / recorder.Count * Counters[i].Scale;
                }
                recorder.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator CostAtTypicalViews()
        {
            var cam = Camera.main;
            var document = Object.FindAnyObjectByType<UIDocument>();
            var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = target;
            document.panelSettings.targetTexture = target;
            yield return null;

            var earth = Index(399);
            var jupiter = Index(599);
            var views = new (string Name, System.Action Place)[]
            {
                ("system (start view)", () => { }),
                ("Jupiter system", () => rig.JumpTo(jupiter, 4.0e6, 0.5, 0.35)),
                ("Earth, 4 radii", () => rig.JumpTo(earth, Sim.Bodies[earth].MaxRadiusKm * 4.0, 0.6, 0.3)),
            };
            var results = new List<(string View, Dictionary<string, double> Values)>();
            foreach (var (name, place) in views)
            {
                place();
                var values = new Dictionary<string, double>();
                yield return Measure(values);
                results.Add((name, values));
            }

            cam.targetTexture = null;
            document.panelSettings.targetTexture = null;
            target.Release();
            Object.Destroy(target);

            var table = new StringBuilder("UniverseSim rendering cost (1920 x 1080, average of " + Frames + " frames)\n");
            table.Append("counter".PadRight(26));
            foreach (var (view, _) in results)
            {
                table.Append(view.PadLeft(22));
            }
            table.Append('\n');
            foreach (var counter in Counters)
            {
                table.Append(counter.Name.PadRight(26));
                foreach (var (_, values) in results)
                {
                    var text = values.TryGetValue(counter.Name, out var v)
                        ? v.ToString(counter.Unit.Length > 0 ? "F3" : "F0", CultureInfo.InvariantCulture) + counter.Unit
                        : "n/a";
                    table.Append(text.PadLeft(22));
                }
                table.Append('\n');
            }
            Debug.Log(table.ToString());
            TestContext.WriteLine(table.ToString());
            Assert.That(results.All(r => r.Values.ContainsKey("Triangles Count")), "the render counters are not recorded");
        }
    }
}
