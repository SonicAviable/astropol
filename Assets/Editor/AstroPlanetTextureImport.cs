using UnityEditor;
using UnityEngine;

/// <summary>
/// Импорт карт планет (Assets/Resources/Planets/Astro): до 4096 px (по умолчанию Unity режет до 2048),
/// мип-мапы, анизотропия, долгота повторяется (шов 180° в шейдере), широта — по краю.
/// </summary>
public class AstroPlanetTextureImport : AssetPostprocessor
{
    private const string Folder = "Assets/Resources/Planets/Astro/";

    private void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').StartsWith(Folder)) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Default;
        ti.maxTextureSize = 4096;
        ti.mipmapEnabled = true;
        ti.anisoLevel = 8;
        ti.filterMode = FilterMode.Trilinear;
        ti.wrapModeU = TextureWrapMode.Repeat;
        ti.wrapModeV = TextureWrapMode.Clamp;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.crunchedCompression = false;
        ti.npotScale = TextureImporterNPOTScale.ToNearest;
        ti.alphaSource = assetPath.EndsWith(".png") ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
    }
}
