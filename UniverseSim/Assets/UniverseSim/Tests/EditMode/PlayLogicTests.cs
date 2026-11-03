using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UniverseSim.Unity.Tests.EditMode
{
    /// <summary>
    /// The engine-free parts of play (ADR-0019): the time throttle's mapping, the logbook's saved form, where the start
    /// near the Earth is placed, the facts' ratio wording, the speed streak mesh, and the synthesized sound (seeded,
    /// bounded, silent when off, audible when on). All values here are synthetic.
    /// </summary>
    public class PlayLogicTests
    {
        private const int Rate = 48000;

        [Test]
        public void TheThrottleMapsEveryPositionToAPresetAndBack()
        {
            const int presets = 7;
            Assert.That(TimeThrottle.Positions(presets), Is.EqualTo(14));
            Assert.That(TimeThrottle.FromPosition(0, presets), Is.EqualTo((-1, presets - 1)), "fastest reverse");
            Assert.That(TimeThrottle.FromPosition(presets - 1, presets), Is.EqualTo((-1, 0)), "slowest reverse");
            Assert.That(TimeThrottle.FromPosition(presets, presets), Is.EqualTo((1, 0)), "slowest forward");
            Assert.That(TimeThrottle.FromPosition(2 * presets - 1, presets), Is.EqualTo((1, presets - 1)), "fastest forward");
            for (var p = 0; p < 2 * presets; ++p)
            {
                var (sign, index) = TimeThrottle.FromPosition(p, presets);
                Assert.That(TimeThrottle.ToPosition(sign, index, presets), Is.EqualTo(p));
            }
            Assert.That(TimeThrottle.FromPosition(99, presets), Is.EqualTo((1, presets - 1)), "clamped");
        }

        [Test]
        public void TheLogbooksSavedFormRoundTrips()
        {
            var text = Logbook.Format(new[] { "sun", "moon" }, new long[] { 499, 399 });
            Assert.That(text, Is.EqualTo("moon,sun|399,499"), "sorted, so the saved text is stable");
            var done = new HashSet<string>();
            var visited = new HashSet<long>();
            Logbook.Parse(text, done, visited);
            Assert.That(done, Is.EquivalentTo(new[] { "moon", "sun" }));
            Assert.That(visited, Is.EquivalentTo(new long[] { 399, 499 }));

            done.Clear();
            visited.Clear();
            Logbook.Parse("moon||x,12,,", done, visited);
            Assert.That(done, Is.EquivalentTo(new[] { "moon" }));
            Assert.That(visited, Is.Empty, "a malformed planet list is skipped");
            Logbook.Parse(string.Empty, done, visited);
            Logbook.Parse(null, done, visited);
            Assert.That(Logbook.Format(Array.Empty<string>(), Array.Empty<long>()), Is.EqualTo("|"));
        }

        [Test]
        public void TheStartIsAcrossFromTheMoonTurnedSunwardAndRaised()
        {
            var toMoon = new Double3(0.0, 0.0, 1.0);
            var toSun = new Double3(1.0, 0.0, 0.0);
            var turn = 25.0 * Math.PI / 180.0;
            var raise = 12.0 * Math.PI / 180.0;
            var d = StartExperience.Placement(toMoon, toSun, turn, raise);
            Assert.That(d.Length, Is.EqualTo(1.0).Within(1e-12));
            Assert.That(Math.Asin(d.Y), Is.EqualTo(raise).Within(1e-12), "raised above the ecliptic");
            var flat = new Double3(d.X, 0.0, d.Z).Normalized;
            Assert.That(Math.Acos(flat.Dot(-toMoon)), Is.EqualTo(turn).Within(1e-12), "turned from the far side");
            Assert.That(flat.Dot(toSun), Is.GreaterThan(0.0), "toward the Sun's side");

            var other = StartExperience.Placement(toMoon, -toSun, turn, raise);
            Assert.That(other.X, Is.EqualTo(-d.X).Within(1e-12), "the other way when the Sun is on the other side");
        }

        [Test]
        public void RatiosReadAsTimesOrPercent()
        {
            Assert.That(BodyFacts.Times(2.53), Is.EqualTo("2.5 times"));
            Assert.That(BodyFacts.Times(11.2), Is.EqualTo("11 times"));
            Assert.That(BodyFacts.Times(1.02), Is.EqualTo("about the same as"));
            Assert.That(BodyFacts.Times(0.166), Is.EqualTo("17 % of"));
            Assert.That(BodyFacts.Times(0.012), Is.EqualTo("1.2 % of"));
        }

        [Test]
        public void TheStreakMeshHasAQuadPerStreak()
        {
            var mesh = SpeedCues.BuildMesh(10);
            try
            {
                Assert.That(mesh.vertexCount, Is.EqualTo(40));
                Assert.That(mesh.GetIndices(0).Length, Is.EqualTo(60));
                var uv = new List<Vector4>();
                mesh.GetUVs(0, uv);
                foreach (var v in uv)
                {
                    Assert.That(v.y, Is.InRange(0.2f, 1.6f), "distance from the axis");
                    Assert.That(Mathf.Abs(v.w), Is.EqualTo(1f), "side");
                }
                // The same pattern every time.
                var again = SpeedCues.BuildMesh(10);
                var uv2 = new List<Vector4>();
                again.GetUVs(0, uv2);
                Assert.That(uv2, Is.EqualTo(uv));
                UnityEngine.Object.DestroyImmediate(again);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static (float Rms, float Peak) Measure(float[] data)
        {
            double sum = 0.0;
            var peak = 0f;
            foreach (var x in data)
            {
                sum += x * x;
                peak = Mathf.Max(peak, Mathf.Abs(x));
            }
            return ((float)Math.Sqrt(sum / Math.Max(data.Length, 1)), peak);
        }

        [Test]
        public void TheMusicIsSeededBoundedAndAudible()
        {
            var a = new AmbientMusic(Rate);
            var b = new AmbientMusic(Rate);
            var bufferA = new float[2 * 1024];
            var bufferB = new float[2 * 1024];
            float peak = 0f, lateRms = 0f;
            // Twenty seconds: past the intro and through a chord change.
            for (var i = 0; i < 20 * Rate / 1024; ++i)
            {
                Array.Clear(bufferA, 0, bufferA.Length);
                Array.Clear(bufferB, 0, bufferB.Length);
                a.Render(bufferA, 2, 1f);
                b.Render(bufferB, 2, 1f);
                Assert.That(bufferB, Is.EqualTo(bufferA), "the same seed plays the same music");
                var (rms, p) = Measure(bufferA);
                peak = Mathf.Max(peak, p);
                if (i * 1024 > 8 * Rate)
                {
                    lateRms = Mathf.Max(lateRms, rms);
                }
            }
            Assert.That(peak, Is.LessThan(1.5f), "within the soft clip's knee");
            Assert.That(lateRms, Is.GreaterThan(0.01f), "audible after the intro");
        }

        [Test]
        public void TheEngineIsSilentWhenOffAndFollowsItsLevel()
        {
            var engine = new EngineSound(Rate);
            var buffer = new float[2 * 4800];
            engine.Render(buffer, 2, 1f);
            Assert.That(Measure(buffer).Peak, Is.EqualTo(0f), "silent with every target at zero");

            engine.Level = 1f;
            engine.Pitch = 0.5f;
            var loud = 0f;
            for (var i = 0; i < 10; ++i)
            {
                Array.Clear(buffer, 0, buffer.Length);
                engine.Render(buffer, 2, 1f);
                loud = Measure(buffer).Rms;
            }
            Assert.That(loud, Is.GreaterThan(0.02f), "audible at full level");
            Assert.That(Measure(buffer).Peak, Is.LessThan(1.5f));

            engine.Level = 0.2f;
            var quiet = 0f;
            for (var i = 0; i < 10; ++i)
            {
                Array.Clear(buffer, 0, buffer.Length);
                engine.Render(buffer, 2, 1f);
                quiet = Measure(buffer).Rms;
            }
            Assert.That(quiet, Is.LessThan(0.6f * loud), "quieter at a lower level");
        }

        [Test]
        public void InterfaceSoundsPlayOnceAndEnd()
        {
            var ui = new UiSounds(Rate);
            var buffer = new float[2 * 1024];
            ui.Render(buffer, 2, 1f);
            Assert.That(Measure(buffer).Peak, Is.EqualTo(0f));
            Assert.That(ui.Playing, Is.False);

            foreach (SoundCue cue in Enum.GetValues(typeof(SoundCue)))
            {
                ui.Trigger(cue);
                Assert.That(ui.Playing);
                var heard = 0f;
                for (var i = 0; i < 4 * Rate / 1024; ++i)
                {
                    Array.Clear(buffer, 0, buffer.Length);
                    ui.Render(buffer, 2, 1f);
                    var (rms, peak) = Measure(buffer);
                    heard = Mathf.Max(heard, rms);
                    Assert.That(peak, Is.LessThan(1f), cue.ToString());
                }
                Assert.That(heard, Is.GreaterThan(1e-3f), $"{cue} is audible");
                Assert.That(ui.Playing, Is.False, $"{cue} ends within four seconds");
            }
        }

        [Test]
        public void TheSoftClipIsSmoothAndBounded()
        {
            var last = -2f;
            for (var x = -3f; x <= 3f; x += 0.01f)
            {
                var y = Wave.SoftClip(x);
                Assert.That(y, Is.InRange(-1f, 1f));
                Assert.That(y, Is.GreaterThanOrEqualTo(last - 1e-6f), "monotonic");
                last = y;
            }
            Assert.That(Wave.SoftClip(0.1f), Is.EqualTo(0.1f).Within(1e-3f), "unity gain near zero");
            Assert.That(Wave.Sine(0.25), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(Wave.NoteHz(69), Is.EqualTo(440.0));
        }
    }
}
