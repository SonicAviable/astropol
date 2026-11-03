using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using StellarisClone.Station3D;

/// <summary>
/// Находит в проекте кит «3D Scifi Kit Starter Kit» (Creepy Cat) и собирает Resources/Station3DKit.asset
/// для 3D-станции. Материалы Built-in (Standard) копируются в URP/Lit (кит при этом не меняется).
/// Запускается сам после компиляции, если ассета ещё нет; вручную — меню Tools → Станция 3D.
/// </summary>
[InitializeOnLoad]
public static class Station3DKitSetup
{
    private const string KitAssetPath = "Assets/Resources/" + Station3DKit.ResourcePath + ".asset";
    private const string MatFolder = "Assets/Station3D_URP";
    private static readonly string[] RoomNames = { "Simple_Room_02", "Simple_Room_01" };
    private static readonly string[] PropNames = { "Crate_01" };

    static Station3DKitSetup()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var kit = AssetDatabase.LoadAssetAtPath<Station3DKit>(KitAssetPath);
            if (kit != null && kit.Room != null) return;
            if (FindPrefab(RoomNames) == null) return;   // кита нет — молчим, станция будет из примитивов
            Rebuild(false);
        };
    }

    [MenuItem("Tools/Станция 3D/Обновить кит Creepy Cat")]
    private static void RebuildMenu() => Rebuild(true);

    private static void Rebuild(bool verbose)
    {
        var room = FindPrefab(RoomNames);
        if (room == null)
        {
            if (verbose) EditorUtility.DisplayDialog("Станция 3D",
                "Кит «3D Scifi Kit Starter Kit» не найден (нужен префаб Simple_Room_02 или Simple_Room_01).\nСтанция будет строиться из примитивов.", "OK");
            return;
        }

        var kit = AssetDatabase.LoadAssetAtPath<Station3DKit>(KitAssetPath);
        if (kit == null)
        {
            kit = ScriptableObject.CreateInstance<Station3DKit>();
            Directory.CreateDirectory(Path.GetDirectoryName(KitAssetPath));
            AssetDatabase.CreateAsset(kit, KitAssetPath);
        }
        kit.Room = room;
        kit.Props.Clear();
        foreach (var n in PropNames) { var p = FindPrefab(n); if (p != null) kit.Props.Add(p); }

        kit.SourceMaterials.Clear();
        kit.UrpMaterials.Clear();
        var seen = new HashSet<Material>();
        var roots = new List<GameObject> { room };
        roots.AddRange(kit.Props);
        int converted = 0;
        foreach (var root in roots)
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !seen.Add(m) || !NeedsConversion(m)) continue;
                    var urp = ConvertToUrp(m);
                    if (urp == null) continue;
                    kit.SourceMaterials.Add(m);
                    kit.UrpMaterials.Add(urp);
                    converted++;
                }

        EditorUtility.SetDirty(kit);
        AssetDatabase.SaveAssets();
        string msg = $"Кит найден: {room.name}. Материалов переведено в URP: {converted}.";
        Debug.Log("[Station3D] " + msg);
        if (verbose) EditorUtility.DisplayDialog("Станция 3D", msg, "OK");
    }

    private static GameObject FindPrefab(params string[] names)
    {
        foreach (var n in names)
            foreach (var guid in AssetDatabase.FindAssets(n + " t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == n)
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
        return null;
    }

    private static bool NeedsConversion(Material m)
    {
        var s = m.shader != null ? m.shader.name : "";
        return !(s.StartsWith("Universal Render Pipeline/") || s.StartsWith("Shader Graphs/") ||
                 s.StartsWith("UI/") || s.StartsWith("Sprites/") || s.StartsWith("Hidden/Universal"));
    }

    // ---------- Built-in → URP/Lit ----------

    private static Material ConvertToUrp(Material src)
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) return null;
        Directory.CreateDirectory(MatFolder);
        string path = MatFolder + "/" + src.name + "_URP.mat";

        var so = new SerializedObject(src);
        var dst = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (dst == null) { dst = new Material(lit); AssetDatabase.CreateAsset(dst, path); }
        else dst.shader = lit;
        dst.shaderKeywords = new string[0];

        var mainTex = Tex(so, "_MainTex");
        var bump = Tex(so, "_BumpMap");
        var metal = Tex(so, "_MetallicGlossMap");
        var occl = Tex(so, "_OcclusionMap");
        var emisTex = Tex(so, "_EmissionMap");
        Color color = Col(so, "_Color", Color.white);
        Color emis = Col(so, "_EmissionColor", Color.black);
        float mode = Flt(so, "_Mode", 0f);
        bool transparent = mode >= 2f || src.renderQueue >= 3000 ||
                           src.name.Contains("Transp") || src.name.Contains("Glass") ||
                           (src.shader != null && src.shader.name.Contains("Glass"));

        dst.SetTexture("_BaseMap", mainTex);
        dst.SetTextureScale("_BaseMap", TexScale(so, "_MainTex"));
        dst.SetTextureOffset("_BaseMap", TexOffset(so, "_MainTex"));
        dst.SetColor("_BaseColor", color);
        dst.SetFloat("_Metallic", Flt(so, "_Metallic", 0f));
        dst.SetFloat("_Smoothness", metal != null ? Flt(so, "_GlossMapScale", 1f) : Flt(so, "_Glossiness", 0.5f));

        if (bump != null)
        {
            dst.SetTexture("_BumpMap", bump);
            dst.SetFloat("_BumpScale", Flt(so, "_BumpScale", 1f));
            dst.EnableKeyword("_NORMALMAP");
        }
        if (metal != null) { dst.SetTexture("_MetallicGlossMap", metal); dst.EnableKeyword("_METALLICSPECGLOSSMAP"); }
        if (occl != null)
        {
            dst.SetTexture("_OcclusionMap", occl);
            dst.SetFloat("_OcclusionStrength", Flt(so, "_OcclusionStrength", 1f));
            dst.EnableKeyword("_OCCLUSIONMAP");
        }
        if (emisTex != null || emis.maxColorComponent > 0.01f)
        {
            if (emisTex != null) dst.SetTexture("_EmissionMap", emisTex);
            dst.SetColor("_EmissionColor", emisTex != null && emis.maxColorComponent < 0.01f ? Color.white : emis);
            dst.EnableKeyword("_EMISSION");
            dst.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        if (mode == 1f)
        {
            dst.SetFloat("_AlphaClip", 1f);
            dst.SetFloat("_Cutoff", Flt(so, "_Cutoff", 0.5f));
            dst.EnableKeyword("_ALPHATEST_ON");
        }

        if (transparent)
        {
            if (color.a > 0.95f) { color.a = 0.35f; dst.SetColor("_BaseColor", color); }
            dst.SetFloat("_Surface", 1f);
            dst.SetFloat("_Blend", 0f);
            dst.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            dst.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            dst.SetFloat("_ZWrite", 0f);
            dst.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            dst.SetOverrideTag("RenderType", "Transparent");
            dst.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        else
        {
            dst.SetFloat("_Surface", 0f);
            dst.SetOverrideTag("RenderType", "Opaque");
            dst.renderQueue = -1;
        }
        EditorUtility.SetDirty(dst);
        return dst;
    }

    // Чтение свойств через сериализацию: работает и когда исходный шейдер в URP не найден
    private static SerializedProperty Find(SerializedObject so, string arr, string name)
    {
        var list = so.FindProperty("m_SavedProperties." + arr);
        if (list == null) return null;
        for (int i = 0; i < list.arraySize; i++)
        {
            var e = list.GetArrayElementAtIndex(i);
            var key = e.FindPropertyRelative("first");
            if (key != null && key.stringValue == name) return e.FindPropertyRelative("second");
        }
        return null;
    }

    private static Texture Tex(SerializedObject so, string n) =>
        Find(so, "m_TexEnvs", n)?.FindPropertyRelative("m_Texture")?.objectReferenceValue as Texture;

    private static Vector2 TexScale(SerializedObject so, string n) =>
        Find(so, "m_TexEnvs", n)?.FindPropertyRelative("m_Scale")?.vector2Value ?? Vector2.one;

    private static Vector2 TexOffset(SerializedObject so, string n) =>
        Find(so, "m_TexEnvs", n)?.FindPropertyRelative("m_Offset")?.vector2Value ?? Vector2.zero;

    private static float Flt(SerializedObject so, string n, float def) => Find(so, "m_Floats", n)?.floatValue ?? def;

    private static Color Col(SerializedObject so, string n, Color def) => Find(so, "m_Colors", n)?.colorValue ?? def;
}
