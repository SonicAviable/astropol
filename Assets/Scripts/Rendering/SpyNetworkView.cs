using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Разведсеть на карте галактики, как в Hearts of Iron 4:
    ///   • у каждой системы, где есть сеть, — тонкое бирюзовое кольцо-шкала вокруг звезды (дуга — сила сети);
    ///   • потоки данных от системы агента к соседям (туда сеть расползается): по бирюзовому каналу бегут кометы света;
    ///   • значок агента рядом со звездой — портрет в фиолетовой рамке и сила сети в системе (▲ — растёт);
    ///     при наведении — подсказка с разбором прироста, по клику открывается окно шпионажа на этом агенте.
    /// Кольца и потоки данных видны только в режиме карты «Шпионаж», в остальных — одни значки агентов.
    /// Выбор системы для агента (BeginPick) — тоже здесь: системы цели помечены пульсирующими кольцами,
    /// ЛКМ рядом со звездой выбирает (даже под туманом войны), ПКМ или Esc — отмена.
    /// В виде системы и в меню всё скрыто. Кольца перестраиваются, только когда сеть изменилась (раз в месяц).
    /// </summary>
    public class SpyNetworkView : MonoBehaviour
    {
        private const float MinShown = 3f;         // слабее — системы не входят в зону
        private const float RingRadius = 4.4f;     // кольцо сети вокруг звезды, ед. карты
        private const float RingWidth = 0.32f;
        private const int RingSegments = 64;

        private static readonly Color Teal = new Color(0.40f, 0.86f, 0.82f);
        private static readonly Color AgentRing = new Color(0.74f, 0.56f, 1f);

        public static SpyNetworkView Instance { get; private set; }

        private class AgentVis
        {
            public int LeaderId, System = -1;
            public bool Busy;
            public readonly List<LineRenderer> Lines = new List<LineRenderer>();
            public RectTransform Badge;
            public LeaderThumb Thumb;
            public Text Pct;
            public TooltipTrigger Tip;
        }

        private readonly Dictionary<int, AgentVis> _agents = new Dictionary<int, AgentVis>();
        private GameObject _zone;
        private SpriteRenderer _zoneSr;
        private Material _zoneMat, _lineMat;
        private Canvas _canvas;
        private Camera _cam;
        private string _netKey = "";
        private float _scan;

        // Выбор системы для агента
        private Leader _pickAgent;
        private int _pickOwner = -1;
        private readonly List<KeyValuePair<StarSystem, SpriteRenderer>> _pickMarks = new List<KeyValuePair<StarSystem, SpriteRenderer>>();
        private StarSystem _pickHover;
        private RectTransform _banner;
        private Text _bannerText;
        private Sprite _ringSprite;
        private Material _markMat;

        public static bool IsPicking => Instance != null && Instance._pickOwner >= 0;

        private void Awake() => Instance = this;

        private void Start()
        {
            var sh = ShaderCache.Sprite ?? Shader.Find("Sprites/Default");
            if (sh != null) _zoneMat = new Material(sh) { name = "SpyZone" };
            // Потоки данных — шейдер, который учитывает сдвиг текстуры (Sprites/Default его игнорирует): так кометы бегут
            var add = Resources.Load<Shader>("Shaders/CombatAdditive") ?? sh;
            if (add != null)
            {
                _lineMat = new Material(add) { name = "SpyLanes", mainTexture = StreamTexture() };
                _lineMat.renderQueue = 3000;                 // вместе со спрайтами карты — порядок по sortingOrder
                _markMat = new Material(add) { name = "SpyPickMarks", renderQueue = 3000 };
            }
            var ringTex = Resources.Load<Texture2D>("UI/Cursors/fx_ring");
            if (ringTex != null) _ringSprite = Sprite.Create(ringTex, new Rect(0, 0, ringTex.width, ringTex.height), new Vector2(0.5f, 0.5f), ringTex.width);

            var cgo = new GameObject("[UI] SpyNetworkBadges");
            _canvas = cgo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 20;                      // под интерфейсом (100), над картой
            cgo.AddComponent<GraphicRaycaster>();
            BuildBanner();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            ClearPickMarks();
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        private void LateUpdate()
        {
            bool hidden = !UIManager.IsGameStarted || MenuAtmosphere.IsActive
                          || (SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView);
            bool intel = !hidden && GalaxyView.Instance != null
                         && GalaxyView.Instance.CurrentMapMode == MapModeController.MapMode.Intel;
            if (_canvas != null) _canvas.enabled = !hidden;
            if (hidden && IsPicking) EndPick();

            _scan -= Time.unscaledDeltaTime;
            if (_scan <= 0f && !hidden)
            {
                _scan = 0.5f;
                RefreshZone();
                RefreshAgents();
            }

            // Кольца и потоки — только в режиме «Шпионаж»
            if (_zone != null && _zone.activeSelf != (intel && _netKey.Length > 0)) _zone.SetActive(intel && _netKey.Length > 0);
            foreach (var v in _agents.Values) SetLines(v, intel && !v.Busy);
            if (hidden) return;

            if (_lineMat != null) _lineMat.mainTextureOffset = new Vector2(-Time.unscaledTime * 0.45f, 0f);
            PlaceBadges();
            if (IsPicking) UpdatePick();
        }

        // ==================== КОЛЬЦА СЕТИ ====================

        private void RefreshZone()
        {
            var net = EspionageManager.Network;
            var sys = new List<StarSystem>();
            var vals = new List<float>();
            var key = new System.Text.StringBuilder();
            if (net != null)
                foreach (var kv in net)
                {
                    if (kv.Value < MinShown) continue;
                    var s = EmpireStats.GetSystem(kv.Key);
                    if (s == null) continue;
                    sys.Add(s);
                    vals.Add(kv.Value);
                    key.Append(kv.Key).Append(':').Append(Mathf.RoundToInt(kv.Value)).Append(';');
                }
            string k = key.ToString();
            if (k == _netKey) return;
            _netKey = k;
            BuildRings(sys, vals);
        }

        /// <summary>
        /// Вокруг каждой системы сети — тонкое кольцо-шкала: тусклая дорожка и яркая дуга от верха по часовой,
        /// длина дуги — сила сети в системе (полный круг — 100 %). Империю целиком ничем не обводим.
        /// </summary>
        private void BuildRings(List<StarSystem> sys, List<float> vals)
        {
            if (_zone == null)
            {
                _zone = new GameObject("SpyNetworkRings");
                _zone.transform.SetParent(transform, false);
            }
            for (int i = _zone.transform.childCount - 1; i >= 0; i--) Destroy(_zone.transform.GetChild(i).gameObject);
            if (_zoneMat == null) return;
            for (int i = 0; i < sys.Count; i++)
            {
                float frac = Mathf.Clamp01(vals[i] / 100f);
                var c = Teal;
                c.a = 0.16f;
                Ring(sys[i].Position, 1f, c, RingWidth * 0.8f);
                c = Color.Lerp(Teal, Color.white, 0.2f);
                c.a = 0.55f + 0.35f * frac;
                Ring(sys[i].Position, frac, c, RingWidth);
            }
        }

        private void Ring(Vector3 center, float frac, Color c, float width)
        {
            if (frac <= 0.005f) return;
            int n = Mathf.Max(3, Mathf.CeilToInt(RingSegments * frac));
            var go = new GameObject("NetRing");
            go.transform.SetParent(_zone.transform, false);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = _zoneMat;
            lr.alignment = LineAlignment.TransformZ;                         // лента лежит в плоскости карты
            lr.useWorldSpace = true;
            lr.loop = frac >= 0.999f;
            lr.widthMultiplier = width;
            lr.numCapVertices = 2;
            lr.sortingOrder = 6;                                             // под звёздами (14) и флотами
            lr.startColor = lr.endColor = c;
            lr.positionCount = lr.loop ? RingSegments : n + 1;
            for (int j = 0; j < lr.positionCount; j++)
            {
                float t = lr.loop ? (float)j / RingSegments : frac * j / n;
                float ang = Mathf.PI * 0.5f - t * Mathf.PI * 2f;               // от верха по часовой
                lr.SetPosition(j, center + new Vector3(Mathf.Cos(ang) * RingRadius, 0.04f, Mathf.Sin(ang) * RingRadius));
            }
        }

        // ==================== ВЫБОР СИСТЕМЫ ДЛЯ АГЕНТА ====================

        /// <summary>
        /// Игрок сам выбирает на карте систему империи owner, откуда агент будет строить сеть
        /// (новое внедрение или перенос внутри той же империи). Окна закрываются, карта — в режиме «Шпионаж».
        /// </summary>
        public static void BeginPick(Leader agent, int owner)
        {
            if (Instance == null || agent == null || owner <= 0) return;
            Instance.StartPick(agent, owner);
        }

        private void StartPick(Leader agent, int owner)
        {
            ClearPickMarks();
            _pickAgent = agent;
            _pickOwner = owner;
            _pickHover = null;
            EspionageWindow.Instance?.Close();
            MapModeController.Instance?.SetMode(MapModeController.MapMode.Intel);

            StarSystem focus = EmpireStats.GetSystem(agent.TargetOwner == owner ? agent.TargetSystem : EspionageManager.SuggestSystem(owner));
            foreach (var s in EmpireStats.Systems)
            {
                if (s.OwnerId != owner || _ringSprite == null) continue;
                var go = new GameObject("SpyPickMark");
                go.transform.SetParent(transform, false);
                go.transform.position = s.Position + Vector3.up * 0.1f;
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _ringSprite;
                if (_markMat != null) sr.sharedMaterial = _markMat;
                sr.sortingOrder = 13;
                _pickMarks.Add(new KeyValuePair<StarSystem, SpriteRenderer>(s, sr));
            }
            if (_pickMarks.Count == 0)
            {
                NotificationCenter.Show(Loc.T("Некуда внедриться"), Loc.T("У этой империи нет систем"), NotificationCenter.Kind.Warning, 4f);
                EndPick();
                return;
            }
            if (focus != null)
            {
                var cam = FindFirstObjectByType<StellarisClone.Cam.StrategyCameraController>();
                if (cam != null) cam.FocusOn(focus.Position);
            }
            string who = Contacts.NameForPlayer(owner);
            _bannerText.text = Loc.F("<b>{0}</b>: выберите систему империи <color={1}>{2}</color>, откуда строить разведсеть", agent.Name, LGBuild.Hex(FleetIndicator.OwnerColor(owner)), who) +
                               Loc.T("   <color=#8AA2A8>ЛКМ — выбрать · ПКМ или Esc — отмена</color>");
            _banner.gameObject.SetActive(true);
        }

        private void UpdatePick()
        {
            if (LoadingScreen.IsActive) return;
            bool alive = false;
            foreach (var a in EspionageManager.PlayerAgents()) if (a == _pickAgent) { alive = true; break; }
            if (!alive || Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)) { EndPick(); return; }

            if (_cam == null) _cam = Camera.main;
            float k = Screen.height / 1080f;
            _banner.localScale = Vector3.one * k;

            // Звезда-кандидат под курсором: ближайшая к точке на плоскости карты
            _pickHover = null;
            var ray = _cam.ScreenPointToRay(Input.mousePosition);
            float planeY = _pickMarks[0].Key.Position.y;
            if (Mathf.Abs(ray.direction.y) > 1e-4f)
            {
                Vector3 p = ray.origin + ray.direction * ((planeY - ray.origin.y) / ray.direction.y);
                float best = 5f;
                foreach (var m in _pickMarks)
                {
                    float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(m.Key.Position.x, m.Key.Position.z));
                    if (d < best) { best = d; _pickHover = m.Key; }
                }
            }

            float t = Time.unscaledTime;
            var purple = MapModeController.SpyPurple;
            foreach (var m in _pickMarks)
            {
                bool hov = m.Key == _pickHover;
                float pulse = 1f + 0.08f * Mathf.Sin(t * 4f + m.Key.Id);
                m.Value.transform.localScale = Vector3.one * (hov ? 8f : 5.5f) * pulse;
                m.Value.transform.rotation = Quaternion.Euler(90f, t * (hov ? 60f : 20f), 0f);
                float a = hov ? 1f : 0.55f;
                m.Value.color = new Color(purple.r * a, purple.g * a, purple.b * a, a);
            }

            bool overUi = UnityEngine.EventSystems.EventSystem.current != null
                          && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
            if (!Input.GetMouseButtonDown(0) || overUi) return;
            if (_pickHover != null) { Confirm(_pickHover); return; }
            NotificationCenter.Show(Loc.T("Не та система"), Loc.F("Нужна система империи {0} — они помечены кольцами", Contacts.NameForPlayer(_pickOwner)), NotificationCenter.Kind.Info, 3f);
        }

        private void Confirm(StarSystem s)
        {
            var a = _pickAgent;
            int owner = _pickOwner;
            EndPick();
            if (a.Post == LeaderPost.Spy && a.TargetOwner == owner)
            {
                if (EspionageManager.Instance == null || !EspionageManager.Instance.Relocate(a, s.Id)) return;
            }
            else
            {
                LeaderManager.Instance?.AssignSpy(a, owner, s.Id);
                SFXManager.Play(StellarisClone.Core.Audio.Sfx.UiConfirm);
            }
            NotificationCenter.Show(Loc.T("Агент на месте"), Loc.F("{0} строит разведсеть из системы {1}", a.Name, s.Name), NotificationCenter.Kind.Success, 5f);
            _scan = 0f;
        }

        private void EndPick()
        {
            _pickAgent = null;
            _pickOwner = -1;
            _pickHover = null;
            ClearPickMarks();
            if (_banner != null) _banner.gameObject.SetActive(false);
        }

        private void ClearPickMarks()
        {
            foreach (var m in _pickMarks) if (m.Value != null) Destroy(m.Value.gameObject);
            _pickMarks.Clear();
        }

        private void BuildBanner()
        {
            var bg = LGBuild.Panel(_canvas.transform, "SpyPickBanner", new Color(0.04f, 0.05f, 0.09f, 0.92f));
            _banner = bg.rectTransform;
            _banner.anchorMin = _banner.anchorMax = new Vector2(0.5f, 1f);
            _banner.pivot = new Vector2(0.5f, 1f);
            _banner.sizeDelta = new Vector2(860f, 34f);
            _banner.anchoredPosition = new Vector2(0f, -200f * Screen.height / 1080f);
            var rim = LGBuild.Panel(_banner, "Rim", new Color(0.74f, 0.56f, 1f, 0.8f));
            rim.rectTransform.anchorMin = new Vector2(0f, 0f);
            rim.rectTransform.anchorMax = new Vector2(1f, 0f);
            rim.rectTransform.sizeDelta = new Vector2(0f, 2f);
            _bannerText = LGBuild.Label(_banner, "", 13, Color.white, TextAnchor.MiddleCenter);
            _bannerText.rectTransform.Stretch(12, 0, 12, 0);
            _bannerText.raycastTarget = false;
            LG.Ignore(_banner.gameObject);
            _banner.gameObject.SetActive(false);
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
                v.Busy = a.OpKind >= 0;
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
                lr.textureScale = new Vector2(0.12f, 1f);                       // одна комета на ~8 единиц
                lr.alignment = LineAlignment.TransformZ;
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);       // лента лежит в плоскости карты
                lr.widthMultiplier = 0.9f;
                lr.numCapVertices = 0;
                lr.sortingOrder = 12;
                // от края звезды к краю соседней — поток не залезает на сами звёзды
                Vector3 dir = (n.Position - s.Position).normalized;
                lr.positionCount = 2;
                lr.SetPosition(0, s.Position + dir * 2.2f + Vector3.up * 0.05f);
                lr.SetPosition(1, n.Position - dir * 2.2f + Vector3.up * 0.05f);
                // бирюзовый поток интерфейса: ярко у агента, к соседу гаснет
                lr.startColor = new Color(1f, 1f, 1f, 0.95f);                   // цвет — в самой текстуре
                lr.endColor = new Color(1f, 1f, 1f, 0.35f);
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

            // Плашка с процентом — тоже часть значка: наведение на неё показывает подсказку, клик открывает агента
            var chip = LGBuild.Panel(root, "Chip", new Color(0.03f, 0.06f, 0.08f, 0.92f), raycast: true);
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
            sb.Append(Loc.F("<b><color=#F2C766>{0}</color></b>  ·  агент, уровень {1}\n", a.Name, a.Level));
            sb.Append(Loc.F("Идёт построение разведсети из системы <color=#F2C766>{0}</color> в империи <color={1}>{2}</color>\n\n", s?.Name, oc, owner));
            if (a.OpKind >= 0)
                sb.Append(Loc.F("<color=#F2C766>Идёт операция «{0}», ещё {1} мес. — сеть не растёт</color>\n\n", SpyInfo.Name((SpyOp)a.OpKind), a.OpMonthsLeft));
            sb.Append(Loc.F("Рост сети в системе: <color=#66F59E>+{0:0.#}</color> в месяц\n", g.Total));
            sb.Append(Loc.F("   Базовое значение: +{0:0}\n", g.Base));
            sb.Append(Loc.F("   Уровень агента: +{0:0}\n", g.Level));
            if (g.TraitMul > 1f) sb.Append(Loc.F("   Черта «Внедренец»: ×{0:0.##}\n", g.TraitMul));
            if (Mathf.Abs(g.TechMul - 1f) > 0.001f) sb.Append(Loc.F("   Технологии: ×{0:0.##}\n", g.TechMul));
            sb.Append(Loc.F("   Контрразведка цели: <color=#FF9F7A>×{0:0.##}</color>\n", g.CounterMul));
            sb.Append(Loc.F("Соседние системы: ×{0:0.#}, через одну: ×{1:0.#}\n\n", EspionageManager.NetRings[1], EspionageManager.NetRings[2]));
            sb.Append(Loc.F("Сеть в системе: <color=#BD8FFF>{0:0}%</color>   ·   в империи: <color=#BD8FFF>{1:0}%</color>\n", local, a.Cover));
            sb.Append(Loc.F("<color=#8AA2A8>{0:0}% — обзор территории и флотов, {1:0}% — досье. ЛКМ — открыть агента</color>", EspionageManager.IntelCover, EspionageManager.DeepIntelCover));
            return sb.ToString();
        }

        private static void Drop(AgentVis v)
        {
            foreach (var l in v.Lines) if (l != null) Destroy(l.gameObject);
            if (v.Badge != null) Destroy(v.Badge.gameObject);
        }

        /// <summary>
        /// Поток данных: слабый светящийся канал и бегущие по нему «кометы» — яркая голова и длинный гаснущий хвост
        /// (одна на период текстуры). Поперёк — мягкий профиль, края растворяются.
        /// </summary>
        private static Texture2D StreamTexture()
        {
            const int w = 128, h = 16;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "SpyStream" };
            var px = new Color32[w * h];
            for (int x = 0; x < w; x++)
            {
                // голова у u = 0,92, хвост тянется назад (к началу периода) — кометы летят по направлению линии
                float d = x / (float)w - 0.92f;
                float tail = d <= 0f ? Mathf.Exp(d / 0.18f) : 0f;
                float head = Mathf.Exp(-(d / 0.014f) * (d / 0.014f));
                for (int y = 0; y < h; y++)
                {
                    float v = (y + 0.5f) / h - 0.5f, v2 = v * v;
                    float a = 0.14f * Mathf.Exp(-v2 / 0.03f)                              // слабый канал
                            + 0.75f * tail * Mathf.Exp(-v2 / (0.006f + 0.02f * tail))     // хвост сужается
                            + head * Mathf.Exp(-v2 / 0.02f);                              // яркая голова
                    float white = Mathf.Clamp01(head + 0.35f * tail);
                    var c = Color.Lerp(Teal, Color.white, white);
                    px[y * w + x] = new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)(Mathf.Clamp01(a) * 255));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }
    }
}
