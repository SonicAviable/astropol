using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// «Путь к победе» — все условия победы и кто к ним ближе (кнопка в верхней панели, клавиша V).
    ///   • сверху — шкала времени партии до подсчёта очков (2200 → 2225);
    ///   • шесть колонок: очки, доминирование, военная, технологическая, экономическая, дипломатическая;
    ///     в каждой — описание и цель, лидер направления с портретом, рейтинг империй с прогрессом к цели
    ///     (ваша строка подсвечена, незнакомые империи — «Неизвестно»), строка состояния
    ///     (месяцы экономического превосходства, прогноз голосования совета, годы до подсчёта).
    /// Обновляется раз в секунду, пока открыт.
    /// </summary>
    public class VictoryPathWindow : MonoBehaviour
    {
        public static VictoryPathWindow Instance { get; private set; }
        public bool IsOpen => _root != null && LG.IsVisible(_root);

        private GameObject _root;
        private Text _subtitle, _timelineText;
        private RectTransform _timelineBar;
        private readonly List<Column> _cols = new List<Column>();
        private float _refresh;

        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Gold => UIManager.DS.Gold;
        private static readonly Color CPanel = new Color(0.020f, 0.045f, 0.065f, 0.90f);
        private const int Rows = 6;

        private enum Kind { Score, Domination, Military, Tech, Economy, Diplomacy }

        private class Column
        {
            public Kind Kind;
            public Color Accent;
            public Text Goal, LeaderName, LeaderValue, Status;
            public RawImage Portrait;
            public Image PortraitFallback, LeaderRing;
            public Row[] Rows;
        }

        private class Row
        {
            public GameObject Go;
            public Image Bg, Dot;
            public Text Rank, Name, Value;
            public RectTransform Bar;
        }

        private struct Entry
        {
            public int Owner;
            public float Value, Progress;
            public string Text;
        }

        public static void Toggle()
        {
            if (Instance == null) new GameObject("[UI] VictoryPathWindow").AddComponent<VictoryPathWindow>();
            if (Instance.IsOpen) Instance.Close(); else Instance.Open();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.V) && UIManager.IsGameStarted && !IsTyping()) Toggle();
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if ((_refresh -= Time.unscaledDeltaTime) <= 0f) { _refresh = 1f; Refresh(); }
        }

        private static bool IsTyping()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            return go != null && go.GetComponent<InputField>() != null;
        }

        public void Open()
        {
            if (_root == null) Build();
            if (_root == null) return;
            Refresh();
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
            UIManager.Instance?.ShowModalDimPublic();
        }

        public void Close()
        {
            if (_root == null) return;
            LG.Hide(_root);
            UIManager.Instance?.HideModalDimPublic();
            TooltipSystem.Hide();
        }

        // ================================================================ Построение

        private void Build()
        {
            var host = GameObject.Find("ModalCanvas");
            if (host == null) return;
            var rt = LGBuild.Rect(host.transform, "VictoryPath");
            rt.Stretch(28, 28, 28, 28);
            _root = rt.gameObject;
            _root.AddComponent<CanvasGroup>();
            var bg = _root.AddComponent<Image>();
            bg.color = new Color(0.010f, 0.025f, 0.038f, 0.97f);
            bg.raycastTarget = true;
            LG.Glass(_root).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.42f));
            LG.Motion(_root, LGAppear.Kind.Pop).fromScale = 0.97f;

            // Шапка
            var icon = LGIcons.Create(rt, LGIcon.Trophy, 26, Gold);
            icon.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -24), new Vector2(26, 26));
            var title = LGBuild.Label(rt, "ПУТЬ К ПОБЕДЕ", 28, Color.white, TextAnchor.UpperLeft, bold: true);
            title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(64, -18), new Vector2(600, 36));
            _subtitle = LGBuild.Label(rt, "", 12, Muted, TextAnchor.UpperLeft);
            _subtitle.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(66, -56), new Vector2(900, 18));

            var close = LGBuild.Button(rt, "Close", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.25f), Close, LGIcon.Close, null, 12);
            ((RectTransform)close.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -18), new Vector2(38, 38));
            TooltipHelper.Attach(close.gameObject, "Закрыть  <color=#8AA2A8>(Esc / V)</color>");

            // Шкала времени партии
            var tl = LGBuild.Rect(rt, "Timeline");
            tl.anchorMin = new Vector2(0.45f, 1); tl.anchorMax = new Vector2(1, 1);
            tl.pivot = new Vector2(0.5f, 1);
            tl.offsetMin = new Vector2(0, -70); tl.offsetMax = new Vector2(-80, -22);
            _timelineText = LGBuild.Label(tl, "", 12, Color.white, TextAnchor.UpperRight, bold: true);
            _timelineText.rectTransform.TopBand(0, 18);
            var barHost = LGBuild.Rect(tl, "Bar");
            barHost.TopBand(26, 10);
            _timelineBar = LGBuild.Bar(barHost, Gold, 0f, 8f);

            // Колонки
            var cols = LGBuild.Rect(rt, "Columns");
            cols.Stretch(20, 20, 20, 92);
            (Kind k, string t, string d, Color c, LGIcon ic)[] defs =
            {
                (Kind.Score, "ПО ОЧКАМ", $"Больше очков, чем у каждого соперника, к 1 января {VictoryManager.EndYear} года. Очки — системы, население, наука и флот.", Gold, LGIcon.Trophy),
                (Kind.Domination, "ДОМИНИРОВАНИЕ", "Поставьте форпосты примерно в трети систем галактики раньше соперников.", UIManager.DS.NeonCyan, LGIcon.Starbase),
                (Kind.Military, "ВОЕННАЯ", "Сокрушите всех соперников: у них не должно остаться ни одной системы.", new Color(1f, 0.45f, 0.42f), LGIcon.Swords),
                (Kind.Tech, "ТЕХНОЛОГИЧЕСКАЯ", $"Первым изучите {VictoryManager.ScienceTarget} технологий из 58.", UIManager.DS.Green, LGIcon.Research),
                (Kind.Economy, "ЭКОНОМИЧЕСКАЯ", $"Давайте {VictoryManager.EconomyShare * 100f:0}% продукта галактики {VictoryManager.EconomyMonths / 12} года подряд.", new Color(0.95f, 0.78f, 0.28f), LGIcon.Trade),
                (Kind.Diplomacy, "ДИПЛОМАТИЧЕСКАЯ", $"Получите 2/3 голосов Галактического совета (заседания: {VictoryManager.CouncilFirstYear}, {VictoryManager.CouncilFirstYear + VictoryManager.CouncilEvery}, {VictoryManager.CouncilFirstYear + VictoryManager.CouncilEvery * 2}).", new Color(0.72f, 0.60f, 1f), LGIcon.Handshake),
            };
            float w = 1f / defs.Length;
            for (int i = 0; i < defs.Length; i++)
            {
                var cell = LGBuild.Rect(cols, "Col");
                cell.anchorMin = new Vector2(i * w, 0);
                cell.anchorMax = new Vector2((i + 1) * w, 1);
                cell.offsetMin = new Vector2(i == 0 ? 0 : 6, 0);
                cell.offsetMax = new Vector2(i == defs.Length - 1 ? 0 : -6, 0);
                _cols.Add(BuildColumn(cell, defs[i].k, defs[i].t, defs[i].d, defs[i].c, defs[i].ic, i));
            }

            LG.Skin(_root.transform);
            _root.SetActive(false);
        }

        private static Column BuildColumn(RectTransform cell, Kind kind, string title, string desc, Color accent, LGIcon icon, int index)
        {
            var c = new Column { Kind = kind, Accent = accent, Rows = new Row[Rows] };
            var panel = LGBuild.Panel(cell, "Panel", CPanel);
            panel.rectTransform.Stretch();
            LG.Glass(panel.gameObject, 10f).SetRim(new Color(accent.r, accent.g, accent.b, 0.35f));
            var mo = LG.Motion(panel.gameObject, LGAppear.Kind.SlideUp);
            mo.delay = 0.04f * index;
            mo.distance = 24f;
            var p = panel.rectTransform;

            var bar = LGBuild.Panel(p, "Accent", accent);
            bar.rectTransform.TopBand(0, 3, 14, 14);
            LG.Ignore(bar.gameObject);

            var ic = LGIcons.Create(p, icon, 18, accent);
            ic.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -16), new Vector2(18, 18));
            var t = LGBuild.Label(p, title, 15, Color.white, TextAnchor.UpperLeft, bold: true);
            t.rectTransform.TopBand(15, 22, 40, 10);
            var d = LGBuild.Label(p, desc, 11, Muted, TextAnchor.UpperLeft, wrap: true);
            d.rectTransform.TopBand(44, 58, 14, 12);
            c.Goal = LGBuild.Label(p, "", 11, accent, TextAnchor.UpperLeft, bold: true, wrap: true);
            c.Goal.rectTransform.TopBand(104, 30, 14, 12);

            // Лидер направления: портрет, имя, значение
            var lead = LGBuild.Rect(p, "Leader");
            lead.TopBand(140, 96, 12, 12);
            var frame = LGBuild.Panel(lead, "Frame", new Color(0.04f, 0.08f, 0.10f, 1f));
            frame.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(84, 84));
            LG.Platter(frame.gameObject, 8f).SetRim(new Color(accent.r, accent.g, accent.b, 0.6f));
            c.LeaderRing = frame;
            var mask = LGBuild.Rect(frame.transform, "Mask");
            mask.Stretch(3, 3, 3, 3);
            mask.gameObject.AddComponent<RectMask2D>();
            c.Portrait = mask.gameObject.AddComponent<RawImage>();
            c.Portrait.raycastTarget = false;
            LG.Ignore(mask.gameObject);
            c.PortraitFallback = LGIcons.Create(frame.transform, LGIcon.Leader, 40, new Color(1f, 1f, 1f, 0.25f));
            c.LeaderName = LGBuild.Label(lead, "", 14, Color.white, TextAnchor.LowerLeft, bold: true, wrap: true);
            c.LeaderName.rectTransform.Stretch(94, 46, 0, 4);
            c.LeaderValue = LGBuild.Label(lead, "", 12, accent, TextAnchor.UpperLeft, bold: true, wrap: true);
            c.LeaderValue.rectTransform.Stretch(94, 4, 0, 52);

            var sep = LGBuild.Panel(p, "Sep", new Color(accent.r, accent.g, accent.b, 0.18f));
            sep.rectTransform.TopBand(244, 1, 12, 12);
            LG.Ignore(sep.gameObject);

            // Рейтинг
            const float rowH = 46f;
            for (int i = 0; i < Rows; i++)
            {
                var r = new Row();
                var rt = LGBuild.Rect(p, "Row");
                rt.TopBand(252 + i * (rowH + 4), rowH, 10, 10);
                r.Go = rt.gameObject;
                r.Bg = rt.gameObject.AddComponent<Image>();
                r.Bg.color = new Color(1f, 1f, 1f, 0.03f);
                r.Bg.raycastTarget = false;
                LG.Platter(rt.gameObject, 6f).SetRim(new Color(1f, 1f, 1f, 0.06f));
                r.Rank = LGBuild.Label(rt, "", 13, Muted, TextAnchor.MiddleCenter, bold: true);
                r.Rank.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(2, 0), new Vector2(22, 30));
                r.Dot = LGBuild.Panel(rt, "Dot", Color.white);
                r.Dot.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(34, 4), new Vector2(10, 10));
                LG.Chip(r.Dot.gameObject, new Color(1f, 1f, 1f, 0.3f));
                r.Name = LGBuild.Label(rt, "", 12, Color.white, TextAnchor.UpperLeft, bold: true);
                r.Name.rectTransform.Stretch(46, 0, 6, 6);
                r.Value = LGBuild.Label(rt, "", 11, Muted, TextAnchor.LowerLeft);
                r.Value.rectTransform.Stretch(46, 12, 6, 0);
                var bh = LGBuild.Rect(rt, "Bar");
                bh.anchorMin = new Vector2(0, 0); bh.anchorMax = new Vector2(1, 0);
                bh.offsetMin = new Vector2(46, 4); bh.offsetMax = new Vector2(-8, 8);
                r.Bar = LGBuild.Bar(bh, accent, 0f, 3f);
                c.Rows[i] = r;
            }

            c.Status = LGBuild.Label(p, "", 11, Color.white, TextAnchor.LowerLeft, wrap: true);
            c.Status.rectTransform.Stretch(14, 12, 12, 0);
            return c;
        }

        // ================================================================ Данные

        private void Refresh()
        {
            var vm = VictoryManager.Instance;
            var tm = TimeManager.Instance;
            int year = tm != null ? tm.Year : 2200;
            int left = Mathf.Max(0, VictoryManager.EndYear - year);
            _subtitle.text = $"Сравните свои успехи с соперниками. Незнакомые империи скрыты, пока вы их не встретите.";
            _timelineText.text = $"{(tm != null ? tm.GetFormattedDate() : "")}   ·   до подсчёта очков: <color=#F2C747>{left} {VictoryManager.YearsWord(left)}</color> ({VictoryManager.EndYear})";
            float span = VictoryManager.EndYear - 2200f;
            float now = tm != null ? (tm.Year - 2200f) + (tm.Month - 1) / 12f : 0f;
            LGBuild.SetBar(_timelineBar, Mathf.Clamp01(now / span), Color.Lerp(UIManager.DS.Green, UIManager.DS.Red, Mathf.Clamp01(now / span)));

            foreach (var c in _cols) FillColumn(c, vm, year);
        }

        private static List<int> Owners()
        {
            var list = new List<int> { 0 };
            foreach (var ai in AIEmpireManager.All) list.Add(ai.OwnerId);
            return list;
        }

        private static bool Alive(int owner)
        {
            if (owner == 0) return true;
            var ai = AIEmpireManager.For(owner);
            return ai != null && !ai.IsEliminated;
        }

        private void FillColumn(Column c, VictoryManager vm, int year)
        {
            var entries = new List<Entry>();
            int dom = vm != null ? vm.DominationRequired : 52;
            CouncilVotes(out var votesFor, out int total);
            foreach (int o in Owners())
            {
                var e = new Entry { Owner = o };
                switch (c.Kind)
                {
                    case Kind.Score:
                        e.Value = EmpireStats.Score(o).Total;
                        e.Text = $"Очки: {e.Value:N0}";
                        break;
                    case Kind.Domination:
                        e.Value = EmpireStats.SystemCount(o);
                        e.Progress = e.Value / Mathf.Max(1, dom);
                        e.Text = $"Систем: {e.Value:0} / {dom}";
                        break;
                    case Kind.Military:
                        e.Value = EmpireStats.MilitaryPower(o);
                        e.Text = Alive(o) ? $"Мощь флота: {e.Value:N0}" : "Повержена";
                        break;
                    case Kind.Tech:
                        e.Value = EmpireStats.TechCount(o);
                        e.Progress = e.Value / VictoryManager.ScienceTarget;
                        e.Text = $"Технологий: {e.Value:0} / {VictoryManager.ScienceTarget}";
                        break;
                    case Kind.Economy:
                        e.Value = EmpireStats.GdpShare(o);
                        e.Progress = e.Value / VictoryManager.EconomyShare;
                        e.Text = $"Доля продукта: {e.Value * 100f:0.0}%";
                        break;
                    default:
                        e.Value = EmpireStats.Votes(o);
                        e.Progress = total > 0 ? e.Value / total : 0f;
                        e.Text = $"Голосов: {e.Value:0}" + (o != 0 && votesFor.Contains(o) ? "  · за вас" : "");
                        break;
                }
                if (!Alive(o)) { e.Value = -1f; e.Progress = 0f; }
                entries.Add(e);
            }
            entries.Sort((a, b) => b.Value.CompareTo(a.Value));

            // Относительный прогресс там, где нет порога (очки, флот) — к лидеру
            float top = entries.Count > 0 ? Mathf.Max(1f, entries[0].Value) : 1f;
            if (c.Kind == Kind.Score || c.Kind == Kind.Military)
                for (int i = 0; i < entries.Count; i++) { var e = entries[i]; e.Progress = Mathf.Max(0f, e.Value) / top; entries[i] = e; }

            // Цель направления
            c.Goal.text = c.Kind switch
            {
                Kind.Score => $"ЦЕЛЬ: БОЛЬШЕ ВСЕХ К {VictoryManager.EndYear}",
                Kind.Domination => $"ЦЕЛЬ: {dom} СИСТЕМ",
                Kind.Military => $"ЦЕЛЬ: ПОВЕРГНУТЬ ВСЕХ · ОСТАЛОСЬ {CountAliveRivals()}",
                Kind.Tech => $"ЦЕЛЬ: {VictoryManager.ScienceTarget} ТЕХНОЛОГИЙ",
                Kind.Economy => $"ЦЕЛЬ: {VictoryManager.EconomyShare * 100f:0}% · {VictoryManager.EconomyMonths} МЕС. ПОДРЯД",
                _ => $"ЦЕЛЬ: {Mathf.CeilToInt(total * VictoryManager.CouncilShare)} ИЗ {total} ГОЛОСОВ"
            };

            // Лидер
            var lead = entries[0];
            bool leadKnown = Known(lead.Owner);
            c.LeaderName.text = leadKnown ? NameOf(lead.Owner) : "НЕИЗВЕСТНО";
            c.LeaderName.color = leadKnown ? Color.Lerp(OwnerCol(lead.Owner), Color.white, 0.35f) : Muted;
            c.LeaderValue.text = lead.Text;
            var portrait = leadKnown ? PortraitOf(lead.Owner) : null;
            c.Portrait.texture = portrait != null ? portrait.Texture : null;
            c.Portrait.enabled = c.Portrait.texture != null;
            var pr = c.Portrait.rectTransform.rect;
            c.Portrait.uvRect = LeaderPortraits.FaceUv(portrait, 0.40f, pr.height > 1f ? pr.width / pr.height : 1f);   // лицо крупно, без растяжения
            c.PortraitFallback.enabled = !c.Portrait.enabled;

            // Рейтинг
            for (int i = 0; i < c.Rows.Length; i++)
            {
                var r = c.Rows[i];
                if (i >= entries.Count) { r.Go.SetActive(false); continue; }
                r.Go.SetActive(true);
                var e = entries[i];
                bool me = e.Owner == 0, known = Known(e.Owner), alive = Alive(e.Owner);
                r.Rank.text = (i + 1).ToString();
                r.Rank.color = i == 0 ? c.Accent : Muted;
                r.Dot.color = known ? OwnerCol(e.Owner) : new Color(0.5f, 0.55f, 0.6f);
                r.Name.text = me ? "ВЫ · " + NameOf(0) : known ? NameOf(e.Owner) : "НЕИЗВЕСТНО";
                r.Name.color = me ? Color.white : known ? Color.Lerp(OwnerCol(e.Owner), Color.white, 0.4f) : Muted;
                r.Value.text = alive ? e.Text : "Повержена";
                r.Value.color = me ? Color.Lerp(c.Accent, Color.white, 0.3f) : Muted;
                r.Bg.color = me ? new Color(c.Accent.r, c.Accent.g, c.Accent.b, 0.14f) : new Color(1f, 1f, 1f, 0.03f);
                LGBuild.SetBar(r.Bar, Mathf.Clamp01(e.Progress), me ? c.Accent : new Color(c.Accent.r, c.Accent.g, c.Accent.b, 0.55f));
            }

            c.Status.text = StatusOf(c.Kind, vm, year, votesFor.Count, total);
        }

        private static string StatusOf(Kind kind, VictoryManager vm, int year, int supporters, int total)
        {
            switch (kind)
            {
                case Kind.Score:
                {
                    int left = Mathf.Max(0, VictoryManager.EndYear - year);
                    return $"Подсчёт через {left} {VictoryManager.YearsWord(left)}";
                }
                case Kind.Economy:
                    if (vm == null) return "";
                    if (vm.EconMonths > 0) return $"<color=#5CF59A>Вы удерживаете долю: {vm.EconMonths} из {VictoryManager.EconomyMonths} мес.</color>";
                    if (vm.RivalEconMonths > 0) return $"<color=#FF6A6A>{NameOf(vm.RivalEconOwner)}: {vm.RivalEconMonths} из {VictoryManager.EconomyMonths} мес.!</color>";
                    return "Никто не держит нужную долю";
                case Kind.Diplomacy:
                {
                    VictoryManager.CouncilForecast(out int pro, out int all);
                    int next = VictoryManager.NextCouncilYear(year);
                    string when = next > 0 ? $"Заседание — {next}" : "Заседаний больше не будет";
                    int need = Mathf.CeilToInt(all * VictoryManager.CouncilShare);
                    return $"{when}. Сейчас за вас {pro} из {all} (нужно {need}); поддерживают империй: {supporters}";
                }
                case Kind.Military:
                    return "Своих систем не должно остаться ни у одного соперника";
                default:
                    return "";
            }
        }

        private static void CouncilVotes(out HashSet<int> supporters, out int total)
        {
            supporters = new HashSet<int>();
            total = EmpireStats.Votes(0);
            foreach (var ai in AIEmpireManager.Alive)
            {
                total += EmpireStats.Votes(ai.OwnerId);
                if (!ai.AtWar && (ai.HasPact || ai.Opinion >= 40f)) supporters.Add(ai.OwnerId);
            }
        }

        private static int CountAliveRivals()
        {
            int n = 0;
            foreach (var _ in AIEmpireManager.Alive) n++;
            return n;
        }

        private static bool Known(int owner) => owner == 0 || Contacts.PlayerMet(owner);

        private static string NameOf(int owner)
        {
            if (owner == 0) return UIManager.Instance?.SelectedFaction?.Name ?? "Ваша империя";
            var ai = AIEmpireManager.For(owner);
            return ai != null ? ai.AIName : "Соперник";
        }

        private static Color OwnerCol(int owner) => FleetIndicator.OwnerColor(owner);

        private static LeaderPortraits.Entry PortraitOf(int owner)
        {
            if (owner == 0) return LeaderPortraits.ForFaction(UIManager.Instance?.SelectedFaction);
            var ai = AIEmpireManager.For(owner);
            return ai != null ? LeaderPortraits.ForFaction(ai.Faction) : null;
        }
    }
}
