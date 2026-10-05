using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Очередь всплывающих уведомлений в правом верхнем углу.
    /// Показывает до 5 штук одновременно, автоматически гасит старые.
    /// </summary>
    public class NotificationCenter : MonoBehaviour
    {
        public static NotificationCenter Instance { get; private set; }

        public enum Kind { Info, Success, Warning, Danger }

        private RectTransform _stack;
        private Font _font;
        private readonly List<Toast> _active = new List<Toast>();
        private const int MaxVisible = 5;

        private sealed class Toast
        {
            public GameObject Go;
            public RectTransform Body;
            public CanvasGroup Group;
            public LayoutElement Layout;
            public LiquidGlassEffect Fx;
            public float Height;
            public Coroutine Routine;
            public bool Leaving;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _font = GameFont.Regular;
        }

        private void Start()
        {
            var canvasObj = GameObject.Find("GalaxyCanvas");
            var canvas = canvasObj != null ? canvasObj.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
            if (canvas == null) { Debug.LogWarning("[NotificationCenter] Canvas не найден."); return; }

            var stackGo = new GameObject("[UI] NotificationStack");
            stackGo.transform.SetParent(canvas.transform, false);

            _stack = stackGo.AddComponent<RectTransform>();
            _stack.anchorMin = new Vector2(1, 1);
            _stack.anchorMax = new Vector2(1, 1);
            _stack.pivot = new Vector2(1, 1);
            _stack.anchoredPosition = new Vector2(-18, -70);
            _stack.sizeDelta = new Vector2(360, 0);

            // Свой подканвас: уведомления видны и поверх главного меню, и когда HUD скрыт
            var sub = stackGo.AddComponent<Canvas>();
            sub.overrideSorting = true;
            sub.sortingOrder = 900;
            stackGo.AddComponent<GraphicRaycaster>();
            stackGo.AddComponent<CanvasGroup>().ignoreParentGroups = true;

            var vlg = stackGo.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.spacing = 0;
            vlg.padding = new RectOffset(0, 0, 0, 0);

            var csf = stackGo.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        public static void Show(string title, string body = "", Kind kind = Kind.Info, float duration = 5f)
        {
            if (Instance == null) return;
            Instance.Push(title, body, kind, duration);
        }

        private void Push(string title, string body, Kind kind, float duration)
        {
            if (_stack == null) return;

            Color accent = kind switch
            {
                Kind.Success => UIManager.DS.Green,
                Kind.Warning => UIManager.DS.Gold,
                Kind.Danger  => UIManager.DS.Red,
                _            => UIManager.DS.NeonCyan
            };

            bool hasBody = !string.IsNullOrEmpty(body);
            const float Gap = 8f;
            float cardH = hasBody ? 60f : 40f;

            // Внешний слот управляется layout'ом (высота анимируется → стек сдвигается плавно),
            // внутреннее стекло ездит отдельно.
            var go = new GameObject("Notif");
            go.transform.SetParent(_stack, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 0f;
            le.minHeight = 0f;
            var cg = go.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.blocksRaycasts = false;

            var cardGo = new GameObject("Card");
            cardGo.transform.SetParent(go.transform, false);
            var card = cardGo.AddComponent<RectTransform>();
            card.anchorMin = new Vector2(0, 1);
            card.anchorMax = new Vector2(1, 1);
            card.pivot = new Vector2(0.5f, 1);
            card.sizeDelta = new Vector2(0, cardH);
            card.anchoredPosition = Vector2.zero;

            var bg = cardGo.AddComponent<Image>();
            bg.color = UIManager.DS.BgDeep;
            bg.raycastTarget = false;
            var fx = LG.Glass(cardGo, hasBody ? 18f : 20f);
            fx.SetRim(new Color(accent.r, accent.g, accent.b, 0.65f));
            fx.ShadowMultiplier = 0.7f;

            // Светящаяся точка-индикатор вместо плоской полосы
            var dot = new GameObject("Dot");
            dot.transform.SetParent(cardGo.transform, false);
            var dRt = dot.AddComponent<RectTransform>();
            dRt.anchorMin = dRt.anchorMax = new Vector2(0, hasBody ? 1f : 0.5f);
            dRt.pivot = new Vector2(0.5f, 0.5f);
            dRt.sizeDelta = new Vector2(8, 8);
            dRt.anchoredPosition = new Vector2(18, hasBody ? -19f : 0f);
            dot.AddComponent<Image>().color = accent;
            LG.Dot(dot);

            var t1 = MakeText(cardGo.transform, title, 11, FontStyle.Bold, accent,
                hasBody ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            t1.rectTransform.anchorMin = new Vector2(0, hasBody ? 0.5f : 0f);
            t1.rectTransform.anchorMax = new Vector2(1, 1);
            t1.rectTransform.offsetMin = new Vector2(32, 0);
            t1.rectTransform.offsetMax = new Vector2(-14, hasBody ? -11 : 0);

            if (hasBody)
            {
                var t2 = MakeText(cardGo.transform, body, 10, FontStyle.Normal, UIManager.DS.TextPrimary, TextAnchor.LowerLeft);
                t2.rectTransform.anchorMin = new Vector2(0, 0);
                t2.rectTransform.anchorMax = new Vector2(1, 0.5f);
                t2.rectTransform.offsetMin = new Vector2(32, 10);
                t2.rectTransform.offsetMax = new Vector2(-14, 0);
                t2.horizontalOverflow = HorizontalWrapMode.Wrap;
                t2.verticalOverflow = VerticalWrapMode.Truncate;
            }

            var toast = new Toast
            {
                Go = go, Body = card, Group = cg, Layout = le, Fx = fx, Height = cardH + Gap
            };
            _active.Add(toast);

            // Лишние — не обрываем, а уводим быстрой анимацией
            int overflow = 0;
            foreach (var a in _active) if (!a.Leaving) overflow++;
            for (int i = 0; i < _active.Count && overflow > MaxVisible; i++)
            {
                var old = _active[i];
                if (old.Leaving) continue;
                Dismiss(old, 0.18f);
                overflow--;
            }

            toast.Routine = StartCoroutine(Lifecycle(toast, duration));
        }

        private void Dismiss(Toast toast, float speed)
        {
            if (toast == null || toast.Leaving) return;
            toast.Leaving = true;
            if (toast.Routine != null) StopCoroutine(toast.Routine);
            toast.Routine = StartCoroutine(Leave(toast, speed));
        }

        private IEnumerator Lifecycle(Toast toast, float duration)
        {
            // Вход: слот раскрывается, стекло выезжает справа с пружинным перелётом
            float t = 0f;
            const float inDur = 0.55f;
            while (t < inDur)
            {
                if (toast.Go == null) yield break;
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / inDur);
                toast.Layout.preferredHeight = toast.Height * LGEase.OutCubic(k * 1.6f);
                toast.Group.alpha = LGEase.OutCubic(k * 1.8f);
                toast.Body.anchoredPosition = new Vector2(80f * (1f - LGEase.OutBack(k, 1.2f)), 0f);
                toast.Fx.SetPulse(1f - k);
                yield return null;
            }
            toast.Layout.preferredHeight = toast.Height;
            toast.Group.alpha = 1f;
            toast.Body.anchoredPosition = Vector2.zero;
            toast.Fx.SetPulse(0f);

            yield return new WaitForSecondsRealtime(duration);

            toast.Leaving = true;
            yield return Leave(toast, 0.38f);
        }

        private IEnumerator Leave(Toast toast, float dur)
        {
            // Выход: стекло уезжает вправо и тает, затем слот плавно схлопывается
            float t = 0f;
            float startAlpha = toast.Group != null ? toast.Group.alpha : 0f;
            Vector2 startPos = toast.Body != null ? toast.Body.anchoredPosition : Vector2.zero;
            while (t < dur)
            {
                if (toast.Go == null) yield break;
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                toast.Group.alpha = startAlpha * (1f - LGEase.OutCubic(k));
                toast.Body.anchoredPosition = startPos + new Vector2(60f * LGEase.InCubic(k), 0f);
                yield return null;
            }

            float h0 = toast.Layout != null ? toast.Layout.preferredHeight : 0f;
            t = 0f;
            const float collapse = 0.24f;
            while (t < collapse)
            {
                if (toast.Go == null) yield break;
                t += Time.unscaledDeltaTime;
                toast.Layout.preferredHeight = h0 * (1f - LGEase.InOutCubic(t / collapse));
                yield return null;
            }

            _active.Remove(toast);
            if (toast.Go != null) Destroy(toast.Go);
        }

        private Text MakeText(Transform p, string v, int sz, FontStyle st, Color c, TextAnchor a)
        {
            var g = new GameObject("T");
            g.transform.SetParent(p, false);
            var t = g.AddComponent<Text>();
            t.font = st == FontStyle.Bold ? GameFont.Bold : GameFont.Regular;
            t.text = v; t.fontSize = sz; t.fontStyle = st; t.color = c;
            t.alignment = a; t.raycastTarget = false; t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }
    }
}