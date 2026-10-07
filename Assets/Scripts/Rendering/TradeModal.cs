using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    public class TradeModal : MonoBehaviour
    {
        public static TradeModal Instance { get; private set; }
        public bool IsOpen => _root != null && LG.IsVisible(_root);

        private const float ValueEnergy    = 1.0f;
        private const float ValueMinerals  = 1.2f;
        private const float ValueAlloys    = 3.0f;
        private const float ValueInfluence = 5.0f;

        private Canvas _host;
        private GameObject _root;
        private CanvasGroup _group;
        private RectTransform _rootRt;

        private int _giveEnergy, _giveMinerals, _giveAlloys, _giveInfluence;
        private int _getEnergy, _getMinerals, _getAlloys, _getInfluence;

        private Text _titleText;
        private Text _giveEnergyTxt, _giveMineralsTxt, _giveAlloysTxt, _giveInfluenceTxt;
        private Text _getEnergyTxt, _getMineralsTxt, _getAlloysTxt, _getInfluenceTxt;
        private Text _fairnessText;
        private Image _fairnessBar;
        private Text _aiStockText;
        private Text _statusText;
        private Image _statusBoxBg;
        private Image _fairnessBoxBg;

        private float _fairnessTarget = 0f;
        private float _fairnessCurrent = 0f;
        private Color _fairnessTargetColor = Color.white;

        private float _flashGiveE, _flashGiveM, _flashGiveA, _flashGiveI;
        private float _flashGetE,  _flashGetM,  _flashGetA,  _flashGetI;

        private float _statusTimer;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void BindHost(Canvas modalCanvas) { _host = modalCanvas; }

        private int _partnerOwner = -1;

        /// <summary>Торговый партнёр — выбранная империя ИИ (по умолчанию первая).</summary>
        private AIEmpireManager Partner => AIEmpireManager.For(_partnerOwner) ?? AIEmpireManager.Instance;

        public void Open() => Open(-1);

        public void Open(int partnerOwner)
        {
            _partnerOwner = partnerOwner;
            if (_host == null)
            {
                var modal = GameObject.Find("ModalCanvas");
                if (modal != null) _host = modal.GetComponent<Canvas>();
            }
            if (_host == null) return;
            if (_root == null) Build();

            var ai = Partner;
            if (_titleText != null && ai != null)
                _titleText.text = $"◆  ТОРГОВЫЙ КАНАЛ · {ai.AIName.ToUpper()}";

            ResetOffer();
            Refresh();
            _fairnessCurrent = _fairnessTarget;

            StopAllCoroutines();
            _rootRt.localScale = Vector3.one;
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
            UIManager.Instance?.ShowModalDimPublic();
            MapModeController.HideGlobal();
        }

        public void Close()
        {
            if (_root == null) return;
            StopAllCoroutines();
            LG.Hide(_root);
            UIManager.Instance?.HideModalDimPublic();
            MapModeController.ShowGlobal();
        }

        private void ResetOffer()
        {
            _giveEnergy = _giveMinerals = _giveAlloys = _giveInfluence = 0;
            _getEnergy  = _getMinerals  = _getAlloys  = _getInfluence  = 0;
            if (_statusText != null) _statusText.text = "";
        }

        private float OfferValue()
            => _giveEnergy * ValueEnergy
             + _giveMinerals * ValueMinerals
             + _giveAlloys * ValueAlloys
             + _giveInfluence * ValueInfluence;

        private float RequestValue()
            => _getEnergy * ValueEnergy
             + _getMinerals * ValueMinerals
             + _getAlloys * ValueAlloys
             + _getInfluence * ValueInfluence;

        private void Update()
        {
            if (_root == null || !_root.activeSelf) return;

            _fairnessCurrent = Mathf.MoveTowards(_fairnessCurrent, _fairnessTarget, Time.unscaledDeltaTime * 3f);
            if (_fairnessBar != null)
            {
                _fairnessBar.rectTransform.anchorMax = new Vector2(_fairnessCurrent, 1f);
                _fairnessBar.color = Color.Lerp(_fairnessBar.color, _fairnessTargetColor, Time.unscaledDeltaTime * 8f);
            }

            if (_fairnessBoxBg != null && _fairnessTarget <= 0.55f && _fairnessTarget > 0.01f)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
                var c = new Color(0.14f, 0.06f, 0.08f, 0.85f);
                var c2 = new Color(0.24f, 0.05f, 0.08f, 0.95f);
                _fairnessBoxBg.color = Color.Lerp(c, c2, pulse);
            }
            else if (_fairnessBoxBg != null)
            {
                _fairnessBoxBg.color = Color.Lerp(_fairnessBoxBg.color,
                    UIManager.DS.BgSlot, Time.unscaledDeltaTime * 6f);
            }

            UpdateFlash(_giveEnergyTxt, ref _flashGiveE);
            UpdateFlash(_giveMineralsTxt, ref _flashGiveM);
            UpdateFlash(_giveAlloysTxt, ref _flashGiveA);
            UpdateFlash(_giveInfluenceTxt, ref _flashGiveI);
            UpdateFlash(_getEnergyTxt, ref _flashGetE);
            UpdateFlash(_getMineralsTxt, ref _flashGetM);
            UpdateFlash(_getAlloysTxt, ref _flashGetA);
            UpdateFlash(_getInfluenceTxt, ref _flashGetI);

            if (_statusTimer > 0f)
            {
                _statusTimer -= Time.unscaledDeltaTime;
                if (_statusText != null)
                {
                    float a = Mathf.Clamp01(_statusTimer / 1.5f);
                    var c = _statusText.color;
                    c.a = a;
                    _statusText.color = c;
                }
            }
        }

        private void UpdateFlash(Text t, ref float timer)
        {
            if (t == null) return;
            if (timer > 0f)
            {
                timer -= Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(timer / 0.35f);
                float scale = 1f + k * 0.35f;
                t.rectTransform.localScale = Vector3.one * scale;
                var c = t.color;
                c.a = Mathf.Lerp(0.4f, 1f, 1f - k);
                t.color = c;
            }
            else if (t.rectTransform.localScale != Vector3.one)
            {
                t.rectTransform.localScale = Vector3.one;
                var c = t.color;
                c.a = 1f;
                t.color = c;
            }
        }

        private void Flash(ref float timer) { timer = 0.35f; }

        private void Refresh()
        {
            var eco = EconomyManager.Instance;
            var ai = Partner;
            if (eco == null || ai == null) return;

            _giveEnergyTxt.text    = $"{_giveEnergy} / {(int)eco.EnergyCredits}";
            _giveMineralsTxt.text  = $"{_giveMinerals} / {(int)eco.Minerals}";
            _giveAlloysTxt.text    = $"{_giveAlloys} / {(int)eco.Alloys}";
            _giveInfluenceTxt.text = $"{_giveInfluence} / {(int)eco.Influence}";

            _getEnergyTxt.text    = $"{_getEnergy} / {(int)ai.EnergyCredits}";
            _getMineralsTxt.text  = $"{_getMinerals} / {(int)ai.Minerals}";
            _getAlloysTxt.text    = $"{_getAlloys} / {(int)ai.Alloys}";
            _getInfluenceTxt.text = $"{_getInfluence} / {(int)ai.Influence}";

            _aiStockText.text =
                $"<color=#8AA2A8>Запасы {ai.AIName}:</color>  " +
                $"<color=#F2C747>⚡{(int)ai.EnergyCredits}</color>  " +
                $"<color=#33E6CC>◆{(int)ai.Minerals}</color>  " +
                $"<color=#E59A55>⬢{(int)ai.Alloys}</color>  " +
                $"<color=#FF7A8A>★{(int)ai.Influence}</color>";

            float offerV = OfferValue();
            float requestV = RequestValue();

            float acceptRatio = AcceptRatio(ai);
            float threshold = requestV * acceptRatio;

            if (requestV <= 0.01f)
                _fairnessTarget = offerV > 0.01f ? 1f : 0f;
            else
                _fairnessTarget = Mathf.Clamp01(offerV / requestV);

            if (requestV <= 0.01f && offerV <= 0.01f)
            {
                _fairnessText.text = "<color=#8AA2A8>Задайте условия сделки</color>";
                _fairnessTargetColor = UIManager.DS.TextMuted;
            }
            else if (offerV >= threshold)
            {
                float profit = offerV - requestV;
                _fairnessText.text = $"<color=#4DF08C>Сделка принята · выгода AI {profit:+0;-0;0}</color>";
                _fairnessTargetColor = UIManager.DS.Green;
            }
            else
            {
                float need = threshold - offerV;
                _fairnessText.text = $"<color=#FF8888>AI откажет · нужно +{need:0} ценности</color>";
                _fairnessTargetColor = UIManager.DS.Red;
            }
        }

        /// <summary>Какую долю запрошенного нужно предложить: зависит от отношения и характера ИИ.</summary>
        private static float AcceptRatio(AIEmpireManager ai)
        {
            float ratio = Mathf.Clamp(0.70f + ai.Opinion / 200f, 0.55f, 1.05f);
            if (ai.Personality == AIPersonality.Trader) ratio *= 0.92f;
            return ratio;
        }

        private void ChangeGive(ref int field, int delta, float available, ref float flashTimer)
        {
            int old = field;
            field = Mathf.Clamp(field + delta, 0, Mathf.FloorToInt(available));
            if (old != field) Flash(ref flashTimer);
            Refresh();
        }

        private void ChangeGet(ref int field, int delta, float available, ref float flashTimer)
        {
            int old = field;
            field = Mathf.Clamp(field + delta, 0, Mathf.FloorToInt(available));
            if (old != field) Flash(ref flashTimer);
            Refresh();
        }

        private void ShowStatus(string msg, Color color)
        {
            _statusText.text = msg;
            _statusText.color = color;
            _statusTimer = 1.5f;

            if (_statusBoxBg != null)
                StartCoroutine(FlashBg(_statusBoxBg, color, 0.35f));
        }

        private IEnumerator FlashBg(Image img, Color flash, float dur)
        {
            Color baseCol = UIManager.DS.BgVisor;
            img.color = new Color(flash.r, flash.g, flash.b, 0.55f);
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                img.color = Color.Lerp(new Color(flash.r, flash.g, flash.b, 0.55f), baseCol, t / dur);
                yield return null;
            }
            img.color = baseCol;
        }

        private void TryPropose()
        {
            var eco = EconomyManager.Instance;
            var ai = Partner;
            if (eco == null || ai == null) return;

            float offerV = OfferValue();
            float requestV = RequestValue();

            if (offerV <= 0.01f && requestV <= 0.01f)
            {
                ShowStatus("Пустая сделка.", UIManager.DS.NeonCyan);
                return;
            }
            if (_giveEnergy    > eco.EnergyCredits ||
                _giveMinerals  > eco.Minerals      ||
                _giveAlloys    > eco.Alloys        ||
                _giveInfluence > eco.Influence)
            {
                ShowStatus("У вас не хватает ресурсов.", UIManager.DS.Red);
                return;
            }
            if (_getEnergy    > ai.EnergyCredits ||
                _getMinerals  > ai.Minerals      ||
                _getAlloys    > ai.Alloys        ||
                _getInfluence > ai.Influence)
            {
                ShowStatus("У AI не хватает ресурсов.", UIManager.DS.Red);
                return;
            }

            if (ai.AtWar)
            {
                ShowStatus("Во время войны торговля невозможна.", UIManager.DS.Red);
                return;
            }
            float acceptRatio = AcceptRatio(ai);
            bool accepted = offerV >= requestV * acceptRatio;

            if (!accepted)
            {
                ShowStatus("Предложение отвергнуто.", UIManager.DS.Red);
                StartCoroutine(ShakeRoot(0.25f, 8f));
                ai.RegisterTrade(-1f);
                Refresh();
                return;
            }

            eco.TrySpend(_giveEnergy, _giveMinerals, _giveAlloys, _giveInfluence);
            eco.EnergyCredits += _getEnergy;
            eco.Minerals += _getMinerals;
            eco.Alloys += _getAlloys;
            eco.Influence += _getInfluence;
            eco.RaiseResourcesChanged();

            ai.SpendStock("energy",    _getEnergy);
            ai.SpendStock("minerals",  _getMinerals);
            ai.SpendStock("alloys",    _getAlloys);
            ai.SpendStock("influence", _getInfluence);
            ai.AddStock("energy",    _giveEnergy);
            ai.AddStock("minerals",  _giveMinerals);
            ai.AddStock("alloys",    _giveAlloys);
            ai.AddStock("influence", _giveInfluence);

            // Выгодная для ИИ сделка улучшает отношение (торговая фракция ценит это сильнее)
            ai.RegisterTrade(1.5f + (offerV - requestV) / 60f);

            NotificationCenter.Show("Сделка заключена",
                $"Отдано: ⚡{_giveEnergy} ◆{_giveMinerals} ⬢{_giveAlloys} ★{_giveInfluence}   ·   " +
                $"Получено: ⚡{_getEnergy} ◆{_getMinerals} ⬢{_getAlloys} ★{_getInfluence}",
                NotificationCenter.Kind.Success, 5f);

            ResetOffer();
            Refresh();
            ShowStatus("Сделка исполнена.", UIManager.DS.Green);
            StartCoroutine(SwapPulse());
        }

        private IEnumerator SwapPulse()
        {
            if (_rootRt == null) yield break;
            float t = 0f;
            const float dur = 0.35f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = t / dur;
                float pulse = 1f + Mathf.Sin(k * Mathf.PI) * 0.03f;
                _rootRt.localScale = Vector3.one * pulse;
                yield return null;
            }
            _rootRt.localScale = Vector3.one;
        }

        private IEnumerator ShakeRoot(float dur, float amp)
        {
            if (_rootRt == null) yield break;
            Vector2 basePos = _rootRt.anchoredPosition;
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = 1f - t / dur;
                float x = Mathf.Sin(t * 60f) * amp * k;
                _rootRt.anchoredPosition = basePos + new Vector2(x, 0);
                yield return null;
            }
            _rootRt.anchoredPosition = basePos;
        }

        // ==================== ПОСТРОЕНИЕ ====================

        private void Build()
        {
            _root = new GameObject("TradeModal");
            _root.transform.SetParent(_host.transform, false);

            _rootRt = _root.AddComponent<RectTransform>();
            _rootRt.anchorMin = _rootRt.anchorMax = new Vector2(0.5f, 0.5f);
            _rootRt.pivot = new Vector2(0.5f, 0.5f);
            _rootRt.sizeDelta = new Vector2(840, 580);

            _group = _root.AddComponent<CanvasGroup>();
            _root.AddComponent<Image>().color = UIManager.DS.BgDeep;
            LG.Glass(_root).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.42f));
            LG.Motion(_root, LGAppear.Kind.Pop);

            // === HEADER ===
            var header = new GameObject("Header");
            header.transform.SetParent(_root.transform, false);
            var hRt = header.AddComponent<RectTransform>();
            hRt.anchorMin = new Vector2(0, 1);
            hRt.anchorMax = new Vector2(1, 1);
            hRt.pivot = new Vector2(0.5f, 1);
            hRt.anchoredPosition = Vector2.zero;
            hRt.sizeDelta = new Vector2(0, 50);
            header.AddComponent<Image>().color = UIManager.DS.BgHeader;
            LG.Header(header);

            // Светящаяся линия под шапкой
            var headerAccent = new GameObject("HeaderAccent");
            headerAccent.transform.SetParent(header.transform, false);
            var haRt = headerAccent.AddComponent<RectTransform>();
            haRt.anchorMin = new Vector2(0, 0);
            haRt.anchorMax = new Vector2(1, 0);
            haRt.pivot = new Vector2(0.5f, 0.5f);
            haRt.sizeDelta = new Vector2(-60, 1.5f);
            haRt.anchoredPosition = Vector2.zero;
            headerAccent.AddComponent<Image>().color = UIManager.DS.NeonCyan;
            LG.Line(headerAccent, hairline: true);

            _titleText = MakeText(header.transform, "◆  ТОРГОВЫЙ КАНАЛ", 15, FontStyle.Bold,
                UIManager.DS.NeonCyan, TextAnchor.MiddleLeft);
            _titleText.rectTransform.anchorMin = Vector2.zero;
            _titleText.rectTransform.anchorMax = Vector2.one;
            _titleText.rectTransform.offsetMin = new Vector2(20, 0);
            _titleText.rectTransform.offsetMax = new Vector2(-60, 0);

            var closeBtn = MakeBtn(header.transform, "✕", new Vector2(30, 30), new Color(0.16f, 0.20f, 0.24f), Close);
            LG.Button(closeBtn, new Color(1f, 0.45f, 0.48f, 0.55f), 15f);
            var cRt = closeBtn.GetComponent<RectTransform>();
            cRt.anchorMin = cRt.anchorMax = new Vector2(1, 0.5f);
            cRt.pivot = new Vector2(1, 0.5f);
            cRt.anchoredPosition = new Vector2(-10, 0);

            // === КОЛОНКИ ===
            BuildColumn("ВЫ ОТДАЁТЕ",
                new Vector2(0.02f, 0.34f), new Vector2(0.49f, 0.90f),
                UIManager.DS.NeonCyan, isGive: true);
            BuildColumn("ВЫ ПОЛУЧАЕТЕ",
                new Vector2(0.51f, 0.34f), new Vector2(0.98f, 0.90f),
                UIManager.DS.Green, isGive: false);

            // === FAIRNESS ===
            var fairBox = new GameObject("Fairness");
            fairBox.transform.SetParent(_root.transform, false);
            var fRt = fairBox.AddComponent<RectTransform>();
            fRt.anchorMin = new Vector2(0.02f, 0.22f);
            fRt.anchorMax = new Vector2(0.98f, 0.32f);
            fRt.offsetMin = fRt.offsetMax = Vector2.zero;
            _fairnessBoxBg = fairBox.AddComponent<Image>();
            _fairnessBoxBg.color = UIManager.DS.BgSlot;
            var fOl = fairBox.AddComponent<Outline>();
            fOl.effectColor = UIManager.DS.NeonTeal;
            fOl.effectDistance = new Vector2(0.8f, -0.8f);

            _fairnessText = MakeText(fairBox.transform, "", 12, FontStyle.Bold,
                UIManager.DS.TextPrimary, TextAnchor.UpperCenter);
            _fairnessText.rectTransform.anchorMin = new Vector2(0, 0.48f);
            _fairnessText.rectTransform.anchorMax = new Vector2(1, 1);
            _fairnessText.rectTransform.offsetMin = new Vector2(10, 0);
            _fairnessText.rectTransform.offsetMax = new Vector2(-10, -4);

            var track = new GameObject("Track");
            track.transform.SetParent(fairBox.transform, false);
            var trRt = track.AddComponent<RectTransform>();
            trRt.anchorMin = new Vector2(0.02f, 0.14f);
            trRt.anchorMax = new Vector2(0.98f, 0.38f);
            trRt.offsetMin = trRt.offsetMax = Vector2.zero;
            track.AddComponent<Image>().color = new Color(0.01f, 0.03f, 0.05f, 1f);

            var fill = new GameObject("Fill");
            fill.transform.SetParent(track.transform, false);
            var ffRt = fill.AddComponent<RectTransform>();
            ffRt.anchorMin = new Vector2(0, 0);
            ffRt.anchorMax = new Vector2(0f, 1f);
            ffRt.offsetMin = Vector2.zero;
            ffRt.offsetMax = Vector2.zero;
            _fairnessBar = fill.AddComponent<Image>();
            _fairnessBar.color = UIManager.DS.TextMuted;
            LG.Fill(fill);

            // === STOCK + STATUS ===
            var stockBox = new GameObject("StockBox");
            stockBox.transform.SetParent(_root.transform, false);
            var spRt = stockBox.AddComponent<RectTransform>();
            spRt.anchorMin = new Vector2(0.02f, 0.10f);
            spRt.anchorMax = new Vector2(0.62f, 0.20f);
            spRt.offsetMin = spRt.offsetMax = Vector2.zero;
            stockBox.AddComponent<Image>().color = UIManager.DS.BgVisor;
            var stockOl = stockBox.AddComponent<Outline>();
            stockOl.effectColor = UIManager.DS.NeonTeal;
            stockOl.effectDistance = new Vector2(0.8f, -0.8f);

            _aiStockText = MakeText(stockBox.transform, "", 11, FontStyle.Normal,
                UIManager.DS.TextPrimary, TextAnchor.MiddleLeft);
            _aiStockText.rectTransform.anchorMin = Vector2.zero;
            _aiStockText.rectTransform.anchorMax = Vector2.one;
            _aiStockText.rectTransform.offsetMin = new Vector2(14, 0);
            _aiStockText.rectTransform.offsetMax = new Vector2(-10, 0);

            var statusBox = new GameObject("StatusBox");
            statusBox.transform.SetParent(_root.transform, false);
            var stBoxRt = statusBox.AddComponent<RectTransform>();
            stBoxRt.anchorMin = new Vector2(0.64f, 0.10f);
            stBoxRt.anchorMax = new Vector2(0.98f, 0.20f);
            stBoxRt.offsetMin = stBoxRt.offsetMax = Vector2.zero;
            _statusBoxBg = statusBox.AddComponent<Image>();
            _statusBoxBg.color = UIManager.DS.BgVisor;
            var stOl = statusBox.AddComponent<Outline>();
            stOl.effectColor = UIManager.DS.NeonTeal;
            stOl.effectDistance = new Vector2(0.8f, -0.8f);

            _statusText = MakeText(statusBox.transform, "", 11, FontStyle.Bold,
                UIManager.DS.NeonCyan, TextAnchor.MiddleCenter);
            _statusText.rectTransform.anchorMin = Vector2.zero;
            _statusText.rectTransform.anchorMax = Vector2.one;
            _statusText.rectTransform.offsetMin = new Vector2(8, 0);
            _statusText.rectTransform.offsetMax = new Vector2(-8, 0);

            // === PROPOSE ===
            var propose = MakeBtn(_root.transform, "◆  ПРЕДЛОЖИТЬ СДЕЛКУ", new Vector2(320, 40),
                UIManager.DS.BtnSuccess, TryPropose);
            var pRt = propose.GetComponent<RectTransform>();
            pRt.anchorMin = new Vector2(0.5f, 0);
            pRt.anchorMax = new Vector2(0.5f, 0);
            pRt.pivot = new Vector2(0.5f, 0);
            pRt.anchoredPosition = new Vector2(0, 14);

            LG.Skin(_root.transform);
            _root.SetActive(false);
        }

        private void BuildColumn(string title, Vector2 amin, Vector2 amax, Color accent, bool isGive)
        {
            var col = new GameObject($"Col_{title}");
            col.transform.SetParent(_root.transform, false);
            var rt = col.AddComponent<RectTransform>();
            rt.anchorMin = amin;
            rt.anchorMax = amax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            col.AddComponent<Image>().color = UIManager.DS.BgSlot;
            var ol = col.AddComponent<Outline>();
            ol.effectColor = accent;
            ol.effectDistance = new Vector2(1f, -1f);

            var vlg = col.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.spacing = 3f;
            vlg.padding = new RectOffset(10, 10, 8, 8);

            var headerGo = new GameObject("ColHeader");
            headerGo.transform.SetParent(col.transform, false);
            var hh = headerGo.AddComponent<LayoutElement>();
            hh.preferredHeight = 28f;
            hh.minHeight = 28f;
            headerGo.AddComponent<Image>().color =
                new Color(accent.r * 0.30f, accent.g * 0.30f, accent.b * 0.30f, 0.75f);

            var hhTxt = MakeText(headerGo.transform, title, 12, FontStyle.Bold, accent, TextAnchor.MiddleCenter);
            hhTxt.rectTransform.anchorMin = Vector2.zero;
            hhTxt.rectTransform.anchorMax = Vector2.one;
            hhTxt.rectTransform.offsetMin = Vector2.zero;
            hhTxt.rectTransform.offsetMax = Vector2.zero;

            if (isGive)
            {
                BuildRow(col.transform, "⚡", "Энергия",  UIManager.DS.Gold,     ref _giveEnergyTxt,
                    () => ChangeGive(ref _giveEnergy, -25, EconomyManager.Instance.EnergyCredits, ref _flashGiveE),
                    () => ChangeGive(ref _giveEnergy,  25, EconomyManager.Instance.EnergyCredits, ref _flashGiveE));
                BuildRow(col.transform, "◆", "Титан",    UIManager.DS.NeonCyan, ref _giveMineralsTxt,
                    () => ChangeGive(ref _giveMinerals, -25, EconomyManager.Instance.Minerals, ref _flashGiveM),
                    () => ChangeGive(ref _giveMinerals,  25, EconomyManager.Instance.Minerals, ref _flashGiveM));
                BuildRow(col.transform, "⬢", "Сплавы",   UIManager.DS.Gold,     ref _giveAlloysTxt,
                    () => ChangeGive(ref _giveAlloys, -25, EconomyManager.Instance.Alloys, ref _flashGiveA),
                    () => ChangeGive(ref _giveAlloys,  25, EconomyManager.Instance.Alloys, ref _flashGiveA));
                BuildRow(col.transform, "★", "Влияние",  UIManager.DS.Red,      ref _giveInfluenceTxt,
                    () => ChangeGive(ref _giveInfluence, -10, EconomyManager.Instance.Influence, ref _flashGiveI),
                    () => ChangeGive(ref _giveInfluence,  10, EconomyManager.Instance.Influence, ref _flashGiveI));
            }
            else
            {
                BuildRow(col.transform, "⚡", "Энергия",  UIManager.DS.Gold,     ref _getEnergyTxt,
                    () => ChangeGet(ref _getEnergy, -25, Partner.EnergyCredits, ref _flashGetE),
                    () => ChangeGet(ref _getEnergy,  25, Partner.EnergyCredits, ref _flashGetE));
                BuildRow(col.transform, "◆", "Титан",    UIManager.DS.NeonCyan, ref _getMineralsTxt,
                    () => ChangeGet(ref _getMinerals, -25, Partner.Minerals, ref _flashGetM),
                    () => ChangeGet(ref _getMinerals,  25, Partner.Minerals, ref _flashGetM));
                BuildRow(col.transform, "⬢", "Сплавы",   UIManager.DS.Gold,     ref _getAlloysTxt,
                    () => ChangeGet(ref _getAlloys, -25, Partner.Alloys, ref _flashGetA),
                    () => ChangeGet(ref _getAlloys,  25, Partner.Alloys, ref _flashGetA));
                BuildRow(col.transform, "★", "Влияние",  UIManager.DS.Red,      ref _getInfluenceTxt,
                    () => ChangeGet(ref _getInfluence, -10, Partner.Influence, ref _flashGetI),
                    () => ChangeGet(ref _getInfluence,  10, Partner.Influence, ref _flashGetI));
            }
        }

        private void BuildRow(Transform parent, string icon, string label, Color accent,
                              ref Text valueText, System.Action onMinus, System.Action onPlus)
        {
            var row = new GameObject($"Row_{label}");
            row.transform.SetParent(parent, false);
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 42f;
            le.minHeight = 42f;

            row.AddComponent<Image>().color = new Color(0.03f, 0.07f, 0.09f, 0.85f);

            // Иконка слева
            var iconTxt = MakeText(row.transform, icon, 18, FontStyle.Bold, accent, TextAnchor.MiddleCenter);
            iconTxt.rectTransform.anchorMin = new Vector2(0, 0);
            iconTxt.rectTransform.anchorMax = new Vector2(0, 1);
            iconTxt.rectTransform.pivot = new Vector2(0, 0.5f);
            iconTxt.rectTransform.sizeDelta = new Vector2(34, 0);
            iconTxt.rectTransform.anchoredPosition = new Vector2(6, 0);

            // Название
            var lblTxt = MakeText(row.transform, label, 12, FontStyle.Bold, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft);
            lblTxt.rectTransform.anchorMin = new Vector2(0, 0);
            lblTxt.rectTransform.anchorMax = new Vector2(0.45f, 1);
            lblTxt.rectTransform.offsetMin = new Vector2(44, 0);
            lblTxt.rectTransform.offsetMax = Vector2.zero;

            // ЧИСЛО — сужено, чтобы не заходить на кнопки.
            // Кнопки: [−] позиция -46, ширина 36 → левый край -82
            //          [+] позиция -6,  ширина 36 → правый край -6
            // Значение оканчиваем на -100 → зазор 18px до кнопки.
            var valTxt = MakeText(row.transform, "0 / 0", 12, FontStyle.Bold, accent, TextAnchor.MiddleRight);
            valTxt.rectTransform.anchorMin = new Vector2(0.45f, 0);
            valTxt.rectTransform.anchorMax = new Vector2(1f, 1);
            valTxt.rectTransform.offsetMin = Vector2.zero;
            valTxt.rectTransform.offsetMax = new Vector2(-100, 0);
            valTxt.rectTransform.pivot = new Vector2(1f, 0.5f);
            valueText = valTxt;

            // Кнопки — фиксированные позиции у правого края
            var minus = MakeSmallBtn(row.transform, "−", new Vector2(36, 28), UIManager.DS.BtnDanger, onMinus);
            var mRt = minus.GetComponent<RectTransform>();
            mRt.anchorMin = mRt.anchorMax = new Vector2(1, 0.5f);
            mRt.pivot = new Vector2(1, 0.5f);
            mRt.anchoredPosition = new Vector2(-46, 0);

            var plus = MakeSmallBtn(row.transform, "+", new Vector2(36, 28), UIManager.DS.BtnSuccess, onPlus);
            var pRt = plus.GetComponent<RectTransform>();
            pRt.anchorMin = pRt.anchorMax = new Vector2(1, 0.5f);
            pRt.pivot = new Vector2(1, 0.5f);
            pRt.anchoredPosition = new Vector2(-6, 0);
        }

        private GameObject MakeSmallBtn(Transform parent, string label, Vector2 size, Color bg, System.Action click)
        {
            var go = new GameObject("SmallBtn");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = size;
            go.AddComponent<Image>().color = bg;
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => click?.Invoke());
            var it = LG.Button(go).GetComponent<LGInteractive>();
            it.pressScale = 0.9f;   // короткий "щелчок" для частых нажатий ±
            var t = MakeText(go.transform, label, 15, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
            return go;
        }

        private GameObject MakeBtn(Transform parent, string label, Vector2 size, Color bg, System.Action onClick)
        {
            var go = new GameObject("Btn");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = size;
            go.AddComponent<Image>().color = bg;
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());
            LG.Button(go);
            var t = MakeText(go.transform, label, 12, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
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
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

    }

    public class ButtonPunch : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler, UnityEngine.EventSystems.IPointerUpHandler
    {
        private Vector3 _base = Vector3.one;
        private float _timer;

        private void Start() { _base = transform.localScale; }

        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e)
        {
            transform.localScale = _base * 0.94f;
        }

        public void OnPointerUp(UnityEngine.EventSystems.PointerEventData e)
        {
            _timer = 0.12f;
        }

        private void Update()
        {
            if (_timer > 0f)
            {
                _timer -= Time.unscaledDeltaTime;
                float k = 1f - Mathf.Clamp01(_timer / 0.12f);
                transform.localScale = Vector3.Lerp(_base * 0.94f, _base * 1.04f, k);
                if (_timer <= 0f) transform.localScale = _base;
            }
            else if (transform.localScale != _base && _timer <= 0f)
            {
                transform.localScale = Vector3.Lerp(transform.localScale, _base, Time.unscaledDeltaTime * 14f);
                if (Mathf.Abs(transform.localScale.x - _base.x) < 0.005f)
                    transform.localScale = _base;
            }
        }
    }
}