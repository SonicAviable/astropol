using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// «Вибрация» по смыслу для важных событий — вместе со своим звуком (SfxLibrary: RedAlert, Bankrupt):
    ///   • атака на наши системы — края экрана дважды вспыхивают красным, геймпад даёт два резких толчка;
    ///   • банкротство — один долгий янтарный «провал» света и низкий затухающий гул в геймпаде.
    /// Обычные клики и оповещения ничего такого не делают — поэтому тревогу невозможно спутать с рутиной.
    /// Отключается в настройках («Вспышка и вибрация при тревоге»).
    /// </summary>
    public sealed class AlarmFeedback : MonoBehaviour
    {
        private enum Pattern { None, Attack, Bankrupt }

        private static AlarmFeedback s_instance;
        private static readonly Color AttackColor = new Color(1f, 0.16f, 0.14f);
        private static readonly Color BankruptColor = new Color(1f, 0.62f, 0.12f);
        private const float AttackTime = 1.25f, BankruptTime = 1.8f, Cooldown = 5f;

        private RawImage _edge;
        private Texture2D _tex;
        private Pattern _pattern, _lastKind;
        private float _t, _len, _lastFire = -100f;
        private bool _rumbling;

        /// <summary>Атака: вспышка красным и два толчка геймпада.</summary>
        public static void Attack() => Fire(Pattern.Attack);

        /// <summary>Банкротство: долгий янтарный провал и низкий гул.</summary>
        public static void Bankrupt() => Fire(Pattern.Bankrupt);

        private static void Fire(Pattern p)
        {
            if (!GameSettings.AlarmFx || GameAudio.Suppressed) return;
            if (s_instance == null)
            {
                var go = new GameObject("[UI] AlarmFeedback");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<AlarmFeedback>();
            }
            s_instance.Begin(p);
        }

        private void Begin(Pattern p)
        {
            // Несколько источников одной беды (осада + бой + оповещение) — одна вспышка
            if (Time.unscaledTime - _lastFire < Cooldown && p <= _lastKind) return;
            _lastFire = Time.unscaledTime;
            _lastKind = p;
            if (_edge == null) Build();
            _pattern = p;
            _t = 0f;
            _len = p == Pattern.Attack ? AttackTime : BankruptTime;
            var c = p == Pattern.Attack ? AttackColor : BankruptColor;
            _edge.color = new Color(c.r, c.g, c.b, 1f);
            _edge.canvasRenderer.SetAlpha(0f);
            _edge.gameObject.SetActive(true);
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;   // поверх всего, но не ловит мышь
            var go = new GameObject("Edge");
            go.transform.SetParent(transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _edge = go.AddComponent<RawImage>();
            _edge.raycastTarget = false;
            _edge.texture = _tex = EdgeTexture();
            LG.Ignore(go, true);
            go.SetActive(false);
        }

        /// <summary>Свечение по краям: плотное у рамки, к центру сходит на нет (углы ярче).</summary>
        private static Texture2D EdgeTexture()
        {
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "AlarmEdge" };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N, v = (y + 0.5f) / N;
                    float dx = Mathf.Min(u, 1f - u), dy = Mathf.Min(v, 1f - v);
                    float ex = Mathf.Clamp01(1f - dx / 0.16f), ey = Mathf.Clamp01(1f - dy / 0.22f);
                    float a = Mathf.Max(ex * ex, ey * ey) + 0.35f * ex * ey;
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        private void Update()
        {
            if (_pattern == Pattern.None) return;
            _t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_t / _len);
            float light, low, high;
            if (_pattern == Pattern.Attack)
            {
                // Два коротких удара: 0 → пик → 0, второй чуть слабее
                float p1 = Pulse(_t, 0.00f, 0.42f), p2 = Pulse(_t, 0.55f, 0.50f) * 0.8f;
                light = 0.55f * Mathf.Max(p1, p2);
                low = Mathf.Max(p1, p2) > 0.35f ? 0.55f : 0f;
                high = Mathf.Max(p1, p2) > 0.55f ? 0.85f : 0f;
            }
            else
            {
                // Медленный провал: быстро набирает, долго гаснет; гул — только низкий мотор
                float p = Pulse(_t, 0f, _len);
                light = 0.42f * p * (0.85f + 0.15f * Mathf.Sin(_t * 22f));
                low = 0.7f * (1f - k) * (1f - k);
                high = 0f;
            }
            if (_edge != null) _edge.canvasRenderer.SetAlpha(light);
            Rumble(Application.isFocused ? low : 0f, Application.isFocused ? high : 0f);
            if (_t >= _len)
            {
                _pattern = Pattern.None;
                if (_edge != null) _edge.gameObject.SetActive(false);
                Rumble(0f, 0f);
            }
        }

        /// <summary>Импульс 0 → 1 → 0 длиной len, начиная со start (быстрый подъём, плавный спад).</summary>
        private static float Pulse(float t, float start, float len)
        {
            float x = (t - start) / len;
            if (x <= 0f || x >= 1f) return 0f;
            return x < 0.18f ? x / 0.18f : Mathf.Pow(1f - (x - 0.18f) / 0.82f, 1.6f);
        }

        private void Rumble(float low, float high)
        {
#if ENABLE_INPUT_SYSTEM
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad == null) return;
            if (low <= 0f && high <= 0f)
            {
                if (_rumbling) { pad.ResetHaptics(); _rumbling = false; }
                return;
            }
            pad.SetMotorSpeeds(low, high);
            _rumbling = true;
#endif
        }

        private void OnDisable() => Rumble(0f, 0f);

        private void OnDestroy()
        {
            Rumble(0f, 0f);
            if (_tex != null) Destroy(_tex);
            if (s_instance == this) s_instance = null;
        }
    }
}
