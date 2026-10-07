using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Процедурные модели кораблей для карты галактики: низкополигональные силуэты с плоским
    /// затенением, которые читаются сверху — клин военного корабля с крыльями и рубкой, тягач
    /// строителя с контейнерами и краном, научное судно с кольцом-сенсором и тарелкой.
    /// Три материала: металл корпуса, полосы цвета владельца, свечение (двигатели, окна).
    /// Плюс мигающие навигационные огни. Меши общие на тип/класс, материалы — на корабль.
    /// Локальные оси: +Z — нос, +Y — верх; длина ~3 единицы (под коллайдер FleetView).
    /// </summary>
    public static class ShipMeshFactory
    {
        public sealed class ShipVisual
        {
            public Material Hull, Accent, Glow;
            public Renderer Nav;
            public Color AccentColor, GlowColor;

            public void SetSelected(bool on)
            {
                var a = on ? new Color(1f, 0.95f, 0.35f) : AccentColor;
                Set(Accent, a, a * (on ? 1.2f : 0.45f));
            }

            /// <summary>Мигание навигационных огней (вызывать каждый кадр).</summary>
            public void Tick(float t)
            {
                if (Nav != null) Nav.enabled = Mathf.Repeat(t, 1.6f) < 0.12f || Mathf.Repeat(t - 0.25f, 1.6f) < 0.08f;
            }
        }

        private const int SubHull = 0, SubAccent = 1, SubGlow = 2;
        private static readonly Dictionary<string, Mesh> s_Meshes = new Dictionary<string, Mesh>();
        private static Mesh s_NavMesh;

        public static ShipVisual Build(Transform parent, FleetType type, ShipClass hull, Color owner)
        {
            var mesh = GetMesh(type, hull);
            var go = new GameObject("ShipModel");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var v = new ShipVisual
            {
                AccentColor = Color.Lerp(owner, Color.white, 0.1f),
                GlowColor = type == FleetType.Constructor ? new Color(1f, 0.72f, 0.35f) : new Color(0.55f, 0.9f, 1f)
            };
            // Светлый металл с лёгкой подсветкой — корабль не теряется на тёмном фоне космоса
            var metal = Color.Lerp(new Color(0.56f, 0.6f, 0.64f), owner, 0.10f);
            v.Hull = LitMat(metal, metal * 0.12f, 0.35f, 0.55f);
            v.Accent = LitMat(v.AccentColor, v.AccentColor * 0.45f, 0.3f, 0.6f);
            v.Glow = GlowMat(v.GlowColor);
            mr.sharedMaterials = new[] { v.Hull, v.Accent, v.Glow };

            // Навигационные огни: красный слева, зелёный справа, мигают
            var nav = new GameObject("NavLights");
            nav.transform.SetParent(go.transform, false);
            nav.AddComponent<MeshFilter>().sharedMesh = NavMesh(type, hull);
            var nr = nav.AddComponent<MeshRenderer>();
            nr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            nr.sharedMaterial = GlowMat(new Color(1f, 0.92f, 0.85f));
            v.Nav = nr;
            return v;
        }

        // ================================================================ МОДЕЛИ

        private static Mesh GetMesh(FleetType type, ShipClass hull)
        {
            string key = type == FleetType.Military ? $"mil_{hull}" : type.ToString();
            if (s_Meshes.TryGetValue(key, out var m) && m != null) return m;
            var b = new Builder();
            switch (type)
            {
                case FleetType.Constructor: Constructor(b); break;
                case FleetType.Science: Science(b); break;
                default: Military(b, hull); break;
            }
            m = b.ToMesh("Ship_" + key);
            s_Meshes[key] = m;
            return m;
        }

        /// <summary>Клин с крыльями, рубкой и двигателями; фрегат и эсминец крупнее и вооружённее.</summary>
        private static void Military(Builder b, ShipClass c)
        {
            float s = c == ShipClass.Destroyer ? 1.18f : c == ShipClass.Frigate ? 1.08f : 1f;
            float len = 2.5f * s;

            // Корпус: острый нос, широкая корма
            b.Frustum(SubHull, new Vector3(0, 0, 0.15f * s), new Vector2(0.95f * s, 0.42f), new Vector2(0.12f, 0.14f), len);
            // Кормовой блок
            b.Box(SubHull, new Vector3(0, 0.02f, -1.15f * s), new Vector3(1.05f * s, 0.5f, 0.6f));
            // Крылья, отведённые назад
            for (int side = -1; side <= 1; side += 2)
            {
                b.Box(SubHull, new Vector3(side * 0.82f * s, -0.04f, -0.55f * s), new Vector3(1.0f * s, 0.07f, 0.55f), Quaternion.Euler(0, side * 24f, 0));
                b.Box(SubAccent, new Vector3(side * 1.2f * s, -0.01f, -0.73f * s), new Vector3(0.28f, 0.05f, 0.22f), Quaternion.Euler(0, side * 24f, 0));
            }
            // Рубка с окнами
            b.Box(SubHull, new Vector3(0, 0.3f, -0.55f * s), new Vector3(0.36f, 0.26f, 0.55f));
            b.Box(SubGlow, new Vector3(0, 0.36f, -0.275f * s), new Vector3(0.28f, 0.05f, 0.02f));
            // Полоса цвета владельца вдоль хребта — по наклону верхней грани
            b.Box(SubAccent, new Vector3(0, 0.16f, 0f), new Vector3(0.1f, 0.02f, 1.6f * s), Quaternion.Euler(3.2f, 0, 0));
            // Двигатели
            int engines = c == ShipClass.Destroyer ? 3 : 2;
            for (int i = 0; i < engines; i++)
            {
                float x = engines == 2 ? (i == 0 ? -0.3f : 0.3f) * s : (i - 1) * 0.36f * s;
                b.Prism(SubHull, new Vector3(x, 0.02f, -1.55f * s), 0.16f, 0.5f, 8);
                b.Prism(SubGlow, new Vector3(x, 0.02f, -1.81f * s), 0.12f, 0.04f, 8);
            }
            // Орудия: у фрегата — башня, у эсминца — две и боковые модули
            if (c != ShipClass.Corvette)
            {
                b.Box(SubHull, new Vector3(0, 0.19f, 0.35f * s), new Vector3(0.26f, 0.12f, 0.3f));
                b.Box(SubHull, new Vector3(0, 0.21f, 0.62f * s), new Vector3(0.06f, 0.06f, 0.45f));
            }
            if (c == ShipClass.Destroyer)
            {
                b.Box(SubHull, new Vector3(0, 0.15f, 0.85f * s), new Vector3(0.22f, 0.1f, 0.24f));
                for (int side = -1; side <= 1; side += 2)
                    b.Frustum(SubHull, new Vector3(side * 0.62f * s, 0f, -0.2f * s), new Vector2(0.24f, 0.22f), new Vector2(0.1f, 0.1f), 1.1f);
            }
        }

        /// <summary>Тягач: кабина, хребет, грузовые контейнеры, кран и блок двигателей.</summary>
        private static void Constructor(Builder b)
        {
            // Кабина с носом и окнами
            b.Box(SubHull, new Vector3(0, 0.05f, 0.95f), new Vector3(0.9f, 0.5f, 0.55f));
            b.Frustum(SubHull, new Vector3(0, 0.0f, 1.38f), new Vector2(0.9f, 0.45f), new Vector2(0.55f, 0.22f), 0.32f);
            b.Box(SubGlow, new Vector3(0, 0.265f, 1.23f), new Vector3(0.62f, 0.06f, 0.02f));
            // Хребет
            b.Box(SubHull, new Vector3(0, 0f, 0f), new Vector3(0.32f, 0.28f, 1.9f));
            // Контейнеры по бокам (цвет владельца)
            for (int side = -1; side <= 1; side += 2)
            for (int row = 0; row < 2; row++)
            {
                var c = new Vector3(side * 0.42f, 0.02f, 0.28f - row * 0.62f);
                b.Box(SubAccent, c, new Vector3(0.46f, 0.4f, 0.54f));
                b.Box(SubHull, c + new Vector3(side * 0.235f, 0, 0), new Vector3(0.02f, 0.3f, 0.44f));
            }
            // Кран: стойка, стрела, захват
            b.Box(SubHull, new Vector3(0, 0.32f, 0.55f), new Vector3(0.16f, 0.36f, 0.16f));
            b.Box(SubHull, new Vector3(0, 0.67f, 0.03f), new Vector3(0.09f, 0.09f, 1.1f), Quaternion.Euler(18f, 0, 0));
            b.Box(SubAccent, new Vector3(0, 0.8f, -0.5f), new Vector3(0.26f, 0.06f, 0.1f));
            // Двигательный блок
            b.Box(SubHull, new Vector3(0, 0.02f, -1.12f), new Vector3(0.95f, 0.46f, 0.42f));
            for (int side = -1; side <= 1; side += 2)
            {
                b.Prism(SubHull, new Vector3(side * 0.28f, 0.02f, -1.42f), 0.17f, 0.22f, 8);
                b.Prism(SubGlow, new Vector3(side * 0.28f, 0.02f, -1.55f), 0.13f, 0.04f, 8);
            }
        }

        /// <summary>Научное судно: тонкий корпус, кольцо-сенсор, тарелка на мачте, антенны.</summary>
        private static void Science(Builder b)
        {
            b.Frustum(SubHull, new Vector3(0, 0, 0.2f), new Vector2(0.5f, 0.38f), new Vector2(0.16f, 0.16f), 2.4f);
            b.Box(SubHull, new Vector3(0, 0, -1.1f), new Vector3(0.6f, 0.42f, 0.5f));
            b.Box(SubGlow, new Vector3(0, 0.12f, 0.95f), new Vector3(0.18f, 0.04f, 0.3f));
            // Кольцо-сенсор: горизонтальный ореол вокруг корпуса — с камеры карты виден кругом
            const int seg = 20;
            const float r = 0.8f;
            var ringC = new Vector3(0, 0, -0.3f);
            for (int i = 0; i < seg; i++)
            {
                float a = (i + 0.5f) / seg * Mathf.PI * 2f;
                var p = ringC + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                float yaw = -a * Mathf.Rad2Deg;               // локальная X смотрит от центра
                b.Box(SubAccent, p, new Vector3(0.12f, 0.06f, 2f * Mathf.PI * r / seg * 0.96f), Quaternion.Euler(0, yaw, 0));
            }
            // Опоры кольца — крестом
            b.Box(SubHull, ringC, new Vector3(1.6f, 0.04f, 0.06f));
            b.Box(SubHull, ringC, new Vector3(0.06f, 0.04f, 1.6f));
            // Мачта и тарелка, смотрящая вперёд-вверх
            b.Box(SubHull, new Vector3(0, 0.35f, 0.35f), new Vector3(0.06f, 0.42f, 0.06f));
            b.Dish(SubAccent, new Vector3(0, 0.58f, 0.38f), 0.42f, 0.14f, 12, Quaternion.Euler(-55f, 0, 0));
            b.Box(SubGlow, new Vector3(0, 0.7f, 0.46f), new Vector3(0.05f, 0.05f, 0.05f));
            // Антенны
            for (int side = -1; side <= 1; side += 2)
                b.Box(SubHull, new Vector3(side * 0.12f, 0.05f, 1.75f), new Vector3(0.025f, 0.025f, 0.9f), Quaternion.Euler(0, side * 6f, 0));
            // Двигатель
            b.Prism(SubHull, new Vector3(0, 0, -1.45f), 0.18f, 0.25f, 8);
            b.Prism(SubGlow, new Vector3(0, 0, -1.6f), 0.14f, 0.04f, 8);
        }

        /// <summary>Огоньки на концах крыльев / бортах и на корме.</summary>
        private static Mesh NavMesh(FleetType type, ShipClass hull)
        {
            var b = new Builder();
            float s = type == FleetType.Military ? (hull == ShipClass.Destroyer ? 1.18f : hull == ShipClass.Frigate ? 1.08f : 1f) : 1f;
            float x = type == FleetType.Military ? 1.3f * s : type == FleetType.Science ? 0.8f : 0.7f;
            float z = type == FleetType.Military ? -0.8f * s : -0.35f;
            b.Box(0, new Vector3(-x, 0.02f, z), Vector3.one * 0.07f);
            b.Box(0, new Vector3(x, 0.02f, z), Vector3.one * 0.07f);
            b.Box(0, new Vector3(0, 0.28f, type == FleetType.Military ? -1.4f * s : -1.3f), Vector3.one * 0.06f);
            return b.ToMesh("ShipNav_" + type + hull, 1);
        }

        // ================================================================ МАТЕРИАЛЫ

        private static Material LitMat(Color baseCol, Color emission, float metallic, float smooth)
        {
            var m = new Material(ShaderCache.Lit) { hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            Set(m, baseCol, emission);
            return m;
        }

        private static Material GlowMat(Color c)
        {
            var m = new Material(ShaderCache.Unlit) { hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            return m;
        }

        private static void Set(Material m, Color baseCol, Color emission)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", baseCol);
            if (m.HasProperty("_Color")) m.SetColor("_Color", baseCol);
            if (m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission);
            }
        }

        // ================================================================ ПОСТРОИТЕЛЬ

        /// <summary>Собирает меш из примитивов с плоскими нормалями; подмеш = материал.</summary>
        private sealed class Builder
        {
            private readonly List<Vector3> _v = new List<Vector3>();
            private readonly List<Vector3> _n = new List<Vector3>();
            private readonly List<int>[] _t = { new List<int>(), new List<int>(), new List<int>() };

            private void Tri(int sub, Vector3 a, Vector3 b, Vector3 c)
            {
                var n = Vector3.Cross(b - a, c - a).normalized;
                int i = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c);
                _n.Add(n); _n.Add(n); _n.Add(n);
                _t[sub].Add(i); _t[sub].Add(i + 1); _t[sub].Add(i + 2);
            }

            private void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Tri(sub, a, b, c);
                Tri(sub, a, c, d);
            }

            public void Box(int sub, Vector3 c, Vector3 size, Quaternion? rot = null)
                => Frustum(sub, c, new Vector2(size.x, size.y), new Vector2(size.x, size.y), size.z, rot);

            /// <summary>Усечённая пирамида вдоль Z: сечение back (корма) → front (нос).</summary>
            public void Frustum(int sub, Vector3 c, Vector2 back, Vector2 front, float length, Quaternion? rot = null)
            {
                var q = rot ?? Quaternion.identity;
                float z0 = -length * 0.5f, z1 = length * 0.5f;
                Vector3 b0 = P(c, q, -back.x / 2, -back.y / 2, z0), b1 = P(c, q, back.x / 2, -back.y / 2, z0),
                        b2 = P(c, q, back.x / 2, back.y / 2, z0), b3 = P(c, q, -back.x / 2, back.y / 2, z0);
                Vector3 f0 = P(c, q, -front.x / 2, -front.y / 2, z1), f1 = P(c, q, front.x / 2, -front.y / 2, z1),
                        f2 = P(c, q, front.x / 2, front.y / 2, z1), f3 = P(c, q, -front.x / 2, front.y / 2, z1);
                Quad(sub, b0, b3, b2, b1);   // корма
                Quad(sub, f0, f1, f2, f3);   // нос
                Quad(sub, b3, f3, f2, b2);   // верх
                Quad(sub, b0, b1, f1, f0);   // низ
                Quad(sub, b0, f0, f3, b3);   // левый борт
                Quad(sub, b1, b2, f2, f1);   // правый борт
            }

            private static Vector3 P(Vector3 c, Quaternion q, float x, float y, float z) => c + q * new Vector3(x, y, z);

            /// <summary>Многогранная призма вдоль Z.</summary>
            public void Prism(int sub, Vector3 c, float radius, float length, int sides)
            {
                float z0 = -length * 0.5f, z1 = length * 0.5f;
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i / (float)sides * Mathf.PI * 2f, a1 = (i + 1) / (float)sides * Mathf.PI * 2f;
                    var p0 = new Vector3(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius, 0);
                    var p1 = new Vector3(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius, 0);
                    Vector3 back = c + Vector3.forward * z0, front = c + Vector3.forward * z1;
                    Quad(sub, back + p0, back + p1, front + p1, front + p0);
                    Tri(sub, back, back + p1, back + p0);
                    Tri(sub, front, front + p0, front + p1);
                }
            }

            /// <summary>Неглубокая тарелка (конус раскрытием вдоль +Z локально), двусторонняя.</summary>
            public void Dish(int sub, Vector3 c, float radius, float depth, int sides, Quaternion rot)
            {
                var tip = c + rot * new Vector3(0, 0, -depth);
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i / (float)sides * Mathf.PI * 2f, a1 = (i + 1) / (float)sides * Mathf.PI * 2f;
                    var p0 = c + rot * new Vector3(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius, 0);
                    var p1 = c + rot * new Vector3(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius, 0);
                    Tri(sub, tip, p0, p1);
                    Tri(sub, tip, p1, p0);
                }
            }

            public Mesh ToMesh(string name, int subMeshes = 3)
            {
                var m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
                m.SetVertices(_v);
                m.SetNormals(_n);
                m.subMeshCount = subMeshes;
                for (int i = 0; i < subMeshes; i++) m.SetTriangles(_t[i], i);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
