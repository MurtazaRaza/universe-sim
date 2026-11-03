using System;
using System.Collections.Generic;
using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>
    /// One sourced sky cue (A-07, B-06, ADR-0018), as listed in Data/SkyCues.json, which
    /// <c>usim-pipeline sky-cues</c> writes from data/catalog/sky-cues.json. Field names match the manifest.
    /// </summary>
    [Serializable]
    public sealed class SkyCueInfo
    {
        public long naifId;
        public string body;
        /// <summary><see cref="SkyCues.DaytimeSky"/> or <see cref="SkyCues.Twilight"/>.</summary>
        public string cue;
        /// <summary>A hue of the pipeline's vocabulary (<see cref="SkyCues.Hues"/>).</summary>
        public string hue;
        public string sourceId;
        public int tier;
        public string reference;
        public string url;
        public string accessed;
        /// <summary>The source's sentence that states the hue.</summary>
        public string quote;

        /// <summary>The hue in words: "red and yellow".</summary>
        public string HueName => hue.Replace('_', ' ');

        /// <summary>Where it comes from, for the panels: the source, its tier when it is 2, the page and its words.</summary>
        public string Citation =>
            $"{sourceId}{(tier == 2 ? " (Tier 2)" : string.Empty)}: {reference}: \"{quote}\"";
    }

    /// <summary>
    /// The sky cues of the bodies: the hue of the daytime sky and of twilight, each with its source. The hues are
    /// facts; the colors that draw them are display choices (docs/ART_DIRECTION.md section 5). Whether a body has an
    /// atmosphere at all comes from the core (<c>CoreSimulation.Atmosphere</c>), not from here.
    /// </summary>
    public sealed class SkyCues
    {
        public const string DaytimeSky = "daytime_sky";
        public const string Twilight = "twilight";

        /// <summary>The hues a cue may name (the pipeline's vocabulary), each with its display color (sRGB).</summary>
        public static readonly IReadOnlyDictionary<string, Color> Hues = new Dictionary<string, Color>
        {
            ["blue"] = new Color(0.42f, 0.66f, 1.00f),
            ["red_and_yellow"] = new Color(1.00f, 0.52f, 0.25f),
            ["brownish_gray"] = new Color(0.74f, 0.62f, 0.52f),
            ["orange"] = new Color(1.00f, 0.66f, 0.32f),
        };

        /// <summary>The daytime sky of a body whose sky color is not in the data: a neutral gray, no claim.</summary>
        public static readonly Color NeutralSky = new Color(0.72f, 0.74f, 0.78f);

        [Serializable]
        private sealed class Manifest
        {
            public SkyCueInfo[] cues = Array.Empty<SkyCueInfo>();
        }

        private readonly Dictionary<(long, string), SkyCueInfo> byBody = new Dictionary<(long, string), SkyCueInfo>();
        private readonly List<SkyCueInfo> all = new List<SkyCueInfo>();

        public static readonly SkyCues None = new SkyCues();

        public IReadOnlyList<SkyCueInfo> All => all;

        /// <summary>The cues of a SkyCues.json manifest (none for empty text). Cues with an unknown hue are refused.</summary>
        public static SkyCues Parse(string manifestJson)
        {
            var cues = new SkyCues();
            if (string.IsNullOrWhiteSpace(manifestJson))
            {
                return cues;
            }
            foreach (var cue in JsonUtility.FromJson<Manifest>(manifestJson)?.cues ?? Array.Empty<SkyCueInfo>())
            {
                if (cue == null || !Hues.ContainsKey(cue.hue ?? string.Empty) ||
                    (cue.cue != DaytimeSky && cue.cue != Twilight))
                {
                    Debug.LogWarning($"UniverseSim: sky cue for {cue?.body} skipped (unknown cue or hue)");
                    continue;
                }
                cues.byBody[(cue.naifId, cue.cue)] = cue;
                cues.all.Add(cue);
            }
            return cues;
        }

        /// <summary>The body's cue of the given kind, if a source gives one.</summary>
        public bool TryGet(long naifId, string cue, out SkyCueInfo info) => byBody.TryGetValue((naifId, cue), out info);

        /// <summary>The display color of the body's cue, or <paramref name="fallback"/> without one.</summary>
        public Color ColorOf(long naifId, string cue, Color fallback) =>
            TryGet(naifId, cue, out var info) ? Hues[info.hue] : fallback;
    }
}
