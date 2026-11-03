using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Центр тайных операций — «оперативный стол». Каждый агент — своя колонка, все видны сразу:
    ///   • сверху крупный портрет с именем, уровнем, чертами, полоской опыта, статусом и гербом цели;
    ///   • ниже — то, что агент делает сейчас:
    ///       внедрён — цель, разведсеть с наградами-узлами на шкале, три операции (шанс, цена, срок, порог сети)
    ///         или идущая операция с прогрессом; досье цели — по кнопке «Досье»;
    ///       контрразведка — защита империи и из чего она складывается, сорванные операции и прорывы;
    ///       без назначения — список постов: контрразведка дома или внедрение (система выбирается на карте);
    ///   • последняя колонка — кандидат на найм (черты с описанием, цена, кнопка «Нанять»).
    /// Колонок больше четырёх — стол прокручивается вбок. Внизу журнал операций и сводка сети по империям.
    /// Без агентов первая колонка объясняет, что даёт разведка.
    /// </summary>
    public sealed class EspionageWindow : MonoBehaviour
    {
        public static EspionageWindow Instance { get; private set; }
        public bool IsOpen => _root != null && LG.IsVisible(_root);

        private const float W = 1840f, H = 1010f, HeaderH = 80f, Pad = 18f;
        private const float ColW = 440f, ColGap = 14f, BannerH = 210f, BottomH = 200f;
        private const float In = 18f;                 // внутренний отступ колонки
        private const float Body = BannerH + 16f;     // где в колонке начинается содержимое

        private static readonly Color Purple = new Color(0.74f, 0.56f, 1f);
        private static readonly Color Cyan = new Color(0.40f, 0.88f, 0.82f);
        private static readonly Color Orange = new Color(1f, 0.67f, 0.33f);
        private static readonly Color Rim = new Color(0.74f, 0.56f, 1f, 0.34f);
        private static readonly Color CardTint = new Color(0.035f, 0.04f, 0.075f, 0.97f);
        private static readonly Color RowTint = new Color(0.05f, 0.055f, 0.10f, 0.95f);
        private static readonly Color Muted = new Color(0.55f, 0.63f, 0.70f, 1f);

        private GameObject _root;
        private RectTransform _fit;
        private Text _subtitle;
        private RectTransform _deskView, _desk, _logHost, _empHost;
        private ScrollRect _deskScroll;
        private int _focusId = -1;        // агент, на котором открыли окно (значок на карте)
        private int _discordId = -1;      // агент, для которого выбираем, кого стравить
        private int _dossierId = -1;      // агент, у которого вместо операций открыто досье
        private bool _dirty, _subscribed, _scrollToFocus;
        private float _timer;

        private static Sprite s_fadeUp;

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
            // Внешний контейнер только масштабирует окно под экран; анимация появления — у самого окна
            _fit = LGBuild.Rect(host.transform, "EspionageFit");
            _fit.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W, H));

            var rootRt = LGBuild.Rect(_fit, "EspionageWindow");
            rootRt.Stretch();
            _root = rootRt.gameObject;
            var bg = _root.AddComponent<Image>();
            bg.color = UIManager.DS.BgDeep;
            bg.raycastTarget = true;
            LG.Glass(_root, 26f).SetRim(Rim);
            LG.Motion(_root, LGAppear.Kind.Pop).inDuration = 0.4f;

            // Плотная подложка: карта не просвечивает сквозь окно, текст читается
            var shadeMask = LG.RoundedMask(rootRt, 26f);
            var shade = LGBuild.Panel(shadeMask, "Shade", new Color(0.016f, 0.016f, 0.04f, 0.94f));
            shade.rectTransform.Stretch();
            LG.Ignore(shadeMask.gameObject);

            BuildHeader(rootRt);

            var body = LGBuild.Rect(rootRt, "Body");
            body.Stretch(Pad, Pad, Pad, HeaderH + 14f);

            // Стол: колонки агентов, прокрутка вбок
            _deskView = LGBuild.Rect(body, "Desk");
            _deskView.Stretch(0, BottomH + 14f, 0, 0);
            _deskView.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(14, 0);
            var hit = _deskView.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            _deskScroll = _deskView.gameObject.AddComponent<ScrollRect>();
            _deskScroll.horizontal = true;
            _deskScroll.vertical = false;
            _deskScroll.movementType = ScrollRect.MovementType.Clamped;
            _deskScroll.inertia = true;
            _deskScroll.decelerationRate = 0.12f;
            _deskScroll.scrollSensitivity = 40f;
            _desk = LGBuild.Rect(_deskView, "Columns");
            _desk.anchorMin = new Vector2(0, 0);
            _desk.anchorMax = new Vector2(0, 1);
            _desk.pivot = new Vector2(0, 0.5f);
            _desk.offsetMin = _desk.offsetMax = Vector2.zero;
            _deskScroll.viewport = _deskView;
            _deskScroll.content = _desk;

            // Низ: журнал и сводка по империям
            var bottom = LGBuild.Rect(body, "Bottom");
            bottom.anchorMin = new Vector2(0, 0);
            bottom.anchorMax = new Vector2(1, 0);
            bottom.pivot = new Vector2(0.5f, 0);
            bottom.offsetMin = Vector2.zero;
            bottom.offsetMax = new Vector2(0, BottomH);
            _logHost = LGBuild.Rect(bottom, "Log");
            _logHost.Stretch(0, 0, 640f + ColGap, 0);
            _empHost = LGBuild.Rect(bottom, "Empires");
            _empHost.anchorMin = new Vector2(1, 0);
            _empHost.anchorMax = new Vector2(1, 1);
            _empHost.pivot = new Vector2(1, 0.5f);
            _empHost.offsetMin = new Vector2(-640f, 0);
            _empHost.offsetMax = Vector2.zero;

            LG.Skin(_root.transform);
            _root.SetActive(false);
        }

        private void BuildHeader(RectTransform root)
        {
            var header = LGBuild.Panel(root, "Header", UIManager.DS.BgHeader);
            header.rectTransform.TopBand(0, HeaderH);
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

        /// <summary>Открыть окно и прокрутить стол к этому агенту (значок агента на карте).</summary>
        public void OpenAgent(int leaderId) => OpenInternal(leaderId);

        public void Open() => OpenInternal(-1);

        private void OpenInternal(int focus)
        {
            if (_root == null) return;
            _focusId = focus;
            _scrollToFocus = true;
            _discordId = -1;
            _dossierId = -1;
            UIManager.Instance?.ShowModalDimPublic();
            MapModeController.HideGlobal();
            Subscribe();
            FitToScreen();
            _fit.SetAsLastSibling();
            LG.Show(_root);
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

        /// <summary>Окно рассчитано на 1840×1010; на узких экранах — равномерно уменьшается.</summary>
        private void FitToScreen()
        {
            var canvasRt = _fit.parent as RectTransform;
            if (canvasRt == null) return;
            Vector2 size = canvasRt.rect.size;
            float k = Mathf.Min(1f, (size.x - 40f) / W, (size.y - 30f) / H);
            _fit.localScale = Vector3.one * Mathf.Max(0.5f, k);
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
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                UIEsc.Consume();
                if (_discordId >= 0) { _discordId = -1; Rebuild(); return; }
                Close();
                return;
            }
            _timer -= Time.unscaledDeltaTime;
            if (_dirty || _timer <= 0f)
            {
                bool full = _dirty;
                _dirty = false;
                _timer = 2f;
                if (full) Rebuild(); else RefreshHeader();
            }
        }

        // ================================================================ Сборка

        private void Rebuild()
        {
            RefreshHeader();
            BuildDesk();
            BuildLog();
            BuildEmpires();
        }

        private void RefreshHeader()
        {
            var es = EspionageManager.Instance;
            if (_subtitle == null) return;
            int agents = 0;
            foreach (var _ in EspionageManager.PlayerAgents()) agents++;
            _subtitle.text = Loc.F("Агентов {0}   ·   защита вашей империи <color=#66E0D0>{1:0}%</color>", agents, EspionageManager.PlayerCounter() * 100f) +
                             (es != null
                                ? Loc.F("   ·   операции: <color=#66F59E>{0}</color> удачных, <color=#FF7A7A>{1}</color> провальных", es.Succeeded, es.Failed) +
                                  Loc.F("   ·   чужих попыток сорвано: {0}   ·   прорывов к вам: {1}", es.Foiled, es.Breaches)
                                : "");
        }

        private void BuildDesk()
        {
            LGBuild.Clear(_desk);
            var agents = new List<Leader>(EspionageManager.PlayerAgents());
            if (_discordId >= 0 && !agents.Exists(l => l.Id == _discordId && l.Post == LeaderPost.Spy && l.TargetOwner > 0 && l.OpKind < 0)) _discordId = -1;

            int i = 0, focusIndex = -1;
            if (agents.Count == 0) WelcomeColumn(NewColumn(i++, Purple, 0.35f, false));
            foreach (var a in agents)
            {
                if (a.Id == _focusId) focusIndex = i;
                AgentColumn(a, i++);
            }
            CandidateColumn(i++);

            float contentW = i * ColW + (i - 1) * ColGap;
            _desk.sizeDelta = new Vector2(contentW, 0f);
            _deskScroll.horizontal = contentW > _deskView.rect.width + 1f;

            if (_scrollToFocus)
            {
                _scrollToFocus = false;
                float viewW = _deskView.rect.width;
                float x = focusIndex > 0 ? focusIndex * (ColW + ColGap) - (viewW - ColW) * 0.5f : 0f;
                _desk.anchoredPosition = new Vector2(-Mathf.Clamp(x, 0f, Mathf.Max(0f, contentW - viewW)), 0f);
                _deskScroll.velocity = Vector2.zero;
            }
        }

        // ================================================================ Колонка: каркас

        private RectTransform NewColumn(int index, Color accent, float rimAlpha, bool focused)
        {
            var p = LGBuild.Panel(_desk, "Column", CardTint);
            var rt = p.rectTransform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.offsetMin = new Vector2(index * (ColW + ColGap), 0);
            rt.offsetMax = new Vector2(index * (ColW + ColGap) + ColW, 0);
            LG.Platter(p.gameObject, 18f).SetRim(new Color(accent.r, accent.g, accent.b, focused ? 0.95f : rimAlpha));
            return rt;
        }

        /// <summary>Полоса-градиент снизу вверх (затемнение под подписью на портрете).</summary>
        private static Sprite FadeUp
        {
            get
            {
                if (s_fadeUp != null) return s_fadeUp;
                var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                for (int y = 0; y < 64; y++) tex.SetPixel(0, y, new Color(1f, 1f, 1f, Mathf.Pow(1f - y / 63f, 1.4f)));
                tex.Apply();
                s_fadeUp = Sprite.Create(tex, new Rect(0, 0, 1, 64), new Vector2(0.5f, 0.5f));
                s_fadeUp.name = "EspFadeUp";
                return s_fadeUp;
            }
        }

        /// <summary>
        /// Портрет во всю ширину колонки: широкая вырезка вокруг лица, затемнение снизу. Возвращает слой баннера
        /// (подписи кладутся в него). Без портрета — фиолетовая подложка со значком агента.
        /// </summary>
        private static RectTransform Banner(RectTransform col, Leader l, bool dim, string tooltip)
        {
            var mask = LG.RoundedMask(col, 18f, 1f);
            LG.Ignore(mask.gameObject);
            var banner = LGBuild.Rect(mask, "Banner");
            banner.TopBand(0, BannerH);

            var face = l != null ? LeaderFaces.For(l) : null;
            if (face != null && face.Texture != null)
            {
                var art = new GameObject("Portrait").AddComponent<RawImage>();
                art.transform.SetParent(banner, false);
                art.rectTransform.Stretch();
                art.texture = face.Texture;
                // Портреты 2:3 (1342×2000): берём всю ширину, по высоте — полосу вокруг лица
                float h = Mathf.Min(1f, (1342f / 2000f) * BannerH / ColW);
                float v = Mathf.Clamp(1f - face.Center.y / 2000f - h * 0.55f, 0f, 1f - h);
                art.uvRect = new Rect(0f, v, 1f, h);
                art.color = dim ? new Color(0.55f, 0.55f, 0.62f, 1f) : Color.white;
                art.raycastTarget = tooltip != null;
                if (tooltip != null) TooltipHelper.Attach(art.gameObject, tooltip);
            }
            else
            {
                var plate = LGBuild.Panel(banner, "Plate", new Color(Purple.r * 0.22f, Purple.g * 0.16f, Purple.b * 0.32f, 1f), raycast: tooltip != null);
                plate.rectTransform.Stretch();
                var ic = LGIcons.Create(plate.transform, LGIcon.Betrayal, 90, new Color(Purple.r, Purple.g, Purple.b, 0.55f));
                ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 16), new Vector2(90, 90));
                if (tooltip != null) TooltipHelper.Attach(plate.gameObject, tooltip);
            }

            var fade = LGBuild.Panel(banner, "Fade", new Color(0.016f, 0.016f, 0.045f, 0.97f));
            fade.sprite = FadeUp;
            fade.rectTransform.anchorMin = new Vector2(0, 0);
            fade.rectTransform.anchorMax = new Vector2(1, 0.72f);
            fade.rectTransform.offsetMin = fade.rectTransform.offsetMax = Vector2.zero;
            return banner;
        }

        private static void BannerTitle(RectTransform banner, string name, string sub, Color subColor, string caption = null, Color? capColor = null)
        {
            if (caption != null)
            {
                var c = LGBuild.Label(banner, caption, 10, capColor ?? Purple, TextAnchor.LowerLeft, bold: true).Track();
                c.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(In, 64), new Vector2(ColW - 2 * In, 14));
            }
            var n = LGBuild.Label(banner, name, 22, Color.white, TextAnchor.LowerLeft, bold: true);
            n.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(In, 34), new Vector2(ColW - 2 * In, 30));
            var s = LGBuild.Label(banner, sub, 12, subColor, TextAnchor.LowerLeft);
            s.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(In, 14), new Vector2(ColW - 2 * In, 18));
        }

        /// <summary>Плашка-статус в левом верхнем углу портрета.</summary>
        private static void StatusChip(RectTransform banner, string text, Color c)
        {
            float w = 34f + text.Length * 8.2f;
            var chip = LGBuild.Panel(banner, "Status", new Color(0.02f, 0.02f, 0.05f, 0.78f));
            chip.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -12), new Vector2(w, 24));
            LG.Platter(chip.gameObject, 12f).SetRim(new Color(c.r, c.g, c.b, 0.6f));
            var t = LGBuild.Label(chip.transform, $"<color={LGBuild.Hex(c)}>●</color>  {text}", 10, Color.white, TextAnchor.MiddleCenter, bold: true).Track(1.2f);
            t.rectTransform.Stretch(4, 0, 4, 0);
        }

        /// <summary>Герб цели (или значок) в правом верхнем углу портрета.</summary>
        private static void CornerMark(RectTransform banner, int owner, LGIcon fallback, Color c, string tooltip)
        {
            var host = LGBuild.Panel(banner, "Mark", new Color(0.02f, 0.02f, 0.05f, 0.6f), raycast: tooltip != null);
            host.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-12, -12), new Vector2(52, 52));
            LG.Platter(host.gameObject, 26f).SetRim(new Color(c.r, c.g, c.b, 0.7f));
            var emblem = owner > 0 ? FactionEmblems.ForOwner(owner) : null;
            if (emblem != null) FactionEmblems.Create(host.transform, emblem, 38, c);
            else
            {
                var ic = LGIcons.Create(host.transform, fallback, 26, c);
                ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26, 26));
            }
            if (tooltip != null) TooltipHelper.Attach(host.gameObject, tooltip);
        }

        /// <summary>Тонкая полоска опыта по нижнему краю портрета.</summary>
        private static void XpLine(RectTransform banner, Leader l, Color c)
        {
            var host = LGBuild.Rect(banner, "Xp");
            host.anchorMin = new Vector2(0, 0);
            host.anchorMax = new Vector2(1, 0);
            host.pivot = new Vector2(0.5f, 0);
            host.offsetMin = new Vector2(In, 4);
            host.offsetMax = new Vector2(-In, 8);
            LGBuild.Bar(host, new Color(c.r, c.g, c.b, 0.8f), l.LevelProgress, 3f);
        }

        /// <summary>Ряд кнопок по низу колонки (равной ширины).</summary>
        private readonly struct BtnDef
        {
            public readonly string Label;
            public readonly LGIcon? Icon;
            public readonly Color Tint, Rim;
            public readonly System.Action Act;

            public BtnDef(string label, LGIcon? icon, Color tint, Color rim, System.Action act)
            {
                Label = label; Icon = icon; Tint = tint; Rim = rim; Act = act;
            }
        }

        private static List<Button> ButtonRow(RectTransform col, params BtnDef[] defs)
        {
            var list = new List<Button>();
            float gap = 8f, w = (ColW - 2 * In - gap * (defs.Length - 1)) / defs.Length;
            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                var b = LGBuild.Button(col, "Btn", d.Tint, d.Rim, d.Act, d.Icon, d.Label, 11);
                ((RectTransform)b.transform).At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(In + i * (w + gap), In), new Vector2(w, 38));
                list.Add(b);
            }
            return list;
        }

        // ================================================================ Колонка агента

        private void AgentColumn(Leader a, int index)
        {
            bool spy = a.Post == LeaderPost.Spy;
            bool counter = spy && a.TargetOwner == 0;
            bool inside = spy && a.TargetOwner > 0;
            Color accent = !spy ? Orange : counter ? Cyan : a.OpKind >= 0 ? UIManager.DS.Gold : inside ? FleetIndicator.OwnerColor(a.TargetOwner) : Purple;

            var col = NewColumn(index, accent, 0.40f, a.Id == _focusId);
            var banner = Banner(col, a, false, AgentTooltip(a));
            string traits = TraitsText(a, "  ·  ");
            BannerTitle(banner, a.Name + Loc.F("  <size=14><color=#8DA2B4>· ур. {0}</color></size>", a.Level),
                        traits.Length > 0 ? traits : Loc.T("без особых черт"), UIManager.DS.Gold);
            XpLine(banner, a, Purple);

            if (!spy)
            {
                StatusChip(banner, Loc.T("ЖДЁТ НАЗНАЧЕНИЯ"), Orange);
                Assignment(col, a);
                return;
            }
            if (counter)
            {
                StatusChip(banner, Loc.T("КОНТРРАЗВЕДКА"), Cyan);
                CornerMark(banner, 0, LGIcon.ShieldDome, Cyan, null);
                Counter(col, a);
                return;
            }

            var ai = AIEmpireManager.For(a.TargetOwner);
            StatusChip(banner, a.OpKind >= 0 ? Loc.T("ИДЁТ ОПЕРАЦИЯ") : Loc.T("СТРОИТ СЕТЬ"), a.OpKind >= 0 ? UIManager.DS.Gold : Purple);
            CornerMark(banner, a.TargetOwner, LGIcon.Betrayal, FleetIndicator.OwnerColor(a.TargetOwner), Contacts.NameForPlayer(a.TargetOwner));
            Infiltrated(col, a, ai);
        }

        // ---- Внедрён в империю ----

        private void Infiltrated(RectTransform col, Leader a, AIEmpireManager ai)
        {
            Color oc = FleetIndicator.OwnerColor(a.TargetOwner);
            var sys = EmpireStats.GetSystem(a.TargetSystem);
            string rel = ai == null ? "" : ai.AtWar ? Loc.T("<color=#FF7A7A>война</color>") : ai.HasPact ? Loc.T("<color=#66F59E>пакт</color>") : Loc.T("мир");
            string where = Loc.T("система не выбрана");
            if (sys != null)
            {
                string role = Role(sys, a.TargetOwner).TrimEnd(' ', '·');
                where = role.Length > 0 ? $"{sys.Name} ({role})" : sys.Name;
            }
            Txt(col, $"<b><color={LGBuild.Hex(oc)}>{Contacts.NameForPlayer(a.TargetOwner)}</color></b>  <color=#8DA2B4>·  {where}  ·</color>  {rel}",
                13, UIManager.DS.TextPrimary, Body, 20, In, In);
            if (ai != null)
            {
                // Подсказка — на прозрачной подложке под строкой цели
                var hit = LGBuild.Panel(col, "TargetHit", new Color(0, 0, 0, 0), raycast: true);
                hit.rectTransform.TopBand(Body, 20, In, In);
                TooltipHelper.Attach(hit.gameObject, Loc.F("Отношение {0:+0;-0;0}   ·   защита цели {1:0}%", ai.Opinion, EspionageManager.TargetCounter(ai) * 100f) +
                    (sys != null ? "\n" + Loc.F("Сеть в системе агента {0:0}%", EspionageManager.NetworkAt(sys.Id)) : ""));
            }

            // Разведсеть: подпись, цифра, рост и шкала наград
            Caption(col, Loc.T("РАЗВЕДСЕТЬ"), Body + 32, In);
            string grow = a.OpKind >= 0 ? Loc.T("<color=#F2C766>заморожена</color>")
                                        : Loc.F("<color=#66F59E>+{0:0.#}</color> / мес", EspionageManager.BuildRate(a).Total);
            Txt(col, $"<color=#BD8FFF><b>{a.Cover:0}%</b></color>   {grow}", 13, UIManager.DS.TextPrimary, Body + 28, 20, In, In, TextAnchor.UpperRight);
            Track(col, a.Cover, Body + 54);
            NextMilestoneLine(col, a.Cover, Body + 94);

            float top = Body + 126;
            if (ai == null)
            {
                Txt(col, Loc.T("Империя пала — отзовите агента."), 12, Muted, top, 20, In, In);
                ButtonRow(col, RecallDef(a));
                return;
            }

            if (_dossierId == a.Id) Dossier(col, a, ai, top);
            else if (a.OpKind >= 0) ActiveOp(col, a, ai, top);
            else if (_discordId == a.Id) DiscordPick(col, a, ai, top);
            else Operations(col, a, ai, top);

            if (_discordId == a.Id && _dossierId != a.Id)
            {
                ButtonRow(col, new BtnDef(Loc.T("ОТМЕНА"), LGIcon.Close, UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.3f), () => { _discordId = -1; Rebuild(); }));
                return;
            }

            bool dossier = _dossierId == a.Id;
            var btns = ButtonRow(col,
                new BtnDef(Loc.T("СИСТЕМА"), LGIcon.Target, UIManager.DS.BtnNeutral, new Color(Purple.r, Purple.g, Purple.b, 0.6f), () => SpyNetworkView.BeginPick(a, a.TargetOwner)),
                new BtnDef(dossier ? Loc.T("ОПЕРАЦИИ") : Loc.T("ДОСЬЕ"), dossier ? LGIcon.Betrayal : LGIcon.Info, UIManager.DS.BtnNeutral, new Color(Purple.r, Purple.g, Purple.b, 0.6f), () =>
                {
                    _dossierId = _dossierId == a.Id ? -1 : a.Id;
                    if (_discordId == a.Id) _discordId = -1;
                    SFXManager.Play(Sfx.UiClick);
                    Rebuild();
                }),
                RecallDef(a));
            btns[0].interactable = a.OpKind < 0;
            TooltipHelper.Attach(btns[0].gameObject, a.OpKind < 0
                ? Loc.T("Выбрать на карте другую систему империи, откуда строить сеть. Уже построенная сеть не пропадает.")
                : Loc.T("Во время операции агент не может сменить систему"));
            TooltipHelper.Attach(btns[1].gameObject, dossier ? Loc.T("Вернуться к операциям")
                : Loc.F("Запасы, доходы, наука и мощь флота цели — открываются при сети {0:0}%", EspionageManager.DeepIntelCover));
            TooltipHelper.Attach(btns[2].gameObject, Loc.T("Агент покидает пост: идущая операция пропадёт, а разведсеть без него начнёт таять (−2% в месяц). Его можно назначить заново."));
        }

        private BtnDef RecallDef(Leader a) =>
            new BtnDef(Loc.T("ОТОЗВАТЬ"), LGIcon.Back, UIManager.DS.BtnDanger, UIManager.DS.Red, () =>
            {
                LeaderManager.Instance?.Unassign(a);
                SFXManager.Play(Sfx.UiBack);
                if (_dossierId == a.Id) _dossierId = -1;
                if (_discordId == a.Id) _discordId = -1;
                Rebuild();
            });

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

        private static void Track(RectTransform col, float cover, float top)
        {
            var track = LGBuild.Rect(col, "Track");
            track.TopBand(top, 34, In + 4, In + 4);
            var barHost = LGBuild.Rect(track, "Bar");
            barHost.TopBand(13, 8, 0, 0);
            LGBuild.Bar(barHost, Purple, Mathf.Clamp01(cover / TrackMax), 6f);

            bool nextFound = false;
            foreach (var m in Milestones)
            {
                bool got = cover >= m.Cover;
                bool isNext = !got && !nextFound;
                if (isNext) nextFound = true;
                var node = LGBuild.Panel(track, "Node", got ? new Color(Purple.r * 0.55f, Purple.g * 0.40f, Purple.b * 0.75f, 1f)
                                                           : new Color(0.05f, 0.05f, 0.09f, 1f), raycast: true);
                var nrt = node.rectTransform;
                nrt.anchorMin = nrt.anchorMax = new Vector2(m.Cover / TrackMax, 1);
                nrt.pivot = new Vector2(0.5f, 0.5f);
                nrt.sizeDelta = new Vector2(26f, 26f);
                nrt.anchoredPosition = new Vector2(0f, -17f);
                LG.Platter(node.gameObject, 13f).SetRim(got ? new Color(Purple.r, Purple.g, Purple.b, 1f)
                                                             : isNext ? new Color(Purple.r, Purple.g, Purple.b, 0.7f)
                                                             : new Color(1f, 1f, 1f, 0.18f));
                var ic = LGIcons.Create(node.transform, m.Icon, 14, got ? Color.white : isNext ? Purple : new Color(0.6f, 0.66f, 0.72f, 0.55f));
                ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14, 14));
                TooltipHelper.Attach(node.gameObject, Loc.F("<b>{0}</b> — при сети {1:0}%\n{2}\n\n", m.Title, m.Cover, m.Desc) +
                    (got ? Loc.T("<color=#66F59E>Открыто</color>") : Loc.F("<color=#8AA2A8>Ещё {0:0.#}%</color>", m.Cover - cover)));
            }
        }

        private static void NextMilestoneLine(RectTransform col, float cover, float top)
        {
            Milestone? next = null;
            foreach (var m in Milestones) if (cover < m.Cover) { next = m; break; }
            string line = next.HasValue
                ? Loc.F("<color=#8AA2A8>Следующее:</color>  <color=#BD8FFF><b>{0}</b></color> при {1:0}%  <color=#8AA2A8>(ещё {2:0.#}%)</color>", next.Value.Title, next.Value.Cover, next.Value.Cover - cover)
                : Loc.T("<color=#66F59E>Все награды разведки открыты</color>");
            Txt(col, line, 12, UIManager.DS.TextPrimary, top, 18, In, In);
        }

        // ---- Операции ----

        private void Operations(RectTransform col, Leader a, AIEmpireManager ai, float top)
        {
            Caption(col, Loc.T("ОПЕРАЦИИ"), top, In);
            var ops = new[] { SpyOp.Sabotage, SpyOp.Steal, SpyOp.Discord };
            for (int i = 0; i < ops.Length; i++) OpRow(col, a, ai, ops[i], top + 22 + i * 72);
        }

        private void OpRow(RectTransform col, Leader a, AIEmpireManager ai, SpyOp op, float top)
        {
            var es = EspionageManager.Instance;
            bool netOk = a.Cover >= SpyInfo.MinCover(op);
            string reason = null;
            bool can;
            if (op == SpyOp.Discord)
            {
                bool money = (EconomyManager.Instance?.Influence ?? 0f) >= SpyInfo.Cost(op);
                can = netOk && money && ThirdCandidate(ai) >= 0;
                if (!can) reason = !netOk ? Loc.F("Нужна разведсеть {0:0}% (сейчас {1:0}%)", SpyInfo.MinCover(op), a.Cover)
                                 : !money ? Loc.F("Нужно {0:0} влияния", SpyInfo.Cost(op))
                                 : Loc.T("Нет второй известной империи, которую можно стравить");
            }
            else can = es != null && es.CanStart(a, op, -1, out reason);

            var row = LGBuild.Panel(col, "Op", netOk ? RowTint : new Color(0.03f, 0.03f, 0.05f, 0.9f), raycast: true);
            row.rectTransform.TopBand(top, 64, In, In);
            LG.Platter(row.gameObject, 12f).SetRim(netOk ? new Color(Purple.r, Purple.g, Purple.b, 0.30f) : new Color(1f, 1f, 1f, 0.07f));
            TooltipHelper.Attach(row.gameObject, $"<b>{SpyInfo.Name(op)}</b>\n{SpyInfo.Desc(op)}");

            var icBg = LGBuild.Panel(row.transform, "IcBg", new Color(Purple.r * 0.2f, Purple.g * 0.15f, Purple.b * 0.3f, netOk ? 1f : 0.4f));
            icBg.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(44, 44));
            LG.Platter(icBg.gameObject, 11f);
            var ic = LGIcons.Create(icBg.transform, SpyInfo.Icon(op), 24, netOk ? Purple : Muted);
            ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24, 24));

            var rrt = row.rectTransform;
            Txt(rrt, SpyInfo.Name(op), 14, netOk ? Color.white : new Color(0.75f, 0.80f, 0.85f, 0.7f), 11, 20, 66, 140, bold: true);
            string info;
            if (netOk)
            {
                float chance = EspionageManager.SuccessChance(a, op, ai);
                Color cc = chance >= 0.6f ? UIManager.DS.Green : chance >= 0.4f ? UIManager.DS.Gold : UIManager.DS.Red;
                info = Loc.F("<color={0}><b>{1:0}%</b></color> успех   <color=#F2C766>★{2:0}</color>   {3} мес.", LGBuild.Hex(cc), chance * 100f, SpyInfo.Cost(op), SpyInfo.Months(op));
            }
            else info = Loc.F("нужна сеть {0:0}%  ·  ещё {1:0.#}%", SpyInfo.MinCover(op), SpyInfo.MinCover(op) - a.Cover);
            Txt(rrt, info, 11, netOk ? UIManager.DS.TextPrimary : Muted, 34, 18, 66, 140);

            string label = !netOk ? $"{SpyInfo.MinCover(op):0}%" : op == SpyOp.Discord ? Loc.T("ВЫБРАТЬ") : Loc.T("НАЧАТЬ");
            var btn = LGBuild.Button(row.transform, "Start", can ? UIManager.DS.BtnPrimary : UIManager.DS.BtnNeutral,
                new Color(Purple.r, Purple.g, Purple.b, can ? 0.75f : 0.25f), () =>
                {
                    if (op == SpyOp.Discord) { _discordId = a.Id; _dossierId = -1; SFXManager.Play(Sfx.UiClick); Rebuild(); }
                    else { EspionageManager.Instance?.StartOp(a, op, -1); Rebuild(); }
                }, netOk ? (LGIcon?)null : LGIcon.Lock, label, 11);
            ((RectTransform)btn.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-12, 0), new Vector2(118, 36));
            btn.interactable = can;
            // Что даст операция и чем рискуете; если кнопка серая — сначала причина
            string preview = EspionageManager.OpPreview(a, op, ai);
            TooltipHelper.Attach(btn.gameObject, can || reason == null ? preview : $"<color=#FF8888>{reason}</color>\n\n{preview}");
        }

        private void ActiveOp(RectTransform col, Leader a, AIEmpireManager ai, float top)
        {
            var op = (SpyOp)a.OpKind;
            Color g = UIManager.DS.Gold;
            var box = LGBuild.Panel(col, "ActiveOp", new Color(0.10f, 0.08f, 0.035f, 0.95f));
            box.rectTransform.TopBand(top, 236, In, In);
            LG.Platter(box.gameObject, 14f).SetRim(new Color(g.r, g.g, g.b, 0.5f));
            var b = box.rectTransform;

            Caption(b, Loc.T("ИДЁТ ОПЕРАЦИЯ"), 14, 16, g);
            var ic = LGIcons.Create(b, SpyInfo.Icon(op), 30, g);
            ic.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -40), new Vector2(30, 30));
            Txt(b, SpyInfo.Name(op), 17, Color.white, 38, 24, 58, 16, bold: true);
            var bh = LGBuild.Rect(b, "Progress");
            bh.TopBand(68, 8, 58, 16);
            LGBuild.Bar(bh, g, 1f - a.OpMonthsLeft / Mathf.Max(1f, SpyInfo.Months(op)), 7f);
            string extra = op == SpyOp.Discord && AIEmpireManager.For(a.OpArg) != null ? Loc.F("\nстравливаем с {0}", AIEmpireManager.For(a.OpArg).AIName) : "";
            Txt(b, Loc.F("Осталось {0} мес.   ·   шанс успеха {1:0}%", a.OpMonthsLeft, EspionageManager.SuccessChance(a, op, ai) * 100f) + extra,
                12, UIManager.DS.TextPrimary, 84, 36, 58, 16);
            Txt(b, SpyInfo.Desc(op) + Loc.T(" Пока идёт операция, сеть не растёт."), 11, UIManager.DS.TextMuted, 122, 56, 16, 16, wrap: true);

            var cancel = LGBuild.Button(b, "Cancel", UIManager.DS.BtnDanger, UIManager.DS.Red, () =>
            {
                EspionageManager.Instance?.CancelOp(a);
                Rebuild();
            }, null, Loc.T("ПРЕРВАТЬ"), 11);
            var crt = (RectTransform)cancel.transform;
            crt.anchorMin = new Vector2(0, 0);
            crt.anchorMax = new Vector2(1, 0);
            crt.pivot = new Vector2(0.5f, 0);
            crt.offsetMin = new Vector2(16, 14);
            crt.offsetMax = new Vector2(-16, 48);
            TooltipHelper.Attach(cancel.gameObject, Loc.T("Прервать операцию. Потраченное влияние не возвращается."));
        }

        /// <summary>Первая подходящая третья империя для «Раздора» (-1 — нет).</summary>
        private static int ThirdCandidate(AIEmpireManager target)
        {
            foreach (var o in AIEmpireManager.Alive)
                if (o.OwnerId != target.OwnerId && Contacts.PlayerMet(o.OwnerId)) return o.OwnerId;
            return -1;
        }

        private void DiscordPick(RectTransform col, Leader a, AIEmpireManager ai, float top)
        {
            Caption(col, Loc.F("КОГО СТРАВИТЬ С {0}?", ai.AIName.ToUpper()), top, In, Purple);
            var others = new List<AIEmpireManager>();
            foreach (var o in AIEmpireManager.Alive)
                if (o.OwnerId != ai.OwnerId && Contacts.PlayerMet(o.OwnerId)) others.Add(o);
            int n = Mathf.Min(4, others.Count);
            for (int i = 0; i < n; i++)
            {
                var o = others[i];
                Color c = FleetIndicator.OwnerColor(o.OwnerId);
                bool war = AIRelations.AtWar(ai.OwnerId, o.OwnerId);
                var b = LGBuild.Button(col, "Pick", UIManager.DS.BtnNeutral, new Color(c.r, c.g, c.b, 0.7f), () =>
                {
                    if (EspionageManager.Instance != null && EspionageManager.Instance.StartOp(a, SpyOp.Discord, o.OwnerId)) _discordId = -1;
                    Rebuild();
                }, null, Loc.F("{0}\n<size=10>отношение {1:+0;-0;0}{2}</size>", o.AIName.ToUpper(), AIRelations.Opinion(ai, o), (war ? Loc.T(" · уже воюют") : "")), 11);
                ((RectTransform)b.transform).TopBand(top + 22 + i * 52, 46, In, In);
            }
            if (n <= 3)
                Txt(col, Loc.F("<color=#F2C766>★{0:0}</color> · {1} мес. · {2}", SpyInfo.Cost(SpyOp.Discord), SpyInfo.Months(SpyOp.Discord), SpyInfo.Desc(SpyOp.Discord)),
                    11, UIManager.DS.TextMuted, top + 26 + n * 52, 48, In, In, wrap: true);
        }

        // ---- Досье ----

        private static void Dossier(RectTransform col, Leader a, AIEmpireManager ai, float top)
        {
            Caption(col, Loc.F("ДОСЬЕ  ·  {0}", ai.AIName.ToUpper()), top, In, FleetIndicator.OwnerColor(ai.OwnerId));
            if (a.Cover < EspionageManager.DeepIntelCover)
            {
                var lockIc = LGIcons.Create(col, LGIcon.Lock, 34, new Color(0.6f, 0.66f, 0.72f, 0.5f));
                lockIc.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -top - 44), new Vector2(34, 34));
                Txt(col, Loc.F("Досье закрыто\n<size=12><color=#8AA2A8>Откроется при сети {0:0}% — сейчас {1:0}%.\nЗапасы, доходы, наука и мощь флота цели.</color></size>", EspionageManager.DeepIntelCover, a.Cover),
                    15, UIManager.DS.TextPrimary, top + 90, 80, In, In, TextAnchor.UpperCenter, bold: true);
                return;
            }
            string m = LGBuild.Hex(UIManager.DS.TextMuted);
            string Inc(float v) => v >= 0f ? $"<color=#66F59E>+{v:0.#}</color>" : $"<color=#FF7A7A>{v:0.#}</color>";
            var res = Txt(col,
                Loc.F("<color={0}>Гелий-3</color>   {1:0}  ({2})\n", m, ai.EnergyCredits, Inc(ai.MonthlyEnergyIncome)) +
                Loc.F("<color={0}>Титан</color>   {1:0}  ({2})\n", m, ai.Minerals, Inc(ai.MonthlyMineralsIncome)) +
                Loc.F("<color={0}>Сплавы</color>   {1:0}  ({2})\n", m, ai.Alloys, Inc(ai.MonthlyAlloysIncome)) +
                Loc.F("<color={0}>Влияние</color>   {1:0}  ({2})", m, ai.Influence, Inc(ai.MonthlyInfluenceIncome)),
                12, UIManager.DS.TextPrimary, top + 24, 96, In, In);
            res.lineSpacing = 1.3f;
            string cur = ai.CurrentTech != null
                ? $"{ai.CurrentTech.Name} ({Mathf.Clamp01(ai.TechProgress / Mathf.Max(1f, ai.CurrentTech.Cost)) * 100f:0}%)"
                : "—";
            var info = Txt(col,
                Loc.F("<color={0}>Систем</color> {1}   <color={2}>Население</color> {3}\n", m, EmpireStats.SystemCount(ai.OwnerId), m, EmpireStats.Population(ai.OwnerId)) +
                Loc.F("<color={0}>Мощь флота</color> {1:N0}\n", m, EmpireStats.MilitaryPower(ai.OwnerId)) +
                Loc.F("<color={0}>Технологий</color> {1} из {2}\n", m, ai.ResearchedCount, TechnologyManager.TotalTechCount) +
                Loc.F("<color={0}>Изучает</color> {1}", m, cur),
                12, UIManager.DS.TextPrimary, top + 128, 100, In, In, wrap: true);
            info.lineSpacing = 1.3f;
        }

        // ---- Контрразведка ----

        private void Counter(RectTransform col, Leader a)
        {
            var es = EspionageManager.Instance;
            float total = EspionageManager.PlayerCounter();
            float own = 0.10f + a.Level * 0.04f + (a.Has("agt_shadow") ? 0.08f : 0f);
            float tech = Mathf.Max(0f, total - own - 0.10f);

            Txt(col, Loc.T("Контрразведка дома"), 14, Cyan, Body, 20, In, In, bold: true);
            Txt(col, $"{total * 100f:0}%", 46, Cyan, Body + 30, 56, In, In, bold: true);
            Caption(col, Loc.T("ЗАЩИТА ИМПЕРИИ"), Body + 88, In);
            Txt(col, Loc.F("Агент  <color=#66E0D0><b>+{0:0}%</b></color>\nБазовая  {1:0}%\nТехнологии  +{2:0}%", own * 100f, 10f, tech * 100f),
                13, UIManager.DS.TextPrimary, Body + 34, 70, 170, In).lineSpacing = 1.25f;
            var bh = LGBuild.Rect(col, "Bar");
            bh.TopBand(Body + 112, 10, In, In);
            LGBuild.Bar(bh, Cyan, total / 0.85f, 8f);

            Tile(col, Body + 136, 0, es != null ? es.Foiled.ToString() : "0", Loc.T("сорвано чужих операций"), UIManager.DS.Green);
            Tile(col, Body + 136, 1, es != null ? es.Breaches.ToString() : "0", Loc.T("прорывов к вам"), UIManager.DS.Red);

            Txt(col, Loc.T("Чем выше защита, тем чаще чужие агенты проваливаются и тем чаще вы узнаёте, чья это работа. ") +
                     Loc.T("Агент набирает опыт за службу и за каждую сорванную операцию. ИИ шпионит за вами: крадёт технологии, устраивает диверсии, похищает запасы."),
                12, UIManager.DS.TextMuted, Body + 226, 120, In, In, wrap: true);

            var btn = ButtonRow(col, new BtnDef(Loc.T("СМЕНИТЬ ПОСТ"), LGIcon.Back, UIManager.DS.BtnNeutral, new Color(Cyan.r, Cyan.g, Cyan.b, 0.5f), () =>
            {
                LeaderManager.Instance?.Unassign(a);
                SFXManager.Play(Sfx.UiBack);
                Rebuild();
            }));
            TooltipHelper.Attach(btn[0].gameObject, Loc.T("Снять агента с контрразведки и выбрать ему новый пост. Защита империи сразу снизится."));
        }

        private static void Tile(RectTransform col, float top, int slot, string value, string label, Color c)
        {
            float w = (ColW - 2 * In - 10f) / 2f;
            var t = LGBuild.Panel(col, "Tile", RowTint);
            t.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(In + slot * (w + 10f), -top), new Vector2(w, 74));
            LG.Platter(t.gameObject, 12f).SetRim(new Color(1f, 1f, 1f, 0.08f));
            Txt(t.rectTransform, value, 24, c, 10, 30, 14, 10, bold: true);
            Txt(t.rectTransform, label, 11, Muted, 44, 16, 14, 10);
        }

        // ---- Без назначения: выбор поста ----

        private void Assignment(RectTransform col, Leader a)
        {
            var lm = LeaderManager.Instance;
            Caption(col, Loc.T("КУДА НАПРАВИТЬ АГЕНТА"), Body, In, Orange);
            Txt(col, Loc.T("Контрразведка защищает вашу империю. Внедрение — агент строит разведсеть в чужой империи; систему вы выберете на карте."),
                12, UIManager.DS.TextMuted, Body + 20, 52, In, In, wrap: true);
            var host = LGBuild.Rect(col, "Posts");
            host.Stretch(In - 6, In - 6, In - 6, Body + 78);
            var list = LGBuild.ScrollList(host, 8f, 6);

            AssignRow(list, a, 0, Loc.T("Контрразведка дома"),
                Loc.F("Защита вашей империи +{0:0}%", (0.10f + a.Level * 0.04f + (a.Has("agt_shadow") ? 0.08f : 0f)) * 100f),
                lm?.SpyOn(0, 0));
            int shown = 0;
            foreach (var ai in AIEmpireManager.All)
            {
                if (ai.IsEliminated || !Contacts.PlayerMet(ai.OwnerId)) continue;
                shown++;
                AssignRow(list, a, ai.OwnerId, Contacts.NameForPlayer(ai.OwnerId),
                    Loc.F("отношение {0:+0;-0;0}  ·  защита {1:0}%  ·  сеть {2:0}%", ai.Opinion, EspionageManager.TargetCounter(ai) * 100f, EspionageManager.NetworkStrength(ai.OwnerId)) +
                    (ai.AtWar ? Loc.T("  ·  <color=#FF7A7A>война</color>") : ""),
                    lm?.SpyOn(0, ai.OwnerId));
            }
            if (shown == 0)
            {
                var e = LGBuild.Rect(list, "None");
                LGBuild.Height(e.gameObject, 60);
                var t = LGBuild.Label(e, Loc.T("Вы пока не встретили ни одной империи — внедряться некуда. Исследуйте галактику."), 12, UIManager.DS.TextMuted, TextAnchor.MiddleLeft, wrap: true);
                t.rectTransform.Stretch(10, 0, 10, 0);
            }
        }

        private void AssignRow(RectTransform list, Leader a, int target, string title, string sub, Leader current)
        {
            var row = LGBuild.Panel(list, "Row", RowTint);
            LGBuild.Height(row.gameObject, 72);
            Color c = target == 0 ? Cyan : FleetIndicator.OwnerColor(target);
            LG.Platter(row.gameObject, 12f).SetRim(new Color(c.r, c.g, c.b, 0.25f));
            var rt = row.rectTransform;

            var mark = LGBuild.Rect(rt, "Mark");
            mark.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(34, 34));
            var emblem = target > 0 ? FactionEmblems.ForOwner(target) : null;
            if (emblem != null) FactionEmblems.Create(mark, emblem, 34, c);
            else LGIcons.Create(mark, target == 0 ? LGIcon.ShieldDome : LGIcon.Betrayal, 26, c);

            Txt(rt, title, 14, c, 10, 20, 54, 128, bold: true);
            Txt(rt, sub, 10, UIManager.DS.TextMuted, 32, 30, 54, 128, wrap: true);
            if (current != null && current != a)
                Txt(rt, Loc.F("сейчас: {0}", current.Name), 10, UIManager.DS.Gold, 10, 14, 54, 128, TextAnchor.UpperRight);

            var btn = LGBuild.Button(rt, "Go", UIManager.DS.BtnPrimary, new Color(Purple.r, Purple.g, Purple.b, 0.7f), () =>
            {
                if (target > 0) { SpyNetworkView.BeginPick(a, target); return; }
                LeaderManager.Instance?.AssignSpy(a, target);
                SFXManager.Play(Sfx.UiConfirm);
                Rebuild();
            }, target > 0 ? LGIcon.Target : (LGIcon?)null, target > 0 ? Loc.T("СИСТЕМА") : Loc.T("НАЗНАЧИТЬ"), 10);
            ((RectTransform)btn.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(108, 34));
            if (target > 0) TooltipHelper.Attach(btn.gameObject, Loc.T("Выбрать на карте систему, откуда агент будет строить сеть"));
        }

        // ================================================================ Кандидат и приветствие

        private void CandidateColumn(int index)
        {
            var lm = LeaderManager.Instance;
            Leader cand = null;
            if (lm != null) foreach (var c in lm.Candidates) if (c.Class == LeaderClass.Agent) { cand = c; break; }

            var col = NewColumn(index, Purple, 0.22f, false);
            if (cand == null || lm == null)
            {
                var ic = LGIcons.Create(col, LGIcon.Leader, 54, new Color(Purple.r, Purple.g, Purple.b, 0.4f));
                ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 50), new Vector2(54, 54));
                var t = LGBuild.Label(col, Loc.T("Кандидатов в агенты нет\n<size=12><color=#8AA2A8>Список кандидатов обновляется каждый год.</color></size>"), 15, UIManager.DS.TextPrimary, TextAnchor.UpperCenter, bold: true, wrap: true);
                t.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0, 6), new Vector2(ColW - 2 * In, 80));
                return;
            }

            var banner = Banner(col, cand, true, AgentTooltip(cand));
            BannerTitle(banner, cand.Name, Loc.F("{0} лет", cand.Age), Muted, Loc.T("КАНДИДАТ НА НАЙМ"), Purple);

            float y = Body;
            int traits = 0;
            foreach (var id in cand.Traits)
            {
                var tr = LeaderManager.Trait(id);
                if (tr == null) continue;
                traits++;
                Txt(col, tr.Name, 14, UIManager.DS.Gold, y, 20, In, In, bold: true);
                Txt(col, tr.Desc, 12, UIManager.DS.TextMuted, y + 20, 34, In, In, wrap: true);
                y += 58;
            }
            if (traits == 0) { Txt(col, Loc.T("без особых черт"), 13, Muted, y, 20, In, In); y += 30; }

            Txt(col, Loc.F("<color=#F2C766>★{0:0}</color> найм   ·   {1:0.#} гелия-3 в месяц   ·   лидеров {2}/{3}",
                LeaderManager.HireInfluence, LeaderManager.UpkeepEnergy, lm.CountFor(0), LeaderManager.MaxLeaders),
                12, UIManager.DS.TextPrimary, y + 6, 18, In, In);

            // Куда ещё некого послать — подсказка
            var free = new StringBuilder();
            foreach (var ai in AIEmpireManager.All)
            {
                if (ai.IsEliminated || !Contacts.PlayerMet(ai.OwnerId) || lm.SpyOn(0, ai.OwnerId) != null) continue;
                if (free.Length > 0) free.Append(", ");
                free.Append($"<color={LGBuild.Hex(FleetIndicator.OwnerColor(ai.OwnerId))}>{Contacts.NameForPlayer(ai.OwnerId)}</color>");
            }
            string hint = (free.Length > 0 ? Loc.F("Без вашего агента: {0}.", free.ToString()) : "") +
                          (lm.SpyOn(0, 0) == null ? (free.Length > 0 ? " " : "") + Loc.T("Контрразведка дома не занята.") : "");
            if (hint.Length > 0) Txt(col, hint, 12, UIManager.DS.TextMuted, y + 34, 54, In, In, wrap: true);

            bool can = lm.CanHire(out string why);
            var hire = LGBuild.Button(col, "Hire", can ? UIManager.DS.BtnPrimary : UIManager.DS.BtnNeutral,
                new Color(Purple.r, Purple.g, Purple.b, 0.8f), () =>
                {
                    if (lm.Hire(cand)) { _focusId = cand.Id; _scrollToFocus = true; }
                    Rebuild();
                }, LGIcon.Leader, Loc.F("НАНЯТЬ АГЕНТА  ★{0:0}", LeaderManager.HireInfluence), 13);
            ((RectTransform)hire.transform).At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(In, In), new Vector2(ColW - 2 * In, 48));
            hire.interactable = can;
            TooltipHelper.Attach(hire.gameObject, can
                ? Loc.F("Найм {0:0} влияния, содержание {1:0.#} гелия-3 в месяц. Лидеров: {2}/{3}", LeaderManager.HireInfluence, LeaderManager.UpkeepEnergy, lm.CountFor(0), LeaderManager.MaxLeaders)
                : why);
        }

        /// <summary>Без агентов: что даёт разведка и как начать.</summary>
        private static void WelcomeColumn(RectTransform col)
        {
            var banner = Banner(col, null, false, null);
            BannerTitle(banner, Loc.T("Разведка"), Loc.T("Наймите агента — колонка справа"), Muted, Loc.T("АГЕНТОВ ПОКА НЕТ"), Purple);

            Txt(col, Loc.T("Внедрите агента в чужую империю. Он строит разведсеть из выбранной вами системы — сильнее всего там, слабее у соседей по гиперкоридорам. Без агента сеть тает."),
                12, UIManager.DS.TextMuted, Body, 64, In, In, wrap: true);
            Caption(col, Loc.T("ЧТО ОТКРЫВАЕТ СЕТЬ"), Body + 74, In);
            float y = Body + 96;
            foreach (var m in Milestones)
            {
                var node = LGBuild.Panel(col, "Node", new Color(Purple.r * 0.2f, Purple.g * 0.15f, Purple.b * 0.3f, 1f));
                node.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(In, -y), new Vector2(30, 30));
                LG.Platter(node.gameObject, 15f).SetRim(new Color(Purple.r, Purple.g, Purple.b, 0.6f));
                var ic = LGIcons.Create(node.transform, m.Icon, 16, Purple);
                ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16, 16));
                Txt(col, $"<color=#BD8FFF>{m.Cover:0}%</color>  {m.Title}", 13, Color.white, y - 1, 18, In + 42, In, bold: true);
                Txt(col, m.Desc, 11, UIManager.DS.TextMuted, y + 17, 16, In + 42, In);
                y += 52;
            }
        }

        // ================================================================ Низ: журнал и империи

        private RectTransform BottomCard(RectTransform host, string title)
        {
            LGBuild.Clear(host);
            var p = LGBuild.Panel(host, "Card", CardTint);
            p.rectTransform.Stretch();
            LG.Platter(p.gameObject, 16f).SetRim(new Color(1f, 1f, 1f, 0.10f));
            Caption(p.rectTransform, title, 14, In, Purple);
            return p.rectTransform;
        }

        private void BuildLog()
        {
            var card = BottomCard(_logHost, Loc.T("ЖУРНАЛ ОПЕРАЦИЙ"));
            var host = LGBuild.Rect(card, "LogHost");
            host.Stretch(8, 8, 8, 34);
            var list = LGBuild.ScrollList(host, 2f, 8);
            var es = EspionageManager.Instance;
            if (es == null || es.Log.Count == 0)
            {
                var e = LGBuild.Rect(list, "Empty");
                LGBuild.Height(e.gameObject, 40);
                var t = LGBuild.Label(e, Loc.T("Здесь появятся результаты ваших операций и попытки чужих агентов проникнуть к вам."), 12, UIManager.DS.TextMuted, TextAnchor.MiddleLeft, wrap: true);
                t.rectTransform.Stretch(4, 0, 4, 0);
                return;
            }
            const float charsPerLine = 140f;
            foreach (var e in es.Log)
            {
                Color c = e.Kind == 1 ? UIManager.DS.Green : e.Kind == 2 ? UIManager.DS.Red : e.Kind == 3 ? UIManager.DS.Gold : UIManager.DS.TextMuted;
                var row = LGBuild.Rect(list, "Entry");
                int lines = Mathf.Max(1, Mathf.CeilToInt(e.Text.Length / charsPerLine));
                LGBuild.Height(row.gameObject, 8f + lines * 17f);
                var d = LGBuild.Label(row, $"<color={LGBuild.Hex(c)}>●</color>  {e.Date}", 12, UIManager.DS.TextMuted, TextAnchor.UpperLeft);
                d.rectTransform.Stretch(4, 0, 0, 4);
                var t = LGBuild.Label(row, e.Text, 12, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
                t.rectTransform.Stretch(120, 0, 4, 4);
            }
        }

        private void BuildEmpires()
        {
            var card = BottomCard(_empHost, Loc.T("РАЗВЕДСЕТЬ ПО ИМПЕРИЯМ"));
            var host = LGBuild.Rect(card, "Rows");
            host.Stretch(8, 8, 8, 34);
            var list = LGBuild.ScrollList(host, 4f, 6);
            var lm = LeaderManager.Instance;
            int n = 0;
            foreach (var ai in AIEmpireManager.All)
            {
                if (ai.IsEliminated || !Contacts.PlayerMet(ai.OwnerId)) continue;
                n++;
                Color c = FleetIndicator.OwnerColor(ai.OwnerId);
                float net = EspionageManager.NetworkStrength(ai.OwnerId);
                var agent = lm?.SpyOn(0, ai.OwnerId);

                var row = LGBuild.Panel(list, "Emp", new Color(0, 0, 0, 0), raycast: true);
                LGBuild.Height(row.gameObject, 28);
                var rt = row.rectTransform;
                var mark = LGBuild.Rect(rt, "Mark");
                mark.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(24, 24));
                var emblem = FactionEmblems.ForOwner(ai.OwnerId);
                if (emblem != null) FactionEmblems.Create(mark, emblem, 24, c);
                else LGIcons.Create(mark, LGIcon.Betrayal, 18, c);

                var name = LGBuild.Label(rt, Contacts.NameForPlayer(ai.OwnerId), 13, Color.white, TextAnchor.MiddleLeft, bold: true);
                name.rectTransform.Stretch(40, 0, 390, 0);
                var bh = LGBuild.Rect(rt, "Bar");
                bh.Stretch(240, 0, 200, 0);
                LGBuild.Bar(bh, Purple, Mathf.Clamp01(net / TrackMax), 5f);
                var pct = LGBuild.Label(rt, $"{net:0}%", 13, net > 0.5f ? Purple : Muted, TextAnchor.MiddleRight, bold: true);
                pct.rectTransform.Stretch(0, 0, 146, 0);
                var who = LGBuild.Label(rt, agent != null ? agent.Name : Loc.T("нет агента"), 11, agent != null ? UIManager.DS.TextPrimary : Muted, TextAnchor.MiddleRight);
                who.rectTransform.Stretch(0, 0, 6, 0);
                who.rectTransform.offsetMin = new Vector2(500, 0);

                TooltipHelper.Attach(row.gameObject,
                    Loc.F("<b>{0}</b>\nСеть {1:0}%   ·   защита {2:0}%   ·   отношение {3:+0;-0;0}", Contacts.NameForPlayer(ai.OwnerId), net, EspionageManager.TargetCounter(ai) * 100f, ai.Opinion) +
                    (ai.AtWar ? Loc.T("   ·   <color=#FF7A7A>война</color>") : ai.HasPact ? Loc.T("   ·   <color=#66F59E>пакт</color>") : ""));
            }
            if (n == 0)
            {
                var e = LGBuild.Rect(list, "None");
                LGBuild.Height(e.gameObject, 40);
                var t = LGBuild.Label(e, Loc.T("Вы пока не встретили ни одной империи."), 12, UIManager.DS.TextMuted, TextAnchor.MiddleLeft, wrap: true);
                t.rectTransform.Stretch(4, 0, 4, 0);
            }
        }

        // ================================================================ Мелочи

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
            if (l.Level < LeaderManager.MaxLevel) sb.Append(Loc.F("\n<color=#8AA2A8>До следующего уровня:</color> {0:0} опыта", l.XpToNext));
            foreach (var id in l.Traits)
            {
                var t = LeaderManager.Trait(id);
                if (t != null) sb.Append($"\n<color=#FFCC52>{t.Name}</color> — {t.Desc}");
            }
            return sb.ToString();
        }

        private static Text Caption(RectTransform parent, string text, float top, float left, Color? col = null, float right = 16f)
        {
            var t = LGBuild.Label(parent, text, 11, col ?? Muted, TextAnchor.UpperLeft, bold: true).Track();
            t.rectTransform.TopBand(top, 16, left, right);
            return t;
        }

        private static Text Txt(RectTransform parent, string text, int size, Color col, float top, float height, float left, float right,
                                TextAnchor anchor = TextAnchor.UpperLeft, bool bold = false, bool wrap = false)
        {
            var t = LGBuild.Label(parent, text, size, col, anchor, bold, wrap);
            t.rectTransform.TopBand(top, height, left, right);
            return t;
        }
    }
}
