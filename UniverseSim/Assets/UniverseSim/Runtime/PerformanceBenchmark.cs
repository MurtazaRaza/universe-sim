using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UniverseSim.Unity.Native;
using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The frame-rate measurement of A-13 (NFR-11) in a player build: started only by the command line option
    /// <c>-usim-benchmark &lt;file.json&gt;</c>, it shows a fixed set of views at a fixed time in the realistic style and
    /// in the stylized style at the high and low effects tiers, measures the frame times with vertical sync off, lets
    /// the automatic tier run on the heaviest view, writes the results as JSON and quits. Run it at 1920 x 1080
    /// (<c>-screen-width 1920 -screen-height 1080 -screen-fullscreen 0</c>). Moves the camera only through the rig and
    /// the ship, like a user; never the clock, which it pauses at a fixed time.
    /// </summary>
    public sealed class PerformanceBenchmark : MonoBehaviour
    {
        public const string Option = "-usim-benchmark";
        private const string FixedTime = "2026-10-21T12:00:00 UTC";
        private const float SettleSeconds = 1.5f;
        private const float MeasureSeconds = 5f;

        private string output;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartIfAsked()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, Option);
            if (i < 0 || i + 1 >= args.Length)
            {
                return;
            }
            var go = new GameObject("UniverseSim benchmark");
            DontDestroyOnLoad(go);
            go.AddComponent<PerformanceBenchmark>().output = args[i + 1];
        }

        private sealed class Result
        {
            public string View;
            public string Config;
            public int Frames;
            public double MeanMs;
            public double MedianMs;
            public double P99Ms;
        }

        private IEnumerator Start()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            var results = new List<Result>();
            var notes = new List<string>();
            SimulationHost host = null;
            for (var i = 0; i < 600 && (host == null || host.Simulation == null || host.Views.Count == 0); ++i)
            {
                host = FindAnyObjectByType<SimulationHost>();
                yield return null;
            }
            if (host == null || host.Simulation == null)
            {
                Write(results, "the pack did not load: " + (host != null ? host.LoadError : "no host"), notes);
                Application.Quit(1);
                yield break;
            }
            var sim = host.Simulation;
            var rig = FindAnyObjectByType<CameraRig>();
            var style = FindAnyObjectByType<VisualStyleSettings>();
            var quality = FindAnyObjectByType<EffectsQuality>();
            var pilot = FindAnyObjectByType<ShipPilot>();
            sim.SetPaused(true);
            if (sim.TryParseTime(FixedTime, out var t, out _))
            {
                sim.SetTime(t);
            }
            int Index(long naif) => sim.IndexOf(UsimBodyId.Naif(naif));

            yield return Wait(1f);
            var start = rig.OffsetKm;
            var startFocus = rig.FocusIndex;
            var configs = new (string Name, VisualStyle Style, EffectsTier Tier)[]
            {
                ("realistic", VisualStyle.Realistic, EffectsTier.High),
                ("stylized high", VisualStyle.Stylized, EffectsTier.High),
                ("stylized low", VisualStyle.Stylized, EffectsTier.Low),
            };
            var views = new (string Name, Action Place)[]
            {
                ("system", () => rig.JumpTo(startFocus, start.Length, Math.Atan2(start.X, start.Z),
                                            Math.Asin(Math.Clamp(start.Normalized.Y, -1.0, 1.0)))),
                ("Jupiter system", () => rig.JumpTo(Index(599), 4.0e6, 0.5, 0.35)),
                ("Earth, 4 radii", () => rig.JumpTo(Index(399), sim.Bodies[Index(399)].MaxRadiusKm * 4.0, 0.6, 0.3)),
                ("Saturn, 3 radii", () => rig.JumpTo(Index(699), sim.Bodies[Index(699)].MaxRadiusKm * 3.0, 0.4, 0.25)),
            };
            foreach (var (view, place) in views)
            {
                foreach (var (config, visualStyle, tier) in configs)
                {
                    style.SetStyle(visualStyle);
                    quality.SetFixed(tier, remember: false);
                    place();
                    yield return Measure(view, config, results);
                }
            }

            // The heaviest view: landed on Mars in the stylized style, with the sky, a halo, the ship and its panel.
            style.SetStyle(VisualStyle.Stylized);
            quality.SetFixed(EffectsTier.High, remember: false);
            var mars = Index(499);
            rig.JumpTo(mars, sim.Bodies[mars].MaxRadiusKm * 1.5, 0.9, 0.2);
            rig.Select(mars);
            yield return Wait(0.5f);
            pilot.SetActive(true);
            yield return Wait(0.5f);
            pilot.Descend();
            yield return Wait(10f);
            notes.Add("Mars: the ship is " + pilot.State);
            yield return Measure("Mars, landed", "stylized high", results);
            quality.SetFixed(EffectsTier.Low, remember: false);
            yield return Measure("Mars, landed", "stylized low", results);
            quality.SetAutomatic(remember: false);
            yield return Wait(12f);
            notes.Add($"automatic tier after 12 s landed on Mars: {EffectsQuality.DisplayName(quality.Tier)} " +
                      $"(smoothed frame {quality.SmoothedFrameSeconds * 1000f:F2} ms)");
            pilot.SetActive(false);

            Write(results, null, notes);
            Application.Quit(0);
        }

        private static IEnumerator Wait(float seconds)
        {
            var end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end)
            {
                yield return null;
            }
        }

        private static IEnumerator Measure(string view, string config, List<Result> results)
        {
            yield return Wait(SettleSeconds);
            var frames = new List<double>(1024);
            var end = Time.unscaledTime + MeasureSeconds;
            while (Time.unscaledTime < end)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000.0);
            }
            frames.Sort();
            var sum = 0.0;
            foreach (var f in frames)
            {
                sum += f;
            }
            results.Add(new Result
            {
                View = view,
                Config = config,
                Frames = frames.Count,
                MeanMs = sum / Math.Max(frames.Count, 1),
                MedianMs = frames.Count > 0 ? frames[frames.Count / 2] : 0.0,
                P99Ms = frames.Count > 0 ? frames[Math.Min(frames.Count - 1, (int)(frames.Count * 0.99))] : 0.0,
            });
        }

        private void Write(List<Result> results, string error, List<string> notes)
        {
            string Q(string s) => "\"" + (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            string N(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
            var json = new StringBuilder();
            json.Append("{\n");
            json.Append($"  \"device\": {Q(SystemInfo.deviceModel)},\n");
            json.Append($"  \"cpu\": {Q(SystemInfo.processorType)},\n");
            json.Append($"  \"gpu\": {Q(SystemInfo.graphicsDeviceName)},\n");
            json.Append($"  \"graphics_api\": {Q(SystemInfo.graphicsDeviceVersion)},\n");
            json.Append($"  \"resolution\": {Q(Screen.width + " x " + Screen.height)},\n");
            json.Append($"  \"unity\": {Q(Application.unityVersion)},\n");
            json.Append($"  \"pack\": {Q(Path.GetFileName(FindAnyObjectByType<SimulationHost>()?.PackPath))},\n");
            json.Append($"  \"time\": {Q(FixedTime)},\n");
            json.Append($"  \"error\": {Q(error)},\n");
            json.Append("  \"notes\": [");
            for (var i = 0; i < notes.Count; ++i)
            {
                json.Append(i > 0 ? ", " : string.Empty).Append(Q(notes[i]));
            }
            json.Append("],\n  \"results\": [\n");
            for (var i = 0; i < results.Count; ++i)
            {
                var r = results[i];
                json.Append($"    {{\"view\": {Q(r.View)}, \"config\": {Q(r.Config)}, \"frames\": {r.Frames}, " +
                            $"\"mean_ms\": {N(r.MeanMs)}, \"median_ms\": {N(r.MedianMs)}, \"p99_ms\": {N(r.P99Ms)}, " +
                            $"\"fps\": {N(r.MeanMs > 0 ? 1000.0 / r.MeanMs : 0)}}}");
                json.Append(i + 1 < results.Count ? ",\n" : "\n");
            }
            json.Append("  ]\n}\n");
            File.WriteAllText(output, json.ToString());
        }
    }
}
