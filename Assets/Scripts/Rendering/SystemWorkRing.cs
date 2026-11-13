using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Работа в вашей системе (вариант B «Кольцо системы»): толстое кольцо вокруг звезды цвета задачи —
    /// голубое — разведка, золотое — форпост, зелёное — колония и терраформинг. Дуга прогресса растёт от «12 часов»
    /// по часовой, на конце — светящаяся точка; четыре риски по сторонам; строитель ещё в пути — пунктирное кольцо.
    /// Сверху на кольце — маяк (RingCapsule): ромб со значком, заливка по прогрессу, число дней. Кольцо лежит
    /// в плоскости галактики, маяк смотрит в камеру и издалека держит постоянный экранный размер. Данные и видимость задаёт SystemNameplate.
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

        /// <summary>Что показывать: цвет задачи, прогресс 0..1 (wait — работа ещё не началась), значок и число дней.</summary>
        public void Set(Color col, float progress, bool wait, LGIcon icon, string days)
        {
            if (!EnsureBuilt()) return;
            if (_progress < 0f || col != _col) _shown = Mathf.Max(0f, progress);   // новая работа — без «догоняния» с нуля
            _col = col;
            _wait = wait;
            _progress = wait ? 0f : Mathf.Clamp01(progress);
            string key = days + "|" + (int)icon + "|" + ColorUtility.ToHtmlStringRGB(col);
            if (key == _capKey) return;
            _capKey = key;
            _cap.Set(icon, days, col);
        }

        public bool ContainsScreenPoint(Camera cam, Vector2 p) => _go != null && _go.activeSelf && _cap.ContainsScreenPoint(cam, p);

        public void Hide() { if (_go != null && _go.activeSelf) _go.SetActive(false); _alpha = 0f; _progress = -1f; }

        /// <summary>Каждый кадр: прозрачность (0 — скрыто), масштаб подписи системы, расстояние до камеры.</summary>
        public void Tick(Transform cam, float alpha, float scale, float dist, float time)
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

            // Маяк — на дальней точке кольца (сверху на экране), смотрит в камеру. Подпись системы перестаёт
            // расти на 3.2, а маяк растёт дальше — на экране не мельче, чем у этой границы; цифра издалека гаснет.
            Vector3 up = cam != null ? Vector3.ProjectOnPlane(cam.forward, Vector3.up) : Vector3.forward;
            up = up.sqrMagnitude > 1e-4f ? up.normalized : Vector3.forward;
            _capRoot.position = c + up * Radius + Vector3.up * 0.05f;
            if (cam != null) _capRoot.rotation = cam.rotation;
            _capRoot.localScale = Vector3.one * Mathf.Clamp(dist * 0.012f, scale, 14f);
            _cap.SetAlpha(alpha, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(215f, 285f, dist)));
            _cap.SetProgress(_shown, _wait);
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
                _head.transform.localScale = Vector3.one * (_headK * scale * 2.2f * pulse);
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

            _head = HeadSprite("Head", FactionSelectScreen.RadialSprite(), 55, out _headK);   // круглое свечение (у WorldChip — прямоугольное)
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
    /// Маяк работы на кольце (вариант C): тёмный ромб с контуром цвета задачи на пунктирной ножке над кольцом.
    /// Ромб заливается снизу вверх по прогрессу, в центре — значок задачи, справа крупно число дней и «дн.».
    /// Издалека цифра гаснет и остаётся один ромб. Корень — в точке кольца, локальная +Y — вверх по экрану.
    /// </summary>
    public sealed class RingCapsule
    {
        private const float R = 0.62f;            // полудиагональ ромба
        private const float Stem = 0.40f;         // ножка от кольца до нижнего угла ромба
        private const int FontSize = 64;
        private static readonly Color PlateCol = new Color(0.02f, 0.045f, 0.08f, 0.94f);
        private static readonly Color UnitCol = new Color(0.62f, 0.70f, 0.74f, 1f);
        private static Material s_mat;

        private readonly Transform _body;
        private readonly SpriteRenderer _glow, _plate, _frame, _icon, _dot;
        private readonly TextMesh _days, _unit;
        private readonly Mesh _mesh;
        private readonly List<Vector3> _v = new List<Vector3>(64);
        private readonly List<Color> _c = new List<Color>(64);
        private readonly List<int> _t = new List<int>(96);
        private Color _col = Color.white;
        private float _alpha = -1f, _textA = -1f, _pulse = 1f, _prog = -1f;
        private bool _wait;

        public RingCapsule(Transform parent, int order)
        {
            _body = new GameObject("Beacon").transform;
            _body.SetParent(parent, false);
            float cy = Stem + R;
            _glow = Sprite("Glow", FactionSelectScreen.RadialSprite(), order, new Vector3(0f, cy, 0.004f), R * 4.2f);
            _plate = Sprite("Plate", HudArt.Diamond(false), order + 1, new Vector3(0f, cy, 0f), R * 2f);
            _frame = Sprite("Frame", HudArt.Diamond(true), order + 3, new Vector3(0f, cy, -0.006f), R * 2f);
            _icon = Sprite("Icon", null, order + 4, new Vector3(0f, cy, -0.008f), R * 1.1f);
            _dot = Sprite("Dot", HudArt.Disc(), order + 2, Vector3.zero, 0.11f);

            if (s_mat == null && ShaderCache.Sprite != null)
                s_mat = new Material(ShaderCache.Sprite) { name = "RingBeacon", renderQueue = 3050 };
            var mgo = new GameObject("Fill");
            mgo.transform.SetParent(_body, false);
            mgo.transform.localPosition = new Vector3(0f, 0f, -0.003f);
            var mr = mgo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = s_mat;
            mr.sortingOrder = order + 2;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _mesh = new Mesh { name = "RingBeacon" };
            _mesh.MarkDynamic();
            mgo.AddComponent<MeshFilter>().sharedMesh = _mesh;

            float cs = 0.52f / (FontSize * 0.1f) * 1.25f;          // высота цифр ≈ 0.52 ед.
            _days = Text("Days", order + 4, GameFont.Bold, cs, new Vector3(R + 0.14f, cy + 0.09f, -0.01f));
            _unit = Text("Unit", order + 4, GameFont.Regular, cs * 0.5f, new Vector3(R + 0.17f, cy - 0.28f, -0.01f));
            _unit.text = Loc.T("дн.");
            SetAlpha(0f);
        }

        /// <summary>Значок, число дней (строкой, например «131» или «~131») и цвет задачи.</summary>
        public void Set(LGIcon icon, string days, Color col)
        {
            _col = col;
            _icon.sprite = LGIcons.Get(icon);
            Fit(_icon, R * 1.1f);
            _days.text = days ?? "";
            _prog = -1f;
            Apply();
        }

        /// <summary>Прогресс 0..1 (заливка ромба снизу вверх); wait — строитель в пути, заливки нет.</summary>
        public void SetProgress(float p, bool wait)
        {
            p = wait ? 0f : Mathf.Clamp01(p);
            if (wait == _wait && Mathf.Abs(p - _prog) < 0.002f) return;
            _prog = p; _wait = wait;
            RebuildMesh();
        }

        public void SetAlpha(float a) => SetAlpha(a, 1f);

        /// <summary>Курсор над ромбом или над цифрой рядом с ним (экранные координаты).</summary>
        public bool ContainsScreenPoint(Camera cam, Vector2 p)
        {
            if (cam == null || _alpha < 0.3f || !_body.gameObject.activeInHierarchy) return false;
            Vector3 c = cam.WorldToScreenPoint(_body.TransformPoint(new Vector3(0f, Stem + R, 0f)));
            if (c.z <= 0f) return false;
            Vector3 e = cam.WorldToScreenPoint(_body.TransformPoint(new Vector3(R * 1.1f, Stem + R, 0f)));
            float r = Mathf.Max(10f, Vector2.Distance(c, e));
            float dx = p.x - c.x, dy = p.y - c.y;
            float right = _days.gameObject.activeSelf ? r * 3.2f : r;
            return dx >= -r && dx <= right && Mathf.Abs(dy) <= r;
        }

        /// <summary>a — общая прозрачность, textA — доля для цифры (издалека гаснет).</summary>
        public void SetAlpha(float a, float textA)
        {
            if (Mathf.Approximately(a, _alpha) && Mathf.Approximately(textA, _textA)) return;
            bool rebuild = !Mathf.Approximately(a, _alpha);
            _alpha = a; _textA = textA;
            Apply();
            if (rebuild) RebuildMesh();
        }

        /// <summary>Мягкое «дыхание» свечения; пока строитель в пути — мигает и контур.</summary>
        public void Pulse(float time)
        {
            float k = 0.75f + 0.25f * Mathf.Sin(time * 2.4f);
            if (Mathf.Abs(k - _pulse) < 0.01f) return;
            _pulse = k;
            float a = Mathf.Max(0f, _alpha);
            _glow.color = new Color(_col.r, _col.g, _col.b, 0.30f * _pulse * a);
            if (_wait) _frame.color = new Color(_col.r, _col.g, _col.b, (0.55f + 0.4f * _pulse) * a);
        }

        private void Apply()
        {
            float a = Mathf.Max(0f, _alpha), ta = a * Mathf.Clamp01(_textA);
            _plate.color = new Color(PlateCol.r, PlateCol.g, PlateCol.b, PlateCol.a * a);
            _frame.color = new Color(_col.r, _col.g, _col.b, 0.95f * a);
            _glow.color = new Color(_col.r, _col.g, _col.b, 0.30f * _pulse * a);
            _icon.color = new Color(1f, 1f, 1f, a);
            _dot.color = new Color(_col.r, _col.g, _col.b, a);
            Color lit = Color.Lerp(_col, Color.white, 0.15f);
            _days.color = new Color(lit.r, lit.g, lit.b, ta);
            _unit.color = new Color(UnitCol.r, UnitCol.g, UnitCol.b, ta);
            bool txt = ta > 0.01f;
            if (_days.gameObject.activeSelf != txt) { _days.gameObject.SetActive(txt); _unit.gameObject.SetActive(txt); }
            bool on = a > 0.01f;
            if (_body.gameObject.activeSelf != on) _body.gameObject.SetActive(on);
        }

        // Заливка ромба снизу до уровня прогресса и пунктир ножки — один меш с цветами вершин
        private void RebuildMesh()
        {
            _v.Clear(); _c.Clear(); _t.Clear();
            float a = Mathf.Max(0f, _alpha);
            float cy = Stem + R;

            // Пунктир ножки: от точки кольца до нижнего угла ромба
            const int dashes = 3;
            float hw = 0.018f, step = Stem / dashes;
            Color sc = new Color(_col.r, _col.g, _col.b, 0.75f * a);
            for (int i = 0; i < dashes; i++)
            {
                float y0 = 0.06f + i * step, y1 = Mathf.Min(Stem - 0.02f, y0 + step * 0.55f);
                if (y1 <= y0) continue;
                Quad(new Vector3(-hw, y0), new Vector3(hw, y0), new Vector3(hw, y1), new Vector3(-hw, y1), sc);
            }

            // Заливка: ромб чуть меньше контура, обрезан сверху горизонталью на уровне прогресса
            if (_prog > 0.001f)
            {
                float r = R * 0.84f, L = -r + 2f * r * _prog;   // уровень относительно центра
                Color fc = new Color(_col.r, _col.g, _col.b, 0.42f * a);
                int s0 = _v.Count;
                _v.Add(new Vector3(0f, cy - r)); _c.Add(fc);                       // нижний угол
                if (L <= 0f)
                {
                    float w = r + L;   // r - |L|
                    _v.Add(new Vector3(w, cy + L)); _v.Add(new Vector3(-w, cy + L));
                }
                else
                {
                    float w = r - L;
                    _v.Add(new Vector3(r, cy)); _v.Add(new Vector3(w, cy + L)); _v.Add(new Vector3(-w, cy + L)); _v.Add(new Vector3(-r, cy));
                }
                while (_c.Count < _v.Count) _c.Add(fc);
                for (int i = s0 + 1; i < _v.Count - 1; i++) { _t.Add(s0); _t.Add(i + 1); _t.Add(i); }
            }

            _mesh.Clear();
            _mesh.SetVertices(_v);
            _mesh.SetColors(_c);
            _mesh.SetTriangles(_t, 0);
            _mesh.RecalculateBounds();
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            int s = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            for (int i = 0; i < 4; i++) _c.Add(col);
            _t.Add(s); _t.Add(s + 2); _t.Add(s + 1);
            _t.Add(s); _t.Add(s + 3); _t.Add(s + 2);
        }

        private static void Fit(SpriteRenderer r, float size)
        {
            float w = r.sprite != null ? r.sprite.bounds.size.x : 1f;
            r.transform.localScale = Vector3.one * (size / Mathf.Max(0.0001f, w));
        }

        private SpriteRenderer Sprite(string name, Sprite sp, int order, Vector3 pos, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_body, false);
            go.transform.localPosition = pos;
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sp;
            r.sortingOrder = order;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (sp != null) Fit(r, size);
            return r;
        }

        private TextMesh Text(string name, int order, Font font, float cs, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_body, false);
            go.transform.localPosition = pos;
            var t = go.AddComponent<TextMesh>();
            t.font = font;
            t.fontSize = FontSize;
            t.characterSize = cs;
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
