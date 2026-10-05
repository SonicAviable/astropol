using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using StellarisClone.Core;
using StellarisClone.Generation;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Маршруты выделенных флотов в духе Stellaris:
    ///   • пунктирная линия цвета владельца, штрихи и стрелки бегут по направлению движения;
    ///   • промежуточные системы — точки с числом дней до прибытия;
    ///   • пункты назначения из очереди (Shift+ПКМ) — кружки с номерами 1, 2, 3…;
    ///   • у конечной точки — общее время «~45 дней»;
    ///   • наведение на линию — подсказка: время, расстояние, промежуточные системы.
    /// Вся геометрия — один меш, перестраиваемый каждый кадр (размеры постоянны на экране).
    /// </summary>
    public class FleetRouteOverlay : MonoBehaviour
    {
        public static FleetRouteOverlay Instance { get; private set; }

        private const float DashPx = 11f, GapPx = 7f, WidthPx = 3f;
        private const float ArrowPx = 8f, ArrowSpacingPx = 70f, FlowPxPerSec = 38f;
        private const float DotPx = 7f;
        private const float LabelPx = 13f;
        private const float RouteY = 0.55f;
        private const int MaxFleets = 16;

        private GalaxyGenerator _gen;
        private Camera _cam;
        private Mesh _mesh;
        private MeshRenderer _renderer;
        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Color> _c = new List<Color>();
        private readonly List<int> _t = new List<int>();

        private readonly List<TextMesh> _labels = new List<TextMesh>();
        private int _labelsUsed;
        private readonly List<GameObject> _markers = new List<GameObject>();
        private int _markersUsed;

        private struct CachedRoute
        {
            public string Signature;
            public List<FleetRoute.Node> Nodes;
        }
        private readonly Dictionary<FleetView, CachedRoute> _cache = new Dictionary<FleetView, CachedRoute>();

        // Для подсказки при наведении на линию
        private readonly List<(FleetView fleet, List<Vector3> points, List<FleetRoute.Node> nodes)> _drawn
            = new List<(FleetView, List<Vector3>, List<FleetRoute.Node>)>();
        private bool _tooltipShown;
        private float _flow;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            _gen = FindAnyObjectByType<GalaxyGenerator>();
            var go = new GameObject("RouteMesh");
            go.transform.SetParent(transform, false);
            _mesh = new Mesh { name = "FleetRoutes" };
            _mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = FleetIndicator.SpriteMaterial;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.sortingOrder = 30;
        }

        private void LateUpdate()
        {
            _v.Clear(); _c.Clear(); _t.Clear();
            _labelsUsed = 0;
            _markersUsed = 0;
            _drawn.Clear();

            if (_cam == null) _cam = Camera.main;
            var fm = FleetManager.Instance;
            if (_gen == null) _gen = FindAnyObjectByType<GalaxyGenerator>();
            bool active = UIManager.IsGameStarted && _cam != null && fm != null && _gen != null
                          && !(SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView);

            if (active)
            {
                _flow += Time.unscaledDeltaTime * FlowPxPerSec;
                int n = 0;
                foreach (var f in fm.SelectedFleets)
                {
                    if (f?.Data == null || f.Data.Destroyed) continue;
                    if (++n > MaxFleets) break;
                    DrawFleet(f);
                }
            }

            _mesh.Clear();
            _mesh.SetVertices(_v);
            _mesh.SetColors(_c);
            _mesh.SetTriangles(_t, 0);
            _mesh.RecalculateBounds();
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 5000f);

            for (int i = _labelsUsed; i < _labels.Count; i++) if (_labels[i].gameObject.activeSelf) _labels[i].gameObject.SetActive(false);
            for (int i = _markersUsed; i < _markers.Count; i++) if (_markers[i].activeSelf) _markers[i].SetActive(false);

            UpdateHoverTooltip(active);
        }

        // ==================== ПОСТРОЕНИЕ ====================

        private static Color OwnerColor(FleetData d)
        {
            if (d.OwnerId == 0)
            {
                var f = UIManager.Instance != null ? UIManager.Instance.SelectedFaction : null;
                return f != null ? Color.Lerp(f.EmpireColor, FleetIndicator.OwnColor, 0.35f) : FleetIndicator.OwnColor;
            }
            var ai = AIEmpireManager.Instance;
            return ai != null ? ai.AIEmpireColor : FleetIndicator.EnemyColor;
        }

        private List<FleetRoute.Node> RouteFor(FleetView f)
        {
            var d = f.Data;
            var sb = new StringBuilder();
            sb.Append((int)d.State).Append('|').Append(d.CurrentSystemId).Append('|').Append(d.TargetSystemId).Append('|');
            foreach (int p in d.Path) sb.Append(p).Append(',');
            sb.Append('|');
            foreach (int q in d.OrderQueue) sb.Append(q).Append(',');
            sb.Append('|').Append(Mathf.RoundToInt(d.DaysRemainingInTransit));
            string sig = sb.ToString();
            if (_cache.TryGetValue(f, out var c) && c.Signature == sig) return c.Nodes;
            var nodes = FleetRoute.Build(d, _gen);
            _cache[f] = new CachedRoute { Signature = sig, Nodes = nodes };
            return nodes;
        }

        private Vector3 SysPos(int id)
        {
            var p = _gen.Systems[id].Position;
            p.y = RouteY;
            return p;
        }

        private float Wpp(Vector3 p)
        {
            float dist = Mathf.Max(1f, Vector3.Distance(p, _cam.transform.position));
            return 2f * dist * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
        }

        private void DrawFleet(FleetView f)
        {
            var d = f.Data;
            var nodes = RouteFor(f);
            if (nodes.Count == 0) return;

            Color col = OwnerColor(d);
            var pts = new List<Vector3>(nodes.Count + 1);
            Vector3 start = f.transform.position;
            start.y = RouteY;
            pts.Add(start);
            foreach (var nd in nodes) pts.Add(SysPos(nd.SystemId));
            _drawn.Add((f, pts, nodes));

            // Пунктир и стрелки вдоль всей ломаной; фаза едет вперёд — линия «течёт» к цели
            float along = 0f;   // пройдено в пикселях от начала
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector3 a = pts[i], b = pts[i + 1];
                Vector3 seg = b - a;
                float len = seg.magnitude;
                if (len < 0.01f) continue;
                Vector3 dir = seg / len;
                float wpp = Wpp((a + b) * 0.5f);
                float lenPx = len / wpp;

                // Штрихи
                float period = DashPx + GapPx;
                float phase = Mathf.Repeat(along - _flow, period);
                for (float s = -phase; s < lenPx; s += period)
                {
                    float s0 = Mathf.Max(0f, s), s1 = Mathf.Min(lenPx, s + DashPx);
                    if (s1 <= s0) continue;
                    // Ближе к цели линия ярче
                    float k = Mathf.Lerp(0.55f, 1f, (i + s0 / lenPx) / Mathf.Max(1, pts.Count - 1));
                    Quad(a + dir * (s0 * wpp), a + dir * (s1 * wpp), WidthPx * wpp, new Color(col.r, col.g, col.b, 0.9f * k));
                }

                // Стрелки
                float aPhase = Mathf.Repeat(along - _flow * 1.6f, ArrowSpacingPx);
                for (float s = ArrowSpacingPx - aPhase; s < lenPx - ArrowPx; s += ArrowSpacingPx)
                    Arrow(a + dir * (s * wpp), dir, ArrowPx * wpp, Color.Lerp(col, Color.white, 0.35f));

                along += lenPx;
            }

            // Точки промежуточных систем, номера пунктов и дни
            int destinations = 0;
            foreach (var nd in nodes) if (nd.OrderIndex > 0) destinations++;
            for (int i = 0; i < nodes.Count; i++)
            {
                var nd = nodes[i];
                Vector3 p = pts[i + 1];
                float wpp = Wpp(p);
                bool last = i == nodes.Count - 1;
                if (nd.OrderIndex == 0)
                {
                    Diamond(p, DotPx * wpp, new Color(col.r, col.g, col.b, 0.95f));
                    Label(p + _cam.transform.up * (11f * wpp), $"{Mathf.CeilToInt(nd.Days)}", new Color(0.85f, 0.92f, 0.98f, 0.9f), LabelPx * 0.85f);
                }
                else
                {
                    Marker(p, wpp, col, destinations > 1 ? nd.OrderIndex.ToString() : "");
                    string text = last ? $"~{FleetRoute.FormatDays(nd.Days)}" : $"{Mathf.CeilToInt(nd.Days)} дн.";
                    Label(p + _cam.transform.up * (18f * wpp), text, Color.white, last ? LabelPx * 1.1f : LabelPx);
                }
            }
        }

        private void Quad(Vector3 a, Vector3 b, float width, Color col)
        {
            Vector3 dir = (b - a).normalized;
            Vector3 n = new Vector3(-dir.z, 0f, dir.x) * (width * 0.5f);
            int i = _v.Count;
            _v.Add(a - n); _v.Add(a + n); _v.Add(b + n); _v.Add(b - n);
            for (int k = 0; k < 4; k++) _c.Add(col);
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
            _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
        }

        private void Arrow(Vector3 p, Vector3 dir, float size, Color col)
        {
            Vector3 n = new Vector3(-dir.z, 0f, dir.x);
            int i = _v.Count;
            _v.Add(p + dir * size);
            _v.Add(p - dir * (size * 0.6f) + n * (size * 0.75f));
            _v.Add(p - dir * (size * 0.25f));
            _v.Add(p - dir * (size * 0.6f) - n * (size * 0.75f));
            for (int k = 0; k < 4; k++) _c.Add(col);
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
            _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
        }

        private void Diamond(Vector3 p, float size, Color col)
        {
            int i = _v.Count;
            _v.Add(p + Vector3.forward * size); _v.Add(p + Vector3.right * size);
            _v.Add(p - Vector3.forward * size); _v.Add(p - Vector3.right * size);
            for (int k = 0; k < 4; k++) _c.Add(col);
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
            _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
        }

        // ==================== ПОДПИСИ И МАРКЕРЫ ====================

        private TextMesh NewText(Transform parent, string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tm = go.AddComponent<TextMesh>();
            var font = GameFont.Bold;
            tm.font = font;
            tm.fontSize = 64;
            tm.characterSize = 0.1f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold;
            var mr = go.GetComponent<MeshRenderer>();
            if (font != null) mr.sharedMaterial = font.material;
            mr.sortingOrder = order;
            return tm;
        }

        private void Label(Vector3 pos, string text, Color col, float px)
        {
            TextMesh tm;
            if (_labelsUsed < _labels.Count) tm = _labels[_labelsUsed];
            else { tm = NewText(transform, "RouteLabel", 33); _labels.Add(tm); }
            _labelsUsed++;
            if (!tm.gameObject.activeSelf) tm.gameObject.SetActive(true);
            if (tm.text != text) tm.text = text;
            tm.color = col;
            tm.transform.position = pos;
            tm.transform.rotation = _cam.transform.rotation;
            tm.transform.localScale = Vector3.one * (px * Wpp(pos) / 0.64f);
        }

        private void Marker(Vector3 pos, float wpp, Color col, string number)
        {
            GameObject m;
            if (_markersUsed < _markers.Count) m = _markers[_markersUsed];
            else
            {
                m = new GameObject("QueueMarker");
                m.transform.SetParent(transform, false);
                var ring = new GameObject("Ring").AddComponent<SpriteRenderer>();
                ring.transform.SetParent(m.transform, false);
                ring.sprite = FleetIndicator.GetRingSprite();
                ring.sharedMaterial = FleetIndicator.SpriteMaterial;
                ring.sortingOrder = 31;
                var disc = new GameObject("Disc").AddComponent<SpriteRenderer>();
                disc.transform.SetParent(m.transform, false);
                disc.transform.localScale = Vector3.one * 0.8f;
                disc.sprite = FleetIndicator.GetDiscSprite();
                disc.sharedMaterial = FleetIndicator.SpriteMaterial;
                disc.sortingOrder = 32;
                var tm = NewText(m.transform, "Num", 34);
                tm.characterSize = 0.06f;
                tm.transform.localPosition = new Vector3(0f, 0f, -0.01f);
                _markers.Add(m);
            }
            _markersUsed++;
            if (!m.activeSelf) m.SetActive(true);

            m.transform.position = pos;
            m.transform.rotation = _cam.transform.rotation;
            float pulse = 1f + 0.06f * Mathf.Sin(Time.unscaledTime * 4f);
            m.transform.localScale = Vector3.one * (24f * wpp / 0.86f) * pulse;

            var srs = m.GetComponentsInChildren<SpriteRenderer>();
            srs[0].color = Color.Lerp(col, Color.white, 0.3f);
            srs[1].color = string.IsNullOrEmpty(number) ? new Color(col.r * 0.25f, col.g * 0.25f, col.b * 0.25f, 0.6f) : col;
            var t = m.GetComponentInChildren<TextMesh>();
            if (t.text != number) t.text = number;
            t.color = new Color(0.02f, 0.05f, 0.07f, 1f);
        }

        // ==================== ПОДСКАЗКА ====================

        private void UpdateHoverTooltip(bool active)
        {
            string text = null;
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (active && !overUi && _drawn.Count > 0)
            {
                Vector2 mouse = Input.mousePosition;
                foreach (var (fleet, points, nodes) in _drawn)
                {
                    if (!NearPolyline(points, mouse, 9f)) continue;
                    text = BuildTooltip(fleet, points, nodes);
                    break;
                }
            }

            if (text != null)
            {
                // Show перезапускает задержку появления — вызываем только при входе
                if (!_tooltipShown) TooltipSystem.Show(text);
                _tooltipShown = true;
            }
            else if (_tooltipShown)
            {
                TooltipSystem.Hide();
                _tooltipShown = false;
            }
        }

        private bool NearPolyline(List<Vector3> pts, Vector2 mouse, float px)
        {
            // Над самими системами показываем их подсказку, а не маршрута
            for (int i = 1; i < pts.Count; i++)
            {
                Vector3 sp = _cam.WorldToScreenPoint(pts[i]);
                if (sp.z > 0f && Vector2.Distance(mouse, sp) < 22f) return false;
            }
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector3 a = _cam.WorldToScreenPoint(pts[i]);
                Vector3 b = _cam.WorldToScreenPoint(pts[i + 1]);
                if (a.z < 0f || b.z < 0f) continue;
                Vector2 ab = (Vector2)b - (Vector2)a;
                float t = ab.sqrMagnitude > 0.01f ? Mathf.Clamp01(Vector2.Dot(mouse - (Vector2)a, ab) / ab.sqrMagnitude) : 0f;
                if (Vector2.Distance(mouse, (Vector2)a + ab * t) <= px) return true;
            }
            return false;
        }

        private string BuildTooltip(FleetView fleet, List<Vector3> pts, List<FleetRoute.Node> nodes)
        {
            float dist = 0f;
            for (int i = 0; i < pts.Count - 1; i++) dist += Vector3.Distance(pts[i], pts[i + 1]);
            var sb = new StringBuilder();
            sb.Append($"<b>Маршрут: {fleet.Data.Name}</b>\n");
            sb.Append($"<color=#8AA2A8>В пути:</color> ~{FleetRoute.FormatDays(nodes[nodes.Count - 1].Days)}\n");
            sb.Append($"<color=#8AA2A8>Расстояние:</color> {dist:0} св. лет · прыжков: {nodes.Count}\n");
            if (fleet.Data.OrderQueue.Count > 0)
                sb.Append($"<color=#8AA2A8>Пунктов в очереди:</color> {fleet.Data.OrderQueue.Count + 1}\n");
            sb.Append('\n');
            int shown = 0;
            foreach (var nd in nodes)
            {
                if (shown++ >= 10) { sb.Append("…\n"); break; }
                string name = _gen.Systems[nd.SystemId].Name;
                string mark = nd.OrderIndex > 0 ? $"<color=#4DF2DB>{nd.OrderIndex}.</color> <b>{name}</b>" : $"<color=#8AA2A8>·</color> {name}";
                sb.Append($"{mark} — {Mathf.CeilToInt(nd.Days)} дн.\n");
            }
            sb.Append("\n<color=#8AA2A8>Shift+ПКМ — добавить пункт · Backspace — очистить очередь</color>");
            return sb.ToString();
        }
    }
}
