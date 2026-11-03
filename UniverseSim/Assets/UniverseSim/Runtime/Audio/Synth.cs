using System;

namespace UniverseSim.Unity
{
    // The viewer's sounds are synthesized at run time by this code (ADR-0019 decision 6): no audio files, so nothing
    // to license or list in ATTRIBUTION.md, like the ship's procedural model. Everything here runs on the audio thread:
    // no allocation and no Unity calls after construction. The random generators are seeded, so a run sounds the same.

    /// <summary>A sine table and the note frequencies of equal temperament (A4 = 440 Hz, the tuning convention).</summary>
    public static class Wave
    {
        private const int Size = 2048;
        private static readonly float[] Table = Build();

        private static float[] Build()
        {
            var table = new float[Size + 1];
            for (var i = 0; i <= Size; ++i)
            {
                table[i] = (float)Math.Sin(2.0 * Math.PI * i / Size);
            }
            return table;
        }

        /// <summary>sin(2 pi <paramref name="cycles"/>), from the table with linear interpolation.</summary>
        public static float Sine(double cycles)
        {
            var x = (cycles - Math.Floor(cycles)) * Size;
            var i = Math.Min((int)x, Size - 1);
            return Table[i] + (Table[i + 1] - Table[i]) * (float)(x - i);
        }

        /// <summary>The frequency of MIDI note <paramref name="note"/>, Hz.</summary>
        public static double NoteHz(int note) => 440.0 * Math.Pow(2.0, (note - 69) / 12.0);

        /// <summary>A smooth limit at +-1: unity gain near zero, flat from +-1.5 (a cubic soft clip).</summary>
        public static float SoftClip(float x) =>
            x <= -1.5f ? -1f : x >= 1.5f ? 1f : x - 4f / 27f * x * x * x;
    }

    /// <summary>A small seeded random generator (xorshift32).</summary>
    public struct XorShift
    {
        private uint state;

        public XorShift(uint seed) => state = seed == 0 ? 0x9E3779B9u : seed;

        public uint Next()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        /// <summary>Uniform in [0, 1).</summary>
        public float Unit() => (Next() & 0xFFFFFF) / 16777216f;

        /// <summary>Uniform in [-1, 1).</summary>
        public float Signed() => 2f * Unit() - 1f;
    }

    /// <summary>
    /// Ambient music: a slow cycle of open chords over a drone, with sparse bell notes and a ping-pong echo for space.
    /// Original and generative; the same seed gives the same music.
    /// </summary>
    public sealed class AmbientMusic
    {
        // MIDI notes; the first of each chord, an octave down, is its drone.
        private static readonly int[][] Chords =
        {
            new[] { 50, 57, 64, 66, 69 }, // D, A, E, F#, A
            new[] { 47, 54, 62, 64, 69 }, // B, F#, D, E, A
            new[] { 43, 50, 57, 62, 66 }, // G, D, A, D, F#
            new[] { 45, 52, 57, 64, 67 }, // A, E, A, E, G
        };
        private static readonly int[] BellNotes = { 74, 76, 78, 81, 83, 86, 88 };

        private const double ChordSeconds = 16.0;
        private const double CrossfadeSeconds = 6.0;
        private const double IntroSeconds = 5.0;
        private const int BellVoices = 6;
        private const double Detune = 0.0015;

        private readonly double dt;
        private readonly double[][] phases;
        private readonly double[][] chordHz; // per chord: its notes, then its drone
        private readonly double[] bellPhase = new double[BellVoices];
        private readonly double[] bellHz = new double[BellVoices];
        private readonly double[] bellAge = new double[BellVoices];
        private readonly float[] bellAmp = new float[BellVoices];
        private readonly float[] bellPan = new float[BellVoices];
        private readonly float[] delayL, delayR;
        private int delayIndexL, delayIndexR;
        private int nextVoice;
        private double nextBell = 2.0;
        private double time;
        private float lowL, lowR;
        private XorShift random;

        public AmbientMusic(int sampleRate, uint seed = 0x51ED270Bu)
        {
            dt = 1.0 / Math.Max(sampleRate, 8000);
            phases = new double[Chords.Length][];
            chordHz = new double[Chords.Length][];
            for (var c = 0; c < Chords.Length; ++c)
            {
                var notes = Chords[c];
                phases[c] = new double[(notes.Length + 1) * 2];
                chordHz[c] = new double[notes.Length + 1];
                for (var n = 0; n <= notes.Length; ++n)
                {
                    chordHz[c][n] = Wave.NoteHz(n < notes.Length ? notes[n] : notes[0] - 12);
                }
            }
            delayL = new float[(int)(0.43 * sampleRate) + 1];
            delayR = new float[(int)(0.61 * sampleRate) + 1];
            random = new XorShift(seed);
        }

        /// <summary>Adds <paramref name="gain"/> times the next samples to <paramref name="data"/> (interleaved).</summary>
        public void Render(float[] data, int channels, float gain)
        {
            var frames = data.Length / Math.Max(channels, 1);
            for (var f = 0; f < frames; ++f)
            {
                var cycle = (long)Math.Floor(time / ChordSeconds);
                var local = time - cycle * ChordSeconds;
                var current = (int)(cycle % Chords.Length);
                var previous = (current + Chords.Length - 1) % Chords.Length;
                var fade = local < CrossfadeSeconds && cycle > 0 ? Smooth((float)(local / CrossfadeSeconds)) : 1f;

                float padL = 0f, padR = 0f;
                Chord(current, fade, ref padL, ref padR);
                if (fade < 1f)
                {
                    Chord(previous, 1f - fade, ref padL, ref padR);
                }
                // A slow sweep of a gentle low-pass keeps the pad warm and moving.
                var cutoff = 850.0 + 450.0 * Wave.Sine(time / 47.0);
                var k = (float)(1.0 - Math.Exp(-2.0 * Math.PI * cutoff * dt));
                lowL += (padL - lowL) * k;
                lowR += (padR - lowR) * k;

                float bellL = 0f, bellR = 0f;
                Bells(ref bellL, ref bellR);

                // The echo: the bells and a little of the pad, crossed between the channels.
                var echoL = delayL[delayIndexL];
                var echoR = delayR[delayIndexR];
                delayL[delayIndexL] = 0.6f * bellL + 0.2f * lowL + 0.42f * echoR;
                delayR[delayIndexR] = 0.6f * bellR + 0.2f * lowR + 0.42f * echoL;
                delayIndexL = (delayIndexL + 1) % delayL.Length;
                delayIndexR = (delayIndexR + 1) % delayR.Length;

                var intro = time < IntroSeconds ? Smooth((float)(time / IntroSeconds)) : 1f;
                var l = gain * intro * (lowL + bellL + 0.35f * echoL);
                var r = gain * intro * (lowR + bellR + 0.35f * echoR);
                Write(data, f, channels, l, r);
                time += dt;
            }
        }

        private void Chord(int c, float weight, ref float l, ref float r)
        {
            var notes = Chords[c];
            var p = phases[c];
            var count = notes.Length + 1;
            for (var n = 0; n < count; ++n)
            {
                var hz = chordHz[c][n];
                p[2 * n] += hz * (1.0 + Detune) * dt;
                p[2 * n + 1] += hz * (1.0 - Detune) * dt;
                p[2 * n] -= Math.Floor(p[2 * n]);
                p[2 * n + 1] -= Math.Floor(p[2 * n + 1]);
                // Each note breathes at its own slow rate.
                var breath = 0.65f + 0.35f * Wave.Sine(time * (0.031 + 0.017 * n) + 0.23 * n);
                var v = Wave.Sine(p[2 * n]) + Wave.Sine(p[2 * n + 1]) + 0.2f * Wave.Sine(2.0 * p[2 * n]);
                var drone = n == notes.Length ? 0.9f : 1f;
                var s = weight * breath * drone * v * (0.33f / count);
                var pan = n == notes.Length ? 0f : (n % 2 == 0 ? -0.3f : 0.3f);
                l += s * (1f - pan);
                r += s * (1f + pan);
            }
        }

        private void Bells(ref float l, ref float r)
        {
            if (time >= nextBell)
            {
                var v = nextVoice;
                nextVoice = (nextVoice + 1) % BellVoices;
                bellHz[v] = Wave.NoteHz(BellNotes[(int)(random.Unit() * BellNotes.Length) % BellNotes.Length]);
                bellPhase[v] = 0.0;
                bellAge[v] = 0.0;
                bellAmp[v] = 0.10f + 0.08f * random.Unit();
                bellPan[v] = 0.6f * random.Signed();
                nextBell = time + 2.5 + 5.0 * random.Unit();
            }
            for (var v = 0; v < BellVoices; ++v)
            {
                if (bellAmp[v] <= 0f)
                {
                    continue;
                }
                var age = bellAge[v];
                var envelope = bellAmp[v] * (float)Math.Exp(-age / 2.4) * Math.Min(1f, (float)(age / 0.004));
                if (envelope < 1e-5f && age > 0.1)
                {
                    bellAmp[v] = 0f;
                    continue;
                }
                bellPhase[v] += bellHz[v] * dt;
                var s = envelope * (Wave.Sine(bellPhase[v]) +
                                    0.25f * (float)Math.Exp(-age / 0.6) * Wave.Sine(2.76 * bellPhase[v]));
                l += s * (1f - bellPan[v]);
                r += s * (1f + bellPan[v]);
                bellAge[v] = age + dt;
            }
        }

        internal static float Smooth(float s)
        {
            s = s < 0f ? 0f : s > 1f ? 1f : s;
            return s * s * (3f - 2f * s);
        }

        internal static void Write(float[] data, int frame, int channels, float l, float r)
        {
            var i = frame * channels;
            if (channels == 1)
            {
                data[i] += 0.5f * (l + r);
                return;
            }
            data[i] += l;
            data[i + 1] += r;
            for (var c = 2; c < channels; ++c)
            {
                data[i + c] += 0.5f * (l + r);
            }
        }
    }

    /// <summary>
    /// The ship's engine: a low hum and a rumble that rise with its pace, a whine while boosting and a rushing sweep
    /// during a trip. The levels are set from the main thread and eased here, so they never click.
    /// </summary>
    public sealed class EngineSound
    {
        /// <summary>Targets, 0 to 1, set from the main thread.</summary>
        public volatile float Level, Pitch, Boost, Warp;

        private readonly double dt;
        private readonly float ease;
        private float level, pitch, boost, warp;
        private double hum, hum2, whine;
        private float brown, low, band;
        private XorShift random;

        public EngineSound(int sampleRate, uint seed = 0x1F2E3D4Cu)
        {
            dt = 1.0 / Math.Max(sampleRate, 8000);
            ease = (float)(1.0 - Math.Exp(-dt / 0.12));
            random = new XorShift(seed);
        }

        /// <summary>Adds <paramref name="gain"/> times the next samples to <paramref name="data"/> (interleaved).</summary>
        public void Render(float[] data, int channels, float gain)
        {
            var frames = data.Length / Math.Max(channels, 1);
            float targetLevel = Level, targetPitch = Pitch, targetBoost = Boost, targetWarp = Warp;
            if (targetLevel <= 0f && targetBoost <= 0f && targetWarp <= 0f && level < 1e-5f && boost < 1e-5f &&
                warp < 1e-5f)
            {
                level = boost = warp = 0f;
                return;
            }
            for (var f = 0; f < frames; ++f)
            {
                level += (targetLevel - level) * ease;
                pitch += (targetPitch - pitch) * ease;
                boost += (targetBoost - boost) * ease;
                warp += (targetWarp - warp) * ease;

                var hz = 42.0 + 34.0 * pitch;
                hum += hz * dt;
                hum2 += hz * 1.503 * dt;
                hum -= Math.Floor(hum);
                hum2 -= Math.Floor(hum2);
                var tone = 0.6f * Wave.Sine(hum) + 0.25f * Wave.Sine(2.0 * hum) + 0.15f * Wave.Sine(hum2);

                var white = random.Signed();
                brown = brown * 0.996f + white * 0.035f;

                whine += (320.0 + 380.0 * pitch) * dt;
                whine -= Math.Floor(whine);

                // A band of noise whose center rises with the trip (a state-variable filter).
                var center = 250.0 + 2600.0 * warp;
                var fc = (float)(2.0 * Math.Sin(Math.PI * Math.Min(center * dt, 0.25)));
                low += fc * band;
                var high = white - low - 0.7f * band;
                band += fc * high;

                var s = level * (0.5f * tone + 0.8f * brown) + 0.08f * boost * Wave.Sine(whine) + 0.35f * warp * band;
                AmbientMusic.Write(data, f, channels, gain * s, gain * s);
            }
        }
    }

    /// <summary>The interface sounds a viewer action can trigger.</summary>
    public enum SoundCue
    {
        Click = 0,
        Select = 1,
        Goal = 2,
        Arrive = 3,
        Depart = 4,
    }

    /// <summary>
    /// Short interface sounds: a click, a two-note selection ping, a rising chime for a logbook goal, a soft chord on
    /// arrival and a rising sweep on departure. <see cref="Trigger"/> is called on the main thread and queued for the
    /// audio thread.
    /// </summary>
    public sealed class UiSounds
    {
        private const int Voices = 16;
        private const int QueueLength = 32;
        private static readonly int[] GoalNotes = { 79, 83, 86, 91 };   // G, B, D, G rising
        private static readonly int[] ArriveNotes = { 62, 69, 74, 81 }; // D, A, D, A

        private readonly double dt;
        private readonly bool[] active = new bool[Voices];
        private readonly bool[] bell = new bool[Voices];
        private readonly bool[] swell = new bool[Voices];
        private readonly double[] delay = new double[Voices];
        private readonly double[] age = new double[Voices];
        private readonly double[] duration = new double[Voices];
        private readonly double[] startHz = new double[Voices];
        private readonly double[] endHz = new double[Voices];
        private readonly double[] phase = new double[Voices];
        private readonly float[] amp = new float[Voices];
        private readonly float[] decay = new float[Voices];
        private readonly int[] queue = new int[QueueLength];
        private volatile int head, tail;
        private int nextVoice;

        public UiSounds(int sampleRate) => dt = 1.0 / Math.Max(sampleRate, 8000);

        /// <summary>Queues <paramref name="cue"/> (main thread). A full queue drops it.</summary>
        public void Trigger(SoundCue cue)
        {
            var next = (head + 1) % QueueLength;
            if (next == tail)
            {
                return;
            }
            queue[head] = (int)cue;
            head = next;
        }

        /// <summary>True while any sound is playing or queued.</summary>
        public bool Playing
        {
            get
            {
                if (head != tail)
                {
                    return true;
                }
                foreach (var a in active)
                {
                    if (a)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>Adds <paramref name="gain"/> times the next samples to <paramref name="data"/> (interleaved).</summary>
        public void Render(float[] data, int channels, float gain)
        {
            while (tail != head)
            {
                Start((SoundCue)queue[tail]);
                tail = (tail + 1) % QueueLength;
            }
            var frames = data.Length / Math.Max(channels, 1);
            for (var v = 0; v < Voices; ++v)
            {
                if (!active[v])
                {
                    continue;
                }
                for (var f = 0; f < frames; ++f)
                {
                    var t = age[v] - delay[v];
                    age[v] += dt;
                    if (t < 0.0)
                    {
                        continue;
                    }
                    if (t > duration[v])
                    {
                        active[v] = false;
                        break;
                    }
                    var hz = startHz[v] + (endHz[v] - startHz[v]) * Math.Min(1.0, t / duration[v]);
                    phase[v] += hz * dt;
                    var envelope = swell[v]
                        ? (float)Math.Sin(Math.PI * t / duration[v])
                        : (float)Math.Exp(-t / decay[v]) * Math.Min(1f, (float)(t / 0.003));
                    var s = Wave.Sine(phase[v]);
                    if (bell[v])
                    {
                        s += 0.3f * (float)Math.Exp(-t / (0.4 * decay[v])) * Wave.Sine(2.0 * phase[v]);
                    }
                    var value = gain * amp[v] * envelope * s;
                    AmbientMusic.Write(data, f, channels, value, value);
                }
            }
        }

        private void Start(SoundCue cue)
        {
            switch (cue)
            {
                case SoundCue.Click:
                    Voice(false, false, 0.0, 0.06, 1700.0, 1150.0, 0.10f, 0.018f);
                    break;
                case SoundCue.Select:
                    Voice(true, false, 0.0, 0.35, 880.0, 880.0, 0.07f, 0.09f);
                    Voice(true, false, 0.07, 0.40, 1320.0, 1320.0, 0.06f, 0.10f);
                    break;
                case SoundCue.Goal:
                    var notes = GoalNotes;
                    for (var i = 0; i < notes.Length; ++i)
                    {
                        var hz = Wave.NoteHz(notes[i]);
                        Voice(true, false, 0.09 * i, 1.6, hz, hz, 0.08f, 0.5f);
                    }
                    break;
                case SoundCue.Arrive:
                    var chord = ArriveNotes;
                    for (var i = 0; i < chord.Length; ++i)
                    {
                        var hz = Wave.NoteHz(chord[i]);
                        Voice(true, false, 0.12 * i, 3.0, hz, hz, 0.07f, 1.1f);
                    }
                    break;
                case SoundCue.Depart:
                    Voice(false, true, 0.0, 1.1, 180.0, 820.0, 0.05f, 1f);
                    break;
            }
        }

        private void Voice(bool isBell, bool isSwell, double startDelay, double seconds, double fromHz, double toHz,
                           float amplitude, float decaySeconds)
        {
            var v = nextVoice;
            nextVoice = (nextVoice + 1) % Voices;
            active[v] = true;
            bell[v] = isBell;
            swell[v] = isSwell;
            delay[v] = startDelay;
            age[v] = 0.0;
            duration[v] = seconds;
            startHz[v] = fromHz;
            endHz[v] = toHz;
            phase[v] = 0.0;
            amp[v] = amplitude;
            decay[v] = decaySeconds;
        }
    }
}
