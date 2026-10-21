using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// «Директивы» — карточка стартовых целей слева под главным меню.
    ///   • Одна текущая директива крупно: медальон с иконкой, прогресс, награда, подсказка.
    ///   • Сверху — восемь сегментов главы: выполненные золотые, текущий мерцает.
    ///   • Выполнение — маленькое событие: вспышка, галочка в медальоне, всплывающая награда, звук сделки,
    ///     затем карточка «перелистывается» на следующую директиву. В конце — «Глава I завершена», и карточка уходит.
    ///   • Щелчок по карточке показывает, с чего начать; по шапке — сворачивает (запоминается).
    /// Если под меню не хватает места (крупный масштаб интерфейса) — встаёт справа от меню.
    /// </summary>
    public class StarterGoalsPanel : MonoBehaviour
    {
        public static StarterGoalsPanel Instance { get; private set; }

        private const float W = 268f, HeadH = 28f, BodyH = 118f, Gap = 8f, MinimapReserve = 322f;

        private static readonly Color Gold = new Color(1f, 0.82f, 0.38f);
        private static readonly Color Cyan = new Color(0.36f, 0.95f, 0.88f);
        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Primary => UIManager.DS.TextPrimary;

        private Canvas _canvas;
        private RectTransform _root, _body, _content, _fill, _sweep, _medalRt, _floaterRt;
        private CanvasGroup _group, _contentGroup;
        private Image _flash, _medalGlow, _icon, _rewardIcon, _headLine;
        private Text _chapter, _title, _count, _reward, _hint, _chev, _floater;
        private readonly List<Image> _segs = new List<Image>();
        private bool _collapsed;

        // состояние показа
        private StarterGoals.Goal _shown;
        private int _shownCur = -1;
        private float _bar, _barTarget, _barDrawn = -1f, _countPop, _layoutTimer, _refreshTimer;
        private readonly Queue<StarterGoals.Goal> _queue = new Queue<StarterGoals.Goal>();
        private StarterGoals.Goal _celebrating;
        private float _celebT = -1f, _floatT = -1f, _finaleT = -1f;
        private bool _finished, _swapped;

        /// <summary>Карточка сейчас на экране (тогда уведомления о целях не нужны).</summary>
        public static bool Showing => Instance != null && Instance._group != null && Instance._group.alpha > 0.5f;

        /// <summary>Отпраздновать выполненную директиву на карточке. false — карточки не видно.</summary>
        public static bool Celebrate(StarterGoals.Goal g)
        {
            if (!Showing || g == null || Instance._collapsed) return false;
            Instance._queue.Enqueue(g);
            return true;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnEnable() => UIManager.OnGameStarted += HandleGameStarted;
        private void OnDisable() => UIManager.OnGameStarted -= HandleGameStarted;

        private void HandleGameStarted()
        {
            _finished = false;
            _shown = null;
            _shownCur = -1;
            _queue.Clear();
            _celebrating = null;
            _celebT = _floatT = _finaleT = -1f;
        }

        // ================================================================ Постройка

        public void Init(Canvas hud)
        {
            _canvas = hud;
            _collapsed = PlayerPrefs.GetInt("directives.collapsed", 0) == 1;

            _root = LGBuild.Rect(hud.transform, "[UI] Directives");
            _root.anchorMin = _root.anchorMax = new Vector2(0, 1);
            _root.pivot = new Vector2(0, 1);
            _root.sizeDelta = new Vector2(W, HeadH);
            _group = _root.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;

            BuildHead();
            BuildBody();
            ApplyCollapsed();
        }

        private void BuildHead()
        {
            var head = LGBuild.Panel(_root, "Head", new Color(0.035f, 0.030f, 0.012f, 0.95f), raycast: true);
            head.rectTransform.TopBand(0, HeadH);
            var fx = LG.Platter(head.gameObject, 8f);
            fx.FillMultiplier = 3f;
            fx.SetRim(new Color(Gold.r, Gold.g, Gold.b, 0.30f));
            LG.Ignore(head.gameObject, includeChildren: false);

            var ic = LGIcons.Create(head.transform, LGIcon.Target, 13, Gold);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(11, 0), new Vector2(13, 13));
            _chapter = LGBuild.Label(head.transform, Loc.T("ДИРЕКТИВЫ"), 11, Gold, TextAnchor.MiddleLeft, bold: true);
            _chapter.rectTransform.Stretch(30, 0, 120, 0);
            _chapter.Track(1.6f);

            // сегменты главы
            var segHost = LGBuild.Rect(head.transform, "Segments");
            segHost.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-26, 0), new Vector2(104, 6));
            int n = StarterGoals.Goals.Count;
            float sw = (104f - (n - 1) * 3f) / Mathf.Max(1, n);
            for (int i = 0; i < n; i++)
            {
                var seg = LGBuild.Panel(segHost, "Seg", new Color(1f, 1f, 1f, 0.14f));
                seg.rectTransform.anchorMin = seg.rectTransform.anchorMax = new Vector2(0, 0.5f);
                seg.rectTransform.pivot = new Vector2(0, 0.5f);
                seg.rectTransform.sizeDelta = new Vector2(sw, 5f);
                seg.rectTransform.anchoredPosition = new Vector2(i * (sw + 3f), 0);
                seg.raycastTarget = false;
                LG.Ignore(seg.gameObject);
                _segs.Add(seg);
            }

            _chev = LGBuild.Label(head.transform, "▴", 12, Muted, TextAnchor.MiddleRight, bold: true);
            _chev.rectTransform.Stretch(0, 0, 9, 0);

            var line = LGBuild.Panel(head.transform, "Line", new Color(Gold.r, Gold.g, Gold.b, 0.5f));
            line.sprite = FactionSelectScreen.RadialSprite();
            line.raycastTarget = false;
            line.rectTransform.anchorMin = new Vector2(0.05f, 0);
            line.rectTransform.anchorMax = new Vector2(0.95f, 0);
            line.rectTransform.sizeDelta = new Vector2(0, 1.5f);
            line.rectTransform.anchoredPosition = new Vector2(0, 0.75f);
            _headLine = line;

            var click = head.gameObject.AddComponent<DirectiveClick>();
            click.OnClick = () =>
            {
                _collapsed = !_collapsed;
                PlayerPrefs.SetInt("directives.collapsed", _collapsed ? 1 : 0);
                SFXManager.Play(Sfx.PanelSlide);
                ApplyCollapsed();
            };
            TooltipHelper.Attach(head.gameObject, Loc.T("Свернуть или развернуть директивы"));
        }

        private void BuildBody()
        {
            var body = LGBuild.Panel(_root, "Body", new Color(0.016f, 0.024f, 0.034f, 0.94f), raycast: true);
            _body = body.rectTransform;
            _body.TopBand(HeadH + 4f, BodyH);
            var fx = LG.Platter(body.gameObject, 10f);
            fx.FillMultiplier = 3f;
            fx.SetRim(new Color(Gold.r, Gold.g, Gold.b, 0.16f));
            LG.Ignore(body.gameObject, includeChildren: false);

            // мягкий блик, иногда пробегающий по карточке
            var clip = LGBuild.Rect(_body, "Clip");
            clip.Stretch(2, 2, 2, 2);
            clip.gameObject.AddComponent<RectMask2D>();
            var sw = LGBuild.Panel(clip, "Sweep", new Color(1f, 0.9f, 0.6f, 0.07f));
            sw.sprite = FactionSelectScreen.RadialSprite();
            sw.raycastTarget = false;
            _sweep = sw.rectTransform;

            _content = LGBuild.Rect(_body, "Content");
            _content.Stretch();
            _contentGroup = _content.gameObject.AddComponent<CanvasGroup>();

            // медальон
            var glow = LGBuild.Panel(_content, "MedalGlow", new Color(Gold.r, Gold.g, Gold.b, 0.22f));
            glow.sprite = FactionSelectScreen.RadialSprite();
            glow.raycastTarget = false;
            glow.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(38, -40), new Vector2(84, 84));
            _medalGlow = glow;
            var medal = LGBuild.Panel(_content, "Medal", new Color(0.10f, 0.08f, 0.03f, 1f));
            medal.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(38, -40), new Vector2(50, 50));
            var mfx = LG.Platter(medal.gameObject, 25f);
            mfx.FillMultiplier = 2f;
            mfx.SetRim(new Color(Gold.r, Gold.g, Gold.b, 0.75f));
            LG.Ignore(medal.gameObject, includeChildren: false);
            _medalRt = medal.rectTransform;
            _icon = LGIcons.Create(medal.transform, LGIcon.Target, 24, Gold);
            _icon.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24, 24));

            _title = LGBuild.Label(_content, "", 13, Primary, TextAnchor.UpperLeft, bold: true, wrap: true);
            _title.rectTransform.anchorMin = new Vector2(0, 1);
            _title.rectTransform.anchorMax = new Vector2(1, 1);
            _title.rectTransform.pivot = new Vector2(0, 1);
            _title.rectTransform.offsetMin = new Vector2(74, -46);
            _title.rectTransform.offsetMax = new Vector2(-10, -10);
            _title.verticalOverflow = VerticalWrapMode.Truncate;

            // полоса прогресса и счётчик
            var barHost = LGBuild.Rect(_content, "Bar");
            barHost.anchorMin = barHost.anchorMax = new Vector2(0, 1);
            barHost.pivot = new Vector2(0, 0.5f);
            barHost.anchoredPosition = new Vector2(74, -54);
            barHost.sizeDelta = new Vector2(W - 74 - 52, 6);
            _fill = LGBuild.Bar(barHost, Gold, 0f, 5f);
            _count = LGBuild.Label(_content, "", 12, Gold, TextAnchor.MiddleRight, bold: true);
            _count.rectTransform.anchorMin = _count.rectTransform.anchorMax = new Vector2(1, 1);
            _count.rectTransform.pivot = new Vector2(1, 0.5f);
            _count.rectTransform.anchoredPosition = new Vector2(-10, -54);
            _count.rectTransform.sizeDelta = new Vector2(44, 16);

            // награда
            _rewardIcon = LGIcons.Create(_content, LGIcon.Energy, 13, Gold);
            _rewardIcon.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(74, -71), new Vector2(13, 13));
            _reward = LGBuild.Label(_content, "", 11, new Color(1f, 0.90f, 0.62f), TextAnchor.MiddleLeft, bold: true);
            _reward.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(91, -71), new Vector2(W - 100, 16));

            // подсказка
            _hint = LGBuild.Label(_content, "", 10, Muted, TextAnchor.UpperLeft, wrap: true);
            _hint.rectTransform.anchorMin = new Vector2(0, 0);
            _hint.rectTransform.anchorMax = new Vector2(1, 0);
            _hint.rectTransform.pivot = new Vector2(0.5f, 0);
            _hint.rectTransform.offsetMin = new Vector2(12, 6);
            _hint.rectTransform.offsetMax = new Vector2(-10, 34);
            _hint.verticalOverflow = VerticalWrapMode.Truncate;
            _hint.lineSpacing = 1.05f;

            // вспышка выполнения
            var flash = LGBuild.Panel(_body, "Flash", new Color(Gold.r, Gold.g, Gold.b, 0f));
            flash.rectTransform.Stretch(2, 2, 2, 2);
            flash.raycastTarget = false;
            _flash = flash;

            // всплывающая награда
            _floater = LGBuild.Label(_root, "", 14, new Color(1f, 0.90f, 0.55f), TextAnchor.MiddleLeft, bold: true);
            _floaterRt = _floater.rectTransform;
            _floaterRt.anchorMin = _floaterRt.anchorMax = new Vector2(0, 1);
            _floaterRt.pivot = new Vector2(0, 0.5f);
            _floaterRt.sizeDelta = new Vector2(200, 20);
            var sh = _floater.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.8f);
            sh.effectDistance = new Vector2(0, -1);
            _floater.enabled = false;

            var click = body.gameObject.AddComponent<DirectiveClick>();
            click.OnClick = () =>
            {
                var g = _celebrating == null ? _shown : null;
                if (g?.Show == null) return;
                SFXManager.Play(Sfx.UiClick);
                g.Show();
            };
            _bodyTip = body.gameObject;
        }

        private GameObject _bodyTip;

        private void ApplyCollapsed()
        {
            _body.gameObject.SetActive(!_collapsed);
            _chev.text = _collapsed ? "▾" : "▴";
            _root.sizeDelta = new Vector2(W, _collapsed ? HeadH : HeadH + 4f + BodyH);
            _layoutTimer = 0f;
        }

        // ================================================================ Кадр

        private void Update()
        {
            if (_root == null) return;
            float dt = Time.unscaledDeltaTime;

            bool inSystem = SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView;
            bool tutorial = TutorialManager.Instance != null && TutorialManager.Instance.IsActive;
            bool want = UIManager.IsGameStarted && !inSystem && !tutorial && !_finished
                        && (StarterGoals.Visible || _celebrating != null || _queue.Count > 0 || _finaleT >= 0f);
            float a = Mathf.MoveTowards(_group.alpha, want ? 1f : 0f, dt * 4f);
            if (!Mathf.Approximately(a, _group.alpha)) _group.alpha = a;
            _group.blocksRaycasts = _group.interactable = a > 0.5f;
            if (a <= 0f && !want) return;

            _layoutTimer -= dt;
            if (_layoutTimer <= 0f) { _layoutTimer = 0.5f; Layout(); }

            _refreshTimer -= dt;
            if (_celebrating == null && _finaleT < 0f)
            {
                if (_queue.Count > 0) StartCelebration(_queue.Dequeue());
                else if (_refreshTimer <= 0f) { _refreshTimer = 0.4f; Refresh(); }
            }

            Animate(dt);
        }

        /// <summary>Под меню, если хватает места до мини-карты; иначе справа от меню.</summary>
        private void Layout()
        {
            var ui = UIManager.Instance;
            if (ui == null || _canvas == null) return;
            float canvasH = ((RectTransform)_canvas.transform).rect.height;
            float h = _root.sizeDelta.y;
            float under = ui.NavBottom + Gap;
            Vector2 pos = under + h <= canvasH - MinimapReserve
                ? new Vector2(10f, -under)
                : new Vector2(10f + ui.NavWidth + 12f, -ui.NavTop);
            if ((_root.anchoredPosition - pos).sqrMagnitude > 1f) _root.anchoredPosition = pos;
        }

        private void Refresh()
        {
            var g = StarterGoals.Current;
            int done = StarterGoals.DoneCount;
            int total = StarterGoals.Goals.Count;
            _chapter.text = Loc.F("ДИРЕКТИВЫ · {0}/{1}", done, total);
            if (g == null) return;

            int cur = Mathf.Min(StarterGoals.SafeCurrent(g), g.Target);
            if (g != _shown)
            {
                _shown = g;
                _shownCur = cur;
                _bar = 0f;
                ShowGoal(g);
            }
            else if (cur != _shownCur)
            {
                if (cur > _shownCur) _countPop = 1f;
                _shownCur = cur;
            }
            _barTarget = cur / (float)Mathf.Max(1, g.Target);
            _count.text = $"{cur}/{g.Target}";
        }

        private void ShowGoal(StarterGoals.Goal g)
        {
            _icon.sprite = LGIcons.Get(g.Icon);
            _icon.color = Gold;
            _title.text = g.Title;
            _hint.text = g.Hint;
            _rewardIcon.sprite = LGIcons.Get(g.RewardIcon);
            _rewardIcon.enabled = true;
            _reward.text = Loc.F("Награда: {0}", g.RewardText);
            _fill.parent.gameObject.SetActive(true);
            _count.enabled = true;
            TooltipHelper.Attach(_bodyTip, $"<b>{g.Title}</b>\n{g.Hint}\n\n<color=#8AA2A8>" + Loc.T("Щелчок — показать, с чего начать") + "</color>");
        }

        // ================================================================ Выполнение

        private void StartCelebration(StarterGoals.Goal g)
        {
            _celebrating = g;
            _celebT = 0f;
            _swapped = false;
            // показываем именно выполненную директиву, даже если игрок видел другую
            if (_shown != g) { _shown = g; ShowGoal(g); }
            _icon.sprite = LGIcons.Get(LGIcon.Check);
            _title.text = Loc.T("Директива выполнена");
            _count.text = $"{g.Target}/{g.Target}";
            _barTarget = 1f;
            _floater.text = g.RewardText;
            _floater.enabled = true;
            _floatT = 0f;
            SFXManager.Play(Sfx.DealAccepted, 0.9f);
        }

        private void Animate(float dt)
        {
            float t = Time.unscaledTime;

            // блик по карточке раз в ~11 с
            float sx = Mathf.Repeat(t, 11f) / 2.2f - 0.5f;
            _sweep.anchorMin = new Vector2(sx, -0.4f);
            _sweep.anchorMax = new Vector2(sx + 0.4f, 1.4f);

            // сегменты главы
            int done = StarterGoals.DoneCount;
            if (_celebrating != null) done = Mathf.Max(0, StarterGoals.IndexOf(_celebrating) + (_celebT > 0.35f ? 1 : 0));
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 3.2f);
            for (int i = 0; i < _segs.Count; i++)
            {
                Color c = i < done ? Gold
                        : i == done ? new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f + 0.55f * pulse)
                        : new Color(1f, 1f, 1f, 0.14f);
                if (_segs[i].color != c) _segs[i].color = c;
            }
            _headLine.canvasRenderer.SetAlpha(0.6f + 0.4f * pulse);

            if (_collapsed) { TickCelebration(dt); return; }

            // медальон «дышит»
            float breathe = 0.5f + 0.5f * Mathf.Sin(t * 1.6f);
            _medalGlow.canvasRenderer.SetAlpha(0.55f + 0.45f * breathe);

            // полоса догоняет значение
            float nb = Mathf.MoveTowards(_bar, _barTarget, dt * 1.6f);
            if (!Mathf.Approximately(nb, _barDrawn)) { _bar = nb; _barDrawn = nb; LGBuild.SetBar(_fill, _bar, Gold); }
            if (_countPop > 0f)
            {
                _countPop = Mathf.MoveTowards(_countPop, 0f, dt * 3f);
                _count.rectTransform.localScale = Vector3.one * (1f + 0.35f * Mathf.Sin(_countPop * Mathf.PI));
            }

            TickCelebration(dt);
        }

        private void TickCelebration(float dt)
        {
            // всплывающая награда
            if (_floatT >= 0f)
            {
                _floatT += dt / 1.4f;
                float k = Mathf.Clamp01(_floatT);
                _floaterRt.anchoredPosition = new Vector2(92f, -(HeadH + 4f + 71f) + 34f * (1f - (1f - k) * (1f - k)));
                _floater.canvasRenderer.SetAlpha(k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f);
                if (_floatT >= 1f) { _floatT = -1f; _floater.enabled = false; }
            }

            if (_finaleT >= 0f)
            {
                _finaleT += dt;
                float fin = Mathf.Clamp01(_finaleT / 0.6f);
                _flash.color = new Color(Gold.r, Gold.g, Gold.b, 0.28f * (1f - fin));
                if (_finaleT > 8f) { _finaleT = -1f; _finished = true; }
                return;
            }

            if (_celebrating == null) return;
            _celebT += dt;
            float ct = _celebT;

            // вспышка и «удар» медальона
            _flash.color = new Color(Gold.r, Gold.g, Gold.b, 0.35f * Mathf.Clamp01(1f - ct / 0.6f));
            float pop = ct < 0.45f ? Mathf.Sin(ct / 0.45f * Mathf.PI) : 0f;
            _medalRt.localScale = Vector3.one * (1f + 0.25f * pop);

            // перелистывание на следующую директиву
            if (ct > 1.4f && ct <= 1.7f)
            {
                float k = (ct - 1.4f) / 0.3f;
                _contentGroup.alpha = 1f - k;
                _content.anchoredPosition = new Vector2(-28f * k, 0);
            }
            else if (ct > 1.7f && !_swapped)
            {
                _swapped = true;
                _shown = null;
                if (StarterGoals.Current == null && _queue.Count == 0) { ShowFinale(); return; }
                Refresh();
            }
            if (ct > 1.7f)
            {
                float k = Mathf.Clamp01((ct - 1.7f) / 0.35f);
                _contentGroup.alpha = k;
                _content.anchoredPosition = new Vector2(28f * (1f - k), 0);
                if (k >= 1f) { _celebrating = null; _celebT = -1f; _medalRt.localScale = Vector3.one; }
            }
        }

        private void ShowFinale()
        {
            _finaleT = 0f;
            _celebrating = null;
            _celebT = -1f;
            _medalRt.localScale = Vector3.one;
            _contentGroup.alpha = 1f;
            _content.anchoredPosition = Vector2.zero;
            _icon.sprite = LGIcons.Get(LGIcon.Trophy);
            _title.text = Loc.T("Глава I завершена");
            _hint.text = Loc.T("Империя встала на ноги. Дальше — путь к победе: клавиша V покажет, как близко каждая из них.");
            _reward.text = Loc.T("Все директивы выполнены");
            _rewardIcon.enabled = false;
            _fill.parent.gameObject.SetActive(false);
            _count.enabled = false;
            _chapter.text = Loc.F("ДИРЕКТИВЫ · {0}/{0}", StarterGoals.Goals.Count);
            SFXManager.Play(Sfx.NotifySuccess);
        }
    }

    /// <summary>Щелчок по области карточки без «кнопочного» оформления (Button перекрасил бы панель).</summary>
    public class DirectiveClick : MonoBehaviour, IPointerClickHandler
    {
        public System.Action OnClick;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) OnClick?.Invoke();
        }
    }
}
