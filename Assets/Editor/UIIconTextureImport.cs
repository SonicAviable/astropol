using UnityEditor;
using UnityEngine;

/// <summary>
/// Импорт рисованных значков интерфейса (Resources/UI/Icons, гербы фракций): исходники 512 px, а на экране они 14–60 px.
/// По умолчанию Unity сжимает их обычным качеством (блочные артефакты по краям силуэта) и строит мип-мапы простым box-фильтром —
/// значки выходили мыльными и «рваными». Здесь: сжатие высокого качества, мип-мапы с фильтром Кайзера (резче при уменьшении),
/// прозрачность без тёмной каймы, трилинейная фильтрация.
/// </summary>
public class UIIconTextureImport : AssetPostprocessor
{
    private static readonly string[] Folders =
    {
        "Assets/Resources/UI/Icons/",
        "Assets/Resources/Factions/",
    };

    // Поменять число — Unity переимпортирует все значки с новыми настройками
    public override uint GetVersion() => 1;

    private static bool Match(string path)
    {
        path = path.Replace('\\', '/');
        foreach (var f in Folders) if (path.StartsWith(f)) return true;
        return false;
    }

    private void OnPreprocessTexture()
    {
        if (!Match(assetPath)) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = true;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.alphaIsTransparency = true;           // без тёмной каймы на краях при фильтрации
        ti.mipmapEnabled = true;
        ti.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
        ti.mipMapsPreserveCoverage = false;
        ti.streamingMipmaps = false;
        ti.filterMode = FilterMode.Trilinear;
        ti.anisoLevel = 4;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.maxTextureSize = 512;
        // Высокое качество (BC7 на ПК): почти без блочных артефактов, а память — как у обычного сжатия
        // (без сжатия 200+ значков по 512 px заняли бы ~300 МБ видеопамяти)
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
        ti.crunchedCompression = false;
    }

    [MenuItem("Astropolity/Переимпортировать значки интерфейса")]
    private static void ReimportAll()
    {
        foreach (var f in Folders)
        {
            string dir = f.TrimEnd('/');
            if (AssetDatabase.IsValidFolder(dir)) AssetDatabase.ImportAsset(dir, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        }
        Debug.Log("[UIIconTextureImport] Значки переимпортированы");
    }
}
