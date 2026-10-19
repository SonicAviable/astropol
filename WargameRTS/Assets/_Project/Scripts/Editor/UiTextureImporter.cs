using UnityEditor;
using UnityEngine;

namespace WargameRTS.Editor
{
    /// <summary>Import settings for HUD icons under Resources/UI (drawn with IMGUI / UI Toolkit).</summary>
    sealed class UiTextureImporter : AssetPostprocessor
    {
        const string UiFolder = "Assets/_Project/Resources/UI/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(UiFolder))
                return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
