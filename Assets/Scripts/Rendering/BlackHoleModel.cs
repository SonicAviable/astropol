using System;
using System.IO;
using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Модель чёрной дыры (Resources/Models/BlackHole): вблизи на карте галактики (MapBlackHole) и в виде системы
    /// вместо плоского спрайта.
    ///
    /// Слои вынуты из исходного FBX скриптом Tools/black_hole/export_bh.py в компактный blackhole.bytes
    /// (без импортёра моделей: оси, масштаб и порядок вершин заданы заранее): горизонт, три слоя диска с «куполом»
    /// гравитационной линзы, фотонное кольцо, внешнее кольцо и облако обломков. Радиус внешнего кольца — 1.
    /// Светятся слои шейдером Astropolity/FX/BlackHole, горизонт — непрозрачный чёрный.
    ///
    /// Камера смотрит на карту сверху, а линза читается под низким углом — поэтому диск всегда повёрнут к камере
    /// так, что она видит его под <see cref="BlackHoleFacing.Elevation"/>: дуги встают над и под горизонтом.
    /// </summary>
    public static class BlackHoleModel
    {
        private const string Folder = "Models/BlackHole/";
        /// <summary>Радиус внешнего кольца в мире — внутри первой орбиты (не ближе 13).</summary>
        public const float DefaultRadius = 9f;

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

        /// <summary>
        /// Собрать модель под parent (в его центре); null — если модели нет (тогда остаётся спрайт карты).
        /// withRings = false — без внешнего кольца и обломков (на карте они сплющиваются в плоское «кольцо Сатурна»
        /// и забивают ядро с дугами линзы). elevation — под каким углом к диску смотрит камера.
        /// </summary>
        public static GameObject Build(Transform parent, float radius = DefaultRadius, bool withRings = true,
                                       float elevation = BlackHoleFacing.DefaultElevation)
        {
            if (!Available) return null;

            var root = new GameObject("BlackHole");
            root.transform.SetParent(parent, false);
            root.transform.localScale = Vector3.one * radius;
            root.AddComponent<BlackHoleFacing>().Elevation = elevation;

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
                if (!withRings && (s_names[i] == "ring" || s_names[i] == "debris")) continue;
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

    /// <summary>
    /// Диск поворачивается к камере: она видит его под <see cref="Elevation"/> над плоскостью диска,
    /// диск лежит горизонтально на экране. Дыра симметрична, так что «правильной» стороны у неё нет.
    /// </summary>
    public class BlackHoleFacing : MonoBehaviour
    {
        public const float DefaultElevation = 17f;
        /// <summary>Угол камеры над плоскостью диска, градусы.</summary>
        public float Elevation = DefaultElevation;
        private Camera _cam;

        private void LateUpdate()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;
            var ct = _cam.transform;
            Vector3 toCam = ct.position - transform.position;
            if (toCam.sqrMagnitude < 1e-6f) return;
            toCam.Normalize();
            Vector3 up = Vector3.ProjectOnPlane(ct.up, toCam);
            if (up.sqrMagnitude < 1e-6f) return;
            up.Normalize();
            float e = Elevation * Mathf.Deg2Rad;
            Vector3 normal = toCam * Mathf.Sin(e) + up * Mathf.Cos(e);
            Vector3 side = Vector3.Cross(up, toCam);
            transform.rotation = Quaternion.LookRotation(Vector3.Cross(side, normal), normal);
        }
    }

    /// <summary>
    /// Чёрная дыра на карте галактики: при приближении камеры (там же, где проступают мини-системы)
    /// спрайт плавно уступает место объёмной модели. Модель строится лениво при первом приближении.
    /// В виде системы модель карты спрятана — там своя, крупнее.
    /// </summary>
    public class MapBlackHole : MonoBehaviour
    {
        public const string ModelName = "BlackHoleMap";
        /// <summary>
        /// Масштаб модели (радиус внешнего кольца, которого на карте нет): светящийся диск выходит на ~2,4 —
        /// крупнее короны обычной звезды, ядро с дугами линзы хорошо читается вблизи.
        /// </summary>
        private const float Radius = 3.4f;
        private const float Elevation = 20f;
        private const float ShowHeight = 90f, FullHeight = 52f;   // как у мини-систем

        /// <summary>Отладочный предпросмотр (F8) на обычной звезде.</summary>
        public bool Preview { get; private set; }

        private StarAnimator _anim;
        private GameObject _model;
        private Renderer[] _glow;
        private Renderer _horizon;
        private MaterialPropertyBlock _mpb;
        private Camera _cam;
        private bool _failed;

        public void Init(StarAnimator anim, bool preview = false)
        {
            _anim = anim;
            Preview = preview;
        }

        private void LateUpdate()
        {
            if (_cam == null) _cam = Camera.main;
            bool inSystem = SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView;
            float k = 0f;
            if (_cam != null && !inSystem && !_failed)
            {
                float h = _cam.transform.position.y - transform.position.y;
                k = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FullHeight, ShowHeight, h));
            }
            if (_anim != null && !inSystem) _anim.BodyHidden = k;

            if (k <= 0.01f)
            {
                if (_model != null && _model.activeSelf) _model.SetActive(false);
                return;
            }
            if (_model == null && !Create()) return;
            if (!_model.activeSelf) _model.SetActive(true);

            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _mpb.SetFloat("_Fade", k);
            foreach (var r in _glow) r.SetPropertyBlock(_mpb);
            // Непрозрачный горизонт не гаснет — включается, когда модель уже заметна (ядро спрайта дыры и так тёмное)
            if (_horizon != null) _horizon.enabled = k > 0.35f;
        }

        private bool Create()
        {
            _model = BlackHoleModel.Build(transform, Radius, withRings: false, elevation: Elevation);
            if (_model == null)
            {
                _failed = true;
                if (_anim != null) _anim.BodyHidden = 0f;
                return false;
            }
            _model.name = ModelName;
            var all = _model.GetComponentsInChildren<Renderer>(true);
            var glow = new System.Collections.Generic.List<Renderer>();
            foreach (var r in all)
            {
                if (r.gameObject.name == "horizon") _horizon = r;
                else glow.Add(r);
            }
            _glow = glow.ToArray();
            return true;
        }

        private void OnDestroy()
        {
            if (_model != null) Destroy(_model);
            if (_anim != null) _anim.BodyHidden = 0f;
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
