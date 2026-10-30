using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Курсор игры, нарисованный поверх всего интерфейса (системный скрыт) — с эффектами, которых не умеет
    /// аппаратный курсор. Картинки — Resources/UI/Cursors (рисует Tools/cursors/make_cursors.py).
    ///   • голографическая стрелка в бирюзовых тонах интерфейса со свечением и мерцающей звёздочкой на острие;
    ///   • наведение на то, что можно нажать (кнопки, звёзды, свои флоты, планеты): орбита со спутником вокруг острия;
    ///   • клик — ударная волна и искры;
    ///   • выделен флот и курсор на звезде — вращающийся золотой прицел (ПКМ отправит флот туда);
    ///   • экран загрузки — рядом со стрелкой крутится дуга-«комета», стрелка притушена.
    /// Размеры заданы для высоты экрана 1080 и масштабируются. Создаётся сам при запуске.
    /// </summary>
    public class GameCursor : MonoBehaviour
    {
        private enum Kind { Arrow, Hover, Move }

        /// <summary>Остриё стрелки в долях картинки (от левого верхнего угла) — как ARROW[0] в make_cursors.py.</summary>
        private static readonly Vector2 ArrowTip = new Vector2(0.10f, 0.06f);
        private const float ArrowSize = 40f;
        private const float Scale = 0.7f;           // общий масштаб курсора (стрелка ≈ 28 px при 1080p)
        // Палитра интерфейса (UIManager.DS): неоновый бирюзовый, глубокий бирюзовый, золото — приказы и ПКМ
        private static readonly Color Teal = new Color(0.40f, 0.86f, 0.82f);
        private static readonly Color DeepTeal = new Color(0.15f, 0.55f, 0.59f);
        private static readonly Color Gold = new Color(0.95f, 0.78f, 0.40f);

        private class Mote
        {
            public RectTransform Rt;
            public Image Img;
            public Vector2 Pos, Vel;
            public float Age, Life, Size;
            public Color From, To;
            public bool Ripple;
        }

        private RectTransform _glow, _arrow, _sparkle, _ring, _sat, _target, _spinner;
        private Image _glowImg, _arrowImg, _sparkleImg, _ringImg, _satImg, _targetImg, _spinnerImg;
        private Sprite _dot, _ripple;
        private Material _additive;
        private readonly List<Mote> _motes = new List<Mote>();
        private RectTransform _moteRoot;

        private float _hover, _move, _busy;
        private Camera _cam;
        private PointerEventData _ped;
        private EventSystem _pedSystem;
        private readonly List<RaycastResult> _uiHits = new List<RaycastResult>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindFirstObjectByType<GameCursor>() != null) return;
            var go = new GameObject("[UI] GameCursor");
            DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;     // поверх любого окна
            go.AddComponent<GameCursor>();
        }

        private void Awake()
        {
            var arrow = Sprite_("cursor_arrow");
            if (arrow == null)
            {
                Debug.LogWarning("[GameCursor] Нет Resources/UI/Cursors/cursor_arrow.png — остаётся системный курсор.");
                enabled = false;
                return;
            }
            var add = Resources.Load<Shader>("Shaders/UIAdditive");
            if (add != null) _additive = new Material(add) { name = "CursorAdditive" };
            _dot = Sprite_("fx_dot");
            _ripple = Sprite_("fx_ripple");

            _moteRoot = Layer("Motes", null, false, out _);
            _ring = Layer("Ring", Sprite_("fx_ring"), true, out _ringImg);
            _sat = Layer("Satellite", _dot, true, out _satImg);
            _glow = Layer("Glow", Sprite_("cursor_arrow_glow"), true, out _glowImg);
            _arrow = Layer("Arrow", arrow, false, out _arrowImg);
            _target = Layer("Target", Sprite_("cursor_target"), false, out _targetImg);
            _sparkle = Layer("Sparkle", Sprite_("fx_sparkle"), true, out _sparkleImg);
            _spinner = Layer("Spinner", Sprite_("fx_spinner"), true, out _spinnerImg);
            _arrow.pivot = _glow.pivot = new Vector2(ArrowTip.x, 1f - ArrowTip.y);
        }

        private void OnEnable() => Cursor.visible = false;
        private void OnDisable() => Cursor.visible = true;
        private void OnApplicationFocus(bool focus) { if (focus && enabled) Cursor.visible = false; }

        private void Update()
        {
            if (_arrowImg == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            float t = Time.unscaledTime;
            float k = Screen.height / 1080f * Scale;
            Vector2 tip = Input.mousePosition;

            bool busy = LoadingScreen.IsActive;
            var kind = busy ? Kind.Arrow : Detect();
            _hover = Mathf.MoveTowards(_hover, kind == Kind.Hover ? 1f : 0f, dt * 7f);
            _move = Mathf.MoveTowards(_move, kind == Kind.Move ? 1f : 0f, dt * 7f);
            _busy = Mathf.MoveTowards(_busy, busy ? 1f : 0f, dt * 5f);
            float arrowK = 1f - _move;

            // Стрелка и её свечение
            float a = ArrowSize * k * (1f + 0.06f * _hover);
            Place(_arrow, tip, a, 0f);
            Place(_glow, tip, a, 0f);
            _arrowImg.color = new Color(1f - 0.25f * _busy, 1f - 0.25f * _busy, 1f - 0.25f * _busy, arrowK);
            if (_glowImg != null)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 3f);
                _glowImg.color = Glow(Color.white, (0.35f + 0.5f * _hover + 0.12f * pulse) * arrowK);
            }

            // Звёздочка на острие: мерцает и медленно вращается
            if (_sparkleImg != null)
            {
                float sp = (15f + 9f * _hover + 4f * _move) * k * (0.85f + 0.2f * Mathf.Sin(t * 5.3f));
                Place(_sparkle, tip, sp, t * 40f);
                _sparkleImg.color = Glow(Color.white, 0.65f + 0.35f * Mathf.Sin(t * 7.1f) * Mathf.Sin(t * 2.3f));
            }

            // Орбита: при наведении — небольшая вокруг острия, при приказе — крупнее и золотая вокруг прицела
            float ringSize = Mathf.Lerp(30f, 72f, _move) * k * Mathf.Lerp(0.75f, 1f, Mathf.Max(_hover, _move));
            if (_ringImg != null)
            {
                Place(_ring, tip, ringSize, -t * 55f);
                _ringImg.color = Glow(Color.Lerp(Teal, Gold, _move), Mathf.Max(_hover * 0.75f, _move * 0.5f));
            }
            if (_satImg != null)
            {
                float orbit = ringSize * 0.40f;
                Place(_sat, tip + new Vector2(Mathf.Cos(t * 3f), Mathf.Sin(t * 3f)) * orbit, 7f * k, 0f);
                _satImg.color = Glow(Color.white, _hover);
            }

            // Прицел приказа флоту
            if (_targetImg != null)
            {
                Place(_target, tip, 50f * k * (1f + 0.05f * Mathf.Sin(t * 4f)), t * 25f);
                _targetImg.color = new Color(1f, 1f, 1f, _move);
            }

            // Загрузка: дуга-«комета» справа внизу от острия, крутится по часовой стрелке
            if (_spinnerImg != null)
            {
                Place(_spinner, tip + new Vector2(24f, -30f) * k, 26f * k, -t * 400f);
                _spinnerImg.color = Glow(Teal, _busy);
                _spinner.gameObject.SetActive(_busy > 0.001f);
            }

            if (_dot != null)
            {
                if (Input.GetMouseButtonDown(0)) Burst(tip, k, Teal);
                if (Input.GetMouseButtonDown(1)) Burst(tip, k, Gold);
            }
            UpdateMotes(dt);
        }

        // ==================== ИСКРЫ КЛИКА ====================

        private void Burst(Vector2 tip, float k, Color c)
        {
            if (_ripple != null) Spawn(tip, Vector2.zero, 0.4f, 80f * k, c, c, true);
            for (int i = 0; i < 7; i++)
            {
                float ang = (i / 7f + Random.Range(-0.04f, 0.04f)) * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                Spawn(tip + dir * 4f * k, dir * Random.Range(130f, 190f) * k, Random.Range(0.28f, 0.4f), 8f * k, Color.white, c, false);
            }
        }

        private void Spawn(Vector2 pos, Vector2 vel, float life, float size, Color from, Color to, bool ripple)
        {
            Mote m = null;
            foreach (var x in _motes) if (x.Age >= x.Life) { m = x; break; }
            if (m == null)
            {
                if (_motes.Count >= 64) return;
                var rt = Layer("Mote", _dot, true, out var img, _moteRoot);
                m = new Mote { Rt = rt, Img = img };
                _motes.Add(m);
            }
            m.Pos = pos; m.Vel = vel; m.Age = 0f; m.Life = life; m.Size = size;
            m.From = from; m.To = to; m.Ripple = ripple;
            m.Img.sprite = ripple ? _ripple : _dot;
            m.Rt.gameObject.SetActive(true);
        }

        private void UpdateMotes(float dt)
        {
            foreach (var m in _motes)
            {
                if (m.Age >= m.Life) continue;
                m.Age += dt;
                float u = Mathf.Clamp01(m.Age / m.Life);
                if (u >= 1f) { m.Rt.gameObject.SetActive(false); continue; }
                m.Pos += m.Vel * dt;
                m.Vel *= Mathf.Max(0f, 1f - 3.5f * dt);
                float size = m.Ripple ? m.Size * Mathf.Lerp(0.15f, 1f, 1f - (1f - u) * (1f - u)) : m.Size * (1f - u * 0.7f);
                Place(m.Rt, m.Pos, size, 0f);
                float alpha = m.Ripple ? (1f - u) * 0.9f : Mathf.Pow(1f - u, 1.5f) * 0.85f;
                m.Img.color = Glow(Color.Lerp(m.From, m.To, u), alpha);
            }
        }

        // ==================== ЧТО ПОД КУРСОРОМ ====================

        private Kind Detect()
        {
            var es = EventSystem.current;
            if (es != null && es.IsPointerOverGameObject())
                return OverInteractiveUi(es) ? Kind.Hover : Kind.Arrow;

            if (!UIManager.IsGameStarted || MenuAtmosphere.IsActive) return Kind.Arrow;
            if (GalaxyView.HoveredMiniPlanet != null) return Kind.Hover;

            Vector2 mouse = Input.mousePosition;
            if (OverOwnFleetBadge(mouse)) return Kind.Hover;

            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return Kind.Arrow;
            if (!Physics.Raycast(_cam.ScreenPointToRay(mouse), out var hit, 2000f)) return Kind.Arrow;

            var col = hit.collider;
            if (col.GetComponentInParent<FleetView>() != null) return Kind.Hover;
            if (col.GetComponentInParent<SystemViewManager.PlanetClickNotifier>() != null) return Kind.Hover;
            if (col.GetComponentInParent<StarSystemSelector>() != null)
            {
                bool inSystem = SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView;
                var fm = FleetManager.Instance;
                return !inSystem && fm != null && fm.SelectedFleets.Count > 0 ? Kind.Move : Kind.Hover;
            }
            return Kind.Arrow;
        }

        /// <summary>Верхний элемент интерфейса под курсором — часть активной кнопки, переключателя, поля ввода…</summary>
        private bool OverInteractiveUi(EventSystem es)
        {
            if (_ped == null || _pedSystem != es) { _ped = new PointerEventData(es); _pedSystem = es; }
            _ped.position = Input.mousePosition;
            _uiHits.Clear();
            es.RaycastAll(_ped, _uiHits);
            if (_uiHits.Count == 0 || _uiHits[0].gameObject == null) return false;
            var sel = _uiHits[0].gameObject.GetComponentInParent<Selectable>();
            return sel != null && sel.IsInteractable();
        }

        /// <summary>Значок своего флота на карте — как его ловит FleetSelectionController.PickFleet.</summary>
        private static bool OverOwnFleetBadge(Vector2 mouse)
        {
            foreach (var ind in FleetIndicator.All)
            {
                if (ind == null || !ind.IsLeader || !ind.OnScreen || ind.Fleet?.Data == null) continue;
                if (ind.Fleet.Data.OwnerId != 0) continue;
                if (Vector2.Distance(mouse, ind.ScreenPos) <= ind.ScreenRadius + 3f) return true;
            }
            return false;
        }

        // ==================== ПОСТРОЕНИЕ ====================

        private RectTransform Layer(string name, Sprite sprite, bool additive, out Image img, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent != null ? parent : transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            img = null;
            if (sprite == null) return rt;
            img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            if (additive && _additive != null) img.material = _additive;
            return rt;
        }

        /// <summary>
        /// Яркость свечения. Шейдер UIAdditive умножает цвет на альфу, так что гасим только RGB — иначе яркость
        /// уходила бы в квадрат. Без шейдера (обычное смешивание) текстуры свечений прозрачны по яркости — тоже верно.
        /// </summary>
        private static Color Glow(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        private static void Place(RectTransform rt, Vector2 screen, float size, float angle)
        {
            rt.position = screen;
            rt.sizeDelta = new Vector2(size, size);
            rt.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private static Sprite Sprite_(string name)
        {
            var tex = Resources.Load<Texture2D>("UI/Cursors/" + name);
            if (tex == null) return null;
            tex.wrapMode = TextureWrapMode.Clamp;
            var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            s.hideFlags = HideFlags.DontSave;
            return s;
        }
    }
}
