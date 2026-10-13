using System;
using System.IO;
using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Модель чёрной дыры для вида системы (Resources/Models/BlackHole) вместо плоского спрайта карты.
    ///
    /// Слои вынуты из исходного FBX скриптом Tools/black_hole/export_bh.py в компактный blackhole.bytes
    /// (без импортёра моделей: оси, масштаб и порядок вершин заданы заранее): горизонт, три слоя диска с «куполом»
    /// гравитационной линзы, фотонное кольцо, внешнее кольцо и облако обломков. Радиус внешнего кольца — 1.
    /// Светятся слои шейдером Astropolity/FX/BlackHole, горизонт — непрозрачный чёрный.
    ///
    /// Камера вида системы смотрит под 55° сверху, а линза читается под низким углом — поэтому диск наклонён
    /// от камеры на <see cref="Tilt"/>: камера видит его под ~17°, дуги встают над и под горизонтом.
    /// </summary>
    public static class BlackHoleModel
    {
        private const string Folder = "Models/BlackHole/";
        /// <summary>Радиус внешнего кольца в мире — внутри первой орбиты (не ближе 13).</summary>
        public const float DefaultRadius = 9f;
        /// <summary>Наклон диска от камеры (её наклон в виде системы — 55°).</summary>
        public const float Tilt = 38f;

        private static Mesh[] s_meshes;
        private static string[] s_names;
        private static Shader s_shader;
        private static Material[] s_materials;
        private static bool s_missing;

        /// <summary>Перезагрузка домена в Play Mode выключена — сбрасываем кэш на каждом запуске.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            s_meshes = null;
            s_names = null;
            s_shader = null;
            s_materials = null;
            s_missing = false;
        }

        public static bool Available
        {
            get
            {
                if (s_meshes == null && !s_missing) Load();
                return s_meshes != null && s_shader != null && ShaderCache.Unlit != null;
            }
        }

        /// <summary>Собрать модель под parent (в его центре); null — если модели нет (тогда остаётся спрайт карты).</summary>
        public static GameObject Build(Transform parent, float radius = DefaultRadius)
        {
            if (!Available) return null;

            var root = new GameObject("BlackHole");
            root.transform.SetParent(parent, false);
            root.transform.localRotation = Quaternion.Euler(Tilt, 0f, 0f);
            root.transform.localScale = Vector3.one * radius;

            // Диск и кольца медленно вращаются вокруг оси; горизонт — сфера, его крутить незачем
            var spin = new GameObject("Spin").transform;
            spin.SetParent(root.transform, false);
            spin.gameObject.AddComponent<BlackHoleSpin>();

            // Материалы общие для всех входов в систему: у модели нет ничего своего на экземпляр
            if (s_materials == null)
            {
                s_materials = new Material[s_meshes.Length];
                for (int i = 0; i < s_meshes.Length; i++) s_materials[i] = MaterialFor(s_names[i]);
            }

            for (int i = 0; i < s_meshes.Length; i++)
            {
                var mat = s_materials[i];
                if (mat == null) continue;
                var go = new GameObject(s_names[i]);
                go.transform.SetParent(s_names[i] == "horizon" ? root.transform : spin, false);
                go.AddComponent<MeshFilter>().sharedMesh = s_meshes[i];
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }
            return root;
        }

        private static Material MaterialFor(string layer)
        {
            if (layer == "horizon")
            {
                var h = new Material(ShaderCache.Unlit) { hideFlags = HideFlags.DontSave, name = "BlackHole_Horizon" };
                if (h.HasProperty("_BaseColor")) h.SetColor("_BaseColor", Color.black);
                if (h.HasProperty("_Color")) h.SetColor("_Color", Color.black);
                return h;
            }

            var m = new Material(s_shader) { hideFlags = HideFlags.DontSave, name = "BlackHole_" + layer };
            switch (layer)
            {
                case "light1": Glow(m, "bh_light_1", 0.9f, 1.5f, 3f); break;
                case "light2": Glow(m, "bh_light_2", 0.8f, 1.3f, 3f); break;
                case "light3": Glow(m, "bh_light_3", 0.7f, 1.1f, 3f); break;
                case "photon":
                    // Тонкое фотонное кольцо по краю горизонта: только силуэт, без диска
                    Glow(m, null, 0f, 2.2f, 6f);
                    m.SetFloat("_Detail", 0f);
                    break;
                case "ring":
                    Tinted(m, "bh_ring", 0.55f, new Color(1f, 0.7f, 0.45f));
                    break;
                case "debris":
                    Tinted(m, null, 0.5f, new Color(1f, 0.62f, 0.35f));
                    break;
                default:
                    Debug.LogWarning("[BlackHole] Неизвестный слой модели: " + layer);
                    return null;
            }
            return m;
        }

        private static void Glow(Material m, string tex, float disk, float lens, float rimPow)
        {
            if (tex != null) m.SetTexture("_MainTex", Resources.Load<Texture2D>(Folder + tex));
            m.SetFloat("_Intensity", disk);
            m.SetFloat("_DomeGain", lens);
            m.SetFloat("_RimPow", rimPow);
            m.SetFloat("_TexColor", 0f);
        }

        private static void Tinted(Material m, string tex, float intensity, Color tint)
        {
            if (tex != null) m.SetTexture("_MainTex", Resources.Load<Texture2D>(Folder + tex));
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_TexColor", 1f);
            m.SetColor("_Tint", tint);
        }

        // ==================== ЗАГРУЗКА ====================

        private static void Load()
        {
            var data = Resources.Load<TextAsset>(Folder + "blackhole");
            s_shader = Resources.Load<Shader>("Shaders/BlackHole");
            if (data == null || s_shader == null)
            {
                s_missing = true;
                Debug.LogWarning("[BlackHole] Нет Resources/" + Folder + "blackhole.bytes или шейдера Shaders/BlackHole — чёрные дыры остаются спрайтом.");
                return;
            }
            try
            {
                Parse(data.bytes);
            }
            catch (Exception e)
            {
                s_meshes = null;
                s_missing = true;
                Debug.LogWarning("[BlackHole] Не удалось прочитать модель: " + e.Message);
            }
            Resources.UnloadAsset(data);
        }

        /// <summary>
        /// Формат BHM1 (little-endian): число слоёв; у слоя — имя, число вершин и индексов,
        /// позиции int16×3 (/32767), нормали int8×3 (/127), UV uint16×2 (/65535·1,5 − 0,25), треугольники uint16.
        /// </summary>
        private static void Parse(byte[] bytes)
        {
            using var r = new BinaryReader(new MemoryStream(bytes));
            if (new string(r.ReadChars(4)) != "BHM1") throw new InvalidDataException("не тот формат");
            int count = r.ReadInt32();
            var meshes = new Mesh[count];
            var names = new string[count];
            for (int l = 0; l < count; l++)
            {
                names[l] = System.Text.Encoding.UTF8.GetString(r.ReadBytes(r.ReadByte()));
                int vc = r.ReadInt32(), ic = r.ReadInt32();

                var pos = new Vector3[vc];
                for (int i = 0; i < vc; i++)
                    pos[i] = new Vector3(r.ReadInt16(), r.ReadInt16(), r.ReadInt16()) / 32767f;
                var nrm = new Vector3[vc];
                for (int i = 0; i < vc; i++)
                    nrm[i] = new Vector3(r.ReadSByte(), r.ReadSByte(), r.ReadSByte()) / 127f;
                var uv = new Vector2[vc];
                for (int i = 0; i < vc; i++)
                    uv[i] = new Vector2(r.ReadUInt16(), r.ReadUInt16()) / 65535f * 1.5f - new Vector2(0.25f, 0.25f);
                var tris = new int[ic];
                for (int i = 0; i < ic; i++) tris[i] = r.ReadUInt16();

                var mesh = new Mesh { name = "BlackHole_" + names[l], hideFlags = HideFlags.DontSave };
                mesh.vertices = pos;
                mesh.normals = nrm;
                mesh.uv = uv;
                mesh.triangles = tris;
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                meshes[l] = mesh;
            }
            s_meshes = meshes;
            s_names = names;
        }
    }

    /// <summary>Медленное вращение диска чёрной дыры вокруг её оси (и на паузе — это декор).</summary>
    public class BlackHoleSpin : MonoBehaviour
    {
        private const float DegPerSecond = 5f;

        private void Update()
        {
            transform.localRotation = Quaternion.Euler(0f, -Time.unscaledTime * DegPerSecond, 0f);
        }
    }
}
