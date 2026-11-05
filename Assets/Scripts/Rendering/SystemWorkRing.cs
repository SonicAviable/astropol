using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Работа в вашей системе (вариант B «Кольцо системы»): толстое кольцо вокруг звезды цвета задачи —
    /// голубое — разведка, золотое — форпост, зелёное — колония и терраформинг. Дуга прогресса растёт от «12 часов»
    /// по часовой, на конце — светящаяся точка; четыре риски по сторонам; строитель ещё в пути — пунктирное кольцо.
    /// Сверху на кольце — капсула: значок, задача, проценты и дни. Кольцо лежит в плоскости галактики,
    /// капсула смотрит в камеру и масштабируется как подпись системы. Данные и видимость задаёт SystemNameplate.
    /// </summary>
    public sealed class SystemWorkRing
    {
        public const float Radius = 6.2f;
        private const int Seg = 120;
        private const int Dashes = 40;

        public static readonly Color SurveyCol = new Color(0.37f, 0.72f, 1f);
        public static readonly Color BuildCol = new Color(1f, 0.76f, 0.30f);
        public static readonly Color ColonyCol = new Color(0.36f, 0.88f, 0.56f);

        private readonly Transform _owner;
        private GameObject _go;
        private Mesh _mesh;
        private MeshRenderer _mr;
        private Transform _capRoot;
        private RingCapsule _cap;
        private SpriteRenderer _head, _core;
        private float _headK = 1f, _coreK = 1f;   // приведение спрайтов разного размера к 1 ед. мира
        private static Material s_mat;

        private Color _col = Color.white;
        private float _progress = -1f, _shown, _alpha;
        private bool _wait;
        private string _capKey;
        private float _bScale = -1f, _bProg = -9f, _bAlpha = -1f;
        private bool _bWait;
        private Color _bCol;

        private readonly List<Vector3> _v = new List<Vector3>(1024);
        private readonly List<Color> _c = new List<Color>(1024);
        private readonly List<int> _t = new List<int>(2048);

        public SystemWorkRing(Transform owner) { _owner = owner; }

        /// <summary>Что показывать: цвет задачи, прогресс 0..1 (wait — работа ещё не началась), значок и текст капсулы.</summary>
        public void Set(Color col, float progress, bool wait, LGIcon icon, string text)
        {
            if (!EnsureBuilt()) return;
            if (_progress < 0f || col != _col) _shown = Mathf.Max(0f, progress);   // новая работа — без «догоняния» с нуля
            _col = col;
            _wait = wait;
            _progress = wait ? 0f : Mathf.Clamp01(progress);
            string key = text + "|" + (int)icon + "|" + ColorUtility.ToHtmlStringRGB(col);
            if (key == _capKey) return;
            _capKey = key;
            _cap.Set(icon, text, col);
        }

        public void Hide() { if (_go != null && _go.activeSelf) _go.SetActive(false); _alpha = 0f; _progress = -1f; }

        /// <summary>Каждый кадр: прозрачность (0 — скрыто), масштаб подписи системы, поворот камеры.</summary>
        public void Tick(Transform cam, float alpha, float scale, float time)
        {
            if (_go == null) return;
            bool on = alpha > 0.01f && _progress >= 0f;
            if (_go.activeSelf != on) _go.SetActive(on);
            if (!on) { _alpha = 0f; return; }
            _alpha = alpha;

            // Прогресс растёт рывками раз в день — показываем плавно
            _shown = Mathf.MoveTowards(_shown, _progress, Mathf.Max(0.02f, Mathf.Abs(_progress - _shown) * 2.5f) * Time.unscaledDeltaTime);
            if (_progress < _shown - 0.2f) _shown = _progress;

            Vector3 c = _owner.position;
            _go.transform.SetPositionAndRotation(c, Quaternion.identity);
            float pulse = 0.85f + 0.15f * Mathf.Sin(time * 2.4f);
            if (Mathf.Abs(scale - _bScale) > _bScale * 0.02f || Mathf.Abs(_shown - _bProg) > 0.001f || Mathf.Abs(alpha - _bAlpha) > 0.01f
                || _wait != _bWait || _col != _bCol)
                Rebuild(scale);

            // Капсула — на дальней точке кольца (сверху на экране), смотрит в камеру
            Vector3 up = cam != null ? Vector3.ProjectOnPlane(cam.forward, Vector3.up) : Vector3.forward;
            up = up.sqrMagnitude > 1e-4f ? up.normalized : Vector3.forward;
            _capRoot.position = c + up * Radius + Vector3.up * 0.05f;
            if (cam != null) _capRoot.rotation = cam.rotation;
            _capRoot.localScale = Vector3.one * scale;
            _cap.SetAlpha(alpha);
            _cap.Pulse(time);

            // Голова дуги: мягкое свечение и яркая точка
            bool head = !_wait && _shown > 0.004f;
            _head.enabled = _core.enabled = head;
            if (head)
            {
                float a = _shown * Mathf.PI * 2f;
                Vector3 hp = c + new Vector3(Mathf.Sin(a) * Radius, 0.06f, Mathf.Cos(a) * Radius);
                _head.transform.position = _core.transform.position = hp;
                if (cam != null) _head.transform.rotation = _core.transform.rotation = cam.rotation;
                _head.transform.localScale = Vector3.one * (_headK * scale * 1.5f * pulse);
                _core.transform.localScale = Vector3.one * (_coreK * scale * 0.4f);
                _head.color = new Color(_col.r, _col.g, _col.b, 0.75f * alpha);
                _core.color = new Color(1f, 1f, 1f, 0.95f * alpha);
            }
        }

        private bool EnsureBuilt()
        {
            if (_go != null) return true;
            if (s_mat == null)
            {
                var sh = ShaderCache.Sprite;
                if (sh == null) return false;
                s_mat = new Material(sh) { name = "SystemWorkRing", renderQueue = 3050 };
            }
            _go = new GameObject("WorkRing");
            _go.transform.SetParent(_owner, false);
            _go.layer = _owner.gameObject.layer;
            var mf = _go.AddComponent<MeshFilter>();
            _mr = _go.AddComponent<MeshRenderer>();
            _mr.sharedMaterial = s_mat;
            _mr.sortingOrder = 46;
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mr.receiveShadows = false;
            _mesh = new Mesh { name = "WorkRing" };
            _mesh.MarkDynamic();
            mf.sharedMesh = _mesh;

            _capRoot = new GameObject("Capsule").transform;
            _capRoot.SetParent(_go.transform, false);
            _cap = new RingCapsule(_capRoot, 54);

            _head = HeadSprite("Head", WorldChip.GlowSprite(), 55, out _headK);
            _core = HeadSprite("Core", HudArt.Disc(), 57, out _coreK);
            foreach (var t in _go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = _owner.gameObject.layer;
            _go.SetActive(false);
            return true;
        }

        private SpriteRenderer HeadSprite(string name, Sprite sp, int order, out float k)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_go.transform, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sp;
            r.sortingOrder = order;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            k = 1f / Mathf.Max(0.001f, sp != null ? sp.bounds.size.x : 1f);
            return r;
        }

        // ==================== МЕШ ====================

        private void Rebuild(float scale)
        {
            _bScale = scale; _bProg = _shown; _bAlpha = _alpha; _bWait = _wait; _bCol = _col;
            _v.Clear(); _c.Clear(); _t.Clear();
            float w = 0.16f * scale, a = _alpha;
            Color c = _col, lit = Color.Lerp(_col, Color.white, 0.3f);

            if (_wait)
            {
                // Строитель в пути: тусклая дорожка и пунктир
                Band(0f, 1f, Radius, w * 0.9f, c, 0.10f * a, true);
                for (int i = 0; i < Dashes; i++)
                {
                    float f = i / (float)Dashes;
                    Band(f, f + 0.45f / Dashes, Radius, w * 0.35f, c, 0.6f * a, false);
                }
            }
            else
            {
                Band(0f, 1f, Radius, w * 0.9f, c, 0.16f * a, true);           // дорожка
                if (_shown > 0.002f)
                {
                    Band(0f, _shown, Radius, w * 1.9f, c, 0.26f * a, true);    // свечение дуги
                    Band(0f, _shown, Radius, w * 0.5f, lit, 0.95f * a, false); // яркая дуга
                }
            }
            // Четыре риски снаружи кольца
            for (int i = 0; i < 4; i++)
                Band(i / 4f - 0.006f, i / 4f + 0.006f, Radius + w * 1.5f, w * 0.45f, c, 0.6f * a, false);

            _mesh.Clear();
            _mesh.SetVertices(_v);
            _mesh.SetColors(_c);
            _mesh.SetTriangles(_t, 0);
            _mesh.RecalculateBounds();
        }

        /// <summary>Дуга-лента от f0 до f1 (доли оборота от «12 часов» по часовой) в плоскости XZ.
        /// soft — края гаснут (свечение), иначе ровная лента с узкой мягкой кромкой.</summary>
        private void Band(float f0, float f1, float r, float halfW, Color col, float alpha, bool soft)
        {
            if (f1 <= f0) return;
            int n = Mathf.Max(2, Mathf.CeilToInt((f1 - f0) * Seg) + 1);
            float[] off = soft ? new[] { -1f, 0f, 1f } : new[] { -1f, -0.6f, 0.6f, 1f };
            float[] k = soft ? new[] { 0f, 1f, 0f } : new[] { 0f, 1f, 1f, 0f };
            int rows = off.Length, start = _v.Count;
            for (int i = 0; i < n; i++)
            {
                float ang = Mathf.Lerp(f0, f1, i / (float)(n - 1)) * Mathf.PI * 2f;
                float s = Mathf.Sin(ang), co = Mathf.Cos(ang);
                for (int j = 0; j < rows; j++)
                {
                    float rr = r + off[j] * halfW;
                    _v.Add(new Vector3(s * rr, 0.04f, co * rr));
                    _c.Add(new Color(col.r, col.g, col.b, alpha * k[j]));
                }
            }
            for (int i = 0; i < n - 1; i++)
            for (int j = 0; j < rows - 1; j++)
            {
                int a0 = start + i * rows + j, a1 = a0 + 1, b0 = a0 + rows, b1 = b0 + 1;
                _t.Add(a0); _t.Add(b0); _t.Add(a1);
                _t.Add(a1); _t.Add(b0); _t.Add(b1);
            }
        }
    }

    /// <summary>
    /// Капсула на кольце работы: тёмная «таблетка» с контуром цвета задачи и мягким свечением,
    /// слева — значок в цветном кружке, затем задача жирным и приглушённое значение («31% · 52 дня»).
    /// Текст «ЗАДАЧА · значение» делится по первой « · ». Корень — по центру капсулы.
    /// </summary>
    public sealed class RingCapsule
    {
        private const float H = 0.56f;            // высота: при ней концы «таблетки» — ровные полукруги
        private const int FontSize = 64;
        private static readonly Color PlateCol = new Color(0.02f, 0.045f, 0.08f, 0.93f);
        private static readonly Color ValueCol = new Color(0.70f, 0.79f, 0.85f, 1f);

        private readonly Transform _body;
        private readonly SpriteRenderer _glow, _plate, _frame, _disc, _icon;
        private readonly TextMesh _label, _value;
        private Color _col = Color.white;
        private float _alpha = -1f, _pulse = 1f;

        public float Width { get; private set; }

        public RingCapsule(Transform parent, int order)
        {
            _body = new GameObject("Capsule").transform;
            _body.SetParent(parent, false);
            _glow = Sprite("Glow", WorldChip.GlowSprite(), order, true);
            _plate = Sprite("Plate", HudArt.Pill(false), order + 1, true);
            _frame = Sprite("Frame", HudArt.Pill(true), order + 2, true);
            _disc = Sprite("Disc", HudArt.Disc(), order + 3, false);
            _icon = Sprite("Icon", null, order + 4, false);
            _label = Text("Label", order + 4);
            _value = Text("Value", order + 4);
            SetAlpha(0f);
        }

        public void Set(LGIcon icon, string text, Color col)
        {
            _col = col;
            string label = text ?? "", val = "";
            int cut = label.IndexOf(" · ", System.StringComparison.Ordinal);
            if (cut > 0) { val = label.Substring(cut + 3); label = label.Substring(0, cut); }
            _label.text = label;
            _value.text = val;

            float cs = 0.25f / (FontSize * 0.1f) * 1.25f;          // высота строки ≈ 0.25 ед.
            _label.characterSize = _value.characterSize = cs;
            float x = H * 0.5f;                                    // центр кружка — в центре левого полукруга
            float d = H * 0.74f;
            _disc.transform.localPosition = new Vector3(x - H * 0.5f + H * 0.13f + d * 0.5f, 0f, -0.01f);
            Fit(_disc, d);
            _icon.sprite = LGIcons.Get(icon);
            _icon.transform.localPosition = _disc.transform.localPosition + new Vector3(0f, 0f, -0.002f);
            Fit(_icon, d * 0.62f);
            x = H * 0.13f + d + H * 0.24f;
            _label.transform.localPosition = new Vector3(x, 0.005f, -0.01f);
            x += WorldChip.MeasureLine(label, FontSize, cs, FontStyle.Normal, GameFont.Bold);
            if (val.Length > 0)
            {
                x += H * 0.22f;
                _value.transform.localPosition = new Vector3(x, 0.005f, -0.01f);
                x += WorldChip.MeasureLine(val, FontSize, cs, FontStyle.Normal, GameFont.Regular);
            }
            x += H * 0.42f;
            Width = x;

            _plate.size = _frame.size = new Vector2(Width, H);
            _plate.transform.localPosition = new Vector3(Width * 0.5f, 0f, 0f);
            _frame.transform.localPosition = new Vector3(Width * 0.5f, 0f, -0.004f);
            float gp = 0.34f;
            _glow.size = new Vector2(Width + gp * 2f, H + gp * 2f);
            _glow.transform.localPosition = new Vector3(Width * 0.5f, 0f, 0.004f);
            _body.localPosition = new Vector3(-Width * 0.5f, 0f, 0f);
            Apply();
        }

        public void SetAlpha(float a)
        {
            if (Mathf.Approximately(a, _alpha)) return;
            _alpha = a;
            Apply();
        }

        /// <summary>Мягкое «дыхание» свечения, пока работа идёт.</summary>
        public void Pulse(float time)
        {
            float k = 0.75f + 0.25f * Mathf.Sin(time * 2.4f);
            if (Mathf.Abs(k - _pulse) < 0.01f) return;
            _pulse = k;
            _glow.color = new Color(_col.r, _col.g, _col.b, 0.32f * _pulse * _alpha);
        }

        private void Apply()
        {
            float a = Mathf.Max(0f, _alpha);
            Color lit = Color.Lerp(_col, Color.white, 0.25f);
            _plate.color = new Color(PlateCol.r, PlateCol.g, PlateCol.b, PlateCol.a * a);
            _frame.color = new Color(_col.r, _col.g, _col.b, 0.85f * a);
            _glow.color = new Color(_col.r, _col.g, _col.b, 0.32f * _pulse * a);
            _disc.color = new Color(lit.r, lit.g, lit.b, a);
            _icon.color = new Color(PlateCol.r, PlateCol.g, PlateCol.b, a);
            _label.color = new Color(1f, 1f, 1f, a);
            _value.color = new Color(ValueCol.r, ValueCol.g, ValueCol.b, a);
            bool on = a > 0.01f;
            if (_body.gameObject.activeSelf != on) _body.gameObject.SetActive(on);
        }

        private static void Fit(SpriteRenderer r, float size)
        {
            float w = r.sprite != null ? r.sprite.bounds.size.x : 1f;
            r.transform.localScale = Vector3.one * (size / Mathf.Max(0.0001f, w));
        }

        private SpriteRenderer Sprite(string name, Sprite sp, int order, bool sliced)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_body, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sp;
            r.sortingOrder = order;
            if (sliced) r.drawMode = SpriteDrawMode.Sliced;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        private TextMesh Text(string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_body, false);
            var t = go.AddComponent<TextMesh>();
            t.font = name == "Value" ? GameFont.Regular : GameFont.Bold;
            t.fontSize = FontSize;
            t.anchor = TextAnchor.MiddleLeft;
            t.alignment = TextAlignment.Left;
            t.richText = false;
            var mr = go.GetComponent<MeshRenderer>();
            if (t.font != null) mr.sharedMaterial = t.font.material;
            mr.sortingOrder = order;
            return t;
        }
    }
}
