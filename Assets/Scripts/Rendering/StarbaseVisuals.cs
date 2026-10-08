using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Модель орбитальной станции (Resources/Models/Station): кольцо со шпилем, восемь групп материалов.
    /// Материалы собираются в коде (URP Lit): корпусные панели с картами нормалей, гладкий металл,
    /// красные навигационные огни (общие, мигают) и синие огни, окрашенные в цвет владельца.
    /// Модель приводится к диаметру кольца 1 с центром в нуле.
    /// </summary>
    public static class StationAssets
    {
        private const string Folder = "Models/Station/";
        private static GameObject s_model;
        private static bool s_tried;
        private static float s_norm = 1f;
        private static Vector3 s_center;
        private static readonly Dictionary<string, Material> s_mats = new Dictionary<string, Material>();
        private static readonly Dictionary<int, Material> s_ownerLights = new Dictionary<int, Material>();
        private static Material s_red, s_off;

        public static bool Ready { get { Load(); return s_model != null; } }
        public static Material RedLights => s_red;

        private static void Load()
        {
            if (s_tried) return;
            s_tried = true;
            s_model = Resources.Load<GameObject>(Folder + "station");
            if (s_model == null || ShaderCache.Lit == null) { s_model = null; return; }

            var b = new Bounds();
            bool first = true;
            foreach (var mf in s_model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                if (first) { b = mb; first = false; } else b.Encapsulate(mb);
            }
            s_center = b.center;
            s_norm = 1f / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z));

            s_mats["hull_a"] = Lit("st_hull", "st_hull_n", 0.45f, 0.55f, new Color(0.92f, 0.94f, 0.98f));
            s_mats["hull_b"] = Lit("st_hull", "st_hull_b_n", 0.62f, 0.6f, new Color(0.85f, 0.88f, 0.93f));
            s_mats["panel"] = Lit("st_panel", "st_panel_n", 0.55f, 0.45f, Color.white);
            s_mats["deck"] = Lit("st_deck", null, 0.32f, 0.35f, new Color(0.9f, 0.9f, 0.92f));
            s_mats["detail"] = Lit("st_detail", "st_detail_n", 0.42f, 0.5f, Color.white);
            s_mats["plain"] = Lit(null, null, 0.55f, 0.65f, new Color(0.72f, 0.75f, 0.8f));
            s_red = Glow(new Color(1f, 0.08f, 0.06f), 3f);
            s_off = Glow(new Color(0.05f, 0.05f, 0.06f), 0f);
        }

        private static Material Lit(string albedo, string normal, float smooth, float metal, Color tint)
        {
            var m = new Material(ShaderCache.Lit) { name = "Station_" + (albedo ?? "plain") };
            if (albedo != null)
            {
                var tex = Resources.Load<Texture2D>(Folder + albedo);
                if (tex != null)
                {
                    if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                    if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
                }
            }
            if (normal != null && m.HasProperty("_BumpMap"))
            {
                var n = Resources.Load<Texture2D>(Folder + normal);
                if (n != null)
                {
                    m.SetTexture("_BumpMap", n);
                    m.SetFloat("_BumpScale", 1.3f);
                    m.EnableKeyword("_NORMALMAP");
                }
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
            if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            return m;
        }

        private static Material Glow(Color c, float intensity)
        {
            var m = Lit(null, null, 0.2f, 0f, Color.black);
            m.name = "Station_Lights";
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * intensity);
            return m;
        }

        /// <summary>Синие огни станции в цвете владельца.</summary>
        public static Material OwnerLights(int owner)
        {
            Load();
            if (!s_ownerLights.TryGetValue(owner, out var m))
            {
                Color c = Color.Lerp(FleetIndicator.OwnerColor(owner), Color.white, 0.15f);
                s_ownerLights[owner] = m = Glow(c, 2.6f);
            }
            return m;
        }

        public static Material LightsOff { get { Load(); return s_off; } }

        /// <summary>Экземпляр станции: корень (центр), внутри — модель с диаметром кольца 1. ownerLights — список рендереров огней владельца.</summary>
        public static GameObject Create(Transform parent, int owner, List<Renderer> ownerLights)
        {
            if (!Ready) return null;
            var root = new GameObject("Starbase");
            root.transform.SetParent(parent, false);
            var model = Object.Instantiate(s_model, root.transform);
            model.name = "Model";
            model.transform.localScale = Vector3.one * s_norm;
            model.transform.localPosition = -s_center * s_norm;
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                string key = r.gameObject.name;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (key.StartsWith("light_blue")) { r.sharedMaterial = OwnerLights(owner); ownerLights?.Add(r); }
                else if (key.StartsWith("light_red")) r.sharedMaterial = s_red;
                else
                {
                    foreach (var kv in s_mats)
                        if (key.StartsWith(kv.Key)) { r.sharedMaterial = kv.Value; break; }
                }
            }
            return root;
        }
    }

    /// <summary>
    /// Станции-форпосты на карте галактики: модель появляется у каждой системы со звёздной базой.
    ///   • Стоит на той же площадке, где строительный корабль собирал голограмму каркаса.
    ///   • Только что построенная станция «материализуется»: вспышка, рост с лёгким перелётом масштаба, огни разгораются.
    ///   • Кольцо медленно вращается, красные навигационные огни мигают, синие горят цветом владельца;
    ///     у выведенной из строя базы огни гаснут; при смене владельца огни перекрашиваются.
    ///   • Видна только при приближении камеры, в известных игроку системах; в виде системы — своя копия на орбите.
    /// </summary>
    public class StarbaseVisuals : MonoBehaviour
    {
        public static StarbaseVisuals Instance { get; private set; }

        private const float ShowHeight = 160f;
        private const float Diameter = 2.4f;
        private const float AppearTime = 2.2f;

        private class Entry
        {
            public GameObject Go;
            public Transform Spin;
            public int Owner;
            public bool LightsOn = true, Flashed;
            public float Born = -100f;
            public Vector3 Star;
            public readonly List<Renderer> OwnerLights = new List<Renderer>();
        }

        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        private Transform _root;
        private float _timer;
        private bool _synced;
        private object _systemsRef;

        /// <summary>Площадка станции у звезды (там же строитель собирает каркас; оттуда стреляет база).</summary>
        public static Vector3 SiteFor(Vector3 star)
        {
            float a = Mathf.Repeat(star.x * 0.37f + star.z * 0.61f, 6.2832f);
            return star + new Vector3(Mathf.Cos(a) * 2.7f, 0.35f, Mathf.Sin(a) * 2.7f);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_root != null) Destroy(_root.gameObject);
        }

        private void Update()
        {
            if (!UIManager.IsGameStarted || MenuAtmosphere.IsActive)
            {
                if (_entries.Count > 0) ClearAll();
                return;
            }
            if (!StationAssets.Ready) return;
            if (_root == null) _root = new GameObject("Starbases").transform;

            // Новая партия или загрузка — системы другие: пересобрать без анимации появления
            var systems = EmpireStats.Systems;
            if (!ReferenceEquals(systems, _systemsRef)) { ClearAll(); _systemsRef = systems; }

            if ((_timer -= Time.unscaledDeltaTime) <= 0f)
            {
                _timer = 0.4f;
                Sync();
            }
            Animate();
        }

        private void ClearAll()
        {
            foreach (var e in _entries.Values) if (e.Go != null) Destroy(e.Go);
            _entries.Clear();
            _synced = false;
        }

        private void Sync()
        {
            var systems = EmpireStats.Systems;
            var cm = CombatManager.Instance;
            var seen = new HashSet<int>();
            var fresh = new List<Entry>();
            foreach (var s in systems)
            {
                if (s.OwnerId < 0 || !s.HasStarbase) continue;
                seen.Add(s.Id);
                if (!_entries.TryGetValue(s.Id, out var e))
                {
                    e = new Entry { Owner = s.OwnerId };
                    var go = StationAssets.Create(_root, s.OwnerId, e.OwnerLights);
                    if (go == null) continue;
                    go.name = "Starbase_" + s.Name;
                    e.Go = go;
                    e.Spin = go.transform.GetChild(0);
                    e.Star = s.Position;
                    go.transform.position = SiteFor(s.Position);
                    go.transform.rotation = Quaternion.Euler(8f, Mathf.Repeat(s.Id * 47f, 360f), 0f);
                    go.transform.localScale = Vector3.one * Diameter;
                    // Только что построена (не при загрузке) — материализуется на глазах
                    if (_synced) fresh.Add(e);
                    _entries[s.Id] = e;
                }
                if (e.Owner != s.OwnerId)
                {
                    e.Owner = s.OwnerId;
                    var mat = StationAssets.OwnerLights(s.OwnerId);
                    foreach (var r in e.OwnerLights) if (r != null && e.LightsOn) r.sharedMaterial = mat;
                }
                bool on = cm == null || cm.IsStarbaseActive(s.Id);
                if (on != e.LightsOn)
                {
                    e.LightsOn = on;
                    var mat = on ? StationAssets.OwnerLights(s.OwnerId) : StationAssets.LightsOff;
                    foreach (var r in e.OwnerLights) if (r != null) r.sharedMaterial = mat;
                }
                if ((e.Star - s.Position).sqrMagnitude > 0.01f)
                {
                    e.Star = s.Position;
                    e.Go.transform.position = SiteFor(s.Position);
                }
            }

            // Разом появилось много станций — это загрузка, а не стройка: без анимации
            if (fresh.Count <= 2) foreach (var e in fresh) e.Born = Time.unscaledTime;

            if (seen.Count != _entries.Count)
            {
                var gone = new List<int>();
                foreach (var id in _entries.Keys) if (!seen.Contains(id)) gone.Add(id);
                foreach (var id in gone)
                {
                    var e = _entries[id];
                    if (e.Go != null)
                    {
                        if (e.Go.activeSelf && CombatFx.CanShow(e.Go.transform.position))
                            CombatFx.Instance.ShipDestroyed(e.Go.transform.position, 1.8f, FleetIndicator.OwnerColor(e.Owner));
                        Destroy(e.Go);
                    }
                    _entries.Remove(id);
                }
            }
            _synced = true;
        }

        private void Animate()
        {
            var cam = Camera.main;
            bool inSystem = SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView;
            bool near = cam != null && cam.transform.position.y < ShowHeight && !inSystem;
            float t = Time.unscaledTime;

            // Красные навигационные огни: короткая вспышка раз в пару секунд
            var red = StationAssets.RedLights;
            if (red != null)
            {
                float blink = Mathf.Repeat(t, 1.8f) < 0.18f ? 4.5f : 0.5f;
                red.SetColor("_EmissionColor", new Color(1f, 0.08f, 0.06f) * blink);
            }

            foreach (var kv in _entries)
            {
                var e = kv.Value;
                if (e.Go == null) continue;
                bool known = e.Owner == 0 || Vision.PlayerKnows(kv.Key);
                // Модель детальная — рисуем только станции рядом с камерой
                bool close = near && (cam.transform.position - e.Go.transform.position).sqrMagnitude < 230f * 230f;
                bool show = close && known;
                if (e.Go.activeSelf != show) e.Go.SetActive(show);
                if (!show) continue;

                e.Spin.localRotation = Quaternion.Euler(0f, t * 5f, 0f);

                float age = t - e.Born;
                if (age < AppearTime)
                {
                    float k = Mathf.Clamp01(age / AppearTime);
                    // Рост с лёгким перелётом масштаба
                    float s = 1f + 2.7f * Mathf.Pow(k - 1f, 3f) + 1.7f * Mathf.Pow(k - 1f, 2f);
                    e.Go.transform.localScale = Vector3.one * Diameter * Mathf.Max(0.05f, s);
                    if (!e.Flashed && CombatFx.CanShow(e.Go.transform.position))
                    {
                        e.Flashed = true;
                        CombatFx.Instance.Blip(e.Go.transform.position, new Color(1f, 0.85f, 0.5f), 7f, 0.9f);
                        CombatFx.Instance.Sparks(e.Go.transform.position, new Color(1f, 0.78f, 0.4f), 14, 1.4f);
                    }
                }
                else if (e.Go.transform.localScale.x != Diameter) e.Go.transform.localScale = Vector3.one * Diameter;
            }
        }

        /// <summary>Станция в виде системы: на собственной орбите внутри первой планеты.</summary>
        public static void SpawnInSystemView(Transform container, StarSystem system)
        {
            if (container == null || system == null || system.OwnerId < 0 || !system.HasStarbase || !StationAssets.Ready) return;
            float minOrbit = 12f;
            if (system.Planets != null)
                foreach (var p in system.Planets) minOrbit = Mathf.Min(minOrbit, p.OrbitRadius);
            float r = Mathf.Max(3.5f, minOrbit * 0.55f);
            var go = StationAssets.Create(container, system.OwnerId, null);
            if (go == null) return;
            float a = Mathf.Repeat(system.Id * 1.7f, 6.2832f);
            go.transform.localPosition = new Vector3(Mathf.Cos(a) * r, 0.4f, Mathf.Sin(a) * r);
            go.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
            go.transform.localScale = Vector3.one * 2.2f;
            go.AddComponent<StationSpin>();
        }
    }

    /// <summary>Медленное вращение кольца станции.</summary>
    public class StationSpin : MonoBehaviour
    {
        private Transform _model;
        private void Start() => _model = transform.childCount > 0 ? transform.GetChild(0) : transform;
        private void Update() { if (_model != null) _model.localRotation = Quaternion.Euler(0f, Time.unscaledTime * 5f, 0f); }
    }
}
