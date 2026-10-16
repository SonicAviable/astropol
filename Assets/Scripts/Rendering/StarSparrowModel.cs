using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Боевые и строительные корабли из набора Star Sparrow (Resources/Models/Ships/StarSparrow): у каждой фракции свой
    /// стиль — три разных корпуса на корвет, фрегат и эсминец; окраска — шейдер набора «Colorize»,
    /// основной цвет = цвет владельца. Если шейдер не поддерживается (не URP) или моделей нет —
    /// возвращает null, и остаётся «Харбингер».
    ///
    /// Ориентация и сопла считаются по вершинам самой модели: нос — узкий конец, сопла — самые задние
    /// вершины, сгруппированные по ширине.
    /// </summary>
    public static class StarSparrowModel
    {
        private const string Folder = "Models/Ships/StarSparrow/";

        /// <summary>Номера моделей набора: корвет, фрегат, эсминец.</summary>
        private static readonly Dictionary<string, int[]> Styles = new Dictionary<string, int[]>
        {
            ["astrea"]  = new[] { 12, 14, 2 },
            ["xarn"]    = new[] { 33, 34, 40 },
            ["aquila"]  = new[] { 25, 16, 1 },
            ["iridia"]  = new[] { 31, 21, 26 },
            ["terraan"] = new[] { 37, 27, 17 },
        };
        /// <summary>Пираты, угрозы и империи без известной фракции.</summary>
        private static readonly int[] Neutral = { 13, 7, 5 };
        /// <summary>Строительный корабль: корпус с грузовым модулем и посадочными опорами — один на все фракции.</summary>
        private const int Constructor = 6;
        /// <summary>Рабочая окраска строителей — янтарь; цвет владельца идёт полосами.</summary>
        private static readonly Color WorkAmber = new Color(0.86f, 0.58f, 0.16f);

        /// <summary>Наибольший размах крыла в единицах карты — чтобы широкие корпуса не вылезали за коллайдер.</summary>
        private const float MaxWidth = 3.8f;

        private static Material s_base;
        private static bool s_checked;
        private static readonly Dictionary<int, GameObject> s_prefabs = new Dictionary<int, GameObject>();

        public static bool Available
        {
            get
            {
                if (!s_checked)
                {
                    s_checked = true;
                    s_base = Resources.Load<Material>(Folder + "Materials/StarSparrow_Grey");
                    var sh = s_base != null ? s_base.shader : null;
                    if (sh == null || !sh.isSupported || sh.name.Contains("InternalError"))
                    {
                        if (s_base != null) Debug.LogWarning("[StarSparrow] Шейдер набора не поддерживается — военные корабли остаются «Харбингером».");
                        s_base = null;
                    }
                }
                return s_base != null;
            }
        }

        private static int ModelFor(ShipClass hull, int ownerId)
        {
            string key = FactionRegistry.KeyOf(FactionTraits.FactionOf(ownerId));
            var set = key != null && Styles.TryGetValue(key, out var s) ? s : Neutral;
            return hull == ShipClass.Destroyer ? set[2] : hull == ShipClass.Frigate ? set[1] : set[0];
        }

        private static GameObject Prefab(int n)
        {
            if (s_prefabs.TryGetValue(n, out var p)) return p;
            p = Resources.Load<GameObject>(Folder + "Meshes/StarSparrow" + n);
            s_prefabs[n] = p;
            return p;
        }

        /// <summary>Собрать модель под parent; null — набора нет или шейдер не работает.</summary>
        public static ShipMeshFactory.ShipVisual Build(Transform parent, Color owner, ShipClass hull, int ownerId, float length)
            => Available ? Make(parent, ModelFor(hull, ownerId), owner, ownerId, length, false) : null;

        /// <summary>Строительный корабль: янтарный корпус, полосы и эмблемы — цвет владельца, оранжевые сопла.</summary>
        public static ShipMeshFactory.ShipVisual BuildConstructor(Transform parent, Color owner, int ownerId, float length)
            => Available ? Make(parent, Constructor, owner, ownerId, length, true) : null;

        private static ShipMeshFactory.ShipVisual Make(Transform parent, int modelNo, Color owner, int ownerId, float length, bool civilian)
        {
            var prefab = Prefab(modelNo);
            if (prefab == null) return null;

            var root = new GameObject("ShipModel").transform;
            root.SetParent(parent, false);
            var pivot = new GameObject("StarSparrow").transform;
            pivot.SetParent(root, false);

            var model = Object.Instantiate(prefab, pivot, false);
            model.name = "StarSparrowModel";
            foreach (var c in model.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            foreach (var l in model.GetComponentsInChildren<Light>()) Object.Destroy(l);
            foreach (var cam in model.GetComponentsInChildren<Camera>()) Object.Destroy(cam);

            var mr = model.GetComponentInChildren<MeshRenderer>();
            var mf = mr != null ? mr.GetComponent<MeshFilter>() : null;
            if (mr == null || mf == null || mf.sharedMesh == null)
            {
                Object.Destroy(root.gameObject);
                return null;
            }
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var v = new ShipMeshFactory.ShipVisual
            {
                AccentColor = civilian ? WorkAmber : Color.Lerp(owner, Color.white, 0.1f),
                GlowColor = civilian ? new Color(1f, 0.72f, 0.35f) : new Color(0.55f, 0.9f, 1f),
            };
            // Свой материал у каждого корабля: основной цвет при выборе желтеет (ShipVisual.SetSelected)
            var mat = new Material(s_base) { name = "StarSparrow_" + ownerId, hideFlags = HideFlags.DontSave };
            if (civilian) PaintCivilian(mat, owner); else Paint(mat, v.AccentColor, owner);
            var mats = new Material[Mathf.Max(1, mr.sharedMaterials.Length)];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            mr.sharedMaterials = mats;
            v.Hull = mat;
            v.Accent = mat;
            v.Glow = ShipMeshFactory.GlowMat(v.GlowColor * 2.4f);

            var verts = mf.sharedMesh.isReadable ? mf.sharedMesh.vertices : null;
            Fit(root, pivot, mr, mf.sharedMesh, verts, length);
            FindNozzles(v, root, mr, verts, length);
            return v;
        }

        /// <summary>Основной цвет — владелец, второй — тёмный металл с оттенком владельца, третий — светлые панели.</summary>
        private static void Paint(Material m, Color primary, Color owner)
        {
            SetColor(m, "_Color1", primary);
            SetColor(m, "_Color2", Color.Lerp(new Color(0.16f, 0.17f, 0.19f), owner, 0.15f));
            SetColor(m, "_Color3", new Color(0.72f, 0.74f, 0.77f));
            SetColor(m, "_LogosColor", Color.Lerp(owner, Color.white, 0.55f));
        }

        /// <summary>Строитель: янтарный корпус, второй цвет — владелец (полосы), светлые панели, оранжевые огни.</summary>
        private static void PaintCivilian(Material m, Color owner)
        {
            SetColor(m, "_Color1", WorkAmber);
            SetColor(m, "_Color2", Color.Lerp(owner, Color.black, 0.25f));
            SetColor(m, "_Color3", new Color(0.7f, 0.7f, 0.68f));
            SetColor(m, "_LogosColor", Color.Lerp(owner, Color.white, 0.55f));
            var glow = new Color(1f, 0.62f, 0.22f);
            SetColor(m, "_Emission1", glow * 0.25f);
            SetColor(m, "_Emission2", glow * 0.7f);
            SetColor(m, "_Emission3", glow);
        }

        private static void SetColor(Material m, string prop, Color c)
        {
            if (m.HasProperty(prop)) m.SetColor(prop, c);
        }

        // ================================================================ Ориентация, размер, сопла

        private static void Fit(Transform root, Transform pivot, Renderer mr, Mesh mesh, Vector3[] verts, float length)
        {
            // Нос — узкий конец: сравниваем ширину передних и задних 15% длины
            if (verts != null && verts.Length > 0)
            {
                var pts = ToRoot(root, mr, verts);
                float zMin = float.MaxValue, zMax = float.MinValue;
                foreach (var p in pts) { zMin = Mathf.Min(zMin, p.z); zMax = Mathf.Max(zMax, p.z); }
                float band = (zMax - zMin) * 0.15f;
                if (Width(pts, zMax - band, zMax) > Width(pts, zMin, zMin + band))
                    pivot.localRotation = Quaternion.Euler(0f, 180f, 0f) * pivot.localRotation;
            }

            var b = BoundsIn(root, mr, mesh.bounds);
            float scale = Mathf.Min(length / Mathf.Max(b.size.z, 0.0001f), MaxWidth / Mathf.Max(b.size.x, 0.0001f));
            pivot.localScale *= scale;
            b = BoundsIn(root, mr, mesh.bounds);
            pivot.localPosition -= b.center;
        }

        private static float Width(List<Vector3> pts, float z0, float z1)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (var p in pts)
                if (p.z >= z0 && p.z <= z1) { min = Mathf.Min(min, p.x); max = Mathf.Max(max, p.x); }
            return max > min ? max - min : 0f;
        }

        /// <summary>Сопла — самые задние вершины, разбитые на группы по ширине (до четырёх двигателей).</summary>
        private static void FindNozzles(ShipMeshFactory.ShipVisual v, Transform root, Renderer mr, Vector3[] verts, float length)
        {
            var list = new List<Vector3>();
            float radius = 0.08f * length / 3f;
            if (verts != null && verts.Length > 0)
            {
                var pts = ToRoot(root, mr, verts);
                float zMin = float.MaxValue, zMax = float.MinValue;
                foreach (var p in pts) { zMin = Mathf.Min(zMin, p.z); zMax = Mathf.Max(zMax, p.z); }
                float len = zMax - zMin;
                var rear = pts.FindAll(p => p.z <= zMin + len * 0.04f);
                rear.Sort((a, b) => a.x.CompareTo(b.x));

                var groups = new List<List<Vector3>>();
                foreach (var p in rear)
                {
                    if (groups.Count == 0 || p.x - groups[groups.Count - 1][groups[groups.Count - 1].Count - 1].x > len * 0.05f)
                        groups.Add(new List<Vector3>());
                    groups[groups.Count - 1].Add(p);
                }
                groups.Sort((a, b) => b.Count.CompareTo(a.Count));
                float rSum = 0f;
                for (int i = 0; i < groups.Count && i < 4; i++)
                {
                    float x0 = float.MaxValue, x1 = float.MinValue, y = 0f;
                    foreach (var p in groups[i]) { x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); y += p.y; }
                    list.Add(new Vector3((x0 + x1) * 0.5f, y / groups[i].Count, zMin));
                    rSum += Mathf.Clamp((x1 - x0) * 0.5f, 0.03f, 0.22f);
                }
                if (list.Count > 0) radius = rSum / list.Count;
            }
            if (list.Count == 0) list.Add(new Vector3(0f, 0f, -length * 0.5f));
            v.Nozzles = list.ToArray();
            v.NozzleRadius = radius;
            float side = length * 0.12f;
            v.RetroNozzles = new[] { new Vector3(-side, 0f, length * 0.2f), new Vector3(side, 0f, length * 0.2f) };
            v.RetroRadius = radius * 0.5f;
        }

        private static List<Vector3> ToRoot(Transform root, Renderer mr, Vector3[] verts)
        {
            var m = root.worldToLocalMatrix * mr.localToWorldMatrix;
            var pts = new List<Vector3>(verts.Length);
            foreach (var p in verts) pts.Add(m.MultiplyPoint3x4(p));
            return pts;
        }

        private static Bounds BoundsIn(Transform root, Renderer mr, Bounds local)
        {
            var m = root.worldToLocalMatrix * mr.localToWorldMatrix;
            Vector3 c = local.center, e = local.extents;
            var b = new Bounds(m.MultiplyPoint3x4(c), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                b.Encapsulate(m.MultiplyPoint3x4(corner));
            }
            return b;
        }
    }
}
