using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Разведсеть на карте галактики, как в Hearts of Iron 4:
    ///   • зона сети — бирюзовый «живой» контур вокруг систем, где у вас есть сеть, с лёгкой заливкой (сильнее сеть — ярче);
    ///   • золотые пунктирные линии от системы агента к соседям: туда сеть расползается, пунктир бежит наружу;
    ///   • значок агента рядом со звездой — портрет в фиолетовой рамке и сила сети в системе (▲ — растёт);
    ///     при наведении — подсказка с разбором прироста, по клику открывается окно шпионажа на этом агенте.
    /// В виде системы и в меню всё скрыто. Зона перерисовывается, только когда сеть изменилась (раз в месяц).
    /// </summary>
    public class SpyNetworkView : MonoBehaviour
    {
        private const float MinShown = 3f;         // слабее — системы не входят в зону
        private const float Sigma = 7f;            // радиус влияния системы на контур, ед. карты
        private const float PixelsPerUnit = 4f;
        private const int MaxTex = 512;
        private const float Threshold = 0.45f;

        private static readonly Color Teal = new Color(0.40f, 0.86f, 0.82f);
        private static readonly Color Gold = new Color(0.95f, 0.78f, 0.40f);
        private static readonly Color AgentRing = new Color(0.74f, 0.56f, 1f);

        private class AgentVis
        {
            public int LeaderId, System = -1;
            public readonly List<LineRenderer> Lines = new List<LineRenderer>();
            public RectTransform Badge;
            public LeaderThumb Thumb;
            public Text Pct;
            public TooltipTrigger Tip;
        }

        private readonly Dictionary<int, AgentVis> _agents = new Dictionary<int, AgentVis>();
        private GameObject _zone;
        private SpriteRenderer _zoneSr;
        private Texture2D _zoneTex;
        private Material _zoneMat, _lineMat;
        private Canvas _canvas;
        private Camera _cam;
        private string _netKey = "";
        private float _scan;

        private void Start()
        {
            var sh = ShaderCache.Sprite ?? Shader.Find("Sprites/Default");
            if (sh != null)
            {
                _zoneMat = new Material(sh) { name = "SpyZone" };
                _lineMat = new Material(sh) { name = "SpyLanes", mainTexture = DashTexture() };
            }

            var cgo = new GameObject("[UI] SpyNetworkBadges");
            _canvas = cgo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 20;                      // под интерфейсом (100), над картой
            cgo.AddComponent<GraphicRaycaster>();
        }

        private void OnDestroy()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
            if (_zoneTex != null) Destroy(_zoneTex);
        }

        private void LateUpdate()
        {
            bool hidden = !UIManager.IsGameStarted || MenuAtmosphere.IsActive
                          || (SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView);
            if (_zone != null) _zone.SetActive(!hidden);
            if (_canvas != null) _canvas.enabled = !hidden;
            if (hidden) { foreach (var v in _agents.Values) SetLines(v, false); return; }

            _scan -= Time.unscaledDeltaTime;
            if (_scan <= 0f)
            {
                _scan = 0.5f;
                RefreshZone();
                RefreshAgents();
            }

            if (_lineMat != null) _lineMat.mainTextureOffset = new Vector2(-Time.unscaledTime * 0.9f, 0f);
            PlaceBadges();
        }

        // ==================== ЗОНА СЕТИ ====================

        private void RefreshZone()
        {
            var net = EspionageManager.Network;
            var pts = new List<Vector3>();
            var vals = new List<float>();
            var ids = new List<int>();
            var key = new System.Text.StringBuilder();
            if (net != null)
                foreach (var kv in net)
                {
                    if (kv.Value < MinShown) continue;
                    var s = EmpireStats.GetSystem(kv.Key);
                    if (s == null) continue;
                    pts.Add(s.Position);
                    vals.Add(kv.Value);
                    ids.Add(s.Id);
                    key.Append(kv.Key).Append(':').Append(Mathf.RoundToInt(kv.Value)).Append(';');
                }
            string k = key.ToString();
            if (k == _netKey) return;
            _netKey = k;

            if (pts.Count == 0)
            {
                if (_zone != null) _zone.SetActive(false);
                return;
            }
            BuildZone(pts, vals, ids);
        }

        /// <summary>
        /// Контур — линия уровня суммы «пятен» вокруг систем сети (метасферы) и полос вдоль гиперкоридоров между ними
        /// (иначе далёкие системы распадаются на острова), с лёгкой рябью, чтобы край был живой, как граница региона.
        /// Внутри — слабая заливка, ярче там, где сеть сильнее.
        /// </summary>
        private void BuildZone(List<Vector3> pts, List<float> vals, List<int> ids)
        {
            float margin = Sigma * 2.2f;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in pts)
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            }
            minX -= margin; maxX += margin; minZ -= margin; maxZ += margin;
            float ppu = Mathf.Min(PixelsPerUnit, MaxTex / Mathf.Max(maxX - minX, maxZ - minZ));
            int w = Mathf.Max(8, Mathf.CeilToInt((maxX - minX) * ppu));
            int h = Mathf.Max(8, Mathf.CeilToInt((maxZ - minZ) * ppu));

            var field = new float[w * h];
            var power = new float[w * h];
            float inv = 1f / (Sigma * Sigma);
            int reach = Mathf.CeilToInt(Sigma * 3f * ppu);
            for (int i = 0; i < pts.Count; i++)
            {
                float amp = 0.6f + 0.4f * vals[i] / 100f;
                int cx = Mathf.RoundToInt((pts[i].x - minX) * ppu), cy = Mathf.RoundToInt((pts[i].z - minZ) * ppu);
                for (int y = Mathf.Max(0, cy - reach); y < Mathf.Min(h, cy + reach); y++)
                for (int x = Mathf.Max(0, cx - reach); x < Mathf.Min(w, cx + reach); x++)
                {
                    float dx = (x - cx) / ppu, dz = (y - cy) / ppu;
                    float g = Mathf.Exp(-(dx * dx + dz * dz) * inv);
                    field[y * w + x] += amp * g;
                    power[y * w + x] = Mathf.Max(power[y * w + x], g * vals[i] / 100f);
                }
            }

            // Полосы вдоль коридоров между системами сети
            float segSigma = Sigma * 0.55f, segInv = 1f / (segSigma * segSigma);
            int segReach = Mathf.CeilToInt(segSigma * 3f * ppu);
            var index = new Dictionary<int, int>();
            for (int i = 0; i < ids.Count; i++) index[ids[i]] = i;
            for (int i = 0; i < ids.Count; i++)
            {
                var sys = EmpireStats.GetSystem(ids[i]);
                if (sys == null) continue;
                foreach (int nid in sys.ConnectedSystemIds)
                {
                    if (nid <= ids[i] || !index.TryGetValue(nid, out int j)) continue;
                    Vector3 a = pts[i], b = pts[j];
                    float abx = b.x - a.x, abz = b.z - a.z, len2 = Mathf.Max(1e-4f, abx * abx + abz * abz);
                    float amp = 0.55f * (0.6f + 0.4f * Mathf.Min(vals[i], vals[j]) / 100f);
                    float pw = Mathf.Min(vals[i], vals[j]) / 100f * 0.8f;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - minX) * ppu) - segReach);
                    int x1 = Mathf.Min(w, Mathf.CeilToInt((Mathf.Max(a.x, b.x) - minX) * ppu) + segReach);
                    int y0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, b.z) - minZ) * ppu) - segReach);
                    int y1 = Mathf.Min(h, Mathf.CeilToInt((Mathf.Max(a.z, b.z) - minZ) * ppu) + segReach);
                    for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        float wx = minX + x / ppu, wz = minZ + y / ppu;
                        float t = Mathf.Clamp01(((wx - a.x) * abx + (wz - a.z) * abz) / len2);
                        float dx = wx - (a.x + t * abx), dz = wz - (a.z + t * abz);
                        float g = Mathf.Exp(-(dx * dx + dz * dz) * segInv);
                        int k = y * w + x;
                        field[k] += amp * g * (1f - Mathf.Clamp01(field[k]) * 0.5f);
                        power[k] = Mathf.Max(power[k], g * pw);
                    }
                }
            }

            if (_zoneTex == null || _zoneTex.width != w || _zoneTex.height != h)
            {
                if (_zoneTex != null) Destroy(_zoneTex);
                _zoneTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SpyZone" };
            }
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float wx = minX + x / ppu, wz = minZ + y / ppu;
                float f = field[i] + 0.035f * (Mathf.Sin(wx * 0.9f + wz * 0.35f) + Mathf.Sin(wz * 1.1f - wx * 0.5f));
                float d = (f - Threshold) / 0.05f;
                float edge = Mathf.Exp(-d * d);                               // сам контур
                float glow = 0.35f * Mathf.Exp(-d * d * 0.12f);               // ореол вокруг контура
                float fill = f > Threshold ? 0.07f + 0.13f * Mathf.Clamp01(power[i]) : 0f;
                float a = Mathf.Clamp01(Mathf.Max(edge, glow) + fill);
                Color c = Color.Lerp(Teal, Color.white, edge * 0.35f);
                px[i] = new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)(a * 255));
            }
            _zoneTex.SetPixels32(px);
            _zoneTex.Apply(false);

            if (_zone == null)
            {
                _zone = new GameObject("SpyNetworkZone");
                _zone.transform.SetParent(transform, false);
                _zone.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                _zoneSr = _zone.AddComponent<SpriteRenderer>();
                if (_zoneMat != null) _zoneSr.sharedMaterial = _zoneMat;
                _zoneSr.sortingOrder = 6;                                     // под звёздами (14) и флотами
            }
            if (_zoneSr.sprite != null) Destroy(_zoneSr.sprite);
            _zoneSr.sprite = Sprite.Create(_zoneTex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), ppu);
            _zone.transform.position = new Vector3((minX + maxX) * 0.5f, pts[0].y - 0.05f, (minZ + maxZ) * 0.5f);
            _zone.SetActive(true);
        }

        // ==================== АГЕНТЫ: ПУНКТИР И ЗНАЧКИ ====================

        private void RefreshAgents()
        {
            var alive = new HashSet<int>();
            foreach (var a in EspionageManager.PlayerAgents())
            {
                if (a.Post != LeaderPost.Spy || a.TargetOwner <= 0 || EmpireStats.GetSystem(a.TargetSystem) == null) continue;
                alive.Add(a.Id);
            }

            foreach (int id in new List<int>(_agents.Keys))
                if (!alive.Contains(id)) { Drop(_agents[id]); _agents.Remove(id); }

            foreach (var a in EspionageManager.PlayerAgents())
            {
                if (!alive.Contains(a.Id)) continue;
                if (!_agents.TryGetValue(a.Id, out var v))
                {
                    v = new AgentVis { LeaderId = a.Id };
                    BuildBadge(v);
                    _agents[a.Id] = v;
                }
                if (v.System != a.TargetSystem) BuildLines(v, a.TargetSystem);
                SetLines(v, a.OpKind < 0);
                UpdateBadge(v, a);
            }
        }

        private void BuildLines(AgentVis v, int systemId)
        {
            foreach (var l in v.Lines) if (l != null) Destroy(l.gameObject);
            v.Lines.Clear();
            v.System = systemId;
            var s = EmpireStats.GetSystem(systemId);
            if (s == null || _lineMat == null) return;
            foreach (int nid in s.ConnectedSystemIds)
            {
                var n = EmpireStats.GetSystem(nid);
                if (n == null) continue;
                var go = new GameObject("SpyLane");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = _lineMat;
                lr.textureMode = LineTextureMode.Tile;
                lr.textureScale = new Vector2(0.45f, 1f);
                lr.alignment = LineAlignment.TransformZ;
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);       // лента лежит в плоскости карты
                lr.widthMultiplier = 0.5f;
                lr.numCapVertices = 0;
                lr.sortingOrder = 12;
                // от края звезды к краю соседней — пунктир не залезает на сами звёзды
                Vector3 dir = (n.Position - s.Position).normalized;
                lr.positionCount = 2;
                lr.SetPosition(0, s.Position + dir * 2.2f + Vector3.up * 0.05f);
                lr.SetPosition(1, n.Position - dir * 2.2f + Vector3.up * 0.05f);
                var c = Gold; c.a = 0.85f;
                lr.startColor = c;
                c.a = 0.35f;
                lr.endColor = c;
                v.Lines.Add(lr);
            }
        }

        private static void SetLines(AgentVis v, bool on)
        {
            foreach (var l in v.Lines) if (l != null && l.gameObject.activeSelf != on) l.gameObject.SetActive(on);
        }

        private void BuildBadge(AgentVis v)
        {
            var root = new GameObject("SpyAgentBadge", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(_canvas.transform, false);
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(40f, 40f);
            var hit = root.gameObject.AddComponent<Image>();               // ловит наведение и клик
            hit.color = new Color(0f, 0f, 0f, 0f);
            int id = v.LeaderId;
            var btn = root.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => EspionageWindow.Instance?.OpenAgent(id));
            v.Tip = root.gameObject.AddComponent<TooltipTrigger>();

            // тёмный круг-подложка, чтобы портрет читался на любой карте
            var backMask = LG.RoundedMask(root, 22f);
            backMask.Stretch(-2, -2, -2, -2);
            var back = LGBuild.Panel(backMask, "Back", new Color(0.03f, 0.05f, 0.08f, 0.9f));
            back.rectTransform.Stretch();

            var face = LGBuild.Rect(root, "Face");
            face.Stretch();
            v.Thumb = LeaderThumb.Create(face, 20f, 2f);

            var chip = LGBuild.Panel(root, "Chip", new Color(0.03f, 0.06f, 0.08f, 0.92f));
            chip.raycastTarget = false;
            chip.rectTransform.anchorMin = chip.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            chip.rectTransform.pivot = new Vector2(0.5f, 1f);
            chip.rectTransform.sizeDelta = new Vector2(46f, 15f);
            chip.rectTransform.anchoredPosition = new Vector2(0f, -1f);
            v.Pct = LGBuild.Label(chip.rectTransform, "", 10, Color.white, TextAnchor.MiddleCenter, bold: true);
            v.Pct.rectTransform.Stretch();
            v.Pct.raycastTarget = false;
            LG.Ignore(root.gameObject);                                     // свои слои, без авто-стекла интерфейса
            v.Badge = root;
        }

        private void UpdateBadge(AgentVis v, Leader a)
        {
            if (v.Thumb != null && !v.Thumb.Set(a, AgentRing)) v.Thumb.Root.gameObject.SetActive(false);
            float local = EspionageManager.NetworkAt(a.TargetSystem);
            bool grows = a.OpKind < 0 && local < 100f;
            v.Pct.text = grows ? $"{local:0}% <color=#66F59E>▲</color>" : $"{local:0}%";
            if (v.Tip != null) v.Tip.SetText(Tooltip(a, local));
        }

        private void PlaceBadges()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;
            float k = Screen.height / 1080f;
            foreach (var v in _agents.Values)
            {
                var s = EmpireStats.GetSystem(v.System);
                if (v.Badge == null || s == null) continue;
                Vector3 sp = _cam.WorldToScreenPoint(s.Position);
                bool on = sp.z > 0f;
                if (v.Badge.gameObject.activeSelf != on) v.Badge.gameObject.SetActive(on);
                if (!on) continue;
                v.Badge.localScale = Vector3.one * k;
                v.Badge.position = new Vector3(sp.x - 34f * k, sp.y + 4f * k, 0f);   // слева от звезды, как оперативник у региона
            }
        }

        private static string Tooltip(Leader a, float local)
        {
            var s = EmpireStats.GetSystem(a.TargetSystem);
            var g = EspionageManager.BuildRate(a);
            string owner = Contacts.NameForPlayer(a.TargetOwner);
            string oc = LGBuild.Hex(FleetIndicator.OwnerColor(a.TargetOwner));
            var sb = new System.Text.StringBuilder();
            sb.Append($"<b><color=#F2C766>{a.Name}</color></b>  ·  агент, уровень {a.Level}\n");
            sb.Append($"Идёт построение разведсети из системы <color=#F2C766>{s?.Name}</color> в империи <color={oc}>{owner}</color>\n\n");
            if (a.OpKind >= 0)
                sb.Append($"<color=#F2C766>Идёт операция «{SpyInfo.Name((SpyOp)a.OpKind)}», ещё {a.OpMonthsLeft} мес. — сеть не растёт</color>\n\n");
            sb.Append($"Рост сети в системе: <color=#66F59E>+{g.Total:0.#}</color> в месяц\n");
            sb.Append($"   Базовое значение: +{g.Base:0}\n");
            sb.Append($"   Уровень агента: +{g.Level:0}\n");
            if (g.TraitMul > 1f) sb.Append($"   Черта «Внедренец»: ×{g.TraitMul:0.##}\n");
            if (Mathf.Abs(g.TechMul - 1f) > 0.001f) sb.Append($"   Технологии: ×{g.TechMul:0.##}\n");
            sb.Append($"   Контрразведка цели: <color=#FF9F7A>×{g.CounterMul:0.##}</color>\n");
            sb.Append($"Соседние системы: ×{EspionageManager.NetRings[1]:0.#}, через одну: ×{EspionageManager.NetRings[2]:0.#}\n\n");
            sb.Append($"Сеть в системе: <color=#BD8FFF>{local:0}%</color>   ·   в империи: <color=#BD8FFF>{a.Cover:0}%</color>\n");
            sb.Append($"<color=#8AA2A8>{EspionageManager.IntelCover:0}% — обзор территории и флотов, {EspionageManager.DeepIntelCover:0}% — досье. ЛКМ — открыть агента</color>");
            return sb.ToString();
        }

        private static void Drop(AgentVis v)
        {
            foreach (var l in v.Lines) if (l != null) Destroy(l.gameObject);
            if (v.Badge != null) Destroy(v.Badge.gameObject);
        }

        /// <summary>Пунктир: штрих 60% периода, мягкие концы; повторяется вдоль линии.</summary>
        private static Texture2D DashTexture()
        {
            const int w = 32, h = 4;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "SpyDash" };
            var px = new Color32[w * h];
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w;
                float a = u < 0.6f ? Mathf.Clamp01(Mathf.Min(u, 0.6f - u) * 30f) : 0f;
                for (int y = 0; y < h; y++) px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }
    }
}
