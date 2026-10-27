using System;
using System.Collections.Generic;
using UniverseSim.Unity.Native;
using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>
    /// One surface map (P6-07, ADR-0014), as listed in Textures/Bodies/BodyTextures.json, which
    /// <c>usim-pipeline textures</c> writes with the maps. Visual assets only: provenance in ATTRIBUTION.md.
    /// </summary>
    [Serializable]
    public sealed class SurfaceMapInfo
    {
        /// <summary>NAIF ID of the body.</summary>
        public long naifId;

        public string name;

        /// <summary>The texture's file name (body-&lt;id&gt;.jpg).</summary>
        public string file;

        /// <summary>What the map shows (for example "Monochrome global mosaic of Cassini ISS images").</summary>
        public string content;

        public string credit;

        /// <summary>Registry id of the source (docs/DATA_SOURCES.md).</summary>
        public string sourceId;

        /// <summary>The product page.</summary>
        public string page;

        /// <summary>The texture asset's name: the file name without its extension.</summary>
        public string TextureName => System.IO.Path.GetFileNameWithoutExtension(file);
    }

    /// <summary>The surface maps the scene has, by body.</summary>
    public sealed class SurfaceMaps
    {
        [Serializable]
        private sealed class Manifest
        {
            public SurfaceMapInfo[] textures = Array.Empty<SurfaceMapInfo>();
        }

        private readonly Dictionary<long, (SurfaceMapInfo Info, Texture2D Texture)> maps =
            new Dictionary<long, (SurfaceMapInfo, Texture2D)>();

        /// <summary>No maps.</summary>
        public static readonly SurfaceMaps None = new SurfaceMaps();

        /// <summary>Number of bodies with a map.</summary>
        public int Count => maps.Count;

        /// <summary>
        /// The maps of a manifest whose textures are among <paramref name="textures"/> (matched by name). A
        /// manifest entry without its texture is left out with a warning.
        /// </summary>
        public static SurfaceMaps Parse(string manifestJson, IEnumerable<Texture2D> textures)
        {
            var result = new SurfaceMaps();
            if (string.IsNullOrEmpty(manifestJson))
            {
                return result;
            }
            var byName = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            foreach (var texture in textures ?? Array.Empty<Texture2D>())
            {
                if (texture != null)
                {
                    byName[texture.name] = texture;
                }
            }
            foreach (var info in ReadManifest(manifestJson))
            {
                if (info == null || string.IsNullOrEmpty(info.file))
                {
                    continue;
                }
                if (byName.TryGetValue(info.TextureName, out var texture))
                {
                    result.maps[info.naifId] = (info, texture);
                }
                else
                {
                    Debug.LogWarning($"UniverseSim: the surface map {info.file} of {info.name} is missing");
                }
            }
            return result;
        }

        /// <summary>The entries of a BodyTextures.json manifest (none for empty text).</summary>
        public static SurfaceMapInfo[] ReadManifest(string manifestJson)
        {
            if (string.IsNullOrEmpty(manifestJson))
            {
                return Array.Empty<SurfaceMapInfo>();
            }
            return JsonUtility.FromJson<Manifest>(manifestJson)?.textures ?? Array.Empty<SurfaceMapInfo>();
        }

        /// <summary>The map of a body, if it has one (NAIF IDs only).</summary>
        public bool TryGet(UsimBodyId id, out SurfaceMapInfo info, out Texture2D texture)
        {
            if (id.IdNamespace == NativeConstants.IdNamespaceNaif && maps.TryGetValue(id.Value, out var map))
            {
                info = map.Info;
                texture = map.Texture;
                return true;
            }
            info = null;
            texture = null;
            return false;
        }
    }
}
