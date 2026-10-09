using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Generation;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Командная панель выделенного флота внизу экрана.
    ///   • Слева — медальон: лицо лидера на борту или эмблема корпуса в кольце цвета типа.
    ///   • Шапка — имя, класс, лидер и система; справа — светящийся статус («В БОЮ» пульсирует, «ПРЫЖОК», «РАЗВЕДКА»…).
    ///   • Три полосы защиты: щиты, броня, корпус — с числами.
    ///   • Чипы характеристик (мощь, урон, уклонение, скорость прыжка; у гражданских — их работа).
    ///   • Нижняя строка — маршрут или текущее действие с полосой прогресса (прыжок, разведка, монтаж, разгон ГПД в бою).
    ///   • Справа — док команд за вертикальным разделителем; у научных кораблей — переключатель авторазведки.
    /// При мультивыделении — состав по типам, суммарная мощь, общая прочность и самое долгое прибытие.
    /// </summary>
    public class FleetCommandHUD : MonoBehaviour
    {
        public static FleetCommandHUD Instance { get; private set; }

        private const float Width = 800f, Height = 148f;
        private const float DockW = 162f, AutoW = 60f;
        private const float BodyLeft = 122f;

        private static readonly Color CShield = new Color(0.45f, 0.78f, 1f);
        private static readonly Color CArmor = new Color(0.98f, 0.76f, 0.38f);
        private static readonly Color CSci = new Color(0.45f, 1f, 0.62f);
        private static readonly Color CCon = new Color(1f, 0.82f, 0.36f);

        private class Gauge { public Text Value; public RectTransform Fill; }
        private class Chip { public GameObject Go; public Image Icon; public Text Text; public TooltipTrigger Tip; }

        private GameObject _root;
        private RectTransform _rootRt, _body, _dockSep;
        private LiquidGlassEffect _glass, _medalFx;
        private Image _accent, _medalIcon, _badgeBg, _badge, _statusDot;
        private Text _badgeCount;
        private LeaderThumb _leaderFace;
        private Text _title, _subtitle, _statusText, _route;
        private RectTransform _statusChip, _progressHost, _progressFill;
        private Gauge _shield, _armor, _hull;
        private readonly Chip[] _chips = new Chip[4];
        private Button _btnStop, _btnNext, _btnClear, _btnAuto;
        private Image _autoIcon, _autoGlow;
        private Text _autoLabel;
        private bool _pulse;
        private Color _statusCol;
        private float _timer;
        private GalaxyGenerator _gen;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            var ui = UIManager.Instance;
            if (ui == null || ui.HudCanvas == null) return;
            Build(ui.HudCanvas.transform);
            FleetManager.OnSelectionChanged += Refresh;
        }

        private void OnDestroy() => FleetManager.OnSelectionChanged -= Refresh;

        // ==================== ПОСТРОЕНИЕ ====================

        private void Build(Transform hud)
        {
            var rt = LGBuild.Rect(hud, "[UI] FleetCommandHUD");
            rt.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 16), new Vector2(Width, Height));
            _root = rt.gameObject;
            _rootRt = rt;
            UIAnchors.Register(UIAnchors.FleetHud, rt);
            var bg = _root.AddComponent<Image>();
            bg.color = UIManager.DS.BgDeep;
            bg.raycastTarget = true;
            _glass = LG.Glass(_root, 22f);
            var mo = LG.Motion(_root, LGAppear.Kind.SlideUp);
            mo.distance = 26f;

            // Акцентная нить по верхней кромке — цвет типа выделенного
            _accent = LGBuild.Panel(rt, "Accent", FleetIndicator.OwnColor);
            _accent.rectTransform.anchorMin = new Vector2(0, 1);
            _accent.rectTransform.anchorMax = new Vector2(1, 1);
            _accent.rectTransform.offsetMin = new Vector2(44, -3);
            _accent.rectTransform.offsetMax = new Vector2(-44, -1);
            LG.Line(_accent.gameObject);

            BuildMedallion(rt);
            BuildBody(rt);
            BuildDock(rt);

            LG.Skin(_root.transform);
            _root.SetActive(false);
        }

        private void BuildMedallion(RectTransform rt)
        {
            var medal = LGBuild.Panel(rt, "Medallion", new Color(0.04f, 0.13f, 0.15f));
            medal.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 2), new Vector2(88, 88));
            _medalFx = LG.Platter(medal.gameObject, 44f);
            _medalFx.FillMultiplier = 2.2f;

            // Внутреннее кольцо — тонкая окантовка поверх стекла
            var ring = LGBuild.Panel(medal.transform, "Ring", new Color(1f, 1f, 1f, 0.05f));
            ring.rectTransform.Stretch(7, 7, 7, 7);
            LG.Platter(ring.gameObject, 37f).FillMultiplier = 1.4f;

            _medalIcon = LGIcons.Create(medal.transform, LGIcon.Ship, 40, FleetIndicator.OwnColor);
            _medalIcon.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40, 40));

            _leaderFace = LeaderThumb.Create(medal.transform, 44f, 2.5f);
            _leaderFace.Hide();

            _badgeBg = LGBuild.Panel(medal.transform, "TypeBadge", new Color(0.03f, 0.08f, 0.10f));
            _badgeBg.rectTransform.At(new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-10, 10), new Vector2(28, 28));
            LG.Platter(_badgeBg.gameObject, 14f).FillMultiplier = 2.2f;
            _badge = LGIcons.Create(_badgeBg.transform, LGIcon.Ship, 15, FleetIndicator.OwnColor);
            _badgeCount = LGBuild.Label(_badgeBg.transform, "", 11, Color.white, TextAnchor.MiddleCenter, bold: true);
            _badgeBg.gameObject.SetActive(false);
        }

        private void BuildBody(RectTransform rt)
        {
            var body = LGBuild.Rect(rt, "Body");
            body.Stretch(BodyLeft, 12, DockW + 34, 14);
            _body = body;

            // Шапка: имя слева, статус справа
            _title = LGBuild.Label(body, "", 17, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
            _title.rectTransform.offsetMax = new Vector2(-172, 0);
            _title.horizontalOverflow = HorizontalWrapMode.Wrap;
            _title.verticalOverflow = VerticalWrapMode.Truncate;
            _title.rectTransform.anchorMin = new Vector2(0, 1);
            _title.rectTransform.offsetMin = new Vector2(0, -23);

            var chip = LGBuild.Panel(body, "Status", new Color(0.05f, 0.10f, 0.13f, 0.95f));
            _statusChip = chip.rectTransform;
            _statusChip.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(164, 22));
            LG.Chip(chip.gameObject, new Color(1f, 1f, 1f, 0.2f), 11f);
            _statusDot = LGBuild.Panel(chip.transform, "Dot", Color.white);
            _statusDot.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(15, 0), new Vector2(8, 8));
            LG.Dot(_statusDot.gameObject);
            _statusText = LGBuild.Label(chip.transform, "", 10, Color.white, TextAnchor.MiddleCenter, bold: true);
            _statusText.rectTransform.Stretch(24, 0, 8, 0);

            _subtitle = LGBuild.Label(body, "", 11, UIManager.DS.TextMuted, TextAnchor.UpperLeft);
            _subtitle.rectTransform.anchorMin = new Vector2(0, 1);
            _subtitle.rectTransform.offsetMin = new Vector2(0, -41);
            _subtitle.rectTransform.offsetMax = new Vector2(0, -24);
            _subtitle.horizontalOverflow = HorizontalWrapMode.Wrap;
            _subtitle.verticalOverflow = VerticalWrapMode.Truncate;

            var sep = LGBuild.Panel(body, "Sep", new Color(0.36f, 0.88f, 0.82f, 0.22f));
            sep.rectTransform.anchorMin = new Vector2(0, 1);
            sep.rectTransform.anchorMax = new Vector2(1, 1);
            sep.rectTransform.offsetMin = new Vector2(0, -45);
            sep.rectTransform.offsetMax = new Vector2(0, -44);
            LG.Line(sep.gameObject, hairline: true);

            // Полосы защиты
            var gauges = LGBuild.Rect(body, "Gauges");
            gauges.anchorMin = new Vector2(0, 1);
            gauges.anchorMax = new Vector2(1, 1);
            gauges.offsetMin = new Vector2(0, -77);
            gauges.offsetMax = new Vector2(0, -51);
            _shield = MakeGauge(gauges, 0, LGIcon.ShieldDome, "ЩИТЫ", CShield);
            _armor = MakeGauge(gauges, 1, LGIcon.ArmorPlate, "БРОНЯ", CArmor);
            _hull = MakeGauge(gauges, 2, LGIcon.Ship, "КОРПУС", UIManager.DS.Green);

            // Чипы характеристик
            var chips = LGBuild.Rect(body, "Chips");
            chips.anchorMin = new Vector2(0, 1);
            chips.anchorMax = new Vector2(1, 1);
            chips.offsetMin = new Vector2(0, -109);
            chips.offsetMax = new Vector2(0, -85);
            for (int i = 0; i < _chips.Length; i++) _chips[i] = MakeChip(chips, i);

            // Действие / маршрут
            _progressHost = LGBuild.Rect(body, "Progress");
            _progressHost.anchorMin = new Vector2(0, 0);
            _progressHost.anchorMax = new Vector2(1, 0);
            _progressHost.offsetMin = new Vector2(0, 17);
            _progressHost.offsetMax = new Vector2(0, 21);
            _progressFill = LGBuild.Bar(_progressHost, UIManager.DS.NeonCyan, 0f, 3f);

            _route = LGBuild.Label(body, "", 10, UIManager.DS.TextMuted, TextAnchor.LowerLeft);
            _route.horizontalOverflow = HorizontalWrapMode.Wrap;
            _route.verticalOverflow = VerticalWrapMode.Truncate;
            _route.rectTransform.anchorMax = new Vector2(1, 0);
            _route.rectTransform.offsetMax = new Vector2(0, 15);
        }

        private static Gauge MakeGauge(RectTransform parent, int col, LGIcon icon, string name, Color color)
        {
            var cell = LGBuild.Rect(parent, "Gauge_" + name);
            cell.Column(col / 3f, (col + 1) / 3f, col == 0 ? 0 : 7, col == 2 ? 0 : 7);
            var ic = LGIcons.Create(cell, icon, 12, color);
            ic.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(12, 12));
            var label = LGBuild.Label(cell, name, 9, UIManager.DS.TextMuted, TextAnchor.UpperLeft, bold: true);
            label.rectTransform.offsetMin = new Vector2(16, 0);
            label.rectTransform.offsetMax = new Vector2(0, 1);
            var value = LGBuild.Label(cell, "", 10, UIManager.DS.TextPrimary, TextAnchor.UpperRight, bold: true);
            value.rectTransform.offsetMax = new Vector2(0, 1);
            var host = LGBuild.Rect(cell, "Bar");
            host.anchorMin = new Vector2(0, 0);
            host.anchorMax = new Vector2(1, 0);
            host.offsetMin = Vector2.zero;
            host.offsetMax = new Vector2(0, 7);
            return new Gauge { Value = value, Fill = LGBuild.Bar(host, color, 1f, 6f) };
        }

        private static Chip MakeChip(RectTransform parent, int col)
        {
            var cell = LGBuild.Panel(parent, "Chip" + col, new Color(0.05f, 0.11f, 0.14f, 0.9f), raycast: true);
            cell.rectTransform.Column(col / 4f, (col + 1) / 4f, col == 0 ? 0 : 3, col == 3 ? 0 : 3);
            LG.Chip(cell.gameObject, new Color(1f, 1f, 1f, 0.12f), 12f);
            var icon = LGIcons.Create(cell.transform, LGIcon.Swords, 13, Color.white);
            icon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(9, 0), new Vector2(13, 13));
            var text = LGBuild.Label(cell.transform, "", 10, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft, bold: true);
            text.rectTransform.Stretch(27, 0, 6, 0);
            TooltipHelper.Attach(cell.gameObject, "");
            return new Chip { Go = cell.gameObject, Icon = icon, Text = text, Tip = cell.GetComponent<TooltipTrigger>() };
        }

        private void BuildDock(RectTransform rt)
        {
            var line = LGBuild.Panel(rt, "DockSep", new Color(0.36f, 0.88f, 0.82f, 0.22f));
            _dockSep = line.rectTransform;
            _dockSep.anchorMin = new Vector2(1, 0);
            _dockSep.anchorMax = new Vector2(1, 1);
            PlaceDockSep(false);
            LG.Line(line.gameObject, hairline: true);

            var btns = LGBuild.Rect(rt, "Dock");
            btns.anchorMin = new Vector2(1, 0);
            btns.anchorMax = new Vector2(1, 1);
            btns.pivot = new Vector2(1, 0.5f);
            btns.sizeDelta = new Vector2(DockW, 0);
            btns.anchoredPosition = new Vector2(-14, 0);

            _btnStop = DockButton(btns, 0, 0, LGIcon.Pause, "Стоп", "Сбросить маршрут и очередь приказов", () => ForEach(f => FleetManager.Instance.StopFleet(f)));
            _btnNext = DockButton(btns, 1, 0, LGIcon.Fast, "Следующая цель", "Пропустить текущий пункт и лететь к следующему из очереди", () => ForEach(f => FleetManager.Instance.SkipToNext(f)));
            _btnClear = DockButton(btns, 2, 0, LGIcon.Trash, "Очистить очередь", "Убрать пункты, добавленные через Shift+ПКМ (клавиша Backspace)", () => ForEach(f => FleetManager.Instance.ClearQueue(f)));
            DockButton(btns, 0, 1, LGIcon.Target, "Камера к флоту", "Плавно навести камеру на флот (клавиша F, двойной клик по значку)",
                () => FleetSelectionController.FocusCamera(FleetManager.Instance?.SelectedFleet));
            DockButton(btns, 1, 1, LGIcon.Fleet, "Выделить только главный", "Оставить в выделении один флот",
                () => FleetManager.Instance?.SelectFleet(FleetManager.Instance.SelectedFleet));
            DockButton(btns, 2, 1, LGIcon.Close, "Снять выделение", "Щелчок ЛКМ по пустому месту делает то же самое",
                () => FleetManager.Instance?.SetSelection(null));

            BuildAutoButton(btns);
        }

        private void PlaceDockSep(bool science)
        {
            float x = DockW + 24f + (science ? AutoW : 0f);
            _dockSep.offsetMin = new Vector2(-x, 18);
            _dockSep.offsetMax = new Vector2(-x + 1f, -18);
        }

        private Button DockButton(RectTransform parent, int col, int row, LGIcon icon, string title, string tip, System.Action onClick)
        {
            var b = LGBuild.Button(parent, title, UIManager.DS.BtnNeutral, new Color(0.5f, 0.95f, 0.9f, 0.55f),
                () => { onClick?.Invoke(); SFXManager.Play("ui_click", 0.9f, 1.05f); Refresh(); }, icon, null, 13, 12f);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(50, 44);
            rt.anchoredPosition = new Vector2(col * 56f, row == 0 ? 25f : -25f);
            TooltipHelper.Attach(b.gameObject, $"<b>{title}</b>\n{tip}");
            return b;
        }

        /// <summary>Высокий переключатель «Авторазведка» слева от сетки дока.</summary>
        private void BuildAutoButton(RectTransform dock)
        {
            _btnAuto = LGBuild.Button(dock, "AutoExplore", UIManager.DS.BtnNeutral, new Color(0.45f, 1f, 0.62f, 0.6f),
                () => { AutoExplore.Toggle(FleetManager.Instance.SelectedFleets); SFXManager.Play("ui_click", 0.9f, 1.05f); Refresh(); },
                null, null, 11, 12f);
            var rt = (RectTransform)_btnAuto.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(AutoW - 6f, 94);
            rt.anchoredPosition = new Vector2(-AutoW, 0f);

            _autoGlow = LGBuild.Panel(rt, "On", CSci);
            _autoGlow.raycastTarget = false;
            _autoGlow.rectTransform.anchorMin = new Vector2(0.22f, 0f);
            _autoGlow.rectTransform.anchorMax = new Vector2(0.78f, 0f);
            _autoGlow.rectTransform.offsetMin = new Vector2(0, 7);
            _autoGlow.rectTransform.offsetMax = new Vector2(0, 10);
            LG.Fill(_autoGlow.gameObject);

            _autoIcon = LGIcons.Create(rt, LGIcon.Sensors, 26, Color.white);
            _autoIcon.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 14), new Vector2(26, 26));
            _autoLabel = LGBuild.Label(rt, "АВТО", 9, Color.white, TextAnchor.LowerCenter, bold: true);
            _autoLabel.rectTransform.Stretch(2, 16, 2, 0);
            TooltipHelper.Attach(_btnAuto.gameObject,
                "<b>Авторазведка</b>\nНаучный корабль сам выбирает ближайшую неизученную систему и исследует её, затем следующую. " +
                "Обходит видимые угрозы и владения врагов. Любой ручной приказ выключает режим.");
            _btnAuto.gameObject.SetActive(false);
        }

        private static void ForEach(System.Action<FleetView> act)
        {
            var fm = FleetManager.Instance;
            if (fm == null) return;
            foreach (var f in new List<FleetView>(fm.SelectedFleets)) act(f);
        }

        // ==================== ОБНОВЛЕНИЕ ====================

        private void Update()
        {
            if (_root == null) return;
            if (_pulse && _statusDot != null)
            {
                float k = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 7f);
                _statusDot.color = new Color(_statusCol.r, _statusCol.g, _statusCol.b, 0.35f + 0.65f * k);
            }
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 0.25f;
            Refresh();
        }

        private void Refresh()
        {
            if (_root == null) return;
            var fm = FleetManager.Instance;
            bool inSystem = SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView;
            bool show = UIManager.IsGameStarted && fm != null && fm.SelectedFleets.Count > 0 && !inSystem;
            if (show != LG.IsVisible(_root))
            {
                if (show) LG.Show(_root);
                else LG.Hide(_root);
            }
            if (!show) return;
            if (_gen == null) _gen = FindAnyObjectByType<GalaxyGenerator>();

            var sel = fm.SelectedFleets;
            if (sel.Count == 1) ShowSingle(sel[0]);
            else ShowMany(sel);

            bool anyRoute = false, anyQueue = false;
            foreach (var f in sel)
            {
                if (f?.Data == null) continue;
                if (f.Data.Path.Count > 0 || f.Data.OrderQueue.Count > 0) anyRoute = true;
                if (f.Data.OrderQueue.Count > 0) anyQueue = true;
            }
            _btnStop.interactable = anyRoute;
            _btnNext.interactable = anyQueue;
            _btnClear.interactable = anyQueue;
            RefreshAuto(sel);
        }

        private void SetTheme(Color c)
        {
            _accent.color = new Color(c.r, c.g, c.b, 0.85f);
            _glass?.SetRim(new Color(c.r, c.g, c.b, 0.42f));
            _medalFx?.SetRim(new Color(c.r, c.g, c.b, 0.75f));
        }

        private void ShowSingle(FleetView f)
        {
            var d = f.Data;
            if (d == null) return;
            Color tc = TypeColor(d.Type);
            SetTheme(tc);
            _medalIcon.sprite = LGIcons.Get(TypeIcon(d));
            _medalIcon.color = tc;
            _badgeCount.text = "";

            // Лидер на борту: его лицо в медальоне, тип — значком в углу
            var leader = LeaderManager.Instance?.LeaderOfShip(d.Id);
            bool face = leader != null && _leaderFace.Set(leader, LeaderThumb.ClassColor(leader.Class));
            if (!face) _leaderFace.Hide();
            _medalIcon.enabled = !face;
            _badgeBg.gameObject.SetActive(face);
            _badge.enabled = true;
            _badge.sprite = _medalIcon.sprite;
            _badge.color = tc;

            _title.text = d.Name;
            var sub = new StringBuilder(ClassName(d));
            if (leader != null)
                sub.Append($"   ·   <color={LGBuild.Hex(LeaderThumb.ClassColor(leader.Class))}>{LeaderManager.ClassName(leader.Class)} {leader.Name}</color>  ур. {leader.Level}");
            if (_gen != null && d.State != FleetState.InHyperlane)
                sub.Append($"   ·   {SysName(d.CurrentSystemId)}");
            _subtitle.text = sub.ToString();

            SetStatus(d);
            SetGauge(_shield, d.ShieldPoints, d.MaxShieldPoints, CShield);
            SetGauge(_armor, d.ArmorPoints, d.MaxArmorPoints, CArmor);
            SetGauge(_hull, d.HullPoints, d.MaxHullPoints, HullColor(d.HullPoints / Mathf.Max(1f, d.MaxHullPoints)));
            FillChipsSingle(d);
            SetActivity(d);
        }

        private void ShowMany(IReadOnlyList<FleetView> sel)
        {
            int mil = 0, sci = 0, con = 0, fighting = 0, moving = 0;
            float power = 0f, upkeep = 0f;
            float sh = 0f, shMax = 0f, ar = 0f, arMax = 0f, hu = 0f, huMax = 0f;
            foreach (var f in sel)
            {
                var d = f?.Data;
                if (d == null) continue;
                if (d.Type == FleetType.Military) { mil++; power += CombatMath.Power(d); }
                else if (d.Type == FleetType.Science) sci++;
                else con++;
                if (d.InCombat) fighting++;
                if (d.State == FleetState.InHyperlane || d.Path.Count > 0) moving++;
                upkeep += d.UpkeepEnergy;
                sh += d.ShieldPoints; shMax += d.MaxShieldPoints;
                ar += d.ArmorPoints; arMax += d.MaxArmorPoints;
                hu += d.HullPoints; huMax += d.MaxHullPoints;
            }
            Color tc = mil > 0 ? FleetIndicator.OwnColor : sci >= con ? CSci : CCon;
            SetTheme(tc);
            _medalIcon.sprite = LGIcons.Get(LGIcon.Fleet);
            _medalIcon.color = tc;
            _medalIcon.enabled = true;
            _leaderFace.Hide();
            _badgeBg.gameObject.SetActive(true);
            _badge.enabled = false;
            _badgeCount.text = sel.Count.ToString();

            _title.text = $"Выделено: {sel.Count} {FleetWord(sel.Count)}";
            _subtitle.text = "ПКМ по системе — приказ всем   ·   Shift+ПКМ — в очередь";

            if (fighting > 0) Status($"В БОЮ · {fighting}", UIManager.DS.Red, true);
            else if (moving > 0) Status($"В ПУТИ · {moving}", UIManager.DS.NeonCyan, false);
            else Status("НА ОРБИТАХ", UIManager.DS.TextMuted, false);

            SetGauge(_shield, sh, shMax, CShield);
            SetGauge(_armor, ar, arMax, CArmor);
            SetGauge(_hull, hu, huMax, HullColor(huMax > 0f ? hu / huMax : 1f));

            int c = 0;
            if (mil > 0) SetChip(c++, LGIcon.Swords, UIManager.DS.Gold, $"{Mathf.RoundToInt(power):N0}", "<b>Суммарная мощь</b>\nБоевая сила всех выделенных военных кораблей");
            if (mil > 0) SetChip(c++, LGIcon.Ship, FleetIndicator.OwnColor, $"военных {mil}", "<b>Военные корабли</b> в выделении");
            if (sci > 0) SetChip(c++, LGIcon.Sensors, CSci, $"научных {sci}", "<b>Научные корабли</b> в выделении");
            if (con > 0 && c < 3) SetChip(c++, LGIcon.Construction, CCon, $"строит. {con}", "<b>Строительные корабли</b> в выделении");
            SetChip(c++, LGIcon.Energy, new Color(1f, 0.86f, 0.35f), $"−{upkeep:0.#}/мес", "<b>Содержание</b>\nЭнергия в месяц на все выделенные корабли");
            for (; c < _chips.Length; c++) _chips[c].Go.SetActive(false);

            float longest = 0f;
            foreach (var f in sel) if (f?.Data != null) longest = Mathf.Max(longest, FleetRoute.TotalDays(f.Data, _gen));
            SetProgress(-1f, UIManager.DS.NeonCyan);
            _route.text = longest > 0f ? $"Последний прибудет через ~{FleetRoute.FormatDays(longest)}" : "Все флоты стоят на орбитах";
        }

        // ==================== ЭЛЕМЕНТЫ ====================

        private void Status(string text, Color c, bool pulse)
        {
            _statusText.text = text;
            _statusText.color = Color.Lerp(c, Color.white, 0.25f);
            _statusCol = c;
            _pulse = pulse;
            if (!pulse) _statusDot.color = c;
            var fx = _statusChip.GetComponent<LiquidGlassEffect>();
            if (fx != null) fx.SetRim(new Color(c.r, c.g, c.b, pulse ? 0.7f : 0.4f));
        }

        private void SetStatus(FleetData d)
        {
            var cm = CombatManager.Instance;
            var fm = FleetManager.Instance;
            if (d.InCombat)
            {
                if (cm != null && cm.IsRetreating(d)) Status("ОТСТУПЛЕНИЕ", UIManager.DS.Gold, true);
                else Status("В БОЮ", UIManager.DS.Red, true);
                return;
            }
            switch (d.State)
            {
                case FleetState.InHyperlane: Status("ПРЫЖОК", UIManager.DS.NeonCyan, false); return;
                case FleetState.Surveying: Status("РАЗВЕДКА", CSci, false); return;
                case FleetState.Constructing: Status("МОНТАЖ ФОРПОСТА", CCon, false); return;
            }
            if (d.Path.Count > 0) Status("К ПРЫЖКУ", UIManager.DS.NeonCyan, false);
            else if (d.AutoExplore) Status("АВТОРАЗВЕДКА", CSci, false);
            else if (fm != null && fm.NeedsRepair(d)) Status("РЕМОНТ", UIManager.DS.Gold, false);
            else Status("НА ОРБИТЕ", UIManager.DS.TextMuted, false);
        }

        private static void SetGauge(Gauge g, float v, float max, Color c)
        {
            bool has = max > 0.5f;
            g.Value.text = has ? $"{v:0}<color=#8FA6B2>/{max:0}</color>" : "<color=#8FA6B2>—</color>";
            LGBuild.SetBar(g.Fill, has ? Mathf.Clamp01(v / max) : 0f, c);
        }

        private static Color HullColor(float frac) =>
            frac > 0.6f ? UIManager.DS.Green : frac > 0.3f ? UIManager.DS.Gold : UIManager.DS.Red;

        private void SetChip(int i, LGIcon icon, Color iconCol, string text, string tip)
        {
            var c = _chips[i];
            if (!c.Go.activeSelf) c.Go.SetActive(true);
            c.Icon.sprite = LGIcons.Get(icon);
            c.Icon.color = iconCol;
            c.Text.text = text;
            if (c.Tip != null) c.Tip.text = tip;
        }

        private void FillChipsSingle(FleetData d)
        {
            int c = 0;
            if (d.Type == FleetType.Military)
            {
                float dpd = 0f;
                foreach (var w in d.Weapons) dpd += w.Damage * w.FireRate * CombatManager.ShotsPerDay;
                SetChip(c++, LGIcon.Swords, UIManager.DS.Gold, $"{Mathf.RoundToInt(CombatMath.Power(d)):N0}",
                    "<b>Мощь</b>\nОбщая боевая сила корабля: урон, прочность и защита");
                SetChip(c++, WeaponIcon(d.PrimaryWeapon), WeaponColor(d.PrimaryWeapon), $"{dpd:0}/дн",
                    $"<b>Урон в день</b>\nОсновное оружие: {WeaponName(d.PrimaryWeapon)}. Орудий: {d.Weapons.Count}");
                SetChip(c++, LGIcon.Thruster, new Color(0.6f, 0.85f, 1f), $"{d.Evasion:0}%",
                    "<b>Уклонение</b>\nШанс увернуться от выстрела; снижается точностью противника");
            }
            else if (d.Type == FleetType.Science)
            {
                float mult = EmpireBonuses.For(d.OwnerId).SurveySpeed * LeaderManager.SurveySpeedMult(d);
                SetChip(c++, LGIcon.Sensors, CSci, $"×{mult:0.##}", "<b>Скорость разведки</b>\nС учётом технологий и учёного на борту");
                SetChip(c++, LGIcon.Clock, UIManager.DS.TextMuted, $"{d.TotalSurveyDays / Mathf.Max(0.05f, mult):0} дн.",
                    "<b>Разведка системы</b>\nСколько дней занимает исследование одной системы");
                SetChip(c++, LGIcon.Star, UIManager.DS.NeonCyan, d.AutoExplore ? "<color=#73FF9E>авто: вкл.</color>" : "авто: выкл.",
                    "<b>Авторазведка</b>\nВключается кнопкой «АВТО» справа");
            }
            else
            {
                SetChip(c++, LGIcon.Construction, CCon, $"{d.TotalConstructionDays:0} дн.", "<b>Монтаж форпоста</b>\nСколько дней строится звёздная база");
                SetChip(c++, LGIcon.Influence, new Color(0.75f, 0.6f, 1f), $"{FleetManager.OutpostInfluenceCost(d.OwnerId):0}",
                    "<b>Цена форпоста</b>\nВлияние за новую систему");
                SetChip(c++, LGIcon.Alloys, new Color(0.85f, 0.7f, 1f), $"{FactionTraits.StarbaseAlloys(d.OwnerId):0}", "<b>Сплавы на форпост</b>");
            }
            SetChip(c++, LGIcon.Propulsion, new Color(0.55f, 0.85f, 1f), $"{FleetRoute.DaysPerJump(d):0} дн/прыжок",
                $"<b>Скорость</b>\nДней на один гиперпрыжок · содержание {d.UpkeepEnergy:0.#} энергии в месяц");
            for (; c < _chips.Length; c++) _chips[c].Go.SetActive(false);
        }

        /// <summary>Нижняя строка: текущее действие с прогрессом либо маршрут.</summary>
        private void SetActivity(FleetData d)
        {
            var cm = CombatManager.Instance;
            if (d.InCombat && cm != null)
            {
                float left = cm.FtlReadyIn(d);
                SetProgress(1f - left / CombatManager.FtlSpoolDays, left > 0f ? UIManager.DS.NeonCyan : UIManager.DS.Green);
                _route.text = cm.IsRetreating(d)
                    ? (left > 0f ? $"<color=#F2C747>Отступление:</color> прыжок через {left:0.0} дн." : "<color=#F2C747>Отступление:</color> гиперпривод готов — прыжок")
                    : left > 0f ? $"Разгон гиперпривода: {left:0.0} дн. до возможности экстренного прыжка"
                                : "<color=#5CF59A>Гиперпривод готов</color> — можно отступить экстренным прыжком";
                return;
            }
            switch (d.State)
            {
                case FleetState.InHyperlane:
                    SetProgress(1f - d.DaysRemainingInTransit / Mathf.Max(0.01f, d.TotalDaysForTransit), UIManager.DS.NeonCyan);
                    break;
                case FleetState.Surveying:
                    SetProgress(1f - d.DaysRemainingSurvey / Mathf.Max(0.01f, d.TotalSurveyDays), CSci);
                    _route.text = $"Разведка системы {SysName(d.CurrentSystemId)} · осталось {Mathf.Max(0, d.DaysRemainingSurvey):0} дн.";
                    return;
                case FleetState.Constructing:
                    SetProgress(1f - d.DaysRemainingConstruction / Mathf.Max(0.01f, d.TotalConstructionDays), CCon);
                    _route.text = $"Монтаж форпоста: {SysName(d.CurrentSystemId)} · осталось {Mathf.Max(0, d.DaysRemainingConstruction):0} дн.";
                    return;
                default:
                    SetProgress(-1f, UIManager.DS.NeonCyan);
                    break;
            }
            _route.text = RouteText(d);
        }

        private void SetProgress(float v, Color c)
        {
            bool on = v >= 0f;
            if (_progressHost.gameObject.activeSelf != on) _progressHost.gameObject.SetActive(on);
            if (on) LGBuild.SetBar(_progressFill, Mathf.Clamp01(v), c);
        }

        private void RefreshAuto(IReadOnlyList<FleetView> sel)
        {
            if (_btnAuto == null) return;
            bool science = false;
            foreach (var f in sel) if (f?.Data != null && f.Data.Type == FleetType.Science && f.Data.OwnerId == 0) { science = true; break; }
            if (_btnAuto.gameObject.activeSelf != science)
            {
                _btnAuto.gameObject.SetActive(science);
                _rootRt.sizeDelta = new Vector2(Width + (science ? AutoW : 0f), _rootRt.sizeDelta.y);
                _body.offsetMax = new Vector2(-(DockW + 34f + (science ? AutoW : 0f)), _body.offsetMax.y);
                PlaceDockSep(science);
            }
            if (!science) return;
            bool on = AutoExplore.AnyOn(sel);
            _autoGlow.gameObject.SetActive(on);
            Color idle = new Color(0.85f, 0.92f, 0.95f);
            _autoIcon.color = on ? CSci : idle;
            _autoLabel.color = on ? CSci : idle;
            _autoLabel.text = on ? "АВТО\nВКЛ" : "АВТО";
        }

        // ==================== ТЕКСТЫ ====================

        private string SysName(int id) => _gen != null && id >= 0 && id < _gen.Systems.Count ? _gen.Systems[id].Name : "?";

        private string RouteText(FleetData d)
        {
            if (_gen == null) return "";
            var nodes = FleetRoute.Build(d, _gen);
            if (nodes.Count == 0)
            {
                var fm = FleetManager.Instance;
                if (fm != null && fm.NeedsRepair(d))
                {
                    float r = fm.RepairRateAt(d.CurrentSystemId, d.OwnerId);
                    return r > 0f ? $"Ремонт: {r * 100f:0}% в день" : "<color=#F2C747>Повреждён</color> — ремонт только на своей территории";
                }
                return "ПКМ по системе — лететь   ·   Shift+ПКМ — добавить в очередь";
            }
            var sb = new StringBuilder("<color=#66DBD1>Маршрут</color>   ");
            int shown = 0;
            foreach (var nd in nodes)
            {
                if (nd.OrderIndex == 0) continue;
                if (shown++ > 0) sb.Append(" → ");
                if (shown > 4) { sb.Append("…"); break; }
                sb.Append(_gen.Systems[nd.SystemId].Name);
            }
            sb.Append($"   ·   ~{FleetRoute.FormatDays(nodes[nodes.Count - 1].Days)}");
            if (d.OrderQueue.Count > 0) sb.Append($"   ·   в очереди: {d.OrderQueue.Count}");
            return sb.ToString();
        }

        private static string FleetWord(int n)
        {
            int m10 = n % 10, m100 = n % 100;
            if (m10 == 1 && m100 != 11) return "флот";
            if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return "флота";
            return "флотов";
        }

        private static string ClassName(FleetData d) => d.Type switch
        {
            FleetType.Science => "Научный корабль",
            FleetType.Constructor => "Строительный корабль",
            _ => d.HullClass == ShipClass.Destroyer ? "Эсминец" : d.HullClass == ShipClass.Frigate ? "Фрегат" : "Корвет"
        };

        private static LGIcon TypeIcon(FleetData d) => d.Type switch
        {
            FleetType.Science => LGIcon.Sensors,
            FleetType.Constructor => LGIcon.Construction,
            _ => d.HullClass == ShipClass.Destroyer ? LGIcon.Destroyer : d.HullClass == ShipClass.Frigate ? LGIcon.Frigate : LGIcon.Corvette
        };

        private static Color TypeColor(FleetType t) => t switch
        {
            FleetType.Science => CSci,
            FleetType.Constructor => CCon,
            _ => FleetIndicator.OwnColor
        };

        private static LGIcon WeaponIcon(WeaponDamageType t) => t switch
        {
            WeaponDamageType.Energy => LGIcon.Laser,
            WeaponDamageType.Kinetic => LGIcon.Cannon,
            _ => LGIcon.Missile
        };

        private static Color WeaponColor(WeaponDamageType t) => t switch
        {
            WeaponDamageType.Energy => new Color(1f, 0.45f, 0.4f),
            WeaponDamageType.Kinetic => new Color(1f, 0.85f, 0.4f),
            _ => new Color(0.5f, 1f, 0.6f)
        };

        private static string WeaponName(WeaponDamageType t) => t switch
        {
            WeaponDamageType.Energy => "энергетическое",
            WeaponDamageType.Kinetic => "кинетическое",
            _ => "ракеты"
        };
    }
}
