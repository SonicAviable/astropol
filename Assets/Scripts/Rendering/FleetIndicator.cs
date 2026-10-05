using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Голографический значок флота в духе Stellaris: глиф типа, кольца, стебель к кораблю.
    /// </summary>
    public class FleetIndicator : MonoBehaviour
    {
        private FleetView _fleet;
        private Transform _cam;
        private Transform _badge;

        private SpriteRenderer _bloomSr;
        private SpriteRenderer _discSr;
        private SpriteRenderer _rimSr;
        private SpriteRenderer _ringA;
        private SpriteRenderer _ringB;
        private SpriteRenderer _glyphSr;
        private SpriteRenderer _sweepSr;
        private SpriteRenderer _pipSr;
        private LineRenderer _stem;
        private Transform[] _orbiters;
        private SpriteRenderer[] _orbiterSr;

        private float _anim;
        private Color _accent;
        private FleetType _type;
        private FleetState _state;
        private bool _combat;
        private bool _selected;
        private float _selectPunch;

        private static Sprite _bloomSp, _discSp, _rimSp, _ringSp, _dashSp;
        private static Sprite _scienceSp, _militarySp, _constructorSp;
        private static Sprite _sweepSp, _sparkSp, _pipSp;
        private static Material _spriteMat;
        private static Material _lineMat;

        public void Init(FleetView fleet)
        {
            _fleet = fleet;
            _cam = Camera.main != null ? Camera.main.transform : null;
            EnsureArt();

            _badge = new GameObject("Badge").transform;
            _badge.SetParent(transform, false);

            _bloomSr = Layer(_badge, "Bloom", _bloomSp, 40, 1.32f);
            _discSr  = Layer(_badge, "Disc",  _discSp,  41, 0.78f);
            _rimSr   = Layer(_badge, "Rim",   _rimSp,   42, 0.82f);
            _ringA   = Layer(_badge, "RingA", _dashSp,  43, 0.96f);
            _ringB   = Layer(_badge, "RingB", _ringSp,  44, 1.08f);
            _sweepSr = Layer(_badge, "Sweep", _sweepSp, 45, 0.90f);
            _glyphSr = Layer(_badge, "Glyph", _scienceSp, 46, 0.70f);

            _pipSr = Layer(transform, "ShipPip", _pipSp, 39, 0.22f);

            _orbiters = new Transform[3];
            _orbiterSr = new SpriteRenderer[3];
            for (int i = 0; i < 3; i++)
            {
                _orbiterSr[i] = Layer(_badge, $"Orbiter{i}", _sparkSp, 47, 0.18f);
                _orbiters[i] = _orbiterSr[i].transform;
            }

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

            RefreshStyle(true);
        }

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

        private void LateUpdate()
        {
            if (_fleet == null) { Destroy(gameObject); return; }
            if (_fleet.Data == null) return;

            _anim += Time.unscaledDeltaTime;
            bool sel = FleetManager.Instance != null && FleetManager.Instance.SelectedFleet == _fleet;
            _selectPunch = Mathf.MoveTowards(_selectPunch, sel ? 1f : 0f, Time.unscaledDeltaTime * 6f);

            if (_type != _fleet.Data.Type || _state != _fleet.Data.State
                || _combat != _fleet.Data.InCombat || _selected != sel)
                RefreshStyle(false);
            _selected = sel;

            PlaceAndBillboard();
            Animate();
        }

        private void PlaceAndBillboard()
        {
            if (_cam == null && Camera.main != null) _cam = Camera.main.transform;

            Vector3 ship = _fleet.transform.position;
            float hover = Mathf.Sin(_anim * 2.15f) * 0.10f + Mathf.Sin(_anim * 1.07f) * 0.04f;
            Vector3 iconPos = ship + Vector3.up * (2.15f + hover + _selectPunch * 0.12f);
            transform.position = iconPos;

            if (_cam != null)
            {
                transform.rotation = _cam.rotation;
                float dist = Vector3.Distance(iconPos, _cam.position);
                float scale = Mathf.Clamp(dist * 0.010f, 0.55f, 1.22f) * (1f + _selectPunch * 0.08f);
                transform.localScale = Vector3.one * scale;
            }

            _pipSr.transform.position = ship + Vector3.up * 0.95f;
            _pipSr.transform.rotation = transform.rotation;

            if (_stem != null)
            {
                _stem.SetPosition(0, ship + Vector3.up * 1.05f);
                _stem.SetPosition(1, iconPos);
                float w = Mathf.Lerp(0.018f, 0.032f, _selectPunch);
                _stem.startWidth = w;
                _stem.endWidth = w * 0.25f;
            }
        }

        private void RefreshStyle(bool forceGlyph)
        {
            var data = _fleet.Data;
            _type = data.Type;
            _state = data.State;
            _combat = data.InCombat;
            bool ai = data.OwnerId != 0;

            switch (data.Type)
            {
                case FleetType.Science:
                    _accent = ai ? new Color(0.55f, 1f, 0.70f) : new Color(0.20f, 1.00f, 0.62f);
                    if (forceGlyph) _glyphSr.sprite = _scienceSp;
                    break;
                case FleetType.Constructor:
                    _accent = ai ? new Color(1f, 0.58f, 0.22f) : new Color(1.00f, 0.78f, 0.28f);
                    if (forceGlyph) _glyphSr.sprite = _constructorSp;
                    break;
                default:
                    _accent = ai ? new Color(1f, 0.28f, 0.30f) : new Color(0.22f, 0.92f, 1.00f);
                    if (forceGlyph) _glyphSr.sprite = _militarySp;
                    break;
            }

            if (data.InCombat) _accent = new Color(1f, 0.18f, 0.16f);
            else if (data.State == FleetState.InHyperlane)
                _accent = Color.Lerp(_accent, new Color(1f, 0.86f, 0.32f), 0.4f);

            _discSr.color = new Color(0.012f, 0.045f, 0.06f, 0.92f);
            _rimSr.color = Color.Lerp(_accent, Color.white, 0.25f);
            _glyphSr.color = Color.Lerp(_accent, Color.white, 0.45f);
            _bloomSr.color = new Color(_accent.r, _accent.g, _accent.b, 0.42f);
            _ringA.color = _accent;
            _ringB.color = new Color(_accent.r, _accent.g, _accent.b, 0.7f);
            _sweepSr.color = new Color(_accent.r, _accent.g, _accent.b, 0.35f);
            _pipSr.color = _accent;
            if (_stem != null)
            {
                var c = new Color(_accent.r, _accent.g, _accent.b, 0.75f);
                _stem.startColor = c;
                _stem.endColor = new Color(c.r, c.g, c.b, 0.12f);
                if (_stem.material != null) _stem.material.color = c;
            }

            bool scienceFx = data.Type == FleetType.Science || data.State == FleetState.Surveying;
            _sweepSr.enabled = scienceFx;
            _ringB.enabled = true;
            for (int i = 0; i < _orbiters.Length; i++)
                _orbiterSr[i].color = Color.Lerp(_accent, Color.white, 0.5f);
        }

        private void Animate()
        {
            var data = _fleet.Data;
            bool busy = data.State == FleetState.Surveying || data.State == FleetState.Constructing
                        || data.State == FleetState.InHyperlane || data.InCombat;

            float beat = data.InCombat ? 8.5f : busy ? 4.4f : 2.35f;
            float breathe = 0.5f + 0.5f * Mathf.Sin(_anim * beat);
            float flicker = 0.92f + Mathf.PerlinNoise(_anim * 7.5f, 0.3f) * 0.08f;

            _bloomSr.color = new Color(_accent.r, _accent.g, _accent.b,
                (0.22f + breathe * 0.14f + _selectPunch * 0.12f) * flicker);
            _bloomSr.transform.localScale = Vector3.one * (1.28f + breathe * 0.06f + _selectPunch * 0.08f);

            _rimSr.color = Color.Lerp(_accent, Color.white, 0.2f + breathe * 0.2f);
            float rimScale = 0.82f + breathe * 0.015f;
            _rimSr.transform.localScale = Vector3.one * rimScale;

            float spinA = data.Type == FleetType.Science ? 42f : data.Type == FleetType.Constructor ? -28f : 16f;
            float spinB = data.Type == FleetType.Science ? -26f : 11f;
            if (data.InCombat) { spinA = 110f; spinB = -80f; }
            if (data.State == FleetState.Surveying) spinA = 85f;
            _ringA.transform.localRotation = Quaternion.Euler(0, 0, _anim * spinA);
            _ringB.transform.localRotation = Quaternion.Euler(0, 0, _anim * spinB);
            _ringA.color = new Color(_accent.r, _accent.g, _accent.b, 0.55f + breathe * 0.35f);
            _ringB.color = new Color(_accent.r, _accent.g, _accent.b, 0.35f + (1f - breathe) * 0.3f);

            float gPulse = 0.68f + Mathf.Sin(_anim * beat * 0.5f) * 0.03f + _selectPunch * 0.04f;
            _glyphSr.transform.localScale = Vector3.one * gPulse;
            if (data.Type == FleetType.Constructor)
                _glyphSr.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(_anim * 1.8f) * 12f);
            else if (data.Type == FleetType.Science)
                _glyphSr.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(_anim * 1.4f) * 4f);
            else
                _glyphSr.transform.localRotation = Quaternion.identity;
            _glyphSr.color = Color.Lerp(_accent, Color.white, 0.35f + breathe * 0.25f) * flicker;

            if (_sweepSr.enabled)
            {
                _sweepSr.transform.localRotation = Quaternion.Euler(0, 0, -_anim * 95f);
                float sa = 0.12f + 0.28f * (0.5f + 0.5f * Mathf.Sin(_anim * 3.2f));
                _sweepSr.color = new Color(_accent.r, _accent.g, _accent.b, sa);
            }

            float orbitR = 0.36f + 0.02f * Mathf.Sin(_anim * 2f);
            float orbitSpd = data.Type == FleetType.Science ? 1.8f : data.Type == FleetType.Constructor ? 1.1f : 0.85f;
            if (data.InCombat) orbitSpd = 3.2f;
            for (int i = 0; i < _orbiters.Length; i++)
            {
                float a = _anim * orbitSpd + i * Mathf.PI * 2f / 3f;
                _orbiters[i].localPosition = new Vector3(Mathf.Cos(a) * orbitR, Mathf.Sin(a) * orbitR, -0.02f);
                float twinkle = 0.45f + 0.55f * Mathf.Sin(_anim * 6f + i * 2.1f);
                var c = Color.Lerp(_accent, Color.white, 0.55f);
                c.a = twinkle;
                _orbiterSr[i].color = c;
                _orbiters[i].localScale = Vector3.one * (0.09f + twinkle * 0.04f);
            }

            _pipSr.transform.localScale = Vector3.one * (0.20f + breathe * 0.03f);
            _pipSr.color = new Color(_accent.r, _accent.g, _accent.b, 0.7f + breathe * 0.3f);

            if (_stem != null)
            {
                float sa = (0.45f + breathe * 0.25f + _selectPunch * 0.2f) * flicker;
                var top = new Color(_accent.r, _accent.g, _accent.b, sa);
                _stem.startColor = top;
                _stem.endColor = new Color(_accent.r, _accent.g, _accent.b, 0.08f);
            }
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