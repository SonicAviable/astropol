using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Центр тайных операций. Всё главное — на одном экране, без прокрутки:
    ///   • слева — ваши агенты (портрет, пост, сила сети) и кандидат на найм;
    ///   • справа сверху — выбранный агент: крупный портрет, черты, цель (империя, система, отношение, защита)
    ///     и кнопки «Сменить на карте» / «Отозвать»;
    ///   • шкала разведсети: крупная цифра, как растёт, награды узлами-иконками прямо на шкале (горят открытые),
    ///     подпись «следующая награда»;
    ///   • операции — три карточки в ряд: шанс, цена, срок, нужный порог сети; идущая операция — одна широкая карточка;
    ///   • снизу рядом — досье цели (с 50% сети) и журнал операций.
    /// Агент без назначения выбирает пост (контрразведка дома или империя — система выбирается на карте);
    /// контрразведчик показывает защиту вашей империи. Без агентов — что даёт разведка и как начать.
    /// </summary>
    public sealed class EspionageWindow : MonoBehaviour
    {
        public static EspionageWindow Instance { get; private set; }
        public bool IsOpen => _root != null && LG.IsVisible(_root);

        private const float W = 1280f, H = 800f, LeftW = 300f;
        private const float MainH = 690f;             // высота правой части (тело окна без шапки)
        private static readonly Color Purple = new Color(0.74f, 0.56f, 1f);
        private static readonly Color Rim = new Color(0.74f, 0.56f, 1f, 0.34f);
        private static readonly Color CardTint = new Color(0.035f, 0.05f, 0.075f, 0.97f);
        private static readonly Color Muted = new Color(0.55f, 0.63f, 0.70f, 1f);

        private GameObject _root;
        private Text _subtitle;
        private RectTransform _agentList, _candidateHost, _main;
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

            // Плотная подложка: карта не просвечивает сквозь окно, текст читается
            var shadeMask = LG.RoundedMask(rootRt, 26f);
            var shade = LGBuild.Panel(shadeMask, "Shade", new Color(0.012f, 0.02f, 0.032f, 0.93f));
            shade.rectTransform.Stretch();
            LG.Ignore(shadeMask.gameObject);

            BuildHeader(rootRt);

            var body = LGBuild.Rect(rootRt, "Body");
            body.Stretch(18, 18, 18, 92);

            // Левая колонка: агенты и кандидат
            var left = LGBuild.Rect(body, "Left");
            left.anchorMin = new Vector2(0, 0);
            left.anchorMax = new Vector2(0, 1);
            left.pivot = new Vector2(0, 0.5f);
            left.offsetMin = Vector2.zero;
            left.offsetMax = new Vector2(LeftW, 0);

            var leftTitle = LGBuild.Label(left, Loc.T("АГЕНТЫ"), 12, Purple, TextAnchor.UpperLeft, bold: true).Track();
            leftTitle.rectTransform.TopBand(2, 18, 4, 0);
            var listHost = LGBuild.Rect(left, "AgentsHost");
            listHost.Stretch(0, 150, 0, 24);
            _agentList = LGBuild.ScrollList(listHost, 8f, 2);
            _candidateHost = LGBuild.Rect(left, "Candidate");
            _candidateHost.anchorMin = new Vector2(0, 0);
            _candidateHost.anchorMax = new Vector2(1, 0);
            _candidateHost.pivot = new Vector2(0.5f, 0);
            _candidateHost.offsetMin = Vector2.zero;
            _candidateHost.offsetMax = new Vector2(0, 140);

            // Правая часть: подробности выбранного агента (раскладка без прокрутки)
            _main = LGBuild.Rect(body, "Main");
            _main.Stretch(LeftW + 16, 0, 0, 0);

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
            var title = LGBuild.Label(titleRt, Loc.T("ТАЙНЫЕ ОПЕРАЦИИ"), 24, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true).Track(2f);
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
            if (Input.GetKeyDown(KeyCode.Escape)) { UIEsc.Consume(); Close(); return; }
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
                _selectedId = -1;
                if (lm != null) foreach (var l in lm.Of(0)) if (l.Class == LeaderClass.Agent) { _selectedId = l.Id; sel = l; break; }
                _discordPick = false;
            }
            if (sel == null || sel.OpKind >= 0 || sel.Post != LeaderPost.Spy) _discordPick = false;

            RefreshHeader();
            BuildAgents();
            BuildCandidate();
            BuildMain(sel);
        }

        private void RefreshHeader()
        {
            var es = EspionageManager.Instance;
            if (_subtitle == null) return;
            _subtitle.text = Loc.F("Защита вашей империи <color=#BD8FFF>{0:0}%</color>", EspionageManager.PlayerCounter() * 100f) +
                             (es != null
                                ? Loc.F("   ·   операции: <color=#66F59E>{0}</color> удачных, <color=#FF7A7A>{1}</color> провальных", es.Succeeded, es.Failed) +
                                  Loc.F("   ·   чужих попыток сорвано: {0}   ·   прорывов к вам: {1}", es.Foiled, es.Breaches)
                                : "");
        }

        // ================================================================ Агенты (левая колонка)

        private static string PostLine(Leader l)
        {
            if (l.Post != LeaderPost.Spy) return Loc.T("<color=#FFAA55>Ждёт назначения</color>");
            if (l.TargetOwner == 0) return Loc.T("<color=#66E0D0>Контрразведка</color> · дома");
            string who = Contacts.NameForPlayer(l.TargetOwner);
            if (l.OpKind >= 0) return Loc.F("<color=#F2C766>{0}</color> · ещё {1} мес.", SpyInfo.Name((SpyOp)l.OpKind), l.OpMonthsLeft);
            return Loc.F("<color=#BD8FFF>{0}</color> · сеть {1:0}%", who, l.Cover);
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
                LGBuild.Height(host.gameObject, 110);
                var t = LGBuild.Label(host, Loc.T("Агентов пока нет.\nНаймите первого внизу."), 12, UIManager.DS.TextMuted, TextAnchor.MiddleCenter, wrap: true);
                t.rectTransform.Stretch(14, 0, 14, 0);
            }
        }

        private void AgentCard(Leader l)
        {
            bool selected = l.Id == _selectedId;
            var card = LGBuild.Panel(_agentList, "Agent", selected ? new Color(Purple.r * 0.16f, Purple.g * 0.12f, Purple.b * 0.24f, 0.97f) : CardTint, raycast: true);
            LGBuild.Height(card.gameObject, 76);
            LG.Platter(card.gameObject, 14f).SetRim(new Color(Purple.r, Purple.g, Purple.b, selected ? 0.95f : 0.22f));
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
            frame.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(54, 54));
            if (!LeaderThumb.Create(frame, 12f).Set(l, Purple))
                LGIcons.Create(frame, LGIcon.Betrayal, 28, Purple);

            var name = LGBuild.Label(card.transform, l.Name, 13, Color.white, TextAnchor.UpperLeft, bold: true);
            name.rectTransform.TopBand(10, 18, 76, 10);
            var post = LGBuild.Label(card.transform, PostLine(l), 11, UIManager.DS.TextPrimary, TextAnchor.UpperLeft);
            post.rectTransform.TopBand(31, 16, 76, 10);

            var barHost = LGBuild.Rect(card.transform, "Bar");
            barHost.anchorMin = new Vector2(0, 0);
            barHost.anchorMax = new Vector2(1, 0);
            barHost.offsetMin = new Vector2(76, 12);
            barHost.offsetMax = new Vector2(-12, 17);
            var bar = LGBuild.Bar(barHost, Purple, l.Post == LeaderPost.Spy && l.TargetOwner > 0 ? l.Cover / 100f : 0f, 4f);
            if (l.OpKind >= 0)
                LGBuild.SetBar(bar, 1f - l.OpMonthsLeft / Mathf.Max(1f, SpyInfo.Months((SpyOp)l.OpKind)), UIManager.DS.Gold);
            TooltipHelper.Attach(card.gameObject, AgentTooltip(l));
        }

        private static string TraitsText(Leader l, string sep = ", ")
        {
            var sb = new StringBuilder();
            foreach (var id in l.Traits)
            {
                var t = LeaderManager.Trait(id);
                if (t == null) continue;
                if (sb.Length > 0) sb.Append(sep);
                sb.Append(t.Name);
            }
            return sb.ToString();
        }

        private static string AgentTooltip(Leader l)
        {
            var sb = new StringBuilder();
            sb.Append(Loc.F("<b>{0}</b>\nАгент · уровень {1}\n<color=#8AA2A8>Уровень:</color> {2}", l.Name, l.Level, LeaderManager.LevelBonusText(l)));
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

            var card = LGBuild.Panel(_candidateHost, "Cand", CardTint);
            card.rectTransform.Stretch(0, 0, 0, 0);
            LG.Platter(card.gameObject, 16f).SetRim(new Color(1f, 1f, 1f, 0.14f));

            var title = LGBuild.Label(card.transform, Loc.T("КАНДИДАТ НА НАЙМ"), 10, UIManager.DS.TextMuted, TextAnchor.UpperLeft, bold: true).Track();
            title.rectTransform.TopBand(10, 14, 14, 14);
            if (cand == null || lm == null)
            {
                var t = LGBuild.Label(card.transform, Loc.T("Кандидатов нет — список обновляется каждый год."), 11, UIManager.DS.TextMuted, TextAnchor.MiddleLeft, wrap: true);
                t.rectTransform.Stretch(14, 10, 14, 30);
                return;
            }

            var frame = LGBuild.Rect(card.transform, "Face");
            frame.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -30), new Vector2(50, 50));
            if (!LeaderThumb.Create(frame, 12f).Set(cand, Purple))
                LGIcons.Create(frame, LGIcon.Betrayal, 26, Purple);
            var name = LGBuild.Label(card.transform, Loc.F("{0} · {1} лет", cand.Name, cand.Age), 13, Color.white, TextAnchor.UpperLeft, bold: true);
            name.rectTransform.TopBand(32, 18, 72, 10);
            var tr = LGBuild.Label(card.transform, TraitsText(cand), 11, UIManager.DS.Gold, TextAnchor.UpperLeft, wrap: true);
            tr.rectTransform.TopBand(52, 30, 72, 10);

            bool can = lm.CanHire(out string why);
            var hire = LGBuild.Button(card.transform, "Hire", can ? UIManager.DS.BtnPrimary : UIManager.DS.BtnNeutral,
                new Color(Purple.r, Purple.g, Purple.b, 0.7f), () =>
                {
                    lm.Hire(cand);
                    Rebuild();
                }, null, Loc.F("НАНЯТЬ  ★{0:0}", LeaderManager.HireInfluence), 11);
            var hrt = (RectTransform)hire.transform;
            hrt.anchorMin = new Vector2(0, 0);
            hrt.anchorMax = new Vector2(1, 0);
            hrt.pivot = new Vector2(0.5f, 0);
            hrt.offsetMin = new Vector2(12, 12);
            hrt.offsetMax = new Vector2(-12, 44);
            hire.interactable = can;
            if (!can) TooltipHelper.Attach(hire.gameObject, why);
            else TooltipHelper.Attach(hire.gameObject, Loc.F("Найм {0:0} влияния, содержание {1:0.#} гелия-3 в месяц. Лидеров: {2}/{3}", LeaderManager.HireInfluence, LeaderManager.UpkeepEnergy, lm.CountFor(0), LeaderManager.MaxLeaders));
        }

        // ================================================================ Раскладка правой части

        /// <summary>Карточка в правой части: полоса сверху (top, height) в доле ширины x0..x1.</summary>
        private RectTransform Card(float top, float height, float x0 = 0f, float x1 = 1f, Color? tint = null, Color? rim = null)
        {
            var p = LGBuild.Panel(_main, "Card", tint ?? CardTint);
            var rt = p.rectTransform;
            rt.anchorMin = new Vector2(x0, 1);
            rt.anchorMax = new Vector2(x1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(x0 > 0f ? 6f : 0f, -top - height);
            rt.offsetMax = new Vector2(x1 < 1f ? -6f : 0f, -top);
            LG.Platter(p.gameObject, 16f).SetRim(rim ?? new Color(1f, 1f, 1f, 0.10f));
            return rt;
        }

        private static Text Caption(RectTransform parent, string text, float top, float left, Color? col = null, float right = 16f)
        {
            var t = LGBuild.Label(parent, text, 10, col ?? Muted, TextAnchor.UpperLeft, bold: true).Track();
            t.rectTransform.TopBand(top, 14, left, right);
            return t;
        }

        private static Text Txt(RectTransform parent, string text, int size, Color col, float top, float height, float left, float right,
                                TextAnchor anchor = TextAnchor.UpperLeft, bool bold = false, bool wrap = false)
        {
            var t = LGBuild.Label(parent, text, size, col, anchor, bold, wrap);
            t.rectTransform.TopBand(top, height, left, right);
            return t;
        }

        /// <summary>Подпись фиксированного размера от левого верхнего угла родителя.</summary>
        private static Text TxtAt(RectTransform parent, string text, int size, Color col, float x, float y, float w, float h,
                                  TextAnchor anchor = TextAnchor.UpperLeft, bool bold = false, bool wrap = false)
        {
            var t = LGBuild.Label(parent, text, size, col, anchor, bold, wrap);
            t.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y), new Vector2(w, h));
            return t;
        }

        private void BuildMain(Leader a)
        {
            LGBuild.Clear(_main);
            if (a == null) { BuildWelcome(); return; }

            AgentStrip(a);
            if (a.Post != LeaderPost.Spy) { BuildAssignment(a); return; }
            if (a.TargetOwner == 0) { BuildCounter(a); return; }

            var ai = AIEmpireManager.For(a.TargetOwner);
            if (ai == null) { BuildLogCard(124f, MainH - 124f, 0f, 1f); return; }
            NetworkTrack(a.Cover, a, ai, 124f);
            if (_discordPick && a.OpKind < 0) DiscordPick(a, ai, 268f);
            else if (a.OpKind >= 0) ActiveOp(a, ai, 268f);
            else Operations(a, ai, 268f);
            Dossier(a, ai, 450f, MainH - 450f);
            BuildLogCard(450f, MainH - 450f, 0.5f, 1f);
        }

        // ---- Полоса агента: портрет, черты, цель, кнопки ----

        private void AgentStrip(Leader a)
        {
            var card = Card(0f, 112f, tint: new Color(Purple.r * 0.10f, Purple.g * 0.08f, Purple.b * 0.15f, 0.97f),
                            rim: new Color(Purple.r, Purple.g, Purple.b, 0.40f));
            var face = LGBuild.Rect(card, "Face");
            face.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(86, 86));
            if (!LeaderThumb.Create(face, 18f, 2f).Set(a, Purple))
                LGIcons.Create(face, LGIcon.Betrayal, 40, Purple);

            Txt(card, a.Name, 22, Color.white, 12, 28, 116, 500, bold: true);
            Txt(card, Loc.F("Агент · уровень {0}   <color=#8AA2A8>{1}</color>", a.Level, LeaderManager.LevelBonusText(a)), 11, UIManager.DS.TextPrimary, 42, 16, 116, 500);
            string traits = TraitsText(a, "  ·  ");
            Txt(card, traits.Length > 0 ? traits : Loc.T("без особых черт"), 11, UIManager.DS.Gold, 60, 16, 116, 500);
            string status = a.Post != LeaderPost.Spy ? Loc.T("<color=#FFAA55>●  ЖДЁТ НАЗНАЧЕНИЯ</color>")
                          : a.TargetOwner == 0 ? Loc.T("<color=#66E0D0>●  КОНТРРАЗВЕДКА</color>")
                          : a.OpKind >= 0 ? Loc.F("<color=#F2C766>●  ОПЕРАЦИЯ «{0}»</color>", SpyInfo.Name((SpyOp)a.OpKind).ToUpper())
                          : Loc.T("<color=#BD8FFF>●  СТРОИТ РАЗВЕДСЕТЬ</color>");
            Txt(card, status, 10, Color.white, 82, 14, 116, 500, bold: true).Track();

            // Цель и кнопки — справа
            if (a.Post == LeaderPost.Spy && a.TargetOwner > 0)
            {
                var ai = AIEmpireManager.For(a.TargetOwner);
                var sys = EmpireStats.GetSystem(a.TargetSystem);
                Color oc = FleetIndicator.OwnerColor(a.TargetOwner);
                var stripe = LGBuild.Panel(card, "Stripe", oc);
                stripe.rectTransform.anchorMin = new Vector2(1, 0);
                stripe.rectTransform.anchorMax = new Vector2(1, 1);
                stripe.rectTransform.pivot = new Vector2(1, 0.5f);
                stripe.rectTransform.sizeDelta = new Vector2(3f, -28f);
                stripe.rectTransform.anchoredPosition = new Vector2(-476f, 0f);

                var info = LGBuild.Rect(card, "Target");
                info.anchorMin = new Vector2(1, 0);
                info.anchorMax = new Vector2(1, 1);
                info.pivot = new Vector2(1, 0.5f);
                info.sizeDelta = new Vector2(290f, 0f);
                info.anchoredPosition = new Vector2(-180f, 0f);
                Caption(info, Loc.T("ЦЕЛЬ"), 14, 0);
                Txt(info, Contacts.NameForPlayer(a.TargetOwner), 15, oc, 30, 20, 0, 0, bold: true);
                string role = sys != null ? Role(sys, a.TargetOwner) : "";
                Txt(info, sys != null ? Loc.F("Система {0}  <color=#8AA2A8>{1}сеть здесь {2:0}%</color>", sys.Name, role, EspionageManager.NetworkAt(sys.Id)) : Loc.T("Система не выбрана"),
                     11, UIManager.DS.TextPrimary, 54, 16, 0, 0);
                if (ai != null)
                    Txt(info, Loc.F("<color=#8AA2A8>Отношение</color> {0:+0;-0;0}   <color=#8AA2A8>Защита цели</color> <color=#FF9F7A>{1:0}%</color>", ai.Opinion, EspionageManager.TargetCounter(ai) * 100f) +
                               (ai.AtWar ? Loc.T("   <color=#FF7A7A>война</color>") : ""), 11, UIManager.DS.TextPrimary, 74, 16, 0, 0);

                var move = LGBuild.Button(card, "PickOnMap", UIManager.DS.BtnPrimary, new Color(Purple.r, Purple.g, Purple.b, 0.7f),
                    () => SpyNetworkView.BeginPick(a, a.TargetOwner), LGIcon.Target, Loc.T("СМЕНИТЬ СИСТЕМУ"), 10);
                ((RectTransform)move.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-14, -16), new Vector2(160, 34));
                move.interactable = a.OpKind < 0;
                TooltipHelper.Attach(move.gameObject, a.OpKind < 0
                    ? Loc.T("Выбрать на карте другую систему империи, откуда строить сеть. Уже построенная сеть не пропадает.")
                    : Loc.T("Во время операции агент не может сменить систему"));
            }
            if (a.Post == LeaderPost.Spy)
            {
                var recall = LGBuild.Button(card, "Recall", UIManager.DS.BtnDanger, UIManager.DS.Red, () =>
                {
                    LeaderManager.Instance?.Unassign(a);
                    SFXManager.Play(Sfx.UiBack);
                    Rebuild();
                }, LGIcon.Back, Loc.T("ОТОЗВАТЬ"), 10);
                ((RectTransform)recall.transform).At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-14, 16), new Vector2(160, 34));
                TooltipHelper.Attach(recall.gameObject, Loc.T("Агент покидает пост: идущая операция пропадёт, а разведсеть без него начнёт таять (−2% в месяц). Его можно назначить заново."));
            }
        }

        private static string Role(StarSystem s, int owner)
        {
            float w = EspionageManager.SystemWeight(s, owner);
            return w >= 3f ? Loc.T("столица · ") : w >= 2f ? Loc.T("колония · ") : "";
        }

        // ---- Шкала разведсети с наградами-узлами ----

        private struct Milestone
        {
            public float Cover;
            public LGIcon Icon;
            public string Short, Title, Desc;
        }

        private static readonly Milestone[] Milestones =
        {
            new Milestone { Cover = EspionageManager.LocalIntel, Icon = LGIcon.Star, Short = Loc.T("Видимость"), Title = Loc.T("Системы на виду"),
                Desc = Loc.T("Системы, где сеть от 15%, видны на карте сквозь туман войны") },
            new Milestone { Cover = EspionageManager.IntelCover, Icon = LGIcon.Globe, Short = Loc.T("Обзор"), Title = Loc.T("Обзор"),
                Desc = Loc.T("Вся территория империи и все её флоты — у вас на карте") },
            new Milestone { Cover = 25f, Icon = LGIcon.Wrench, Short = Loc.T("Диверсия"), Title = Loc.T("Операция «Диверсия»"),
                Desc = Loc.T("Удар по верфям и складам: цель теряет сплавы и гелий-3") },
            new Milestone { Cover = EspionageManager.BlueprintCover, Icon = LGIcon.Research, Short = Loc.T("Чертежи"), Title = Loc.T("Перехват чертежей"),
                Desc = Loc.T("Технологии, которые они уже знают, вы изучаете на 25% быстрее") },
            new Milestone { Cover = 40f, Icon = LGIcon.Target, Short = Loc.T("Кража"), Title = Loc.T("Операция «Кража технологии»"),
                Desc = Loc.T("Агент выкрадывает технологию, которой у вас нет") },
            new Milestone { Cover = EspionageManager.DeepIntelCover, Icon = LGIcon.Diplomacy, Short = Loc.T("Досье"), Title = Loc.T("Досье и «Раздор»"),
                Desc = Loc.T("Запасы, доходы, наука и мощь флота цели; можно стравить их с другой империей") },
        };

        /// <summary>Шкала показывает 0–55%: дальше новых наград нет, а пороги не слипаются у левого края.</summary>
        private const float TrackMax = 55f;

        private void NetworkTrack(float cover, Leader a, AIEmpireManager ai, float top)
        {
            var card = Card(top, 132f);
            // Крупная цифра и как растёт
            Txt(card, $"{cover:0}%", 40, Purple, 12, 50, 20, 0, bold: true);
            Caption(card, a != null ? Loc.T("РАЗВЕДСЕТЬ В ИМПЕРИИ") : Loc.T("РАЗВЕДСЕТЬ"), 64, 22);
            string grow = a == null ? Loc.T("Наймите агента и внедрите его в чужую империю")
                : a.OpKind >= 0 ? Loc.T("<color=#F2C766>Сеть заморожена — идёт операция</color>")
                : Loc.F("<color=#66F59E>+{0:0.#}</color> в месяц в системе агента, у соседей ×0,6", EspionageManager.BuildRate(a).Total);
            TxtAt(card, grow, 11, UIManager.DS.TextPrimary, 22, 82, 260, 40, wrap: true);

            // Шкала с узлами
            var track = LGBuild.Rect(card, "Track");
            track.Stretch(300, 0, 36, 0);
            var barHost = LGBuild.Rect(track, "Bar");
            barHost.TopBand(52, 8, 0, 0);
            LGBuild.Bar(barHost, Purple, Mathf.Clamp01(cover / TrackMax), 6f);

            Milestone? next = null;
            foreach (var m in Milestones)
            {
                bool got = cover >= m.Cover;
                if (!got && next == null) next = m;
                bool isNext = next.HasValue && next.Value.Cover == m.Cover;
                float x = m.Cover / TrackMax;

                var node = LGBuild.Panel(track, "Node", got ? new Color(Purple.r * 0.55f, Purple.g * 0.40f, Purple.b * 0.75f, 1f)
                                                           : new Color(0.05f, 0.06f, 0.09f, 1f), raycast: true);
                var nrt = node.rectTransform;
                nrt.anchorMin = nrt.anchorMax = new Vector2(x, 1);
                nrt.pivot = new Vector2(0.5f, 0.5f);
                nrt.sizeDelta = new Vector2(30f, 30f);
                nrt.anchoredPosition = new Vector2(0f, -56f);
                LG.Platter(node.gameObject, 15f).SetRim(got ? new Color(Purple.r, Purple.g, Purple.b, 1f)
                                                             : isNext ? new Color(Purple.r, Purple.g, Purple.b, 0.7f)
                                                             : new Color(1f, 1f, 1f, 0.18f));
                var ic = LGIcons.Create(node.transform, m.Icon, 16, got ? Color.white : isNext ? Purple : new Color(0.6f, 0.66f, 0.72f, 0.6f));
                ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16, 16));

                var title = LGBuild.Label(track, m.Short, 10, got ? Color.white : isNext ? Purple : Muted, TextAnchor.LowerCenter, bold: got || isNext);
                title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(x, 1);
                title.rectTransform.pivot = new Vector2(0.5f, 0f);
                title.rectTransform.sizeDelta = new Vector2(70f, 14f);
                title.rectTransform.anchoredPosition = new Vector2(0f, -38f);
                var pct = LGBuild.Label(track, $"{m.Cover:0}%", 10, got ? Purple : Muted, TextAnchor.UpperCenter, bold: got);
                pct.rectTransform.anchorMin = pct.rectTransform.anchorMax = new Vector2(x, 1);
                pct.rectTransform.pivot = new Vector2(0.5f, 1f);
                pct.rectTransform.sizeDelta = new Vector2(40f, 14f);
                pct.rectTransform.anchoredPosition = new Vector2(0f, -74f);

                TooltipHelper.Attach(node.gameObject, Loc.F("<b>{0}</b> — при сети {1:0}%\n{2}\n\n", m.Title, m.Cover, m.Desc) +
                    (got ? Loc.T("<color=#66F59E>Открыто</color>") : Loc.F("<color=#8AA2A8>Ещё {0:0.#}%</color>", m.Cover - cover)));
            }

            string nextLine = next.HasValue
                ? Loc.F("<color=#8AA2A8>Следующее:</color>  <b>{0}</b> — {1}  <color=#BD8FFF>(ещё {2:0.#}%)</color>", next.Value.Title, next.Value.Desc, next.Value.Cover - cover)
                : Loc.T("<color=#66F59E>Все награды разведки открыты</color> — сеть продолжает расти и смягчает урон от провалов");
            Txt(card, nextLine, 11, UIManager.DS.TextPrimary, 104, 18, 300, 20);
        }

        // ---- Операции: три карточки в ряд ----

        private void Operations(Leader a, AIEmpireManager ai, float top)
        {
            var ops = new[] { SpyOp.Sabotage, SpyOp.Steal, SpyOp.Discord };
            for (int i = 0; i < ops.Length; i++) OpCard(a, ai, ops[i], top, i / 3f, (i + 1) / 3f);
        }

        private void OpCard(Leader a, AIEmpireManager ai, SpyOp op, float top, float x0, float x1)
        {
            var es = EspionageManager.Instance;
            bool netOk = a.Cover >= SpyInfo.MinCover(op);
            bool money = (EconomyManager.Instance?.Influence ?? 0f) >= SpyInfo.Cost(op);
            string reason = null;
            bool can;
            if (op == SpyOp.Discord)
            {
                can = netOk && money && ThirdCandidate(ai) >= 0;
                if (!can) reason = !netOk ? Loc.F("Нужна разведсеть {0:0}% (сейчас {1:0}%)", SpyInfo.MinCover(op), a.Cover)
                                 : !money ? Loc.F("Нужно {0:0} влияния", SpyInfo.Cost(op))
                                 : Loc.T("Нет второй известной империи, которую можно стравить");
            }
            else
            {
                can = es != null && es.CanStart(a, op, -1, out reason);
            }

            var card = Card(top, 170f, x0, x1, tint: netOk ? CardTint : new Color(0.025f, 0.03f, 0.045f, 0.97f),
                            rim: netOk ? new Color(Purple.r, Purple.g, Purple.b, 0.35f) : new Color(1f, 1f, 1f, 0.08f));
            Color head = netOk ? Color.white : new Color(0.75f, 0.80f, 0.85f, 0.7f);
            var ic = LGIcons.Create(card, SpyInfo.Icon(op), 22, netOk ? Purple : Muted);
            ic.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -14), new Vector2(22, 22));
            Txt(card, SpyInfo.Name(op), 14, head, 14, 22, 46, 70, bold: true);

            float chance = EspionageManager.SuccessChance(a, op, ai);
            Color cc = chance >= 0.6f ? UIManager.DS.Green : chance >= 0.4f ? UIManager.DS.Gold : UIManager.DS.Red;
            var pill = LGBuild.Panel(card, "Chance", new Color(cc.r * 0.18f, cc.g * 0.18f, cc.b * 0.18f, 0.95f));
            pill.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-14, -12), new Vector2(58, 24));
            LG.Platter(pill.gameObject, 12f).SetRim(new Color(cc.r, cc.g, cc.b, 0.6f));
            LGBuild.Label(pill.transform, $"{chance * 100f:0}%", 12, cc, TextAnchor.MiddleCenter, bold: true);
            TooltipHelper.Attach(pill.gameObject, Loc.F("Шанс успеха. Складывается из базового ({0:0}%), уровня и черт агента, силы разведсети, ваших технологий и защиты цели ({1:0}%).", SpyInfo.BaseChance(op) * 100f, EspionageManager.TargetCounter(ai) * 100f));

            Txt(card, SpyInfo.Desc(op), 11, netOk ? UIManager.DS.TextMuted : new Color(0.55f, 0.6f, 0.66f, 0.7f), 44, 58, 16, 16, wrap: true);
            Txt(card, Loc.F("<color=#F2C766>★{0:0}</color>   ·   {1} мес.   ·   нужна сеть {2:0}%", SpyInfo.Cost(op), SpyInfo.Months(op), SpyInfo.MinCover(op)),
                 10, netOk ? UIManager.DS.TextPrimary : Muted, 104, 14, 16, 16);

            string label = !netOk ? Loc.F("СЕТЬ {0:0}%  ·  ЕЩЁ {1:0.#}%", SpyInfo.MinCover(op), SpyInfo.MinCover(op) - a.Cover)
                         : op == SpyOp.Discord ? Loc.T("ВЫБРАТЬ, КОГО СТРАВИТЬ") : Loc.T("НАЧАТЬ ОПЕРАЦИЮ");
            var btn = LGBuild.Button(card, "Start", can ? UIManager.DS.BtnPrimary : UIManager.DS.BtnNeutral,
                new Color(Purple.r, Purple.g, Purple.b, can ? 0.75f : 0.25f), () =>
                {
                    if (op == SpyOp.Discord) { _discordPick = true; Rebuild(); }
                    else { EspionageManager.Instance?.StartOp(a, op, -1); Rebuild(); }
                }, netOk ? (LGIcon?)null : LGIcon.Lock, label, 10);
            var brt = (RectTransform)btn.transform;
            brt.anchorMin = new Vector2(0, 0);
            brt.anchorMax = new Vector2(1, 0);
            brt.pivot = new Vector2(0.5f, 0);
            brt.offsetMin = new Vector2(14, 12);
            brt.offsetMax = new Vector2(-14, 44);
            btn.interactable = can;
            // Что даст операция и чем рискуете; если кнопка серая — сначала причина
            string preview = EspionageManager.OpPreview(a, op, ai);
            TooltipHelper.Attach(btn.gameObject, can || reason == null ? preview : $"<color=#FF8888>{reason}</color>\n\n{preview}");
        }

        private void ActiveOp(Leader a, AIEmpireManager ai, float top)
        {
            var op = (SpyOp)a.OpKind;
            var card = Card(top, 170f, tint: new Color(0.09f, 0.07f, 0.035f, 0.97f), rim: new Color(UIManager.DS.Gold.r, UIManager.DS.Gold.g, UIManager.DS.Gold.b, 0.5f));
            var ic = LGIcons.Create(card, SpyInfo.Icon(op), 34, UIManager.DS.Gold);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(26, 10), new Vector2(34, 34));
            Caption(card, Loc.T("ИДЁТ ОПЕРАЦИЯ"), 22, 82, UIManager.DS.Gold);
            Txt(card, SpyInfo.Name(op), 20, Color.white, 40, 26, 82, 220, bold: true);
            string extra = op == SpyOp.Discord && AIEmpireManager.For(a.OpArg) != null ? Loc.F(" · стравливаем с {0}", AIEmpireManager.For(a.OpArg).AIName) : "";
            Txt(card, Loc.F("Осталось {0} мес.{1}   ·   шанс успеха {2:0}%   ·   сеть пока не растёт", a.OpMonthsLeft, extra, EspionageManager.SuccessChance(a, op, ai) * 100f),
                 12, UIManager.DS.TextPrimary, 72, 18, 82, 220);
            Txt(card, SpyInfo.Desc(op), 11, UIManager.DS.TextMuted, 96, 32, 82, 220, wrap: true);
            var bh = LGBuild.Rect(card, "Progress");
            bh.anchorMin = new Vector2(0, 0);
            bh.anchorMax = new Vector2(1, 0);
            bh.offsetMin = new Vector2(82, 18);
            bh.offsetMax = new Vector2(-220, 28);
            LGBuild.Bar(bh, UIManager.DS.Gold, 1f - a.OpMonthsLeft / Mathf.Max(1f, SpyInfo.Months(op)), 8f);
            var cancel = LGBuild.Button(card, "Cancel", UIManager.DS.BtnDanger, UIManager.DS.Red, () =>
            {
                EspionageManager.Instance?.CancelOp(a);
                Rebuild();
            }, null, Loc.T("ПРЕРВАТЬ"), 11);
            ((RectTransform)cancel.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-20, 0), new Vector2(170, 38));
            TooltipHelper.Attach(cancel.gameObject, Loc.T("Прервать операцию. Потраченное влияние не возвращается."));
        }

        /// <summary>Первая подходящая третья империя для «Раздора» (-1 — нет).</summary>
        private static int ThirdCandidate(AIEmpireManager target)
        {
            foreach (var o in AIEmpireManager.Alive)
                if (o.OwnerId != target.OwnerId && Contacts.PlayerMet(o.OwnerId)) return o.OwnerId;
            return -1;
        }

        private void DiscordPick(Leader a, AIEmpireManager ai, float top)
        {
            var card = Card(top, 170f, rim: new Color(Purple.r, Purple.g, Purple.b, 0.5f));
            Caption(card, Loc.F("КОГО СТРАВИТЬ С {0}?", ai.AIName.ToUpper()), 14, 18, Purple);
            var list = LGBuild.Rect(card, "List");
            list.Stretch(14, 56, 14, 34);
            var others = new List<AIEmpireManager>();
            foreach (var o in AIEmpireManager.Alive)
                if (o.OwnerId != ai.OwnerId && Contacts.PlayerMet(o.OwnerId)) others.Add(o);
            int n = Mathf.Min(4, others.Count);
            for (int i = 0; i < n; i++)
            {
                var o = others[i];
                Color c = FleetIndicator.OwnerColor(o.OwnerId);
                bool war = AIRelations.AtWar(ai.OwnerId, o.OwnerId);
                var b = LGBuild.Button(list, "Pick", UIManager.DS.BtnNeutral, new Color(c.r, c.g, c.b, 0.7f), () =>
                {
                    if (EspionageManager.Instance != null && EspionageManager.Instance.StartOp(a, SpyOp.Discord, o.OwnerId)) _discordPick = false;
                    Rebuild();
                }, null, Loc.F("{0}\n<size=10>отношение {1:+0;-0;0}{2}</size>", o.AIName.ToUpper(), AIRelations.Opinion(ai, o), (war ? Loc.T(" · уже воюют") : "")), 11);
                var rt = (RectTransform)b.transform;
                rt.anchorMin = new Vector2(i / (float)n, 0);
                rt.anchorMax = new Vector2((i + 1) / (float)n, 1);
                rt.offsetMin = new Vector2(4, 0);
                rt.offsetMax = new Vector2(-4, 0);
            }
            var back = LGBuild.Button(card, "Back", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.3f), () => { _discordPick = false; Rebuild(); }, LGIcon.Back, Loc.T("ОТМЕНА"), 10);
            ((RectTransform)back.transform).At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-14, 12), new Vector2(140, 32));
            Txt(card, Loc.F("<color=#F2C766>★{0:0}</color> · {1} мес. · {2}", SpyInfo.Cost(SpyOp.Discord), SpyInfo.Months(SpyOp.Discord), SpyInfo.Desc(SpyOp.Discord)),
                 10, UIManager.DS.TextMuted, 130, 28, 18, 170, wrap: true);
        }

        // ---- Досье и журнал ----

        private void Dossier(Leader a, AIEmpireManager ai, float top, float height)
        {
            var card = Card(top, height, 0f, 0.5f);
            Caption(card, Loc.F("ДОСЬЕ  ·  {0}", ai.AIName.ToUpper()), 14, 18, FleetIndicator.OwnerColor(ai.OwnerId));
            if (a.Cover < EspionageManager.DeepIntelCover)
            {
                var lockIc = LGIcons.Create(card, LGIcon.Lock, 34, new Color(0.6f, 0.66f, 0.72f, 0.5f));
                lockIc.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 22), new Vector2(34, 34));
                var t = LGBuild.Label(card, Loc.F("Досье закрыто\n<size=11><color=#8AA2A8>Откроется при сети {0:0}% — сейчас {1:0}%.\nЗапасы, доходы, наука и мощь флота цели.</color></size>", EspionageManager.DeepIntelCover, a.Cover),
                    14, UIManager.DS.TextPrimary, TextAnchor.UpperCenter, bold: true);
                t.rectTransform.Stretch(16, 20, 16, height * 0.5f + 2f);
                return;
            }
            string m = LGBuild.Hex(UIManager.DS.TextMuted);
            string Inc(float v) => v >= 0f ? $"<color=#66F59E>+{v:0.#}</color>" : $"<color=#FF7A7A>{v:0.#}</color>";
            var left = LGBuild.Label(card,
                Loc.F("<color={0}>Гелий-3</color>   {1:0}  ({2})\n", m, ai.EnergyCredits, Inc(ai.MonthlyEnergyIncome)) +
                Loc.F("<color={0}>Титан</color>   {1:0}  ({2})\n", m, ai.Minerals, Inc(ai.MonthlyMineralsIncome)) +
                Loc.F("<color={0}>Сплавы</color>   {1:0}  ({2})\n", m, ai.Alloys, Inc(ai.MonthlyAlloysIncome)) +
                Loc.F("<color={0}>Влияние</color>   {1:0}  ({2})", m, ai.Influence, Inc(ai.MonthlyInfluenceIncome)),
                12, UIManager.DS.TextPrimary, TextAnchor.UpperLeft);
            left.rectTransform.TopBand(38, 90, 18, 16);
            left.lineSpacing = 1.3f;
            string cur = ai.CurrentTech != null
                ? $"{ai.CurrentTech.Name} ({Mathf.Clamp01(ai.TechProgress / Mathf.Max(1f, ai.CurrentTech.Cost)) * 100f:0}%)"
                : "—";
            var right = LGBuild.Label(card,
                Loc.F("<color={0}>Систем</color> {1}   <color={2}>Население</color> {3}   <color={4}>Мощь флота</color> {5:N0}\n", m, EmpireStats.SystemCount(ai.OwnerId), m, EmpireStats.Population(ai.OwnerId), m, EmpireStats.MilitaryPower(ai.OwnerId)) +
                Loc.F("<color={0}>Технологий</color> {1} из {2}   <color={3}>Изучает</color> {4}\n", m, ai.ResearchedCount, TechnologyManager.TotalTechCount, m, cur) +
                Loc.F("<color={0}>С вами</color> {1}", m, (ai.AtWar ? Loc.T("<color=#FF7A7A>война</color>") : ai.HasPact ? Loc.T("<color=#66F59E>пакт</color>") : Loc.T("мир"))),
                11, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
            right.rectTransform.TopBand(136, 70, 18, 16);
            right.lineSpacing = 1.25f;
        }

        private void BuildLogCard(float top, float height, float x0, float x1)
        {
            var card = Card(top, height, x0, x1);
            Caption(card, Loc.T("ЖУРНАЛ ОПЕРАЦИЙ"), 14, 18, Purple);
            var host = LGBuild.Rect(card, "LogHost");
            host.Stretch(6, 6, 6, 34);
            var list = LGBuild.ScrollList(host, 4f, 8);
            var es = EspionageManager.Instance;
            if (es == null || es.Log.Count == 0)
            {
                var e = LGBuild.Rect(list, "Empty");
                LGBuild.Height(e.gameObject, 40);
                var t = LGBuild.Label(e, Loc.T("Здесь появятся результаты ваших операций и попытки чужих агентов проникнуть к вам."), 11, UIManager.DS.TextMuted, TextAnchor.MiddleLeft, wrap: true);
                t.rectTransform.Stretch(6, 0, 6, 0);
                return;
            }
            float charsPerLine = Mathf.Max(30f, (x1 - x0) * 928f / 6.6f);
            foreach (var e in es.Log)
            {
                Color c = e.Kind == 1 ? UIManager.DS.Green : e.Kind == 2 ? UIManager.DS.Red : e.Kind == 3 ? UIManager.DS.Gold : UIManager.DS.TextMuted;
                var row = LGBuild.Rect(list, "Entry");
                LGBuild.Height(row.gameObject, Mathf.Max(32f, 15f * Mathf.Ceil((e.Text.Length + 12) / charsPerLine) + 18f));
                var t = LGBuild.Label(row, $"<color={LGBuild.Hex(c)}>●</color>  <color=#8AA2A8>{e.Date}</color>\n{e.Text}", 11, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
                t.rectTransform.Stretch(6, 0, 6, 0);
            }
        }

        // ---- Без назначения: выбор поста ----

        private void BuildAssignment(Leader a)
        {
            var lm = LeaderManager.Instance;
            var card = Card(124f, 326f, rim: new Color(Purple.r, Purple.g, Purple.b, 0.35f));
            Caption(card, Loc.T("КУДА НАПРАВИТЬ АГЕНТА"), 14, 18, Purple);
            Txt(card, Loc.T("Контрразведка защищает вашу империю. Внедрение — агент строит разведсеть в чужой империи; систему, откуда строить, вы выберете на карте."),
                 11, UIManager.DS.TextMuted, 32, 30, 18, 18, wrap: true);
            var host = LGBuild.Rect(card, "Targets");
            host.Stretch(6, 6, 6, 66);
            var list = LGBuild.ScrollList(host, 6f, 6);

            AssignRow(list, a, 0, LGIcon.Shipyard, Loc.T("Контрразведка дома"),
                Loc.F("Защита вашей империи +{0:0}%: чужие агенты чаще проваливаются и раскрываются", (0.10f + a.Level * 0.04f + (a.Has("agt_shadow") ? 0.08f : 0f)) * 100f),
                lm?.SpyOn(0, 0));
            int shown = 0;
            foreach (var ai in AIEmpireManager.All)
            {
                if (ai.IsEliminated || !Contacts.PlayerMet(ai.OwnerId)) continue;
                shown++;
                AssignRow(list, a, ai.OwnerId, LGIcon.Betrayal, Contacts.NameForPlayer(ai.OwnerId),
                    Loc.F("Отношение {0:+0;-0;0}   ·   защита цели {1:0}%   ·   ваша сеть там {2:0}%", ai.Opinion, EspionageManager.TargetCounter(ai) * 100f, EspionageManager.NetworkStrength(ai.OwnerId)) + (ai.AtWar ? Loc.T("   ·   <color=#FF7A7A>война</color>") : ""),
                    lm?.SpyOn(0, ai.OwnerId));
            }
            if (shown == 0)
            {
                var e = LGBuild.Rect(list, "None");
                LGBuild.Height(e.gameObject, 40);
                var t = LGBuild.Label(e, Loc.T("Вы пока не встретили ни одной империи — внедряться некуда. Исследуйте галактику."), 12, UIManager.DS.TextMuted, TextAnchor.MiddleLeft, wrap: true);
                t.rectTransform.Stretch(10, 0, 10, 0);
            }
            // Под списком — шкала-подсказка: что даёт разведка; ниже журнал
            NetworkTrack(0f, null, null, 462f);
            BuildLogCard(606f, MainH - 606f, 0f, 1f);
        }

        private void AssignRow(RectTransform list, Leader a, int target, LGIcon icon, string title, string sub, Leader current)
        {
            var row = LGBuild.Panel(list, "Row", new Color(0.05f, 0.07f, 0.10f, 0.95f));
            LGBuild.Height(row.gameObject, 58);
            LG.Platter(row.gameObject, 12f).SetRim(new Color(1f, 1f, 1f, 0.10f));
            Color c = target == 0 ? UIManager.DS.NeonCyan : FleetIndicator.OwnerColor(target);
            var ic = LGIcons.Create(row.transform, icon, 22, c);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(22, 22));
            var t = LGBuild.Label(row.transform, title, 14, c, TextAnchor.UpperLeft, bold: true);
            t.rectTransform.TopBand(9, 20, 52, 200);
            var s = LGBuild.Label(row.transform, sub, 10, UIManager.DS.TextMuted, TextAnchor.UpperLeft);
            s.rectTransform.TopBand(32, 16, 52, 200);
            if (current != null && current != a)
            {
                var cur = LGBuild.Label(row.transform, Loc.F("сейчас: {0}", current.Name), 10, UIManager.DS.Gold, TextAnchor.UpperRight);
                cur.rectTransform.TopBand(9, 14, 52, 200);
            }
            var btn = LGBuild.Button(row.transform, "Go", UIManager.DS.BtnPrimary, new Color(Purple.r, Purple.g, Purple.b, 0.7f), () =>
            {
                if (target > 0) { SpyNetworkView.BeginPick(a, target); return; }
                LeaderManager.Instance?.AssignSpy(a, target);
                SFXManager.Play(Sfx.UiConfirm);
                Rebuild();
            }, target > 0 ? LGIcon.Target : (LGIcon?)null, target > 0 ? Loc.T("ВЫБРАТЬ СИСТЕМУ") : Loc.T("НАЗНАЧИТЬ"), 10);
            ((RectTransform)btn.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-12, 0), new Vector2(170, 34));
        }

        // ---- Контрразведка ----

        private void BuildCounter(Leader a)
        {
            var card = Card(124f, 132f, rim: new Color(0.40f, 0.86f, 0.82f, 0.35f));
            float total = EspionageManager.PlayerCounter();
            float own = 0.10f + a.Level * 0.04f + (a.Has("agt_shadow") ? 0.08f : 0f);
            Txt(card, $"{total * 100f:0}%", 40, UIManager.DS.NeonCyan, 12, 50, 20, 0, bold: true);
            Caption(card, Loc.T("ЗАЩИТА ВАШЕЙ ИМПЕРИИ"), 64, 22);
            TxtAt(card, Loc.F("Вклад агента <color=#66E0D0>+{0:0}%</color>, остальное — базовые 10% и технологии шифрования.", own * 100f),
                  11, UIManager.DS.TextPrimary, 22, 82, 280, 40, wrap: true);
            Txt(card, Loc.T("Чем выше защита, тем чаще чужие агенты проваливаются и тем чаще вы узнаёте, чья это работа. ") +
                       Loc.T("Агент набирает опыт за службу и за каждую сорванную операцию. ИИ шпионит за вами: крадёт технологии, устраивает диверсии, похищает запасы."),
                 12, UIManager.DS.TextMuted, 18, 100, 320, 20, wrap: true);
            BuildLogCard(268f, MainH - 268f, 0f, 1f);
        }

        // ---- Без агентов ----

        private void BuildWelcome()
        {
            var card = Card(0f, 112f, tint: new Color(Purple.r * 0.10f, Purple.g * 0.08f, Purple.b * 0.15f, 0.97f), rim: new Color(Purple.r, Purple.g, Purple.b, 0.4f));
            var ic = LGIcons.Create(card, LGIcon.Betrayal, 46, Purple);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(30, 0), new Vector2(46, 46));
            Txt(card, Loc.T("Разведка"), 22, Color.white, 16, 28, 100, 20, bold: true);
            Txt(card, Loc.T("Наймите агента (слева внизу) и внедрите его в чужую империю. Он будет строить разведсеть из выбранной вами системы — ") +
                       Loc.T("сильнее всего там, слабее у соседей по гиперкоридорам. Без агента сеть тает. Зону сети видно на карте в режиме «Шпионаж»."),
                 12, UIManager.DS.TextMuted, 48, 56, 100, 20, wrap: true);
            NetworkTrack(0f, null, null, 124f);
            BuildLogCard(268f, MainH - 268f, 0f, 1f);
        }
    }
}
