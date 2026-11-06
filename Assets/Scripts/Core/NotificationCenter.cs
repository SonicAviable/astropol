using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Очередь всплывающих уведомлений в правом верхнем углу.
    /// Показывает до 5 штук одновременно, автоматически гасит старые. Под курсором уведомление не исчезает;
    /// клик — камера к месту события (система указана явно или найдена по названию в тексте), ПКМ — убрать.
    /// Всё показанное попадает в журнал (<see cref="History"/>, окно «Журнал событий»).
    /// </summary>
    public class NotificationCenter : MonoBehaviour
    {
        public static NotificationCenter Instance { get; private set; }

        public enum Kind { Info, Success, Warning, Danger }

        private RectTransform _stack;
        private Font _font;
        private readonly List<Toast> _active = new List<Toast>();
        private const int MaxVisible = 5;
        /// <summary>Сколько секунд уведомление висит минимум (важные — дольше).</summary>
        private const float MinDuration = 7f, MinDurationImportant = 10f;

        /// <summary>Запись журнала событий.</summary>
        public sealed class Entry
        {
            public string Title, Body, Date;
            public Kind Kind;
            public int SystemId = -1;
        }

        public const int HistoryMax = 150;
        private static readonly List<Entry> s_history = new List<Entry>();
        /// <summary>Журнал: последние уведомления партии, новые — в конце.</summary>
        public static IReadOnlyList<Entry> History => s_history;
        /// <summary>Новых записей с последнего открытия журнала.</summary>
        public static int Unread { get; set; }
        public static event System.Action<Entry> OnLogged;

        /// <summary>Новая партия или загрузка: журнал пуст.</summary>
        public static void ClearHistory() { s_history.Clear(); Unread = 0; OnLogged?.Invoke(null); }

        private sealed class Toast
        {
            public GameObject Go;
            public RectTransform Body;
            public CanvasGroup Group;
            public LayoutElement Layout;
            public LiquidGlassEffect Fx;
            public float Height;
            public Coroutine Routine;
            public bool Leaving, Hovered;
            public int SystemId = -1;
        }

        /// <summary>Наведение и клик по уведомлению.</summary>
        private sealed class ToastHit : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
        {
            public System.Action<bool> Hover;
            public System.Action<PointerEventData.InputButton> Click;
            public void OnPointerEnter(PointerEventData e) => Hover?.Invoke(true);
            public void OnPointerExit(PointerEventData e) => Hover?.Invoke(false);
            public void OnPointerClick(PointerEventData e) => Click?.Invoke(e.button);
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

        /// <param name="systemId">Где случилось (клик по уведомлению — камера туда). −1 — найти систему по названию в тексте.</param>
        public static void Show(string title, string body = "", Kind kind = Kind.Info, float duration = 5f, int systemId = -1)
        {
            if (systemId < 0) systemId = GuessSystem(title + "\n" + body);
            Log(title, body, kind, systemId);
            if (Instance == null) return;
            Instance.Push(title, body, kind, duration, systemId);
        }

        private static void Log(string title, string body, Kind kind, int systemId)
        {
            var t = TimeManager.Instance;
            var e = new Entry
            {
                Title = title, Body = body ?? "", Kind = kind, SystemId = systemId,
                Date = t != null ? $"{t.Day:00}.{t.Month:00}.{t.Year}" : ""
            };
            s_history.Add(e);
            if (s_history.Count > HistoryMax) s_history.RemoveRange(0, s_history.Count - HistoryMax);
            Unread++;
            OnLogged?.Invoke(e);
        }

        private static readonly System.Text.RegularExpressions.Regex s_tags = new System.Text.RegularExpressions.Regex("<[^>]+>");

        /// <summary>Система, названная в тексте целым словом (самое длинное совпадение); −1 — не нашлось.</summary>
        public static int GuessSystem(string text)
        {
            if (string.IsNullOrEmpty(text)) return -1;
            text = s_tags.Replace(text, "");
            int best = -1, bestLen = 0;
            foreach (var sys in EmpireStats.Systems)
            {
                var n = sys?.Name;
                if (string.IsNullOrEmpty(n) || n.Length < 3 || n.Length <= bestLen) continue;
                int i = text.IndexOf(n, System.StringComparison.Ordinal);
                while (i >= 0)
                {
                    bool left = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
                    int end = i + n.Length;
                    bool right = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                    if (left && right) { best = sys.Id; bestLen = n.Length; break; }
                    i = text.IndexOf(n, i + 1, System.StringComparison.Ordinal);
                }
            }
            return best;
        }

        private void Push(string title, string body, Kind kind, float duration, int systemId)
        {
            if (_stack == null) return;
            duration = Mathf.Max(duration, kind == Kind.Danger || kind == Kind.Warning ? MinDurationImportant : MinDuration);
            bool located = systemId >= 0;
            SFXManager.Notify(kind);

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
            bg.raycastTarget = true;
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
            t1.rectTransform.offsetMax = new Vector2(located ? -34 : -14, hasBody ? -11 : 0);

            // Место события: значок прицела справа — клик по уведомлению переносит камеру туда
            if (located)
            {
                var pin = LGIcons.Create(cardGo.transform, LGIcon.Target, 13, accent);
                pin.rectTransform.At(new Vector2(1, hasBody ? 1f : 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-18, hasBody ? -19f : 0f), new Vector2(13, 13));
                pin.raycastTarget = false;
            }

            if (hasBody)
            {
                var t2 = MakeText(cardGo.transform, body, 10, FontStyle.Normal, UIManager.DS.TextPrimary, TextAnchor.LowerLeft);
                t2.rectTransform.anchorMin = new Vector2(0, 0);
                t2.rectTransform.anchorMax = new Vector2(1, 0.5f);
                t2.rectTransform.offsetMin = new Vector2(32, 10);
                t2.rectTransform.offsetMax = new Vector2(located ? -34 : -14, 0);
                t2.horizontalOverflow = HorizontalWrapMode.Wrap;
                t2.verticalOverflow = VerticalWrapMode.Truncate;
            }

            var toast = new Toast
            {
                Go = go, Body = card, Group = cg, Layout = le, Fx = fx, Height = cardH + Gap, SystemId = systemId
            };
            _active.Add(toast);
            var hit = cardGo.AddComponent<ToastHit>();
            hit.Hover = on => { toast.Hovered = on; if (toast.Fx != null) toast.Fx.SetPulse(on ? 0.35f : 0f); };
            hit.Click = button =>
            {
                if (button == PointerEventData.InputButton.Left && toast.SystemId >= 0)
                {
                    var sys = EmpireStats.GetSystem(toast.SystemId);
                    if (sys != null) UIManager.Instance?.FocusSystem(sys);
                }
                if (button != PointerEventData.InputButton.Middle) Dismiss(toast, 0.2f);
            };
            string tip = located ? Loc.T("ЛКМ — к месту события · ПКМ — убрать · все сообщения — в журнале (O)")
                                 : Loc.T("ПКМ — убрать · все сообщения — в журнале (O)");
            TooltipHelper.Attach(cardGo, tip);

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
            toast.Group.blocksRaycasts = true;
            toast.Body.anchoredPosition = Vector2.zero;
            toast.Fx.SetPulse(0f);

            // Под курсором уведомление не уходит — его можно дочитать
            float left = duration;
            while (left > 0f)
            {
                if (toast.Go == null) yield break;
                if (!toast.Hovered) left -= Time.unscaledDeltaTime;
                yield return null;
            }

            toast.Leaving = true;
            yield return Leave(toast, 0.38f);
        }

        private IEnumerator Leave(Toast toast, float dur)
        {
            // Выход: стекло уезжает вправо и тает, затем слот плавно схлопывается
            float t = 0f;
            if (toast.Group != null) toast.Group.blocksRaycasts = false;
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