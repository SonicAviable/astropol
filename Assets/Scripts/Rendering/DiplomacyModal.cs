using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    public class DiplomacyModal : MonoBehaviour
    {
        public static DiplomacyModal Instance { get; private set; }
        public bool IsOpen => _root != null && LG.IsVisible(_root);

        private Canvas _host;
        private GameObject _root;
        private CanvasGroup _group;
        private Font _font;

        private Text _titleText;
        private Text _statusText;
        private Text _relationsText;
        private Image _relationsBar;
        private Text _powerText;
        private Text _incomeText;
        private Button _warBtn;
        private Text _warBtnText;
        private Button _peaceBtn;
        private Text _peaceBtnText;
        private Button _tradeBtn;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _font = GameFont.Regular;
        }

        public void BindHost(Canvas modalCanvas) { _host = modalCanvas; }

        public void Open()
        {
            if (_host == null)
            {
                var modal = GameObject.Find("ModalCanvas");
                if (modal != null) _host = modal.GetComponent<Canvas>();
            }
            if (_host == null) return;
            if (_root == null) Build();

            Refresh();
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
            UIManager.Instance?.ShowModalDimPublic();
            MapModeController.HideGlobal();
        }

        public void Close()
        {
            if (_root == null) return;
            LG.Hide(_root);
            UIManager.Instance?.HideModalDimPublic();
            MapModeController.ShowGlobal();
        }

        private void Refresh()
        {
            var ai = AIEmpireManager.Instance;
            if (ai == null) return;

            _titleText.text = $"ДИПЛОМАТИЧЕСКИЙ КАНАЛ · {ai.AIName.ToUpper()}";

            float rel = ai.RelationsWithPlayer;
            string status;
            Color statusColor;

            if (rel <= -60f)       { status = "ВОЙНА";                statusColor = UIManager.DS.Red; }
            else if (rel <= -20f)  { status = "ХОЛОДНАЯ ВОЙНА";       statusColor = new Color(1f, 0.45f, 0.30f); }
            else if (rel < 10f)    { status = "НЕЙТРАЛИТЕТ";          statusColor = UIManager.DS.TextMuted; }
            else if (rel < 30f)    { status = "ТЁПЛЫЕ ОТНОШЕНИЯ";     statusColor = UIManager.DS.Green; }
            else                   { status = "СОЮЗНИЧЕСКИЙ КУРС";    statusColor = UIManager.DS.NeonCyan; }

            _statusText.text = status;
            _statusText.color = statusColor;

            float normalized = Mathf.InverseLerp(-100f, 40f, rel);
            _relationsBar.rectTransform.anchorMax = new Vector2(normalized, 1f);
            _relationsBar.color = Color.Lerp(UIManager.DS.Red, UIManager.DS.Green, normalized);

            _relationsText.text = $"<color=#8AA2A8>Отношения:</color> <b>{rel:+0;-0;0}</b>  " +
                                  $"<color=#8AA2A8>(−100…+40)</color>";

            int playerPow = FleetManager.Instance != null ? FleetManager.Instance.GetMilitaryPower(0) : 0;
            int aiPow = FleetManager.Instance != null ? FleetManager.Instance.GetMilitaryPower(AIEmpireManager.AIOwnerId) : 0;

            string balance;
            if (playerPow > aiPow * 1.3f) balance = "<color=#4DF08C>ВЫ СИЛЬНЕЕ</color>";
            else if (aiPow > playerPow * 1.3f) balance = "<color=#FF5555>ВРАГ СИЛЬНЕЕ</color>";
            else balance = "<color=#F2C747>ПАРИТЕТ</color>";

            _powerText.text =
                $"<color=#8AA2A8>Ваш флот:</color>  ⚔ {playerPow}\n" +
                $"<color=#8AA2A8>Их флот:</color>   ⚔ {aiPow}\n" +
                $"<color=#8AA2A8>Баланс:</color> {balance}";

            _incomeText.text =
                $"<color=#8AA2A8>Запасы {ai.AIName}:</color>\n" +
                $"⚡ {(int)ai.EnergyCredits}   ◆ {(int)ai.Minerals}\n" +
                $"⬢ {(int)ai.Alloys}   ★ {(int)ai.Influence}\n" +
                $"<color=#8AA2A8>Доход:</color> +{ai.MonthlyAlloysIncome:0.#} ⬢ / мес";

            bool hostile = ai.IsHostileToPlayer;
            _warBtn.interactable = !hostile;
            _warBtnText.text = hostile ? "⚔  УЖЕ В СОСТОЯНИИ ВОЙНЫ" : "⚔  ОБЪЯВИТЬ ВОЙНУ";

            _peaceBtn.interactable = hostile;
            _peaceBtnText.text = hostile ? "☮  ПРЕДЛОЖИТЬ МИР  ·  30 ★" : "✓  МИР УЖЕ ДЕЙСТВУЕТ";

            _tradeBtn.interactable = !hostile;
        }

        private void OnGiftEnergy()
        {
            var eco = EconomyManager.Instance;
            var ai = AIEmpireManager.Instance;
            if (eco == null || ai == null) return;
            if (!eco.TrySpend(100f, 0f, 0f, 0f)) return;

            ai.EnergyCredits += 100f;
            ai.RelationsWithPlayer = Mathf.Clamp(ai.RelationsWithPlayer + 8f, -100f, 40f);
            NotificationCenter.Show("Подарок отправлен", "+100 ⚡  ·  +8 отношений", NotificationCenter.Kind.Success, 4f);
            Refresh();
        }

        private void OnGiftAlloys()
        {
            var eco = EconomyManager.Instance;
            var ai = AIEmpireManager.Instance;
            if (eco == null || ai == null) return;
            if (!eco.TrySpend(0f, 0f, 50f, 0f)) return;

            ai.Alloys += 50f;
            ai.RelationsWithPlayer = Mathf.Clamp(ai.RelationsWithPlayer + 14f, -100f, 40f);
            NotificationCenter.Show("Подарок отправлен", "+50 ⬢  ·  +14 отношений", NotificationCenter.Kind.Success, 4f);
            Refresh();
        }

        private void OnDeclareWar()
        {
            var ai = AIEmpireManager.Instance;
            if (ai == null) return;

            ai.RelationsWithPlayer = -100f;
            NotificationCenter.Show("ВОЙНА ОБЪЯВЛЕНА",
                $"{ai.AIName} переходит в наступление",
                NotificationCenter.Kind.Danger, 8f);
            Refresh();
        }

        private void OnOfferPeace()
        {
            var ai = AIEmpireManager.Instance;
            var eco = EconomyManager.Instance;
            if (ai == null || eco == null) return;
            if (!eco.TrySpend(0f, 0f, 0f, 30f)) return;

            int playerPow = FleetManager.Instance != null ? FleetManager.Instance.GetMilitaryPower(0) : 0;
            int aiPow = FleetManager.Instance != null ? FleetManager.Instance.GetMilitaryPower(AIEmpireManager.AIOwnerId) : 0;

            float acceptChance = Mathf.Clamp01(0.4f + (playerPow - aiPow) / 400f);

            if (UnityEngine.Random.value < acceptChance)
            {
                ai.RelationsWithPlayer = 15f;
                NotificationCenter.Show("Мир заключён",
                    $"{ai.AIName} прекращает военные действия",
                    NotificationCenter.Kind.Success, 8f);
            }
            else
            {
                NotificationCenter.Show("Мир отвергнут",
                    $"{ai.AIName} продолжает агрессию",
                    NotificationCenter.Kind.Danger, 6f);
            }
            Refresh();
        }

        private void OnOpenTrade()
        {
            Close();
            TradeModal.Instance?.Open();
        }

        private void Build()
        {
            _root = new GameObject("DiplomacyModal");
            _root.transform.SetParent(_host.transform, false);

            var rt = _root.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(720, 620);

            _group = _root.AddComponent<CanvasGroup>();
            _root.AddComponent<Image>().color = UIManager.DS.BgDeep;
            LG.Glass(_root).SetRim(LG.Palette.GoldRim);
            LG.Motion(_root, LGAppear.Kind.Pop);

            var header = new GameObject("Header");
            header.transform.SetParent(_root.transform, false);
            var hRt = header.AddComponent<RectTransform>();
            hRt.anchorMin = new Vector2(0, 1);
            hRt.anchorMax = new Vector2(1, 1);
            hRt.pivot = new Vector2(0.5f, 1);
            hRt.sizeDelta = new Vector2(0, 52);
            header.AddComponent<Image>().color = UIManager.DS.BgHeader;
            LG.Header(header);

            _titleText = MakeText(header.transform, "ДИПЛОМАТИЧЕСКИЙ КАНАЛ", 16, FontStyle.Bold,
                UIManager.DS.Gold, TextAnchor.MiddleLeft);
            _titleText.rectTransform.anchorMin = Vector2.zero;
            _titleText.rectTransform.anchorMax = Vector2.one;
            _titleText.rectTransform.offsetMin = new Vector2(20, 0);
            _titleText.rectTransform.offsetMax = new Vector2(-60, 0);

            var closeBtn = MakeBtn(header.transform, "✕", new Vector2(30, 30), new Color(0.16f, 0.20f, 0.24f), Close);
            LG.Button(closeBtn, new Color(1f, 0.45f, 0.48f, 0.55f), 15f);
            var cRt = closeBtn.GetComponent<RectTransform>();
            cRt.anchorMin = cRt.anchorMax = new Vector2(1, 0.5f);
            cRt.pivot = new Vector2(1, 0.5f);
            cRt.anchoredPosition = new Vector2(-14, 0);

            var statusPanel = new GameObject("StatusPanel");
            statusPanel.transform.SetParent(_root.transform, false);
            var spRt = statusPanel.AddComponent<RectTransform>();
            spRt.anchorMin = new Vector2(0.04f, 0.72f);
            spRt.anchorMax = new Vector2(0.96f, 0.92f);
            spRt.offsetMin = spRt.offsetMax = Vector2.zero;
            statusPanel.AddComponent<Image>().color = UIManager.DS.BgSlot;

            _statusText = MakeText(statusPanel.transform, "", 20, FontStyle.Bold,
                UIManager.DS.NeonCyan, TextAnchor.UpperCenter);
            _statusText.rectTransform.anchorMin = new Vector2(0, 0.4f);
            _statusText.rectTransform.anchorMax = new Vector2(1, 1);
            _statusText.rectTransform.offsetMin = new Vector2(10, 0);
            _statusText.rectTransform.offsetMax = new Vector2(-10, -8);

            var barBg = new GameObject("BarBg");
            barBg.transform.SetParent(statusPanel.transform, false);
            var bbRt = barBg.AddComponent<RectTransform>();
            bbRt.anchorMin = new Vector2(0.08f, 0.20f);
            bbRt.anchorMax = new Vector2(0.92f, 0.30f);
            bbRt.offsetMin = bbRt.offsetMax = Vector2.zero;
            barBg.AddComponent<Image>().color = new Color(0.05f, 0.08f, 0.10f, 1f);

            var barFill = new GameObject("BarFill");
            barFill.transform.SetParent(barBg.transform, false);
            var bfRt = barFill.AddComponent<RectTransform>();
            bfRt.anchorMin = new Vector2(0, 0);
            bfRt.anchorMax = new Vector2(0.5f, 1f);
            bfRt.offsetMin = new Vector2(2, 2);
            bfRt.offsetMax = new Vector2(-2, -2);
            _relationsBar = barFill.AddComponent<Image>();
            _relationsBar.color = UIManager.DS.Green;
            LG.Fill(barFill);

            _relationsText = MakeText(statusPanel.transform, "", 11, FontStyle.Normal,
                UIManager.DS.TextPrimary, TextAnchor.LowerCenter);
            _relationsText.rectTransform.anchorMin = new Vector2(0, 0);
            _relationsText.rectTransform.anchorMax = new Vector2(1, 0.18f);
            _relationsText.rectTransform.offsetMin = new Vector2(10, 4);
            _relationsText.rectTransform.offsetMax = new Vector2(-10, 0);

            var powerPanel = new GameObject("PowerPanel");
            powerPanel.transform.SetParent(_root.transform, false);
            var ppRt = powerPanel.AddComponent<RectTransform>();
            ppRt.anchorMin = new Vector2(0.04f, 0.45f);
            ppRt.anchorMax = new Vector2(0.48f, 0.70f);
            ppRt.offsetMin = ppRt.offsetMax = Vector2.zero;
            powerPanel.AddComponent<Image>().color = UIManager.DS.BgSlot;
            var ppOl = powerPanel.AddComponent<Outline>();
            ppOl.effectColor = UIManager.DS.NeonTeal;
            ppOl.effectDistance = new Vector2(0.8f, -0.8f);

            _powerText = MakeText(powerPanel.transform, "", 11, FontStyle.Normal,
                UIManager.DS.TextPrimary, TextAnchor.UpperLeft);
            _powerText.rectTransform.anchorMin = Vector2.zero;
            _powerText.rectTransform.anchorMax = Vector2.one;
            _powerText.rectTransform.offsetMin = new Vector2(14, 10);
            _powerText.rectTransform.offsetMax = new Vector2(-12, -10);
            _powerText.lineSpacing = 1.35f;

            var incomePanel = new GameObject("IncomePanel");
            incomePanel.transform.SetParent(_root.transform, false);
            var ipRt = incomePanel.AddComponent<RectTransform>();
            ipRt.anchorMin = new Vector2(0.52f, 0.45f);
            ipRt.anchorMax = new Vector2(0.96f, 0.70f);
            ipRt.offsetMin = ipRt.offsetMax = Vector2.zero;
            incomePanel.AddComponent<Image>().color = UIManager.DS.BgSlot;
            var ipOl = incomePanel.AddComponent<Outline>();
            ipOl.effectColor = UIManager.DS.NeonTeal;
            ipOl.effectDistance = new Vector2(0.8f, -0.8f);

            _incomeText = MakeText(incomePanel.transform, "", 11, FontStyle.Normal,
                UIManager.DS.TextPrimary, TextAnchor.UpperLeft);
            _incomeText.rectTransform.anchorMin = Vector2.zero;
            _incomeText.rectTransform.anchorMax = Vector2.one;
            _incomeText.rectTransform.offsetMin = new Vector2(14, 10);
            _incomeText.rectTransform.offsetMax = new Vector2(-12, -10);
            _incomeText.lineSpacing = 1.35f;

            float y = -20f;
            MakeActionButton("ПОДАРОК  ·  100 ⚡", UIManager.DS.BtnSuccess, ref y, OnGiftEnergy);
            MakeActionButton("ПОДАРОК  ·  50 ⬢",  new Color(0.10f, 0.30f, 0.38f), ref y, OnGiftAlloys);

            _warBtn = MakeActionButton("⚔  ОБЪЯВИТЬ ВОЙНУ", UIManager.DS.BtnDanger, ref y, OnDeclareWar);
            _warBtnText = _warBtn.GetComponentInChildren<Text>();

            _peaceBtn = MakeActionButton("☮  ПРЕДЛОЖИТЬ МИР", new Color(0.14f, 0.38f, 0.32f), ref y, OnOfferPeace);
            _peaceBtnText = _peaceBtn.GetComponentInChildren<Text>();

            _tradeBtn = MakeActionButton("◆  ТОРГОВЫЙ КАНАЛ", UIManager.DS.BtnPrimary, ref y, OnOpenTrade);

            LG.Skin(_root.transform);
            _root.SetActive(false);
        }

        private Button MakeActionButton(string label, Color bg, ref float yOffset, System.Action onClick)
        {
            var go = new GameObject("ActionBtn");
            go.transform.SetParent(_root.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0);
            rt.anchorMax = new Vector2(0.5f, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.sizeDelta = new Vector2(420, 36);
            rt.anchoredPosition = new Vector2(0, 190 + yOffset);

            go.AddComponent<Image>().color = bg;
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());
            LG.Button(go);   // кромка — светлый оттенок цвета кнопки

            var txt = MakeText(go.transform, label, 11, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            txt.rectTransform.anchorMin = Vector2.zero;
            txt.rectTransform.anchorMax = Vector2.one;
            txt.rectTransform.offsetMin = Vector2.zero;
            txt.rectTransform.offsetMax = Vector2.zero;

            yOffset -= 42f;
            return btn;
        }

        private GameObject MakeBtn(Transform parent, string label, Vector2 size, Color bg, System.Action click)
        {
            var go = new GameObject("Btn");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = size;
            go.AddComponent<Image>().color = bg;
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => click?.Invoke());
            var txt = MakeText(go.transform, label, 12, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            txt.rectTransform.anchorMin = Vector2.zero;
            txt.rectTransform.anchorMax = Vector2.one;
            txt.rectTransform.offsetMin = txt.rectTransform.offsetMax = Vector2.zero;
            return go;
        }

        private Text MakeText(Transform parent, string val, int size, FontStyle style, Color col, TextAnchor anchor)
        {
            var go = new GameObject("Txt");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = (style == FontStyle.Bold) ? GameFont.Bold : GameFont.Regular;
            t.text = val;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = col;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

    }
}