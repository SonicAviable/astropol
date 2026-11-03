using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Rendering;
using Object = UnityEngine.Object;

namespace StellarisClone.Station3D
{
    /// <summary>
    /// Строит интерьер 3D-станции далеко от галактики, на своём слое: помещение из кита Creepy Cat
    /// (или из примитивов, если кита нет), свет, пульты по кругу и голограмму столицы в центре.
    /// Размеры зала меряются лучами по коллайдерам — расстановка не зависит от пивотов модулей кита.
    /// </summary>
    public static class StationBuilder
    {
        public const int Layer = 31;
        public static readonly Vector3 Origin = new Vector3(0f, -20000f, 0f);

        public sealed class Result
        {
            public GameObject Root;
            public Vector3 Spawn;
            public float SpawnYaw;
            public float FloorY;
            public Bounds Area;
            public bool FromKit;
            public readonly List<StationTerminal> Terminals = new List<StationTerminal>();
            public readonly List<Material> Materials = new List<Material>();
            public Transform Holo;
        }

        private static Result s_res;
        private static Material s_grey;

        public static Result Build()
        {
            var res = s_res = new Result();
            s_grey = null;
            var root = new GameObject("[Station3D]");
            root.transform.SetPositionAndRotation(Origin, Quaternion.identity);
            res.Root = root;

            Bounds full, room;
            var kit = Station3DKit.Load();
            GameObject inst = null;
            if (kit != null)
            {
                try
                {
                    inst = Object.Instantiate(kit.Room, root.transform);
                    inst.transform.localPosition = Vector3.zero;
                    inst.transform.localRotation = Quaternion.identity;
                    PrepareKitObject(inst, kit);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Station3D] Не удалось собрать станцию из кита: " + e.Message);
                    if (inst != null) Object.Destroy(inst);
                    inst = null;
                }
            }

            if (inst != null && TryBounds(inst.transform, out full))
            {
                var roomT = FindDeep(inst.transform, "Simple_Room_01");
                if (roomT == null || !TryBounds(roomT, out room)) room = full;
                res.FromKit = true;
            }
            else
            {
                if (inst != null) Object.Destroy(inst);
                BuildFallbackRoom(root.transform, out full, out room);
            }

            SetLayer(root, Layer);
            Physics.SyncTransforms();
            int mask = 1 << Layer;

            // Пол и потолок
            float floorY = room.min.y;
            var probe = new Vector3(room.center.x, room.min.y + 2f, room.center.z);
            if (Physics.Raycast(probe, Vector3.down, out var hit, 3f, mask, QueryTriggerInteraction.Ignore)) floorY = hit.point.y;
            float ceilY = room.max.y;
            if (Physics.Raycast(new Vector3(room.center.x, floorY + 1f, room.center.z), Vector3.up, out hit, 20f, mask, QueryTriggerInteraction.Ignore))
                ceilY = hit.point.y;
            ceilY = Mathf.Max(ceilY, floorY + 2.6f);

            // Стены: лучи из центра на высоте груди
            float eye = floorY + 1.2f;
            var c0 = new Vector3(room.center.x, eye, room.center.z);
            float xp = WallDist(c0, Vector3.right, room.extents.x - 1f), xm = WallDist(c0, Vector3.left, room.extents.x - 1f);
            float zp = WallDist(c0, Vector3.forward, room.extents.z - 1f), zm = WallDist(c0, Vector3.back, room.extents.z - 1f);
            var center = new Vector3(c0.x + (xp - xm) * 0.5f, floorY, c0.z + (zp - zm) * 0.5f);
            float halfX = (xp + xm) * 0.5f, halfZ = (zp + zm) * 0.5f;
            float radius = Mathf.Clamp(Mathf.Min(halfX, halfZ) - 1.2f, 2.6f, 9f);

            // Страховка: невидимый пол и ограда по всей площади — из коридоров кита не выпасть
            var safety = new GameObject("Safety").transform;
            safety.SetParent(root.transform, false);
            AddBox(safety, "Floor", new Vector3(full.center.x, floorY - 0.26f, full.center.z), new Vector3(full.size.x + 6f, 0.5f, full.size.z + 6f));
            float h2 = 14f;
            var fc = full.center; var fe = full.extents + new Vector3(0.5f, 0f, 0.5f);
            AddBox(safety, "N", new Vector3(fc.x, floorY + h2 * 0.5f, fc.z + fe.z), new Vector3(fe.x * 2f + 1f, h2, 0.4f));
            AddBox(safety, "S", new Vector3(fc.x, floorY + h2 * 0.5f, fc.z - fe.z), new Vector3(fe.x * 2f + 1f, h2, 0.4f));
            AddBox(safety, "E", new Vector3(fc.x + fe.x, floorY + h2 * 0.5f, fc.z), new Vector3(0.4f, h2, fe.z * 2f + 1f));
            AddBox(safety, "W", new Vector3(fc.x - fe.x, floorY + h2 * 0.5f, fc.z), new Vector3(0.4f, h2, fe.z * 2f + 1f));

            // Свет
            float ly = Mathf.Min(ceilY - 0.5f, floorY + 3.6f);
            AddLight(root.transform, new Vector3(center.x, ly, center.z), radius * 2.4f, 2.0f, new Color(0.78f, 0.9f, 1f));
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                AddLight(root.transform, center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * 0.75f + Vector3.up * (ly - floorY),
                         radius * 1.4f, 1.2f, new Color(0.62f, 0.82f, 1f));
            }
            if (inst != null)
                foreach (Transform t in inst.transform)
                    if (t.name.StartsWith("Corridor") && TryBounds(t, out var cb))
                        AddLight(t, new Vector3(cb.center.x, floorY + 2.8f, cb.center.z), 9f, 1.1f, new Color(0.7f, 0.85f, 1f), worldPos: true);

            // Пульты по кругу лицом к центру, «Столица» — голостол в центре
            var entries = StationActions.All();
            StationActions.Entry capitalEntry = entries.Find(e => e.Icon == LGIcon.Planet);
            if (capitalEntry != null) entries.Remove(capitalEntry);
            int n = entries.Count;
            for (int i = 0; i < n; i++)
            {
                float ang = (i / (float)n) * 360f + 180f / n;
                var dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
                var pos = center + dir * radius;
                var rot = Quaternion.LookRotation(-dir, Vector3.up);
                res.Terminals.Add(BuildTerminal(root.transform, entries[i], pos, rot));
            }
            if (capitalEntry != null) res.Terminals.Add(BuildHoloTable(root.transform, capitalEntry, center, out res.Holo));

            // Декор: ящики по углам зала
            BuildCrates(root.transform, kit, center, halfX, halfZ, floorY);

            SetLayer(root, Layer);

            res.FloorY = floorY;
            res.Area = full;
            res.Spawn = center + new Vector3(0f, 0.05f, -radius * 0.55f);
            res.SpawnYaw = 0f;
            s_res = null;
            return res;
        }

        private static float WallDist(Vector3 from, Vector3 dir, float def) =>
            Physics.Raycast(from, dir, out var h, 60f, 1 << Layer, QueryTriggerInteraction.Ignore) ? h.distance : def;

        public static void Destroy(Result r)
        {
            if (r == null) return;
            foreach (var m in r.Materials) if (m != null) Object.Destroy(m);
            if (r.Root != null) Object.Destroy(r.Root);
        }

        // ---------- Кит ----------

        private static void PrepareKitObject(GameObject go, Station3DKit kit)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = kit.Remap(mats[i]);
                    if (m != null && m.shader != null && !m.shader.isSupported)
                        m = s_grey != null ? s_grey : (s_grey = Mat(new Color(0.35f, 0.38f, 0.42f), 0.6f, 0.5f));
                    if (m != mats[i]) { mats[i] = m; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
            foreach (var c in go.GetComponentsInChildren<Camera>(true)) c.enabled = false;
            foreach (var l in go.GetComponentsInChildren<AudioListener>(true)) l.enabled = false;
        }

        private static void BuildCrates(Transform root, Station3DKit kit, Vector3 c, float hx, float hz, float floorY)
        {
            GameObject crate = kit != null && kit.Props.Count > 0 ? kit.Props[0] : null;
            var metal = Mat(new Color(0.2f, 0.22f, 0.24f), 0.7f, 0.45f);
            var stripe = Unlit(new Color(1f, 0.62f, 0.2f));
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? 1f : -1f, sz = (i & 2) == 0 ? 1f : -1f;
                var pos = new Vector3(c.x + sx * (hx - 1.1f), floorY, c.z + sz * (hz - 1.1f));
                if (crate != null)
                {
                    var g = Object.Instantiate(crate, root);
                    g.transform.position = pos;
                    g.transform.rotation = Quaternion.Euler(0f, i * 37f, 0f);
                    PrepareKitObject(g, kit);
                    if (TryBounds(g.transform, out var b0))
                    {
                        float big = Mathf.Max(b0.size.x, b0.size.y, b0.size.z);
                        if (big > 1.6f) g.transform.localScale *= 1.6f / big;
                        if (TryBounds(g.transform, out b0)) g.transform.position += Vector3.up * (floorY - b0.min.y);
                    }
                    if (g.GetComponentInChildren<Collider>() == null && TryBounds(g.transform, out var b))
                    {
                        var bc = g.AddComponent<BoxCollider>();
                        bc.center = g.transform.InverseTransformPoint(b.center);
                        var ls = g.transform.lossyScale;
                        bc.size = new Vector3(b.size.x / Mathf.Max(0.001f, ls.x), b.size.y / Mathf.Max(0.001f, ls.y), b.size.z / Mathf.Max(0.001f, ls.z));
                    }
                }
                else
                {
                    var g = Part(root, PrimitiveType.Cube, pos + Vector3.up * 0.45f, new Vector3(0.9f, 0.9f, 0.9f), metal, true);
                    g.transform.rotation = Quaternion.Euler(0f, i * 37f, 0f);
                    var s = Part(g.transform, PrimitiveType.Cube, Vector3.zero, Vector3.one, stripe, false);
                    s.transform.localPosition = new Vector3(0f, 0.25f, 0f);
                    s.transform.localScale = new Vector3(1.02f, 0.08f, 1.02f);
                }
            }
        }

        // ---------- Запасной зал из примитивов ----------

        private static void BuildFallbackRoom(Transform root, out Bounds full, out Bounds room)
        {
            const float size = 22f, height = 5f, wall = 0.4f;
            var floorMat = Mat(new Color(0.13f, 0.15f, 0.18f), 0.75f, 0.55f);
            var wallMat = Mat(new Color(0.2f, 0.23f, 0.27f), 0.6f, 0.45f);
            var ribMat = Mat(new Color(0.1f, 0.11f, 0.13f), 0.85f, 0.6f);
            var glow = Unlit(new Color(0.25f, 0.85f, 1f));
            var panel = Unlit(new Color(0.85f, 0.93f, 1f));
            var space = Unlit(new Color(0.01f, 0.015f, 0.04f));
            var star = Unlit(Color.white);

            var o = root.position;
            Part(root, PrimitiveType.Cube, o + new Vector3(0f, -0.1f, 0f), new Vector3(size, 0.2f, size), floorMat, true);
            Part(root, PrimitiveType.Cube, o + new Vector3(0f, height + 0.1f, 0f), new Vector3(size, 0.2f, size), wallMat, true);
            for (int i = 0; i < 4; i++)
            {
                var rot = Quaternion.Euler(0f, i * 90f, 0f);
                var fwd = rot * Vector3.forward;
                var right = rot * Vector3.right;
                var wc = o + fwd * (size * 0.5f + wall * 0.5f) + Vector3.up * height * 0.5f;
                var w = Part(root, PrimitiveType.Cube, wc, new Vector3(size + wall * 2f, height, wall), wallMat, true);
                w.transform.rotation = rot;
                // Светящиеся полосы у пола и под потолком
                for (int k = 0; k < 2; k++)
                {
                    var s = Part(root, PrimitiveType.Cube, o + fwd * (size * 0.5f - 0.03f) + Vector3.up * (k == 0 ? 0.18f : height - 0.5f),
                                 new Vector3(size, 0.05f, 0.04f), glow, false);
                    s.transform.rotation = rot;
                }
                // Рёбра жёсткости
                for (int k = -3; k <= 3; k++)
                {
                    var r = Part(root, PrimitiveType.Cube, o + fwd * (size * 0.5f - 0.15f) + right * (k * 3f) + Vector3.up * height * 0.5f,
                                 new Vector3(0.35f, height, 0.3f), ribMat, true);
                    r.transform.rotation = rot;
                }
                // Иллюминатор с видом на космос на одной стене
                if (i == 0)
                {
                    var win = Part(root, PrimitiveType.Cube, o + fwd * (size * 0.5f - 0.02f) + Vector3.up * 2.4f, new Vector3(8f, 2.2f, 0.02f), space, false);
                    win.transform.rotation = rot;
                    var rnd = new System.Random(7);
                    for (int s = 0; s < 40; s++)
                    {
                        var p = o + fwd * (size * 0.5f - 0.035f) + right * ((float)rnd.NextDouble() * 7.6f - 3.8f) + Vector3.up * (1.4f + (float)rnd.NextDouble() * 2f);
                        float d = 0.02f + (float)rnd.NextDouble() * 0.03f;
                        var st = Part(root, PrimitiveType.Cube, p, new Vector3(d, d, 0.01f), star, false);
                        st.transform.rotation = rot;
                    }
                }
            }
            // Световые панели на потолке
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                    if (x != 0 || z != 0)
                        Part(root, PrimitiveType.Cube, o + new Vector3(x * 6f, height - 0.02f, z * 6f), new Vector3(2.4f, 0.04f, 0.8f), panel, false);
            // Решётка на полу
            for (int k = -5; k <= 5; k++)
            {
                Part(root, PrimitiveType.Cube, o + new Vector3(k * 2f, 0.003f, 0f), new Vector3(0.03f, 0.006f, size), ribMat, false);
                Part(root, PrimitiveType.Cube, o + new Vector3(0f, 0.003f, k * 2f), new Vector3(size, 0.006f, 0.03f), ribMat, false);
            }
            room = new Bounds(o + Vector3.up * height * 0.5f, new Vector3(size, height, size));
            full = new Bounds(room.center, room.size + new Vector3(wall * 2f, 0.4f, wall * 2f));
        }

        // ---------- Пульт ----------

        private static StationTerminal BuildTerminal(Transform root, StationActions.Entry e, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject("Terminal_" + e.Title);
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(pos, rot);
            var t = go.transform;

            var metal = Mat(new Color(0.16f, 0.18f, 0.21f), 0.8f, 0.55f);
            var dark = Mat(new Color(0.07f, 0.08f, 0.1f), 0.6f, 0.7f);
            var glow = Unlit(e.Color);
            var dim = Unlit(e.Color * 0.35f + new Color(0f, 0f, 0f, 1f));

            Local(t, PrimitiveType.Cube, new Vector3(0f, 0.45f, -0.05f), new Vector3(0.9f, 0.9f, 0.5f), Quaternion.identity, metal);
            Local(t, PrimitiveType.Cube, new Vector3(0f, 0.95f, 0.05f), new Vector3(1.0f, 0.06f, 0.6f), Quaternion.Euler(14f, 0f, 0f), dark);
            Local(t, PrimitiveType.Cube, new Vector3(0f, 0.9f, 0.34f), new Vector3(0.96f, 0.025f, 0.03f), Quaternion.identity, glow);
            Local(t, PrimitiveType.Cube, new Vector3(0f, 0.2f, 0.21f), new Vector3(0.8f, 0.02f, 0.02f), Quaternion.identity, glow);
            Local(t, PrimitiveType.Cube, new Vector3(0f, 1.45f, -0.24f), new Vector3(0.12f, 1.1f, 0.1f), Quaternion.identity, metal);
            Local(t, PrimitiveType.Cube, new Vector3(0f, 0.004f, 0.35f), new Vector3(1.2f, 0.008f, 0.9f), Quaternion.identity, dim);

            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 1.0f, 0f);
            col.size = new Vector3(1.1f, 2.0f, 0.8f);

            var panel = BuildPanel(t, e, new Vector3(0f, 1.62f, -0.18f), Quaternion.Euler(-10f, 180f, 0f), out var bg);
            var term = go.AddComponent<StationTerminal>();
            term.Init(e, panel, bg);
            return term;
        }

        private static StationTerminal BuildHoloTable(Transform root, StationActions.Entry e, Vector3 center, out Transform holo)
        {
            var go = new GameObject("HoloTable");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(center, Quaternion.identity);
            var t = go.transform;
            var metal = Mat(new Color(0.16f, 0.18f, 0.21f), 0.8f, 0.55f);
            var glow = Unlit(e.Color);

            Local(t, PrimitiveType.Cylinder, new Vector3(0f, 0.45f, 0f), new Vector3(1.8f, 0.45f, 1.8f), Quaternion.identity, metal);
            Local(t, PrimitiveType.Cylinder, new Vector3(0f, 0.91f, 0f), new Vector3(1.6f, 0.01f, 1.6f), Quaternion.identity, glow);
            Local(t, PrimitiveType.Cylinder, new Vector3(0f, 0.004f, 0f), new Vector3(2.6f, 0.004f, 2.6f), Quaternion.identity, Unlit(e.Color * 0.3f + Color.black));

            var col = go.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 1.2f, 0f);
            col.radius = 0.95f;
            col.height = 2.6f;

            holo = new GameObject("Holo").transform;
            holo.SetParent(t, false);
            holo.localPosition = new Vector3(0f, 1.95f, 0f);
            holo.gameObject.AddComponent<HoloSpin>();
            BuildPlanetHolo(holo, glow);
            // Кольца-орбиты
            for (int i = 0; i < 2; i++)
            {
                var ring = new GameObject("Ring" + i).AddComponent<LineRenderer>();
                ring.transform.SetParent(holo, false);
                ring.transform.localRotation = Quaternion.Euler(i == 0 ? 72f : 105f, i * 40f, 0f);
                ring.useWorldSpace = false;
                ring.loop = true;
                ring.widthMultiplier = 0.015f;
                ring.sharedMaterial = glow;
                const int seg = 64;
                ring.positionCount = seg;
                float rr = 0.95f + i * 0.18f;
                for (int k = 0; k < seg; k++)
                {
                    float a = k / (float)seg * Mathf.PI * 2f;
                    ring.SetPosition(k, new Vector3(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr, 0f));
                }
            }

            var panel = BuildPanel(t, e, new Vector3(0f, 3.1f, 0f), Quaternion.identity, out var bg);
            panel.gameObject.AddComponent<Billboard>();
            var term = go.AddComponent<StationTerminal>();
            term.Init(e, panel, bg);
            return term;
        }

        private static void BuildPlanetHolo(Transform holo, Material fallback)
        {
            GameObject model = null;
            try
            {
                var p = StationActions.HomePlanet;
                var tpl = p != null ? (PlanetModelLibrary.Template(p) ?? RealPlanetModel.Template(p)) : null;
                if (tpl != null)
                {
                    model = Object.Instantiate(tpl, holo);
                    model.SetActive(true);
                    model.transform.localPosition = Vector3.zero;
                    model.transform.localRotation = Quaternion.identity;
                    foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                    if (TryBounds(model.transform, out var b))
                    {
                        float k = 1.3f / Mathf.Max(0.001f, b.size.magnitude / 1.732f);
                        model.transform.localScale *= k;
                        if (TryBounds(model.transform, out b)) model.transform.position += holo.position - b.center;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Station3D] Голограмма планеты: " + e.Message);
                if (model != null) Object.Destroy(model);
                model = null;
            }
            if (model == null)
            {
                var s = Part(holo, PrimitiveType.Sphere, holo.position, Vector3.one * 1.3f, Mat(new Color(0.2f, 0.45f, 0.7f), 0.1f, 0.7f), false);
                s.name = "PlanetFallback";
                var core = Part(s.transform, PrimitiveType.Sphere, s.transform.position, Vector3.one * 1.4f, fallback, false);
                core.transform.localScale = Vector3.one * 0.08f;
                core.transform.localPosition = new Vector3(0f, 0.52f, 0f);
            }
        }

        /// <summary>Голопанель над пультом: world-space Canvas со значком и названием окна.</summary>
        private static Transform BuildPanel(Transform parent, StationActions.Entry e, Vector3 localPos, Quaternion localRot, out Image bg)
        {
            const float W = 500f, H = 280f, Scale = 0.0022f;
            var go = new GameObject("Panel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.localPosition = localPos;
            rt.localRotation = localRot;
            rt.localScale = Vector3.one * Scale;
            rt.sizeDelta = new Vector2(W, H);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 4f;

            bg = NewImage(rt, "Bg", new Color(e.Color.r, e.Color.g, e.Color.b, 0.16f));
            Stretch(bg.rectTransform, 0, 0, 0, 0);
            var top = NewImage(rt, "Top", new Color(e.Color.r, e.Color.g, e.Color.b, 0.9f));
            top.rectTransform.anchorMin = new Vector2(0, 1); top.rectTransform.anchorMax = new Vector2(1, 1);
            top.rectTransform.pivot = new Vector2(0.5f, 1); top.rectTransform.sizeDelta = new Vector2(0, 8); top.rectTransform.anchoredPosition = Vector2.zero;
            var bot = NewImage(rt, "Bottom", new Color(e.Color.r, e.Color.g, e.Color.b, 0.6f));
            bot.rectTransform.anchorMin = new Vector2(0, 0); bot.rectTransform.anchorMax = new Vector2(1, 0);
            bot.rectTransform.pivot = new Vector2(0.5f, 0); bot.rectTransform.sizeDelta = new Vector2(0, 3); bot.rectTransform.anchoredPosition = Vector2.zero;

            try
            {
                var icon = LGIcons.Create(rt, e.Icon, 120f, e.Color, "Icon");
                icon.raycastTarget = false;
                icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0, 0.5f);
                icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                icon.rectTransform.anchoredPosition = new Vector2(95f, 10f);
                icon.rectTransform.sizeDelta = new Vector2(120f, 120f);
            }
            catch (Exception ex) { Debug.LogWarning("[Station3D] Значок пульта: " + ex.Message); }

            var title = NewText(rt, "Title", e.Title, 44, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft);
            Stretch(title.rectTransform, 180, 20, 70, 20);
            string keyHint = string.IsNullOrEmpty(e.Key) ? "[E]" : "[E]   ·   " + Loc.T("на карте") + " — " + e.Key;
            var hint = NewText(rt, "Hint", keyHint, 24, FontStyle.Normal, new Color(0.7f, 0.82f, 0.88f), TextAnchor.LowerLeft);
            Stretch(hint.rectTransform, 180, 22, 20, 20);
            return rt;
        }

        // ---------- Утилиты ----------

        internal static Text NewText(Transform parent, string name, string text, int size, FontStyle style, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = style == FontStyle.Bold ? GameFont.Bold : GameFont.Regular;
            if (t.font == null) t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.text = text;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            return t;
        }

        internal static Image NewImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        internal static void Stretch(RectTransform rt, float left, float bottom, float top, float right)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        private static GameObject Local(Transform parent, PrimitiveType type, Vector3 lp, Vector3 scale, Quaternion lr, Material m)
        {
            var g = Part(parent, type, parent.TransformPoint(lp), scale, m, false);
            g.transform.localPosition = lp;
            g.transform.localRotation = lr;
            g.transform.localScale = scale;
            return g;
        }

        private static GameObject Part(Transform parent, PrimitiveType type, Vector3 worldPos, Vector3 scale, Material m, bool collider)
        {
            var g = GameObject.CreatePrimitive(type);
            var c = g.GetComponent<Collider>();
            if (!collider && c != null) Object.DestroyImmediate(c);
            g.transform.SetParent(parent, true);
            g.transform.position = worldPos;
            g.transform.localScale = scale;
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;
        }

        private static void AddBox(Transform parent, string name, Vector3 worldPos, Vector3 size)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, true);
            g.transform.position = worldPos;
            g.AddComponent<BoxCollider>().size = size;
        }

        private static void AddLight(Transform parent, Vector3 pos, float range, float intensity, Color color, bool worldPos = true)
        {
            var g = new GameObject("Light");
            g.transform.SetParent(parent, true);
            g.transform.position = pos;
            var l = g.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = range;
            l.intensity = intensity;
            l.color = color;
            l.shadows = LightShadows.None;
            l.cullingMask = 1 << Layer;
        }

        private static Material Mat(Color c, float metal, float smooth)
        {
            var m = new Material(ShaderCache.Lit) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            s_res?.Materials.Add(m);
            return m;
        }

        private static Material Unlit(Color c)
        {
            c.a = 1f;
            var m = new Material(ShaderCache.Unlit) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            s_res?.Materials.Add(m);
            return m;
        }

        internal static bool TryBounds(Transform t, out Bounds b)
        {
            b = default;
            bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }

        internal static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayer(c.gameObject, layer);
        }
    }

    /// <summary>Медленное вращение голограммы.</summary>
    public class HoloSpin : MonoBehaviour
    {
        private void Update()
        {
            float t = Time.unscaledTime;
            transform.localRotation = Quaternion.Euler(0f, t * 12f, 0f);
            transform.localPosition = new Vector3(0f, 1.95f + Mathf.Sin(t * 0.8f) * 0.05f, 0f);
        }
    }

    /// <summary>Панель всегда повёрнута к камере (только по горизонтали).</summary>
    public class Billboard : MonoBehaviour
    {
        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var d = transform.position - cam.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(d, Vector3.up);
        }
    }
}
