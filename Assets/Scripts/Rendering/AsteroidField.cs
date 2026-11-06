using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Модели астероидов (Resources/Models/Asteroids): три меша из FBX и материалы URP Lit с инстансингом.
    /// Текстура цвета уже содержит запечённые затенение и полости, рельеф даёт карта нормалей.
    /// Меши приводятся к единичному диаметру с центром в нуле — пивоты в FBX стоят где придётся.
    /// </summary>
    public static class AsteroidAssets
    {
        private const string Folder = "Models/Asteroids/";
        private static Mesh[] s_meshes;
        private static Material[] s_mats;
        private static Matrix4x4[] s_pivots;
        private static bool s_tried;

        public static bool Ready { get { Load(); return s_meshes != null && s_meshes.Length > 0; } }
        public static int Count => Ready ? s_meshes.Length : 0;
        public static Mesh MeshAt(int i) => s_meshes[i];
        public static Material MaterialAt(int i) => s_mats[i];
        public static Matrix4x4 PivotAt(int i) => s_pivots[i];

        private static void Load()
        {
            if (s_tried) return;
            s_tried = true;
            var model = Resources.Load<GameObject>(Folder + "asteroids");
            var shader = ShaderCache.Lit;
            if (model == null || shader == null) return;

            var filters = new List<MeshFilter>(model.GetComponentsInChildren<MeshFilter>(true));
            filters.RemoveAll(f => f.sharedMesh == null);
            filters.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            if (filters.Count == 0) return;

            s_meshes = new Mesh[filters.Count];
            s_mats = new Material[filters.Count];
            s_pivots = new Matrix4x4[filters.Count];
            for (int i = 0; i < filters.Count; i++)
            {
                var mesh = filters[i].sharedMesh;
                var b = mesh.bounds;
                float size = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                float norm = size > 1e-5f ? 1f / size : 1f;
                s_meshes[i] = mesh;
                s_pivots[i] = Matrix4x4.Scale(Vector3.one * norm) * Matrix4x4.Translate(-b.center);

                int k = Mathf.Clamp(i + 1, 1, 3);
                var mat = new Material(shader) { name = "Asteroid_" + k, enableInstancing = true };
                var albedo = Resources.Load<Texture2D>(Folder + "asteroid_" + k);
                var normal = Resources.Load<Texture2D>(Folder + "asteroid_" + k + "_normal");
                if (albedo != null)
                {
                    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", albedo);
                    if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", albedo);
                }
                if (normal != null && mat.HasProperty("_BumpMap"))
                {
                    mat.SetTexture("_BumpMap", normal);
                    mat.SetFloat("_BumpScale", 1.2f);
                    mat.EnableKeyword("_NORMALMAP");
                }
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.92f, 0.88f, 0.84f));
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.12f);
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
                s_mats[i] = mat;
            }
        }
    }

    /// <summary>
    /// Поле астероидов: пояс вокруг звезды (камни медленно обращаются вокруг центра) или скопление
    /// в пустоте (камни дрейфуют на месте). Рисуется инстансингом без отдельных объектов на каждый камень;
    /// каждый камень вращается вокруг своей оси.
    /// </summary>
    public class AsteroidField : MonoBehaviour
    {
        private struct Rock
        {
            public int Mesh;
            public Vector3 Pos;          // для скопления — позиция; для пояса — (радиус, высота, нач. угол)
            public float Orbit;          // угловая скорость обращения (рад/с), 0 — без обращения
            public Vector3 Axis;
            public float Spin, Scale;
            public Quaternion Rot0;
        }

        private readonly List<Rock> _rocks = new List<Rock>();
        private Matrix4x4[][] _buffers;
        private int[] _counts;

        /// <summary>Рисовать ли сейчас (например, только при приближении камеры). null — всегда.</summary>
        public System.Func<bool> VisibleWhen;

        /// <summary>Пояс вокруг центра родителя между радиусами inner..outer.</summary>
        public static AsteroidField CreateBelt(Transform parent, float inner, float outer, int count, int seed,
                                               float minScale, float maxScale, float thickness)
        {
            var f = Make(parent, "AsteroidBelt");
            var rng = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                // Плотнее к середине пояса
                float u = (float)(rng.NextDouble() + rng.NextDouble()) * 0.5f;
                float r = Mathf.Lerp(inner, outer, u);
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float h = ((float)rng.NextDouble() - 0.5f) * thickness;
                f._rocks.Add(NewRock(rng, new Vector3(r, h, a), 0.5f / Mathf.Max(4f, r) * (0.85f + 0.3f * (float)rng.NextDouble()), minScale, maxScale));
            }
            return f;
        }

        /// <summary>Скопление вокруг точки center (в координатах родителя): сплюснутый эллипсоид радиуса radius.</summary>
        public static AsteroidField CreateCluster(Transform parent, Vector3 center, float radius, int count, int seed,
                                                  float minScale, float maxScale, float flatten)
        {
            var f = Make(parent, "AsteroidCluster");
            f.transform.localPosition = center;
            var rng = new System.Random(seed);
            float stretch = 1f + (float)rng.NextDouble() * 0.8f;
            float yaw = (float)rng.NextDouble() * 360f;
            var q = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < count; i++)
            {
                Vector3 p;
                do p = new Vector3((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);
                while (p.sqrMagnitude > 1f);
                p = q * new Vector3(p.x * radius * stretch, p.y * radius * flatten, p.z * radius);
                f._rocks.Add(NewRock(rng, p, 0f, minScale, maxScale));
            }
            return f;
        }

        private static AsteroidField Make(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<AsteroidField>();
        }

        private static Rock NewRock(System.Random rng, Vector3 pos, float orbit, float minScale, float maxScale)
        {
            int meshes = Mathf.Max(1, AsteroidAssets.Count);
            // Мелких камней больше, крупных — единицы
            float s = Mathf.Lerp(minScale, maxScale, Mathf.Pow((float)rng.NextDouble(), 2.2f));
            var axis = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f);
            return new Rock
            {
                Mesh = rng.Next(meshes),
                Pos = pos,
                Orbit = orbit,
                Axis = axis.sqrMagnitude > 1e-4f ? axis.normalized : Vector3.up,
                Spin = Mathf.Lerp(4f, 22f, (float)rng.NextDouble()) * (rng.NextDouble() < 0.5 ? -1f : 1f),
                Scale = s,
                Rot0 = Quaternion.Euler((float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f)
            };
        }

        private void LateUpdate()
        {
            if (_rocks.Count == 0 || !AsteroidAssets.Ready) return;
            if (VisibleWhen != null && !VisibleWhen()) return;

            int meshes = AsteroidAssets.Count;
            if (_buffers == null || _buffers.Length != meshes)
            {
                _buffers = new Matrix4x4[meshes][];
                for (int i = 0; i < meshes; i++) _buffers[i] = new Matrix4x4[1023];
                _counts = new int[meshes];
            }
            for (int i = 0; i < meshes; i++) _counts[i] = 0;

            float t = Time.time;
            var l2w = transform.localToWorldMatrix;
            for (int i = 0; i < _rocks.Count; i++)
            {
                var r = _rocks[i];
                int m = r.Mesh % meshes;
                if (_counts[m] >= 1023) continue;
                Vector3 pos = r.Pos;
                if (r.Orbit != 0f)
                {
                    float a = r.Pos.z + r.Orbit * t;
                    pos = new Vector3(Mathf.Cos(a) * r.Pos.x, r.Pos.y, Mathf.Sin(a) * r.Pos.x);
                }
                var rot = Quaternion.AngleAxis(r.Spin * t, r.Axis) * r.Rot0;
                _buffers[m][_counts[m]++] = l2w * Matrix4x4.TRS(pos, rot, Vector3.one * r.Scale) * AsteroidAssets.PivotAt(m);
            }

            int layer = gameObject.layer;
            for (int m = 0; m < meshes; m++)
                if (_counts[m] > 0)
                    Graphics.DrawMeshInstanced(AsteroidAssets.MeshAt(m), 0, AsteroidAssets.MaterialAt(m), _buffers[m], _counts[m],
                                               null, ShadowCastingMode.Off, false, layer);
        }
    }
}
