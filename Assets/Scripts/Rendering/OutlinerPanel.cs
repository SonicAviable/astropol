using System.Collections.Generic;
using System.Text;
using UnityEngine;
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
    /// по исследованию — открывает дерево технологий. Простаивающие корабли подсвечены.
    /// </summary>
    public class OutlinerPanel : MonoBehaviour
    {
        private const float Width = 300f, Top = 66f, Bottom = 284f, RowH = 40f;

        private static Color Primary => UIManager.DS.TextPrimary;
        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Cyan => UIManager.DS.NeonCyan;
        private static Color Gold => UIManager.DS.Gold;
        private static Color Green => UIManager.DS.Green;
        private static Color Red => UIManager.DS.Red;

        private enum Sec { Research, Build, Military, Science, Constructor }
        private static readonly string[] SecNames = { "ИССЛЕДОВАНИЯ", "СТРОИТЕЛЬСТВО", "ФЛОТЫ", "НАУЧНЫЕ КОРАБЛИ", "СТРОИТЕЛИ" };
        private static readonly LGIcon[] SecIcons = { LGIcon.Research, LGIcon.Construction, LGIcon.Fleet, LGIcon.Sensors, LGIcon.Wrench };

        private RectTransform _root, _list, _body;
        private Text _toggleLabel;
        private bool _collapsed;
        private readonly bool[] _secClosed = new bool[5];
        private float _timer;
        private string _sig = "";
        private StrategyCameraController _cam;

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

            // Шапка со сворачиванием
            var head = LGBuild.Panel(_root, "Head", new Color(0.02f, 0.04f, 0.06f, 0.94f), raycast: true);
            head.rectTransform.TopBand(0, 30);
            var hfx = LG.Platter(head.gameObject, 6f);
            hfx.FillMultiplier = 3f;
            hfx.SpecularMultiplier = 0.2f;
            hfx.SetRim(new Color(1f, 1f, 1f, 0.10f));
            var hi = LGIcons.Create(head.transform, LGIcon.Menu, 14, Cyan);
            hi.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(14, 14));
            var ht = LGBuild.Label(head.transform, "АУТЛАЙНЕР", 12, Primary, TextAnchor.MiddleLeft, bold: true);
            ht.rectTransform.Stretch(32, 0, 40, 0);
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

            // Тело со списком
            var body = LGBuild.Panel(_root, "Body", new Color(0.015f, 0.03f, 0.045f, 0.88f), raycast: true);
            _body = body.rectTransform;
            _body.Stretch(0, 0, 0, 34);
            var bfx = LG.Platter(body.gameObject, 6f);
            bfx.FillMultiplier = 2.8f;
            bfx.SpecularMultiplier = 0.2f;
            bfx.SetRim(new Color(1f, 1f, 1f, 0.08f));
            var host = LGBuild.Rect(_body, "ListHost");
            host.Stretch(2, 4, 2, 4);
            _list = LGBuild.ScrollList(host, 2f, 4);

            ApplyCollapsed();
        }

        private void ApplyCollapsed()
        {
            _body.gameObject.SetActive(!_collapsed);
            _toggleLabel.text = _collapsed ? "▾" : "▴";
            _sig = "";
        }

        private void Update()
        {
            if (_root == null) return;
            bool show = UIManager.IsGameStarted && (SystemViewManager.Instance == null || !SystemViewManager.Instance.IsInSystemView);
            if (_root.gameObject.activeSelf != show) _root.gameObject.SetActive(show);
            if (!show || _collapsed) return;

            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 0.5f;
            Rebuild();
        }

        // ==================== ПОСТРОЕНИЕ СПИСКА ====================

        private struct Row
        {
            public LGIcon Icon;
            public Color IconCol;
            public string Title, Status, Right;
            public float Progress;      // < 0 — без полосы
            public Color BarCol;
            public bool Warn;
            public System.Action Click;
        }

        private void Rebuild()
        {
            var sections = new List<Row>[5];
            for (int i = 0; i < 5; i++) sections[i] = new List<Row>();
            CollectResearch(sections[(int)Sec.Research]);
            CollectBuild(sections[(int)Sec.Build]);
            CollectFleets(sections);

            // Перестраиваем только если что-то поменялось (тексты и полосы)
            var sb = new StringBuilder(256);
            for (int s = 0; s < 5; s++)
            {
                sb.Append(s).Append(_secClosed[s] ? 'c' : 'o').Append(sections[s].Count).Append('|');
                if (_secClosed[s]) continue;
                foreach (var r in sections[s]) sb.Append(r.Title).Append(r.Status).Append(r.Right).Append((int)(r.Progress * 50f)).Append(';');
            }
            string sig = sb.ToString();
            if (sig == _sig) return;
            _sig = sig;

            LGBuild.Clear(_list);
            for (int s = 0; s < 5; s++)
            {
                var rows = sections[s];
                SectionHeader(s, rows.Count, rows.Exists(r => r.Warn));
                if (_secClosed[s]) continue;
                if (rows.Count == 0) Empty(s);
                foreach (var r in rows) RowView(r);
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
                        if (d.InCombat) { row.Status = $"БОЙ: {here}"; row.Warn = true; }
                        else if (d.Type != FleetType.Military && d.OrderQueue.Count == 0) { row.Status = $"Простаивает: {here}"; row.Warn = true; }
                        else row.Status = $"На орбите: {here}";
                        break;
                }

                if (d.InCombat && d.State != FleetState.Orbiting) { row.Status = "БОЙ · " + row.Status; row.Warn = true; }

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

        private void SectionHeader(int s, int count, bool warn)
        {
            var row = LGBuild.Panel(_list, "Sec_" + s, new Color(0.04f, 0.07f, 0.09f, 0.95f), raycast: true);
            LGBuild.Height(row.gameObject, 26f);
            LG.Ignore(row.gameObject, includeChildren: false);
            var ic = LGIcons.Create(row.transform, SecIcons[s], 13, warn ? Gold : Cyan);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(13, 13));
            var t = LGBuild.Label(row.transform, $"{SecNames[s]}  <color=#8AA2A8>{count}</color>", 11, Primary, TextAnchor.MiddleLeft, bold: true);
            t.rectTransform.Stretch(28, 0, 24, 0);
            var ch = LGBuild.Label(row.transform, _secClosed[s] ? "▸" : "▾", 12, Muted, TextAnchor.MiddleRight, bold: true);
            ch.rectTransform.Stretch(0, 0, 8, 0);
            var b = row.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            int idx = s;
            b.onClick.AddListener(() =>
            {
                _secClosed[idx] = !_secClosed[idx];
                PlayerPrefs.SetInt("outliner.sec" + idx, _secClosed[idx] ? 1 : 0);
                SFXManager.Play(Sfx.UiClick);
                _sig = "";
                _timer = 0f;
            });
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
            var t = LGBuild.Label(_list, text, 11, new Color(Muted.r, Muted.g, Muted.b, 0.6f), TextAnchor.MiddleLeft);
            t.fontStyle = FontStyle.Italic;
            LGBuild.Height(t.gameObject, 22f);
        }

        private void RowView(Row r)
        {
            var row = LGBuild.Panel(_list, "Row", r.Warn ? new Color(0.14f, 0.10f, 0.03f, 0.9f) : new Color(0.03f, 0.05f, 0.07f, 0.85f), raycast: true);
            LGBuild.Height(row.gameObject, RowH);
            LG.Ignore(row.gameObject, includeChildren: false);

            var ic = LGIcons.Create(row.transform, r.Icon, 16, r.IconCol);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 2), new Vector2(16, 16));
            var title = LGBuild.Label(row.transform, r.Title, 12, Primary, TextAnchor.UpperLeft, bold: true);
            title.horizontalOverflow = HorizontalWrapMode.Wrap;
            title.verticalOverflow = VerticalWrapMode.Truncate;
            title.rectTransform.Stretch(34, 0, 62, 5);
            var status = LGBuild.Label(row.transform, r.Status ?? "", 10, r.Warn ? Gold : Muted, TextAnchor.UpperLeft);
            status.horizontalOverflow = HorizontalWrapMode.Wrap;
            status.verticalOverflow = VerticalWrapMode.Truncate;
            status.rectTransform.Stretch(34, 0, 62, 21);
            if (!string.IsNullOrEmpty(r.Right))
            {
                var right = LGBuild.Label(row.transform, r.Right, 11, r.Warn ? Gold : Primary, TextAnchor.UpperRight, bold: true);
                right.rectTransform.Stretch(0, 0, 10, 6);
            }
            if (r.Progress >= 0f)
            {
                var barHost = LGBuild.Rect(row.transform, "Bar");
                barHost.anchorMin = new Vector2(0, 0);
                barHost.anchorMax = new Vector2(1, 0);
                barHost.pivot = new Vector2(0.5f, 0);
                barHost.offsetMin = new Vector2(34, 3);
                barHost.offsetMax = new Vector2(-10, 6);
                LGBuild.Bar(barHost, r.BarCol, r.Progress, 3f);
            }
            if (r.Click != null)
            {
                var b = row.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                var act = r.Click;
                b.onClick.AddListener(() => { SFXManager.Play(Sfx.UiClick); act(); });
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
}
