using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Модальное окно подтверждения выбора технологии.
    /// Показывает: название, категорию, ЭФФЕКТ (что даёт), ОПИСАНИЕ (флавор), стоимость.
    /// Эффект автоматически колоризуется: плюсы — зелёным, минусы — красным.
    /// </summary>
    public class TechConfirmDialog : MonoBehaviour
    {
        public static TechConfirmDialog Instance { get; private set; }
        public bool IsOpen => _root != null && LG.IsVisible(_root);

        private Canvas _host;
        private Font _font;
        private GameObject _root;
        private CanvasGroup _group;

        private Text _titleText;
        private Text _categoryText;
        private Text _effectText;
        private Text _flavorText;
        private Text _costText;

        private Technology _pendingTech;
        private Action _onConfirm;
        private LiquidGlassEffect _fx;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void BindHost(Canvas modalCanvas)
        {
            _host = modalCanvas;
            _font = GameFont.Regular;
        }

        public void Open(Technology tech, Action onConfirm)
        {
            if (tech == null) return;
            if (_host == null)
            {
                var modal = GameObject.Find("ModalCanvas");
                if (modal != null) _host = modal.GetComponent<Canvas>();
            }
            if (_host == null) return;

            if (_font == null) _font = GameFont.Regular;
            if (_root == null) Build();

            _pendingTech = tech;
            _onConfirm = onConfirm;

            RefreshContent();
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
        }

        public void Close()
        {
            if (_root != null) LG.Hide(_root);
            _pendingTech = null;
            _onConfirm = null;
        }

        private void RefreshContent()
        {
            if (_pendingTech == null) return;

            var tm = TechnologyManager.Instance;
            var tech = _pendingTech;

            // Заголовок и категория; кромка окна — цвет категории
            _titleText.text = tech.Name;
            _titleText.color = TechCategoryInfo.Color(tech.Category);
            var catCol = TechCategoryInfo.Color(tech.Category);
            _fx?.SetRim(new Color(catCol.r, catCol.g, catCol.b, 0.55f));

            string tierStr = tech.Tier == 0 ? "Базовый" : $"Tier {tech.Tier}";
            _categoryText.text = $"{TechCategoryInfo.Icon(tech.Category)}  {TechCategoryInfo.Name(tech.Category)}  ·  {tierStr}";

            // Эффект — короткое техническое описание с авто-окраской модификаторов
            string desc = string.IsNullOrEmpty(tech.Description) ? "Без описания." : tech.Description;
            _effectText.text = ColorizeModifiers(desc);

            // Флавор — атмосферное описание
            string flavor = TechFlavorDatabase.Get(tech.Id);
            _flavorText.text = flavor;

            // Стоимость
            float penalty = tm != null ? tm.GetYearPenalty(tech) : 1f;
            int days = tm != null ? tm.EstimateDays(tech) : -1;

            string penaltyNote = "";
            if (penalty > 1.01f)
                penaltyNote = $"  <color=#FFAA88>· опережает время ×{penalty:0.0}</color>";

            int slotCount = tm != null ? tm.Slots.Count : 0;
            int freeSlots = 0;
            if (tm != null)
            {
                foreach (var s in tm.Slots) if (s.CurrentTech == null) freeSlots++;
            }

            string slotNote = freeSlots > 0
                ? $"<color=#8EF08C>Свободных слотов: {freeSlots} / {slotCount}</color>"
                : $"<color=#FFAA88>Все слоты заняты — технология встанет в очередь</color>";

            _costText.text =
                $"<color=#F2C747><b>СТОИМОСТЬ</b></color>\n" +
                $"Время исследования: <b>~{TechnologyManager.FormatDays(days)}</b>{penaltyNote}\n" +
                $"<color=#8AA2A8>{tech.Cost:0} очков науки · наука {(tm != null ? tm.MonthlyResearchIncome : 0f):0.#}/мес</color>\n" +
                $"{slotNote}";
        }

        /// <summary>
        /// Ищет в тексте «+N%», «−N%», «+N» и «−N» и красит их зелёным / красным.
        /// Понимает как дефис «-», так и минус «−».
        /// </summary>
        public static string ColorizeModifiers(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var sb = new StringBuilder(text.Length + 64);
            int i = 0;

            while (i < text.Length)
            {
                char c = text[i];

                bool isPlus = c == '+';
                bool isMinus = c == '-' || c == '−';

                bool valid = (isPlus || isMinus)
                             && (i == 0 || !char.IsLetter(text[i - 1]))
                             && (i + 1 < text.Length && (char.IsDigit(text[i + 1]) || text[i + 1] == '0'));

                if (!valid) { sb.Append(c); i++; continue; }

                int start = i;
                i++; // пропускаем знак
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.' || text[i] == ',')) i++;
                if (i < text.Length && text[i] == '%') i++;

                string token = text.Substring(start, i - start);
                sb.Append(isPlus ? StatFormat.Positive(token) : StatFormat.Negative(token));
            }

            return sb.ToString();
        }

        private void Build()
        {
            _root = new GameObject("TechConfirmDialog");
            _root.transform.SetParent(_host.transform, false);

            var rt = _root.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(760, 600);

            _group = _root.AddComponent<CanvasGroup>();

            var bg = _root.AddComponent<Image>();
            bg.color = UIManager.DS.BgDeep;
            bg.raycastTarget = true;
            _fx = LG.Glass(_root);
            _fx.SetRim(new Color(0.45f, 0.95f, 0.90f, 0.5f));
            LG.Motion(_root, LGAppear.Kind.Pop);

            // Заголовок
            _titleText = MakeText(_root.transform, "", 20, FontStyle.Bold,
                UIManager.DS.TextPrimary, TextAnchor.MiddleLeft);
            var tRt = _titleText.rectTransform;
            tRt.anchorMin = new Vector2(0, 1);
            tRt.anchorMax = new Vector2(1, 1);
            tRt.pivot = new Vector2(0.5f, 1);
            tRt.offsetMin = new Vector2(24, 0);
            tRt.offsetMax = new Vector2(-24, 0);
            tRt.sizeDelta = new Vector2(0, 30);
            tRt.anchoredPosition = new Vector2(0, -18);

            // Категория
            _categoryText = MakeText(_root.transform, "", 11, FontStyle.Bold,
                UIManager.DS.TextMuted, TextAnchor.MiddleLeft);
            var cRt = _categoryText.rectTransform;
            cRt.anchorMin = new Vector2(0, 1);
            cRt.anchorMax = new Vector2(1, 1);
            cRt.pivot = new Vector2(0.5f, 1);
            cRt.offsetMin = new Vector2(24, 0);
            cRt.offsetMax = new Vector2(-24, 0);
            cRt.sizeDelta = new Vector2(0, 18);
            cRt.anchoredPosition = new Vector2(0, -52);

            // Разделитель под категорией
            MakeSeparator(_root.transform, -76);

            // ====== БЛОК «ЭФФЕКТ» ======
            var effectLabel = MakeText(_root.transform, "▸  ЭФФЕКТ", 11, FontStyle.Bold,
                UIManager.DS.Gold, TextAnchor.MiddleLeft);
            var elRt = effectLabel.rectTransform;
            elRt.anchorMin = new Vector2(0, 1);
            elRt.anchorMax = new Vector2(1, 1);
            elRt.pivot = new Vector2(0.5f, 1);
            elRt.offsetMin = new Vector2(24, 0);
            elRt.offsetMax = new Vector2(-24, 0);
            elRt.sizeDelta = new Vector2(0, 18);
            elRt.anchoredPosition = new Vector2(0, -88);

            var effectBox = new GameObject("EffectBox");
            effectBox.transform.SetParent(_root.transform, false);
            var eBt = effectBox.AddComponent<RectTransform>();
            eBt.anchorMin = new Vector2(0, 1);
            eBt.anchorMax = new Vector2(1, 1);
            eBt.pivot = new Vector2(0.5f, 1);
            eBt.offsetMin = new Vector2(24, 0);
            eBt.offsetMax = new Vector2(-24, 0);
            eBt.sizeDelta = new Vector2(0, 60);
            eBt.anchoredPosition = new Vector2(0, -112);

            var eBg = effectBox.AddComponent<Image>();
            eBg.color = new Color(0.02f, 0.06f, 0.08f, 0.9f);
            var eOl = effectBox.AddComponent<Outline>();
            eOl.effectColor = new Color(0.18f, 0.65f, 0.60f, 0.4f);
            eOl.effectDistance = new Vector2(0.8f, -0.8f);

            _effectText = MakeText(effectBox.transform, "", 13, FontStyle.Normal,
                UIManager.DS.TextPrimary, TextAnchor.MiddleLeft);
            var efRt = _effectText.rectTransform;
            efRt.anchorMin = Vector2.zero;
            efRt.anchorMax = Vector2.one;
            efRt.offsetMin = new Vector2(14, 8);
            efRt.offsetMax = new Vector2(-14, -8);
            _effectText.lineSpacing = 1.3f;
            _effectText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _effectText.verticalOverflow = VerticalWrapMode.Truncate;
            _effectText.supportRichText = true;

            // ====== БЛОК «ОПИСАНИЕ» ======
            var flavorLabel = MakeText(_root.transform, "▸  ОПИСАНИЕ", 11, FontStyle.Bold,
                UIManager.DS.Gold, TextAnchor.MiddleLeft);
            var flRt = flavorLabel.rectTransform;
            flRt.anchorMin = new Vector2(0, 1);
            flRt.anchorMax = new Vector2(1, 1);
            flRt.pivot = new Vector2(0.5f, 1);
            flRt.offsetMin = new Vector2(24, 0);
            flRt.offsetMax = new Vector2(-24, 0);
            flRt.sizeDelta = new Vector2(0, 18);
            flRt.anchoredPosition = new Vector2(0, -184);

            var flavorBox = new GameObject("FlavorBox");
            flavorBox.transform.SetParent(_root.transform, false);
            var fBt = flavorBox.AddComponent<RectTransform>();
            fBt.anchorMin = new Vector2(0, 1);
            fBt.anchorMax = new Vector2(1, 1);
            fBt.pivot = new Vector2(0.5f, 1);
            fBt.offsetMin = new Vector2(24, 0);
            fBt.offsetMax = new Vector2(-24, 0);
            fBt.sizeDelta = new Vector2(0, 190);
            fBt.anchoredPosition = new Vector2(0, -208);

            var fBg = flavorBox.AddComponent<Image>();
            fBg.color = new Color(0.02f, 0.06f, 0.08f, 0.75f);
            var fOl = flavorBox.AddComponent<Outline>();
            fOl.effectColor = new Color(0.18f, 0.65f, 0.60f, 0.4f);
            fOl.effectDistance = new Vector2(0.8f, -0.8f);

            _flavorText = MakeText(flavorBox.transform, "", 12, FontStyle.Normal,
                new Color(0.78f, 0.85f, 0.88f), TextAnchor.UpperLeft);
            var ffRt = _flavorText.rectTransform;
            ffRt.anchorMin = Vector2.zero;
            ffRt.anchorMax = Vector2.one;
            ffRt.offsetMin = new Vector2(14, 10);
            ffRt.offsetMax = new Vector2(-14, -10);
            _flavorText.lineSpacing = 1.4f;
            _flavorText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _flavorText.verticalOverflow = VerticalWrapMode.Truncate;

            // ====== РАЗДЕЛИТЕЛЬ ======
            MakeSeparator(_root.transform, -410);

            // ====== СТОИМОСТЬ ======
            var costBox = new GameObject("CostBox");
            costBox.transform.SetParent(_root.transform, false);
            var costRt = costBox.AddComponent<RectTransform>();
            costRt.anchorMin = new Vector2(0, 1);
            costRt.anchorMax = new Vector2(1, 1);
            costRt.pivot = new Vector2(0.5f, 1);
            costRt.offsetMin = new Vector2(24, 0);
            costRt.offsetMax = new Vector2(-24, 0);
            costRt.sizeDelta = new Vector2(0, 70);
            costRt.anchoredPosition = new Vector2(0, -428);

            _costText = MakeText(costBox.transform, "", 12, FontStyle.Normal,
                UIManager.DS.TextPrimary, TextAnchor.UpperLeft);
            var costTRt = _costText.rectTransform;
            costTRt.anchorMin = Vector2.zero;
            costTRt.anchorMax = Vector2.one;
            costTRt.offsetMin = Vector2.zero;
            costTRt.offsetMax = Vector2.zero;
            _costText.lineSpacing = 1.4f;
            _costText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _costText.supportRichText = true;

            // ====== КНОПКИ ======
            var confirmBtn = MakeButton(_root.transform, "▶  НАЧАТЬ ИССЛЕДОВАНИЕ",
                new Vector2(280, 46), UIManager.DS.BtnSuccess, OnConfirm);
            var cfRt = confirmBtn.GetComponent<RectTransform>();
            cfRt.anchorMin = new Vector2(0.5f, 0);
            cfRt.anchorMax = new Vector2(0.5f, 0);
            cfRt.pivot = new Vector2(0.5f, 0);
            cfRt.anchoredPosition = new Vector2(-150, 24);

            var cancelBtn = MakeButton(_root.transform, "✕  ОТМЕНА",
                new Vector2(280, 46), UIManager.DS.BtnNeutral, Close);
            var cnRt = cancelBtn.GetComponent<RectTransform>();
            cnRt.anchorMin = new Vector2(0.5f, 0);
            cnRt.anchorMax = new Vector2(0.5f, 0);
            cnRt.pivot = new Vector2(0.5f, 0);
            cnRt.anchoredPosition = new Vector2(150, 24);

            LG.Skin(_root.transform);
            _root.SetActive(false);
        }

        private void MakeSeparator(Transform parent, float yFromTop)
        {
            var sep = new GameObject("Sep");
            sep.transform.SetParent(parent, false);
            var rt = sep.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(-40, 1);
            rt.anchoredPosition = new Vector2(0, yFromTop);
            var img = sep.AddComponent<Image>();
            img.color = new Color(0.18f, 0.65f, 0.60f, 0.35f);
            img.raycastTarget = false;
        }

        private void OnConfirm()
        {
            var cb = _onConfirm;
            Close();
            cb?.Invoke();
        }

        private Text MakeText(Transform parent, string val, int size, FontStyle style,
            Color col, TextAnchor anchor)
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
            return t;
        }

        private GameObject MakeButton(Transform parent, string label, Vector2 size,
            Color bgColor, Action onClick)
        {
            var go = new GameObject("Btn");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = bgColor;
            img.raycastTarget = true;

            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());
            LG.Button(go);

            var labelOut = MakeText(go.transform, label, 13, FontStyle.Bold,
                Color.white, TextAnchor.MiddleCenter);
            labelOut.rectTransform.anchorMin = Vector2.zero;
            labelOut.rectTransform.anchorMax = Vector2.one;
            labelOut.rectTransform.offsetMin = Vector2.zero;
            labelOut.rectTransform.offsetMax = Vector2.zero;

            return go;
        }
    }
}