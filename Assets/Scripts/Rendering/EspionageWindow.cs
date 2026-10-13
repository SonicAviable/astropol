using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Центр тайных операций. Слева — ваши агенты и кандидат на найм; справа — выбранный агент:
    ///   • не назначен — выбор поста (контрразведка дома или внедрение в одну из встреченных империй);
    ///   • внедрён — разведсеть (как в Hearts of Iron: агент строит её из выбранной системы), защита цели,
    ///     досье (с 50% сети) и операции «Кража технологии»,
    ///     «Диверсия», «Раздор» со шансом успеха и стоимостью;
    ///   • контрразведка — текущая защита вашей империи.
    /// Внизу — журнал операций, в том числе чужих попыток шпионить за вами.
    /// </summary>
    public sealed class EspionageWindow : MonoBehaviour
    {
        public static EspionageWindow Instance { get; private set; }
        public bool IsOpen => _root != null && LG.IsVisible(_root);

        private const float W = 1260f, H = 800f, LeftW = 420f;
        private static readonly Color Purple = new Color(0.74f, 0.56f, 1f);
        private static readonly Color Rim = new Color(0.74f, 0.56f, 1f, 0.34f);

        private GameObject _root;
        private Text _subtitle, _counterText;
        private RectTransform _agentList, _candidateHost, _detail, _logHost;
        private int _selectedId = -1;
        private bool _discordPick;
        private bool _dirty, _subscribed;
        private float _timer;

        // ================================================================ Создание

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (Instance == this) Instance = null;
        }

        public void Build(Canvas host)
        {
            if (_root != null || host == null) return;
            var rootRt = LGBuild.Rect(host.transform, "EspionageWindow");
            rootRt.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W, H));
            _root = rootRt.gameObject;
            var bg = _root.AddComponent<Image>();
            bg.color = UIManager.DS.BgDeep;
            bg.raycastTarget = true;
            LG.Glass(_root, 26f).SetRim(Rim);
            LG.Motion(_root, LGAppear.Kind.Pop).inDuration = 0.4f;

            BuildHeader(rootRt);

            var body = LGBuild.Rect(rootRt, "Body");
            body.Stretch(20, 20, 20, 98);

            // Левая колонка: агенты и кандидат
            var left = LGBuild.Rect(body, "Left");
            left.anchorMin = new Vector2(0, 0);
            left.anchorMax = new Vector2(0, 1);
            left.pivot = new Vector2(0, 0.5f);
            left.offsetMin = Vector2.zero;
            left.offsetMax = new Vector2(LeftW, 0);

            var leftTitle = LGBuild.Label(left, "ВАШИ АГЕНТЫ", 12, Purple, TextAnchor.UpperLeft, bold: true).Track();
            leftTitle.rectTransform.TopBand(0, 20);
            var listHost = LGBuild.Rect(left, "AgentsHost");
            listHost.Stretch(0, 150, 0, 24);
            _agentList = LGBuild.ScrollList(listHost, 8f, 2);
            _candidateHost = LGBuild.Rect(left, "Candidate");
            _candidateHost.anchorMin = new Vector2(0, 0);
            _candidateHost.anchorMax = new Vector2(1, 0);
            _candidateHost.pivot = new Vector2(0.5f, 0);
            _candidateHost.offsetMin = Vector2.zero;
            _candidateHost.offsetMax = new Vector2(0, 140);

            // Правая колонка: подробности и журнал
            var right = LGBuild.Rect(body, "Right");
            right.Stretch(LeftW + 16, 0, 0, 0);
            var detailPanel = LGBuild.Panel(right, "DetailPanel", UIManager.DS.BgVisor);
            detailPanel.rectTransform.Stretch(0, 190, 0, 0);
            LG.Platter(detailPanel.gameObject, 18f);
            var detailHost = LGBuild.Rect(detailPanel.transform, "DetailHost");
            detailHost.Stretch(6, 6, 6, 6);
            _detail = LGBuild.ScrollList(detailHost, 6f, 10);

            var logPanel = LGBuild.Panel(right, "LogPanel", UIManager.DS.BgVisor);
            logPanel.rectTransform.anchorMin = new Vector2(0, 0);
            logPanel.rectTransform.anchorMax = new Vector2(1, 0);
            logPanel.rectTransform.pivot = new Vector2(0.5f, 0);
            logPanel.rectTransform.offsetMin = Vector2.zero;
            logPanel.rectTransform.offsetMax = new Vector2(0, 176);
            LG.Platter(logPanel.gameObject, 18f);
            var logTitle = LGBuild.Label(logPanel.transform, "ЖУРНАЛ ОПЕРАЦИЙ", 11, Purple, TextAnchor.UpperLeft, bold: true).Track();
            logTitle.rectTransform.TopBand(10, 18, 16, 16);
            var logScrollHost = LGBuild.Rect(logPanel.transform, "LogHost");
            logScrollHost.Stretch(6, 6, 6, 34);
            _logHost = LGBuild.ScrollList(logScrollHost, 3f, 8);

            LG.Skin(_root.transform);
            _root.SetActive(false);
        }

        private void BuildHeader(RectTransform root)
        {
            var header = LGBuild.Panel(root, "Header", UIManager.DS.BgHeader);
            header.rectTransform.TopBand(0, 78);
            LG.Header(header.gameObject);

            var badge = LGBuild.Panel(header.transform, "Badge", new Color(0.30f, 0.20f, 0.45f));
            badge.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(54, 54));
            LG.Platter(badge.gameObject, 27f).FillMultiplier = 2.4f;
            LGIcons.Create(badge.transform, LGIcon.Betrayal, 30, Purple);

            var titleRt = LGBuild.Rect(header.transform, "TitleBox");
            titleRt.Stretch(92, 0, 80, 0);
            var title = LGBuild.Label(titleRt, "ТАЙНЫЕ ОПЕРАЦИИ", 24, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true).Track(2f);
            title.rectTransform.offsetMax = new Vector2(0, -12);
            _subtitle = LGBuild.Label(titleRt, "", 12, UIManager.DS.TextMuted, TextAnchor.LowerLeft);
            _subtitle.rectTransform.offsetMin = new Vector2(0, 14);

            var close = LGBuild.Button(header.transform, "Close", new Color(0.16f, 0.20f, 0.24f),
                new Color(1f, 0.45f, 0.48f, 0.5f), Close, LGIcon.Close, null, 12, 17f);
            ((RectTransform)close.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-22, 0), new Vector2(34, 34));

            var line = LGBuild.Panel(header.transform, "Line", new Color(1f, 1f, 1f, 0.18f));
            line.rectTransform.anchorMin = new Vector2(0, 0);
            line.rectTransform.anchorMax = new Vector2(1, 0);
            line.rectTransform.sizeDelta = new Vector2(-60, 1.2f);
            LG.Line(line.gameObject, hairline: true);
        }

        // ================================================================ Открытие

        /// <summary>Открыть окно сразу на этом агенте (значок агента на карте).</summary>
        public void OpenAgent(int leaderId)
        {
            _selectedId = leaderId;
            _discordPick = false;
            Open();
        }

        public void Open()
        {
            if (_root == null) return;
            UIManager.Instance?.ShowModalDimPublic();
            MapModeController.HideGlobal();
            Subscribe();
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
            Rebuild();
        }

        public void Close()
        {
            if (_root == null || !_root.activeSelf) return;
            Unsubscribe();
            LG.Hide(_root);
            UIManager.Instance?.HideModalDimPublic();
            MapModeController.ShowGlobal();
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            EspionageManager.OnChanged += MarkDirty;
            LeaderManager.OnLeadersChanged += MarkDirty;
            if (EconomyManager.Instance != null) EconomyManager.Instance.OnResourcesChanged += MarkDirty;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            EspionageManager.OnChanged -= MarkDirty;
            LeaderManager.OnLeadersChanged -= MarkDirty;
            if (EconomyManager.Instance != null) EconomyManager.Instance.OnResourcesChanged -= MarkDirty;
        }

        private void MarkDirty() => _dirty = true;

        private void Update()
        {
            if (_root == null || !_root.activeInHierarchy) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            _timer -= Time.unscaledDeltaTime;
            if (_dirty || _timer <= 0f)
            {
                bool full = _dirty;
                _dirty = false;
                _timer = 2f;
                if (full) Rebuild(); else RefreshHeader();
            }
        }

        // ================================================================ Данные

        private Leader Selected()
        {
            var lm = LeaderManager.Instance;
            if (lm == null) return null;
            foreach (var l in lm.Of(0)) if (l.Class == LeaderClass.Agent && l.Id == _selectedId) return l;
            return null;
        }

        private void Rebuild()
        {
            var lm = LeaderManager.Instance;
            var sel = Selected();
            if (sel == null)
            {
                // выбираем первого агента
                _selectedId = -1;
                if (lm != null) foreach (var l in lm.Of(0)) if (l.Class == LeaderClass.Agent) { _selectedId = l.Id; sel = l; break; }
                _discordPick = false;
            }
            if (sel == null || sel.OpKind >= 0 || sel.Post != LeaderPost.Spy) _discordPick = false;

            RefreshHeader();
            BuildAgents();
            BuildCandidate();
            BuildDetail(sel);
            BuildLog();
        }

        private void RefreshHeader()
        {
            var es = EspionageManager.Instance;
            if (_subtitle == null) return;
            _subtitle.text = $"Защита вашей империи: <color=#BD8FFF>{EspionageManager.PlayerCounter() * 100f:0}%</color>   ·   " +
                             (es != null
                                ? $"операций: <color=#66F59E>{es.Succeeded}</color> удалось, <color=#FF7A7A>{es.Failed}</color> провалено   ·   чужих попыток сорвано: {es.Foiled}   ·   прорывов: {es.Breaches}"
                                : "");
        }

        // ================================================================ Агенты

        private static string PostLine(Leader l)
        {
            if (l.Post != LeaderPost.Spy) return "<color=#FFAA55>Без назначения</color>";
            if (l.TargetOwner == 0) return "<color=#BD8FFF>Контрразведка</color> · дома";
            string who = Contacts.NameForPlayer(l.TargetOwner);
            if (l.OpKind >= 0) return $"<color=#BD8FFF>{who}</color> · {SpyInfo.Name((SpyOp)l.OpKind).ToLower()}, ещё {l.OpMonthsLeft} мес.";
            return $"<color=#BD8FFF>{who}</color> · сеть {l.Cover:0}%";
        }

        private void BuildAgents()
        {
            LGBuild.Clear(_agentList);
            var lm = LeaderManager.Instance;
            int n = 0;
            if (lm != null)
                foreach (var l in lm.Of(0))
                {
                    if (l.Class != LeaderClass.Agent) continue;
                    n++;
                    AgentCard(l);
                }
            if (n == 0)
            {
                var host = LGBuild.Rect(_agentList, "Empty");
                LGBuild.Height(host.gameObject, 120);
                var t = LGBuild.Label(host, "Агентов пока нет.\nНаймите агента внизу — он внедрится в чужую империю, станет вашими глазами и ушами и проведёт тайные операции.",
                                      12, UIManager.DS.TextMuted, TextAnchor.MiddleCenter, wrap: true);
                t.rectTransform.Stretch(14, 0, 14, 0);
            }
        }

        private void AgentCard(Leader l)
        {
            bool selected = l.Id == _selectedId;
            var card = LGBuild.Panel(_agentList, "Agent", new Color(Purple.r * 0.10f, Purple.g * 0.10f, Purple.b * 0.12f, 0.95f), raycast: true);
            LGBuild.Height(card.gameObject, 92);
            LG.Platter(card.gameObject, 16f).SetRim(new Color(Purple.r, Purple.g, Purple.b, selected ? 0.95f : 0.32f));
            var btn = card.gameObject.AddComponent<Button>();
            int id = l.Id;
            btn.onClick.AddListener(() =>
            {
                _selectedId = id;
                _discordPick = false;
                SFXManager.Play(Sfx.UiClick);
                Rebuild();
            });

            var frame = LGBuild.Rect(card.transform, "Face");
            frame.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(60, 60));
            if (!LeaderThumb.Create(frame, 14f).Set(l, Purple))
                LGIcons.Create(frame, LGIcon.Betrayal, 30, Purple);

            var name = LGBuild.Label(card.transform, l.Name, 14, Color.white, TextAnchor.UpperLeft, bold: true);
            name.rectTransform.Stretch(86, 0, 12, 12);
            name.rectTransform.offsetMax = new Vector2(-12, -10);
            var sub = LGBuild.Label(card.transform, $"Агент · ур. {l.Level}   {TraitsText(l)}", 10, UIManager.DS.TextMuted, TextAnchor.UpperLeft);
            sub.rectTransform.TopBand(30, 14, 86, 12);
            var post = LGBuild.Label(card.transform, PostLine(l), 11, UIManager.DS.TextPrimary, TextAnchor.UpperLeft);
            post.rectTransform.TopBand(48, 16, 86, 12);

            var barHost = LGBuild.Rect(card.transform, "Bar");
            barHost.anchorMin = new Vector2(0, 0);
            barHost.anchorMax = new Vector2(1, 0);
            barHost.offsetMin = new Vector2(86, 12);
            barHost.offsetMax = new Vector2(-14, 18);
            var bar = LGBuild.Bar(barHost, Purple, l.Post == LeaderPost.Spy && l.TargetOwner > 0 ? l.Cover / 100f : 0f, 5f);
            if (l.OpKind >= 0)
            {
                float total = SpyInfo.Months((SpyOp)l.OpKind);
                LGBuild.SetBar(bar, 1f - l.OpMonthsLeft / Mathf.Max(1f, total), UIManager.DS.Gold);
            }
            TooltipHelper.Attach(card.gameObject, AgentTooltip(l));
        }

        private static string TraitsText(Leader l)
        {
            var sb = new StringBuilder();
            foreach (var id in l.Traits)
            {
                var t = LeaderManager.Trait(id);
                if (t == null) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(t.Name);
            }
            return sb.ToString();
        }

        private static string AgentTooltip(Leader l)
        {
            var sb = new StringBuilder();
            sb.Append($"<b>{l.Name}</b>\nАгент · уровень {l.Level}\n<color=#8AA2A8>Уровень:</color> {LeaderManager.LevelBonusText(l)}");
            foreach (var id in l.Traits)
            {
                var t = LeaderManager.Trait(id);
                if (t != null) sb.Append($"\n<color=#FFCC52>{t.Name}</color> — {t.Desc}");
            }
            return sb.ToString();
        }

        private void BuildCandidate()
        {
            LGBuild.Clear(_candidateHost);
            var lm = LeaderManager.Instance;
            Leader cand = null;
            if (lm != null) foreach (var c in lm.Candidates) if (c.Class == LeaderClass.Agent) { cand = c; break; }

            var card = LGBuild.Panel(_candidateHost, "Cand", new Color(0.05f, 0.07f, 0.10f, 0.95f));
            card.rectTransform.Stretch(0, 0, 0, 0);
            LG.Platter(card.gameObject, 16f).SetRim(new Color(1f, 1f, 1f, 0.14f));

            var title = LGBuild.Label(card.transform, "КАНДИДАТ НА НАЙМ", 10, UIManager.DS.TextMuted, TextAnchor.UpperLeft, bold: true).Track();
            title.rectTransform.TopBand(10, 14, 16, 16);
            if (cand == null || lm == null)
            {
                var t = LGBuild.Label(card.transform, "Кандидатов нет — список обновляется каждый год.", 11, UIManager.DS.TextMuted, TextAnchor.MiddleLeft, wrap: true);
                t.rectTransform.Stretch(16, 10, 16, 30);
                return;
            }

            var frame = LGBuild.Rect(card.transform, "Face");
            frame.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -34), new Vector2(54, 54));
            if (!LeaderThumb.Create(frame, 12f).Set(cand, Purple))
                LGIcons.Create(frame, LGIcon.Betrayal, 28, Purple);
            var name = LGBuild.Label(card.transform, $"{cand.Name} · {cand.Age} лет", 13, Color.white, TextAnchor.UpperLeft, bold: true);
            name.rectTransform.TopBand(32, 18, 80, 12);
            var tr = LGBuild.Label(card.transform, TraitsText(cand), 11, UIManager.DS.Gold, TextAnchor.UpperLeft, wrap: true);
            tr.rectTransform.TopBand(52, 32, 80, 12);

            bool can = lm.CanHire(out string why);
            var hire = LGBuild.Button(card.transform, "Hire", can ? UIManager.DS.BtnPrimary : UIManager.DS.BtnNeutral,
                new Color(Purple.r, Purple.g, Purple.b, 0.7f), () =>
                {
                    lm.Hire(cand);
                    Rebuild();
                }, null, $"НАНЯТЬ  ★{LeaderManager.HireInfluence:0}", 11);
            var hrt = (RectTransform)hire.transform;
            hrt.anchorMin = new Vector2(0, 0);
            hrt.anchorMax = new Vector2(1, 0);
            hrt.pivot = new Vector2(0.5f, 0);
            hrt.offsetMin = new Vector2(14, 12);
            hrt.offsetMax = new Vector2(-14, 44);
            hire.interactable = can;
            if (!can) TooltipHelper.Attach(hire.gameObject, why);
            else TooltipHelper.Attach(hire.gameObject, $"Найм {LeaderManager.HireInfluence:0} влияния, содержание {LeaderManager.UpkeepEnergy:0.#} гелия-3 в месяц. Лидеров: {lm.CountFor(0)}/{LeaderManager.MaxLeaders}");
        }

        // ================================================================ Подробности

        private RectTransform Block(float height, Color tint, float radius = 14f)
        {
            var p = LGBuild.Panel(_detail, "Block", tint);
            LGBuild.Height(p.gameObject, height);
            LG.Platter(p.gameObject, radius).SetRim(new Color(1f, 1f, 1f, 0.10f));
            return p.rectTransform;
        }

        private Text Heading(string text, Color col)
        {
            var host = LGBuild.Rect(_detail, "Heading");
            LGBuild.Height(host.gameObject, 26);
            var t = LGBuild.Label(host, text, 12, col, TextAnchor.LowerLeft, bold: true).Track();
            t.rectTransform.Stretch(6, 2, 6, 0);
            return t;
        }

        private Text Paragraph(string text, int size = 12, float height = 0f, Color? col = null)
        {
            var host = LGBuild.Rect(_detail, "Text");
            // оценка высоты: ~95 символов в строке при ширине окна
            float h = height > 0f ? height : Mathf.Max(22f, 16f * Mathf.Ceil(text.Length / 95f) + 6f);
            LGBuild.Height(host.gameObject, h);
            var t = LGBuild.Label(host, text, size, col ?? UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
            t.rectTransform.Stretch(8, 0, 8, 0);
            t.lineSpacing = 1.15f;
            return t;
        }

        private void BuildDetail(Leader a)
        {
            LGBuild.Clear(_detail);
            if (a == null) { BuildHelp(); return; }

            // Заголовок агента
            var head = Block(66, new Color(Purple.r * 0.12f, Purple.g * 0.10f, Purple.b * 0.16f, 0.95f));
            var hn = LGBuild.Label(head, a.Name, 20, Color.white, TextAnchor.UpperLeft, bold: true);
            hn.rectTransform.TopBand(8, 28, 18, 18);
            var hs = LGBuild.Label(head, $"Агент · уровень {a.Level} · {LeaderManager.LevelBonusText(a)}", 11, UIManager.DS.TextMuted, TextAnchor.UpperLeft);
            hs.rectTransform.TopBand(36, 16, 18, 18);
            var ht = LGBuild.Label(head, TraitsLine(a), 11, UIManager.DS.Gold, TextAnchor.UpperLeft);
            ht.rectTransform.TopBand(50, 14, 18, 18);

            if (a.Post != LeaderPost.Spy) { BuildAssignment(a); return; }
            if (a.TargetOwner == 0) { BuildCounter(a); return; }
            BuildEmbedded(a);
        }

        private static string TraitsLine(Leader l)
        {
            var sb = new StringBuilder();
            foreach (var id in l.Traits)
            {
                var t = LeaderManager.Trait(id);
                if (t == null) continue;
                if (sb.Length > 0) sb.Append("   ·   ");
                sb.Append($"{t.Name}: {t.Desc}");
            }
            return sb.ToString();
        }

        private void BuildHelp()
        {
            Heading("КАК ЭТО РАБОТАЕТ", Purple);
            Paragraph("Агент — четвёртый класс лидеров. Его можно внедрить в любую империю, которую вы встретили.");
            Paragraph("• Агент строит разведсеть из выбранной системы цели; сеть расползается к соседям по гиперкоридорам, без агента тает. На карте видно её зону.\n• С сетью 20% вы видите всю территорию и все флоты цели, с 50% открывается досье.\n• Операции: кража технологии, диверсия, раздор между двумя империями.", 12, 86);
            Paragraph("Провал грозит поимкой и скандалом. Агент дома, шифрование и «Криптография» защищают вас от чужих агентов: ИИ тоже шпионит.", 12, 40, UIManager.DS.TextMuted);
        }

        // ---- Не назначен: выбор поста ----

        private void BuildAssignment(Leader a)
        {
            var lm = LeaderManager.Instance;
            Heading("КУДА НАПРАВИТЬ АГЕНТА", Purple);
            AssignRow(a, 0, LGIcon.Shipyard, "Контрразведка дома",
                $"Защита вашей империи +{(0.10f + a.Level * 0.04f + (a.Has("agt_shadow") ? 0.08f : 0f)) * 100f:0}%: чужие агенты чаще проваливаются и раскрываются",
                lm?.SpyOn(0, 0));
            int shown = 0;
            foreach (var ai in AIEmpireManager.All)
            {
                if (ai.IsEliminated || !Contacts.PlayerMet(ai.OwnerId)) continue;
                shown++;
                AssignRow(a, ai.OwnerId, LGIcon.Betrayal, Contacts.NameForPlayer(ai.OwnerId),
                    $"Внедрение · отношение {ai.Opinion:+0;-0;0} · защита цели {EspionageManager.TargetCounter(ai) * 100f:0}%" + (ai.AtWar ? " · война" : ""),
                    lm?.SpyOn(0, ai.OwnerId));
            }
            if (shown == 0) Paragraph("Вы пока не встретили ни одной империи — внедряться некуда. Исследуйте галактику.", 12, 40, UIManager.DS.TextMuted);
        }

        private void AssignRow(Leader a, int target, LGIcon icon, string title, string sub, Leader current)
        {
            var row = Block(60, new Color(0.05f, 0.07f, 0.10f, 0.95f));
            Color c = target == 0 ? UIManager.DS.NeonCyan : FleetIndicator.OwnerColor(target);
            var ic = LGIcons.Create(row, icon, 22, c);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(22, 22));
            var t = LGBuild.Label(row, title, 14, Color.white, TextAnchor.UpperLeft, bold: true);
            t.rectTransform.TopBand(10, 20, 56, 190);
            var s = LGBuild.Label(row, sub, 10, UIManager.DS.TextMuted, TextAnchor.UpperLeft, wrap: true);
            s.rectTransform.TopBand(32, 22, 56, 190);
            if (current != null && current != a)
            {
                var cur = LGBuild.Label(row, $"сейчас: {current.Name}", 10, UIManager.DS.Gold, TextAnchor.MiddleRight);
                cur.rectTransform.anchorMin = new Vector2(1, 0.5f);
                cur.rectTransform.anchorMax = new Vector2(1, 0.5f);
                cur.rectTransform.pivot = new Vector2(1, 0.5f);
                cur.rectTransform.sizeDelta = new Vector2(150, 16);
                cur.rectTransform.anchoredPosition = new Vector2(-166, 0);
            }
            var btn = LGBuild.Button(row, "Go", UIManager.DS.BtnPrimary, new Color(Purple.r, Purple.g, Purple.b, 0.7f), () =>
            {
                LeaderManager.Instance?.AssignSpy(a, target);
                SFXManager.Play(Sfx.UiConfirm);
                Rebuild();
            }, null, "НАЗНАЧИТЬ", 11);
            ((RectTransform)btn.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-14, 0), new Vector2(140, 34));
        }

        // ---- Контрразведка ----

        private void BuildCounter(Leader a)
        {
            Heading("КОНТРРАЗВЕДКА ДОМА", Purple);
            float own = 0.10f + a.Level * 0.04f + (a.Has("agt_shadow") ? 0.08f : 0f);
            Paragraph($"Агент охраняет вашу империю. Его вклад в защиту: <color=#BD8FFF>+{own * 100f:0}%</color>. Общая защита сейчас — " +
                      $"<color=#BD8FFF>{EspionageManager.PlayerCounter() * 100f:0}%</color> (базовые 10% и технологии шифрования).", 12, 48);
            Paragraph("Чем выше защита, тем чаще чужие агенты проваливаются, и тем чаще вы узнаёте, чья это работа. Агент набирает опыт за службу и за каждую сорванную операцию.", 12, 48, UIManager.DS.TextMuted);
            RecallButton(a);
        }

        // ---- Внедрён в империю ----

        private void BuildEmbedded(Leader a)
        {
            var ai = AIEmpireManager.For(a.TargetOwner);
            if (ai == null) { RecallButton(a); return; }
            var es = EspionageManager.Instance;
            Color oc = FleetIndicator.OwnerColor(ai.OwnerId);

            Heading($"ВНЕДРЁН В ИМПЕРИЮ  ·  {ai.AIName.ToUpper()}", oc);

            // Прикрытие и защита цели
            var cover = Block(86, new Color(0.05f, 0.07f, 0.10f, 0.95f));
            var ct = LGBuild.Label(cover, $"РАЗВЕДСЕТЬ В ИМПЕРИИ  <color=#BD8FFF>{a.Cover:0}%</color>", 12, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
            ct.rectTransform.TopBand(10, 18, 18, 18);
            var bh = LGBuild.Rect(cover, "CoverBar");
            bh.TopBand(34, 8, 18, 18);
            LGBuild.Bar(bh, Purple, a.Cover / 100f, 8f);
            // Отметки порогов 20 / 50
            foreach (float m in new[] { EspionageManager.IntelCover, EspionageManager.DeepIntelCover })
            {
                var tick = LGBuild.Panel(bh, "Tick", new Color(1f, 1f, 1f, 0.55f));
                tick.rectTransform.anchorMin = new Vector2(m / 100f, 0);
                tick.rectTransform.anchorMax = new Vector2(m / 100f, 1);
                tick.rectTransform.sizeDelta = new Vector2(2f, 6f);
                LG.Ignore(tick.gameObject);
            }
            bool intel = a.Cover >= EspionageManager.IntelCover, deep = a.Cover >= EspionageManager.DeepIntelCover;
            var cs = LGBuild.Label(cover,
                $"{(intel ? "<color=#66F59E>✓</color>" : "<color=#8AA2A8>○</color>")} обзор территории и флотов (20%)    " +
                $"{(deep ? "<color=#66F59E>✓</color>" : "<color=#8AA2A8>○</color>")} досье (50%)    " +
                $"Защита цели: <color=#FF9F7A>{EspionageManager.TargetCounter(ai) * 100f:0}%</color>    Отношение: {ai.Opinion:+0;-0;0}",
                11, UIManager.DS.TextMuted, TextAnchor.UpperLeft);
            cs.rectTransform.TopBand(52, 18, 18, 18);
            var gain = LGBuild.Label(cover, a.OpKind >= 0 ? "Агент занят операцией — сеть не растёт (но и не тает)" :
                $"Прирост в системе агента: +{EspionageManager.BuildRate(a).Total:0.#} в месяц, у соседей ×0,6, дальше ×0,3. Сила в империи — среднее по её системам (столица ×3, колонии ×2)",
                10, UIManager.DS.TextMuted, TextAnchor.UpperLeft);
            gain.rectTransform.TopBand(68, 14, 18, 18);

            BuildRegion(a, ai);

            // Досье
            Heading("ДОСЬЕ", UIManager.DS.TextMuted);
            if (deep) BuildDossier(ai);
            else Paragraph("Досье закрыто. Оно откроется, когда разведсеть в империи достигнет 50%.", 12, 28, UIManager.DS.TextMuted);

            // Операции
            Heading("ОПЕРАЦИИ", Purple);
            if (a.OpKind >= 0) BuildActiveOp(a, ai);
            else foreach (SpyOp op in new[] { SpyOp.Steal, SpyOp.Sabotage, SpyOp.Discord }) OpRow(a, ai, op);

            if (_discordPick && a.OpKind < 0) BuildDiscordPick(a, ai);
            RecallButton(a);
        }

        // ---- Система агента: откуда строится сеть (как регион в Hearts of Iron) ----

        private void BuildRegion(Leader a, AIEmpireManager ai)
        {
            var es = EspionageManager.Instance;
            var cur = EmpireStats.GetSystem(a.TargetSystem);
            Heading("СИСТЕМА АГЕНТА", Purple);

            var box = Block(58, new Color(0.05f, 0.07f, 0.10f, 0.95f));
            var ic = LGIcons.Create(box, LGIcon.Target, 22, Purple);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(22, 22));
            var name = LGBuild.Label(box, cur != null ? cur.Name : "—", 14, Color.white, TextAnchor.UpperLeft, bold: true);
            name.rectTransform.TopBand(9, 20, 56, 200);
            var sub = LGBuild.Label(box, cur != null
                    ? $"{Role(cur, ai.OwnerId)}сеть здесь <color=#BD8FFF>{EspionageManager.NetworkAt(cur.Id):0}%</color>"
                    : "Агенту некуда встать — у империи нет систем", 10, UIManager.DS.TextMuted, TextAnchor.UpperLeft);
            sub.rectTransform.TopBand(31, 16, 56, 200);
            if (cur != null)
            {
                var show = LGBuild.Button(box, "ShowOnMap", UIManager.DS.BtnNeutral, new Color(Purple.r, Purple.g, Purple.b, 0.6f), () =>
                {
                    Close();
                    var cam = FindFirstObjectByType<StellarisClone.Cam.StrategyCameraController>();
                    if (cam != null) cam.FocusOn(cur.Position);
                }, LGIcon.Globe, "НА КАРТЕ", 11);
                ((RectTransform)show.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-14, 0), new Vector2(130, 32));
            }

            // Куда ещё можно перебраться: самые весомые системы цели (столица, колонии), потом — где сеть сильнее
            var list = new List<StarSystem>();
            foreach (var sys in EmpireStats.Systems)
                if (sys.OwnerId == ai.OwnerId && (cur == null || sys.Id != cur.Id)) list.Add(sys);
            list.Sort((x, y) =>
            {
                int c = EspionageManager.SystemWeight(y, ai.OwnerId).CompareTo(EspionageManager.SystemWeight(x, ai.OwnerId));
                return c != 0 ? c : EspionageManager.NetworkAt(y.Id).CompareTo(EspionageManager.NetworkAt(x.Id));
            });
            int n = Mathf.Min(5, list.Count);
            if (n == 0) return;
            Paragraph("Перенести агента — сеть строится оттуда, уже построенная не пропадает. Ставьте его ближе к столице и колониям: они весят больше.", 11, 32, UIManager.DS.TextMuted);
            for (int i = 0; i < n; i++)
            {
                var sys = list[i];
                var row = Block(40, new Color(0.04f, 0.06f, 0.09f, 0.95f), 10f);
                var t = LGBuild.Label(row, $"{sys.Name}   <color=#8AA2A8>{Role(sys, ai.OwnerId)}сеть {EspionageManager.NetworkAt(sys.Id):0}%</color>",
                    12, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft);
                t.rectTransform.Stretch(18, 0, 170, 0);
                var mv = LGBuild.Button(row, "Move", UIManager.DS.BtnPrimary, new Color(Purple.r, Purple.g, Purple.b, 0.7f), () =>
                {
                    if (es != null && es.Relocate(a, sys.Id)) Rebuild();
                }, null, "ПЕРЕНЕСТИ", 10);
                ((RectTransform)mv.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-12, 0), new Vector2(140, 28));
                mv.interactable = a.OpKind < 0;
                if (a.OpKind >= 0) TooltipHelper.Attach(mv.gameObject, "Во время операции агент не может сменить систему");
            }
        }

        private static string Role(StarSystem s, int owner)
        {
            float w = EspionageManager.SystemWeight(s, owner);
            return w >= 3f ? "<color=#F2C766>столица</color> · " : w >= 2f ? "колония · " : "";
        }

        private void BuildDossier(AIEmpireManager ai)
        {
            var box = Block(150, new Color(0.04f, 0.06f, 0.09f, 0.95f));
            string m = LGBuild.Hex(UIManager.DS.TextMuted);
            string Inc(float v) => v >= 0f ? $"<color=#66F59E>+{v:0.#}</color>" : $"<color=#FF7A7A>{v:0.#}</color>";
            var left = LGBuild.Label(box,
                $"<color={m}>Гелий-3</color>   {ai.EnergyCredits:0}  ({Inc(ai.MonthlyEnergyIncome)}/мес)\n" +
                $"<color={m}>Титан</color>   {ai.Minerals:0}  ({Inc(ai.MonthlyMineralsIncome)}/мес)\n" +
                $"<color={m}>Сплавы</color>   {ai.Alloys:0}  ({Inc(ai.MonthlyAlloysIncome)}/мес)\n" +
                $"<color={m}>Влияние</color>   {ai.Influence:0}  ({Inc(ai.MonthlyInfluenceIncome)}/мес)",
                12, UIManager.DS.TextPrimary, TextAnchor.UpperLeft);
            left.rectTransform.TopBand(12, 80, 18, 340);
            left.lineSpacing = 1.25f;

            string cur = ai.CurrentTech != null
                ? $"{ai.CurrentTech.Name} ({Mathf.Clamp01(ai.TechProgress / Mathf.Max(1f, ai.CurrentTech.Cost)) * 100f:0}%)"
                : "—";
            var right = LGBuild.Label(box,
                $"<color={m}>Систем</color>   {EmpireStats.SystemCount(ai.OwnerId)}      <color={m}>Население</color>   {EmpireStats.Population(ai.OwnerId)}\n" +
                $"<color={m}>Технологий</color>   {ai.ResearchedCount} из {TechnologyManager.TotalTechCount}      <color={m}>Мощь флота</color>   {EmpireStats.MilitaryPower(ai.OwnerId):N0}\n" +
                $"<color={m}>Изучает</color>   {cur}\n" +
                $"<color={m}>Дипломатия</color>   {(ai.AtWar ? "<color=#FF7A7A>война с вами</color>" : ai.HasPact ? "<color=#66F59E>пакт с вами</color>" : "мир")}",
                12, UIManager.DS.TextPrimary, TextAnchor.UpperLeft);
            right.rectTransform.TopBand(86, 60, 18, 18);
            right.lineSpacing = 1.25f;
        }

        private void BuildActiveOp(Leader a, AIEmpireManager ai)
        {
            var op = (SpyOp)a.OpKind;
            var row = Block(70, new Color(0.10f, 0.08f, 0.04f, 0.95f));
            var ic = LGIcons.Create(row, SpyInfo.Icon(op), 24, UIManager.DS.Gold);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(24, 24));
            var t = LGBuild.Label(row, $"Идёт операция: {SpyInfo.Name(op)}", 14, Color.white, TextAnchor.UpperLeft, bold: true);
            t.rectTransform.TopBand(10, 20, 56, 190);
            string extra = op == SpyOp.Discord && AIEmpireManager.For(a.OpArg) != null ? $" · против {AIEmpireManager.For(a.OpArg).AIName}" : "";
            var s = LGBuild.Label(row, $"Осталось {a.OpMonthsLeft} мес.{extra} · шанс успеха {EspionageManager.SuccessChance(a, op, ai) * 100f:0}%", 11, UIManager.DS.TextMuted, TextAnchor.UpperLeft);
            s.rectTransform.TopBand(34, 16, 56, 190);
            var bh = LGBuild.Rect(row, "Progress");
            bh.anchorMin = new Vector2(0, 0);
            bh.anchorMax = new Vector2(1, 0);
            bh.offsetMin = new Vector2(56, 10);
            bh.offsetMax = new Vector2(-190, 16);
            LGBuild.Bar(bh, UIManager.DS.Gold, 1f - a.OpMonthsLeft / Mathf.Max(1f, SpyInfo.Months(op)), 6f);
            var cancel = LGBuild.Button(row, "Cancel", UIManager.DS.BtnDanger, UIManager.DS.Red, () =>
            {
                EspionageManager.Instance?.CancelOp(a);
                Rebuild();
            }, null, "ПРЕРВАТЬ", 11);
            ((RectTransform)cancel.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-14, 0), new Vector2(140, 34));
            TooltipHelper.Attach(cancel.gameObject, "Прервать операцию. Потраченное влияние не возвращается.");
        }

        private void OpRow(Leader a, AIEmpireManager ai, SpyOp op)
        {
            var es = EspionageManager.Instance;
            string why = null;
            bool can = false;
            if (es != null) can = es.CanStart(a, op, op == SpyOp.Discord ? ThirdCandidate(ai) : -1, out why);
            // «Раздор» открывает выбор второй стороны — кнопка активна, если для него вообще есть кандидаты и хватает прикрытия и влияния
            string reason = null;
            if (op == SpyOp.Discord)
            {
                can = a.Cover >= SpyInfo.MinCover(op) && (EconomyManager.Instance?.Influence ?? 0f) >= SpyInfo.Cost(op) && ThirdCandidate(ai) >= 0;
                if (!can)
                {
                    if (a.Cover < SpyInfo.MinCover(op)) reason = $"Нужна разведсеть {SpyInfo.MinCover(op):0}% (сейчас {a.Cover:0}%)";
                    else if ((EconomyManager.Instance?.Influence ?? 0f) < SpyInfo.Cost(op)) reason = $"Нужно {SpyInfo.Cost(op):0} влияния";
                    else reason = "Нет второй известной империи, которую можно стравить";
                }
            }
            else if (!can) reason = why;

            var row = Block(78, new Color(0.05f, 0.07f, 0.10f, 0.95f));
            var ic = LGIcons.Create(row, SpyInfo.Icon(op), 24, Purple);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(24, 24));
            var t = LGBuild.Label(row, SpyInfo.Name(op), 14, Color.white, TextAnchor.UpperLeft, bold: true);
            t.rectTransform.TopBand(8, 20, 56, 250);
            var d = LGBuild.Label(row, SpyInfo.Desc(op), 10, UIManager.DS.TextMuted, TextAnchor.UpperLeft, wrap: true);
            d.rectTransform.TopBand(30, 44, 56, 250);

            float chance = EspionageManager.SuccessChance(a, op, ai);
            Color cc = chance >= 0.6f ? UIManager.DS.Green : chance >= 0.4f ? UIManager.DS.Gold : UIManager.DS.Red;
            var stats = LGBuild.Label(row,
                $"шанс <color={LGBuild.Hex(cc)}><b>{chance * 100f:0}%</b></color>\n★{SpyInfo.Cost(op):0}  ·  {SpyInfo.Months(op)} мес.  ·  сеть {SpyInfo.MinCover(op):0}%",
                10, UIManager.DS.TextPrimary, TextAnchor.UpperRight);
            stats.rectTransform.anchorMin = new Vector2(1, 1);
            stats.rectTransform.anchorMax = new Vector2(1, 1);
            stats.rectTransform.pivot = new Vector2(1, 1);
            stats.rectTransform.sizeDelta = new Vector2(220, 32);
            stats.rectTransform.anchoredPosition = new Vector2(-14, -8);
            stats.lineSpacing = 1.2f;

            var btn = LGBuild.Button(row, "Start", can ? UIManager.DS.BtnPrimary : UIManager.DS.BtnNeutral,
                new Color(Purple.r, Purple.g, Purple.b, 0.7f), () =>
                {
                    if (op == SpyOp.Discord) { _discordPick = true; Rebuild(); }
                    else { EspionageManager.Instance?.StartOp(a, op, -1); Rebuild(); }
                }, null, op == SpyOp.Discord ? "ВЫБРАТЬ ЦЕЛЬ" : "НАЧАТЬ", 11);
            ((RectTransform)btn.transform).At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-14, 10), new Vector2(150, 30));
            btn.interactable = can;
            if (!can && reason != null) TooltipHelper.Attach(btn.gameObject, reason);
            TooltipHelper.Attach(row.gameObject,
                $"<b>{SpyInfo.Name(op)}</b>\n{SpyInfo.Desc(op)}\n\n<color=#8AA2A8>Шанс складывается из базового ({SpyInfo.BaseChance(op) * 100f:0}%), уровня и черт агента, силы разведсети, ваших технологий и защиты цели ({EspionageManager.TargetCounter(ai) * 100f:0}%).</color>");
        }

        /// <summary>Первая подходящая третья империя для «Раздора» (-1 — нет).</summary>
        private static int ThirdCandidate(AIEmpireManager target)
        {
            foreach (var o in AIEmpireManager.Alive)
                if (o.OwnerId != target.OwnerId && Contacts.PlayerMet(o.OwnerId)) return o.OwnerId;
            return -1;
        }

        private void BuildDiscordPick(Leader a, AIEmpireManager ai)
        {
            Heading($"КОГО СТРАВИТЬ С {ai.AIName.ToUpper()}?", Purple);
            foreach (var other in AIEmpireManager.Alive)
            {
                if (other.OwnerId == ai.OwnerId || !Contacts.PlayerMet(other.OwnerId)) continue;
                var o = other;
                var row = Block(52, new Color(0.05f, 0.07f, 0.10f, 0.95f));
                Color c = FleetIndicator.OwnerColor(o.OwnerId);
                var t = LGBuild.Label(row, o.AIName, 13, Color.white, TextAnchor.UpperLeft, bold: true);
                t.rectTransform.TopBand(8, 18, 18, 190);
                bool war = AIRelations.AtWar(ai.OwnerId, o.OwnerId);
                var s = LGBuild.Label(row, $"отношение между ними {AIRelations.Opinion(ai, o):+0;-0;0}" + (war ? " · уже воюют" : ""), 10, UIManager.DS.TextMuted, TextAnchor.UpperLeft);
                s.rectTransform.TopBand(28, 16, 18, 190);
                var btn = LGBuild.Button(row, "Pick", UIManager.DS.BtnPrimary, new Color(c.r, c.g, c.b, 0.7f), () =>
                {
                    if (EspionageManager.Instance != null && EspionageManager.Instance.StartOp(a, SpyOp.Discord, o.OwnerId)) _discordPick = false;
                    Rebuild();
                }, null, $"СТРАВИТЬ  ★{SpyInfo.Cost(SpyOp.Discord):0}", 11);
                ((RectTransform)btn.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-14, 0), new Vector2(170, 32));
            }
            var back = LGBuild.Button(_detail, "Back", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.3f), () => { _discordPick = false; Rebuild(); }, LGIcon.Back, "ОТМЕНА", 11);
            LGBuild.Height(back.gameObject, 34);
        }

        private void RecallButton(Leader a)
        {
            var host = LGBuild.Rect(_detail, "Recall");
            LGBuild.Height(host.gameObject, 46);
            var btn = LGBuild.Button(host, "Recall", UIManager.DS.BtnDanger, UIManager.DS.Red, () =>
            {
                LeaderManager.Instance?.Unassign(a);
                SFXManager.Play(Sfx.UiBack);
                Rebuild();
            }, LGIcon.Back, "ОТОЗВАТЬ АГЕНТА", 11);
            ((RectTransform)btn.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-6, 0), new Vector2(210, 34));
            TooltipHelper.Attach(btn.gameObject, "Агент покидает пост: идущая операция пропадёт, а разведсеть без него начнёт таять (−2% в месяц). Его можно назначить заново.");
        }

        // ================================================================ Журнал

        private void BuildLog()
        {
            LGBuild.Clear(_logHost);
            var es = EspionageManager.Instance;
            if (es == null || es.Log.Count == 0)
            {
                var host = LGBuild.Rect(_logHost, "Empty");
                LGBuild.Height(host.gameObject, 40);
                var t = LGBuild.Label(host, "Здесь появятся результаты ваших операций и попытки чужих агентов проникнуть в вашу империю.", 11, UIManager.DS.TextMuted, TextAnchor.MiddleLeft, wrap: true);
                t.rectTransform.Stretch(10, 0, 10, 0);
                return;
            }
            foreach (var e in es.Log)
            {
                Color c = e.Kind == 1 ? UIManager.DS.Green : e.Kind == 2 ? UIManager.DS.Red : e.Kind == 3 ? UIManager.DS.Gold : UIManager.DS.TextMuted;
                var host = LGBuild.Rect(_logHost, "Entry");
                LGBuild.Height(host.gameObject, Mathf.Max(20f, 15f * Mathf.Ceil(e.Text.Length / 118f) + 6f));
                var t = LGBuild.Label(host, $"<color=#8AA2A8>{e.Date}</color>   <color={LGBuild.Hex(c)}>●</color>  {e.Text}", 11, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
                t.rectTransform.Stretch(10, 0, 10, 0);
            }
        }
    }
}
