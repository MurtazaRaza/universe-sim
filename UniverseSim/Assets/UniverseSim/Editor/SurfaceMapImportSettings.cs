using UnityEditor;
using UnityEngine;

namespace UniverseSim.Unity.Editor
{
    /// <summary>
    /// Import settings of the surface maps that <c>usim-pipeline textures</c> writes to Textures/Bodies (P6-07):
    /// color in sRGB with mipmaps, repeating across the 180 degree seam and clamped at the poles, at the
    /// maps' full 2048 x 1024 pixels. Applied on every import, so the committed .meta files carry them.
    /// </summary>
    internal sealed class SurfaceMapImportSettings : AssetPostprocessor
    {
        public const string Folder = "Assets/UniverseSim/Textures/Bodies";

        private void OnPreprocessTexture()
        {
            if (assetImporter is TextureImporter importer && assetPath.StartsWith(Folder + "/"))
            {
                Apply(importer);
            }
        }

        /// <summary>Sets the surface map settings; true if any changed.</summary>
        public static bool Apply(TextureImporter importer)
        {
            var before = EditorJsonUtility.ToJson(importer);
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.isReadable = false;
            return EditorJsonUtility.ToJson(importer) != before;
        }
    }
}
