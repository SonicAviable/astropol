using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    public class ShipDesignerModal : MonoBehaviour
    {
        public static ShipDesignerModal Instance { get; private set; }
        public bool IsOpen => _root != null && LG.IsVisible(_root);

        private Canvas _host;
        private GameObject _root;
        private CanvasGroup _group;
        private Font _font;
        private ShipDesign _draft;
        private ShipClass _selectedClass = ShipClass.Corvette;
        private Text _statsText;
        private Transform _slotHost;
        private Transform _classHost;
        private GameObject _picker;
        private Text _statusText;

        private void Awake()
        {
            Instance = this;
            EnsureFont();
        }

        private void EnsureFont()
        {
            if (_font != null) return;
_font = GameFont.Regular;
        }

        public void BindHost(Canvas modalCanvas)
        {
            _host = modalCanvas;
        }

        public void Open()
        {
            EnsureFont();
            if (_host == null)
            {
                var modal = GameObject.Find("ModalCanvas");
                if (modal != null) _host = modal.GetComponent<Canvas>();
            }
            if (_root != null && _classHost == null)
            {
                Destroy(_root);
                _root = null;
            }
            if (_root == null) Build();
            if (_root == null || _classHost == null) return;
            EnsureDraft();
            RebuildAll();
            UIManager.Instance?.ShowModalDimPublic();
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
            LG.Skin(_root.transform);
        }

        public void Close()
        {
            if (_picker != null) _picker.SetActive(false);
            if (_root != null) LG.Hide(_root);
            UIManager.Instance?.HideModalDimPublic();
        }

        private void EnsureDraft()
        {
            var dm = ShipDesignManager.Instance;
            if (dm == null) return;
            var existing = dm.GetLatestDesign(_selectedClass);
            _draft = existing != null ? Clone(existing) : dm.CreateDraft(_selectedClass);
            dm.Recalc(_draft);
        }

        private static ShipDesign Clone(ShipDesign src)
        {
            var d = new ShipDesign
            {
                Id = src.Id,
                Name = src.Name,
                HullClass = src.HullClass
            };
            d.WeaponModuleIds.AddRange(src.WeaponModuleIds);
            d.DefenseModuleIds.AddRange(src.DefenseModuleIds);
            d.UtilityModuleIds.AddRange(src.UtilityModuleIds);
            return d;
        }

        private void Build()
        {
            if (_host == null) return;
            _root = new GameObject("ShipDesignerModal");
            _root.transform.SetParent(_host.transform, false);
            var rt = _root.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1100, 640);
            _group = _root.AddComponent<CanvasGroup>();
            _root.AddComponent<Image>().color = UIManager.DS.BgDeep;
            LG.Glass(_root).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.42f));
            LG.Motion(_root, LGAppear.Kind.Pop);

            var header = Panel(_root.transform, "Header", UIManager.DS.BgHeader);
            StretchTop(header, 52);
            LG.Header(header);
            Txt(header.transform, "◆  КОНСТРУКТОР КОРАБЛЕЙ", 14, FontStyle.Bold, UIManager.DS.NeonCyan, TextAnchor.MiddleLeft, new Vector2(20, 0), new Vector2(1, 1));

            var close = Btn(header.transform, "✕", new Vector2(30, 30), new Color(0.16f, 0.20f, 0.24f), Close);
            LG.Button(close, new Color(1f, 0.45f, 0.48f, 0.55f), 15f);
            var cRt = close.GetComponent<RectTransform>();
            cRt.anchorMin = cRt.anchorMax = new Vector2(1, 0.5f);
            cRt.pivot = new Vector2(1, 0.5f);
            cRt.anchoredPosition = new Vector2(-14, 0);

            _classHost = Panel(_root.transform, "Classes", UIManager.DS.BgSlot).transform;
            var clRt = _classHost.GetComponent<RectTransform>();
            clRt.anchorMin = new Vector2(0.015f, 0.08f);
            clRt.anchorMax = new Vector2(0.22f, 0.90f);
            clRt.offsetMin = clRt.offsetMax = Vector2.zero;

            _slotHost = Panel(_root.transform, "Hull", UIManager.DS.BgVisor).transform;
            var sRt = _slotHost.GetComponent<RectTransform>();
            sRt.anchorMin = new Vector2(0.235f, 0.08f);
            sRt.anchorMax = new Vector2(0.68f, 0.90f);
            sRt.offsetMin = sRt.offsetMax = Vector2.zero;

            var stats = Panel(_root.transform, "Stats", UIManager.DS.BgSlot);
            var stRt = stats.GetComponent<RectTransform>();
            stRt.anchorMin = new Vector2(0.695f, 0.16f);
            stRt.anchorMax = new Vector2(0.985f, 0.90f);
            stRt.offsetMin = stRt.offsetMax = Vector2.zero;
            _statsText = Txt(stats.transform, "", 12, FontStyle.Normal, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, Vector2.zero, Vector2.one);
            _statsText.rectTransform.offsetMin = new Vector2(14, 12);
            _statsText.rectTransform.offsetMax = new Vector2(-14, -12);
            _statsText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _statsText.verticalOverflow = VerticalWrapMode.Overflow;
            _statsText.lineSpacing = 1.2f;

            var save = Btn(_root.transform, "СОХРАНИТЬ ПРОЕКТ", new Vector2(300, 36), UIManager.DS.BtnSuccess, SaveDraft);
            var svRt = save.GetComponent<RectTransform>();
            svRt.anchorMin = svRt.anchorMax = new Vector2(0.84f, 0.05f);
            svRt.pivot = new Vector2(0.5f, 0.5f);

            _statusText = Txt(_root.transform, "", 11, FontStyle.Bold, UIManager.DS.Gold, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
            _statusText.rectTransform.anchorMin = new Vector2(0.24f, 0.02f);
            _statusText.rectTransform.anchorMax = new Vector2(0.68f, 0.08f);

            _picker = new GameObject("ModulePicker");
            _picker.transform.SetParent(_root.transform, false);
            var pRt = _picker.AddComponent<RectTransform>();
            pRt.anchorMin = pRt.anchorMax = new Vector2(0.5f, 0.5f);
            pRt.sizeDelta = new Vector2(340, 280);
            _picker.AddComponent<Image>().color = UIManager.DS.BgHeader;
            // Поповер внутри окна: почти непрозрачная пластина с золотой кромкой и тенью
            var pickerFx = LG.Platter(_picker, 20f);
            pickerFx.SetRim(LG.Palette.GoldRim);
            pickerFx.FillMultiplier = 2.9f;
            pickerFx.Elevation = 0.9f;
            LG.Motion(_picker, LGAppear.Kind.Pop).fromScale = 0.9f;
            _picker.SetActive(false);

            _root.SetActive(false);
        }

        private void RebuildAll()
        {
            RebuildClasses();
            RebuildSlots();
            RefreshStats();
        }

        private void RebuildClasses()
        {
            if (_classHost == null || ShipDesignManager.Instance == null) return;
            ClearKids(_classHost);
            Txt(_classHost, "КЛАСС КОРПУСА", 11, FontStyle.Bold, UIManager.DS.Gold, TextAnchor.UpperCenter,
                new Vector2(0, 0.88f), new Vector2(1, 1));

            float y = 70f;
            foreach (var hull in ShipDesignManager.Instance.Hulls)
            {
                bool unlocked = hull.IsUnlocked();
                var label = unlocked ? hull.DisplayName.ToUpper() : $"{hull.DisplayName.ToUpper()}  [ЗАКРЫТО]";
                var captured = hull.Class;
                var go = Btn(_classHost, label, new Vector2(180, 42),
                    _selectedClass == hull.Class ? UIManager.DS.BtnPrimary : UIManager.DS.BtnNeutral,
                    () =>
                    {
                        if (!unlocked)
                        {
                            _statusText.text = "Корпус заблокирован: требуется технология верфей «Эсминец».";
                            return;
                        }
                        _selectedClass = captured;
                        EnsureDraft();
                        RebuildAll();
                    });
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0, -y);
                y += 52f;
            }
        }

        private void RebuildSlots()
        {
            if (_slotHost == null) return;
            ClearKids(_slotHost);
            if (_draft == null || ShipDesignManager.Instance == null) return;
            var hull = ShipDesignManager.Instance.GetHull(_draft.HullClass);
            Txt(_slotHost, $"СХЕМА КОРПУСА · {hull.DisplayName.ToUpper()}", 12, FontStyle.Bold, UIManager.DS.NeonCyan,
                TextAnchor.UpperCenter, new Vector2(0, 0.92f), new Vector2(1, 1));

            float y = 56f;
            y = DrawSlotRow(_slotHost, "ОРУЖИЕ", _draft.WeaponModuleIds, ModuleSlotType.Weapon, y);
            y = DrawSlotRow(_slotHost, "ЗАЩИТА", _draft.DefenseModuleIds, ModuleSlotType.Defense, y);
            DrawSlotRow(_slotHost, "ВСПОМОГАТЕЛЬНЫЕ", _draft.UtilityModuleIds, ModuleSlotType.Utility, y);
        }

        private float DrawSlotRow(Transform parent, string title, List<string> slots, ModuleSlotType type, float y)
        {
            Txt(parent, title, 11, FontStyle.Bold, UIManager.DS.Gold, TextAnchor.MiddleLeft,
                new Vector2(0.06f, 1f), new Vector2(1, 1)).rectTransform.anchoredPosition = new Vector2(0, -y);
            y += 28f;
            for (int i = 0; i < slots.Count; i++)
            {
                int idx = i;
                var mod = ShipDesignManager.Instance.GetModule(slots[i]);
                string label = mod != null ? mod.Name : "▸  ПУСТОЙ СЛОТ";
                var go = Btn(parent, label, new Vector2(400, 34), UIManager.DS.BgSlot, () => OpenPicker(type, idx, slots));
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0, -y);
                y += 40f;
            }
            return y + 8f;
        }

        private void OpenPicker(ModuleSlotType type, int index, List<string> slots)
        {
            ClearKids(_picker.transform);
            LG.Show(_picker);
            _picker.transform.SetAsLastSibling();
            Txt(_picker.transform, "ДОСТУПНЫЕ КОМПОНЕНТЫ", 12, FontStyle.Bold, UIManager.DS.NeonCyan,
                TextAnchor.UpperCenter, new Vector2(0, 0.86f), new Vector2(1, 1));

            var mods = ShipDesignManager.Instance.GetUnlockedModules(type);
            float y = 48f;
            var none = Btn(_picker.transform, "— снять модуль —", new Vector2(300, 28), UIManager.DS.BtnDanger, () =>
            {
                slots[index] = "";
                ShipDesignManager.Instance.Recalc(_draft);
                LG.Hide(_picker);
                RebuildSlots();
                RefreshStats();
            });
            PinTop(none, y); y += 34f;

            foreach (var m in mods)
            {
                var captured = m;
                var go = Btn(_picker.transform, $"{captured.Name}  ({FleetManager.ShipAlloyCost(captured.AlloyCost, 0):0} спл.)", new Vector2(300, 30), UIManager.DS.BtnPrimary, () =>
                {
                    slots[index] = captured.Id;
                    ShipDesignManager.Instance.Recalc(_draft);
                    LG.Hide(_picker);
                    RebuildSlots();
                    RefreshStats();
                });
                PinTop(go, y);
                y += 34f;
            }
            LG.Skin(_picker.transform);
        }

        private void RefreshStats()
        {
            if (_draft == null || _statsText == null) return;
            ShipDesignManager.Instance.Recalc(_draft);
            string powerCol = _draft.IsPowerValid ? "#4DF08C" : "#FF5555";
            _statsText.text =
                $"<color=#F2C747><b>ТАКТИЧЕСКИЙ ПРОФИЛЬ</b></color>\n\n" +
                $"Проект: {_draft.Name}\n" +
                $"Корпус: {_draft.Hull:0}\n" +
                $"Броня: {_draft.Armor:0}\n" +
                $"Щиты: {_draft.Shields:0}\n" +
                $"Урон / залп: {_draft.Damage:0.0}\n" +
                $"Скорострельность: {_draft.FireRate:0.00}/с\n" +
                $"DPS: <color=#33E6CC><b>{_draft.Dps:0.0}</b></color>\n" +
                $"Уклонение: {_draft.Evasion:0}%\n" +
                $"Энергобаланс: <color={powerCol}>{_draft.PowerBalance:+0.0;-0.0}</color>\n" +
                $"Стоимость: <color=#F2C747>{FleetManager.ShipAlloyCost(_draft.AlloyCost, 0):0} сплавов</color>\n" +
                $"Содержание: <color=#FF8888>{FleetData.UpkeepFor(FleetType.Military, _draft.HullClass):0.#} гелия-3/мес</color>, " +
                $"флотский лимит {EmpireEconomy.NavalSize(_draft.HullClass)}\n\n" +
                (_draft.IsPowerValid
                    ? "<color=#4DF08C>Реактор держит нагрузку.</color>"
                    : "<color=#FF5555>Перегрузка энергосети. Усильте реактор.</color>");
        }

        private void SaveDraft()
        {
            if (_draft == null) return;
            ShipDesignManager.Instance.Recalc(_draft);
            if (!_draft.IsPowerValid)
            {
                _statusText.text = "Невозможно сохранить: энергопотребление превышает выработку.";
                return;
            }
            if (ShipDesignManager.Instance.SaveDesign(_draft))
                _statusText.text = "Проект зафиксирован в архиве верфи.";
        }

        private static void PinTop(GameObject go, float y)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0, -y);
        }

        private static void ClearKids(Transform t)
        {
            if (t == null) return;
            for (int i = t.childCount - 1; i >= 0; i--)
                Destroy(t.GetChild(i).gameObject);
        }

        private static GameObject Panel(Transform parent, string name, Color col)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            go.AddComponent<Image>().color = col;
            LG.Platter(go, 16f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.20f));
            return go;
        }

        private static void StretchTop(GameObject go, float h)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, h);
            rt.anchoredPosition = Vector2.zero;
        }

        private Text Txt(Transform parent, string val, int size, FontStyle st, Color col, TextAnchor a, Vector2 amin, Vector2 amax)
        {
            EnsureFont();
            var go = new GameObject("Txt");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            if (_font != null) t.font = _font;
            t.text = val; t.fontSize = size; t.fontStyle = st; t.color = col; t.alignment = a;
            t.raycastTarget = false; t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.rectTransform.anchorMin = amin; t.rectTransform.anchorMax = amax;
            t.rectTransform.offsetMin = new Vector2(8, 0);
            t.rectTransform.offsetMax = new Vector2(-8, 0);
            return t;
        }

        private GameObject Btn(Transform parent, string label, Vector2 size, Color bg, System.Action click)
        {
            EnsureFont();
            var go = new GameObject("Btn");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = size;
            go.AddComponent<Image>().color = bg;
            var b = go.AddComponent<Button>();
            b.onClick.AddListener(() => click?.Invoke());
            LG.Button(go);

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            var lRt = labelGo.AddComponent<RectTransform>();
            lRt.anchorMin = Vector2.zero;
            lRt.anchorMax = Vector2.one;
            lRt.offsetMin = Vector2.zero;
            lRt.offsetMax = Vector2.zero;
            var t = labelGo.AddComponent<Text>();
            if (_font != null) t.font = _font;
            t.text = label;
            t.fontSize = 11;
            t.fontStyle = FontStyle.Bold;
            t.color = UIManager.DS.TextPrimary;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return go;
        }
    }
}
