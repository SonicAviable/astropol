using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Значок флота в духе Stellaris:
    ///   • постоянный экранный размер — читается и при большом отдалении; контрастная тёмная обводка;
    ///   • фон и кромка — цвет владельца (свои — бирюза, враг — красный);
    ///   • глиф — тип флота, а во время действия — само действие (разведка, стройка, бой, прыжок);
    ///   • корабли одного владельца и типа на одной орбите сливаются в один значок со счётчиком;
    ///   • у военных — сила под значком, у повреждённых — полоска прочности;
    ///   • при выделении — пульсирующее кольцо, вспышка и яркая линия к кораблю;
    ///   • по завершении действия — короткая вспышка с галочкой.
    /// Значки не перекрывают друг друга: раскладка расталкивает их в экранных координатах.
    /// </summary>
    public class FleetIndicator : MonoBehaviour
    {
        // ==================== РЕЕСТР И РАСКЛАДКА ====================

        public static readonly List<FleetIndicator> All = new List<FleetIndicator>();

        /// <summary>Диаметр значка на экране, пикселей.</summary>
        public const float IconPixels = 46f;
        private const float BadgeWorldSize = 0.86f;   // диаметр обводки при масштабе 1
        private const float MinSeparationPx = IconPixels * 1.08f;

        private static int s_layoutFrame = -1;

        public FleetView Fleet => _fleet;
        /// <summary>Значок виден (лидер группы); остальные корабли группы его не рисуют.</summary>
        public bool IsLeader { get; private set; } = true;
        /// <summary>Корабли, которые представляет значок (сам флот + слитые с ним).</summary>
        public readonly List<FleetView> Members = new List<FleetView>();
        /// <summary>Центр значка на экране и его радиус (для выбора кликом и рамкой).</summary>
        public Vector2 ScreenPos { get; private set; }
        public float ScreenRadius => IconPixels * 0.5f * _sizeMul;
        public bool OnScreen { get; private set; }

        private Vector3 _anchorWorld;     // точка над кораблём до раскладки
        private Vector2 _layoutOffsetPx;  // смещение после расталкивания
        private float _sizeMul = 1f;

        private FleetView _fleet;
        private Camera _camera;
        private Transform _badge;

        private SpriteRenderer _outlineSr;
        private SpriteRenderer _bloomSr;
        private SpriteRenderer _discSr;
        private SpriteRenderer _rimSr;
        private SpriteRenderer _ringA;
        private SpriteRenderer _ringB;
        private SpriteRenderer _glyphSr;
        private SpriteRenderer _sweepSr;
        private SpriteRenderer _pipSr;
        private SpriteRenderer _pulseSr;
        private SpriteRenderer _flashSr;
        private LineRenderer _stem;

        // Счётчик кораблей
        private GameObject _countRoot;
        private SpriteRenderer _countBg;
        private TextMesh _countText, _countShadow;

        // Сила флота
        private GameObject _powerRoot;
        private SpriteRenderer _powerIcon;
        private TextMesh _powerText, _powerShadow;

        // Прочность
        private GameObject _hpRoot;
        private SpriteRenderer _hpBg, _hpFill;

        private float _anim;
        private Color _owner;      // цвет владельца
        private Color _accent;     // цвет глифа/действия
        private string _styleKey;
        private bool _selected;
        private float _selectPunch;
        private float _selectFlash;   // 1 → 0 после выделения
        private float _eventSeen = -100f;
        private float _eventFlash;    // 1 → 0 после завершения действия

        private static Sprite _bloomSp, _discSp, _rimSp, _ringSp, _dashSp;
        private static Sprite _scienceSp, _militarySp, _constructorSp;
        private static Sprite _sweepSp, _sparkSp, _pipSp, _squareSp;
        private static Material _spriteMat;
        private static Material _lineMat;
        private static Font _font;

        public static readonly Color OwnColor = new Color(0.22f, 0.92f, 0.86f);
        public static readonly Color EnemyColor = new Color(1.00f, 0.30f, 0.30f);

        public void Init(FleetView fleet)
        {
            _fleet = fleet;
            _camera = Camera.main;
            EnsureArt();
            if (_squareSp == null) _squareSp = SquareSprite();
            if (_font == null) _font = GameFont.Bold;

            _badge = new GameObject("Badge").transform;
            _badge.SetParent(transform, false);

            _bloomSr   = Layer(_badge, "Bloom",   _bloomSp, 40, 1.30f);
            _outlineSr = Layer(_badge, "Outline", _discSp,  41, 1.02f);
            _discSr    = Layer(_badge, "Disc",    _discSp,  42, 0.80f);
            _rimSr     = Layer(_badge, "Rim",     _rimSp,   43, 0.84f);
            _ringA     = Layer(_badge, "RingA",   _dashSp,  44, 0.98f);
            _ringB     = Layer(_badge, "RingB",   _ringSp,  44, 1.10f);
            _sweepSr   = Layer(_badge, "Sweep",   _sweepSp, 45, 0.90f);
            _glyphSr   = Layer(_badge, "Glyph",   _scienceSp, 46, 0.70f);
            _pulseSr   = Layer(_badge, "Pulse",   _ringSp,  47, 1.0f);
            _flashSr   = Layer(_badge, "Flash",   _bloomSp, 48, 1.0f);
            _pulseSr.enabled = false;
            _flashSr.enabled = false;

            _pipSr = Layer(transform, "ShipPip", _pipSp, 39, 0.22f);

            // Счётчик — кружок в правом верхнем углу
            _countRoot = new GameObject("Count");
            _countRoot.transform.SetParent(_badge, false);
            _countRoot.transform.localPosition = new Vector3(0.36f, 0.34f, -0.03f);
            _countBg = Layer(_countRoot.transform, "Bg", _discSp, 49, 0.42f);
            _countShadow = MakeText(_countRoot.transform, "Shadow", 50, new Vector3(0.012f, -0.012f, 0f), TextAnchor.MiddleCenter, 0.036f);
            _countText = MakeText(_countRoot.transform, "Num", 51, Vector3.zero, TextAnchor.MiddleCenter, 0.036f);
            _countRoot.SetActive(false);

            // Прочность — полоска под значком
            _hpRoot = new GameObject("Hp");
            _hpRoot.transform.SetParent(_badge, false);
            _hpRoot.transform.localPosition = new Vector3(0f, -0.56f, -0.02f);
            _hpBg = Layer(_hpRoot.transform, "Bg", _squareSp, 47, 1f);
            _hpBg.transform.localScale = new Vector3(0.74f, 0.10f, 1f);
            _hpBg.color = new Color(0f, 0.02f, 0.04f, 0.9f);
            _hpFill = Layer(_hpRoot.transform, "Fill", _squareSp, 48, 1f);
            _hpRoot.SetActive(false);

            // Сила — иконка оружия и число
            _powerRoot = new GameObject("Power");
            _powerRoot.transform.SetParent(_badge, false);
            _powerRoot.transform.localPosition = new Vector3(0f, -0.76f, -0.02f);
            _powerIcon = Layer(_powerRoot.transform, "Icon", LGIcons.Get(LGIcon.Weapons), 49, 0.17f);
            _powerShadow = MakeText(_powerRoot.transform, "Shadow", 49, new Vector3(0.01f, -0.01f, 0f), TextAnchor.MiddleLeft, 0.028f);
            _powerText = MakeText(_powerRoot.transform, "Num", 50, Vector3.zero, TextAnchor.MiddleLeft, 0.028f);
            _powerRoot.SetActive(false);

            var stemGo = new GameObject("Stem");
            stemGo.transform.SetParent(transform, false);
            _stem = stemGo.AddComponent<LineRenderer>();
            _stem.positionCount = 2;
            _stem.useWorldSpace = true;
            _stem.startWidth = 0.02f;
            _stem.endWidth = 0.008f;
            _stem.numCapVertices = 4;
            _stem.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _stem.receiveShadows = false;
            _stem.sortingOrder = 38;
            if (_lineMat != null) _stem.material = _lineMat;

            _eventSeen = fleet.Data != null ? fleet.Data.LastEventTime : -100f;
            Members.Add(fleet);
            All.Add(this);
            RefreshStyle();
        }

        private void OnDestroy() => All.Remove(this);

        private static SpriteRenderer Layer(Transform parent, string name, Sprite sp, int order, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp;
            sr.sortingOrder = order;
            if (_spriteMat != null) sr.sharedMaterial = _spriteMat;
            return sr;
        }

        private static TextMesh MakeText(Transform parent, string name, int order, Vector3 pos, TextAnchor anchor, float charSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos + new Vector3(0f, 0f, -0.01f);
            var tm = go.AddComponent<TextMesh>();
            tm.font = _font;
            tm.fontSize = 64;
            tm.characterSize = charSize;
            tm.fontStyle = FontStyle.Bold;
            tm.anchor = anchor;
            tm.alignment = TextAlignment.Center;
            var mr = go.GetComponent<MeshRenderer>();
            if (_font != null) mr.sharedMaterial = _font.material;
            mr.sortingOrder = order;
            return tm;
        }

        // ==================== ЦИКЛ ====================

        private void LateUpdate()
        {
            if (_fleet == null) { Destroy(gameObject); return; }
            var data = _fleet.Data;
            if (data == null) return;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            if (s_layoutFrame != Time.frameCount) Layout(_camera);

            float dt = Time.unscaledDeltaTime;
            _anim += dt;

            bool sel = false;
            var fm = FleetManager.Instance;
            if (fm != null) foreach (var m in Members) if (fm.IsSelected(m)) { sel = true; break; }
            if (sel && !_selected) _selectFlash = 1f;
            _selected = sel;
            _selectPunch = Mathf.MoveTowards(_selectPunch, sel ? 1f : 0f, dt * 6f);
            _selectFlash = Mathf.MoveTowards(_selectFlash, 0f, dt / 0.35f);

            if (data.LastEventTime > _eventSeen + 0.01f)
            {
                _eventSeen = data.LastEventTime;
                if (data.LastEvent != FleetData.FleetEvent.None) _eventFlash = 1f;
            }
            _eventFlash = Mathf.MoveTowards(_eventFlash, 0f, dt / 1.6f);

            _badge.gameObject.SetActive(IsLeader);
            _stem.enabled = IsLeader;

            RefreshStyle();
            Place();
            if (IsLeader)
            {
                Animate();
                RefreshInfo();
            }
        }

        /// <summary>
        /// Раскладка всех значков на кадр: слить стоящие вместе корабли одного владельца и типа,
        /// затем растолкать пересекающиеся значки на экране.
        /// </summary>
        private static void Layout(Camera cam)
        {
            s_layoutFrame = Time.frameCount;
            All.RemoveAll(i => i == null || i._fleet == null);

            var groups = new Dictionary<long, FleetIndicator>();
            foreach (var ind in All)
            {
                ind.Members.Clear();
                ind.Members.Add(ind._fleet);
                ind.IsLeader = true;
            }
            foreach (var ind in All)
            {
                var d = ind._fleet.Data;
                if (d == null || d.Destroyed || d.State == FleetState.InHyperlane) continue;
                long key = ((long)d.CurrentSystemId << 16) | ((long)(d.OwnerId + 8) << 4) | (long)d.Type;
                if (!groups.TryGetValue(key, out var leader)) { groups[key] = ind; continue; }
                // Лидер — корабль с меньшим Id (значок не «прыгает» между кораблями)
                if (d.Id < leader._fleet.Data.Id)
                {
                    ind.Members.Clear();
                    ind.Members.AddRange(leader.Members);
                    ind.Members.Insert(0, ind._fleet);
                    leader.IsLeader = false;
                    leader.Members.Clear();
                    groups[key] = ind;
                }
                else
                {
                    leader.Members.Add(ind._fleet);
                    ind.IsLeader = false;
                    ind.Members.Clear();
                }
            }

            // Экранные позиции лидеров
            var leaders = new List<FleetIndicator>();
            foreach (var ind in All)
            {
                ind._anchorWorld = ind._fleet.transform.position + Vector3.up * 2.2f;
                ind._layoutOffsetPx = Vector2.zero;
                if (!ind.IsLeader) { ind.OnScreen = false; continue; }
                Vector3 sp = cam.WorldToScreenPoint(ind._anchorWorld);
                ind.OnScreen = sp.z > 0f && sp.x > -60 && sp.y > -60 && sp.x < Screen.width + 60 && sp.y < Screen.height + 60;
                ind.ScreenPos = sp;
                ind._layoutOffsetPx = Vector2.zero;
                if (ind.OnScreen) leaders.Add(ind);
            }

            // Расталкивание (несколько проходов попарно)
            for (int pass = 0; pass < 4; pass++)
            {
                for (int i = 0; i < leaders.Count; i++)
                for (int j = i + 1; j < leaders.Count; j++)
                {
                    var a = leaders[i]; var b = leaders[j];
                    Vector2 pa = a.ScreenPos + a._layoutOffsetPx, pb = b.ScreenPos + b._layoutOffsetPx;
                    Vector2 delta = pb - pa;
                    float dist = delta.magnitude;
                    float need = MinSeparationPx * Mathf.Max(a._sizeMul, b._sizeMul);
                    if (dist >= need) continue;
                    Vector2 dir = dist > 0.01f ? delta / dist : new Vector2(a._fleet.Data.Id < b._fleet.Data.Id ? 1f : -1f, 0f);
                    // Расталкиваем в основном по горизонтали — так значки выстраиваются в ряд
                    dir = new Vector2(dir.x, dir.y * 0.35f);
                    if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;
                    dir.Normalize();
                    Vector2 push = dir * (need - dist) * 0.5f;
                    a._layoutOffsetPx -= push;
                    b._layoutOffsetPx += push;
                }
            }
            foreach (var l in leaders) l.ScreenPos += l._layoutOffsetPx;
        }

        private static float WorldPerPixel(Camera cam, float distance)
            => 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);

        private void Place()
        {
            Vector3 ship = _fleet.transform.position;
            Transform ct = _camera.transform;
            float dist = Mathf.Max(1f, Vector3.Distance(_anchorWorld, ct.position));
            float wpp = WorldPerPixel(_camera, dist);

            float hover = (Mathf.Sin(_anim * 2.15f) * 0.10f + Mathf.Sin(_anim * 1.07f) * 0.04f) * wpp * 20f;
            Vector3 offset = ct.right * (_layoutOffsetPx.x * wpp) + ct.up * (_layoutOffsetPx.y * wpp + hover);
            Vector3 iconPos = _anchorWorld + offset;
            transform.position = iconPos;
            transform.rotation = ct.rotation;

            _sizeMul = 1f + _selectPunch * 0.12f;
            float scale = IconPixels * _sizeMul * wpp / BadgeWorldSize;
            transform.localScale = Vector3.one * scale;

            _pipSr.transform.position = ship + Vector3.up * 0.95f;
            _pipSr.transform.rotation = ct.rotation;
            _pipSr.transform.localScale = Vector3.one * Mathf.Clamp(0.22f / scale, 0.08f, 0.6f);

            _stem.SetPosition(0, ship + Vector3.up * 1.05f);
            _stem.SetPosition(1, iconPos - ct.up * (BadgeWorldSize * 0.5f * scale));
            float w = Mathf.Lerp(1.6f, 3.2f, _selectPunch) * wpp;
            _stem.startWidth = w * 0.4f;
            _stem.endWidth = w;
        }

        // ==================== ВНЕШНИЙ ВИД ====================

        private Sprite GlyphFor(FleetData d, out float scale)
        {
            if (_eventFlash > 0.35f) { scale = 0.40f; return LGIcons.Get(LGIcon.Check); }
            if (d.InCombat) { scale = 0.42f; return LGIcons.Get(LGIcon.Swords); }
            switch (d.State)
            {
                case FleetState.Surveying: scale = 0.42f; return LGIcons.Get(LGIcon.Sensors);
                case FleetState.Constructing: scale = 0.42f; return LGIcons.Get(LGIcon.Construction);
                case FleetState.InHyperlane: scale = 0.40f; return LGIcons.Get(LGIcon.Fast);
            }
            var siege = SiegeManager.Instance != null ? SiegeManager.Instance.GetSiege(d.CurrentSystemId) : null;
            if (d.Type == FleetType.Military && siege != null && siege.Attacker == d.OwnerId && siege.Active)
            { scale = 0.42f; return LGIcons.Get(LGIcon.Siege); }

            scale = 0.70f;
            return d.Type switch
            {
                FleetType.Science => _scienceSp,
                FleetType.Constructor => _constructorSp,
                _ => _militarySp
            };
        }

        private void RefreshStyle()
        {
            var d = _fleet.Data;
            _owner = d.OwnerId == 0 ? OwnColor
                   : d.OwnerId == AIEmpireManager.AIOwnerId ? EnemyColor
                   : new Color(0.75f, 0.75f, 0.8f);

            Color typeCol = d.Type switch
            {
                FleetType.Science => new Color(0.45f, 1f, 0.62f),
                FleetType.Constructor => new Color(1f, 0.82f, 0.36f),
                _ => new Color(0.92f, 0.98f, 1f)
            };
            if (_eventFlash > 0.35f) typeCol = new Color(0.45f, 1f, 0.55f);
            else if (d.InCombat) typeCol = new Color(1f, 0.55f, 0.35f);
            else if (d.State == FleetState.InHyperlane) typeCol = Color.Lerp(typeCol, new Color(1f, 0.88f, 0.4f), 0.5f);
            _accent = typeCol;

            var glyph = GlyphFor(d, out float gScale);
            string key = $"{glyph.GetInstanceID()}|{d.OwnerId}|{d.InCombat}";
            if (key == _styleKey) return;
            _styleKey = key;

            _glyphSr.sprite = glyph;
            _glyphSr.transform.localScale = Vector3.one * gScale;
            _glyphSr.transform.localRotation = Quaternion.identity;

            _outlineSr.color = new Color(0f, 0.01f, 0.02f, 0.88f);
            _discSr.color = new Color(_owner.r * 0.16f, _owner.g * 0.16f, _owner.b * 0.16f, 0.96f);
            _countBg.color = Color.Lerp(_owner, Color.white, 0.15f);
            _countText.color = new Color(0.02f, 0.05f, 0.07f, 1f);
            _countShadow.color = new Color(1f, 1f, 1f, 0f);
            _powerIcon.color = Color.Lerp(_owner, Color.white, 0.35f);
            _powerText.color = Color.white;
            _powerShadow.color = new Color(0f, 0f, 0f, 0.9f);
            _pipSr.color = _owner;
            _sweepSr.enabled = d.State == FleetState.Surveying;
        }

        private void Animate()
        {
            var d = _fleet.Data;
            bool busy = d.State != FleetState.Orbiting || d.InCombat;
            float beat = d.InCombat ? 8.5f : busy ? 4.4f : 2.35f;
            float breathe = 0.5f + 0.5f * Mathf.Sin(_anim * beat);

            Color rimCol = d.InCombat ? Color.Lerp(_owner, new Color(1f, 0.2f, 0.15f), 0.5f + 0.5f * breathe) : _owner;
            _rimSr.color = Color.Lerp(rimCol, Color.white, 0.12f + breathe * 0.15f + _selectPunch * 0.25f);
            _bloomSr.color = new Color(rimCol.r, rimCol.g, rimCol.b, 0.18f + breathe * 0.10f + _selectPunch * 0.18f);
            _bloomSr.transform.localScale = Vector3.one * (1.24f + breathe * 0.05f + _selectPunch * 0.1f);

            float spinA = d.Type == FleetType.Science ? 42f : d.Type == FleetType.Constructor ? -28f : 16f;
            if (d.InCombat) spinA = 110f;
            if (d.State == FleetState.Surveying) spinA = 85f;
            _ringA.transform.localRotation = Quaternion.Euler(0, 0, _anim * spinA);
            _ringB.transform.localRotation = Quaternion.Euler(0, 0, -_anim * spinA * 0.6f);
            _ringA.color = new Color(rimCol.r, rimCol.g, rimCol.b, (busy ? 0.65f : 0.35f) + breathe * 0.25f);
            _ringB.color = new Color(rimCol.r, rimCol.g, rimCol.b, _selectPunch * (0.5f + 0.4f * breathe));

            _glyphSr.color = Color.Lerp(_accent, Color.white, 0.25f + breathe * 0.2f);
            if (d.State == FleetState.Constructing)
                _glyphSr.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(_anim * 3f) * 10f);

            if (_sweepSr.enabled)
            {
                _sweepSr.transform.localRotation = Quaternion.Euler(0, 0, -_anim * 95f);
                _sweepSr.color = new Color(_accent.r, _accent.g, _accent.b, 0.12f + 0.28f * breathe);
            }

            // Выделение: кольцо, которое расходится волнами
            _pulseSr.enabled = _selectPunch > 0.01f;
            if (_pulseSr.enabled)
            {
                float t = Mathf.Repeat(_anim / 1.1f, 1f);
                _pulseSr.transform.localScale = Vector3.one * Mathf.Lerp(0.95f, 1.55f, t);
                _pulseSr.color = new Color(_owner.r, _owner.g, _owner.b, (1f - t) * 0.85f * _selectPunch);
            }

            // Вспышка при выделении или по завершении действия
            float flash = Mathf.Max(_selectFlash, _eventFlash > 0.35f ? (_eventFlash - 0.35f) / 0.65f : 0f);
            _flashSr.enabled = flash > 0.01f;
            if (_flashSr.enabled)
            {
                Color fc = _selectFlash >= _eventFlash ? Color.Lerp(_owner, Color.white, 0.5f) : new Color(0.5f, 1f, 0.6f);
                _flashSr.color = new Color(fc.r, fc.g, fc.b, flash * 0.9f);
                _flashSr.transform.localScale = Vector3.one * Mathf.Lerp(2.4f, 1.1f, flash);
            }

            float sa = 0.45f + breathe * 0.2f + _selectPunch * 0.35f;
            _stem.startColor = new Color(_owner.r, _owner.g, _owner.b, 0.15f);
            _stem.endColor = new Color(_owner.r, _owner.g, _owner.b, sa);
            _pipSr.color = new Color(_owner.r, _owner.g, _owner.b, 0.7f + breathe * 0.3f);
        }

        /// <summary>Счётчик кораблей, сила и прочность группы.</summary>
        private void RefreshInfo()
        {
            int count = 0;
            float power = 0f, hp = 0f, hpMax = 0f;
            bool military = false;
            foreach (var m in Members)
            {
                var d = m?.Data;
                if (d == null || d.Destroyed) continue;
                count++;
                if (d.Type == FleetType.Military) { military = true; power += CombatMath.Power(d); }
                hp += d.HullPoints + d.ArmorPoints;
                hpMax += d.MaxHullPoints + d.MaxArmorPoints;
            }

            bool showCount = count > 1;
            if (_countRoot.activeSelf != showCount) _countRoot.SetActive(showCount);
            if (showCount)
            {
                string c = count.ToString();
                if (_countText.text != c) { _countText.text = c; _countShadow.text = c; }
            }

            if (_powerRoot.activeSelf != military) _powerRoot.SetActive(military);
            if (military)
            {
                string p = Mathf.RoundToInt(power).ToString();
                if (_powerText.text != p) { _powerText.text = p; _powerShadow.text = p; }
                // Иконка + число по центру под значком
                float textW = p.Length * 0.10f;
                float total = 0.2f + textW;
                _powerIcon.transform.localPosition = new Vector3(-total * 0.5f + 0.08f, 0f, 0f);
                _powerText.transform.localPosition = new Vector3(-total * 0.5f + 0.19f, 0f, -0.01f);
                _powerShadow.transform.localPosition = _powerText.transform.localPosition + new Vector3(0.01f, -0.01f, 0.005f);
            }

            float frac = hpMax > 0f ? Mathf.Clamp01(hp / hpMax) : 1f;
            bool showHp = frac < 0.995f;
            if (_hpRoot.activeSelf != showHp) _hpRoot.SetActive(showHp);
            if (showHp)
            {
                const float W = 0.70f;
                _hpFill.transform.localScale = new Vector3(W * frac, 0.06f, 1f);
                _hpFill.transform.localPosition = new Vector3(-W * 0.5f + W * frac * 0.5f, 0f, -0.005f);
                _hpFill.color = frac > 0.6f ? new Color(0.36f, 0.96f, 0.5f)
                              : frac > 0.3f ? new Color(1f, 0.82f, 0.3f)
                              : new Color(1f, 0.32f, 0.3f);
            }
            // Если полоски нет — сила поднимается ближе к значку
            _powerRoot.transform.localPosition = new Vector3(0f, showHp ? -0.76f : -0.62f, -0.02f);
        }

        private static Sprite SquareSprite()
        {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
            var px = new Color[16];
            for (int i = 0; i < px.Length; i++) px[i] = Color.white;
            t.SetPixels(px);
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        }

        // ==================== ART ====================

        public static void EnsureArt()
        {
            if (_discSp != null) return;

            var sh = ShaderCache.Sprite;
            if (sh != null)
            {
                _spriteMat = new Material(sh) { color = Color.white };
            }
            var unlit = ShaderCache.Unlit;
            if (unlit != null)
                _lineMat = new Material(unlit) { color = Color.white };

            const int R = 192;
            _bloomSp = RadialGlow(R);
            _discSp = FilledDisc(R, 0.40f, 0.96f);
            _rimSp = Ring(R, 0.455f, 0.392f, true);
            _ringSp = Ring(R, 0.48f, 0.445f, true);
            _dashSp = DashedRing(R, 0.47f, 0.425f, 16, 0.55f);
            _sweepSp = Sweep(R);
            _sparkSp = Spark(48);
            _pipSp = Diamond(48, 0.72f, true);
            _scienceSp = GlyphScience(R);
            _militarySp = GlyphMilitary(R);
            _constructorSp = GlyphConstructor(R);
        }

        // === ПУБЛИЧНЫЕ ГЕТТЕРЫ ДЛЯ ГЛИФОВ ===
        // Используются в SystemFleetBadge, чтобы значки над системами
        // выглядели точно так же, как значки над кораблями.

        public static Sprite GetDiscSprite()
        {
            EnsureArt();
            return _discSp;
        }

        public static Sprite GetRingSprite()
        {
            EnsureArt();
            return _rimSp;
        }

        public static Material SpriteMaterial
        {
            get
            {
                EnsureArt();
                return _spriteMat;
            }
        }

        public static Sprite GetScienceSprite()
        {
            EnsureArt();
            return _scienceSp;
        }

        public static Sprite GetMilitarySprite()
        {
            EnsureArt();
            return _militarySp;
        }

        public static Sprite GetConstructorSprite()
        {
            EnsureArt();
            return _constructorSp;
        }

        private static Texture2D Blank(int s)
        {
            var t = new Texture2D(s, s, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            var px = new Color[s * s];
            t.SetPixels(px);
            return t;
        }

        private static Sprite Spr(Texture2D t)
            => Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 185f);

        private static float AAstep(float d, float w) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d / Mathf.Max(0.0001f, w)));

        private static Sprite RadialGlow(int s)
        {
            var tex = Blank(s);
            float c = s * 0.5f;
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float nx = (x - c) / c, ny = (y - c) / c;
                float d = Mathf.Sqrt(nx * nx + ny * ny);
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (0.35f + 0.65f * a);
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            tex.Apply();
            return Spr(tex);
        }

        private static Sprite FilledDisc(int s, float radius, float alpha)
        {
            var tex = Blank(s);
            float c = s * 0.5f, r = c * radius * 2f, aa = 1.6f;
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c));
                float a = AAstep(d - r, aa) * alpha;
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            tex.Apply();
            return Spr(tex);
        }

        private static Sprite Ring(int s, float o, float i, bool soft)
        {
            var tex = Blank(s);
            float c = s * 0.5f, rO = c * o * 2f, rI = c * i * 2f, aa = soft ? 1.5f : 1f;
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c));
                float a = AAstep(d - rO, aa) * AAstep(rI - d, aa);
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            tex.Apply();
            return Spr(tex);
        }

        private static Sprite DashedRing(int s, float o, float i, int n, float duty)
        {
            var tex = Blank(s);
            float c = s * 0.5f, rO = c * o * 2f, rI = c * i * 2f;
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float ring = AAstep(d - rO, 1.4f) * AAstep(rI - d, 1.4f);
                if (ring <= 0f) continue;
                float u = (Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) + 1f) * n;
                float dash = Mathf.Abs(Mathf.Repeat(u, 1f) - 0.5f) * 2f;
                float mask = AAstep(dash - duty, 0.12f);
                tex.SetPixel(x, y, new Color(1, 1, 1, ring * mask));
            }
            tex.Apply();
            return Spr(tex);
        }

        private static Sprite Sweep(int s)
        {
            var tex = Blank(s);
            float c = s * 0.5f;
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / c;
                if (d > 0.46f || d < 0.08f) continue;
                float ang = Mathf.Atan2(dy, dx);
                float cone = Mathf.Repeat(ang / (Mathf.PI * 2f) + 1f, 1f);
                if (cone > 0.18f) continue;
                float fade = 1f - cone / 0.18f;
                float rad = 1f - Mathf.Abs(d - 0.28f) / 0.22f;
                tex.SetPixel(x, y, new Color(1, 1, 1, fade * Mathf.Clamp01(rad) * 0.9f));
            }
            tex.Apply();
            return Spr(tex);
        }

        private static Sprite Spark(int s)
        {
            var tex = Blank(s);
            float c = s * 0.5f;
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float nx = (x - c) / c, ny = (y - c) / c;
                float diamond = Mathf.Abs(nx) + Mathf.Abs(ny);
                float a = AAstep(diamond - 0.72f, 0.18f);
                float core = AAstep(diamond - 0.22f, 0.12f);
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Max(a * 0.7f, core)));
            }
            tex.Apply();
            return Spr(tex);
        }

        private static Sprite Diamond(int s, float size, bool filled)
        {
            var tex = Blank(s);
            float c = s * 0.5f;
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float nx = (x - c) / c, ny = (y - c) / c;
                float d = Mathf.Abs(nx) + Mathf.Abs(ny);
                float a = filled ? AAstep(d - size, 0.08f) : AAstep(d - size, 0.08f) * AAstep((size - 0.18f) - d, 0.08f);
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            tex.Apply();
            return Spr(tex);
        }

        private static Sprite GlyphScience(int s)
        {
            var tex = Blank(s);
            float c = s * 0.5f, n = c;
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float nx = (x - c) / n, ny = (y - c) / n;
                float dia = Mathf.Abs(nx) + Mathf.Abs(ny);
                float outer = AAstep(dia - 0.46f, 0.035f);
                float hole  = 1f - AAstep(dia - 0.22f, 0.03f);
                float barV  = AAstep(Mathf.Abs(nx) - 0.055f, 0.02f) * AAstep(Mathf.Abs(ny) - 0.34f, 0.02f);
                float barH  = AAstep(Mathf.Abs(ny) - 0.055f, 0.02f) * AAstep(Mathf.Abs(nx) - 0.34f, 0.02f);
                float core  = AAstep(dia - 0.10f, 0.025f);
                float a = Mathf.Max(outer * hole, Mathf.Max(barV, barH));
                a = Mathf.Max(a, core);
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            tex.Apply();
            return Spr(tex);
        }

        private static Sprite GlyphMilitary(int s)
        {
            var tex = Blank(s);
            float c = s * 0.5f, n = c;
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float nx = (x - c) / n;
                float ny = (y - c) / n;
                float a = 0f;
                if (ny < 0.38f && ny > -0.08f)
                {
                    bool inOuter = Mathf.Abs(nx) < 0.22f + (0.38f - ny) * 0.35f;
                    bool inCut = ny < 0.16f && Mathf.Abs(nx) < 0.10f + Mathf.Max(0f, 0.16f - ny) * 0.4f && ny > -0.02f;
                    if (inOuter && !inCut) a = 1f;
                    if (ny > 0.22f && Mathf.Abs(nx) < 0.16f) a = 1f;
                }
                if (Mathf.Abs(nx) < 0.07f && ny > -0.40f && ny < 0.10f) a = 1f;
                if (ny > -0.22f && ny < -0.08f && Mathf.Abs(nx) > 0.10f && Mathf.Abs(nx) < 0.34f) a = 1f;
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            tex.Apply();
            return Spr(tex);
        }

        private static Sprite GlyphConstructor(int s)
        {
            var tex = Blank(s);
            float c = s * 0.5f, n = c;
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float nx = (x - c) / n, ny = (y - c) / n;
                float a = 0f;
                float bx = Mathf.Abs(nx), by = Mathf.Abs(ny);
                if (bx < 0.11f && by < 0.36f) a = 1f;
                if (by < 0.11f && bx < 0.36f) a = 1f;
                bool frame = bx < 0.40f && by < 0.40f && (bx > 0.28f || by > 0.28f);
                bool corner = (bx > 0.18f && by > 0.18f);
                if (frame && corner) a = 1f;
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            tex.Apply();
            return Spr(tex);
        }
    }
}