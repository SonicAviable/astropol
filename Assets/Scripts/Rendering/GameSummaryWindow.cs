using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Cam;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// «Итоги партии»: графики империй по месяцам (очки, системы, население, технологии, мощь флота), итоги игрока
    /// плитками и летопись ключевых событий. Открывается с экрана победы/поражения и из меню паузы в любой момент.
    /// Данные — из <see cref="GameChronicle"/>. Щелчок по событию с местом — камера к системе.
    /// </summary>
    public sealed class GameSummaryWindow : MonoBehaviour
    {
        public static GameSummaryWindow Instance { get; private set; }
        public bool IsOpen => _root != null && LG.IsVisible(_root);
        public static bool IsOpenNow => Instance != null && Instance.IsOpen;

        private const float W = 1500f, H = 860f;
        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Gold => UIManager.DS.Gold;
        private static Color Cyan => UIManager.DS.NeonCyan;
        private static readonly Color CPanel = new Color(0.020f, 0.042f, 0.075f, 0.92f);

        private enum Metric { Score, Systems, Population, Techs, Power }
        private static readonly string[] MetricNames = { "ОЧКИ", "СИСТЕМЫ", "НАСЕЛЕНИЕ", "ТЕХНОЛОГИИ", "МОЩЬ ФЛОТА" };

        private GameObject _root;
        private RectTransform _rt, _legend, _tiles, _list;
        private Text _subtitle, _yMax, _yMid, _xStart, _xEnd, _empty;
        private LineChart _chart;
        private Metric _metric = Metric.Score;
        private readonly List<(Image bg, Text t, Metric m)> _tabs = new List<(Image, Text, Metric)>();

        private Action _onClosed;

        /// <summary>Открыть итоги; <paramref name="onClosed"/> — что сделать после закрытия (вернуть экран победы).</summary>
        public static void Open(Action onClosed = null)
        {
            if (Instance == null) new GameObject("[UI] GameSummaryWindow").AddComponent<GameSummaryWindow>();
            Instance._onClosed = onClosed;
            Instance.Show();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { UIEsc.Consume(); Close(); }
        }

        private void Show()
        {
            if (_root == null) Build();
            if (_root == null) return;
            Rebuild();
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
            UIManager.Instance?.ShowModalDimPublic();
            SFXManager.Play(Sfx.WindowOpen);
        }

        public void Close()
        {
            if (_root == null) return;
            LG.Hide(_root);
            UIManager.Instance?.HideModalDimPublic();
            TooltipSystem.Hide();
            SFXManager.Play(Sfx.WindowClose);
            var cb = _onClosed;
            _onClosed = null;
            cb?.Invoke();
        }

        // ================================================================ Построение

        private void Build()
        {
            var host = GameObject.Find("ModalCanvas");
            if (host == null) return;
            _rt = LGBuild.Rect(host.transform, "GameSummaryWindow");
            _rt.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W, H));
            _root = _rt.gameObject;
            _root.AddComponent<CanvasGroup>();
            var bg = _root.AddComponent<Image>();
            bg.color = UIManager.DS.BgDeep;
            bg.raycastTarget = true;
            LG.Glass(_root).SetRim(new Color(Gold.r, Gold.g, Gold.b, 0.45f));
            LG.Motion(_root, LGAppear.Kind.Pop).fromScale = 0.97f;

            var title = LGBuild.Label(_rt, Loc.T("ИТОГИ ПАРТИИ"), 26, Color.white, TextAnchor.UpperLeft, bold: true);
            title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -22), new Vector2(700, 36));
            title.Track(1.6f);
            _subtitle = LGBuild.Label(_rt, "", 13, Muted, TextAnchor.UpperLeft);
            _subtitle.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(37, -60), new Vector2(1200, 20));
            _subtitle.supportRichText = true;
            var close = LGBuild.Button(_rt, "Close", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.25f), Close, LGIcon.Close, null, 12);
            ((RectTransform)close.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -18), new Vector2(38, 38));
            TooltipHelper.Attach(close.gameObject, Loc.T("Закрыть  <color=#8AA2A8>(Esc)</color>"));

            // ---- График слева
            var chartPanel = LGBuild.Panel(_rt, "ChartPanel", CPanel);
            chartPanel.rectTransform.anchorMin = new Vector2(0, 1); chartPanel.rectTransform.anchorMax = new Vector2(0, 1);
            chartPanel.rectTransform.pivot = new Vector2(0, 1);
            chartPanel.rectTransform.anchoredPosition = new Vector2(24, -96);
            chartPanel.rectTransform.sizeDelta = new Vector2(1000, 520);
            LG.Platter(chartPanel.gameObject, 14f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.18f));
            var cp = chartPanel.rectTransform;

            var tabs = LGBuild.Rect(cp, "Tabs");
            tabs.TopBand(14, 32, 18, 18);
            float x = 0f;
            for (int i = 0; i < MetricNames.Length; i++)
            {
                var m = (Metric)i;
                float w = 30f + MetricNames[i].Length * 9.5f;
                var tb = LGBuild.Panel(tabs, "Tab", new Color(0.05f, 0.11f, 0.16f, 0.9f), raycast: true);
                tb.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(w, 30));
                LG.Chip(tb.gameObject, new Color(1f, 1f, 1f, 0.14f), 15f);
                var tt = LGBuild.Label(tb.transform, Loc.T(MetricNames[i]), 11, UIManager.DS.TextPrimary, TextAnchor.MiddleCenter, bold: true);
                tt.Track(1.4f);
                var b = tb.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => { _metric = m; SFXManager.Play(Sfx.UiTab, 0.6f); RefreshChart(); });
                _tabs.Add((tb, tt, m));
                x += w + 8f;
            }

            var area = LGBuild.Rect(cp, "Area");
            area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(70, 110); area.offsetMax = new Vector2(-24, -62);
            _chart = new GameObject("Chart", typeof(RectTransform)).AddComponent<LineChart>();
            _chart.transform.SetParent(area, false);
            _chart.rectTransform.Stretch();
            _chart.raycastTarget = false;

            _yMax = AxisLabel(cp, TextAnchor.MiddleRight); _yMax.rectTransform.At(new Vector2(0, 1), new Vector2(1, 0.5f), new Vector2(62, -62), new Vector2(60, 16));
            _yMid = AxisLabel(cp, TextAnchor.MiddleRight);
            _xStart = AxisLabel(cp, TextAnchor.UpperLeft); _xStart.rectTransform.At(new Vector2(0, 0), new Vector2(0, 1), new Vector2(70, 104), new Vector2(120, 16));
            _xEnd = AxisLabel(cp, TextAnchor.UpperRight); _xEnd.rectTransform.At(new Vector2(1, 0), new Vector2(1, 1), new Vector2(-24, 104), new Vector2(160, 16));
            _empty = LGBuild.Label(area, Loc.T("Летопись только началась — графики появятся через пару игровых месяцев"), 14, Muted, TextAnchor.MiddleCenter, wrap: true);

            _legend = LGBuild.Rect(cp, "Legend");
            _legend.anchorMin = new Vector2(0, 0); _legend.anchorMax = new Vector2(1, 0); _legend.pivot = new Vector2(0.5f, 0);
            _legend.offsetMin = new Vector2(24, 14); _legend.offsetMax = new Vector2(-24, 74);

            // ---- Плитки итогов справа
            var right = LGBuild.Panel(_rt, "Stats", CPanel);
            right.rectTransform.anchorMin = new Vector2(1, 1); right.rectTransform.anchorMax = new Vector2(1, 1);
            right.rectTransform.pivot = new Vector2(1, 1);
            right.rectTransform.anchoredPosition = new Vector2(-24, -96);
            right.rectTransform.sizeDelta = new Vector2(W - 1000 - 24 * 2 - 16, 520);
            LG.Platter(right.gameObject, 14f).SetRim(new Color(Gold.r, Gold.g, Gold.b, 0.18f));
            var head = LGBuild.Rect(right.rectTransform, "Head");
            head.TopBand(14, 22, 18, 18);
            LGBuild.SectionDecor(head, Loc.T("ИТОГИ ИМПЕРИИ"), LGIcon.Trophy, Gold);
            _tiles = LGBuild.Rect(right.rectTransform, "Tiles");
            _tiles.anchorMin = Vector2.zero; _tiles.anchorMax = Vector2.one;
            _tiles.offsetMin = new Vector2(16, 16); _tiles.offsetMax = new Vector2(-16, -48);

            // ---- Летопись снизу
            var chron = LGBuild.Panel(_rt, "Chronicle", CPanel);
            chron.rectTransform.anchorMin = new Vector2(0, 0); chron.rectTransform.anchorMax = new Vector2(1, 0);
            chron.rectTransform.pivot = new Vector2(0.5f, 0);
            chron.rectTransform.offsetMin = new Vector2(24, 24); chron.rectTransform.offsetMax = new Vector2(-24, 24 + 206);
            LG.Platter(chron.gameObject, 14f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.18f));
            var ch = LGBuild.Rect(chron.rectTransform, "Head");
            ch.TopBand(12, 22, 18, 18);
            LGBuild.SectionDecor(ch, Loc.T("ЛЕТОПИСЬ"), LGIcon.Calendar, Cyan);
            var listHost = LGBuild.Rect(chron.rectTransform, "ListHost");
            listHost.Stretch(10, 8, 10, 40);
            _list = LGBuild.ScrollList(listHost, 2f, 4);

            _root.SetActive(false);
        }

        private static Text AxisLabel(Transform parent, TextAnchor a)
        {
            var t = LGBuild.Label(parent, "", 11, Muted, a);
            t.raycastTarget = false;
            return t;
        }

        // ================================================================ Наполнение

        private void Rebuild()
        {
            var c = GameChronicle.Instance;
            var tm = TimeManager.Instance;
            var d = c != null ? c.Data : null;
            string date = tm != null ? tm.GetFormattedDate() : "";
            int years = tm != null && d != null ? Mathf.Max(0, tm.Year - d.StartYear) : 0;
            var vm = VictoryManager.Instance;
            string outcome = vm != null && vm.Result != VictoryManager.Outcome.None
                ? (VictoryManager.IsVictory(vm.Result) ? Loc.T("  ·  <color=#F2C766><b>Победа</b></color>") : Loc.T("  ·  <color=#FF8080><b>Поражение</b></color>"))
                : "";
            _subtitle.text = Loc.F("<b>{0}</b>  ·  {1} — {2}  ·  лет у власти: {3}{4}", GameChronicle.PlayerName(),
                d != null ? d.StartYear.ToString() : "—", date, years, outcome);

            RefreshChart();
            BuildTiles(d, years);
            BuildChronicle(d);
        }

        private int Value(ChronicleTrack t, int i)
        {
            var l = Series(t);
            return i < l.Count ? l[i] : 0;
        }

        private List<int> Series(ChronicleTrack t)
        {
            switch (_metric)
            {
                case Metric.Systems: return t.Systems;
                case Metric.Population: return t.Population;
                case Metric.Techs: return t.Techs;
                case Metric.Power: return t.Power;
                default: return t.Score;
            }
        }

        private void RefreshChart()
        {
            foreach (var (bg, t, m) in _tabs)
            {
                bool on = m == _metric;
                bg.color = on ? new Color(0.10f, 0.26f, 0.30f, 0.95f) : new Color(0.05f, 0.11f, 0.16f, 0.9f);
                t.color = on ? Color.white : Muted;
            }

            var c = GameChronicle.Instance;
            var tracks = c != null ? c.Data.Tracks : new List<ChronicleTrack>();
            int max = 1, len = 0;
            foreach (var t in tracks)
            {
                var l = Series(t);
                len = Mathf.Max(len, l.Count);
                foreach (int v in l) if (v > max) max = v;
            }
            float top = NiceMax(max);
            var series = new List<LineChart.Series>();
            // Соперники тоньше и приглушённее, игрок — поверх, ярче и толще
            foreach (var t in tracks)
                if (t.Owner != 0) series.Add(new LineChart.Series { Values = Series(t), Color = new Color(t.Color.r, t.Color.g, t.Color.b, 0.75f), Width = 2f });
            foreach (var t in tracks)
                if (t.Owner == 0) series.Add(new LineChart.Series { Values = Series(t), Color = Gold, Width = 3.5f });
            _chart.Set(series, top);
            _empty.gameObject.SetActive(len < 2);

            _yMax.text = Fmt(top);
            _yMid.text = Fmt(top / 2f);
            var area = (RectTransform)_chart.transform.parent;
            _yMid.rectTransform.At(new Vector2(0, 0), new Vector2(1, 0.5f), new Vector2(62, 110 + (area.rect.height > 0 ? area.rect.height : 348) * 0.5f), new Vector2(60, 16));
            var d = c != null ? c.Data : null;
            if (d != null && len > 0)
            {
                int endMonth = d.StartMonth - 1 + len - 1;
                _xStart.text = Loc.F("{0:00}.{1}", d.StartMonth, d.StartYear);
                _xEnd.text = Loc.F("{0:00}.{1}", endMonth % 12 + 1, d.StartYear + endMonth / 12);
            }
            else { _xStart.text = ""; _xEnd.text = ""; }

            BuildLegend(tracks, len);
        }

        private static float NiceMax(int v)
        {
            float m = Mathf.Max(4f, v * 1.1f);
            float p = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(m)));
            float n = m / p;
            n = n <= 1f ? 1f : n <= 2f ? 2f : n <= 2.5f ? 2.5f : n <= 5f ? 5f : 10f;
            return n * p;
        }

        private static string Fmt(float v) => v >= 10000f ? (v / 1000f).ToString("0.#") + Loc.T("к") : Mathf.RoundToInt(v).ToString("N0");

        private void BuildLegend(List<ChronicleTrack> tracks, int len)
        {
            LGBuild.Clear(_legend);
            var order = new List<ChronicleTrack>(tracks);
            // По последнему значению: лидеры первыми
            order.Sort((a, b) => Value(b, Series(b).Count - 1).CompareTo(Value(a, Series(a).Count - 1)));
            int cols = 3;
            float cw = (_legend.rect.width > 0 ? _legend.rect.width : 952f) / cols;
            for (int i = 0; i < order.Count && i < 6; i++)
            {
                var t = order[i];
                bool me = t.Owner == 0;
                var cell = LGBuild.Rect(_legend, "Item");
                cell.anchorMin = cell.anchorMax = new Vector2(0, 1);
                cell.pivot = new Vector2(0, 1);
                cell.sizeDelta = new Vector2(cw - 10f, 26f);
                cell.anchoredPosition = new Vector2((i % cols) * cw, -(i / cols) * 30f);
                Color col = me ? Gold : t.Color;
                var dot = LGBuild.Panel(cell, "Dot", col);
                dot.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(18, me ? 5 : 3));
                LG.Ignore(dot.gameObject);
                int last = Value(t, Series(t).Count - 1);
                var name = LGBuild.Label(cell, $"{(me ? "<b>" : "")}{t.Name}{(me ? "</b>" : "")}", 12, me ? Color.white : UIManager.DS.TextPrimary, TextAnchor.MiddleLeft);
                name.supportRichText = true;
                name.rectTransform.Stretch(26, 0, 80, 0);
                name.horizontalOverflow = HorizontalWrapMode.Overflow;
                var val = LGBuild.Label(cell, last.ToString("N0"), 12, me ? Gold : Muted, TextAnchor.MiddleRight, bold: true);
                val.rectTransform.Stretch(0, 0, 0, 0);
            }
        }

        private void BuildTiles(ChronicleSave d, int years)
        {
            LGBuild.Clear(_tiles);
            if (d == null) return;
            var obj = EmpireObjectives.Instance;
            (LGIcon icon, string label, string value, Color col)[] tiles =
            {
                (LGIcon.Calendar, Loc.T("Лет у власти"), years.ToString(), Cyan),
                (LGIcon.Trophy, Loc.T("Достижений за партию"), d.Achievements.ToString(), Gold),
                (LGIcon.Starbase, Loc.T("Пик систем"), d.PeakSystems.ToString(), Cyan),
                (LGIcon.Population, Loc.T("Пик населения"), d.PeakPopulation.ToString("N0"), UIManager.DS.Green),
                (LGIcon.Fleet, Loc.T("Пик мощи флота"), d.PeakPower.ToString("N0"), new Color(1f, 0.62f, 0.45f)),
                (LGIcon.Swords, Loc.T("Кораблей врага сбито"), d.EnemyShipsDestroyed.ToString(), new Color(1f, 0.62f, 0.45f)),
                (LGIcon.Warning, Loc.T("Своих кораблей потеряно"), d.ShipsLost.ToString(), UIManager.DS.Red),
                (LGIcon.Target, Loc.T("Систем захвачено / потеряно"), $"{d.SystemsCaptured} / {d.SystemsLost}", Gold),
                (LGIcon.Siege, Loc.T("Штурмов: побед / отбито"), $"{d.AssaultsWon} / {d.AssaultsLost}", new Color(1f, 0.62f, 0.45f)),
                (LGIcon.Defense, Loc.T("Штурмов врага отбито"), d.DefensesHeld.ToString(), UIManager.DS.Green),
                (LGIcon.Diplomacy, Loc.T("Войн / контактов"), $"{d.Wars} / {d.Contacts}", Cyan),
                (LGIcon.Check, Loc.T("Целей империи выполнено"), obj != null ? obj.Data.Completed.ToString() : "0", Gold),
            };
            float w = (_tiles.rect.width > 0 ? _tiles.rect.width : 412f), cw = (w - 10f) / 2f, ch = 70f;
            for (int i = 0; i < tiles.Length; i++)
            {
                var (icon, label, value, col) = tiles[i];
                var tile = LGBuild.Panel(_tiles, "Tile", new Color(0.03f, 0.07f, 0.11f, 0.92f));
                var rt = tile.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0, 1);
                rt.sizeDelta = new Vector2(cw, ch);
                rt.anchoredPosition = new Vector2((i % 2) * (cw + 10f), -(i / 2) * (ch + 7f));
                LG.Platter(tile.gameObject, 8f).SetRim(new Color(col.r, col.g, col.b, 0.22f));
                var ic = LGIcons.Create(rt, icon, 18, col);
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(18, 18));
                var v = LGBuild.Label(rt, value, 22, Color.white, TextAnchor.LowerLeft, bold: true);
                v.rectTransform.Stretch(44, ch * 0.42f, 8, 6);
                var l = LGBuild.Label(rt, label, 11, Muted, TextAnchor.UpperLeft, wrap: true);
                l.rectTransform.Stretch(44, 6, 8, ch * 0.6f);
            }
        }

        private void BuildChronicle(ChronicleSave d)
        {
            LGBuild.Clear(_list);
            if (d == null || d.Events.Count == 0)
            {
                var row = LGBuild.Rect(_list, "Empty");
                LGBuild.Height(row.gameObject, 30);
                var t = LGBuild.Label(row, Loc.T("Пока тихо: захваты, штурмы, войны, достижения и цели империи появятся здесь"), 13, Muted, TextAnchor.MiddleLeft);
                t.rectTransform.Stretch(10, 0, 10, 0);
                return;
            }
            // Новые сверху
            for (int i = d.Events.Count - 1; i >= 0; i--)
            {
                var e = d.Events[i];
                var row = LGBuild.Panel(_list, "Event", new Color(0, 0, 0, 0), raycast: e.SystemId >= 0);
                LGBuild.Height(row.gameObject, 26);
                Color col = e.Tone == 1 ? UIManager.DS.Green : e.Tone == 2 ? UIManager.DS.Red : Cyan;
                var dot = LGBuild.Panel(row.transform, "Dot", col);
                dot.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(14, 0), new Vector2(7, 7));
                var date = LGBuild.Label(row.transform, $"{e.Month:00}.{e.Year}", 12, Muted, TextAnchor.MiddleLeft);
                date.rectTransform.Stretch(28, 0, 0, 0);
                var txt = LGBuild.Label(row.transform, e.Text + (e.SystemId >= 0 ? "  <color=#66DBD1>›</color>" : ""), 13, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft);
                txt.supportRichText = true;
                txt.rectTransform.Stretch(110, 0, 10, 0);
                if (e.SystemId >= 0)
                {
                    int sid = e.SystemId;
                    var b = row.gameObject.AddComponent<Button>();
                    b.transition = Selectable.Transition.None;
                    b.onClick.AddListener(() => GoTo(sid));
                    TooltipHelper.Attach(row.gameObject, Loc.T("Показать на карте"));
                }
            }
        }

        private void GoTo(int systemId)
        {
            var sys = EmpireStats.GetSystem(systemId);
            if (sys == null) return;
            Close();
            FindAnyObjectByType<StrategyCameraController>()?.FocusOn(sys.Position);
            GalaxyView.Instance?.SelectSystem(sys.Id);
        }
    }
}
