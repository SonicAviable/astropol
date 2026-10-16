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
                parts.Add(Loc.F("{0:0} урона × {1:0.#}/день", m.Damage, m.FireRate * CombatManager.ShotsPerDay));
                parts.Add(ShipDesignManager.DamageTypeName(m.DamageType));
            }
            if (m.Hull > 0f) parts.Add(Loc.F("+{0:0} корпуса", m.Hull));
            if (m.Armor > 0f) parts.Add(Loc.F("+{0:0} брони", m.Armor));
            if (m.Shields > 0f) parts.Add(Loc.F("+{0:0} щитов", m.Shields));
            if (m.PowerProduce > 0f) parts.Add(Loc.F("+{0:0} энергии", m.PowerProduce));
            if (m.SpeedBonus > 0f) parts.Add(Loc.F("+{0:0}% скорости", m.SpeedBonus * 100f));
            if (Mathf.Abs(m.Evasion) > 0.01f) parts.Add(Loc.F("уклонение {0}{1:0}", (m.Evasion > 0 ? "+" : ""), m.Evasion));
            if (m.Accuracy > 0f) parts.Add(Loc.F("+{0:0} точности", m.Accuracy));
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
        // Чертёж: каркас корабля из ShipHoloStudio, выноски от слотов к точкам на корпусе
        private RawImage _blueprint;
        private RectTransform _callouts;
        private const float ViewTop = 56f, ViewH = 220f, SlotsTop = ViewTop + ViewH + 10f, SidePad = 16f;
        private Button _deleteBtn, _buildBtn, _retrofitBtn;
        private Text _buildText, _retrofitText;
        private int _doctrine;

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
            ShipHoloStudio.Instance?.Hide();
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
            _statusText.text = Loc.T("<color=#F2C747>Есть несохранённые изменения.</color>");
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
            var title = LGBuild.Label(header.transform, Loc.T("КОНСТРУКТОР КОРАБЛЕЙ"), 15, Primary, TextAnchor.MiddleLeft, bold: true);
            title.rectTransform.Stretch(52, 0, 0, 0);
            title.Track(2.2f);
            var hint = LGBuild.Label(header.transform, Loc.T("каждое орудие стреляет своим типом урона · кинетика срывает щиты, энергия жжёт броню, ракеты бьют всё"),
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
            SectionLabel(left, Loc.T("КОРПУС"), 10f);
            _hullList = LGBuild.Rect(left, "HullList");
            _hullList.TopBand(34f, 3 * 70f);
            SectionLabel(left, Loc.T("ПРОЕКТЫ ВЕРФИ"), 34f + 3 * 70f + 8f);
            var listHost = LGBuild.Rect(left, "DesignListHost");
            listHost.Stretch(6, 52, 6, 34f + 3 * 70f + 34f);
            _designList = LGBuild.ScrollList(listHost, 4f, 2);
            var newBtn = LGBuild.Button(left, "NewDesign", UIManager.DS.BtnPrimary, new Color(Neon.r, Neon.g, Neon.b, 0.5f),
                NewDesign, LGIcon.Wrench, Loc.T("НОВЫЙ ПРОЕКТ"), 11);
            var nbRt = (RectTransform)newBtn.transform;
            nbRt.anchorMin = new Vector2(0, 0); nbRt.anchorMax = new Vector2(1, 0); nbRt.pivot = new Vector2(0.5f, 0);
            nbRt.offsetMin = new Vector2(12, 10); nbRt.offsetMax = new Vector2(-12, 44);

            // Центр: имя, силуэт, слоты, выбор модуля
            var nameHost = LGBuild.Rect(center, "NameHost");
            nameHost.TopBand(12f, 38f, 16f, 172f);
            _nameInput = LGControls.Input(nameHost, "", Loc.T("Название проекта"), 40);
            _nameInput.onValueChanged.AddListener(v =>
            {
                if (_draft == null) return;
                _draft.Name = string.IsNullOrWhiteSpace(v) ? _draft.Name : v.Trim();
                _statusText.text = Loc.T("<color=#F2C747>Есть несохранённые изменения.</color>");
            });

            // Автопроект: лучшие изученные модули под выбранную доктрину (каждое нажатие — следующая доктрина)
            var auto = LGBuild.Button(center, "AutoDesign", new Color(0.24f, 0.18f, 0.40f), new Color(0.72f, 0.60f, 1f), AutoDesign, LGIcon.Dice, Loc.T("АВТОПРОЕКТ"), 11);
            ((RectTransform)auto.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16f, -12f), new Vector2(148f, 38f));
            TooltipHelper.Attach(auto.gameObject, Loc.T("<b>Автопроект</b>\nСобрать корабль из лучших изученных модулей. Каждое нажатие — другая доктрина: ") +
                                                  Loc.T("сбалансированная, против щитов (кинетика и ракеты), против брони (энергия и ракеты)."));

            // Чертёжная сетка: мелкие клетки по 16, крупные по 64
            var grid = LGBuild.Panel(center, "BlueprintGrid", new Color(0.35f, 0.85f, 0.95f, 1f));
            grid.sprite = GridSprite();
            grid.type = Image.Type.Tiled;
            grid.raycastTarget = false;
            grid.rectTransform.Stretch(6, 6, 6, 6);
            LG.Ignore(grid.gameObject);

            // Значок корпуса остаётся запасным вариантом, если модель не отрисуется
            _silhouette = LGIcons.Create(center, LGIcon.Corvette, 200f, new Color(Neon.r, Neon.g, Neon.b, 0.06f), "Silhouette");
            _silhouette.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0, -ViewTop - ViewH * 0.5f), new Vector2(200f, 200f));

            var view = LGBuild.Rect(center, "ShipView");
            view.TopBand(ViewTop, ViewH, SidePad, SidePad);
            _blueprint = view.gameObject.AddComponent<RawImage>();
            _blueprint.raycastTarget = false;
            var add = Resources.Load<Shader>("Shaders/UIAdditive");
            if (add != null) _blueprint.material = new Material(add) { name = "BlueprintAdditive" };
            LG.Ignore(view.gameObject);
            var viewTag = LGBuild.Label(center, Loc.T("СХЕМА КОРПУСА"), 9, new Color(Neon.r, Neon.g, Neon.b, 0.55f), TextAnchor.UpperRight, bold: true);
            viewTag.rectTransform.TopBand(ViewTop + 4f, 14f, SidePad, SidePad + 6f);

            _callouts = LGBuild.Rect(center, "Callouts");
            _callouts.Stretch();
            LG.Ignore(_callouts.gameObject);
            _slotArea = LGBuild.Rect(center, "Slots");
            _slotArea.Stretch(SidePad, 12, SidePad, SlotsTop);

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
            _statusText.rectTransform.sizeDelta = new Vector2(298f, 36f);

            // Заложить корабль по этому проекту на главной верфи
            _buildBtn = LGBuild.Button(rt, "BuildShip", UIManager.DS.BtnPrimary, Neon, BuildNow, null, null, 11);
            ((RectTransform)_buildBtn.transform).At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(594f, 12f), new Vector2(198f, 36f));
            var bIc = LGIcons.Create(_buildBtn.transform, LGIcon.Shipyard, 15, Color.white);
            bIc.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(15, 15));
            _buildText = LGBuild.Label(_buildBtn.transform, "", 11, Color.white, TextAnchor.MiddleLeft, bold: true);
            _buildText.rectTransform.Stretch(34, 0, 6, 0);
            TooltipHelper.Attach(_buildBtn.gameObject, Loc.T("<b>Заложить на верфи</b>\nКорабль по этому проекту встанет на стапель главной верфи. Проект должен быть сохранён."));

            // Модернизация устаревших кораблей под основные проекты
            _retrofitBtn = LGBuild.Button(rt, "Retrofit", new Color(0.36f, 0.28f, 0.06f), Gold, RetrofitAll, null, null, 11);
            ((RectTransform)_retrofitBtn.transform).At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(14f, 12f), new Vector2(262f, 36f));
            var rIc = LGIcons.Create(_retrofitBtn.transform, LGIcon.Wrench, 15, Color.white);
            rIc.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(15, 15));
            _retrofitText = LGBuild.Label(_retrofitBtn.transform, "", 11, Color.white, TextAnchor.MiddleLeft, bold: true);
            _retrofitText.rectTransform.Stretch(34, 0, 6, 0);
            TooltipHelper.Attach(_retrofitBtn.gameObject, Loc.T("<b>Модернизировать флот</b>\nКорабли старых проектов получают вооружение и защиту основного проекта своего корпуса. ") +
                                                          Loc.T("Платится разница в сплавах (не меньше 10 за корабль), пока хватает запаса."));

            _deleteBtn = LGBuild.Button(rt, "Delete", UIManager.DS.BtnDanger, UIManager.DS.Red, DeleteDraft, LGIcon.Trash, Loc.T("УДАЛИТЬ"), 11);
            ((RectTransform)_deleteBtn.transform).At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(804f, 12f), new Vector2(118f, 36f));
            var save = LGBuild.Button(rt, "Save", UIManager.DS.BtnSuccess, UIManager.DS.Green, SaveDraft, LGIcon.Save, Loc.T("СОХРАНИТЬ ПРОЕКТ"), 12);
            ((RectTransform)save.transform).At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(930f, 12f), new Vector2(236f, 36f));
            TooltipHelper.Attach(save.gameObject, Loc.T("<b>Сохранить проект</b>\nСохранённый проект становится основным для своего корпуса: ") +
                                                  Loc.T("по нему строит верфь, к нему модернизируются корабли."));

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
            t.Track(1.6f);
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
            RefreshFooter();
        }

        private void RefreshFooter()
        {
            var dm = ShipDesignManager.Instance;
            var eco = EconomyManager.Instance;
            if (_buildBtn != null && _draft != null)
            {
                float cost = FleetManager.ShipAlloyCost(_draft.AlloyCost, 0);
                bool saved = IsSaved(_draft);
                bool can = saved && _draft.IsPowerValid && eco != null && eco.CanAfford(FleetManager.WarshipEnergy, 0f, cost, 0f);
                _buildBtn.interactable = can;
                _buildBtn.GetComponent<Image>().color = can ? UIManager.DS.BtnPrimary : UIManager.DS.BtnDisabled;
                _buildText.text = saved ? Loc.F("ПОСТРОИТЬ · {0:0} спл.", cost) : Loc.T("СНАЧАЛА СОХРАНИТЕ");
                _buildText.color = can ? Color.white : Muted;
            }
            if (_retrofitBtn != null && dm != null)
            {
                var old = dm.OutdatedShips(0);
                float total = 0f;
                foreach (var f in old) total += dm.RetrofitCost(f);
                bool any = old.Count > 0;
                _retrofitBtn.interactable = any;
                _retrofitBtn.GetComponent<Image>().color = any ? new Color(0.36f, 0.28f, 0.06f) : UIManager.DS.BtnDisabled;
                _retrofitText.text = any ? Loc.F("МОДЕРНИЗИРОВАТЬ {0} · {1:0} спл.", old.Count, total) : Loc.T("ФЛОТ СОВРЕМЕННЫЙ");
                _retrofitText.color = any ? Color.white : Muted;
            }
        }

        private static readonly (WeaponDamageType a, WeaponDamageType b)[] Doctrines =
        {
            (WeaponDamageType.Kinetic, WeaponDamageType.Energy),
            (WeaponDamageType.Kinetic, WeaponDamageType.Explosive),
            (WeaponDamageType.Energy, WeaponDamageType.Explosive),
        };

        private static string DoctrineName(int i) => i switch
        {
            1 => Loc.T("Щитолом"),
            2 => Loc.T("Бронебой"),
            _ => Loc.T("Универсал")
        };

        private void AutoDesign()
        {
            var dm = ShipDesignManager.Instance;
            var tm = TechnologyManager.Instance;
            if (dm == null) return;
            var hull = dm.GetHull(_selectedClass);
            if (hull == null) return;
            var doc = Doctrines[_doctrine % Doctrines.Length];
            string name = Loc.F("{0} «{1}»", hull.DisplayName, DoctrineName(_doctrine % Doctrines.Length));
            var d = dm.CreateAutoDesign(_selectedClass, doc.a, doc.b, id => tm == null || (tm.FindTech(id) != null && tm.FindTech(id).IsResearched), name);
            _doctrine++;
            if (d == null) return;
            SetDraft(d);
            if (_picker != null) _picker.SetActive(false);
            SFXManager.Play(Sfx.UiConfirm, 0.6f);
            _statusText.text = Loc.F("<color=#C9B8FF>Автопроект «{0}» собран. Проверьте и сохраните.</color>", DoctrineName((_doctrine - 1) % Doctrines.Length));
            RebuildAll();
            LG.Skin(_root.transform);
        }

        private void BuildNow()
        {
            var dm = ShipDesignManager.Instance;
            var fm = FleetManager.Instance;
            if (dm == null || fm == null || _draft == null) return;
            var saved = dm.GetDesign(_draft.Id);
            if (saved == null) { _statusText.text = Loc.T("<color=#FFCC52>Сначала сохраните проект.</color>"); return; }
            if (fm.BuildShipFromDesign(saved))
            {
                SFXManager.Play(Sfx.BuildQueued);
                _statusText.text = Loc.F("<color=#5CF59A>«{0}» заложен на верфи.</color>", saved.Name);
            }
            else _statusText.text = Loc.T("<color=#FF5A5A>Не хватает ресурсов на постройку.</color>");
            RefreshFooter();
        }

        private void RetrofitAll()
        {
            var dm = ShipDesignManager.Instance;
            if (dm == null) return;
            int n = dm.TryRetrofitAll();
            _statusText.text = n > 0 ? Loc.F("<color=#5CF59A>Модернизировано кораблей: {0}.</color>", n)
                                     : Loc.T("<color=#FFCC52>Не хватает сплавов на модернизацию.</color>");
            if (n > 0) SFXManager.Play(Sfx.UiConfirm);
            RefreshFooter();
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
                    ? Loc.F("оружие {0} · защита {1} · вспом. {2}\nкорпус {3:0} · уклонение {4:0}", h.WeaponSlots, h.DefenseSlots, h.UtilitySlots, h.BaseHull, h.BaseEvasion)
                    : Loc.F("нужна технология «{0}»", TechName(h.Class == ShipClass.Destroyer ? "cns_dst_1" : h.RequiredTechId));
                var s = LGBuild.Label(btn.transform, sub, 10, Muted, TextAnchor.LowerLeft, wrap: true);
                s.rectTransform.Stretch(58, 7, 8, 0);
                TooltipHelper.Attach(btn.gameObject, Loc.F("<b>{0}</b>\nКорпус {1:0} · броня {2:0} · щиты {3:0}\n", h.DisplayName, h.BaseHull, h.BaseArmor, h.BaseShields) +
                    Loc.F("Уклонение {0:0} · скорость ×{1:0.##}\nСодержание {2:0.#} гелия-3/мес · ", h.BaseEvasion, h.BaseSpeed, FleetData.UpkeepFor(FleetType.Military, h.Class)) +
                    Loc.F("флотский лимит {0}", EmpireEconomy.NavalSize(h.Class)));
            }
        }

        private void SelectHull(HullBlueprint h)
        {
            if (!h.IsUnlocked())
            {
                _statusText.text = Loc.F("Корпус «{0}» откроется после технологии «{1}».", h.DisplayName, TechName(h.Class == ShipClass.Destroyer ? "cns_dst_1" : h.RequiredTechId));
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
                var empty = LGBuild.Label(_designList, Loc.T("Проектов нет — соберите первый."), 11, Muted, TextAnchor.MiddleCenter);
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
                Loc.F("урон {0:0}/день · {1:0} сплавов", d.Dps * CombatManager.ShotsPerDay, FleetManager.ShipAlloyCost(d.AlloyCost, 0)), 10, Muted, TextAnchor.LowerLeft);
            sub.rectTransform.Stretch(10, 6, 8, 0);

            if (isPrimary || isDraft)
            {
                var tag = LGBuild.Label(btn.transform, isDraft ? Loc.T("ЧЕРНОВИК") : Loc.T("ВЕРФЬ"), 9, isDraft ? Gold : UIManager.DS.Green, TextAnchor.UpperRight, bold: true);
                tag.rectTransform.Stretch(0, 0, 10, 7);
                if (isPrimary) TooltipHelper.Attach(btn.gameObject, Loc.T("<b>Основной проект</b>\nПо нему строит верфь и модернизируются корабли этого корпуса."));
            }
            else
            {
                var make = LGBuild.Button(btn.transform, "MakePrimary", new Color(0.10f, 0.22f, 0.18f), new Color(0.36f, 0.96f, 0.6f, 0.4f), () =>
                {
                    dm.MakePrimary(d);
                    _statusText.text = Loc.F("«{0}» — теперь основной проект верфи.", d.Name);
                    RebuildDesignList();
                    LG.Skin(_root.transform);
                }, LGIcon.Check, null, 8);
                ((RectTransform)make.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-6, -6), new Vector2(22, 22));
                TooltipHelper.Attach(make.gameObject, Loc.T("<b>Сделать основным</b>\nВерфь начнёт строить этот проект."));
            }
        }

        // ==================== СЛОТЫ ====================

        private void RebuildSlots()
        {
            if (_slotArea == null) return;
            LGBuild.Clear(_slotArea);
            if (_callouts != null) LGBuild.Clear(_callouts);
            if (_draft == null || ShipDesignManager.Instance == null) return;
            _silhouette.sprite = LGIcons.Get(ModuleVisuals.HullIcon(_draft.HullClass));

            // Каркас корабля на чертеже
            var studio = ShipHoloStudio.Ensure();
            studio.Show(_draft.HullClass, new Color(0.40f, 0.92f, 1f, 1f));
            _blueprint.texture = studio.Target;
            _silhouette.enabled = studio.Target == null;

            _calloutN = 0;
            float y = 0f;
            y = SlotGroup(Loc.T("ОРУЖИЕ"), LGIcon.Weapons, _draft.WeaponModuleIds, ModuleSlotType.Weapon, y);
            y = SlotGroup(Loc.T("ЗАЩИТА"), LGIcon.Defense, _draft.DefenseModuleIds, ModuleSlotType.Defense, y);
            SlotGroup(Loc.T("ВСПОМОГАТЕЛЬНЫЕ"), LGIcon.Gear, _draft.UtilityModuleIds, ModuleSlotType.Utility, y);
        }

        private float SlotGroup(string title, LGIcon icon, List<string> slots, ModuleSlotType type, float y)
        {
            var head = LGBuild.Rect(_slotArea, "Group");
            head.TopBand(y, 20f);
            var hic = LGIcons.Create(head, icon, 13f, Gold);
            hic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(2, 0), new Vector2(13, 13));
            var ht = LGBuild.Label(head, Loc.F("{0}  <color=#8AA2A8>· слотов {1}</color>", title, slots.Count), 10, Gold, TextAnchor.MiddleLeft, bold: true);
            ht.rectTransform.Stretch(20, 0, 0, 0);
            y += 22f;

            const float cardH = 40f, gap = 6f;
            for (int i = 0; i < slots.Count; i++)
            {
                int idx = i;
                int col = i % 3, row = i / 3;
                var mod = ShipDesignManager.Instance.GetModule(slots[i]);
                float top = y + row * (cardH + gap);
                int n = ++_calloutN;
                SlotCard(mod, type, col, top, cardH, n, () => OpenPicker(type, idx, slots));
                Callout(type, i, mod, n);
            }
            int rows = (slots.Count + 2) / 3;
            return y + rows * (cardH + gap) + 4f;
        }

        private void SlotCard(ShipModule mod, ModuleSlotType type, int col, float top, float h, int number, System.Action onClick)
        {
            Color tint = mod != null ? ModuleVisuals.Tint(mod) : Muted;
            var btn = LGBuild.Button(_slotArea, "Slot", mod != null ? new Color(tint.r * 0.12f, tint.g * 0.12f, tint.b * 0.12f, 0.95f) : new Color(0.04f, 0.08f, 0.1f, 0.8f),
                new Color(tint.r, tint.g, tint.b, mod != null ? 0.55f : 0.18f), onClick);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = new Vector2(col / 3f, 1f);
            rt.anchorMax = new Vector2((col + 1) / 3f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(col == 0 ? 0f : 4f, -top - h);
            rt.offsetMax = new Vector2(col == 2 ? 0f : -4f, -top);

            // Номер слота — совпадает с меткой на чертеже корпуса
            var num = LGBuild.Label(btn.transform, number.ToString("00"), 9, new Color(tint.r, tint.g, tint.b, mod != null ? 0.9f : 0.5f), TextAnchor.LowerRight, bold: true);
            num.rectTransform.Stretch(0, 4, 7, 0);

            if (mod == null)
            {
                var plus = LGBuild.Label(btn.transform, Loc.T("+  пустой слот"), 10, new Color(Muted.r, Muted.g, Muted.b, 0.75f), TextAnchor.MiddleCenter);
                plus.fontStyle = FontStyle.Italic;
                return;
            }

            var icon = LGIcons.Create(btn.transform, ModuleVisuals.Icon(mod), 20f, tint);
            icon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(20, 20));
            var name = LGBuild.Label(btn.transform, mod.Name, 10, Primary, TextAnchor.UpperLeft, bold: true);
            name.rectTransform.Stretch(34, 0, 30, 5);
            var stats = LGBuild.Label(btn.transform, ModuleVisuals.StatLine(mod), 8, Muted, TextAnchor.LowerLeft);
            stats.rectTransform.Stretch(34, 5, 22, 0);
            if (mod.PowerDraw > 0f)
            {
                var pw = LGBuild.Label(btn.transform, $"<color=#FFCC52>−{mod.PowerDraw:0}</color>", 10, Gold, TextAnchor.UpperRight, bold: true);
                pw.rectTransform.Stretch(0, 0, 10, 7);
            }
            TooltipHelper.Attach(btn.gameObject, ModuleTooltip(mod) + Loc.T("\n\n<color=#8AA2A8>Щёлкните, чтобы заменить.</color>"));
        }

        private int _calloutN;

        /// <summary>
        /// Метка на чертеже, как на технической схеме: точка на корпусе и номер слота рядом с ней (тот же номер — на карточке).
        /// Оружие — в носовой части, защита — в середине, вспомогательные системы — в корме (нос — справа).
        /// Без длинных выносок через весь чертёж — они путались и пересекались.
        /// </summary>
        private void Callout(ModuleSlotType type, int index, ShipModule mod, int number)
        {
            if (_callouts == null) return;
            var area = _callouts.rect;
            float w = area.width > 1f ? area.width : 504f;
            float viewW = w - SidePad * 2f;
            float fx = type switch
            {
                ModuleSlotType.Weapon => 0.66f + index * 0.07f,
                ModuleSlotType.Defense => 0.42f + index * 0.07f,
                _ => 0.16f + index * 0.08f
            };
            bool up = index % 2 == 0;
            float fy = up ? 0.40f : 0.58f;
            var anchor = new Vector2(SidePad + fx * viewW, -(ViewTop + fy * ViewH));

            Color c = mod != null ? ModuleVisuals.Tint(mod) : Muted;
            float a = mod != null ? 0.95f : 0.4f;

            var dot = LGBuild.Panel(_callouts, "Point", new Color(c.r, c.g, c.b, a));
            dot.sprite = DotSprite();
            dot.raycastTarget = false;
            dot.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), anchor, new Vector2(11, 11));

            // Короткая косая выноска к номеру: вверх-вправо у верхних точек, вниз-вправо у нижних
            var dir = new Vector2(1f, up ? 1f : -1f).normalized;
            var tick = LGBuild.Panel(_callouts, "Tick", new Color(c.r, c.g, c.b, a * 0.7f));
            var tr = tick.rectTransform;
            tr.anchorMin = tr.anchorMax = new Vector2(0, 1);
            tr.pivot = new Vector2(0, 0.5f);
            tr.sizeDelta = new Vector2(16f, 1.2f);
            tr.anchoredPosition = anchor + dir * 5f;
            tr.localRotation = Quaternion.Euler(0, 0, up ? 45f : -45f);
            tick.raycastTarget = false;
            LG.Ignore(tick.gameObject);

            var lbl = LGBuild.Label(_callouts, number.ToString("00"), 10, new Color(c.r, c.g, c.b, a), TextAnchor.MiddleLeft, bold: true);
            lbl.rectTransform.At(new Vector2(0, 1), new Vector2(0f, 0.5f), anchor + dir * 19f + new Vector2(2f, 0f), new Vector2(24, 14));
        }

        private static Sprite s_grid, s_dot;

        /// <summary>Клетка чертёжной сетки 64×64: тонкие линии каждые 16 px, линия посильнее по краю клетки.</summary>
        private static Sprite GridSprite()
        {
            if (s_grid != null) return s_grid;
            const int R = 64;
            var tex = new Texture2D(R, R, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            var px = new Color32[R * R];
            for (int y = 0; y < R; y++)
            for (int x = 0; x < R; x++)
            {
                byte a = 0;
                if (x == 0 || y == 0) a = 46;
                else if (x % 16 == 0 || y % 16 == 0) a = 18;
                px[y * R + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            s_grid = Sprite.Create(tex, new Rect(0, 0, R, R), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            s_grid.hideFlags = HideFlags.DontSave;
            return s_grid;
        }

        /// <summary>Точка выноски: яркое ядро и мягкий ореол.</summary>
        private static Sprite DotSprite()
        {
            if (s_dot != null) return s_dot;
            const int R = 32;
            var tex = new Texture2D(R, R, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontSave };
            var px = new Color32[R * R];
            float c = (R - 1) * 0.5f;
            for (int y = 0; y < R; y++)
            for (int x = 0; x < R; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float core = Mathf.Clamp01((0.38f - d) * 12f);
                float ring = Mathf.Exp(-Mathf.Pow((d - 0.72f) / 0.07f, 2f)) * 0.8f;
                px[y * R + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(Mathf.Max(core, ring)) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            s_dot = Sprite.Create(tex, new Rect(0, 0, R, R), new Vector2(0.5f, 0.5f), 100f);
            s_dot.hideFlags = HideFlags.DontSave;
            return s_dot;
        }

        private static string ModuleTooltip(ShipModule m)
        {
            string power = m.PowerProduce > 0f ? Loc.F("Выработка энергии +{0:0}", m.PowerProduce) : m.PowerDraw > 0f ? Loc.F("Потребление энергии {0:0}", m.PowerDraw) : Loc.T("Не требует энергии");
            return $"<b>{m.Name}</b>\n{m.Description}\n<color=#8AA2A8>{ModuleVisuals.StatLine(m)}</color>\n{power} · " +
                   Loc.F("{0:0} сплавов", FleetManager.ShipAlloyCost(m.AlloyCost, 0));
        }

        // ==================== ВЫБОР МОДУЛЯ ====================

        private void OpenPicker(ModuleSlotType type, int index, List<string> slots)
        {
            var dm = ShipDesignManager.Instance;
            if (dm == null || _picker == null) return;
            string typeName = type == ModuleSlotType.Weapon ? Loc.T("ОРУЖИЕ") : type == ModuleSlotType.Defense ? Loc.T("ЗАЩИТА") : Loc.T("ВСПОМОГАТЕЛЬНЫЙ МОДУЛЬ");
            _pickerTitle.text = Loc.F("ВЫБОР МОДУЛЯ · {0} · слот {1}", typeName, index + 1);
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
                }, LGIcon.Close, Loc.T("СНЯТЬ МОДУЛЬ"), 10);
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
            string line = unlocked ? ModuleVisuals.StatLine(m) : Loc.F("<color=#FF8A8A>требуется «{0}»</color>", TechName(m.RequiredTechId));
            var stats = LGBuild.Label(btn.transform, line, 10, Muted, TextAnchor.LowerLeft);
            stats.rectTransform.Stretch(50, 8, 120, 0);

            string power = m.PowerProduce > 0f ? $"<color=#5CF59A>+{m.PowerProduce:0} ⚡</color>"
                         : m.PowerDraw > 0f ? $"<color=#FFCC52>−{m.PowerDraw:0} ⚡</color>" : "<color=#8AA2A8>0 ⚡</color>";
            var right = LGBuild.Label(btn.transform, Loc.F("{0}\n<color=#C9D6DC>{1:0} сплавов</color>", power, FleetManager.ShipAlloyCost(m.AlloyCost, 0)),
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
            // Сравнение с основным проектом корпуса (тем, по которому сейчас строит верфь)
            var cur = ShipDesignManager.Instance.GetLatestDesign(d.HullClass);
            if (cur != null) ShipDesignManager.Instance.Recalc(cur);

            var head = LGBuild.Label(_profile, Loc.T("ТАКТИЧЕСКИЙ ПРОФИЛЬ"), 13, Neon, TextAnchor.UpperLeft, bold: true);
            head.rectTransform.TopBand(y, 20f);
            var note = LGBuild.Label(_profile, cur != null && cur.Id != d.Id ? Loc.F("▲▼ к «{0}»", cur.Name) : Loc.T("с учётом изученных технологий"), 9, Muted, TextAnchor.UpperRight);
            note.rectTransform.TopBand(y + 3f, 16f);
            y += 28f;

            // Защита
            y = Group(Loc.T("ЗАЩИТА"), y);
            y = StatBar(LGIcon.Ship, Loc.T("Корпус"), d.Hull * b.HullMult, 500f, new Color(0.75f, 0.85f, 0.9f), y, cur != null ? cur.Hull * b.HullMult : float.NaN);
            y = StatBar(LGIcon.ArmorPlate, Loc.T("Броня"), d.Armor * b.ArmorMult, 320f, new Color(0.88f, 0.78f, 0.62f), y, cur != null ? cur.Armor * b.ArmorMult : float.NaN);
            y = StatBar(LGIcon.ShieldDome, Loc.T("Щиты"), d.Shields * b.ShieldMult, 300f, new Color(0.45f, 0.76f, 1f), y, cur != null ? cur.Shields * b.ShieldMult : float.NaN);
            y += 6f;

            // Огневая мощь
            float e = d.DpsOf(WeaponDamageType.Energy) * b.DamageMult(WeaponDamageType.Energy) * CombatManager.ShotsPerDay;
            float k = d.DpsOf(WeaponDamageType.Kinetic) * b.DamageMult(WeaponDamageType.Kinetic) * CombatManager.ShotsPerDay;
            float x = d.DpsOf(WeaponDamageType.Explosive) * b.DamageMult(WeaponDamageType.Explosive) * CombatManager.ShotsPerDay;
            float total = e + k + x;
            float curTotal = 0f;
            if (cur != null)
                foreach (WeaponDamageType t in new[] { WeaponDamageType.Energy, WeaponDamageType.Kinetic, WeaponDamageType.Explosive })
                    curTotal += cur.DpsOf(t) * b.DamageMult(t) * CombatManager.ShotsPerDay;
            y = Group(Loc.F("ОГНЕВАЯ МОЩЬ  <color=#E8F4F8>{0:0} урона в день</color>", total) + (cur != null ? Delta(total, curTotal) : ""), y);
            y = StatBar(LGIcon.Laser, Loc.T("Энергия"), e, 160f, ModuleVisuals.TypeColor(WeaponDamageType.Energy), y);
            y = StatBar(LGIcon.Cannon, Loc.T("Кинетика"), k, 160f, ModuleVisuals.TypeColor(WeaponDamageType.Kinetic), y);
            y = StatBar(LGIcon.Missile, Loc.T("Взрыв"), x, 160f, ModuleVisuals.TypeColor(WeaponDamageType.Explosive), y);
            float vsShields = e * CombatManager.EnergyVsShield + k * CombatManager.KineticVsShield + x * CombatManager.ExplosiveVsAll;
            float vsArmor = e * CombatManager.EnergyVsArmor + k * CombatManager.KineticVsArmor + x * CombatManager.ExplosiveVsAll;
            y = Line(Loc.F("По щитам <b>{0:0}</b>/день  ·  по броне <b>{1:0}</b>/день  ·  орудий {2}", vsShields, vsArmor, d.Mounts.Count), y);
            y += 6f;

            // Манёвр
            y = Group(Loc.T("МАНЁВР"), y);
            float evasion = d.Evasion * b.EvasionMult;
            y = Line(Loc.F("Уклонение <b>{0:0}</b>  ·  точность <b>+{1:0}</b>  ·  скорость <b>×{2:0.##}</b>", evasion, d.Accuracy + b.Accuracy, d.Speed * b.HyperlaneSpeed), y);
            y += 6f;

            // Энергия
            y = Group(Loc.T("ЭНЕРГОСЕТЬ"), y);
            float load = d.PowerProduce > 0f ? d.PowerDraw / d.PowerProduce : (d.PowerDraw > 0f ? 2f : 0f);
            Color pc = d.IsPowerValid ? (load > 0.85f ? Gold : UIManager.DS.Green) : UIManager.DS.Red;
            y = StatBarRaw(LGIcon.Reactor, Loc.T("Нагрузка"), $"{d.PowerDraw:0} / {d.PowerProduce:0}", Mathf.Clamp01(load), pc, y);
            y += 6f;

            // Стоимость
            y = Group(Loc.T("СТОИМОСТЬ"), y);
            float build = FleetManager.ShipAlloyCost(d.AlloyCost, 0);
            float days = ConstructionManager.ShipDays(FleetType.Military, d.HullClass) * FactionTraits.BuildTimeMult(0);
            y = Line(Loc.F("Постройка <color=#F2C747><b>{0:0}</b> сплавов</color>", build) +
                     (cur != null ? Delta(build, FleetManager.ShipAlloyCost(cur.AlloyCost, 0), true) : "") +
                     Loc.F("  ·  <b>{0:0}</b> дн. на стапеле", days), y);
            y = Line(Loc.F("<color=#8AA2A8>Купить сразу после закладки ≈ {0:0} гелия-3</color>",
                     Mathf.Max(5f, Mathf.Ceil(days * ConstructionManager.BuyPerDayShip * ConstructionManager.BuyUnstartedMult))), y);
            y = Line(Loc.F("Содержание <color=#FF9A9A><b>{0:0.#}</b> гелия-3/мес</color>  ·  ", FleetData.UpkeepFor(FleetType.Military, d.HullClass)) +
                     Loc.F("флотский лимит {0}", EmpireEconomy.NavalSize(d.HullClass)), y);
            y += 4f;

            // Предупреждения
            string warn = !d.IsPowerValid ? Loc.T("<color=#FF5A5A>Перегрузка энергосети — поставьте реактор или снимите прожорливые модули.</color>")
                        : d.Mounts.Count == 0 ? Loc.T("<color=#FFCC52>Без оружия корабль не участвует в перестрелке.</color>")
                        : vsShields > vsArmor * 1.4f ? Loc.T("<color=#8AA2A8>Уклон в кинетику: силён против щитов, слабее против брони.</color>")
                        : vsArmor > vsShields * 1.4f ? Loc.T("<color=#8AA2A8>Уклон в энергию: силён против брони, щиты отражают часть урона.</color>")
                        : Loc.T("<color=#5CF59A>Сбалансированное вооружение.</color>");
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

        private float StatBar(LGIcon icon, string label, float value, float reference, Color col, float y, float prev = float.NaN)
            => StatBarRaw(icon, label, (value > 0f ? value.ToString("0") : "—") + (float.IsNaN(prev) ? "" : Delta(value, prev)), value / reference, col, y, value <= 0f);

        /// <summary>Разница с основным проектом: «▲12» зелёным, «▼5» красным (для цены — наоборот).</summary>
        private static string Delta(float now, float before, bool lowerIsBetter = false)
        {
            float dv = now - before;
            if (Mathf.Abs(dv) < 0.5f) return "";
            bool good = lowerIsBetter ? dv < 0f : dv > 0f;
            return $" <size=9><color={(good ? "#5CF59A" : "#FF7A7A")}>{(dv > 0f ? "▲" : "▼")}{Mathf.Abs(dv):0}</color></size>";
        }

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
            v.rectTransform.offsetMin = new Vector2(72, 0); v.rectTransform.offsetMax = new Vector2(170, 0);
            var barHost = LGBuild.Rect(row, "BarHost");
            barHost.Stretch(178, 0, 0, 0);
            LGBuild.Bar(barHost, c, Mathf.Clamp01(fill), 7f);
            return y + 25f;
        }

        // ==================== ДЕЙСТВИЯ ====================

        private void NewDesign()
        {
            var dm = ShipDesignManager.Instance;
            if (dm == null) return;
            var d = dm.CreateDraft(_selectedClass);
            d.Name = Loc.F("{0} «Новый {1}»", dm.GetHull(_selectedClass).DisplayName, dm.DesignsFor(_selectedClass).Count + 1);
            SetDraft(d);
            if (_picker != null) _picker.SetActive(false);
            _statusText.text = Loc.T("Новый проект: заполните слоты и сохраните.");
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
                _statusText.text = Loc.T("<color=#FF5A5A>Невозможно сохранить: потребление энергии больше выработки.</color>");
                return;
            }
            // В архив кладём копию: черновик остаётся редактируемым, сохранённый проект не меняется без «Сохранить»
            var copy = Clone(_draft);
            if (!dm.SaveDesign(copy)) return;
            SFXManager.Play(Sfx.UiConfirm, 0.7f);
            _statusText.text = Loc.F("<color=#5CF59A>«{0}» сохранён — верфь строит по этому проекту.</color>", copy.Name);
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
                _statusText.text = Loc.T("Последний проект корпуса удалить нельзя — верфи нужен чертёж.");
                return;
            }
            string name = saved.Name;
            LoadPrimaryDraft();
            _statusText.text = Loc.F("Проект «{0}» удалён.", name);
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
