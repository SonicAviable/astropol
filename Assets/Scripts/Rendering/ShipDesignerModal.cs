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

        /// <summary>Значок модуля: свой рисованный, если есть, иначе общий по типу.</summary>
        public static Image CreateIcon(Transform parent, ShipModule m, float size, Color color)
        {
            var ic = LGIcons.Create(parent, Icon(m), size, color);
            var art = m != null ? LGIcons.ModuleArt(m.Id) : null;
            if (art != null) ic.sprite = art;
            return ic;
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
    /// Проекты кораблей — полноэкранный конструктор по мотивам Master of Orion, в стиле игры:
    ///   • слева — вкладки корпусов и карточки проектов (атака, защита, «верфь» у основного) и «новый проект»;
    ///   • в центре — крупная модель корабля (студийный свет, вращение мышью), над ней — название проекта;
    ///   • справа — атака и защита крупно, технические и боевые характеристики, стоимость
    ///     (▲▼ — разница с основным проектом корпуса), кнопки: сохранить, построить, автопроект, удалить, модернизировать;
    ///   • внизу — энергосеть и ряды слотов: вооружение, защита, спецмодули (клик по слоту — выбор модуля).
    /// Каждое орудие стреляет в бою само, своим типом урона: кинетика срывает щиты, энергия жжёт броню, ракеты бьют всё.
    /// </summary>
    public class ShipDesignerModal : MonoBehaviour
    {
        public static ShipDesignerModal Instance { get; private set; }
        public bool IsOpen => _root != null && LG.IsVisible(_root);

        private Canvas _host;
        private GameObject _root;
        private ShipDesign _draft;
        private ShipClass _selectedClass = ShipClass.Corvette;

        private RectTransform _hullTabs, _designList, _slotArea, _stats;
        private GameObject _picker;
        private Text _pickerTitle, _statusText, _subtitle, _atkValue, _defValue, _energyText, _warnText;
        private Image _energyIcon, _energyRing;
        private GameObject _card;
        private RectTransform _cardBody, _infoTarget;
        // Арсенал (выбор модуля)
        private RectTransform _pickGrid, _pickDetail, _pickFilters;
        private Button _pickInstall, _pickRemove;
        private Text _pickInstallText, _pickSub;
        private ShipModule _pickSel;
        private ModuleSlotType _pickType;
        private int _pickIndex, _pickFilter = -1;
        private List<string> _pickSlots;
        private float _lastTileClick;
        private string _lastTileId;
        private RectTransform _energyBar;
        private InputField _nameInput;
        private RawImage _view;
        private Image _silhouette;
        private Button _deleteBtn, _buildBtn, _retrofitBtn, _saveBtn;
        private Text _buildText, _retrofitText, _autoText, _deleteText;
        private Image _energyFill;
        private Text _energyCap;
        private int _doctrine;

        private const float Pad = 40f, LeftW = 340f, RightW = 360f, SlotsH = 268f;

        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Primary => UIManager.DS.TextPrimary;
        private static Color Neon => UIManager.DS.NeonCyan;
        private static Color Gold => UIManager.DS.Gold;
        private static readonly Color CAtk = new Color(1f, 0.42f, 0.36f);
        private static readonly Color CDef = new Color(0.45f, 0.76f, 1f);
        private static readonly Color CPanel = new Color(0.020f, 0.045f, 0.065f, 0.88f);

        private void Awake() => Instance = this;

        public void BindHost(Canvas modalCanvas) => _host = modalCanvas;

        public void Open()
        {
            if (_host == null)
            {
                var modal = GameObject.Find("ModalCanvas");
                if (modal != null) _host = modal.GetComponent<Canvas>();
            }
            if (_root == null) Build();
            if (_root == null) return;

            var dm = ShipDesignManager.Instance;
            var hull = dm != null ? dm.GetHull(_selectedClass) : null;
            if (hull == null || !hull.IsUnlocked()) _selectedClass = ShipClass.Corvette;
            LoadPrimaryDraft();
            _statusText.text = "";
            if (_picker != null) _picker.SetActive(false);
            RebuildAll();
            UIManager.Instance?.ShowModalDimPublic();
            UIManager.Instance?.SetOverlayMode(true);
            MapModeController.HideGlobal();
            GalaxyMinimap.HideGlobal();
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
            LG.Skin(_root.transform);
        }

        public void Close()
        {
            if (_root == null || !LG.IsVisible(_root)) return;
            if (_picker != null) _picker.SetActive(false);
            HideModuleCard();
            LG.Hide(_root);
            TooltipSystem.Hide();
            ShipHoloStudio.Instance?.Hide();
            UIManager.Instance?.HideModalDimPublic();
            UIManager.Instance?.SetOverlayMode(false);
            MapModeController.ShowGlobal();
            GalaxyMinimap.ShowGlobal();
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_picker != null && _picker.activeSelf) LG.Hide(_picker);
                else Close();
            }
        }

        private void MarkDirty()
        {
            ShipDesignManager.Instance?.Recalc(_draft);
            _statusText.text = Loc.T("<color=#F2C747>Есть несохранённые изменения.</color>");
        }

        // ==================== КАРКАС ЭКРАНА ====================

        private void Build()
        {
            if (_host == null) return;
            var rt = LGBuild.Rect(_host.transform, "ShipDesigner");
            rt.Stretch();
            _root = rt.gameObject;
            _root.AddComponent<CanvasGroup>();
            var bg = _root.AddComponent<Image>();
            bg.color = new Color(0.006f, 0.016f, 0.026f, 1f);
            bg.raycastTarget = true;
            LG.Scrim(_root).FillMultiplier = 2.2f;
            var mo = LG.Motion(_root, LGAppear.Kind.Fade);
            mo.inDuration = 0.35f;
            mo.outDuration = 0.25f;

            // Мягкое свечение за кораблём
            var glow = LGBuild.Panel(rt, "Glow", new Color(0.30f, 0.80f, 0.95f, 0.07f));
            glow.sprite = FactionSelectScreen.RadialSprite();
            glow.raycastTarget = false;
            glow.rectTransform.anchorMin = new Vector2(0.18f, 0.18f);
            glow.rectTransform.anchorMax = new Vector2(0.82f, 1.02f);
            glow.rectTransform.offsetMin = glow.rectTransform.offsetMax = Vector2.zero;
            LG.Ignore(glow.gameObject);

            BuildLeft(rt);
            BuildCenter(rt);
            BuildRight(rt);
            BuildSlots(rt);
            BuildPicker(rt);
            _root.SetActive(false);
        }

        private void BuildLeft(RectTransform rt)
        {
            var title = LGBuild.Label(rt, Loc.T("ПРОЕКТЫ КОРАБЛЕЙ"), 30, Color.white, TextAnchor.UpperLeft, bold: true);
            title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -22), new Vector2(600, 40));
            title.Track(1.6f);
            _subtitle = LGBuild.Label(rt, "", 12, Muted, TextAnchor.UpperLeft);
            _subtitle.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad + 2, -62), new Vector2(LeftW + 200, 18));

            _hullTabs = LGBuild.Rect(rt, "HullTabs");
            _hullTabs.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -90), new Vector2(LeftW, 36));

            var listHost = LGBuild.Rect(rt, "DesignsHost");
            listHost.anchorMin = new Vector2(0, 0); listHost.anchorMax = new Vector2(0, 1);
            listHost.pivot = new Vector2(0, 1);
            listHost.offsetMin = new Vector2(Pad - 6, 24); listHost.offsetMax = new Vector2(Pad + LeftW + 6, -136);
            _designList = LGBuild.ScrollList(listHost, 8f, 6);
            LG.Motion(listHost.gameObject, LGAppear.Kind.SlideLeft).delay = 0.08f;
        }

        private void BuildCenter(RectTransform rt)
        {
            // Название проекта — по центру сверху
            var nameHost = LGBuild.Rect(rt, "NameHost");
            nameHost.At(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-(RightW - LeftW) * 0.5f, -26), new Vector2(520, 40));
            _nameInput = LGControls.Input(nameHost, "", Loc.T("Название проекта"), 40);
            _nameInput.onValueChanged.AddListener(v =>
            {
                if (_draft == null) return;
                _draft.Name = string.IsNullOrWhiteSpace(v) ? _draft.Name : v.Trim();
                _statusText.text = Loc.T("<color=#F2C747>Есть несохранённые изменения.</color>");
            });

            // Модель корабля
            var view = LGBuild.Rect(rt, "ShipView");
            view.anchorMin = new Vector2(0, 0); view.anchorMax = new Vector2(1, 1);
            view.offsetMin = new Vector2(Pad + LeftW + 20, SlotsH + 40); view.offsetMax = new Vector2(-(Pad + RightW + 20), -76);
            _view = view.gameObject.AddComponent<RawImage>();
            _view.raycastTarget = true;
            view.gameObject.AddComponent<ShipSpinCatcher>();
            TooltipHelper.Attach(view.gameObject, Loc.T("Зажмите ЛКМ и ведите мышью — вращение корабля"));
            LG.Ignore(view.gameObject);

            _silhouette = LGIcons.Create(view, LGIcon.Corvette, 220f, new Color(Neon.r, Neon.g, Neon.b, 0.10f), "Silhouette");
            _silhouette.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220, 220));
            _silhouette.raycastTarget = false;

            _statusText = LGBuild.Label(rt, "", 12, Gold, TextAnchor.MiddleCenter, bold: true, wrap: true);
            _statusText.rectTransform.anchorMin = new Vector2(0, 0); _statusText.rectTransform.anchorMax = new Vector2(1, 0);
            _statusText.rectTransform.offsetMin = new Vector2(Pad + LeftW + 20, SlotsH + 12);
            _statusText.rectTransform.offsetMax = new Vector2(-(Pad + RightW + 20), SlotsH + 36);
        }

        private void BuildRight(RectTransform rt)
        {
            var col = LGBuild.Rect(rt, "Right");
            col.anchorMin = new Vector2(1, 0); col.anchorMax = new Vector2(1, 1);
            col.pivot = new Vector2(1, 0.5f);
            col.offsetMin = new Vector2(-Pad - RightW, 24); col.offsetMax = new Vector2(-Pad, -24);
            LG.Motion(col.gameObject, LGAppear.Kind.SlideRight).delay = 0.1f;

            // Атака и защита — крупно
            var head = LGBuild.Rect(col, "AtkDef");
            head.TopBand(0, 76);
            BigStat(head, 0f, Loc.T("АТАКА"), LGIcon.Laser, CAtk, out _atkValue,
                Loc.T("<b>Атака</b>\nСуммарный урон всех орудий за игровой день (с технологиями)."));
            BigStat(head, 0.5f, Loc.T("ЗАЩИТА"), LGIcon.ShieldDome, CDef, out _defValue,
                Loc.T("<b>Защита</b>\nКорпус + броня + щиты (с технологиями), делённые на 10."));
            var sep = LGBuild.Panel(head, "Sep", new Color(1f, 1f, 1f, 0.10f));
            sep.rectTransform.anchorMin = new Vector2(0.5f, 0.15f); sep.rectTransform.anchorMax = new Vector2(0.5f, 0.85f);
            sep.rectTransform.offsetMin = Vector2.zero; sep.rectTransform.offsetMax = new Vector2(1, 0);

            _stats = LGBuild.Rect(col, "Stats");
            _stats.anchorMin = new Vector2(0, 0); _stats.anchorMax = new Vector2(1, 1);
            _stats.offsetMin = new Vector2(0, 300); _stats.offsetMax = new Vector2(0, -88);

            // Действия: главная кнопка и плитки под ней
            _saveBtn = PrimaryButton(col, 214f, SaveDraft);
            TooltipHelper.Attach(_saveBtn.gameObject, Loc.T("<b>Сохранить проект</b>\nСохранённый проект становится основным для своего корпуса: ") +
                                                      Loc.T("по нему строит верфь, к нему модернизируются корабли."));
            _buildBtn = ActionTile(col, 0, 1, Neon, LGIcon.Shipyard, Loc.T("ПОСТРОИТЬ"), BuildNow, out _buildText,
                Loc.T("<b>Заложить на верфи</b>\nКорабль по этому проекту встанет на стапель главной верфи. Проект должен быть сохранён."));
            ActionTile(col, 1, 1, new Color(0.72f, 0.60f, 1f), LGIcon.Dice, Loc.T("АВТОПРОЕКТ"), AutoDesign, out _autoText,
                Loc.T("<b>Автопроект</b>\nСобрать корабль из лучших изученных модулей. Каждое нажатие — другая доктрина: ") +
                Loc.T("сбалансированная, против щитов (кинетика и ракеты), против брони (энергия и ракеты)."));
            _retrofitBtn = ActionTile(col, 0, 0, Gold, LGIcon.Wrench, Loc.T("МОДЕРНИЗАЦИЯ"), RetrofitAll, out _retrofitText,
                Loc.T("<b>Модернизировать флот</b>\nКорабли старых проектов получают вооружение и защиту основного проекта своего корпуса. ") +
                Loc.T("Платится разница в сплавах (не меньше 10 за корабль), пока хватает запаса."));
            _deleteBtn = ActionTile(col, 1, 0, UIManager.DS.Red, LGIcon.Trash, Loc.T("УДАЛИТЬ"), DeleteDraft, out _deleteText,
                Loc.T("<b>Удалить проект</b>\nПоследний проект корпуса удалить нельзя — верфи нужен чертёж."));

            // Вернуться — тонкая текстовая кнопка
            var done = LGBuild.Button(col, "Done", new Color(1f, 1f, 1f, 0.03f), new Color(1f, 1f, 1f, 0.22f), Close, null, null, 12, 22f);
            var drt = (RectTransform)done.transform;
            drt.anchorMin = new Vector2(0, 0); drt.anchorMax = new Vector2(1, 0); drt.pivot = new Vector2(0.5f, 0);
            drt.offsetMin = new Vector2(0, 0); drt.offsetMax = new Vector2(0, 40);
            var dic = LGIcons.Create(done.transform, LGIcon.Back, 13, Muted);
            dic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(1, 0.5f), new Vector2(-62, 0), new Vector2(13, 13));
            var dt = LGBuild.Label(done.transform, Loc.T("ГОТОВО"), 12, Primary, TextAnchor.MiddleLeft, bold: true);
            dt.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(-54, 0), new Vector2(80, 20));
            dt.Track(2f);
            var esc = LGBuild.Label(done.transform, "ESC", 9, Muted, TextAnchor.MiddleCenter, bold: true);
            esc.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(26, 0), new Vector2(32, 18));
            var escBg = LGBuild.Panel(done.transform, "Key", new Color(1f, 1f, 1f, 0.06f));
            escBg.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(26, 0), new Vector2(32, 18));
            escBg.transform.SetAsFirstSibling();
            LG.Chip(escBg.gameObject, new Color(1f, 1f, 1f, 0.25f), 4f);
            TooltipHelper.Attach(done.gameObject, Loc.T("Вернуться на карту  <color=#8AA2A8>(Esc)</color>"));
        }

        /// <summary>Главная кнопка: тёмное стекло, неоновая кромка, светящаяся полоса слева, подпись с разрядкой.</summary>
        private Button PrimaryButton(RectTransform col, float y, System.Action act)
        {
            Color c = new Color(0.36f, 0.96f, 0.68f);
            var b = LGBuild.Button(col, "Primary", new Color(0.04f, 0.16f, 0.12f, 0.92f), new Color(c.r, c.g, c.b, 0.85f), act, null, null, 13, 6f);
            var r = (RectTransform)b.transform;
            r.anchorMin = new Vector2(0, 0); r.anchorMax = new Vector2(1, 0); r.pivot = new Vector2(0.5f, 0);
            r.offsetMin = new Vector2(0, y); r.offsetMax = new Vector2(0, y + 50);
            var bar = LGBuild.Panel(b.transform, "Bar", c);
            bar.rectTransform.anchorMin = new Vector2(0, 0.18f); bar.rectTransform.anchorMax = new Vector2(0, 0.82f);
            bar.rectTransform.offsetMin = new Vector2(8, 0); bar.rectTransform.offsetMax = new Vector2(11, 0);
            LG.Line(bar.gameObject);
            var ic = LGIcons.Create(b.transform, LGIcon.Save, 18, c);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(26, 0), new Vector2(18, 18));
            var t = LGBuild.Label(b.transform, Loc.T("СОХРАНИТЬ ПРОЕКТ"), 14, Color.white, TextAnchor.MiddleLeft, bold: true);
            t.rectTransform.Stretch(58, 0, 12, 0);
            t.Track(2.2f);
            return b;
        }

        /// <summary>Плитка действия 2×2: значок в шестиграннике, заголовок и строка состояния; прозрачная, с тонкой кромкой цвета действия.</summary>
        private static Button ActionTile(RectTransform col, int cx, int cy, Color c, LGIcon icon, string title, System.Action act, out Text sub, string tip)
        {
            var b = LGBuild.Button(col, "Tile", new Color(c.r * 0.08f, c.g * 0.08f, c.b * 0.08f, 0.85f), new Color(c.r, c.g, c.b, 0.45f), act, null, null, 11, 6f);
            var r = (RectTransform)b.transform;
            r.anchorMin = new Vector2(cx * 0.5f, 0); r.anchorMax = new Vector2(cx * 0.5f + 0.5f, 0);
            r.pivot = new Vector2(0.5f, 0);
            float y0 = 52f + cy * 78f;
            r.offsetMin = new Vector2(cx == 0 ? 0 : 4, y0); r.offsetMax = new Vector2(cx == 0 ? -4 : 0, y0 + 72);
            var hex = LGBuild.Panel(b.transform, "Hex", new Color(c.r * 0.25f, c.g * 0.25f, c.b * 0.25f, 1f));
            hex.sprite = HexSprite(true);
            hex.raycastTarget = false;
            hex.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(30, 0), new Vector2(38, 34));
            var hexRim = LGBuild.Panel(b.transform, "HexRim", new Color(c.r, c.g, c.b, 0.8f));
            hexRim.sprite = HexSprite(false);
            hexRim.raycastTarget = false;
            hexRim.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(30, 0), new Vector2(38, 34));
            var ic = LGIcons.Create(b.transform, icon, 16, Color.Lerp(c, Color.white, 0.35f));
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(30, 0), new Vector2(16, 16));
            var t = LGBuild.Label(b.transform, title, 12, Color.white, TextAnchor.LowerLeft, bold: true);
            t.rectTransform.Stretch(56, 36, 6, 10);
            t.Track(1.2f);
            sub = LGBuild.Label(b.transform, "", 10, Color.Lerp(c, Color.white, 0.25f), TextAnchor.UpperLeft, wrap: true);
            sub.rectTransform.Stretch(56, 6, 6, 38);
            TooltipHelper.Attach(b.gameObject, tip);
            return b;
        }

        private static void BigStat(RectTransform parent, float x, string caption, LGIcon icon, Color c, out Text value, string tip)
        {
            var cell = LGBuild.Panel(parent, "Big", new Color(1f, 1f, 1f, 0f), raycast: true);
            cell.rectTransform.anchorMin = new Vector2(x, 0); cell.rectTransform.anchorMax = new Vector2(x + 0.5f, 1);
            cell.rectTransform.offsetMin = cell.rectTransform.offsetMax = Vector2.zero;
            var cap = LGBuild.Label(cell.transform, caption, 13, c, TextAnchor.UpperCenter, bold: true);
            cap.rectTransform.TopBand(4, 18);
            cap.Track(1.6f);
            var ic = LGIcons.Create(cell.transform, icon, 24, c);
            ic.rectTransform.At(new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(-6, 12), new Vector2(24, 24));
            value = LGBuild.Label(cell.transform, "", 26, c, TextAnchor.LowerLeft, bold: true);
            value.rectTransform.anchorMin = new Vector2(0.5f, 0); value.rectTransform.anchorMax = new Vector2(1, 0);
            value.rectTransform.pivot = new Vector2(0, 0);
            value.rectTransform.offsetMin = new Vector2(2, 8); value.rectTransform.offsetMax = new Vector2(0, 42);
            TooltipHelper.Attach(cell.gameObject, tip);
        }

        /// <summary>Низ экрана: энергосеть (круг) и ряды слотов модулей — без панели, прямо на фоне, как в Master of Orion.</summary>
        private void BuildSlots(RectTransform rt)
        {
            var area = LGBuild.Rect(rt, "SlotsPanel");
            area.anchorMin = new Vector2(0, 0); area.anchorMax = new Vector2(1, 0);
            area.pivot = new Vector2(0.5f, 0);
            area.offsetMin = new Vector2(Pad + LeftW + 20, 24); area.offsetMax = new Vector2(-(Pad + RightW + 20), 24 + SlotsH - 24);
            LG.Motion(area.gameObject, LGAppear.Kind.SlideUp).delay = 0.12f;

            // Энергосеть — кольцевой сегментный индикатор: заполнено = потребление, вся окружность = выработка
            var energy = LGBuild.Panel(area, "Energy", new Color(1f, 1f, 1f, 0f), raycast: true);
            energy.rectTransform.anchorMin = new Vector2(0, 0); energy.rectTransform.anchorMax = new Vector2(0, 1);
            energy.rectTransform.offsetMin = new Vector2(0, 0); energy.rectTransform.offsetMax = new Vector2(170, 0);
            const float G = 150f;
            var halo = LGBuild.Panel(energy.transform, "Halo", new Color(Gold.r, Gold.g, Gold.b, 0.07f));
            halo.sprite = FactionSelectScreen.RadialSprite();
            halo.raycastTarget = false;
            halo.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 6), new Vector2(G + 50, G + 50));
            _energyRing = LGBuild.Panel(energy.transform, "Track", new Color(1f, 1f, 1f, 0.07f));
            _energyRing.sprite = RingSprite();
            _energyRing.raycastTarget = false;
            _energyRing.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 6), new Vector2(G, G));
            _energyFill = LGBuild.Panel(energy.transform, "Fill", UIManager.DS.Green);
            _energyFill.sprite = RingSprite();
            _energyFill.raycastTarget = false;
            _energyFill.type = Image.Type.Filled;
            _energyFill.fillMethod = Image.FillMethod.Radial360;
            _energyFill.fillOrigin = (int)Image.Origin360.Bottom;
            _energyFill.fillClockwise = true;
            _energyFill.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 6), new Vector2(G, G));
            var ticks = LGBuild.Panel(energy.transform, "Ticks", new Color(0.006f, 0.016f, 0.026f, 1f));
            ticks.sprite = TickSprite();
            ticks.raycastTarget = false;
            ticks.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 6), new Vector2(G, G));
            _energyIcon = LGIcons.Create(energy.transform, LGIcon.Power, 16, Gold);
            _energyIcon.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 38), new Vector2(16, 16));
            _energyText = LGBuild.Label(energy.transform, "", 30, Color.white, TextAnchor.MiddleCenter, bold: true);
            _energyText.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 8), new Vector2(120, 36));
            _energyCap = LGBuild.Label(energy.transform, "", 10, Muted, TextAnchor.MiddleCenter, bold: true);
            _energyCap.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(130, 14));
            _energyCap.Track(1.4f);
            var barHost = LGBuild.Rect(energy.transform, "Bar");   // запасная полоса (скрыта): кольцо её заменяет
            barHost.At(new Vector2(0.5f, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(1, 1));
            _energyBar = LGBuild.Bar(barHost, Gold, 0f, 1f);
            barHost.gameObject.SetActive(false);
            TooltipHelper.Attach(energy.gameObject, Loc.T("<b>Энергосеть</b>\nПотребление модулей / выработка реакторов. Перегруженный проект нельзя сохранить."));

            var vline = LGBuild.Panel(area, "Split", new Color(1f, 1f, 1f, 0.16f));
            vline.rectTransform.anchorMin = new Vector2(0, 0.06f); vline.rectTransform.anchorMax = new Vector2(0, 0.94f);
            vline.rectTransform.offsetMin = new Vector2(184, 0); vline.rectTransform.offsetMax = new Vector2(185, 0);

            _slotArea = LGBuild.Rect(area, "Slots");
            _slotArea.Stretch(204, 0, 0, 0);

            _warnText = LGBuild.Label(rt, "", 11, Primary, TextAnchor.LowerRight, wrap: true);
            _warnText.rectTransform.anchorMin = new Vector2(0, 0); _warnText.rectTransform.anchorMax = new Vector2(1, 0);
            _warnText.rectTransform.offsetMin = new Vector2(Pad + LeftW + 20, SlotsH + 2);
            _warnText.rectTransform.offsetMax = new Vector2(-(Pad + RightW + 30), SlotsH + 14);

            BuildModuleCard(rt);
        }

        /// <summary>Арсенал — выбор модуля для слота: сетка плиток с фильтрами, справа — подробности и сравнение
        /// с установленным модулем, «Установить» и «Снять». Оформлена общим стилем окон и кнопок игры.</summary>
        private void BuildPicker(RectTransform rt)
        {
            var img = LGBuild.Panel(rt, "Arsenal", new Color(0.016f, 0.034f, 0.050f, 0.97f), raycast: true);
            var r = img.rectTransform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(Pad + LeftW + 20, SlotsH + 8); r.offsetMax = new Vector2(-(Pad + RightW + 20), -84);
            _picker = img.gameObject;
            var win = LG.Glass(_picker);
            win.SetRim(new Color(Neon.r, Neon.g, Neon.b, 0.34f));
            win.Elevation = 0.8f;
            LG.Motion(_picker, LGAppear.Kind.Pop).fromScale = 0.97f;

            // Шапка
            _pickerTitle = LGBuild.Label(img.transform, "", 18, Color.white, TextAnchor.MiddleLeft, bold: true);
            _pickerTitle.rectTransform.TopBand(14, 26, 22, 70);
            _pickerTitle.Track(2.4f);
            _pickSub = LGBuild.Label(img.transform, Loc.T("Выберите модуль — подробности справа. Двойной щелчок — установить."), 12, Muted, TextAnchor.MiddleLeft);
            _pickSub.rectTransform.TopBand(40, 18, 22, 70);
            var close = FlatButton(img.transform, "Close", new Color(1f, 1f, 1f, 0.04f), new Color(1f, 1f, 1f, 0.18f), () => LG.Hide(_picker));
            ((RectTransform)close.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -14), new Vector2(36, 36));
            var cic = LGIcons.Create(close.transform, LGIcon.Close, 14, Primary);
            cic.raycastTarget = false;
            var esc = LGBuild.Label(img.transform, "ESC", 10, Muted, TextAnchor.MiddleRight, bold: true);
            esc.rectTransform.TopBand(22, 20, 22, 60);

            // Фильтры (только для оружия)
            _pickFilters = LGBuild.Rect(img.transform, "Filters");
            _pickFilters.TopBand(68, 28, 22, 392);
            var sep = LGBuild.Panel(img.transform, "Sep", new Color(1f, 1f, 1f, 0.06f));
            sep.raycastTarget = false;
            sep.rectTransform.TopBand(102, 1, 16, 392);

            // Сетка модулей с прокруткой
            var gridHost = LGBuild.Panel(img.transform, "GridHost", new Color(0f, 0f, 0f, 0f), raycast: true).rectTransform;
            gridHost.Stretch(16, 14, 392, 108);
            LG.Ignore(gridHost.gameObject, false);   // прозрачная подложка прокрутки; плитки оформляются как кнопки
            gridHost.gameObject.AddComponent<RectMask2D>();
            var sr = gridHost.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 32f;
            _pickGrid = LGBuild.Rect(gridHost, "Grid");
            _pickGrid.anchorMin = new Vector2(0, 1); _pickGrid.anchorMax = new Vector2(1, 1);
            _pickGrid.pivot = new Vector2(0.5f, 1);
            _pickGrid.offsetMin = Vector2.zero; _pickGrid.offsetMax = Vector2.zero;
            var gl = _pickGrid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(146, 168);
            gl.spacing = new Vector2(12, 12);
            gl.padding = new RectOffset(6, 6, 6, 6);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 4;
            gl.childAlignment = TextAnchor.UpperLeft;
            var fit = _pickGrid.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = _pickGrid;
            sr.viewport = gridHost;

            // Подробности справа
            var pane = LGBuild.Panel(img.transform, "Detail", new Color(0.018f, 0.030f, 0.042f, 1f), raycast: true);
            var pr = pane.rectTransform;
            pr.anchorMin = new Vector2(1, 0); pr.anchorMax = new Vector2(1, 1);
            pr.pivot = new Vector2(1, 0.5f);
            pr.offsetMin = new Vector2(-376, 14); pr.offsetMax = new Vector2(-14, -64);
            LG.Platter(pane.gameObject).SetRim(new Color(1f, 1f, 1f, 0.08f));
            _pickDetail = LGBuild.Rect(pane.transform, "Info");
            _pickDetail.Stretch(18, 116, 18, 16);

            _pickInstall = FlatButton(pane.transform, "Install", new Color(0.04f, 0.20f, 0.15f, 1f), new Color(0.36f, 0.96f, 0.68f, 0.9f), InstallSelected);
            var ir = (RectTransform)_pickInstall.transform;
            ir.anchorMin = new Vector2(0, 0); ir.anchorMax = new Vector2(1, 0); ir.pivot = new Vector2(0.5f, 0);
            ir.offsetMin = new Vector2(18, 62); ir.offsetMax = new Vector2(-18, 108);
            var iic = LGIcons.Create(_pickInstall.transform, LGIcon.Check, 18, new Color(0.36f, 0.96f, 0.68f));
            iic.raycastTarget = false;
            iic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(18, 18));
            _pickInstallText = LGBuild.Label(_pickInstall.transform, Loc.T("УСТАНОВИТЬ"), 14, Color.white, TextAnchor.MiddleCenter, bold: true);
            _pickInstallText.Track(2.4f);

            _pickRemove = FlatButton(pane.transform, "Remove", new Color(0.09f, 0.025f, 0.03f, 1f), new Color(1f, 0.4f, 0.42f, 0.45f), RemoveFromSlot);
            var rr = (RectTransform)_pickRemove.transform;
            rr.anchorMin = new Vector2(0, 0); rr.anchorMax = new Vector2(1, 0); rr.pivot = new Vector2(0.5f, 0);
            rr.offsetMin = new Vector2(18, 16); rr.offsetMax = new Vector2(-18, 52);
            var ric = LGIcons.Create(_pickRemove.transform, LGIcon.Trash, 14, new Color(1f, 0.5f, 0.5f));
            ric.raycastTarget = false;
            ric.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(14, 14));
            var rt2 = LGBuild.Label(_pickRemove.transform, Loc.T("СНЯТЬ МОДУЛЬ"), 11, new Color(1f, 0.62f, 0.62f), TextAnchor.MiddleCenter, bold: true);
            rt2.Track(1.8f);

            _picker.SetActive(false);
        }

        /// <summary>Кнопка арсенала — общий стиль кнопок игры (капсула / плитка с тонкой кромкой); bg задаёт оттенок, rim — кромку.</summary>
        private static Button FlatButton(Transform parent, string name, Color bg, Color rim, System.Action act)
            => LGBuild.Button(parent, name, bg, rim, act);

        // ==================== ПЕРЕСТРОЙКА ====================

        private void RebuildAll()
        {
            RebuildHullTabs();
            RebuildDesignList();
            RebuildSlots();
            RefreshStats();
            RefreshFooter();
        }

        private void RefreshFooter()
        {
            var dm = ShipDesignManager.Instance;
            var eco = EconomyManager.Instance;
            if (_deleteBtn != null)
            {
                bool canDel = dm != null && IsSaved(_draft) && dm.DesignsFor(_draft.HullClass).Count > 1;
                SetTile(_deleteBtn, canDel, UIManager.DS.Red);
                _deleteText.text = canDel ? Loc.T("убрать этот проект") : Loc.T("нельзя: последний проект");
            }
            if (_saveBtn != null && _draft != null)
            {
                bool ok = _draft.IsPowerValid;
                _saveBtn.interactable = ok;
                var img = _saveBtn.GetComponent<Image>();
                img.color = ok ? new Color(0.04f, 0.16f, 0.12f, 0.92f) : new Color(0.06f, 0.07f, 0.08f, 0.9f);
            }
            if (_buildBtn != null && _draft != null)
            {
                float cost = FleetManager.ShipAlloyCost(_draft.AlloyCost, 0);
                bool saved = IsSaved(_draft);
                bool can = saved && _draft.IsPowerValid && eco != null && eco.CanAfford(FleetManager.WarshipEnergy, 0f, cost, 0f);
                SetTile(_buildBtn, can, Neon);
                _buildText.text = !saved ? Loc.T("сначала сохраните") : Loc.F("{0:0} сплавов · {1:0} дн.", cost,
                    ConstructionManager.ShipDays(FleetType.Military, _draft.HullClass) * FactionTraits.BuildTimeMult(0));
            }
            if (_retrofitBtn != null && dm != null)
            {
                var old = dm.OutdatedShips(0);
                float total = 0f;
                foreach (var f in old) total += dm.RetrofitCost(f);
                bool any = old.Count > 0;
                SetTile(_retrofitBtn, any, Gold);
                _retrofitText.text = any ? Loc.F("{0} кораблей · {1:0} сплавов", old.Count, total) : Loc.T("флот современный");
            }
            if (_autoText != null) _autoText.text = Loc.F("следующая: {0}", DoctrineName(_doctrine % Doctrines.Length).ToLower());
        }

        private static void SetTile(Button b, bool on, Color c)
        {
            b.interactable = on;
            b.GetComponent<Image>().color = on ? new Color(c.r * 0.08f, c.g * 0.08f, c.b * 0.08f, 0.85f) : new Color(0.03f, 0.04f, 0.05f, 0.6f);
            foreach (var t in b.GetComponentsInChildren<Text>(true))
                t.color = new Color(t.color.r, t.color.g, t.color.b, on ? 1f : 0.45f);
        }

        private void RebuildHullTabs()
        {
            var dm = ShipDesignManager.Instance;
            if (_hullTabs == null || dm == null) return;
            LGBuild.Clear(_hullTabs);
            int n = dm.Hulls.Count;
            for (int i = 0; i < n; i++)
            {
                var h = dm.Hulls[i];
                bool unlocked = h.IsUnlocked();
                bool selected = h.Class == _selectedClass;
                var btn = LGBuild.Button(_hullTabs, "Hull_" + h.Class,
                    selected ? new Color(0.10f, 0.36f, 0.42f) : UIManager.DS.BtnNeutral,
                    selected ? new Color(Neon.r, Neon.g, Neon.b, 0.8f) : new Color(Neon.r, Neon.g, Neon.b, 0.2f),
                    () => SelectHull(h), null, null, 11, 8f);
                var r = (RectTransform)btn.transform;
                r.anchorMin = new Vector2(i / (float)n, 0); r.anchorMax = new Vector2((i + 1) / (float)n, 1);
                r.offsetMin = new Vector2(i == 0 ? 0 : 3, 0); r.offsetMax = new Vector2(i == n - 1 ? 0 : -3, 0);
                var ic = LGIcons.Create(btn.transform, unlocked ? ModuleVisuals.HullIcon(h.Class) : LGIcon.Lock, 16, unlocked ? (selected ? Neon : Primary) : Muted);
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(16, 16));
                var t = LGBuild.Label(btn.transform, h.DisplayName.ToUpper(), 11, unlocked ? Color.white : Muted, TextAnchor.MiddleCenter, bold: true);
                t.rectTransform.Stretch(26, 0, 4, 0);
                TooltipHelper.Attach(btn.gameObject, unlocked
                    ? Loc.F("<b>{0}</b>\nСлотов: оружие {1} · защита {2} · спецмодули {3}\n", h.DisplayName, h.WeaponSlots, h.DefenseSlots, h.UtilitySlots) +
                      Loc.F("Корпус {0:0} · броня {1:0} · щиты {2:0} · уклонение {3:0}\n", h.BaseHull, h.BaseArmor, h.BaseShields, h.BaseEvasion) +
                      Loc.F("Содержание {0:0.#} гелия-3/мес · флотский лимит {1}", FleetData.UpkeepFor(FleetType.Military, h.Class), EmpireEconomy.NavalSize(h.Class))
                    : Loc.F("<b>{0}</b>\nОткроется после технологии «{1}».", h.DisplayName, TechName(h.Class == ShipClass.Destroyer ? "cns_dst_1" : h.RequiredTechId)));
            }
            var designs = dm.DesignsFor(_selectedClass);
            var primary = dm.GetLatestDesign(_selectedClass);
            _subtitle.text = Loc.F("Проектов корпуса: {0}", designs.Count) + (primary != null ? Loc.F("  ·  верфь строит «{0}»", primary.Name) : "");
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
            SFXManager.Play(Sfx.UiTab, 0.6f);
            RebuildAll();
            LG.Skin(_root.transform);
        }

        /// <summary>Атака (урон за день) и защита (корпус + броня + щиты / 10) — с технологиями игрока.</summary>
        private static void AtkDef(ShipDesign d, out float atk, out float def)
        {
            var b = EmpireBonuses.For(0);
            atk = 0f;
            foreach (WeaponDamageType t in new[] { WeaponDamageType.Energy, WeaponDamageType.Kinetic, WeaponDamageType.Explosive })
                atk += d.DpsOf(t) * b.DamageMult(t) * CombatManager.ShotsPerDay;
            def = (d.Hull * b.HullMult + d.Armor * b.ArmorMult + d.Shields * b.ShieldMult) / 10f;
        }

        private void RebuildDesignList()
        {
            var dm = ShipDesignManager.Instance;
            if (_designList == null || dm == null) return;
            LGBuild.Clear(_designList);
            var primary = dm.GetLatestDesign(_selectedClass);
            var designs = dm.DesignsFor(_selectedClass);

            if (_draft != null && !IsSaved(_draft)) DesignCard(_draft, false, true);
            for (int i = designs.Count - 1; i >= 0; i--) DesignCard(designs[i], designs[i] == primary, false);

            // «Новый проект»
            var add = LGBuild.Button(_designList, "New", new Color(0.02f, 0.06f, 0.08f, 0.9f), new Color(Neon.r, Neon.g, Neon.b, 0.25f), NewDesign, null, null, 12, 12f);
            LGBuild.Height(add.gameObject, 88);
            var tile = LGBuild.Panel(add.transform, "Tile", new Color(0.04f, 0.14f, 0.15f));
            tile.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(64, 64));
            LG.Chip(tile.gameObject, new Color(Neon.r, Neon.g, Neon.b, 0.4f), 10f);
            var plus = LGBuild.Label(tile.transform, "+", 34, Neon, TextAnchor.MiddleCenter, bold: true);
            var nt = LGBuild.Label(add.transform, Loc.T("НОВЫЙ"), 15, Neon, TextAnchor.UpperLeft, bold: true);
            nt.rectTransform.Stretch(90, 0, 10, 16);
            var ns = LGBuild.Label(add.transform, Loc.T("Нажмите, чтобы создать проект"), 11, Muted, TextAnchor.UpperLeft, wrap: true);
            ns.rectTransform.Stretch(90, 0, 10, 40);
        }

        private void DesignCard(ShipDesign d, bool isPrimary, bool isDraft)
        {
            var dm = ShipDesignManager.Instance;
            dm.Recalc(d);
            bool editing = _draft != null && _draft.Id == d.Id;
            var btn = LGBuild.Button(_designList, "Design", editing ? new Color(0.05f, 0.20f, 0.20f, 0.95f) : new Color(0.02f, 0.06f, 0.08f, 0.9f),
                new Color(Neon.r, Neon.g, Neon.b, editing ? 0.7f : 0.18f), () =>
                {
                    if (isDraft || editing) return;
                    SetDraft(Clone(d));
                    _statusText.text = "";
                    if (_picker != null) _picker.SetActive(false);
                    SFXManager.Play(Sfx.UiClick, 0.5f);
                    RebuildAll();
                    LG.Skin(_root.transform);
                }, null, null, 12, 12f);
            LGBuild.Height(btn.gameObject, 96);

            if (editing)
            {
                var line = LGBuild.Panel(btn.transform, "Underline", Neon);
                line.rectTransform.anchorMin = new Vector2(0, 0); line.rectTransform.anchorMax = new Vector2(1, 0);
                line.rectTransform.offsetMin = new Vector2(6, 0); line.rectTransform.offsetMax = new Vector2(-6, 3);
                LG.Ignore(line.gameObject);
            }

            var tile = LGBuild.Panel(btn.transform, "Tile", new Color(0.04f, 0.10f, 0.13f));
            tile.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(64, 64));
            LG.Chip(tile.gameObject, new Color(Neon.r, Neon.g, Neon.b, 0.3f), 10f);
            var ic = LGIcons.Create(tile.transform, ModuleVisuals.HullIcon(d.HullClass), 40, editing ? Neon : Primary);

            var name = LGBuild.Label(btn.transform, d.Name.ToUpper(), 13, Color.white, TextAnchor.UpperLeft, bold: true);
            name.rectTransform.Stretch(90, 0, isPrimary || isDraft ? 70 : 36, 12);
            var hull = dm.GetHull(d.HullClass);
            var sub = LGBuild.Label(btn.transform, hull != null ? hull.DisplayName : "", 11, Muted, TextAnchor.UpperLeft);
            sub.rectTransform.Stretch(90, 0, 10, 34);

            AtkDef(d, out float atk, out float def);
            var a = LGIcons.Create(btn.transform, LGIcon.Laser, 14, CAtk);
            a.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(90, 14), new Vector2(14, 14));
            var at = LGBuild.Label(btn.transform, $"{atk:0}", 15, CAtk, TextAnchor.LowerLeft, bold: true);
            at.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(108, 10), new Vector2(60, 22));
            var di = LGIcons.Create(btn.transform, LGIcon.ShieldDome, 14, CDef);
            di.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(162, 14), new Vector2(14, 14));
            var dt = LGBuild.Label(btn.transform, $"{def:0}", 15, CDef, TextAnchor.LowerLeft, bold: true);
            dt.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(180, 10), new Vector2(60, 22));
            var cost = LGBuild.Label(btn.transform, Loc.F("{0:0} спл.", FleetManager.ShipAlloyCost(d.AlloyCost, 0)), 11, Muted, TextAnchor.LowerRight, bold: true);
            cost.rectTransform.Stretch(0, 12, 12, 0);

            if (isPrimary || isDraft)
            {
                var tag = LGBuild.Label(btn.transform, isDraft ? Loc.T("ЧЕРНОВИК") : Loc.T("ВЕРФЬ"), 10, isDraft ? Gold : UIManager.DS.Green, TextAnchor.UpperRight, bold: true);
                tag.rectTransform.Stretch(0, 0, 12, 12);
                if (isPrimary) TooltipHelper.Attach(btn.gameObject, Loc.T("<b>Основной проект</b>\nПо нему строит верфь и модернизируются корабли этого корпуса."));
            }
            else
            {
                var make = LGBuild.Button(btn.transform, "MakePrimary", new Color(0.10f, 0.22f, 0.18f), new Color(0.36f, 0.96f, 0.6f, 0.4f), () =>
                {
                    dm.MakePrimary(d);
                    _statusText.text = Loc.F("«{0}» — теперь основной проект верфи.", d.Name);
                    RebuildAll();
                    LG.Skin(_root.transform);
                }, LGIcon.Check, null, 8);
                ((RectTransform)make.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-8, -8), new Vector2(24, 24));
                TooltipHelper.Attach(make.gameObject, Loc.T("<b>Сделать основным</b>\nВерфь начнёт строить этот проект."));
            }
        }

        // ==================== СЛОТЫ ====================

        private void RebuildSlots()
        {
            if (_slotArea == null) return;
            LGBuild.Clear(_slotArea);
            if (_draft == null || ShipDesignManager.Instance == null) return;

            // Модель корабля по центру
            var studio = ShipHoloStudio.Ensure();
            studio.ShowReal(_draft.HullClass);
            _view.texture = studio.Target;
            _silhouette.sprite = LGIcons.Get(ModuleVisuals.HullIcon(_draft.HullClass));
            _silhouette.enabled = studio.Target == null;

            SlotRow(0, Loc.T("ВООРУЖЕНИЕ"), _draft.WeaponModuleIds, ModuleSlotType.Weapon);
            SlotRow(1, Loc.T("ЗАЩИТА"), _draft.DefenseModuleIds, ModuleSlotType.Defense);
            SlotRow(2, Loc.T("СПЕЦМОДУЛИ"), _draft.UtilityModuleIds, ModuleSlotType.Utility);
            RefreshEnergy();
        }

        private void SlotRow(int row, string title, List<string> slots, ModuleSlotType type)
        {
            var r = LGBuild.Rect(_slotArea, "Row" + row);
            r.anchorMin = new Vector2(0, 1f - (row + 1) / 3f); r.anchorMax = new Vector2(1, 1f - row / 3f);
            r.offsetMin = r.offsetMax = Vector2.zero;
            Color rowCol = type == ModuleSlotType.Weapon ? new Color(1f, 0.52f, 0.42f) : type == ModuleSlotType.Defense ? new Color(0.45f, 0.76f, 1f) : Gold;
            LGIcon rowIcon = type == ModuleSlotType.Weapon ? LGIcon.Weapons : type == ModuleSlotType.Defense ? LGIcon.Defense : LGIcon.Gear;

            // Подпись ряда: значок в шестиграннике, название, число слотов
            var hx = LGBuild.Panel(r, "RowHex", new Color(rowCol.r, rowCol.g, rowCol.b, 0.85f));
            hx.sprite = HexSprite(false);
            hx.raycastTarget = false;
            hx.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(16, 0), new Vector2(30, 26));
            var hi = LGIcons.Create(r, rowIcon, 13, rowCol);
            hi.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(16, 0), new Vector2(13, 13));
            int filled = 0;
            foreach (var id in slots) if (!string.IsNullOrEmpty(id)) filled++;
            var t = LGBuild.Label(r, title, 13, Color.white, TextAnchor.LowerLeft, bold: true);
            t.rectTransform.anchorMin = new Vector2(0, 0.5f); t.rectTransform.anchorMax = new Vector2(0, 0.5f);
            t.rectTransform.pivot = new Vector2(0, 0); t.rectTransform.anchoredPosition = new Vector2(38, -1); t.rectTransform.sizeDelta = new Vector2(140, 18);
            t.Track(1.4f);
            var cnt = LGBuild.Label(r, Loc.F("{0} из {1}", filled, slots.Count), 10, rowCol, TextAnchor.UpperLeft, bold: true);
            cnt.rectTransform.anchorMin = new Vector2(0, 0.5f); cnt.rectTransform.anchorMax = new Vector2(0, 0.5f);
            cnt.rectTransform.pivot = new Vector2(0, 1); cnt.rectTransform.anchoredPosition = new Vector2(38, -3); cnt.rectTransform.sizeDelta = new Vector2(140, 14);

            const float W = 70f, H = 62f, G = 26f, X0 = 196f;
            // Дорожка схемы, соединяющая гнёзда ряда
            if (slots.Count > 0)
            {
                var trace = LGBuild.Panel(r, "Trace", new Color(rowCol.r, rowCol.g, rowCol.b, 0.22f));
                trace.raycastTarget = false;
                float x1 = X0 + (slots.Count - 1) * (W + G) + W * 0.5f;
                trace.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(150, 0), new Vector2(x1 - 150, 1.5f));
                LG.Ignore(trace.gameObject);
            }

            for (int i = 0; i < slots.Count; i++)
            {
                int idx = i;
                var mod = ShipDesignManager.Instance.GetModule(slots[i]);
                Color tint = mod != null ? ModuleVisuals.Tint(mod) : rowCol;
                float cx = X0 + i * (W + G) + W * 0.5f;

                var glow = LGBuild.Panel(r, "Glow", new Color(tint.r, tint.g, tint.b, mod != null ? 0.16f : 0f));
                glow.sprite = FactionSelectScreen.RadialSprite();
                glow.raycastTarget = false;
                glow.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(cx, 0), new Vector2(W + 40, W + 40));
                LG.Ignore(glow.gameObject);

                var fill = LGBuild.Panel(r, "Socket", mod != null ? new Color(tint.r * 0.16f, tint.g * 0.16f, tint.b * 0.16f, 0.96f) : new Color(0.02f, 0.04f, 0.05f, 0.9f), raycast: true);
                fill.sprite = HexSprite(true);
                fill.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(cx, 0), new Vector2(W, H));
                var rim = LGBuild.Panel(r, "SocketRim", new Color(tint.r, tint.g, tint.b, mod != null ? 0.85f : 0.30f));
                rim.sprite = HexSprite(false);
                rim.raycastTarget = false;
                rim.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(cx, 0), new Vector2(W, H));

                var btn = fill.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => OpenPicker(type, idx, slots));
                var hover = fill.gameObject.AddComponent<UIHover>();

                if (mod != null)
                {
                    var ic = ModuleVisuals.CreateIcon(r, mod, 30, Color.Lerp(tint, Color.white, 0.3f));
                    ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(cx, 0), new Vector2(30, 30));
                    ic.raycastTarget = false;
                    var m = mod;
                    hover.Enter = () => { glow.color = new Color(tint.r, tint.g, tint.b, 0.34f); rim.color = Color.Lerp(tint, Color.white, 0.4f); ShowModuleCard(m, (RectTransform)fill.transform, true); };
                    hover.Exit = () => { glow.color = new Color(tint.r, tint.g, tint.b, 0.16f); rim.color = new Color(tint.r, tint.g, tint.b, 0.85f); HideModuleCard(); };

                    // Энергия модуля — маленький ярлык у нижней грани
                    if (mod.PowerDraw > 0f || mod.PowerProduce > 0f)
                    {
                        bool prod = mod.PowerProduce > 0f;
                        Color pc = prod ? UIManager.DS.Green : Gold;
                        var chip = LGBuild.Panel(r, "Pw", new Color(0.01f, 0.02f, 0.03f, 0.95f));
                        chip.raycastTarget = false;
                        chip.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(cx, -H * 0.5f - 2), new Vector2(38, 15));
                        LG.Chip(chip.gameObject, new Color(pc.r, pc.g, pc.b, 0.6f), 7f);
                        var pw = LGBuild.Label(chip.transform, prod ? $"+{mod.PowerProduce:0}" : $"−{mod.PowerDraw:0}", 10, pc, TextAnchor.MiddleCenter, bold: true);
                    }
                }
                else
                {
                    var plus = LGBuild.Label(r, "+", 22, new Color(rowCol.r, rowCol.g, rowCol.b, 0.45f), TextAnchor.MiddleCenter, bold: true);
                    plus.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(cx, 1), new Vector2(30, 30));
                    hover.Enter = () => { rim.color = new Color(rowCol.r, rowCol.g, rowCol.b, 0.9f); plus.color = rowCol; };
                    hover.Exit = () => { rim.color = new Color(rowCol.r, rowCol.g, rowCol.b, 0.30f); plus.color = new Color(rowCol.r, rowCol.g, rowCol.b, 0.45f); };
                    TooltipHelper.Attach(fill.gameObject, Loc.T("<b>Пустой слот</b>\nЩёлкните, чтобы поставить модуль."));
                }
            }
        }

        private void RefreshEnergy()
        {
            if (_draft == null || _energyText == null) return;
            var d = _draft;
            float load = d.PowerProduce > 0f ? d.PowerDraw / d.PowerProduce : (d.PowerDraw > 0f ? 2f : 0f);
            Color pc = d.IsPowerValid ? (load > 0.85f ? Gold : UIManager.DS.Green) : UIManager.DS.Red;
            _energyText.text = $"{d.PowerDraw:0}<size=16><color=#8AA2A8>/{d.PowerProduce:0}</color></size>";
            _energyFill.fillAmount = Mathf.Clamp01(load);
            _energyFill.color = pc;
            _energyIcon.color = pc;
            _energyCap.text = !d.IsPowerValid ? Loc.T("ПЕРЕГРУЗКА") : Loc.F("ЗАПАС {0:0}", d.PowerProduce - d.PowerDraw);
            _energyCap.color = d.IsPowerValid ? Muted : UIManager.DS.Red;
        }

        private void AfterSlotChange()
        {
            LG.Hide(_picker);
            HideModuleCard();
            MarkDirty();
            RebuildSlots();
            RebuildDesignList();
            RefreshStats();
            RefreshFooter();
            LG.Skin(_root.transform);
        }

        // ==================== ХАРАКТЕРИСТИКИ ====================

        private void RefreshStats()
        {
            if (_stats == null || _draft == null) return;
            LGBuild.Clear(_stats);
            var dm = ShipDesignManager.Instance;
            var d = _draft;
            dm.Recalc(d);
            var b = EmpireBonuses.For(0);
            var cur = dm.GetLatestDesign(d.HullClass);
            if (cur != null) dm.Recalc(cur);
            bool cmp = cur != null && cur.Id != d.Id;

            AtkDef(d, out float atk, out float def);
            float catk = 0f, cdef = 0f;
            if (cmp) AtkDef(cur, out catk, out cdef);
            _atkValue.text = $"{atk:0}" + (cmp ? Delta(atk, catk) : "");
            _defValue.text = $"{def:0}" + (cmp ? Delta(def, cdef) : "");

            float y = 0f;
            y = Section(Loc.T("ТЕХНИЧЕСКИЕ ХАРАКТЕРИСТИКИ"), y);
            y = Row(Loc.T("Корпус"), d.Hull * b.HullMult, cmp ? cur.Hull * b.HullMult : float.NaN, "0", y);
            y = Row(Loc.T("Броня"), d.Armor * b.ArmorMult, cmp ? cur.Armor * b.ArmorMult : float.NaN, "0", y);
            y = Row(Loc.T("Щиты"), d.Shields * b.ShieldMult, cmp ? cur.Shields * b.ShieldMult : float.NaN, "0", y);
            y = Row(Loc.T("Уклонение"), d.Evasion * b.EvasionMult, cmp ? cur.Evasion * b.EvasionMult : float.NaN, "0", y);
            y = RowText(Loc.T("Скорость"), $"×{d.Speed * b.HyperlaneSpeed:0.##}", y);
            y += 8f;

            float e = d.DpsOf(WeaponDamageType.Energy) * b.DamageMult(WeaponDamageType.Energy) * CombatManager.ShotsPerDay;
            float k = d.DpsOf(WeaponDamageType.Kinetic) * b.DamageMult(WeaponDamageType.Kinetic) * CombatManager.ShotsPerDay;
            float x = d.DpsOf(WeaponDamageType.Explosive) * b.DamageMult(WeaponDamageType.Explosive) * CombatManager.ShotsPerDay;
            float vsShields = e * CombatManager.EnergyVsShield + k * CombatManager.KineticVsShield + x * CombatManager.ExplosiveVsAll;
            float vsArmor = e * CombatManager.EnergyVsArmor + k * CombatManager.KineticVsArmor + x * CombatManager.ExplosiveVsAll;
            y = Section(Loc.T("БОЕВЫЕ ХАРАКТЕРИСТИКИ"), y);
            y = Row(Loc.T("Урон в день"), atk, cmp ? catk : float.NaN, "0.#", y);
            y = Row(Loc.T("По щитам"), vsShields, float.NaN, "0.#", y);
            y = Row(Loc.T("По броне"), vsArmor, float.NaN, "0.#", y);
            y = RowText(Loc.T("Точность"), $"+{d.Accuracy + b.Accuracy:0}", y);
            y = RowText(Loc.T("Орудий"), d.Mounts.Count > 0 ? Loc.F("{0} · {1}", d.Mounts.Count, ShipDesignManager.DamageTypeName(d.PrimaryWeapon).ToLower()) : "—", y);
            y += 8f;

            float build = FleetManager.ShipAlloyCost(d.AlloyCost, 0);
            float days = ConstructionManager.ShipDays(FleetType.Military, d.HullClass) * FactionTraits.BuildTimeMult(0);
            y = Section(Loc.T("СТОИМОСТЬ"), y);
            y = Row(Loc.T("Сплавы"), build, cmp ? FleetManager.ShipAlloyCost(cur.AlloyCost, 0) : float.NaN, "0", y, true);
            y = RowText(Loc.T("Гелий-3 на закладку"), $"{FleetManager.WarshipEnergy:0}", y);
            y = RowText(Loc.T("Срок постройки"), Loc.F("{0:0} дн.", days), y);
            y = RowText(Loc.T("Содержание"), Loc.F("{0:0.#} гелия-3/мес", FleetData.UpkeepFor(FleetType.Military, d.HullClass)), y);
            y = RowText(Loc.T("Флотский лимит"), EmpireEconomy.NavalSize(d.HullClass).ToString(), y);

            _warnText.text = !d.IsPowerValid ? Loc.T("<color=#FF5A5A>Перегрузка энергосети — поставьте реактор или снимите прожорливые модули.</color>")
                           : d.Mounts.Count == 0 ? Loc.T("<color=#FFCC52>Без оружия корабль не участвует в перестрелке.</color>")
                           : vsShields > vsArmor * 1.4f ? Loc.T("<color=#8AA2A8>Уклон в кинетику: силён против щитов, слабее против брони.</color>")
                           : vsArmor > vsShields * 1.4f ? Loc.T("<color=#8AA2A8>Уклон в энергию: силён против брони, щиты отражают часть урона.</color>")
                           : Loc.T("<color=#5CF59A>Сбалансированное вооружение.</color>");
            if (cmp) _warnText.text += Loc.F("  <color=#8AA2A8>· ▲▼ — к «{0}»</color>", cur.Name);
        }

        private float Section(string title, float y)
        {
            var t = LGBuild.Label(_stats, title, 12, Color.white, TextAnchor.LowerLeft, bold: true);
            t.rectTransform.TopBand(y, 20f);
            t.Track(1.0f);
            var line = LGBuild.Panel(_stats, "Line", new Color(Neon.r, Neon.g, Neon.b, 0.25f));
            line.rectTransform.TopBand(y + 22f, 1f);
            return y + 28f;
        }

        private float Row(string label, float value, float prev, string fmt, float y, bool lowerIsBetter = false)
            => RowText(label, (value > 0f ? value.ToString(fmt) : "—") + (float.IsNaN(prev) ? "" : Delta(value, prev, lowerIsBetter)), y);

        private float RowText(string label, string value, float y)
        {
            var row = LGBuild.Rect(_stats, "Row");
            row.TopBand(y, 24f);
            var l = LGBuild.Label(row, label, 12, Muted, TextAnchor.MiddleLeft);
            var v = LGBuild.Label(row, value, 14, Color.white, TextAnchor.MiddleRight, bold: true);
            var line = LGBuild.Panel(row, "Line", new Color(1f, 1f, 1f, 0.05f));
            line.rectTransform.anchorMin = new Vector2(0, 0); line.rectTransform.anchorMax = new Vector2(1, 0);
            line.rectTransform.offsetMin = Vector2.zero; line.rectTransform.offsetMax = new Vector2(0, 1);
            return y + 26f;
        }

        // ==================== КАРТОЧКА МОДУЛЯ ====================

        private void BuildModuleCard(RectTransform rt)
        {
            var card = LGBuild.Panel(rt, "ModuleCard", new Color(0.016f, 0.034f, 0.050f, 0.97f));
            card.raycastTarget = false;
            card.rectTransform.anchorMin = card.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            card.rectTransform.pivot = new Vector2(0, 0);
            card.rectTransform.sizeDelta = new Vector2(380, 300);
            var cfx = LG.Glass(card.gameObject);
            cfx.SetRim(new Color(Neon.r, Neon.g, Neon.b, 0.34f));
            cfx.Elevation = 0.9f;
            _card = card.gameObject;
            _cardBody = LGBuild.Rect(card.transform, "Body");
            _cardBody.Stretch(16, 14, 16, 14);
            _card.SetActive(false);
        }

        private static string ModuleKind(ShipModule m)
        {
            if (m.SlotType == ModuleSlotType.Weapon)
                return m.DamageType == WeaponDamageType.Energy ? Loc.T("Энергетическое вооружение")
                     : m.DamageType == WeaponDamageType.Kinetic ? Loc.T("Кинетическое вооружение") : Loc.T("Ракетное вооружение");
            if (m.SlotType == ModuleSlotType.Defense) return m.Shields > 0f ? Loc.T("Защитное поле") : Loc.T("Бронирование");
            if (m.PowerProduce > 0f) return Loc.T("Энергетическая установка");
            if (m.Accuracy > 0f) return Loc.T("Система наведения");
            return Loc.T("Двигательная установка");
        }

        /// <summary>Подробная карточка модуля рядом с элементом (как всплывающее окно модуля в Master of Orion).</summary>
        private void ShowModuleCard(ShipModule m, RectTransform near, bool above)
        {
            if (_card == null || m == null || near == null) return;
            if (_picker != null && _picker.activeSelf) return;   // в арсенале подробности — в его правой панели
            TooltipSystem.Hide();
            float y = FillModuleInfo(_cardBody, m, null);

            // Размер и место: справа от элемента (или слева, если не помещается), над слотом — выше рядов
            var rt = (RectTransform)_card.transform;
            var root = (RectTransform)_root.transform;
            float h = y + 28f;
            rt.sizeDelta = new Vector2(380, h);
            var corners = new Vector3[4];
            near.GetWorldCorners(corners);
            Vector3 tl = root.InverseTransformPoint(corners[1]);
            Vector3 tr = root.InverseTransformPoint(corners[2]);
            Vector3 br = root.InverseTransformPoint(corners[3]);
            float x = tr.x + 12f;
            float maxX = root.rect.xMax - (Pad + RightW + 20f) - 384f;   // не заходить на колонку характеристик
            if (x > maxX) x = tl.x - 12f - 380f;
            float yb = above ? tr.y + 8f : br.y;
            yb = Mathf.Clamp(yb, root.rect.yMin + 10f, root.rect.yMax - h - 10f);
            rt.anchoredPosition = new Vector2(x, yb);
            _card.SetActive(true);
            _card.transform.SetAsLastSibling();
        }

        /// <summary>Описание модуля в указанную область: шапка, описание, характеристики, эффективность, энергия и цена,
        /// а если задан compare — разница с ним. Возвращает занятую высоту.</summary>
        private float FillModuleInfo(RectTransform body, ShipModule m, ShipModule compare)
        {
            _infoTarget = body;
            LGBuild.Clear(body);
            Color tint = ModuleVisuals.Tint(m);
            bool unlocked = m.IsUnlocked();
            float y = 0f;

            var disc = LGBuild.Panel(body, "Disc", tint);
            disc.sprite = DiscSprite();
            disc.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(34, 34));
            ModuleVisuals.CreateIcon(disc.transform, m, 20, Color.white);
            var name = LGBuild.Label(body, m.Name.ToUpper(), 17, Color.white, TextAnchor.MiddleLeft, bold: true);
            name.rectTransform.TopBand(0, 34, 44, 0);
            y += 40f;
            var kind = LGBuild.Label(body, ModuleKind(m), 12, tint, TextAnchor.UpperLeft, bold: true);
            kind.rectTransform.TopBand(y, 18);
            y += 22f;
            if (!string.IsNullOrEmpty(m.Description))
            {
                var desc = LGBuild.Label(body, m.Description, 12, Primary, TextAnchor.UpperLeft, wrap: true);
                desc.rectTransform.TopBand(y, 48);
                desc.lineSpacing = 1.1f;
                y += Mathf.Clamp(desc.preferredHeight, 18f, 60f) + 6f;
            }
            y = CardLine(y, null);

            var b = EmpireBonuses.For(0);
            if (m.SlotType == ModuleSlotType.Weapon && m.Damage > 0f)
            {
                float perDay = m.FireRate * CombatManager.ShotsPerDay;
                y = CardStat(y, LGIcon.Target, new Color(1f, 0.45f, 0.4f), Loc.T("Урон за выстрел"), $"{m.Damage * b.DamageMult(m.DamageType):0.#}");
                y = CardStat(y, LGIcon.Clock, new Color(0.7f, 0.65f, 1f), Loc.T("Выстрелов в день"), $"{perDay:0.##}");
                y = CardStat(y, ModuleVisuals.TypeIcon(m.DamageType), ModuleVisuals.TypeColor(m.DamageType), Loc.T("Урон в день"), $"{DamagePerDay(m):0.#}");
                if (m.Accuracy > 0f) y = CardStat(y, LGIcon.Target, new Color(1f, 0.56f, 0.78f), Loc.T("Точность"), $"+{m.Accuracy:0}");
                y += 6f;
                var eff = LGBuild.Label(body, Loc.T("ЭФФЕКТИВНОСТЬ"), 12, Color.white, TextAnchor.LowerLeft, bold: true);
                eff.rectTransform.TopBand(y, 18);
                y += 22f;
                float vsS = m.DamageType == WeaponDamageType.Energy ? CombatManager.EnergyVsShield : m.DamageType == WeaponDamageType.Kinetic ? CombatManager.KineticVsShield : CombatManager.ExplosiveVsAll;
                float vsA = m.DamageType == WeaponDamageType.Energy ? CombatManager.EnergyVsArmor : m.DamageType == WeaponDamageType.Kinetic ? CombatManager.KineticVsArmor : CombatManager.ExplosiveVsAll;
                y = CardBar(y, Loc.T("по щитам"), vsS, new Color(0.45f, 0.76f, 1f));
                y = CardBar(y, Loc.T("по броне"), vsA, new Color(0.88f, 0.78f, 0.62f));
            }
            if (m.Hull > 0f) y = CardStat(y, LGIcon.Ship, Primary, Loc.T("Корпус"), $"+{m.Hull * b.HullMult:0}");
            if (m.Armor > 0f) y = CardStat(y, LGIcon.ArmorPlate, new Color(0.88f, 0.78f, 0.62f), Loc.T("Броня"), $"+{m.Armor * b.ArmorMult:0}");
            if (m.Shields > 0f) y = CardStat(y, LGIcon.ShieldDome, new Color(0.45f, 0.76f, 1f), Loc.T("Щиты"), $"+{m.Shields * b.ShieldMult:0}");
            if (Mathf.Abs(m.Evasion) > 0.01f) y = CardStat(y, LGIcon.Thruster, Neon, Loc.T("Уклонение"), $"{(m.Evasion > 0 ? "+" : "")}{m.Evasion:0}");
            if (m.SpeedBonus > 0f) y = CardStat(y, LGIcon.Propulsion, Neon, Loc.T("Скорость"), $"+{m.SpeedBonus * 100f:0}%");
            if (m.SlotType != ModuleSlotType.Weapon && m.Accuracy > 0f) y = CardStat(y, LGIcon.Target, new Color(1f, 0.56f, 0.78f), Loc.T("Точность"), $"+{m.Accuracy:0}");
            y = CardLine(y + 2f, null);
            y = CardStat(y, LGIcon.Power, Gold, m.PowerProduce > 0f ? Loc.T("Выработка энергии") : Loc.T("Потребление энергии"),
                m.PowerProduce > 0f ? $"+{m.PowerProduce:0}" : m.PowerDraw > 0f ? $"{m.PowerDraw:0}" : "0");
            y = CardStat(y, LGIcon.Alloys, new Color(0.95f, 0.62f, 0.36f), Loc.T("Цена"), Loc.F("{0:0} сплавов", FleetManager.ShipAlloyCost(m.AlloyCost, 0)));

            if (compare != null && compare.Id != m.Id)
            {
                y += 8f;
                var ct = LGBuild.Label(body, Loc.F("ВМЕСТО «{0}»", compare.Name.ToUpper()), 11, Muted, TextAnchor.LowerLeft, bold: true);
                ct.rectTransform.TopBand(y, 18);
                y += 22f;
                if (m.SlotType == ModuleSlotType.Weapon) y = CardDelta(y, Loc.T("Урон в день"), DamagePerDay(m) - DamagePerDay(compare), false);
                if (m.Armor > 0f || compare.Armor > 0f) y = CardDelta(y, Loc.T("Броня"), (m.Armor - compare.Armor) * b.ArmorMult, false);
                if (m.Shields > 0f || compare.Shields > 0f) y = CardDelta(y, Loc.T("Щиты"), (m.Shields - compare.Shields) * b.ShieldMult, false);
                float pm = m.PowerProduce - m.PowerDraw, pc = compare.PowerProduce - compare.PowerDraw;
                y = CardDelta(y, Loc.T("Запас энергии"), pm - pc, false);
                y = CardDelta(y, Loc.T("Цена"), FleetManager.ShipAlloyCost(m.AlloyCost, 0) - FleetManager.ShipAlloyCost(compare.AlloyCost, 0), true);
            }
            if (!unlocked)
            {
                var req = LGBuild.Label(body, Loc.F("Требуется технология «{0}»", TechName(m.RequiredTechId)), 12, new Color(1f, 0.5f, 0.5f), TextAnchor.UpperLeft, wrap: true);
                req.rectTransform.TopBand(y + 4f, 32);
                y += 36f;
            }
            return y;
        }

        private static float DamagePerDay(ShipModule m)
            => m == null || m.SlotType != ModuleSlotType.Weapon ? 0f : m.Dps * CombatManager.ShotsPerDay * EmpireBonuses.For(0).DamageMult(m.DamageType);

        private float CardDelta(float y, string label, float dv, bool lowerIsBetter)
        {
            if (Mathf.Abs(dv) < 0.05f) return y;
            bool good = lowerIsBetter ? dv < 0f : dv > 0f;
            string col = good ? "#5CF59A" : "#FF7A7A";
            return CardStatRaw(y, label, $"<color={col}>{(dv > 0f ? "▲ +" : "▼ ")}{dv:0.#}</color>");
        }

        private float CardStatRaw(float y, string label, string value)
        {
            var row = LGBuild.Rect(_infoTarget, "Delta");
            row.TopBand(y, 20f);
            LGBuild.Label(row, label, 12, Muted, TextAnchor.MiddleLeft);
            LGBuild.Label(row, value, 12, Color.white, TextAnchor.MiddleRight, bold: true);
            return y + 21f;
        }

        private void HideModuleCard()
        {
            if (_card != null) _card.SetActive(false);
        }

        private float CardLine(float y, string _)
        {
            var line = LGBuild.Panel(_infoTarget, "Line", new Color(1f, 1f, 1f, 0.10f));
            line.rectTransform.TopBand(y + 3f, 1f);
            return y + 8f;
        }

        private float CardStat(float y, LGIcon icon, Color c, string label, string value)
        {
            var row = LGBuild.Rect(_infoTarget, "Stat");
            row.TopBand(y, 22f);
            var ic = LGIcons.Create(row, icon, 13, c);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(13, 13));
            var l = LGBuild.Label(row, label, 13, Primary, TextAnchor.MiddleLeft);
            l.rectTransform.Stretch(20, 0, 0, 0);
            var v = LGBuild.Label(row, value, 13, c == Primary ? Color.white : Color.Lerp(c, Color.white, 0.3f), TextAnchor.MiddleRight, bold: true);
            return y + 23f;
        }

        private float CardBar(float y, string label, float mult, Color c)
        {
            var row = LGBuild.Rect(_infoTarget, "Eff");
            row.TopBand(y, 20f);
            var l = LGBuild.Label(row, label, 11, Muted, TextAnchor.MiddleLeft);
            l.rectTransform.anchorMin = new Vector2(0, 0); l.rectTransform.anchorMax = new Vector2(0, 1);
            l.rectTransform.offsetMin = Vector2.zero; l.rectTransform.offsetMax = new Vector2(90, 0);
            var host = LGBuild.Rect(row, "Bar");
            host.Stretch(92, 0, 56, 0);
            Color bc = mult >= 1.2f ? UIManager.DS.Green : mult >= 0.95f ? Gold : new Color(1f, 0.4f, 0.35f);
            LGBuild.Bar(host, bc, Mathf.Clamp01(mult / 1.6f), 10f);
            var v = LGBuild.Label(row, $"×{mult:0.##}", 12, bc, TextAnchor.MiddleRight, bold: true);
            return y + 22f;
        }

        // ==================== ПРОЦЕДУРНЫЕ СПРАЙТЫ ====================

        private static Sprite s_disc, s_hexFill, s_hexRim, s_gaugeRing, s_ticks;

        /// <summary>Шестигранник с плоским верхом: заливка или тонкая кромка.</summary>
        private static Sprite HexSprite(bool fill)
        {
            if (fill && s_hexFill != null) return s_hexFill;
            if (!fill && s_hexRim != null) return s_hexRim;
            const int W = 128, H = 112;
            var sp = MakeSprite(W, H, (u, v) =>
            {
                // Нормированное расстояние до шестигранника с плоским верхом (1 — грань): спрайт по высоте = 0,866 ширины
                float x = Mathf.Abs(u), y = Mathf.Abs(v) * 0.866f;
                float d = Mathf.Max(Mathf.Abs(v), x + y * 0.577f);
                if (fill) return Mathf.Clamp01((0.96f - d) * 60f);
                return Mathf.Clamp01(1f - Mathf.Abs(d - 0.93f) * 60f);
            }, fill ? "HexFill" : "HexRim");
            if (fill) s_hexFill = sp; else s_hexRim = sp;
            return sp;
        }

        /// <summary>Толстое кольцо для индикатора энергии.</summary>
        private static Sprite RingSprite()
        {
            if (s_gaugeRing != null) return s_gaugeRing;
            const int R = 256;
            s_gaugeRing = MakeSprite(R, R, (u, v) =>
            {
                float d = Mathf.Sqrt(u * u + v * v);
                return Mathf.Clamp01((0.98f - d) * 80f) * Mathf.Clamp01((d - 0.82f) * 80f);
            }, "GaugeRing");
            return s_gaugeRing;
        }

        /// <summary>Прорези между сегментами кольца (24 сегмента) — рисуются цветом фона поверх кольца.</summary>
        private static Sprite TickSprite()
        {
            if (s_ticks != null) return s_ticks;
            const int R = 256;
            s_ticks = MakeSprite(R, R, (u, v) =>
            {
                float d = Mathf.Sqrt(u * u + v * v);
                if (d < 0.78f || d > 1f) return 0f;
                float ang = Mathf.Atan2(v, u) * Mathf.Rad2Deg + 360f;
                float seg = Mathf.Repeat(ang, 15f);
                return Mathf.Clamp01(1f - Mathf.Min(seg, 15f - seg) * 0.9f);
            }, "GaugeTicks");
            return s_ticks;
        }

        private static Sprite MakeSprite(int w, int h, System.Func<float, float, float> alpha, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave, name = name };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;
                px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(u, v)) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            var sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sp.hideFlags = HideFlags.DontSave;
            return sp;
        }

        /// <summary>Сплошной круг с мягким краем.</summary>
        private static Sprite DiscSprite()
        {
            if (s_disc != null) return s_disc;
            const int R = 128;
            s_disc = MakeSprite(R, R, (u, v) => Mathf.Clamp01((1f - Mathf.Sqrt(u * u + v * v)) * R * 0.5f), "UiDisc");
            return s_disc;
        }

        /// <summary>Разница с основным проектом: «▲12» зелёным, «▼5» красным (для цены — наоборот).</summary>
        private static string Delta(float now, float before, bool lowerIsBetter = false)
        {
            float dv = now - before;
            if (Mathf.Abs(dv) < 0.5f) return "";
            bool good = lowerIsBetter ? dv < 0f : dv > 0f;
            return $" <size=10><color={(good ? "#5CF59A" : "#FF7A7A")}>{(dv > 0f ? "▲" : "▼")}{Mathf.Abs(dv):0}</color></size>";
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

        private static readonly WeaponDamageType[] FilterTypes = { WeaponDamageType.Energy, WeaponDamageType.Kinetic, WeaponDamageType.Explosive };

        private void OpenPicker(ModuleSlotType type, int index, List<string> slots)
        {
            var dm = ShipDesignManager.Instance;
            if (dm == null || _picker == null) return;
            HideModuleCard();
            if (type != _pickType || type != ModuleSlotType.Weapon) _pickFilter = -1;
            _pickType = type; _pickIndex = index; _pickSlots = slots;
            string typeName = type == ModuleSlotType.Weapon ? Loc.T("ОРУЖИЕ") : type == ModuleSlotType.Defense ? Loc.T("ЗАЩИТА") : Loc.T("ВСПОМОГАТЕЛЬНЫЙ МОДУЛЬ");
            _pickerTitle.text = Loc.F("АРСЕНАЛ · {0} · СЛОТ {1}", typeName, index + 1);
            _pickSel = null;
            string cur = slots[index];
            if (!string.IsNullOrEmpty(cur) && dm.Modules.TryGetValue(cur, out var installed)) _pickSel = installed;
            _lastTileId = null;

            RebuildFilters();
            RebuildArsenal();
            var sr = _pickGrid.GetComponentInParent<ScrollRect>();
            if (sr != null) sr.verticalNormalizedPosition = 1f;
            LG.Show(_picker);
            _picker.transform.SetAsLastSibling();
            SFXManager.Play(Sfx.UiClick, 0.5f);
        }

        private void RebuildFilters()
        {
            LGBuild.Clear(_pickFilters);
            if (_pickType != ModuleSlotType.Weapon) return;
            string[] names = { Loc.T("ВСЕ"), Loc.T("ЭНЕРГИЯ"), Loc.T("КИНЕТИКА"), Loc.T("РАКЕТЫ") };
            float x = 0f;
            for (int i = 0; i < names.Length; i++)
            {
                int f = i - 1;
                bool on = _pickFilter == f;
                Color c = f < 0 ? Neon : ModuleVisuals.TypeColor(FilterTypes[f]);
                float w = (f < 0 ? 36f : 54f) + names[i].Length * 10f;
                var b = FlatButton(_pickFilters, "Filter", on ? new Color(c.r * 0.20f, c.g * 0.20f, c.b * 0.20f, 1f) : new Color(1f, 1f, 1f, 0.03f),
                    new Color(c.r, c.g, c.b, on ? 0.9f : 0.22f), () =>
                    {
                        if (_pickFilter == f) return;
                        _pickFilter = f;
                        SFXManager.Play(Sfx.UiTab, 0.5f);
                        RebuildFilters();
                        RebuildArsenal();
                    });
                ((RectTransform)b.transform).At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(w, 28));
                var t = LGBuild.Label(b.transform, names[i], 11, on ? Color.white : Muted, TextAnchor.MiddleCenter, bold: true);
                t.Track(1.4f);
                if (f >= 0)
                {
                    t.rectTransform.Stretch(18, 0, 0, 0);
                    var ic = LGIcons.Create(b.transform, ModuleVisuals.TypeIcon(FilterTypes[f]), 13, on ? c : new Color(c.r, c.g, c.b, 0.6f));
                    ic.raycastTarget = false;
                    ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(13, 13));
                }
                x += w + 8f;
            }
        }

        private void RebuildArsenal()
        {
            var dm = ShipDesignManager.Instance;
            if (dm == null || _pickGrid == null || _pickSlots == null) return;
            LGBuild.Clear(_pickGrid);
            var all = new List<ShipModule>();
            foreach (var m in dm.Modules.Values)
                if (m.SlotType == _pickType && (_pickFilter < 0 || m.DamageType == FilterTypes[_pickFilter])) all.Add(m);
            all.Sort((a, b) =>
            {
                bool ua = a.IsUnlocked(), ub = b.IsUnlocked();
                if (ua != ub) return ua ? -1 : 1;
                return a.AlloyCost.CompareTo(b.AlloyCost);
            });
            string cur = _pickSlots[_pickIndex];
            foreach (var m in all) ArsenalTile(m, m.Id == cur);
            RefreshArsenalDetail();
        }

        /// <summary>Плитка модуля: метка типа, крупный значок со свечением, название, энергия и цена; выбранная — в цвете модуля.</summary>
        private void ArsenalTile(ShipModule m, bool installed)
        {
            bool unlocked = m.IsUnlocked();
            bool sel = _pickSel != null && _pickSel.Id == m.Id;
            Color tint = ModuleVisuals.Tint(m);
            Color bg = sel ? new Color(tint.r * 0.15f, tint.g * 0.15f, tint.b * 0.15f, 1f) : new Color(0.022f, 0.034f, 0.046f, 1f);
            Color rim = sel ? new Color(tint.r, tint.g, tint.b, 0.95f) : new Color(1f, 1f, 1f, 0.08f);
            var b = FlatButton(_pickGrid, "Tile", bg, rim, () => OnTileClick(m));
            var t = b.transform;

            var strip = LGBuild.Panel(t, "Strip", new Color(tint.r, tint.g, tint.b, unlocked ? (sel ? 1f : 0.5f) : 0.12f));
            strip.raycastTarget = false;
            strip.rectTransform.TopBand(1, 2, 12, 12);

            var glow = LGBuild.Panel(t, "Glow", new Color(tint.r, tint.g, tint.b, unlocked ? (sel ? 0.42f : 0.18f) : 0.04f));
            glow.sprite = FactionSelectScreen.RadialSprite();
            glow.raycastTarget = false;
            glow.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -64), new Vector2(124, 124));
            var ic = ModuleVisuals.CreateIcon(t, m, 46, unlocked ? Color.Lerp(tint, Color.white, 0.25f) : new Color(Muted.r, Muted.g, Muted.b, 0.35f));
            ic.raycastTarget = false;
            ic.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -64), new Vector2(46, 46));

            var tag = LGBuild.Label(t, TypeTag(m), 9, new Color(tint.r, tint.g, tint.b, unlocked ? 0.95f : 0.4f), TextAnchor.MiddleLeft, bold: true);
            tag.rectTransform.TopBand(9, 14, 10, 28);
            tag.Track(1.6f);
            if (installed || !unlocked)
            {
                var badge = LGIcons.Create(t, installed ? LGIcon.Check : LGIcon.Lock, 13, installed ? new Color(0.36f, 0.96f, 0.68f) : new Color(1f, 0.5f, 0.5f, 0.85f));
                badge.raycastTarget = false;
                badge.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-9, -9), new Vector2(13, 13));
            }

            var name = LGBuild.Label(t, m.Name, 12, unlocked ? Color.white : Muted, TextAnchor.UpperCenter, bold: true, wrap: true);
            name.rectTransform.Stretch(8, 36, 8, 102);
            name.lineSpacing = 0.95f;
            if (installed)
            {
                var it = LGBuild.Label(t, Loc.T("УСТАНОВЛЕН"), 9, new Color(0.36f, 0.96f, 0.68f), TextAnchor.MiddleCenter, bold: true);
                it.rectTransform.anchorMin = new Vector2(0, 0); it.rectTransform.anchorMax = new Vector2(1, 0);
                it.rectTransform.offsetMin = new Vector2(8, 36); it.rectTransform.offsetMax = new Vector2(-8, 50);
                it.Track(1.6f);
            }

            var line = LGBuild.Panel(t, "Sep", new Color(1f, 1f, 1f, 0.06f));
            line.raycastTarget = false;
            line.rectTransform.anchorMin = new Vector2(0, 0); line.rectTransform.anchorMax = new Vector2(1, 0);
            line.rectTransform.offsetMin = new Vector2(8, 32); line.rectTransform.offsetMax = new Vector2(-8, 33);

            Color pc = m.PowerProduce > 0f ? new Color(0.36f, 0.96f, 0.6f) : m.PowerDraw > 0f ? Gold : Muted;
            var pic = LGIcons.Create(t, LGIcon.Power, 12, pc);
            pic.raycastTarget = false;
            pic.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(10, 17), new Vector2(12, 12));
            var pw = LGBuild.Label(t, m.PowerProduce > 0f ? $"+{m.PowerProduce:0}" : m.PowerDraw > 0f ? $"−{m.PowerDraw:0}" : "0", 12, pc, TextAnchor.MiddleLeft, bold: true);
            pw.rectTransform.anchorMin = new Vector2(0, 0); pw.rectTransform.anchorMax = new Vector2(0.5f, 0);
            pw.rectTransform.offsetMin = new Vector2(26, 6); pw.rectTransform.offsetMax = new Vector2(0, 28);
            Color cc = new Color(0.95f, 0.62f, 0.36f);
            var aic = LGIcons.Create(t, LGIcon.Alloys, 12, cc);
            aic.raycastTarget = false;
            aic.rectTransform.At(new Vector2(1, 0), new Vector2(1, 0.5f), new Vector2(-10, 17), new Vector2(12, 12));
            var cost = LGBuild.Label(t, $"{FleetManager.ShipAlloyCost(m.AlloyCost, 0):0}", 12, Primary, TextAnchor.MiddleRight, bold: true);
            cost.rectTransform.anchorMin = new Vector2(0.5f, 0); cost.rectTransform.anchorMax = new Vector2(1, 0);
            cost.rectTransform.offsetMin = new Vector2(0, 6); cost.rectTransform.offsetMax = new Vector2(-26, 28);
        }

        private static string TypeTag(ShipModule m)
        {
            if (m.SlotType == ModuleSlotType.Weapon)
                return m.DamageType == WeaponDamageType.Energy ? Loc.T("ЭНЕРГИЯ") : m.DamageType == WeaponDamageType.Kinetic ? Loc.T("КИНЕТИКА") : Loc.T("РАКЕТЫ");
            if (m.SlotType == ModuleSlotType.Defense) return m.Shields > 0f ? Loc.T("ЩИТ") : Loc.T("БРОНЯ");
            if (m.PowerProduce > 0f) return Loc.T("РЕАКТОР");
            if (m.Accuracy > 0f) return Loc.T("НАВЕДЕНИЕ");
            return Loc.T("ДВИГАТЕЛЬ");
        }

        /// <summary>Щелчок — выбрать (подробности справа), двойной щелчок — сразу установить.</summary>
        private void OnTileClick(ShipModule m)
        {
            bool dbl = _lastTileId == m.Id && Time.unscaledTime - _lastTileClick < 0.35f;
            _lastTileClick = Time.unscaledTime;
            _lastTileId = m.Id;
            _pickSel = m;
            if (dbl && m.IsUnlocked()) { InstallSelected(); return; }
            SFXManager.Play(Sfx.UiClick, 0.4f);
            RebuildArsenal();
        }

        private void RefreshArsenalDetail()
        {
            var dm = ShipDesignManager.Instance;
            string cur = _pickSlots != null ? _pickSlots[_pickIndex] : null;
            ShipModule installed = null;
            if (!string.IsNullOrEmpty(cur) && dm != null) dm.Modules.TryGetValue(cur, out installed);
            if (_pickSel == null)
            {
                LGBuild.Clear(_pickDetail);
                LGBuild.Label(_pickDetail, Loc.T("Слот пуст. Выберите модуль слева."), 13, Muted, TextAnchor.MiddleCenter, wrap: true);
            }
            else FillModuleInfo(_pickDetail, _pickSel, installed);

            bool unlocked = _pickSel != null && _pickSel.IsUnlocked();
            bool isInst = _pickSel != null && installed != null && installed.Id == _pickSel.Id;
            bool can = unlocked && !isInst;
            _pickInstall.interactable = can;
            _pickInstall.GetComponent<Image>().color = can ? new Color(0.04f, 0.20f, 0.15f, 1f) : new Color(0.03f, 0.04f, 0.05f, 1f);
            _pickInstall.GetComponent<LiquidGlassEffect>()?.SetRim(can ? new Color(0.36f, 0.96f, 0.68f, 0.9f) : new Color(1f, 1f, 1f, 0.10f));
            _pickInstallText.text = _pickSel == null || can ? Loc.T("УСТАНОВИТЬ") : isInst ? Loc.T("УЖЕ УСТАНОВЛЕН") : Loc.T("НЕ ИЗУЧЕН");
            _pickInstallText.color = can ? Color.white : Muted;
            _pickRemove.gameObject.SetActive(installed != null);
        }

        private void InstallSelected()
        {
            if (_pickSel == null || !_pickSel.IsUnlocked() || _pickSlots == null) return;
            _pickSlots[_pickIndex] = _pickSel.Id;
            SFXManager.Play(Sfx.UiConfirm, 0.6f);
            AfterSlotChange();
        }

        private void RemoveFromSlot()
        {
            if (_pickSlots == null) return;
            _pickSlots[_pickIndex] = "";
            SFXManager.Play(Sfx.UiClick, 0.5f);
            AfterSlotChange();
        }

        private static string ModuleTooltip(ShipModule m)
        {
            string power = m.PowerProduce > 0f ? Loc.F("Выработка энергии +{0:0}", m.PowerProduce) : m.PowerDraw > 0f ? Loc.F("Потребление энергии {0:0}", m.PowerDraw) : Loc.T("Не требует энергии");
            return $"<b>{m.Name}</b>\n{m.Description}\n<color=#8AA2A8>{ModuleVisuals.StatLine(m)}</color>\n{power} · " +
                   Loc.F("{0:0} сплавов", FleetManager.ShipAlloyCost(m.AlloyCost, 0));
        }

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
            Achievements.Unlock(Ach.FirstDesign);
            StarterGoals.MarkDesigned();
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

    /// <summary>Вращение модели корабля мышью в конструкторе.</summary>
    public class ShipSpinCatcher : MonoBehaviour, UnityEngine.EventSystems.IDragHandler
    {
        public void OnDrag(UnityEngine.EventSystems.PointerEventData e)
            => ShipHoloStudio.Instance?.Spin(e.delta.x * 0.4f, -e.delta.y * 0.2f);
    }

    /// <summary>Наведение мыши на элемент интерфейса: действия при входе и выходе.</summary>
    public class UIHover : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        public System.Action Enter, Exit;
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) => Enter?.Invoke();
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => Exit?.Invoke();
        private void OnDisable() => Exit?.Invoke();
    }
}
