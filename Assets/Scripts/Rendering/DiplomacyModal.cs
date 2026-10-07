using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Окно дипломатии. Вкладки в шапке — по одной на каждую империю ИИ. Три колонки:
    ///   • слева — соперник: характер, статус (мир / пакт / война / перемирие), отношение, его войны и миры
    ///     с другими империями ИИ, сравнение сил;
    ///   • в центре — «Почему так»: каждое слагаемое отношения ИИ с иконкой, пояснением и величиной;
    ///   • справа — действия (подарки, пакт, война, мир, торговля), входящее предложение ИИ
    ///     и прогноз: примут ли ваше предложение и почему.
    /// Все значки — векторные иконки LGIcons, без символов шрифта.
    /// </summary>
    public class DiplomacyModal : MonoBehaviour
    {
        public static DiplomacyModal Instance { get; private set; }
        public bool IsOpen => _root != null && LG.IsVisible(_root);

        private Canvas _host;
        private GameObject _root;
        private Text _title;
        private RectTransform _left, _reasons, _actions;
        private Text _reasonsTotal;
        private RectTransform _tabs;
        private int _rivalOwner = -1;
        private float _refreshTimer;

        private AIEmpireManager Current => AIEmpireManager.For(_rivalOwner) ?? AIEmpireManager.Instance;
        private bool _dirty;

        private static readonly Color CGold = UIManager.DS.Gold;
        private static readonly Color CMuted = UIManager.DS.TextMuted;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            AIEmpireManager.OnDiplomacyChanged += MarkDirty;
        }

        private void OnDestroy()
        {
            AIEmpireManager.OnDiplomacyChanged -= MarkDirty;
            if (Instance == this) Instance = null;
        }

        private void MarkDirty() => _dirty = true;

        public void BindHost(Canvas modalCanvas) { _host = modalCanvas; }

        public void Open() => Open(-1);

        /// <summary>Открыть на вкладке конкретной империи (-1 — оставить текущую).</summary>
        public void Open(int rivalOwner)
        {
            if (rivalOwner > 0) _rivalOwner = rivalOwner;
            if (_host == null)
            {
                var modal = GameObject.Find("ModalCanvas");
                if (modal != null) _host = modal.GetComponent<Canvas>();
            }
            if (_host == null) return;
            if (_root == null) Build();

            Refresh();
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
            UIManager.Instance?.ShowModalDimPublic();
            MapModeController.HideGlobal();
        }

        public void Close()
        {
            if (_root == null) return;
            LG.Hide(_root);
            UIManager.Instance?.HideModalDimPublic();
            MapModeController.ShowGlobal();
        }

        private void Update()
        {
            if (!IsOpen) return;
            _refreshTimer -= Time.unscaledDeltaTime;
            if (!_dirty && _refreshTimer > 0f) return;
            _refreshTimer = 1f;
            // Перестраиваем окно только когда что-то заметно изменилось (иначе мигали бы подсказки)
            string sig = Signature();
            if (!_dirty && sig == _signature) return;
            _dirty = false;
            Refresh();
        }

        // ================================================================ Каркас

        private void Build()
        {
            var rt = LGBuild.Rect(_host.transform, "DiplomacyModal");
            rt.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1120, 770));
            _root = rt.gameObject;
            _root.AddComponent<CanvasGroup>();
            _root.AddComponent<Image>().color = UIManager.DS.BgDeep;
            LG.Glass(_root, 26f).SetRim(LG.Palette.GoldRim);
            LG.Motion(_root, LGAppear.Kind.Pop);

            // Шапка
            var header = LGBuild.Panel(rt, "Header", UIManager.DS.BgHeader);
            header.rectTransform.TopBand(0, 56);
            LG.Header(header.gameObject);
            var hi = LGIcons.Create(header.transform, LGIcon.Diplomacy, 22, CGold);
            hi.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(22, 22));
            _title = LGBuild.Label(header.transform, "ДИПЛОМАТИЯ", 16, CGold, TextAnchor.MiddleLeft, bold: true);
            _title.rectTransform.Stretch(56, 0, 70, 0);
            _tabs = LGBuild.Rect(header.transform, "Tabs");
            _tabs.anchorMin = new Vector2(1, 0.5f);
            _tabs.anchorMax = new Vector2(1, 0.5f);
            _tabs.pivot = new Vector2(1, 0.5f);
            _tabs.anchoredPosition = new Vector2(-60, 0);
            _tabs.sizeDelta = new Vector2(520, 34);
            var close = LGBuild.Button(header.transform, "Close", new Color(0.16f, 0.20f, 0.24f), new Color(1f, 0.45f, 0.48f, 0.55f),
                                       Close, LGIcon.Close, null, 12, 15f);
            ((RectTransform)close.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-16, 0), new Vector2(32, 32));

            var body = LGBuild.Rect(rt, "Body");
            body.Stretch(18, 18, 18, 72);

            // Левая колонка — соперник
            _left = LGBuild.Rect(body, "Left");
            _left.Column(0f, 0.31f, 0, 8);

            // Центр — причины отношения
            var mid = LGBuild.Panel(body, "Reasons", UIManager.DS.BgSlot);
            mid.rectTransform.Column(0.31f, 0.66f, 8, 8);
            LG.Platter(mid.gameObject, 18f);
            var mh = LGBuild.Rect(mid.transform, "Head");
            mh.TopBand(12, 22, 16, 16);
            var mhi = LGIcons.Create(mh, LGIcon.Info, 15, UIManager.DS.NeonCyan);
            mhi.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(15, 15));
            var mht = LGBuild.Label(mh, "ПОЧЕМУ ТАКОЕ ОТНОШЕНИЕ", 11, UIManager.DS.NeonCyan, TextAnchor.MiddleLeft, bold: true);
            mht.rectTransform.offsetMin = new Vector2(22, 0);
            var listHost = LGBuild.Rect(mid.transform, "ListHost");
            listHost.Stretch(8, 46, 8, 40);
            _reasons = LGBuild.ScrollList(listHost, 6f, 4);
            var totalRow = LGBuild.Rect(mid.transform, "Total");
            totalRow.anchorMin = new Vector2(0, 0);
            totalRow.anchorMax = new Vector2(1, 0);
            totalRow.offsetMin = new Vector2(16, 10);
            totalRow.offsetMax = new Vector2(-16, 40);
            _reasonsTotal = LGBuild.Label(totalRow, "", 13, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft, bold: true);

            // Правая колонка — действия
            _actions = LGBuild.Rect(body, "Actions");
            _actions.Column(0.66f, 1f, 8, 0);

            LG.Skin(_root.transform);
            _root.SetActive(false);
        }

        // ================================================================ Обновление

        private string _signature;

        private string Signature()
        {
            var ai = Current;
            var eco = EconomyManager.Instance;
            if (ai == null) return "";
            string rel = "";
            foreach (var o in AIEmpireManager.All)
                if (o != ai) rel += $"{AIRelations.AtWar(ai.OwnerId, o.OwnerId)}{AIRelations.TruceDays(ai.OwnerId, o.OwnerId) / 30}{o.IsEliminated}|";
            return $"{ai.OwnerId}|{rel}{Mathf.RoundToInt(ai.Opinion)}|{ai.AtWar}|{ai.HasPact}|{ai.TruceDays / 30}|{ai.PendingOffer}|{ai.PendingOfferDays / 10}|" +
                   $"{Mathf.RoundToInt(ai.WarWeariness)}|{ai.OpinionBreakdown.Count}|{ai.IsEliminated}|" +
                   (eco != null ? $"{(int)(eco.Influence / 5)}|{(int)(eco.EnergyCredits / 50)}|{(int)(eco.Alloys / 25)}" : "");
        }

        private void Refresh()
        {
            var ai = Current;
            if (ai == null || _root == null) return;
            _rivalOwner = ai.OwnerId;
            _signature = Signature();
            _title.text = "ДИПЛОМАТИЯ";
            BuildTabs(ai);
            BuildLeft(ai);
            BuildReasons(ai);
            BuildActions(ai);
            LG.Skin(_root.transform);
        }

        // ---------- Вкладки империй ----------

        private void BuildTabs(AIEmpireManager current)
        {
            LGBuild.Clear(_tabs);
            int n = AIEmpireManager.All.Count;
            if (n == 0) return;
            float w = Mathf.Min(250f, 520f / n);
            for (int i = 0; i < n; i++)
            {
                var ai = AIEmpireManager.All[i];
                bool sel = ai == current;
                Color mc = ai.MapColor;
                string state = ai.IsEliminated ? "повержен" : ai.AtWar ? "война" : ai.HasPact ? "пакт" : "мир";
                var b = LGBuild.Button(_tabs, "Tab_" + ai.OwnerId,
                    sel ? new Color(mc.r * 0.35f, mc.g * 0.35f, mc.b * 0.35f, 1f) : UIManager.DS.BtnNeutral,
                    new Color(mc.r, mc.g, mc.b, sel ? 0.85f : 0.3f),
                    () => { _rivalOwner = ai.OwnerId; Refresh(); },
                    null, $"<color={LGBuild.Hex(mc)}>◆</color>  {ai.AIName.ToUpper()}  <color=#8AA2A8>· {state}</color>", 10);
                var rt = (RectTransform)b.transform;
                rt.anchorMin = new Vector2(1, 0);
                rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(1, 0.5f);
                rt.sizeDelta = new Vector2(w - 6f, 0);
                rt.anchoredPosition = new Vector2(-(n - 1 - i) * w, 0);
            }
        }

        // ---------- Левая колонка ----------

        private void BuildLeft(AIEmpireManager ai)
        {
            LGBuild.Clear(_left);
            Color ac = ai.AIEmpireColor;
            float y = 0f;

            // Карточка империи: живой портрет правителя слева, сведения справа
            var leader = LeaderPortraits.ForFaction(ai.Faction);
            const float PortW = 118f, PortH = 164f;
            var card = LGBuild.Panel(_left, "Empire", UIManager.DS.BgSlot);
            card.rectTransform.TopBand(y, PortH + 76f);
            LG.Platter(card.gameObject, 18f).SetRim(new Color(ac.r, ac.g, ac.b, 0.45f));

            var frame = LGBuild.Panel(card.transform, "Portrait", new Color(0.01f, 0.02f, 0.03f, 1f));
            frame.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -12), new Vector2(PortW, PortH));
            var ffx = LG.Platter(frame.gameObject, 8f);
            ffx.SetRim(new Color(ac.r, ac.g, ac.b, 0.55f));
            ffx.FillMultiplier = 3f;
            var portHost = LG.RoundedMask(frame.transform, 8f, 1f);
            var portrait = LeaderPortraitView.Create(portHost, leader, zoom: 1.45f);
            if (portrait == null) LGIcons.Create(frame.transform, LGIcon.Leader, 46, Color.Lerp(ac, Color.white, 0.3f));
            else if (ai.AtWar) portrait.GetComponent<RawImage>().color = new Color(1f, 0.86f, 0.84f, 1f);

            float tx = PortW + 24f;
            var name = LGBuild.Label(card.transform, ai.AIName.ToUpper(), 14, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true, wrap: true);
            name.rectTransform.TopBand(12, 38, tx, 10);
            var title = LGBuild.Label(card.transform, ai.AITitle, 11, CMuted, TextAnchor.UpperLeft);
            title.rectTransform.TopBand(50, 16, tx, 10);
            if (leader != null)
            {
                var ln = LGBuild.Label(card.transform, leader.Name, 13, Color.Lerp(ac, Color.white, 0.45f), TextAnchor.UpperLeft, bold: true);
                ln.rectTransform.TopBand(76, 18, tx, 10);
                var lt = LGBuild.Label(card.transform, leader.Title, 10, CMuted, TextAnchor.UpperLeft, wrap: true);
                lt.rectTransform.TopBand(95, 28, tx, 10);
            }
            var pers = LGBuild.Label(card.transform, $"<color={LGBuild.Hex(CGold)}>Характер: {ai.Profile.Name.ToLower()}</color>", 11,
                                     UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
            pers.rectTransform.TopBand(PortH - 8f, 18, tx, 10);
            string quote = leader != null ? $"<i>«{leader.Quote}»</i>  " : "";
            var sum = LGBuild.Label(card.transform, quote + ai.Profile.Summary, 10, CMuted, TextAnchor.UpperLeft, wrap: true);
            sum.rectTransform.Stretch(14, 8, 14, PortH + 20f);
            y += PortH + 86f;

            // Статус
            StatusChip(ai, ref y);
            NeighbourRelations(ai, ref y);

            // Отношение
            var op = LGBuild.Panel(_left, "Opinion", UIManager.DS.BgSlot);
            op.rectTransform.TopBand(y, 70);
            LG.Platter(op.gameObject, 16f);
            float o = ai.Opinion;
            Color oc = OpinionColor(o);
            var ol = LGBuild.Label(op.transform, "ОТНОШЕНИЕ К ВАМ", 10, CMuted, TextAnchor.UpperLeft, bold: true);
            ol.rectTransform.Stretch(14, 0, 14, 10);
            var ov = LGBuild.Label(op.transform, $"<b>{o:+0;-0;0}</b>  {AIEmpireManager.OpinionLabel(o)}", 15, oc, TextAnchor.UpperRight);
            ov.rectTransform.Stretch(14, 0, 14, 8);
            var barHost = LGBuild.Rect(op.transform, "Bar");
            barHost.anchorMin = new Vector2(0, 0);
            barHost.anchorMax = new Vector2(1, 0);
            barHost.offsetMin = new Vector2(14, 12);
            barHost.offsetMax = new Vector2(-14, 24);
            LGBuild.Bar(barHost, oc, Mathf.InverseLerp(AIEmpireManager.OpinionMin, AIEmpireManager.OpinionMax, o), 8f);
            y += 80f;

            // Сравнение сил
            int myPow = Mathf.RoundToInt(EmpireStats.MilitaryPower(0)), aiPow = Mathf.RoundToInt(EmpireStats.MilitaryPower(ai.OwnerId));
            int mySys = EmpireStats.SystemCount(0), aiSys = EmpireStats.SystemCount(ai.OwnerId);
            int myTech = EmpireStats.TechCount(0), aiTech = ai.ResearchedCount;
            int myScore = EmpireStats.Score(0).Total, aiScore = EmpireStats.Score(ai.OwnerId).Total;
            Compare(ref y, LGIcon.Fleet, "Флот", myPow, aiPow);
            Compare(ref y, LGIcon.Starbase, "Системы", mySys, aiSys);
            Compare(ref y, LGIcon.Research, "Технологии", myTech, aiTech);
            Compare(ref y, LGIcon.Trophy, "Очки", myScore, aiScore);
        }

        private void StatusChip(AIEmpireManager ai, ref float y)
        {
            LGIcon icon; string text; Color col; string sub;
            if (ai.IsEliminated) { icon = LGIcon.Trophy; text = "ПОВЕРЖЕН"; col = CGold; sub = "Империя потеряла все системы"; }
            else if (ai.AtWar)
            {
                icon = LGIcon.Swords; text = "ВОЙНА"; col = UIManager.DS.Red;
                sub = $"{ai.WarMonths} мес. · усталость {ai.WarWeariness:0}% · захвачено вами {ai.SystemsLostInWar}, ими {ai.SystemsTakenInWar}";
            }
            else if (ai.HasPact) { icon = LGIcon.Handshake; text = "ПАКТ О НЕНАПАДЕНИИ"; col = UIManager.DS.NeonCyan; sub = "Договор действует, пока его не расторгнут"; }
            else if (ai.TruceDays > 0) { icon = LGIcon.Peace; text = "ПЕРЕМИРИЕ"; col = UIManager.DS.Green; sub = $"Войну нельзя объявить ещё {Mathf.CeilToInt(ai.TruceDays / 30f)} мес."; }
            else { icon = LGIcon.Peace; text = "МИР"; col = UIManager.DS.Green; sub = "Войны нет, но и договоров тоже"; }

            var chip = LGBuild.Panel(_left, "Status", new Color(col.r * 0.22f, col.g * 0.22f, col.b * 0.22f, 1f));
            chip.rectTransform.TopBand(y, 62);
            LG.Platter(chip.gameObject, 16f).SetRim(new Color(col.r, col.g, col.b, 0.6f));
            var ic = LGIcons.Create(chip.transform, icon, 26, col);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(26, 26));
            var t = LGBuild.Label(chip.transform, text, 14, col, TextAnchor.UpperLeft, bold: true);
            t.rectTransform.Stretch(52, 0, 10, 10);
            var s = LGBuild.Label(chip.transform, sub, 10, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
            s.rectTransform.Stretch(52, 4, 10, 30);
            y += 72f;
        }

        /// <summary>Как эта империя живёт с другими империями ИИ: война, перемирие или мир и отношение.</summary>
        private void NeighbourRelations(AIEmpireManager ai, ref float y)
        {
            foreach (var other in AIEmpireManager.All)
            {
                if (other == ai || other.IsEliminated || ai.IsEliminated) continue;
                bool war = AIRelations.AtWar(ai.OwnerId, other.OwnerId);
                int truce = AIRelations.TruceDays(ai.OwnerId, other.OwnerId);
                var reasons = new List<string>();
                float op = AIRelations.Opinion(ai, other, reasons);
                Color col = war ? UIManager.DS.Red : truce > 0 ? UIManager.DS.Green : OpinionColor(op);
                string state = war ? "воюют" : truce > 0 ? $"перемирие {Mathf.CeilToInt(truce / 30f)} мес." : "мир";

                var row = LGBuild.Panel(_left, "Neighbour", new Color(col.r * 0.12f, col.g * 0.12f, col.b * 0.12f, 0.95f));
                row.rectTransform.TopBand(y, 36);
                LG.Platter(row.gameObject, 12f).SetRim(new Color(col.r, col.g, col.b, 0.35f));
                var ic = LGIcons.Create(row.transform, war ? LGIcon.Swords : LGIcon.Diplomacy, 16, col);
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(16, 16));
                var t = LGBuild.Label(row.transform, $"С империей <color={LGBuild.Hex(other.MapColor)}>{other.AIName}</color>: <b>{state}</b>",
                    11, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft);
                t.rectTransform.Stretch(36, 0, 50, 0);
                var v = LGBuild.Label(row.transform, $"{op:+0;-0;0}", 12, col, TextAnchor.MiddleRight, bold: true);
                v.rectTransform.Stretch(0, 0, 12, 0);
                TooltipHelper.Attach(row.gameObject, $"<b>{ai.AIName} и {other.AIName}</b>\nОтношение {op:+0;-0;0}: " +
                    (reasons.Count > 0 ? string.Join(", ", reasons) : "нейтрально") +
                    "\n<color=#8AA2A8>Соседи-ИИ сами воюют и мирятся. Пока они заняты друг другом, у вас развязаны руки.</color>");
                y += 42f;
            }
        }

        private void Compare(ref float y, LGIcon icon, string label, int mine, int theirs)
        {
            var row = LGBuild.Rect(_left, "Cmp_" + label);
            row.TopBand(y, 34, 4, 4);
            var ic = LGIcons.Create(row, icon, 14, CMuted);
            ic.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -1), new Vector2(14, 14));
            var l = LGBuild.Label(row, label, 11, CMuted, TextAnchor.UpperLeft, bold: true);
            l.rectTransform.offsetMin = new Vector2(20, 0);
            string mc = mine >= theirs ? LGBuild.Hex(UIManager.DS.Green) : LGBuild.Hex(UIManager.DS.TextPrimary);
            string tc = theirs > mine ? LGBuild.Hex(UIManager.DS.Red) : LGBuild.Hex(UIManager.DS.TextPrimary);
            LGBuild.Label(row, $"<color={mc}>вы {mine:N0}</color>  /  <color={tc}>они {theirs:N0}</color>", 11,
                          UIManager.DS.TextPrimary, TextAnchor.UpperRight, bold: true);
            var host = LGBuild.Rect(row, "Bar");
            host.anchorMin = new Vector2(0, 0);
            host.anchorMax = new Vector2(1, 0);
            host.offsetMin = new Vector2(20, 2);
            host.offsetMax = new Vector2(0, 10);
            float share = mine + theirs > 0 ? mine / (float)(mine + theirs) : 0.5f;
            LGBuild.Bar(host, UIManager.DS.NeonCyan, share, 6f);
            y += 40f;
        }

        private static Color OpinionColor(float o)
            => Color.Lerp(UIManager.DS.Red, UIManager.DS.Green, Mathf.InverseLerp(-60f, 50f, o));

        // ---------- Центр: причины ----------

        private void BuildReasons(AIEmpireManager ai)
        {
            LGBuild.Clear(_reasons);
            if (ai.OpinionBreakdown.Count == 0)
            {
                var empty = LGBuild.Label(_reasons, "Особых причин нет — нейтральное отношение.", 11, CMuted, TextAnchor.MiddleCenter);
                LGBuild.Height(empty.gameObject, 40);
            }
            foreach (var m in ai.OpinionBreakdown) ReasonRow(_reasons, m, true);

            float o = ai.Opinion;
            _reasonsTotal.text = $"Итого: <color={LGBuild.Hex(OpinionColor(o))}>{o:+0;-0;0}</color>   " +
                                 $"<color={LGBuild.Hex(CMuted)}>({AIEmpireManager.OpinionLabel(o).ToLower()}, шкала −100…+100)</color>";
        }

        private static void ReasonRow(Transform parent, OpinionModifier m, bool tall)
        {
            Color vc = m.Value >= 0f ? UIManager.DS.Green : UIManager.DS.Red;
            var row = LGBuild.Panel(parent, "Reason", m.Value >= 0f ? new Color(0.05f, 0.14f, 0.12f, 0.9f) : new Color(0.16f, 0.06f, 0.08f, 0.9f));
            LGBuild.Height(row.gameObject, tall ? 52 : 34);
            LG.Platter(row.gameObject, 12f).SetRim(new Color(vc.r, vc.g, vc.b, 0.3f));

            float isz = tall ? 30 : 22;
            var badge = LGBuild.Panel(row.transform, "Badge", new Color(vc.r * 0.3f, vc.g * 0.3f, vc.b * 0.3f, 1f));
            badge.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(isz, isz));
            LG.Platter(badge.gameObject, isz * 0.5f).FillMultiplier = 2f;
            LGIcons.Create(badge.transform, m.Icon, isz * 0.55f, Color.Lerp(vc, Color.white, 0.25f));

            var val = LGBuild.Label(row.transform, $"{m.Value:+0;-0;0}", tall ? 15 : 12, vc, TextAnchor.MiddleRight, bold: true);
            val.rectTransform.Stretch(0, 0, 12, 0);

            float left = isz + 18;
            if (tall)
            {
                var l = LGBuild.Label(row.transform, m.Label, 12, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
                l.rectTransform.Stretch(left, 0, 52, 8);
                var d = LGBuild.Label(row.transform, m.Detail, 10, CMuted, TextAnchor.UpperLeft, wrap: true);
                d.rectTransform.Stretch(left, 2, 52, 26);
            }
            else
            {
                var l = LGBuild.Label(row.transform, m.Label, 11, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft);
                l.rectTransform.Stretch(left, 0, 48, 0);
                TooltipHelper.Attach(row.gameObject, $"<b>{m.Label}</b>\n{m.Detail}");
            }
        }

        // ---------- Правая колонка: действия и прогноз ----------

        private void BuildActions(AIEmpireManager ai)
        {
            LGBuild.Clear(_actions);
            var eco = EconomyManager.Instance;
            float y = 0f;
            bool alive = !ai.IsEliminated;

            if (ai.PendingOffer != AIEmpireManager.OfferKind.None && alive)
                OfferPanel(ai, ref y);

            bool atWar = ai.AtWar;
            Action(ref y, LGIcon.Gift, $"ПОДАРОК  ·  {AIEmpireManager.GiftEnergyAmount:0} гелия-3", UIManager.DS.BtnSuccess,
                alive && !atWar && eco != null && eco.EnergyCredits >= AIEmpireManager.GiftEnergyAmount,
                atWar ? "Во время войны подарки не принимают" : "Улучшает отношение (эффект постепенно забывается)",
                () => ai.PlayerGiftEnergy());
            Action(ref y, LGIcon.Gift, $"ПОДАРОК  ·  {AIEmpireManager.GiftAlloysAmount:0} сплавов", new Color(0.10f, 0.30f, 0.38f),
                alive && !atWar && eco != null && eco.Alloys >= AIEmpireManager.GiftAlloysAmount,
                atWar ? "Во время войны подарки не принимают" : "Сплавы ценятся выше энергии",
                () => ai.PlayerGiftAlloys());

            if (ai.HasPact)
                Action(ref y, LGIcon.Handshake, "РАСТОРГНУТЬ ПАКТ", new Color(0.30f, 0.22f, 0.10f), alive,
                    "Отношение ухудшится; объявить войну после этого можно без обвинений в вероломстве",
                    ai.PlayerCancelPact);
            else
            {
                var pe = ai.EvaluatePact();
                Action(ref y, LGIcon.Handshake, $"ПАКТ О НЕНАПАДЕНИИ  ·  {AIEmpireManager.PactInfluenceCost:0} влияния", UIManager.DS.BtnPrimary,
                    alive && pe.Blocker == null && eco != null && eco.Influence >= AIEmpireManager.PactInfluenceCost,
                    pe.Blocker ?? (pe.Accept ? "Согласятся" : $"Откажут: готовность {pe.Score:+0;-0;0} из 0"),
                    () => ai.PlayerProposePact(out _));
            }

            if (atWar)
            {
                var ev = ai.EvaluatePeace();
                Action(ref y, LGIcon.Peace, $"ПРЕДЛОЖИТЬ МИР  ·  {AIEmpireManager.PeaceInfluenceCost:0} влияния", new Color(0.14f, 0.38f, 0.32f),
                    alive && eco != null && eco.Influence >= AIEmpireManager.PeaceInfluenceCost,
                    ev.Accept ? "Согласятся" : $"Откажут: готовность {ev.Score:0} из {ev.Threshold:0}",
                    () => ai.PlayerProposePeace(out _));
            }
            else
            {
                string blocker = ai.WarBlocker;
                string tip = blocker ?? (ai.HasPact ? "Нарушение пакта — тяжёлое вероломство: −40 к отношению надолго"
                                                    : "Объявление войны ухудшит отношение. Захватывайте системы осадой: флот на орбите без защитников");
                Action(ref y, LGIcon.Swords, ai.HasPact ? "ОБЪЯВИТЬ ВОЙНУ (НАРУШИТЬ ПАКТ)" : "ОБЪЯВИТЬ ВОЙНУ", UIManager.DS.BtnDanger,
                    alive && blocker == null, tip, () => ai.PlayerDeclareWar());
            }

            Action(ref y, LGIcon.Trade, "ТОРГОВЫЙ КАНАЛ", UIManager.DS.BtnPrimary, alive && !atWar,
                atWar ? "Во время войны торговля закрыта" : $"Выгодные для них сделки улучшают отношение{(ai.Personality == AIPersonality.Trader ? " (торговцы ценят это вдвое)" : "")}",
                () => { Close(); TradeModal.Instance?.Open(ai.OwnerId); });

            if (alive) Forecast(ai, ref y);
        }

        private void OfferPanel(AIEmpireManager ai, ref float y)
        {
            bool peace = ai.PendingOffer == AIEmpireManager.OfferKind.Peace;
            Color col = peace ? UIManager.DS.Green : UIManager.DS.NeonCyan;
            var panel = LGBuild.Panel(_actions, "Offer", new Color(col.r * 0.2f, col.g * 0.2f, col.b * 0.2f, 1f));
            panel.rectTransform.TopBand(y, 118);
            var fx = LG.Platter(panel.gameObject, 16f);
            fx.SetRim(new Color(col.r, col.g, col.b, 0.8f));
            fx.SetPulse(0.4f);
            var ic = LGIcons.Create(panel.transform, peace ? LGIcon.Peace : LGIcon.Handshake, 22, col);
            ic.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -12), new Vector2(22, 22));
            var t = LGBuild.Label(panel.transform, peace ? "ОНИ ПРЕДЛАГАЮТ МИР" : "ОНИ ПРЕДЛАГАЮТ ПАКТ", 12, col, TextAnchor.UpperLeft, bold: true);
            t.rectTransform.Stretch(42, 0, 10, 14);
            var r = LGBuild.Label(panel.transform, $"Причина: {ai.PendingOfferReason}. Ответ ждут ещё {ai.PendingOfferDays} дн.", 10,
                                  UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
            r.rectTransform.Stretch(12, 44, 12, 40);

            var row = LGBuild.Rect(panel.transform, "Btns");
            row.anchorMin = new Vector2(0, 0);
            row.anchorMax = new Vector2(1, 0);
            row.offsetMin = new Vector2(10, 8);
            row.offsetMax = new Vector2(-10, 40);
            var yes = LGBuild.Button(row, "Accept", UIManager.DS.BtnSuccess, UIManager.DS.Green, () => { ai.AcceptOffer(); Refresh(); },
                                     LGIcon.Check, "ПРИНЯТЬ", 11);
            ((RectTransform)yes.transform).Column(0f, 0.5f, 0, 4);
            var no = LGBuild.Button(row, "Decline", UIManager.DS.BtnDanger, UIManager.DS.Red, () => { ai.DeclineOffer(); Refresh(); },
                                    LGIcon.Close, "ОТКЛОНИТЬ", 11);
            ((RectTransform)no.transform).Column(0.5f, 1f, 4, 0);
            y += 128f;
        }

        private void Action(ref float y, LGIcon icon, string label, Color tint, bool enabled, string tip, System.Action onClick)
        {
            var b = LGBuild.Button(_actions, "Act", enabled ? tint : UIManager.DS.BtnDisabled,
                                   new Color(Mathf.Min(1f, tint.r * 2.2f), Mathf.Min(1f, tint.g * 2.2f), Mathf.Min(1f, tint.b * 2.2f), enabled ? 0.7f : 0.25f),
                                   () => { onClick?.Invoke(); SFXManager.Play("ui_click", 1f, 1.05f); Refresh(); },
                                   icon, label, 11);
            ((RectTransform)b.transform).TopBand(y, 38);
            b.interactable = enabled;
            if (!string.IsNullOrEmpty(tip)) TooltipHelper.Attach(b.gameObject, $"<b>{label}</b>\n{tip}");
            y += 44f;
        }

        /// <summary>Прогноз: примут ли мир (во время войны) или пакт (в мирное время) и почему.</summary>
        private void Forecast(AIEmpireManager ai, ref float y)
        {
            var e = ai.AtWar ? ai.EvaluatePeace() : ai.EvaluatePact();
            if (e.Reasons == null) return;
            string what = ai.AtWar ? "МИР" : "ПАКТ";

            var panel = LGBuild.Panel(_actions, "Forecast", UIManager.DS.BgSlot);
            panel.rectTransform.anchorMin = new Vector2(0, 0);
            panel.rectTransform.anchorMax = new Vector2(1, 1);
            panel.rectTransform.offsetMin = new Vector2(0, 0);
            panel.rectTransform.offsetMax = new Vector2(0, -(y + 6));
            LG.Platter(panel.gameObject, 16f);

            Color verdict = e.Blocker != null ? CMuted : e.Accept ? UIManager.DS.Green : UIManager.DS.Red;
            var head = LGBuild.Rect(panel.transform, "Head");
            head.TopBand(10, 20, 12, 12);
            var hi = LGIcons.Create(head, e.Accept && e.Blocker == null ? LGIcon.Check : LGIcon.Warning, 14, verdict);
            hi.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(14, 14));
            string verdictText = e.Blocker ?? (e.Accept ? "согласятся" : "откажут");
            var ht = LGBuild.Label(head, $"ПРОГНОЗ: {what} — {verdictText}", 11, verdict, TextAnchor.MiddleLeft, bold: true);
            ht.rectTransform.offsetMin = new Vector2(20, 0);

            if (e.Blocker != null) return;

            var sc = LGBuild.Label(panel.transform, $"Готовность {e.Score:+0;-0;0} — нужно не меньше {e.Threshold:0}", 10, CMuted, TextAnchor.UpperLeft);
            sc.rectTransform.Stretch(14, 0, 12, 34);

            var host = LGBuild.Rect(panel.transform, "List");
            host.Stretch(6, 6, 6, 52);
            var list = LGBuild.ScrollList(host, 4f, 2);
            foreach (var m in e.Reasons) ReasonRow(list, m, false);
        }
    }
}
