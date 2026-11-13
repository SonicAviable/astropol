using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Карточка «Цель империи» справа внизу, на месте «Директив», когда те выполнены: значок цели, задача, прогресс,
    /// сколько лет осталось и награда. Щелчок — показать, с чего начать; крестик (дважды) — отказаться от цели.
    /// Пока ждёт выбор новой цели — карточка зовёт выбрать.
    /// </summary>
    public class EmpireGoalsPanel : MonoBehaviour
    {
        public static EmpireGoalsPanel Instance { get; private set; }
        /// <summary>Сколько места снизу занимает карточка (для планировщика), 0 — скрыта.</summary>
        public static float Reserve;

        private const float W = 300f, H = 124f, Gap = 8f;
        private static readonly Color Gold = new Color(1f, 0.82f, 0.38f);
        private static Color Muted => UIManager.DS.TextMuted;

        private RectTransform _root, _fill;
        private CanvasGroup _group;
        private Image _icon;
        private Text _head, _title, _count, _left, _reward, _abandonLabel;
        private Image _rewardIcon;
        private float _refresh, _armed;
        private bool _wanted;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy() { if (Instance == this) { Instance = null; Reserve = 0f; } }

        public void Init(Canvas hud)
        {
            _root = LGBuild.Rect(hud.transform, "[UI] EmpireGoals");
            _root.anchorMin = _root.anchorMax = new Vector2(1, 0);
            _root.pivot = new Vector2(1, 0);
            _root.sizeDelta = new Vector2(W, H);
            _group = _root.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;

            var bg = LGBuild.Panel(_root, "Card", new Color(0.020f, 0.040f, 0.070f, 0.95f), raycast: true);
            bg.rectTransform.Stretch();
            LG.Platter(bg.gameObject, 10f).SetRim(new Color(Gold.r, Gold.g, Gold.b, 0.35f));
            var btn = bg.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(Click);
            LGFx.HoverGlow(bg.gameObject);

            var hi = LGIcons.Create(_root, LGIcon.Target, 13, Gold);
            hi.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(12, -15), new Vector2(13, 13));
            _head = LGBuild.Label(_root, Loc.T("ЦЕЛЬ ИМПЕРИИ"), 11, Gold, TextAnchor.MiddleLeft, bold: true);
            _head.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(32, -15), new Vector2(220, 18));
            _head.Track(1.6f);
            _head.supportRichText = true;

            // Отказ от цели — двумя щелчками
            var ab = LGBuild.Panel(_root, "Abandon", new Color(0, 0, 0, 0), raycast: true);
            ab.rectTransform.At(new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(-6, -15), new Vector2(26, 22));
            var abb = ab.gameObject.AddComponent<Button>();
            abb.transition = Selectable.Transition.None;
            abb.onClick.AddListener(AbandonClick);
            _abandonLabel = LGBuild.Label(ab.transform, "×", 16, Muted, TextAnchor.MiddleCenter, bold: true);
            TooltipHelper.Attach(ab.gameObject, Loc.T("Отказаться от цели (дважды). Следующая — через год"));

            var line = LGBuild.Panel(_root, "Line", new Color(Gold.r, Gold.g, Gold.b, 0.35f));
            line.raycastTarget = false;
            line.rectTransform.anchorMin = new Vector2(0.04f, 1); line.rectTransform.anchorMax = new Vector2(0.96f, 1);
            line.rectTransform.sizeDelta = new Vector2(0, 1.2f);
            line.rectTransform.anchoredPosition = new Vector2(0, -30);

            var medal = LGBuild.Panel(_root, "Medal", new Color(0.10f, 0.08f, 0.03f, 0.95f));
            medal.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -40), new Vector2(40, 40));
            LG.Chip(medal.gameObject, new Color(Gold.r, Gold.g, Gold.b, 0.6f), 20f);
            _icon = LGIcons.Create(medal.transform, LGIcon.Starbase, 20, Gold);
            _icon.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20));

            _title = LGBuild.Label(_root, "", 13, Color.white, TextAnchor.UpperLeft, bold: true, wrap: true);
            _title.rectTransform.anchorMin = new Vector2(0, 1); _title.rectTransform.anchorMax = new Vector2(1, 1);
            _title.rectTransform.pivot = new Vector2(0, 1);
            _title.rectTransform.offsetMin = new Vector2(62, -78); _title.rectTransform.offsetMax = new Vector2(-12, -38);
            _title.resizeTextForBestFit = true; _title.resizeTextMinSize = 10; _title.resizeTextMaxSize = 13;

            var track = LGBuild.Rect(_root, "Bar");
            track.anchorMin = new Vector2(0, 0); track.anchorMax = new Vector2(1, 0);
            track.pivot = new Vector2(0.5f, 0);
            track.offsetMin = new Vector2(62, 34); track.offsetMax = new Vector2(-64, 40);
            _fill = LGBuild.Bar(track, Gold, 0f, 5f);
            _count = LGBuild.Label(_root, "", 12, Color.white, TextAnchor.MiddleRight, bold: true);
            _count.rectTransform.At(new Vector2(1, 0), new Vector2(1, 0.5f), new Vector2(-12, 37), new Vector2(60, 16));

            _left = LGBuild.Label(_root, "", 11, Muted, TextAnchor.MiddleLeft);
            _left.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(62, 16), new Vector2(120, 16));
            _rewardIcon = LGIcons.Create(_root, LGIcon.Energy, 12, Gold);
            _rewardIcon.rectTransform.At(new Vector2(1, 0), new Vector2(1, 0.5f), new Vector2(-118, 16), new Vector2(12, 12));
            _reward = LGBuild.Label(_root, "", 11, Gold, TextAnchor.MiddleRight, bold: true);
            _reward.rectTransform.At(new Vector2(1, 0), new Vector2(1, 0.5f), new Vector2(-12, 16), new Vector2(104, 16));
        }

        private void Update()
        {
            var obj = EmpireObjectives.Instance;
            float dt = Time.unscaledDeltaTime;
            if (_armed > 0f) { _armed -= dt; if (_armed <= 0f && _abandonLabel != null) _abandonLabel.color = Muted; }

            bool tutorial = TutorialManager.Instance != null && TutorialManager.Instance.IsActive;
            _wanted = UIManager.IsGameStarted && obj != null && (obj.HasActive || obj.HasOffer) && !StarterGoalsPanel.Showing && !tutorial;
            _group.alpha = Mathf.MoveTowards(_group.alpha, _wanted ? 1f : 0f, dt * 4f);
            _group.blocksRaycasts = _wanted;

            float news = NewsFeedPanel.TopEdge;
            var pos = new Vector2(-10f, (news > 0f ? news : 12f) + Gap);
            if ((_root.anchoredPosition - pos).sqrMagnitude > 1f) _root.anchoredPosition = pos;
            Reserve = _group.alpha > 0.01f ? pos.y + H + Gap : 0f;
            if (!StarterGoalsPanel.Showing) OutlinerPanel.BottomReserve = Reserve;

            if (!_wanted) return;
            _refresh -= dt;
            if (_refresh > 0f) return;
            _refresh = 0.5f;
            Refresh(obj);
        }

        private void Refresh(EmpireObjectives obj)
        {
            var d = obj.Data;
            _head.text = d.Completed > 0 ? Loc.F("ЦЕЛЬ ИМПЕРИИ  <color=#8AA2A8>· выполнено {0}</color>", d.Completed) : Loc.T("ЦЕЛЬ ИМПЕРИИ");
            if (!obj.HasActive)
            {
                _icon.sprite = LGIcons.Get(LGIcon.Target);
                _title.text = Loc.T("Империя ждёт решения: выберите цель на 10 лет");
                LGBuild.SetBar(_fill, 0f);
                _count.text = "";
                _left.text = Loc.T("щелкните, чтобы выбрать");
                _reward.text = "";
                _rewardIcon.enabled = false;
                return;
            }
            var t = obj.Active;
            int cur = obj.Current;
            _icon.sprite = LGIcons.Get(t.Icon);
            _title.text = obj.TitleOf(t, d.Target);
            float span = Mathf.Max(1, d.Target - d.Start);
            LGBuild.SetBar(_fill, Mathf.Clamp01((cur - d.Start) / span));
            _count.text = $"{cur} / {d.Target}";
            int days = obj.DaysLeft;
            int years = days / EmpireObjectives.YearDays, months = days % EmpireObjectives.YearDays / 30;
            _left.text = years > 0 ? Loc.F("осталось {0} г. {1} мес.", years, months) : Loc.F("осталось {0} мес.", months);
            _left.color = years == 0 ? UIManager.DS.Red : Muted;
            _rewardIcon.enabled = true;
            _rewardIcon.sprite = LGIcons.Get(EmpireObjectives.RewardIcon(d.RewardRes));
            _reward.text = EmpireObjectives.RewardText(d.RewardRes, d.RewardAmount);
        }

        private void Click()
        {
            var obj = EmpireObjectives.Instance;
            if (obj == null) return;
            SFXManager.Play(Sfx.UiClick, 0.7f);
            if (!obj.HasActive) { EmpireGoalsChoice.Open(); return; }
            var t = obj.Active;
            if (t.Show != null) t.Show();
            NotificationCenter.Show(obj.TitleOf(t, obj.Data.Target), t.Hint, NotificationCenter.Kind.Info, 7f);
        }

        private void AbandonClick()
        {
            var obj = EmpireObjectives.Instance;
            if (obj == null || !obj.HasActive) return;
            if (_armed > 0f) { obj.Abandon(); _armed = 0f; _abandonLabel.color = Muted; SFXManager.Play(Sfx.DealRemove, 0.8f); return; }
            _armed = 3f;
            _abandonLabel.color = UIManager.DS.Red;
            SFXManager.Play(Sfx.UiDenied, 0.6f);
        }
    }

    /// <summary>
    /// Выбор цели империи: три карточки (значок, задача с числом, подсказка, награда). Время на паузе, пока окно открыто.
    /// «Позже» — цели вернутся через год.
    /// </summary>
    public class EmpireGoalsChoice : MonoBehaviour
    {
        public static EmpireGoalsChoice Instance { get; private set; }
        public static bool IsOpenNow => Instance != null && Instance._root != null && LG.IsVisible(Instance._root);

        private static readonly Color Gold = new Color(1f, 0.82f, 0.38f);
        private GameObject _root;
        private RectTransform _rt, _cards;
        private int _speedBefore = -1;

        public static void Open()
        {
            var obj = EmpireObjectives.Instance;
            if (obj == null || !obj.HasOffer) return;
            if (Instance == null) new GameObject("[UI] EmpireGoalsChoice").AddComponent<EmpireGoalsChoice>();
            Instance.Show();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnEnable() => EmpireObjectives.OnOffer += HandleOffer;
        private void OnDisable() => EmpireObjectives.OnOffer -= HandleOffer;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>Создать слушателя заранее (из UIManager), чтобы новое предложение само открыло окно.</summary>
        public static void Ensure()
        {
            if (Instance == null) new GameObject("[UI] EmpireGoalsChoice").AddComponent<EmpireGoalsChoice>();
        }

        private void HandleOffer()
        {
            if (!UIManager.IsGameStarted) return;
            if (TutorialManager.Instance != null && TutorialManager.Instance.IsActive) return;
            Show();
        }

        private void Update()
        {
            if (!IsOpenNow) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { UIEsc.Consume(); Later(); }
        }

        private void Show()
        {
            if (_root == null) Build();
            if (_root == null) return;
            Fill();
            if (!LG.IsVisible(_root))
            {
                var tm = TimeManager.Instance;
                if (tm != null) { _speedBefore = tm.CurrentSpeed; tm.SetSpeed(0); }
                UIManager.Instance?.ShowModalDimPublic();
                SFXManager.Play(Sfx.WindowOpen);
            }
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
        }

        private void Hide()
        {
            if (_root == null || !LG.IsVisible(_root)) return;
            LG.Hide(_root);
            UIManager.Instance?.HideModalDimPublic();
            TooltipSystem.Hide();
            var tm = TimeManager.Instance;
            if (tm != null && _speedBefore > 0) tm.SetSpeed(_speedBefore);
            _speedBefore = -1;
        }

        private void Later()
        {
            EmpireObjectives.Instance?.SkipOffer();
            SFXManager.Play(Sfx.WindowClose);
            Hide();
        }

        private void Build()
        {
            var host = GameObject.Find("ModalCanvas");
            if (host == null) return;
            _rt = LGBuild.Rect(host.transform, "EmpireGoalsChoice");
            _rt.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1060, 520));
            _root = _rt.gameObject;
            _root.AddComponent<CanvasGroup>();
            var bg = _root.AddComponent<Image>();
            bg.color = UIManager.DS.BgDeep;
            bg.raycastTarget = true;
            LG.Glass(_root).SetRim(new Color(Gold.r, Gold.g, Gold.b, 0.5f));
            LG.Motion(_root, LGAppear.Kind.Pop).fromScale = 0.96f;

            var title = LGBuild.Label(_rt, Loc.T("ЦЕЛЬ ИМПЕРИИ"), 26, Color.white, TextAnchor.UpperCenter, bold: true);
            title.rectTransform.TopBand(24, 36, 30, 30);
            title.Track(2f);
            var sub = LGBuild.Label(_rt, Loc.T("Выберите, к чему империя будет стремиться ближайшие 10 лет. За успех — награда; провал ничего не отнимает."),
                                    14, UIManager.DS.TextMuted, TextAnchor.UpperCenter, wrap: true);
            sub.rectTransform.TopBand(66, 40, 60, 60);

            _cards = LGBuild.Rect(_rt, "Cards");
            _cards.anchorMin = Vector2.zero; _cards.anchorMax = Vector2.one;
            _cards.offsetMin = new Vector2(30, 92); _cards.offsetMax = new Vector2(-30, -116);

            var later = LGBuild.Button(_rt, "Later", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.25f), Later, null, Loc.T("ПОЗЖЕ"), 13);
            ((RectTransform)later.transform).At(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(200, 46));
            TooltipHelper.Attach(later.gameObject, Loc.T("Не выбирать сейчас — новые цели предложат через год  <color=#8AA2A8>(Esc)</color>"));
            _root.SetActive(false);
        }

        private void Fill()
        {
            LGBuild.Clear(_cards);
            var obj = EmpireObjectives.Instance;
            if (obj == null) return;
            var ids = obj.Data.Offer;
            int n = Mathf.Max(1, ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                var t = obj.Get(ids[i]);
                if (t == null) continue;
                string id = t.Id;
                int target = obj.PreviewTarget(t);
                var card = LGBuild.Panel(_cards, "Goal", new Color(0.03f, 0.065f, 0.105f, 0.95f), raycast: true);
                var rt = card.rectTransform;
                rt.anchorMin = new Vector2(i / (float)n, 0f); rt.anchorMax = new Vector2((i + 1) / (float)n, 1f);
                rt.offsetMin = new Vector2(8, 0); rt.offsetMax = new Vector2(-8, 0);
                LG.Platter(card.gameObject, 12f).SetRim(new Color(Gold.r, Gold.g, Gold.b, 0.28f));
                LGFx.HoverGlow(card.gameObject);
                var b = card.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => Pick(id));

                var medal = LGBuild.Panel(rt, "Medal", new Color(0.10f, 0.08f, 0.03f, 0.95f));
                medal.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(72, 72));
                LG.Chip(medal.gameObject, new Color(Gold.r, Gold.g, Gold.b, 0.7f), 36f);
                var ic = LGIcons.Create(medal.transform, t.Icon, 34, Gold);
                ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34, 34));

                var tt = LGBuild.Label(rt, obj.TitleOf(t, target), 18, Color.white, TextAnchor.UpperCenter, bold: true, wrap: true);
                tt.rectTransform.TopBand(112, 52, 18, 18);
                var cur = LGBuild.Label(rt, Loc.F("сейчас: {0}", Mathf.Max(0, t.Metric())), 12, Gold, TextAnchor.UpperCenter);
                cur.rectTransform.TopBand(166, 18, 18, 18);
                var hint = LGBuild.Label(rt, t.Hint, 13, UIManager.DS.TextMuted, TextAnchor.UpperCenter, wrap: true);
                hint.rectTransform.TopBand(192, 60, 22, 22);

                var rw = LGBuild.Rect(rt, "Reward");
                rw.anchorMin = new Vector2(0, 0); rw.anchorMax = new Vector2(1, 0); rw.pivot = new Vector2(0.5f, 0);
                rw.offsetMin = new Vector2(18, 16); rw.offsetMax = new Vector2(-18, 44);
                var rwi = LGIcons.Create(rw, EmpireObjectives.RewardIcon(t.RewardRes), 16, Gold);
                rwi.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(1, 0.5f), new Vector2(-50, 0), new Vector2(16, 16));
                var rwt = LGBuild.Label(rw, EmpireObjectives.RewardText(t.RewardRes, obj.RewardAmount(t)), 15, Gold, TextAnchor.MiddleLeft, bold: true);
                rwt.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(-44, 0), new Vector2(160, 24));
            }
        }

        private void Pick(string id)
        {
            EmpireObjectives.Instance?.Choose(id);
            SFXManager.Play(Sfx.DealAccepted, 0.8f);
            Hide();
        }
    }
}
