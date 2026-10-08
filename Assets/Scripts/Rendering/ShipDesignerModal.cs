using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Core.Audio;

namespace StellarisClone.Rendering
{
    /// <summary>Иконки и цвета модулей кораблей — общие для конструктора и подсказок.</summary>
    public static class ModuleVisuals
    {
        public static Color TypeColor(WeaponDamageType t) => t switch
        {
            WeaponDamageType.Energy => new Color(1f, 0.40f, 0.36f),
            WeaponDamageType.Kinetic => new Color(1f, 0.82f, 0.36f),
            _ => new Color(0.48f, 1f, 0.58f)
        };

        public static LGIcon TypeIcon(WeaponDamageType t) => t switch
        {
            WeaponDamageType.Energy => LGIcon.Laser,
            WeaponDamageType.Kinetic => LGIcon.Cannon,
            _ => LGIcon.Missile
        };

        public static LGIcon HullIcon(ShipClass c) => c switch
        {
            ShipClass.Destroyer => LGIcon.Destroyer,
            ShipClass.Frigate => LGIcon.Frigate,
            _ => LGIcon.Corvette
        };

        public static LGIcon Icon(ShipModule m)
        {
            if (m.SlotType == ModuleSlotType.Weapon) return TypeIcon(m.DamageType);
            if (m.SlotType == ModuleSlotType.Defense) return m.Shields > 0f ? LGIcon.ShieldDome : LGIcon.ArmorPlate;
            if (m.PowerProduce > 0f) return LGIcon.Reactor;
            if (m.Accuracy > 0f) return LGIcon.Target;
            return LGIcon.Thruster;
        }

        public static Color Tint(ShipModule m)
        {
            if (m.SlotType == ModuleSlotType.Weapon) return TypeColor(m.DamageType);
            if (m.SlotType == ModuleSlotType.Defense) return m.Shields > 0f ? new Color(0.45f, 0.76f, 1f) : new Color(0.88f, 0.78f, 0.62f);
            if (m.PowerProduce > 0f) return UIManager.DS.Gold;
            if (m.Accuracy > 0f) return new Color(1f, 0.56f, 0.78f);
            return UIManager.DS.NeonCyan;
        }

        /// <summary>Короткая строка характеристик модуля (урон — в игровых днях, как идёт бой).</summary>
        public static string StatLine(ShipModule m)
        {
            var parts = new List<string>();
            if (m.SlotType == ModuleSlotType.Weapon)
            {
                parts.Add($"{m.Damage:0} урона × {m.FireRate * CombatManager.ShotsPerDay:0.#}/день");
                parts.Add(ShipDesignManager.DamageTypeName(m.DamageType));
            }
            if (m.Hull > 0f) parts.Add($"+{m.Hull:0} корпуса");
            if (m.Armor > 0f) parts.Add($"+{m.Armor:0} брони");
            if (m.Shields > 0f) parts.Add($"+{m.Shields:0} щитов");
            if (m.PowerProduce > 0f) parts.Add($"+{m.PowerProduce:0} энергии");
            if (m.SpeedBonus > 0f) parts.Add($"+{m.SpeedBonus * 100f:0}% скорости");
            if (Mathf.Abs(m.Evasion) > 0.01f) parts.Add($"уклонение {(m.Evasion > 0 ? "+" : "")}{m.Evasion:0}");
            if (m.Accuracy > 0f) parts.Add($"+{m.Accuracy:0} точности");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// Конструктор кораблей: корпуса и проекты слева, схема корпуса со слотами по центру,
    /// тактический профиль справа. Каждое орудие проекта стреляет в бою само, своим типом урона,
    /// поэтому смешанное вооружение и баланс щитов и брони здесь реально решают исход боя.
    /// </summary>
    public class ShipDesignerModal : MonoBehaviour
    {
        public static ShipDesignerModal Instance { get; private set; }
        public bool IsOpen => _root != null && LG.IsVisible(_root);

        private const float Width = 1180f, Height = 700f;
        private const float HeaderH = 52f, FooterH = 60f;

        private Canvas _host;
        private GameObject _root;
        private ShipDesign _draft;
        private ShipClass _selectedClass = ShipClass.Corvette;

        private RectTransform _hullList, _designList, _slotArea, _profile, _pickerList;
        private GameObject _picker;
        private Text _pickerTitle, _statusText;
        private InputField _nameInput;
        private Image _silhouette;
        private Button _deleteBtn;

        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Primary => UIManager.DS.TextPrimary;
        private static Color Neon => UIManager.DS.NeonCyan;
        private static Color Gold => UIManager.DS.Gold;

        private void Awake() => Instance = this;

        public void BindHost(Canvas modalCanvas) => _host = modalCanvas;

        public void Open()
        {
            if (_host == null)
            {
                var modal = GameObject.Find("ModalCanvas");
                if (modal != null) _host = modal.GetComponent<Canvas>();
            }
            if (_root != null && _hullList == null) { Destroy(_root); _root = null; }
            if (_root == null) Build();
            if (_root == null) return;

            var dm = ShipDesignManager.Instance;
            var hull = dm != null ? dm.GetHull(_selectedClass) : null;
            if (hull == null || !hull.IsUnlocked()) _selectedClass = ShipClass.Corvette;
            LoadPrimaryDraft();
            _statusText.text = "";
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

        // ==================== ЧЕРНОВИК ====================

        private void LoadPrimaryDraft()
        {
            var dm = ShipDesignManager.Instance;
            if (dm == null) return;
            var existing = dm.GetLatestDesign(_selectedClass);
            SetDraft(existing != null ? Clone(existing) : dm.CreateDraft(_selectedClass));
        }

        private void SetDraft(ShipDesign d)
        {
            _draft = d;
            ShipDesignManager.Instance?.Recalc(_draft);
            if (_nameInput != null) _nameInput.SetTextWithoutNotify(_draft != null ? _draft.Name : "");
        }

        private static ShipDesign Clone(ShipDesign src)
        {
            var d = new ShipDesign { Id = src.Id, Name = src.Name, HullClass = src.HullClass };
            d.WeaponModuleIds.AddRange(src.WeaponModuleIds);
            d.DefenseModuleIds.AddRange(src.DefenseModuleIds);
            d.UtilityModuleIds.AddRange(src.UtilityModuleIds);
            return d;
        }

        private bool IsSaved(ShipDesign d) => d != null && ShipDesignManager.Instance?.GetDesign(d.Id) != null;

        private void MarkDirty()
        {
            ShipDesignManager.Instance?.Recalc(_draft);
            _statusText.text = "<color=#F2C747>Есть несохранённые изменения.</color>";
        }

        // ==================== КАРКАС ОКНА ====================

        private void Build()
        {
            if (_host == null) return;
            var rt = LGBuild.Rect(_host.transform, "ShipDesignerModal");
            rt.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Width, Height));
            _root = rt.gameObject;
            _root.AddComponent<CanvasGroup>();
            var bg = _root.AddComponent<Image>();
            bg.color = UIManager.DS.BgDeep;
            bg.raycastTarget = true;
            LG.Glass(_root).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.42f));
            LG.Motion(_root, LGAppear.Kind.Pop);

            // Шапка
            var header = LGBuild.Panel(rt, "Header", UIManager.DS.BgHeader);
            header.rectTransform.TopBand(0, HeaderH);
            LG.Header(header.gameObject);
            var hIcon = LGIcons.Create(header.transform, LGIcon.Shipyard, 20, Neon);
            hIcon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(20, 20));
            var title = LGBuild.Label(header.transform, "КОНСТРУКТОР КОРАБЛЕЙ", 15, Neon, TextAnchor.MiddleLeft, bold: true);
            title.rectTransform.Stretch(52, 0, 0, 0);
            var hint = LGBuild.Label(header.transform, "каждое орудие стреляет своим типом урона · кинетика срывает щиты, энергия жжёт броню, ракеты бьют всё",
                10, Muted, TextAnchor.MiddleRight);
            hint.rectTransform.Stretch(0, 0, 60, 0);
            var close = LGBuild.Button(header.transform, "Close", new Color(0.16f, 0.20f, 0.24f), new Color(1f, 0.45f, 0.48f, 0.55f), Close, LGIcon.Close, null, 11);
            ((RectTransform)close.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-12, 0), new Vector2(32, 32));

            // Колонки
            float top = HeaderH + 12f, bottom = FooterH;
            var left = Column(rt, "Hulls", 14f, 262f, top, bottom, UIManager.DS.BgSlot);
            var center = Column(rt, "Schematic", 288f, 504f, top, bottom, UIManager.DS.BgVisor);
            var right = Column(rt, "Profile", 804f, 362f, top, bottom, UIManager.DS.BgSlot);

            // Левая: корпуса и проекты
            SectionLabel(left, "КОРПУС", 10f);
            _hullList = LGBuild.Rect(left, "HullList");
            _hullList.TopBand(34f, 3 * 70f);
            SectionLabel(left, "ПРОЕКТЫ ВЕРФИ", 34f + 3 * 70f + 8f);
            var listHost = LGBuild.Rect(left, "DesignListHost");
            listHost.Stretch(6, 52, 6, 34f + 3 * 70f + 34f);
            _designList = LGBuild.ScrollList(listHost, 4f, 2);
            var newBtn = LGBuild.Button(left, "NewDesign", UIManager.DS.BtnPrimary, new Color(Neon.r, Neon.g, Neon.b, 0.5f),
                NewDesign, LGIcon.Wrench, "НОВЫЙ ПРОЕКТ", 11);
            var nbRt = (RectTransform)newBtn.transform;
            nbRt.anchorMin = new Vector2(0, 0); nbRt.anchorMax = new Vector2(1, 0); nbRt.pivot = new Vector2(0.5f, 0);
            nbRt.offsetMin = new Vector2(12, 10); nbRt.offsetMax = new Vector2(-12, 44);

            // Центр: имя, силуэт, слоты, выбор модуля
            var nameHost = LGBuild.Rect(center, "NameHost");
            nameHost.TopBand(12f, 38f, 16f, 16f);
            _nameInput = LGControls.Input(nameHost, "", "Название проекта", 40);
            _nameInput.onValueChanged.AddListener(v =>
            {
                if (_draft == null) return;
                _draft.Name = string.IsNullOrWhiteSpace(v) ? _draft.Name : v.Trim();
                _statusText.text = "<color=#F2C747>Есть несохранённые изменения.</color>";
            });

            _silhouette = LGIcons.Create(center, LGIcon.Corvette, 330f, new Color(Neon.r, Neon.g, Neon.b, 0.05f), "Silhouette");
            _silhouette.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -20f), new Vector2(330f, 330f));
            _slotArea = LGBuild.Rect(center, "Slots");
            _slotArea.Stretch(16, 12, 16, 62);

            BuildPicker(center);

            // Правая: профиль
            _profile = LGBuild.Rect(right, "ProfileBody");
            _profile.Stretch(14, 10, 14, 10);

            // Подвал: статус и кнопки
            _statusText = LGBuild.Label(rt, "", 12, Gold, TextAnchor.MiddleLeft, bold: true, wrap: true);
            _statusText.rectTransform.anchorMin = new Vector2(0, 0);
            _statusText.rectTransform.anchorMax = new Vector2(0, 0);
            _statusText.rectTransform.pivot = new Vector2(0, 0);
            _statusText.rectTransform.anchoredPosition = new Vector2(288f, 12f);
            _statusText.rectTransform.sizeDelta = new Vector2(504f, 36f);

            _deleteBtn = LGBuild.Button(rt, "Delete", UIManager.DS.BtnDanger, UIManager.DS.Red, DeleteDraft, LGIcon.Trash, "УДАЛИТЬ", 11);
            ((RectTransform)_deleteBtn.transform).At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(804f, 12f), new Vector2(118f, 36f));
            var save = LGBuild.Button(rt, "Save", UIManager.DS.BtnSuccess, UIManager.DS.Green, SaveDraft, LGIcon.Save, "СОХРАНИТЬ ПРОЕКТ", 12);
            ((RectTransform)save.transform).At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(930f, 12f), new Vector2(236f, 36f));
            TooltipHelper.Attach(save.gameObject, "<b>Сохранить проект</b>\nСохранённый проект становится основным для своего корпуса: " +
                                                  "по нему строит верфь, к нему модернизируются корабли.");

            _root.SetActive(false);
        }

        private static RectTransform Column(RectTransform parent, string name, float x, float w, float top, float bottom, Color tint)
        {
            var img = LGBuild.Panel(parent, name, tint);
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.offsetMin = new Vector2(x, bottom);
            rt.offsetMax = new Vector2(x + w, -top);
            LG.Platter(img.gameObject, 16f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.18f));
            return rt;
        }

        private static void SectionLabel(Transform parent, string text, float top)
        {
            var host = LGBuild.Rect(parent, "Section");
            host.TopBand(top, 20f, 14f, 14f);
            var t = LGBuild.Label(host, text, 10, Gold, TextAnchor.MiddleLeft, bold: true);
            t.rectTransform.Stretch();
            var line = LGBuild.Panel(host, "Line", new Color(Gold.r, Gold.g, Gold.b, 0.22f));
            line.rectTransform.anchorMin = new Vector2(0, 0);
            line.rectTransform.anchorMax = new Vector2(1, 0);
            line.rectTransform.sizeDelta = new Vector2(0, 1);
        }

        private void BuildPicker(RectTransform center)
        {
            var img = LGBuild.Panel(center, "ModulePicker", UIManager.DS.BgHeader, raycast: true);
            img.rectTransform.Stretch(10, 10, 10, 58);
            _picker = img.gameObject;
            var fx = LG.Platter(_picker, 16f);
            fx.SetRim(LG.Palette.GoldRim);
            fx.FillMultiplier = 2.9f;
            fx.Elevation = 0.9f;
            LG.Motion(_picker, LGAppear.Kind.Pop).fromScale = 0.94f;

            var head = LGBuild.Rect(img.transform, "Head");
            head.TopBand(8, 28, 14, 14);
            _pickerTitle = LGBuild.Label(head, "", 12, Neon, TextAnchor.MiddleLeft, bold: true);
            var close = LGBuild.Button(head, "ClosePicker", new Color(0.16f, 0.20f, 0.24f), new Color(1f, 0.45f, 0.48f, 0.55f),
                () => LG.Hide(_picker), LGIcon.Close, null, 9);
            ((RectTransform)close.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(26, 26));

            var listHost = LGBuild.Rect(img.transform, "ListHost");
            listHost.Stretch(8, 8, 8, 42);
            _pickerList = LGBuild.ScrollList(listHost, 5f, 4);
            _picker.SetActive(false);
        }

        // ==================== ПЕРЕСТРОЙКА ====================

        private void RebuildAll()
        {
            RebuildHulls();
            RebuildDesignList();
            RebuildSlots();
            RefreshProfile();
            if (_deleteBtn != null)
            {
                var dm = ShipDesignManager.Instance;
                _deleteBtn.interactable = dm != null && IsSaved(_draft) && dm.DesignsFor(_draft.HullClass).Count > 1;
            }
        }

        private void RebuildHulls()
        {
            var dm = ShipDesignManager.Instance;
            if (_hullList == null || dm == null) return;
            LGBuild.Clear(_hullList);
            float y = 0f;
            foreach (var hull in dm.Hulls)
            {
                var h = hull;
                bool unlocked = h.IsUnlocked();
                bool selected = h.Class == _selectedClass;
                var btn = LGBuild.Button(_hullList, "Hull_" + h.Class,
                    selected ? new Color(0.10f, 0.36f, 0.42f) : UIManager.DS.BtnNeutral,
                    selected ? new Color(Neon.r, Neon.g, Neon.b, 0.7f) : new Color(Neon.r, Neon.g, Neon.b, 0.2f),
                    () => SelectHull(h));
                var brt = (RectTransform)btn.transform;
                brt.TopBand(y, 62f, 10f, 10f);
                y += 70f;

                Color ic = unlocked ? (selected ? Neon : Primary) : new Color(Muted.r, Muted.g, Muted.b, 0.5f);
                var icon = LGIcons.Create(btn.transform, unlocked ? ModuleVisuals.HullIcon(h.Class) : LGIcon.Lock, 34f, ic);
                icon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(34, 34));

                var name = LGBuild.Label(btn.transform, h.DisplayName.ToUpper(), 13, unlocked ? Primary : Muted, TextAnchor.UpperLeft, bold: true);
                name.rectTransform.Stretch(58, 0, 8, 9);
                string sub = unlocked
                    ? $"оружие {h.WeaponSlots} · защита {h.DefenseSlots} · вспом. {h.UtilitySlots}\nкорпус {h.BaseHull:0} · уклонение {h.BaseEvasion:0}"
                    : $"нужна технология «{TechName(h.Class == ShipClass.Destroyer ? "cns_dst_1" : h.RequiredTechId)}»";
                var s = LGBuild.Label(btn.transform, sub, 10, Muted, TextAnchor.LowerLeft, wrap: true);
                s.rectTransform.Stretch(58, 7, 8, 0);
                TooltipHelper.Attach(btn.gameObject, $"<b>{h.DisplayName}</b>\nКорпус {h.BaseHull:0} · броня {h.BaseArmor:0} · щиты {h.BaseShields:0}\n" +
                    $"Уклонение {h.BaseEvasion:0} · скорость ×{h.BaseSpeed:0.##}\nСодержание {FleetData.UpkeepFor(FleetType.Military, h.Class):0.#} гелия-3/мес · " +
                    $"флотский лимит {EmpireEconomy.NavalSize(h.Class)}");
            }
        }

        private void SelectHull(HullBlueprint h)
        {
            if (!h.IsUnlocked())
            {
                _statusText.text = $"Корпус «{h.DisplayName}» откроется после технологии «{TechName(h.Class == ShipClass.Destroyer ? "cns_dst_1" : h.RequiredTechId)}».";
                return;
            }
            if (h.Class == _selectedClass) return;
            _selectedClass = h.Class;
            LoadPrimaryDraft();
            if (_picker != null) _picker.SetActive(false);
            _statusText.text = "";
            RebuildAll();
            LG.Skin(_root.transform);
        }

        private void RebuildDesignList()
        {
            var dm = ShipDesignManager.Instance;
            if (_designList == null || dm == null) return;
            LGBuild.Clear(_designList);
            var primary = dm.GetLatestDesign(_selectedClass);
            var designs = dm.DesignsFor(_selectedClass);

            if (_draft != null && !IsSaved(_draft))
                DesignRow(_draft, false, true);
            for (int i = designs.Count - 1; i >= 0; i--)
                DesignRow(designs[i], designs[i] == primary, false);
            if (designs.Count == 0 && (_draft == null || IsSaved(_draft)))
            {
                var empty = LGBuild.Label(_designList, "Проектов нет — соберите первый.", 11, Muted, TextAnchor.MiddleCenter);
                LGBuild.Height(empty.gameObject, 30);
            }
        }

        private void DesignRow(ShipDesign d, bool isPrimary, bool isDraft)
        {
            bool editing = _draft != null && _draft.Id == d.Id;
            var btn = LGBuild.Button(_designList, "Design", editing ? new Color(0.10f, 0.30f, 0.36f) : UIManager.DS.BtnNeutral,
                new Color(Neon.r, Neon.g, Neon.b, editing ? 0.6f : 0.18f), () =>
                {
                    if (isDraft || editing) return;
                    SetDraft(Clone(d));
                    _statusText.text = "";
                    if (_picker != null) _picker.SetActive(false);
                    RebuildAll();
                    LG.Skin(_root.transform);
                });
            LGBuild.Height(btn.gameObject, 44);

            var dm = ShipDesignManager.Instance;
            dm.Recalc(d);
            var name = LGBuild.Label(btn.transform, d.Name, 11, editing ? Neon : Primary, TextAnchor.UpperLeft, bold: true);
            name.rectTransform.Stretch(10, 0, isPrimary || isDraft ? 70 : 34, 6);
            var sub = LGBuild.Label(btn.transform,
                $"урон {d.Dps * CombatManager.ShotsPerDay:0}/день · {FleetManager.ShipAlloyCost(d.AlloyCost, 0):0} сплавов", 10, Muted, TextAnchor.LowerLeft);
            sub.rectTransform.Stretch(10, 6, 8, 0);

            if (isPrimary || isDraft)
            {
                var tag = LGBuild.Label(btn.transform, isDraft ? "ЧЕРНОВИК" : "ВЕРФЬ", 9, isDraft ? Gold : UIManager.DS.Green, TextAnchor.UpperRight, bold: true);
                tag.rectTransform.Stretch(0, 0, 10, 7);
                if (isPrimary) TooltipHelper.Attach(btn.gameObject, "<b>Основной проект</b>\nПо нему строит верфь и модернизируются корабли этого корпуса.");
            }
            else
            {
                var make = LGBuild.Button(btn.transform, "MakePrimary", new Color(0.10f, 0.22f, 0.18f), new Color(0.36f, 0.96f, 0.6f, 0.4f), () =>
                {
                    dm.MakePrimary(d);
                    _statusText.text = $"«{d.Name}» — теперь основной проект верфи.";
                    RebuildDesignList();
                    LG.Skin(_root.transform);
                }, LGIcon.Check, null, 8);
                ((RectTransform)make.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-6, -6), new Vector2(22, 22));
                TooltipHelper.Attach(make.gameObject, "<b>Сделать основным</b>\nВерфь начнёт строить этот проект.");
            }
        }

        // ==================== СЛОТЫ ====================

        private void RebuildSlots()
        {
            if (_slotArea == null) return;
            LGBuild.Clear(_slotArea);
            if (_draft == null || ShipDesignManager.Instance == null) return;
            _silhouette.sprite = LGIcons.Get(ModuleVisuals.HullIcon(_draft.HullClass));

            float y = 0f;
            y = SlotGroup("ОРУЖИЕ", LGIcon.Weapons, _draft.WeaponModuleIds, ModuleSlotType.Weapon, y);
            y = SlotGroup("ЗАЩИТА", LGIcon.Defense, _draft.DefenseModuleIds, ModuleSlotType.Defense, y);
            SlotGroup("ВСПОМОГАТЕЛЬНЫЕ", LGIcon.Gear, _draft.UtilityModuleIds, ModuleSlotType.Utility, y);
        }

        private float SlotGroup(string title, LGIcon icon, List<string> slots, ModuleSlotType type, float y)
        {
            var head = LGBuild.Rect(_slotArea, "Group");
            head.TopBand(y, 22f);
            var hic = LGIcons.Create(head, icon, 13f, Gold);
            hic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(2, 0), new Vector2(13, 13));
            var ht = LGBuild.Label(head, $"{title}  <color=#8AA2A8>· слотов {slots.Count}</color>", 10, Gold, TextAnchor.MiddleLeft, bold: true);
            ht.rectTransform.Stretch(20, 0, 0, 0);
            y += 26f;

            const float cardH = 48f, gap = 8f;
            for (int i = 0; i < slots.Count; i++)
            {
                int idx = i;
                int col = i % 2, row = i / 2;
                var mod = ShipDesignManager.Instance.GetModule(slots[i]);
                SlotCard(mod, type, col, y + row * (cardH + gap), cardH, () => OpenPicker(type, idx, slots));
            }
            int rows = (slots.Count + 1) / 2;
            return y + rows * (cardH + gap) + 6f;
        }

        private void SlotCard(ShipModule mod, ModuleSlotType type, int col, float top, float h, System.Action onClick)
        {
            Color tint = mod != null ? ModuleVisuals.Tint(mod) : Muted;
            var btn = LGBuild.Button(_slotArea, "Slot", mod != null ? new Color(tint.r * 0.12f, tint.g * 0.12f, tint.b * 0.12f, 0.95f) : new Color(0.04f, 0.08f, 0.1f, 0.8f),
                new Color(tint.r, tint.g, tint.b, mod != null ? 0.55f : 0.18f), onClick);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = new Vector2(col * 0.5f, 1f);
            rt.anchorMax = new Vector2(col * 0.5f + 0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(col == 0 ? 0f : 5f, -top - h);
            rt.offsetMax = new Vector2(col == 0 ? -5f : 0f, -top);

            if (mod == null)
            {
                var plus = LGBuild.Label(btn.transform, "+  пустой слот", 11, new Color(Muted.r, Muted.g, Muted.b, 0.75f), TextAnchor.MiddleCenter);
                plus.fontStyle = FontStyle.Italic;
                return;
            }

            var icon = LGIcons.Create(btn.transform, ModuleVisuals.Icon(mod), 26f, tint);
            icon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(26, 26));
            var name = LGBuild.Label(btn.transform, mod.Name, 11, Primary, TextAnchor.UpperLeft, bold: true);
            name.rectTransform.Stretch(44, 0, 44, 7);
            var stats = LGBuild.Label(btn.transform, ModuleVisuals.StatLine(mod), 9, Muted, TextAnchor.LowerLeft);
            stats.rectTransform.Stretch(44, 7, 6, 0);
            if (mod.PowerDraw > 0f)
            {
                var pw = LGBuild.Label(btn.transform, $"<color=#FFCC52>−{mod.PowerDraw:0}</color>", 10, Gold, TextAnchor.UpperRight, bold: true);
                pw.rectTransform.Stretch(0, 0, 10, 7);
            }
            TooltipHelper.Attach(btn.gameObject, ModuleTooltip(mod) + "\n\n<color=#8AA2A8>Щёлкните, чтобы заменить.</color>");
        }

        private static string ModuleTooltip(ShipModule m)
        {
            string power = m.PowerProduce > 0f ? $"Выработка энергии +{m.PowerProduce:0}" : m.PowerDraw > 0f ? $"Потребление энергии {m.PowerDraw:0}" : "Не требует энергии";
            return $"<b>{m.Name}</b>\n{m.Description}\n<color=#8AA2A8>{ModuleVisuals.StatLine(m)}</color>\n{power} · " +
                   $"{FleetManager.ShipAlloyCost(m.AlloyCost, 0):0} сплавов";
        }

        // ==================== ВЫБОР МОДУЛЯ ====================

        private void OpenPicker(ModuleSlotType type, int index, List<string> slots)
        {
            var dm = ShipDesignManager.Instance;
            if (dm == null || _picker == null) return;
            string typeName = type == ModuleSlotType.Weapon ? "ОРУЖИЕ" : type == ModuleSlotType.Defense ? "ЗАЩИТА" : "ВСПОМОГАТЕЛЬНЫЙ МОДУЛЬ";
            _pickerTitle.text = $"ВЫБОР МОДУЛЯ · {typeName} · слот {index + 1}";
            LGBuild.Clear(_pickerList);

            var all = new List<ShipModule>();
            foreach (var m in dm.Modules.Values) if (m.SlotType == type) all.Add(m);
            all.Sort((a, b) =>
            {
                bool ua = a.IsUnlocked(), ub = b.IsUnlocked();
                if (ua != ub) return ua ? -1 : 1;
                return a.AlloyCost.CompareTo(b.AlloyCost);
            });

            string current = slots[index];
            if (!string.IsNullOrEmpty(current))
            {
                var remove = LGBuild.Button(_pickerList, "Remove", new Color(0.24f, 0.08f, 0.1f), new Color(1f, 0.4f, 0.42f, 0.4f), () =>
                {
                    slots[index] = "";
                    AfterSlotChange();
                }, LGIcon.Close, "СНЯТЬ МОДУЛЬ", 10);
                LGBuild.Height(remove.gameObject, 30);
            }

            foreach (var m in all) PickerRow(m, m.Id == current, () =>
            {
                slots[index] = m.Id;
                AfterSlotChange();
            });

            LG.Show(_picker);
            _picker.transform.SetAsLastSibling();
            LG.Skin(_picker.transform);
        }

        private void PickerRow(ShipModule m, bool isCurrent, System.Action pick)
        {
            bool unlocked = m.IsUnlocked();
            Color tint = ModuleVisuals.Tint(m);
            var btn = LGBuild.Button(_pickerList, "Module", isCurrent ? new Color(0.10f, 0.30f, 0.36f) : UIManager.DS.BtnNeutral,
                new Color(tint.r, tint.g, tint.b, unlocked ? 0.45f : 0.12f), () => { if (unlocked) pick(); });
            btn.interactable = unlocked;
            LGBuild.Height(btn.gameObject, 56);

            Color ic = unlocked ? tint : new Color(Muted.r, Muted.g, Muted.b, 0.45f);
            var icon = LGIcons.Create(btn.transform, unlocked ? ModuleVisuals.Icon(m) : LGIcon.Lock, 28f, ic);
            icon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(28, 28));

            var name = LGBuild.Label(btn.transform, (isCurrent ? "▸ " : "") + m.Name, 12, unlocked ? Primary : Muted, TextAnchor.UpperLeft, bold: true);
            name.rectTransform.Stretch(50, 0, 120, 8);
            string line = unlocked ? ModuleVisuals.StatLine(m) : $"<color=#FF8A8A>требуется «{TechName(m.RequiredTechId)}»</color>";
            var stats = LGBuild.Label(btn.transform, line, 10, Muted, TextAnchor.LowerLeft);
            stats.rectTransform.Stretch(50, 8, 120, 0);

            string power = m.PowerProduce > 0f ? $"<color=#5CF59A>+{m.PowerProduce:0} ⚡</color>"
                         : m.PowerDraw > 0f ? $"<color=#FFCC52>−{m.PowerDraw:0} ⚡</color>" : "<color=#8AA2A8>0 ⚡</color>";
            var right = LGBuild.Label(btn.transform, $"{power}\n<color=#C9D6DC>{FleetManager.ShipAlloyCost(m.AlloyCost, 0):0} сплавов</color>",
                10, Primary, TextAnchor.MiddleRight, bold: true);
            right.rectTransform.Stretch(0, 0, 12, 0);
            TooltipHelper.Attach(btn.gameObject, ModuleTooltip(m));
        }

        private void AfterSlotChange()
        {
            LG.Hide(_picker);
            MarkDirty();
            RebuildSlots();
            RebuildDesignList();
            RefreshProfile();
            LG.Skin(_root.transform);
        }

        // ==================== ТАКТИЧЕСКИЙ ПРОФИЛЬ ====================

        private void RefreshProfile()
        {
            if (_profile == null || _draft == null) return;
            LGBuild.Clear(_profile);
            var d = _draft;
            ShipDesignManager.Instance.Recalc(d);
            var b = EmpireBonuses.For(0);
            float y = 0f;

            var head = LGBuild.Label(_profile, "ТАКТИЧЕСКИЙ ПРОФИЛЬ", 13, Neon, TextAnchor.UpperLeft, bold: true);
            head.rectTransform.TopBand(y, 20f);
            var note = LGBuild.Label(_profile, "с учётом изученных технологий", 9, Muted, TextAnchor.UpperRight);
            note.rectTransform.TopBand(y + 3f, 16f);
            y += 28f;

            // Защита
            y = Group("ЗАЩИТА", y);
            y = StatBar(LGIcon.Ship, "Корпус", d.Hull * b.HullMult, 500f, new Color(0.75f, 0.85f, 0.9f), y);
            y = StatBar(LGIcon.ArmorPlate, "Броня", d.Armor * b.ArmorMult, 320f, new Color(0.88f, 0.78f, 0.62f), y);
            y = StatBar(LGIcon.ShieldDome, "Щиты", d.Shields * b.ShieldMult, 300f, new Color(0.45f, 0.76f, 1f), y);
            y += 6f;

            // Огневая мощь
            float e = d.DpsOf(WeaponDamageType.Energy) * b.DamageMult(WeaponDamageType.Energy) * CombatManager.ShotsPerDay;
            float k = d.DpsOf(WeaponDamageType.Kinetic) * b.DamageMult(WeaponDamageType.Kinetic) * CombatManager.ShotsPerDay;
            float x = d.DpsOf(WeaponDamageType.Explosive) * b.DamageMult(WeaponDamageType.Explosive) * CombatManager.ShotsPerDay;
            float total = e + k + x;
            y = Group($"ОГНЕВАЯ МОЩЬ  <color=#E8F4F8>{total:0} урона в день</color>", y);
            y = StatBar(LGIcon.Laser, "Энергия", e, 160f, ModuleVisuals.TypeColor(WeaponDamageType.Energy), y);
            y = StatBar(LGIcon.Cannon, "Кинетика", k, 160f, ModuleVisuals.TypeColor(WeaponDamageType.Kinetic), y);
            y = StatBar(LGIcon.Missile, "Взрыв", x, 160f, ModuleVisuals.TypeColor(WeaponDamageType.Explosive), y);
            float vsShields = e * CombatManager.EnergyVsShield + k * CombatManager.KineticVsShield + x * CombatManager.ExplosiveVsAll;
            float vsArmor = e * CombatManager.EnergyVsArmor + k * CombatManager.KineticVsArmor + x * CombatManager.ExplosiveVsAll;
            y = Line($"По щитам <b>{vsShields:0}</b>/день  ·  по броне <b>{vsArmor:0}</b>/день  ·  орудий {d.Mounts.Count}", y);
            y += 6f;

            // Манёвр
            y = Group("МАНЁВР", y);
            float evasion = d.Evasion * b.EvasionMult;
            y = Line($"Уклонение <b>{evasion:0}</b>  ·  точность <b>+{d.Accuracy + b.Accuracy:0}</b>  ·  скорость <b>×{d.Speed * b.HyperlaneSpeed:0.##}</b>", y);
            y += 6f;

            // Энергия
            y = Group("ЭНЕРГОСЕТЬ", y);
            float load = d.PowerProduce > 0f ? d.PowerDraw / d.PowerProduce : (d.PowerDraw > 0f ? 2f : 0f);
            Color pc = d.IsPowerValid ? (load > 0.85f ? Gold : UIManager.DS.Green) : UIManager.DS.Red;
            y = StatBarRaw(LGIcon.Reactor, "Нагрузка", $"{d.PowerDraw:0} / {d.PowerProduce:0}", Mathf.Clamp01(load), pc, y);
            y += 6f;

            // Стоимость
            y = Group("СТОИМОСТЬ", y);
            y = Line($"Постройка <color=#F2C747><b>{FleetManager.ShipAlloyCost(d.AlloyCost, 0):0}</b> сплавов</color>", y);
            y = Line($"Содержание <color=#FF9A9A><b>{FleetData.UpkeepFor(FleetType.Military, d.HullClass):0.#}</b> гелия-3/мес</color>  ·  " +
                     $"флотский лимит {EmpireEconomy.NavalSize(d.HullClass)}", y);
            y += 4f;

            // Предупреждения
            string warn = !d.IsPowerValid ? "<color=#FF5A5A>Перегрузка энергосети — поставьте реактор или снимите прожорливые модули.</color>"
                        : d.Mounts.Count == 0 ? "<color=#FFCC52>Без оружия корабль не участвует в перестрелке.</color>"
                        : vsShields > vsArmor * 1.4f ? "<color=#8AA2A8>Уклон в кинетику: силён против щитов, слабее против брони.</color>"
                        : vsArmor > vsShields * 1.4f ? "<color=#8AA2A8>Уклон в энергию: силён против брони, щиты отражают часть урона.</color>"
                        : "<color=#5CF59A>Сбалансированное вооружение.</color>";
            var w = LGBuild.Label(_profile, warn, 10, Primary, TextAnchor.UpperLeft, wrap: true);
            w.rectTransform.TopBand(y, 36f);
        }

        private float Group(string title, float y)
        {
            var t = LGBuild.Label(_profile, title, 10, Gold, TextAnchor.LowerLeft, bold: true);
            t.rectTransform.TopBand(y, 18f);
            var line = LGBuild.Panel(_profile, "Line", new Color(Gold.r, Gold.g, Gold.b, 0.18f));
            line.rectTransform.TopBand(y + 19f, 1f);
            return y + 24f;
        }

        private float Line(string text, float y)
        {
            var t = LGBuild.Label(_profile, text, 11, Primary, TextAnchor.MiddleLeft, wrap: true);
            t.rectTransform.TopBand(y, 20f);
            return y + 21f;
        }

        private float StatBar(LGIcon icon, string label, float value, float reference, Color col, float y)
            => StatBarRaw(icon, label, value > 0f ? value.ToString("0") : "—", value / reference, col, y, value <= 0f);

        private float StatBarRaw(LGIcon icon, string label, string valueText, float fill, Color col, float y, bool dim = false)
        {
            var row = LGBuild.Rect(_profile, "Stat");
            row.TopBand(y, 22f);
            Color c = dim ? new Color(Muted.r, Muted.g, Muted.b, 0.45f) : col;
            var ic = LGIcons.Create(row, icon, 14f, c);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(14, 14));
            var l = LGBuild.Label(row, label, 11, dim ? Muted : Primary, TextAnchor.MiddleLeft);
            l.rectTransform.Stretch(20, 0, 0, 0);
            var v = LGBuild.Label(row, valueText, 11, c, TextAnchor.MiddleRight, bold: true);
            v.rectTransform.anchorMin = new Vector2(0, 0); v.rectTransform.anchorMax = new Vector2(0, 1);
            v.rectTransform.offsetMin = new Vector2(96, 0); v.rectTransform.offsetMax = new Vector2(160, 0);
            var barHost = LGBuild.Rect(row, "BarHost");
            barHost.Stretch(170, 0, 0, 0);
            LGBuild.Bar(barHost, c, Mathf.Clamp01(fill), 7f);
            return y + 25f;
        }

        // ==================== ДЕЙСТВИЯ ====================

        private void NewDesign()
        {
            var dm = ShipDesignManager.Instance;
            if (dm == null) return;
            var d = dm.CreateDraft(_selectedClass);
            d.Name = $"{dm.GetHull(_selectedClass).DisplayName} «Новый {dm.DesignsFor(_selectedClass).Count + 1}»";
            SetDraft(d);
            if (_picker != null) _picker.SetActive(false);
            _statusText.text = "Новый проект: заполните слоты и сохраните.";
            RebuildAll();
            LG.Skin(_root.transform);
        }

        private void SaveDraft()
        {
            var dm = ShipDesignManager.Instance;
            if (_draft == null || dm == null) return;
            dm.Recalc(_draft);
            if (!_draft.IsPowerValid)
            {
                _statusText.text = "<color=#FF5A5A>Невозможно сохранить: потребление энергии больше выработки.</color>";
                return;
            }
            // В архив кладём копию: черновик остаётся редактируемым, сохранённый проект не меняется без «Сохранить»
            var copy = Clone(_draft);
            if (!dm.SaveDesign(copy)) return;
            SFXManager.Play(Sfx.UiConfirm, 0.7f);
            _statusText.text = $"<color=#5CF59A>«{copy.Name}» сохранён — верфь строит по этому проекту.</color>";
            RebuildAll();
            LG.Skin(_root.transform);
        }

        private void DeleteDraft()
        {
            var dm = ShipDesignManager.Instance;
            if (dm == null || _draft == null) return;
            var saved = dm.GetDesign(_draft.Id);
            if (saved == null || !dm.DeleteDesign(saved))
            {
                _statusText.text = "Последний проект корпуса удалить нельзя — верфи нужен чертёж.";
                return;
            }
            string name = saved.Name;
            LoadPrimaryDraft();
            _statusText.text = $"Проект «{name}» удалён.";
            RebuildAll();
            LG.Skin(_root.transform);
        }

        private static string TechName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "—";
            var t = TechnologyManager.Instance?.FindTech(id);
            return t != null ? t.Name : id;
        }
    }
}
