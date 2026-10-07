using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Cam;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Аутлайнер справа, как в Stellaris: исследования, стройки, военные флоты, научные и строительные
    /// корабли — с состоянием и таймерами. Секции сворачиваются (запоминается), вся панель — тоже.
    /// Щелчок по флоту выделяет его и наводит камеру; по стройке — камера на систему;
    /// по исследованию — открывает дерево технологий. Простаивающие корабли и бои пульсируют.
    /// Строки обновляются на месте, поэтому анимации (блики на полосах, пульс) не сбиваются.
    /// </summary>
    public class OutlinerPanel : MonoBehaviour
    {
        private const float Width = 300f, Top = 66f, Bottom = 284f, RowH = 42f, HeadH = 32f;

        private static Color Primary => UIManager.DS.TextPrimary;
        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Cyan => UIManager.DS.NeonCyan;
        private static Color Gold => UIManager.DS.Gold;
        private static Color Green => UIManager.DS.Green;
        private static Color Red => UIManager.DS.Red;

        private static readonly Color RowBg = new Color(0.030f, 0.055f, 0.075f, 0.92f);
        private static readonly Color RowWarnBg = new Color(0.120f, 0.085f, 0.030f, 0.92f);
        private static readonly Color RowDangerBg = new Color(0.140f, 0.040f, 0.045f, 0.92f);

        private enum Sec { Research, Build, Military, Science, Constructor }
        private static readonly string[] SecNames = { "ИССЛЕДОВАНИЯ", "СТРОИТЕЛЬСТВО", "ФЛОТЫ", "НАУЧНЫЕ КОРАБЛИ", "СТРОИТЕЛИ" };
        private static readonly LGIcon[] SecIcons = { LGIcon.Research, LGIcon.Construction, LGIcon.Fleet, LGIcon.Sensors, LGIcon.Wrench };
        private static readonly Color[] SecColors =
        {
            new Color(0.62f, 0.58f, 1.00f),
            new Color(0.44f, 0.88f, 0.60f),
            new Color(0.40f, 0.86f, 0.82f),
            new Color(0.42f, 0.66f, 1.00f),
            new Color(1.00f, 0.70f, 0.36f)
        };

        private RectTransform _root, _list, _body, _sweep, _headRt;
        private Image _headIcon, _headLine;
        private Text _toggleLabel, _alertText;
        private GameObject _alertChip;
        private bool _collapsed;
        private readonly bool[] _secClosed = new bool[5];
        private float _timer, _bodyH, _bodyTarget;
        private string _shape = "";
        private StrategyCameraController _cam;

        // ---- живые виды строк
        private class SecView { public int Sec; public Text Count, Chevron; public Image Accent, Icon; public OutlinerHover Hover; public Image Bg; }
        private class RowView
        {
            public Image Bg, Stripe, Glow, Icon;
            public Text Title, Status, Right;
            public GameObject BarHost;
            public RectTransform Fill, Shine;
            public OutlinerHover Hover;
            public System.Action Click;
            public bool Warn, Danger;
            public float Phase;
            public Color Accent;
        }
        private readonly List<SecView> _secViews = new List<SecView>();
        private readonly List<RowView> _rowViews = new List<RowView>();

        public void Init(Canvas hud)
        {
            _collapsed = PlayerPrefs.GetInt("outliner.collapsed", 0) == 1;
            for (int i = 0; i < _secClosed.Length; i++) _secClosed[i] = PlayerPrefs.GetInt("outliner.sec" + i, 0) == 1;

            _root = LGBuild.Rect(hud.transform, "[UI] Outliner");
            _root.anchorMin = new Vector2(1, 0);
            _root.anchorMax = new Vector2(1, 1);
            _root.pivot = new Vector2(1, 1);
            _root.offsetMin = new Vector2(-Width - 10f, Bottom);
            _root.offsetMax = new Vector2(-10f, -Top);
            var mo = LG.Motion(_root.gameObject, LGAppear.Kind.SlideRight);
            mo.distance = 30f;
            mo.delay = 0.15f;

            BuildHeader();

            // Тело: высота подстраивается под содержимое
            var body = LGBuild.Panel(_root, "Body", new Color(0.012f, 0.026f, 0.040f, 0.94f), raycast: true);
            _body = body.rectTransform;
            _body.anchorMin = new Vector2(0, 1);
            _body.anchorMax = new Vector2(1, 1);
            _body.pivot = new Vector2(0.5f, 1);
            _body.anchoredPosition = new Vector2(0, -(HeadH + 4f));
            _body.sizeDelta = new Vector2(0, 60f);
            var bfx = LG.Platter(body.gameObject, 8f);
            bfx.FillMultiplier = 3f;
            bfx.SpecularMultiplier = 0.25f;
            bfx.SetRim(new Color(0.55f, 0.95f, 1f, 0.12f));
            LG.Ignore(body.gameObject, includeChildren: false);

            // мягкое свечение у верхнего края
            var glow = LGBuild.Panel(_body, "TopGlow", new Color(Cyan.r, Cyan.g, Cyan.b, 0.10f));
            glow.sprite = FactionSelectScreen.RadialSprite();
            glow.rectTransform.anchorMin = new Vector2(0, 1);
            glow.rectTransform.anchorMax = new Vector2(1, 1);
            glow.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            glow.rectTransform.sizeDelta = new Vector2(40, 90);
            glow.rectTransform.anchoredPosition = Vector2.zero;
            glow.raycastTarget = false;

            var host = LGBuild.Rect(_body, "ListHost");
            host.Stretch(4, 4, 4, 4);
            _list = LGBuild.ScrollList(host, 3f, 2);

            ApplyCollapsed();
        }

        private void BuildHeader()
        {
            var head = LGBuild.Panel(_root, "Head", new Color(0.020f, 0.045f, 0.065f, 0.96f), raycast: true);
            _headRt = head.rectTransform;
            _headRt.TopBand(0, HeadH);
            var hfx = LG.Platter(head.gameObject, 8f);
            hfx.FillMultiplier = 3f;
            hfx.SpecularMultiplier = 0.3f;
            hfx.SetRim(new Color(0.55f, 0.95f, 1f, 0.20f));
            LG.Ignore(head.gameObject, includeChildren: false);

            // бегущий блик по шапке
            var clip = LGBuild.Rect(head.transform, "Clip");
            clip.Stretch(2, 2, 2, 2);
            clip.gameObject.AddComponent<RectMask2D>();
            var sw = LGBuild.Panel(clip, "Sweep", new Color(0.6f, 1f, 1f, 0.10f));
            sw.sprite = FactionSelectScreen.RadialSprite();
            sw.raycastTarget = false;
            _sweep = sw.rectTransform;
            _sweep.anchorMin = new Vector2(0, -0.6f);
            _sweep.anchorMax = new Vector2(0.35f, 1.6f);
            _sweep.offsetMin = _sweep.offsetMax = Vector2.zero;

            // светящаяся линия под шапкой
            var line = LGBuild.Panel(head.transform, "Line", new Color(Cyan.r, Cyan.g, Cyan.b, 0.55f));
            line.sprite = FactionSelectScreen.RadialSprite();
            line.raycastTarget = false;
            line.rectTransform.anchorMin = new Vector2(0.04f, 0);
            line.rectTransform.anchorMax = new Vector2(0.96f, 0);
            line.rectTransform.sizeDelta = new Vector2(0, 2f);
            line.rectTransform.anchoredPosition = new Vector2(0, 1f);
            _headLine = line;

            _headIcon = LGIcons.Create(head.transform, LGIcon.Menu, 14, Cyan);
            _headIcon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(14, 14));
            var ht = LGBuild.Label(head.transform, "АУТЛАЙНЕР", 12, Primary, TextAnchor.MiddleLeft, bold: true);
            ht.rectTransform.Stretch(34, 0, 90, 0);

            // плашка «требует внимания»
            var chip = LGBuild.Panel(head.transform, "Alert", new Color(0.30f, 0.20f, 0.04f, 0.95f));
            chip.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-30, 0), new Vector2(46, 18));
            LG.Chip(chip.gameObject, new Color(Gold.r, Gold.g, Gold.b, 0.6f), 9f);
            var ci = LGIcons.Create(chip.transform, LGIcon.Warning, 11, Gold);
            ci.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(7, 0), new Vector2(11, 11));
            _alertText = LGBuild.Label(chip.transform, "", 11, Gold, TextAnchor.MiddleRight, bold: true);
            _alertText.rectTransform.Stretch(0, 0, 8, 0);
            _alertChip = chip.gameObject;
            TooltipHelper.Attach(_alertChip, "Требуют внимания: простаивающие корабли, свободные слоты исследований и бои");
            _alertChip.SetActive(false);

            _toggleLabel = LGBuild.Label(head.transform, "", 14, Muted, TextAnchor.MiddleRight, bold: true);
            _toggleLabel.rectTransform.Stretch(0, 0, 12, 0);
            var hb = head.gameObject.AddComponent<Button>();
            hb.transition = Selectable.Transition.None;
            hb.onClick.AddListener(() =>
            {
                _collapsed = !_collapsed;
                PlayerPrefs.SetInt("outliner.collapsed", _collapsed ? 1 : 0);
                SFXManager.Play(Sfx.UiClick);
                ApplyCollapsed();
            });
            TooltipHelper.Attach(head.gameObject, "Свернуть или развернуть аутлайнер");
        }

        private void ApplyCollapsed()
        {
            _body.gameObject.SetActive(!_collapsed);
            _toggleLabel.text = _collapsed ? "▾" : "▴";
            _shape = "";
            _timer = 0f;
        }

        private void Update()
        {
            if (_root == null) return;
            bool show = UIManager.IsGameStarted && (SystemViewManager.Instance == null || !SystemViewManager.Instance.IsInSystemView);
            if (_root.gameObject.activeSelf != show) _root.gameObject.SetActive(show);
            if (!show) return;

            Animate();
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 0.5f;
            Refresh();
        }

        // ==================== ДАННЫЕ ====================

        private struct Row
        {
            public LGIcon Icon;
            public Color IconCol;
            public string Title, Status, Right;
            public float Progress;      // < 0 — без полосы
            public Color BarCol;
            public bool Warn, Danger;
            public System.Action Click;
        }

        private void Refresh()
        {
            var sections = new List<Row>[5];
            for (int i = 0; i < 5; i++) sections[i] = new List<Row>();
            CollectResearch(sections[(int)Sec.Research]);
            CollectBuild(sections[(int)Sec.Build]);
            CollectFleets(sections);

            int alerts = 0;
            foreach (var s in sections) foreach (var r in s) if (r.Warn || r.Danger) alerts++;
            _alertChip.SetActive(alerts > 0);
            _alertText.text = alerts.ToString();
            if (_collapsed) return;

            // Форма списка: секции, их состояние и число строк (и есть ли у строки полоса)
            var sb = new StringBuilder(64);
            for (int s = 0; s < 5; s++)
            {
                sb.Append(_secClosed[s] ? 'c' : 'o');
                if (_secClosed[s]) continue;
                if (sections[s].Count == 0) sb.Append('e');
                foreach (var r in sections[s]) sb.Append(r.Progress >= 0f ? 'b' : 'r');
                sb.Append('|');
            }
            string shape = sb.ToString();
            if (shape != _shape)
            {
                _shape = shape;
                BuildViews(sections);
            }

            // Обновляем на месте
            int rv = 0;
            for (int s = 0; s < 5; s++)
            {
                var sv = _secViews[s];
                bool warn = sections[s].Exists(r => r.Warn || r.Danger);
                sv.Count.text = sections[s].Count.ToString();
                sv.Icon.color = warn ? Gold : SecColors[s];
                if (_secClosed[s]) continue;
                foreach (var r in sections[s]) Fill(_rowViews[rv++], r);
            }
        }

        private void CollectResearch(List<Row> rows)
        {
            var tm = TechnologyManager.Instance;
            if (tm == null) return;
            for (int i = 0; i < tm.Slots.Count; i++)
            {
                var slot = tm.Slots[i];
                if (slot.CurrentTech == null)
                {
                    rows.Add(new Row
                    {
                        Icon = LGIcon.Research, IconCol = Gold, Title = $"Слот {i + 1} свободен", Status = "Выберите технологию",
                        Progress = -1f, Warn = true, Click = OpenTech
                    });
                    continue;
                }
                var t = slot.CurrentTech;
                int days = tm.EstimateDays(t);
                rows.Add(new Row
                {
                    Icon = LGIcons.ForTechCategory(t.Category), IconCol = TechCategoryInfo.Color(t.Category),
                    Title = t.Name,
                    Status = slot.IsPaused ? "Пауза" : (slot.Queue.Count > 0 ? $"в очереди ещё {slot.Queue.Count}" : ""),
                    Right = slot.IsPaused ? "—" : TechnologyManager.FormatDays(days),
                    Progress = slot.ProgressNormalized, BarCol = slot.IsPaused ? Gold : Cyan,
                    Click = OpenTech
                });
            }
        }

        private void CollectBuild(List<Row> rows)
        {
            var cm = ConstructionManager.Instance;
            if (cm == null) return;
            var started = new HashSet<long>();
            int ships = 0;
            var waiting = new Dictionary<long, int>();
            foreach (var j in cm.Jobs)
            {
                if (j.Owner != 0) continue;
                bool active;
                long key = j.IsPlanetJob ? (((long)j.SystemId << 16) | (uint)j.PlanetIndex) : -1;
                if (j.IsPlanetJob) active = started.Add(key);
                else active = ships++ < ConstructionManager.ShipyardSlots;
                if (!active)
                {
                    waiting.TryGetValue(key, out int n);
                    waiting[key] = n + 1;
                    continue;
                }
                var sys = EmpireStats.GetSystem(j.SystemId);
                string where = sys != null ? sys.Name : "";
                if (j.IsPlanetJob && sys != null && j.PlanetIndex >= 0 && j.PlanetIndex < sys.Planets.Count) where = sys.Planets[j.PlanetIndex].Name;
                int sysId = j.SystemId;
                rows.Add(new Row
                {
                    Icon = JobIcon(j), IconCol = j.IsPlanetJob ? Green : Cyan,
                    Title = JobName(j), Status = where,
                    Right = TechnologyManager.FormatDays(Mathf.CeilToInt(j.DaysLeft)),
                    Progress = j.Progress, BarCol = j.IsPlanetJob ? Green : Cyan,
                    Click = () => Focus(sysId)
                });
            }
            int queued = 0;
            foreach (var kv in waiting) queued += kv.Value;
            if (queued > 0)
                rows.Add(new Row { Icon = LGIcon.Clock, IconCol = Muted, Title = $"В очереди: {queued}", Status = "ждут свободного места", Progress = -1f });
        }

        private static string JobName(ConstructionJob j) => j.Kind switch
        {
            JobKind.District => "Район: " + DistrictInfo.Name(j.District).ToLower(),
            JobKind.MiningStation => "Добывающая станция",
            JobKind.Colony => "Колония",
            JobKind.Terraform => "Терраформирование",
            _ => j.ShipType == FleetType.Science ? "Научный корабль"
               : j.ShipType == FleetType.Constructor ? "Строительный корабль"
               : j.Hull == ShipClass.Destroyer ? "Эсминец" : j.Hull == ShipClass.Frigate ? "Фрегат" : "Корвет"
        };

        private static LGIcon JobIcon(ConstructionJob j) => j.Kind switch
        {
            JobKind.District => LGIcon.Industry,
            JobKind.MiningStation => LGIcon.Minerals,
            JobKind.Colony => LGIcon.Planet,
            JobKind.Terraform => LGIcon.Globe,
            _ => j.ShipType == FleetType.Science ? LGIcon.Sensors
               : j.ShipType == FleetType.Constructor ? LGIcon.Construction : ModuleVisuals.HullIcon(j.Hull)
        };

        private void CollectFleets(List<Row>[] sections)
        {
            var fm = FleetManager.Instance;
            if (fm == null) return;
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d == null || d.Destroyed || d.OwnerId != 0) continue;
                var row = new Row { Title = string.IsNullOrEmpty(d.Name) ? "Флот" : d.Name, Progress = -1f };
                string here = SysName(d.CurrentSystemId);
                var fv = f;
                row.Click = () => SelectFleet(fv);

                switch (d.State)
                {
                    case FleetState.InHyperlane:
                    {
                        int days = Mathf.CeilToInt(RouteDays(d));
                        int dest = d.Path.Count > 0 ? LastOf(d) : d.TargetSystemId;
                        row.Status = $"→ {SysName(dest)}";
                        row.Right = TechnologyManager.FormatDays(days);
                        row.Progress = Mathf.Clamp01(1f - d.DaysRemainingInTransit / Mathf.Max(1f, d.TotalDaysForTransit));
                        row.BarCol = Muted;
                        break;
                    }
                    case FleetState.Surveying:
                        row.Status = $"Разведка: {here}";
                        row.Right = TechnologyManager.FormatDays(Mathf.CeilToInt(d.DaysRemainingSurvey));
                        row.Progress = Mathf.Clamp01(1f - d.DaysRemainingSurvey / Mathf.Max(1f, d.TotalSurveyDays));
                        row.BarCol = Cyan;
                        break;
                    case FleetState.Constructing:
                        row.Status = $"Форпост: {here}";
                        row.Right = TechnologyManager.FormatDays(Mathf.CeilToInt(d.DaysRemainingConstruction));
                        row.Progress = Mathf.Clamp01(1f - d.DaysRemainingConstruction / Mathf.Max(1f, d.TotalConstructionDays));
                        row.BarCol = Green;
                        break;
                    default:
                        if (d.InCombat) row.Status = $"БОЙ: {here}";
                        else if (d.Type != FleetType.Military && d.OrderQueue.Count == 0) { row.Status = $"Простаивает: {here}"; row.Warn = true; }
                        else row.Status = $"На орбите: {here}";
                        break;
                }

                if (d.InCombat && d.State != FleetState.Orbiting) row.Status = "БОЙ · " + row.Status;
                if (d.InCombat) { row.Danger = true; row.Warn = false; }

                switch (d.Type)
                {
                    case FleetType.Military:
                        row.Icon = ModuleVisuals.HullIcon(d.HullClass);
                        row.IconCol = d.InCombat ? Red : Cyan;
                        float hp = d.MaxHullPoints > 0f ? d.HullPoints / d.MaxHullPoints : 1f;
                        if (row.Progress < 0f && hp < 0.99f) { row.Progress = hp; row.BarCol = hp < 0.4f ? Red : Gold; }
                        if (string.IsNullOrEmpty(row.Right)) row.Right = $"{CombatMath.Power(d):0}";
                        sections[(int)Sec.Military].Add(row);
                        break;
                    case FleetType.Science:
                        row.Icon = LGIcon.Sensors;
                        row.IconCol = row.Warn ? Gold : new Color(0.42f, 0.66f, 1f);
                        sections[(int)Sec.Science].Add(row);
                        break;
                    default:
                        row.Icon = LGIcon.Construction;
                        row.IconCol = row.Warn ? Gold : Green;
                        sections[(int)Sec.Constructor].Add(row);
                        break;
                }
            }
        }

        private static int LastOf(FleetData d)
        {
            int last = d.TargetSystemId;
            foreach (int s in d.Path) last = s;
            return last;
        }

        /// <summary>Сколько дней до конца маршрута (текущий прыжок + оставшиеся).</summary>
        private static float RouteDays(FleetData d)
        {
            float speed = FleetRoute.JumpSpeed(d);
            return Mathf.Max(0f, d.DaysRemainingInTransit) / Mathf.Max(0.05f, speed) + d.Path.Count * FleetRoute.DaysPerJump(d);
        }

        private static string SysName(int id)
        {
            var s = EmpireStats.GetSystem(id);
            return s != null ? s.Name : "—";
        }

        // ==================== ВИДЫ ====================

        private void BuildViews(List<Row>[] sections)
        {
            LGBuild.Clear(_list);
            _secViews.Clear();
            _rowViews.Clear();
            for (int s = 0; s < 5; s++)
            {
                _secViews.Add(SectionHeader(s));
                if (_secClosed[s]) continue;
                if (sections[s].Count == 0) Empty(s);
                for (int i = 0; i < sections[s].Count; i++) _rowViews.Add(MakeRow(s, _rowViews.Count, sections[s][i].Progress >= 0f));
            }
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_list);
            _bodyTarget = LayoutUtility.GetPreferredHeight(_list) + 12f;
        }

        private SecView SectionHeader(int s)
        {
            var col = SecColors[s];
            var row = LGBuild.Panel(_list, "Sec_" + s, new Color(col.r * 0.10f, col.g * 0.10f + 0.02f, col.b * 0.10f + 0.03f, 0.96f), raycast: true);
            LGBuild.Height(row.gameObject, 28f);
            LG.Ignore(row.gameObject, includeChildren: false);
            var v = new SecView { Sec = s, Bg = row };

            // цветной акцент слева и затухающая линия снизу
            v.Accent = LGBuild.Panel(row.transform, "Accent", col);
            v.Accent.rectTransform.anchorMin = new Vector2(0, 0.18f);
            v.Accent.rectTransform.anchorMax = new Vector2(0, 0.82f);
            v.Accent.rectTransform.sizeDelta = new Vector2(3, 0);
            v.Accent.rectTransform.anchoredPosition = new Vector2(2.5f, 0);
            var line = LGBuild.Panel(row.transform, "Line", new Color(col.r, col.g, col.b, 0.35f));
            line.sprite = FactionSelectScreen.RadialSprite();
            line.rectTransform.anchorMin = new Vector2(-0.3f, 0);
            line.rectTransform.anchorMax = new Vector2(0.9f, 0);
            line.rectTransform.sizeDelta = new Vector2(0, 1.5f);
            line.rectTransform.anchoredPosition = new Vector2(0, 0.75f);

            v.Icon = LGIcons.Create(row.transform, SecIcons[s], 13, col);
            v.Icon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(13, 13));
            var t = LGBuild.Label(row.transform, SecNames[s], 11, Primary, TextAnchor.MiddleLeft, bold: true);
            t.rectTransform.Stretch(32, 0, 70, 0);

            var pill = LGBuild.Panel(row.transform, "Count", new Color(col.r, col.g, col.b, 0.16f));
            pill.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-28, 0), new Vector2(28, 16));
            LG.Chip(pill.gameObject, new Color(col.r, col.g, col.b, 0.45f), 8f);
            v.Count = LGBuild.Label(pill.transform, "0", 10, col, TextAnchor.MiddleCenter, bold: true);
            v.Count.rectTransform.Stretch();

            v.Chevron = LGBuild.Label(row.transform, _secClosed[s] ? "▸" : "▾", 12, Muted, TextAnchor.MiddleRight, bold: true);
            v.Chevron.rectTransform.Stretch(0, 0, 9, 0);
            v.Hover = row.gameObject.AddComponent<OutlinerHover>();

            var b = row.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() =>
            {
                _secClosed[s] = !_secClosed[s];
                PlayerPrefs.SetInt("outliner.sec" + s, _secClosed[s] ? 1 : 0);
                SFXManager.Play(Sfx.UiClick);
                _shape = "";
                _timer = 0f;
            });
            return v;
        }

        private void Empty(int s)
        {
            string text = s switch
            {
                (int)Sec.Research => "Нет активных исследований",
                (int)Sec.Build => "Ничего не строится",
                (int)Sec.Military => "Военных флотов нет",
                (int)Sec.Science => "Научных кораблей нет",
                _ => "Строителей нет"
            };
            var host = LGBuild.Rect(_list, "Empty");
            LGBuild.Height(host.gameObject, 22f);
            var t = LGBuild.Label(host, text, 10, new Color(Muted.r, Muted.g, Muted.b, 0.55f), TextAnchor.MiddleLeft);
            t.fontStyle = FontStyle.Italic;
            t.rectTransform.Stretch(14, 0, 8, 0);
        }

        private RowView MakeRow(int s, int index, bool withBar)
        {
            var v = new RowView { Phase = index * 0.37f, Accent = SecColors[s] };
            var row = LGBuild.Panel(_list, "Row", RowBg, raycast: true);
            LGBuild.Height(row.gameObject, RowH);
            LG.Ignore(row.gameObject, includeChildren: false);
            v.Bg = row;

            v.Stripe = LGBuild.Panel(row.transform, "Stripe", SecColors[s]);
            v.Stripe.rectTransform.anchorMin = new Vector2(0, 0.12f);
            v.Stripe.rectTransform.anchorMax = new Vector2(0, 0.88f);
            v.Stripe.rectTransform.sizeDelta = new Vector2(2, 0);
            v.Stripe.rectTransform.anchoredPosition = new Vector2(2, 0);

            // гнездо иконки: мягкое свечение + иконка
            v.Glow = LGBuild.Panel(row.transform, "Glow", Color.clear);
            v.Glow.sprite = FactionSelectScreen.RadialSprite();
            v.Glow.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(21, 0), new Vector2(34, 34));
            v.Icon = LGIcons.Create(row.transform, LGIcon.Fleet, 16, Cyan);
            v.Icon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(21, withBar ? 2 : 0), new Vector2(16, 16));

            v.Title = LGBuild.Label(row.transform, "", 12, Primary, TextAnchor.UpperLeft, bold: true);
            v.Title.horizontalOverflow = HorizontalWrapMode.Wrap;
            v.Title.verticalOverflow = VerticalWrapMode.Truncate;
            v.Title.rectTransform.Stretch(40, 0, 64, withBar ? 5 : 6);
            v.Status = LGBuild.Label(row.transform, "", 10, Muted, TextAnchor.UpperLeft);
            v.Status.horizontalOverflow = HorizontalWrapMode.Wrap;
            v.Status.verticalOverflow = VerticalWrapMode.Truncate;
            v.Status.rectTransform.Stretch(40, 0, 64, withBar ? 20 : 22);
            v.Right = LGBuild.Label(row.transform, "", 11, Primary, TextAnchor.UpperRight, bold: true);
            v.Right.rectTransform.Stretch(0, 0, 10, withBar ? 6 : 13);

            if (withBar)
            {
                var barHost = LGBuild.Rect(row.transform, "Bar");
                barHost.anchorMin = new Vector2(0, 0);
                barHost.anchorMax = new Vector2(1, 0);
                barHost.pivot = new Vector2(0.5f, 0);
                barHost.offsetMin = new Vector2(40, 4);
                barHost.offsetMax = new Vector2(-10, 8);
                v.BarHost = barHost.gameObject;
                v.Fill = LGBuild.Bar(barHost, Cyan, 0f, 4f);
                v.Fill.gameObject.AddComponent<RectMask2D>();
                var shine = LGBuild.Panel(v.Fill, "Shine", new Color(1f, 1f, 1f, 0.55f));
                shine.sprite = FactionSelectScreen.RadialSprite();
                shine.raycastTarget = false;
                v.Shine = shine.rectTransform;
                v.Shine.anchorMin = new Vector2(0, -1f);
                v.Shine.anchorMax = new Vector2(0.25f, 2f);
                v.Shine.offsetMin = v.Shine.offsetMax = Vector2.zero;
            }

            v.Hover = row.gameObject.AddComponent<OutlinerHover>();
            var b = row.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() =>
            {
                if (v.Click == null) return;
                SFXManager.Play(Sfx.UiClick);
                v.Click();
            });
            return v;
        }

        private static void Fill(RowView v, Row r)
        {
            v.Warn = r.Warn;
            v.Danger = r.Danger;
            v.Click = r.Click;
            v.Accent = r.Danger ? Red : r.Warn ? Gold : r.IconCol;
            v.Icon.sprite = LGIcons.Get(r.Icon);
            v.Icon.color = r.IconCol;
            if (v.Title.text != r.Title) v.Title.text = r.Title;
            string st = r.Status ?? "";
            if (v.Status.text != st) v.Status.text = st;
            v.Status.color = r.Danger ? Red : r.Warn ? Gold : Muted;
            string right = r.Right ?? "";
            if (v.Right.text != right) v.Right.text = right;
            v.Right.color = r.Danger ? Red : r.Warn ? Gold : Primary;
            if (v.Fill != null) LGBuild.SetBar(v.Fill, r.Progress, r.BarCol);
        }

        // ==================== АНИМАЦИЯ ====================

        private void Animate()
        {
            float t = Time.unscaledTime;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);

            // блик по шапке раз в несколько секунд, дыхание линии и иконки
            float sw = Mathf.Repeat(t * 0.22f, 1.8f) - 0.45f;
            _sweep.anchorMin = new Vector2(sw, -0.6f);
            _sweep.anchorMax = new Vector2(sw + 0.35f, 1.6f);
            float breath = 0.5f + 0.5f * Mathf.Sin(t * 1.6f);
            _headLine.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f + 0.35f * breath);
            _headIcon.color = Color.Lerp(Cyan, Color.white, 0.25f * breath);
            if (_alertChip.activeSelf)
            {
                float p = 0.5f + 0.5f * Mathf.Sin(t * 4f);
                _alertText.color = Color.Lerp(Gold, Color.white, 0.35f * p);
            }

            if (_collapsed) return;

            // плавная высота тела
            float avail = Mathf.Max(60f, _root.rect.height - HeadH - 4f);
            float target = Mathf.Min(_bodyTarget, avail);
            _bodyH = _bodyH <= 0f ? target : Mathf.Lerp(_bodyH, target, 1f - Mathf.Exp(-dt * 12f));
            _body.sizeDelta = new Vector2(0, _bodyH);

            foreach (var sv in _secViews)
            {
                var col = SecColors[sv.Sec];
                float h = sv.Hover.Amount(dt);
                sv.Bg.color = new Color(col.r * 0.10f + 0.04f * h, col.g * 0.10f + 0.02f + 0.05f * h, col.b * 0.10f + 0.03f + 0.06f * h, 0.96f);
                sv.Accent.color = Color.Lerp(col, Color.white, 0.4f * h);
                sv.Chevron.color = Color.Lerp(Muted, Primary, h);
            }

            foreach (var v in _rowViews)
            {
                float h = v.Hover.Amount(dt);
                float pulse = v.Danger ? 0.5f + 0.5f * Mathf.Sin(t * 7f + v.Phase)
                            : v.Warn ? 0.5f + 0.5f * Mathf.Sin(t * 3f + v.Phase) : 0f;
                var bg = v.Danger ? RowDangerBg : v.Warn ? RowWarnBg : RowBg;
                v.Bg.color = new Color(bg.r + 0.05f * h + 0.04f * pulse, bg.g + 0.07f * h + 0.02f * pulse, bg.b + 0.08f * h, bg.a);
                var a = v.Accent;
                v.Stripe.color = new Color(a.r, a.g, a.b, (v.Warn || v.Danger) ? 0.45f + 0.55f * pulse : 0.55f + 0.45f * h);
                v.Glow.color = new Color(a.r, a.g, a.b, 0.14f + 0.12f * h + 0.22f * pulse);
                float sc = 1f + 0.08f * h;
                v.Icon.rectTransform.localScale = new Vector3(sc, sc, 1f);

                if (v.Shine != null)
                {
                    float x = Mathf.Repeat(t * 0.45f + v.Phase, 2.2f) - 0.6f;
                    v.Shine.anchorMin = new Vector2(x, -1f);
                    v.Shine.anchorMax = new Vector2(x + 0.25f, 2f);
                }
            }
        }

        // ==================== ДЕЙСТВИЯ ====================

        private static void OpenTech()
        {
            UIManager.Instance?.ShowModalDimPublic();
            TechTreePanel.Instance?.Open();
        }

        private void Focus(int systemId)
        {
            var sys = EmpireStats.GetSystem(systemId);
            if (sys == null) return;
            if (_cam == null) _cam = FindAnyObjectByType<StrategyCameraController>();
            _cam?.FocusOn(sys.Position);
            GalaxyView.Instance?.SelectSystem(systemId);
        }

        private void SelectFleet(FleetView f)
        {
            if (f == null || f.Data == null) return;
            FleetManager.Instance?.SelectFleet(f);
            if (_cam == null) _cam = FindAnyObjectByType<StrategyCameraController>();
            _cam?.FocusOn(f.transform.position);
        }
    }

    /// <summary>Плавное наведение для строк аутлайнера.</summary>
    public class OutlinerHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private bool _over;
        private float _amount;
        public void OnPointerEnter(PointerEventData e) => _over = true;
        public void OnPointerExit(PointerEventData e) => _over = false;
        private void OnDisable() { _over = false; _amount = 0f; }
        public float Amount(float dt) => _amount = Mathf.MoveTowards(_amount, _over ? 1f : 0f, dt * 7f);
    }
}
