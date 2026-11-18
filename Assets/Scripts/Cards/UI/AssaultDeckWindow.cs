using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Rendering;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Cards
{
    /// <summary>
    /// «Сбор десанта» — перед штурмом игрок сам выбирает, какими картами резерва нападать. Сверху — сколько мест на
    /// кораблях у цели занято и что известно об обороне; ниже — «В десанте» (клик — убрать) и резерв по типам
    /// (клик — взять; что не помещается — затемнено). Сразу предложены лучшие карты, «ЛУЧШИЕ» возвращают этот выбор.
    /// «В БОЙ» — экран боя, «АВТОБОЙ» — тот же десант без экрана.
    /// </summary>
    public sealed class AssaultDeckWindow : MonoBehaviour
    {
        public static AssaultDeckWindow Instance { get; private set; }
        public bool IsOpen => _root != null && LG.IsVisible(_root);
        public static bool IsOpenNow => Instance != null && Instance.IsOpen;

        private static readonly Vector2 Card = new Vector2(116, 162);
        private static readonly Vector2 BigCard = new Vector2(280, 392);
        private const float Gap = 12f;

        private GameObject _root;
        private RectTransform _rt, _list, _previewRoot, _placesFill;
        private Text _subtitle, _places, _orders, _deck, _intel, _warn;
        private ScrollRect _scroll;
        private CardView _preview;
        private int _system = -1;
        private readonly List<long> _sel = new List<long>();
        private float _warnUntil;
        private bool _dirty;
        // Прогноз: автобои с выбранным десантом идут понемногу каждый кадр, чтобы окно не подвисало
        private const int ForecastRuns = 40, ForecastPerFrame = 4;
        private Text _forecast;
        private RectTransform _forecastFill;
        private string _fcSig;
        private int _fcDone, _fcWins;
        private float _fcStart;

        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Gold => UIManager.DS.Gold;
        private static Color Cyan => UIManager.DS.NeonCyan;
        private static readonly Color CPanel = new Color(0.020f, 0.045f, 0.065f, 0.90f);
        private static readonly Color Accent = new Color(0.95f, 0.45f, 0.35f);

        /// <summary>Открыть выбор десанта для штурма системы (кнопка «ШТУРМ»).</summary>
        public static void Open(StarSystem sys)
        {
            if (sys == null || ArmyManager.Instance == null) return;
            if (Instance == null) new GameObject("[UI] AssaultDeckWindow").AddComponent<AssaultDeckWindow>();
            Instance.Show(sys);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            ArmyManager.OnChanged += MarkDirty;
        }

        private void OnDestroy()
        {
            ArmyManager.OnChanged -= MarkDirty;
            if (Instance == this) Instance = null;
        }

        private void MarkDirty() => _dirty = true;

        private void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { UIEsc.Consume(); Close(); return; }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { Launch(false); return; }
            if (_preview != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, Input.mousePosition, null, out var lp);
                var half = _rt.rect.size * 0.5f;
                float x = lp.x + (lp.x > 0 ? -BigCard.x * 0.5f - 46f : BigCard.x * 0.5f + 46f);
                float y = Mathf.Clamp(lp.y, -half.y + BigCard.y * 0.5f + 10f, half.y - BigCard.y * 0.5f - 10f);
                _previewRoot.anchoredPosition = new Vector2(x, y);
            }
            if (_warn != null && _warn.text.Length > 0 && Time.unscaledTime > _warnUntil) _warn.text = "";
            if (_dirty) { _dirty = false; Rebuild(); }
            UpdateForecast();
        }

        private void Show(StarSystem sys)
        {
            if (_root == null) Build();
            if (_root == null) return;
            _system = sys.Id;
            _sel.Clear();
            foreach (var c in ArmyManager.Instance.AttackDeck(0, sys.Id)) _sel.Add(c.Id);
            _dirty = true;
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
            UIManager.Instance?.ShowModalDimPublic();
            SFXManager.Play(Sfx.WindowOpen);
        }

        public void Close()
        {
            if (_root == null) return;
            HidePreview();
            LG.Hide(_root);
            UIManager.Instance?.HideModalDimPublic();
            TooltipSystem.Hide();
            SFXManager.Play(Sfx.WindowClose);
        }

        // ================================================================ Построение

        private void Build()
        {
            var host = GameObject.Find("ModalCanvas");
            if (host == null) return;
            _rt = LGBuild.Rect(host.transform, "AssaultDeckWindow");
            _rt.Stretch(28, 28, 28, 28);
            _root = _rt.gameObject;
            _root.AddComponent<CanvasGroup>();
            var bg = _root.AddComponent<Image>();
            bg.color = new Color(0.010f, 0.025f, 0.038f, 0.97f);
            bg.raycastTarget = true;
            LG.Glass(_root).SetRim(new Color(0.95f, 0.55f, 0.45f, 0.42f));
            LG.Motion(_root, LGAppear.Kind.Pop).fromScale = 0.97f;

            var accent = LGBuild.Panel(_rt, "Accent", Accent);
            accent.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -24), new Vector2(5, 30));
            var title = LGBuild.Label(_rt, Loc.T("СБОР ДЕСАНТА"), 26, Color.white, TextAnchor.UpperLeft, bold: true);
            title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(42, -18), new Vector2(700, 36));
            title.Track(1.6f);
            _subtitle = LGBuild.Label(_rt, "", 12, Muted, TextAnchor.UpperLeft);
            _subtitle.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(43, -54), new Vector2(1400, 18));
            _subtitle.supportRichText = true;
            var close = LGBuild.Button(_rt, "Close", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.25f), Close, LGIcon.Close, null, 12);
            ((RectTransform)close.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -18), new Vector2(38, 38));
            TooltipHelper.Attach(close.gameObject, Loc.T("Отменить штурм  <color=#8AA2A8>(Esc)</color>"));

            // Полоса состояния: места, приказы, колода, разведка
            var bar = LGBuild.Panel(_rt, "Status", CPanel);
            bar.rectTransform.anchorMin = new Vector2(0, 1); bar.rectTransform.anchorMax = new Vector2(1, 1); bar.rectTransform.pivot = new Vector2(0.5f, 1);
            bar.rectTransform.offsetMin = new Vector2(20, -152); bar.rectTransform.offsetMax = new Vector2(-20, -84);
            LG.Platter(bar.gameObject, 14f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.18f));
            var b = bar.rectTransform;

            var pi = LGIcons.Create(b, LGIcon.Fleet, 18, Cyan);
            pi.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 8), new Vector2(18, 18));
            _places = LGBuild.Label(b, "", 15, Color.white, TextAnchor.MiddleLeft, bold: true);
            _places.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(46, 8), new Vector2(240, 24));
            var track = LGBuild.Rect(b, "Places");
            track.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(46, -16), new Vector2(220, 8));
            _placesFill = LGBuild.Bar(track, Cyan, 0f, 6f);
            TooltipHelper.Attach(_places.gameObject, Loc.T("<b>Десантные места</b>\nСколько карт увезут военные корабли на орбите: корвет 2 места, фрегат 4, эсминец 6. Техника занимает больше места, чем пехота"));
            _places.raycastTarget = true;

            _orders = LGBuild.Label(b, "", 14, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft, bold: true);
            _orders.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(300, 8), new Vector2(200, 24));
            _orders.supportRichText = true;
            _deck = LGBuild.Label(b, "", 12, Muted, TextAnchor.MiddleLeft);
            _deck.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(300, -14), new Vector2(320, 20));
            _deck.supportRichText = true;

            var ii = LGIcons.Create(b, LGIcon.Sensors, 18, Accent);
            ii.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(660, 0), new Vector2(18, 18));
            _intel = LGBuild.Label(b, "", 12, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft, wrap: true);
            _intel.rectTransform.anchorMin = new Vector2(0, 0); _intel.rectTransform.anchorMax = new Vector2(1, 1);
            _intel.rectTransform.offsetMin = new Vector2(684, 6); _intel.rectTransform.offsetMax = new Vector2(-16, -6);
            _intel.supportRichText = true;
            _intel.lineSpacing = 1.15f;

            // Список карт
            var panel = LGBuild.Panel(_rt, "Content", CPanel);
            panel.rectTransform.anchorMin = Vector2.zero; panel.rectTransform.anchorMax = Vector2.one;
            panel.rectTransform.offsetMin = new Vector2(20f, 92f); panel.rectTransform.offsetMax = new Vector2(-20f, -162f);
            LG.Platter(panel.gameObject, 16f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.18f));
            var listHost = LGBuild.Rect(panel.rectTransform, "ListHost");
            listHost.Stretch(8, 8, 8, 8);
            _list = LGBuild.ScrollList(listHost, 8f, 8);
            _scroll = listHost.GetComponentInChildren<ScrollRect>();

            // Нижняя панель: подбор слева, решение справа
            var foot = LGBuild.Rect(_rt, "Footer");
            foot.anchorMin = new Vector2(0, 0); foot.anchorMax = new Vector2(1, 0); foot.pivot = new Vector2(0.5f, 0);
            foot.offsetMin = new Vector2(20, 20); foot.offsetMax = new Vector2(-20, 76);
            FootButton(foot, Loc.T("ЛУЧШИЕ"), LGIcon.Star, UIManager.DS.BtnNeutral, true, 0f, 150f, AutoPick,
                Loc.T("Взять сильнейшие карты резерва, сколько поместится"));
            FootButton(foot, Loc.T("ОЧИСТИТЬ"), null, UIManager.DS.BtnNeutral, true, 160f, 140f, () => { _sel.Clear(); SFXManager.Play(Sfx.DealRemove, 0.7f); _dirty = true; },
                Loc.T("Убрать все карты из десанта"));
            // Прогноз исхода: доля побед в автобоях с этим десантом против известной обороны
            var fc = LGBuild.Rect(foot, "Forecast");
            fc.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(316, 0), new Vector2(300, 52));
            var fi = LGIcons.Create(fc, LGIcon.Target, 18, Accent);
            fi.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 6), new Vector2(18, 18));
            _forecast = LGBuild.Label(fc, "", 15, Color.white, TextAnchor.MiddleLeft, bold: true);
            _forecast.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(26, 6), new Vector2(274, 24));
            _forecast.supportRichText = true;
            _forecast.raycastTarget = true;
            var ft = LGBuild.Rect(fc, "Bar");
            ft.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(26, -16), new Vector2(240, 8));
            _forecastFill = LGBuild.Bar(ft, Gold, 0f, 6f);
            TooltipHelper.Attach(_forecast.gameObject, Loc.F("<b>Прогноз штурма</b>\nДоля побед в {0} автобоях с этим десантом против обороны, известной разведке: карты резерва врага, ополчение, щит и турели укреплений. В бою вы решаете сами — исход может быть и лучше, и хуже", ForecastRuns));
            _warn = LGBuild.Label(foot, "", 13, new Color(1f, 0.55f, 0.45f), TextAnchor.MiddleLeft, bold: true);
            _warn.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(640, 0), new Vector2(520, 30));
            // Главное действие окна — яркая кнопка, как во всех меню
            var fight = MenuPrimaryButton.Create(foot, Loc.T("В БОЙ  ›"), 17, () => Launch(false), MenuPrimaryButton.Red);
            ((RectTransform)fight.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(210, 52));
            fight.Label.Track(2f);
            TooltipHelper.Attach(fight.Label.transform.parent.gameObject, Loc.T("<b>Штурм</b>\nКарточный бой за колонию с выбранным десантом  <color=#8AA2A8>(Enter)</color>"));
            FootButton(foot, Loc.T("АВТОБОЙ"), LGIcon.Fast, UIManager.DS.BtnNeutral, false, 220f, 160f, () => Launch(true),
                Loc.T("Тот же десант, но без экрана боя: исход рассчитают правила, решения примет ИИ"));
            FootButton(foot, Loc.T("ОТМЕНА"), null, UIManager.DS.BtnNeutral, false, 390f, 130f, Close, Loc.T("Не штурмовать  <color=#8AA2A8>(Esc)</color>"));

            _previewRoot = LGBuild.Rect(_rt, "Preview");
            _previewRoot.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, BigCard);
            _root.SetActive(false);
        }

        /// <summary>Кнопка нижней панели: left — от левого края, иначе от правого (x — отступ).</summary>
        private static void FootButton(RectTransform parent, string label, LGIcon? icon, Color tint, bool left, float x, float w, System.Action act, string tip)
        {
            var btn = LGBuild.Button(parent, "Btn_" + label, tint, new Color(1f, 1f, 1f, 0.28f), act, icon, label, 13);
            var a = new Vector2(left ? 0 : 1, 0.5f);
            ((RectTransform)btn.transform).At(a, a, new Vector2(left ? x : -x, 0), new Vector2(w, 52));
            var t = btn.GetComponentInChildren<Text>();
            if (t != null) t.Track(2f);
            if (tip != null) TooltipHelper.Attach(btn.gameObject, tip);
        }

        // ================================================================ Выбор

        private struct Load { public int Places, Used, OrderCap, Orders, Cards; }

        private Load Current(ArmyManager am)
        {
            var l = new Load { Places = ArmyManager.LandingPlaces(0, _system), OrderCap = ArmyManager.OrderCap(0, _system) };
            foreach (var id in _sel)
            {
                var c = am.Get(id);
                if (c?.D == null) continue;
                l.Cards++;
                if (c.D.Size <= 0) l.Orders++; else l.Used += c.D.Size;
            }
            return l;
        }

        /// <summary>Почему карту нельзя добавить (null — можно).</summary>
        private string AddBlocker(CardInstance c, Load l)
        {
            if (c.Wounded) return Loc.F("Ранены — ещё {0} дн.", c.WoundDays);
            if (l.Cards >= ArmyManager.MaxDeck) return Loc.F("В колоде не больше {0} карт", ArmyManager.MaxDeck);
            if (c.D.Size <= 0)
                return l.Orders >= l.OrderCap ? Loc.F("Приказов не больше {0}: по два на военный корабль", l.OrderCap) : null;
            return l.Used + c.D.Size > l.Places ? Loc.F("Не хватает мест: нужно {0}, свободно {1}", c.D.Size, Mathf.Max(0, l.Places - l.Used)) : null;
        }

        private void Toggle(CardInstance c)
        {
            if (_sel.Remove(c.Id)) { SFXManager.Play(Sfx.DealRemove, 0.7f); _dirty = true; return; }
            var am = ArmyManager.Instance;
            if (am == null) return;
            string why = AddBlocker(c, Current(am));
            if (why != null) { Warn(why); return; }
            _sel.Add(c.Id);
            SFXManager.Play(Sfx.CardPlace, 0.75f);
            _dirty = true;
        }

        private void AutoPick()
        {
            var am = ArmyManager.Instance;
            if (am == null) return;
            _sel.Clear();
            foreach (var c in am.AttackDeck(0, _system)) _sel.Add(c.Id);
            SFXManager.Play(Sfx.CardDraw, 0.8f);
            _dirty = true;
        }

        private void Warn(string text)
        {
            _warn.text = text;
            _warnUntil = Time.unscaledTime + 3f;
            SFXManager.Play(Sfx.UiDenied);
        }

        private void Launch(bool auto)
        {
            var sys = EmpireStats.GetSystem(_system);
            string blocker = AssaultLink.Blocker(0, sys);
            if (blocker != null) { Warn(blocker); return; }
            var ids = new List<long>(_sel);
            Close();
            if (!AssaultLink.StartPlayerAssault(sys, auto, ids))
                NotificationCenter.Show(Loc.T("Штурм не начат"), AssaultLink.Blocker(0, sys) ?? "", NotificationCenter.Kind.Warning, 4f);
        }

        // ================================================================ Прогноз

        /// <summary>Выбор изменился — прогноз считается заново (с паузой, пока игрок щёлкает картами), по нескольку боёв за кадр.</summary>
        private void UpdateForecast()
        {
            if (_forecast == null) return;
            var sys = EmpireStats.GetSystem(_system);
            if (sys == null) return;
            string sig = _system + ":" + string.Join(",", _sel);
            if (sig != _fcSig)
            {
                _fcSig = sig; _fcDone = 0; _fcWins = 0;
                _fcStart = Time.unscaledTime + 0.25f;
                _forecast.text = Loc.T("<color=#8AA2A8>Прогноз: считаем…</color>");
                LGBuild.SetBar(_forecastFill, 0f);
                return;
            }
            if (_fcDone >= ForecastRuns || Time.unscaledTime < _fcStart) return;
            var ids = new List<long>(_sel);
            for (int i = 0; i < ForecastPerFrame && _fcDone < ForecastRuns; i++, _fcDone++)
                if (AssaultLink.ForecastRun(sys, ids, 7919 * (_fcDone + 1) + _system)) _fcWins++;
            if (_fcDone < ForecastRuns) return;
            float p = _fcWins / (float)ForecastRuns;
            Color c = p >= 0.6f ? UIManager.DS.Green : p >= 0.35f ? Gold : UIManager.DS.Red;
            string verdict = p >= 0.75f ? Loc.T("уверенная победа") : p >= 0.6f ? Loc.T("скорее победа") : p >= 0.35f ? Loc.T("рискованно")
                           : p >= 0.15f ? Loc.T("скорее поражение") : Loc.T("почти без шансов");
            _forecast.text = Loc.F("Прогноз: <color=#{0}>{1:0}%</color> · {2}", ColorUtility.ToHtmlStringRGB(c), p * 100f, verdict);
            LGBuild.SetBar(_forecastFill, p);
            var img = _forecastFill.GetComponent<Image>();
            if (img != null) img.color = c;
        }

        // ================================================================ Наполнение

        private void Rebuild()
        {
            var am = ArmyManager.Instance;
            var sys = EmpireStats.GetSystem(_system);
            if (am == null || sys == null) { Close(); return; }
            HidePreview();

            // Карты, которых больше нет (погибли, ранены), выпадают из выбора
            var pool = am.AttackPool(0);
            var ok = new HashSet<long>();
            foreach (var c in pool) ok.Add(c.Id);
            _sel.RemoveAll(id => !ok.Contains(id));

            var load = Current(am);
            int crew = Mathf.Max(0, Mathf.Min(ArmyManager.Warships(0, sys.Id), ArmyManager.MinDeck - load.Cards));
            _subtitle.text = Loc.F("Цель: <b>{0}</b> · {1}. Выберите, какими картами резерва нападать: клик по карте — взять или убрать",
                sys.Name, AssaultLink.NameOf(sys.OwnerId));
            _places.text = Loc.F("Места на кораблях: {0} / {1}", load.Used, load.Places);
            _placesFill.anchorMax = new Vector2(load.Places > 0 ? Mathf.Clamp01((float)load.Used / load.Places) : 0f, 1f);
            _orders.text = Loc.F("Приказы: {0} / {1}", load.Orders, load.OrderCap);
            _deck.text = crew > 0
                ? Loc.F("В колоде {0} карт + экипажи {1} + генерал", load.Cards, crew)
                : Loc.F("В колоде {0} карт + генерал", load.Cards);

            var defense = am.DefenseCards(sys.OwnerId, sys.Id);
            int turrets = Fortifications.GroundTurrets(sys.Fortification);
            _intel.text = Loc.F("<b>Разведка</b>  ·  в обороне карт резерва: <b>{0}</b>  ·  ополчение: <b>{1}</b>  ·  укрепления базы: <b>{2}</b>{3}",
                defense.Count, AssaultLink.MilitiaFor(sys), sys.Fortification,
                sys.Fortification >= 1 ? Loc.F("  ·  щит Штаба и турелей: {0}", turrets) : "");

            float sp = _scroll != null ? _scroll.verticalNormalizedPosition : 1f;
            Canvas.ForceUpdateCanvases();
            LGBuild.Clear(_list);

            // В десанте — в порядке выбора
            var chosen = new List<CardInstance>();
            foreach (var id in _sel) { var c = am.Get(id); if (c != null) chosen.Add(c); }
            Header(Loc.F("В ДЕСАНТЕ · {0}", chosen.Count), LGIcon.Fleet, Accent);
            if (chosen.Count == 0) Note(Loc.T("Десант пуст — в бой пойдут только экипажи кораблей. Возьмите карты из резерва ниже"));
            else Grid(chosen, c => null, true);

            // Резерв по типам
            var groups = new SortedDictionary<int, List<CardInstance>>();
            int left = 0;
            foreach (var c in pool)
            {
                if (_sel.Contains(c.Id)) continue;
                int k = (int)c.D.Site;
                if (!groups.TryGetValue(k, out var l)) groups[k] = l = new List<CardInstance>();
                l.Add(c);
                left++;
            }
            if (left == 0) Header(Loc.T("РЕЗЕРВ · ВСЕ ГОТОВЫЕ КАРТЫ УЖЕ В ДЕСАНТЕ"), LGIcon.Planet, Gold);
            foreach (var kv in groups)
            {
                Header(SiteHeader((ProdSite)kv.Key) + "  ·  " + kv.Value.Count, SiteIcon((ProdSite)kv.Key), Gold);
                Grid(kv.Value, c => AddBlocker(c, load), false);
            }

            // Раненые — видно, но взять нельзя
            var hurt = new List<CardInstance>();
            foreach (var c in am.Of(0)) if (c.Wounded && !c.DefenseOnly && c.D != null) hurt.Add(c);
            if (hurt.Count > 0)
            {
                Header(Loc.F("РАНЕНЫ · {0} · ПРОПУСКАЮТ БОЙ", hurt.Count), LGIcon.Warning, new Color(1f, 0.5f, 0.45f));
                Grid(hurt, c => Loc.F("Ранены — ещё {0} дн.", c.WoundDays), false);
            }
            if (pool.Count == 0 && hurt.Count == 0)
                Note(Loc.T("Резерв армии пуст — задайте состав армии в «Арсенале» (J), империя произведёт карты сама"));

            Canvas.ForceUpdateCanvases();
            if (_scroll != null) _scroll.verticalNormalizedPosition = sp;
        }

        private static string SiteHeader(ProdSite s)
        {
            switch (s)
            {
                case ProdSite.City: return Loc.T("РЕЗЕРВ · ПЕХОТА");
                case ProdSite.Industry: return Loc.T("РЕЗЕРВ · ТЕХНИКА");
                case ProdSite.Command: return Loc.T("РЕЗЕРВ · ПРИКАЗЫ");
                default: return Loc.T("РЕЗЕРВ");
            }
        }

        private static LGIcon SiteIcon(ProdSite s) => s == ProdSite.City ? LGIcon.Population : s == ProdSite.Industry ? LGIcon.Industry : LGIcon.Target;

        private void Header(string text, LGIcon icon, Color col)
        {
            var row = LGBuild.Rect(_list, "H");
            LGBuild.Height(row.gameObject, 30);
            LGBuild.SectionDecor(row, text, icon, col);
        }

        private void Note(string text)
        {
            var row = LGBuild.Rect(_list, "Note");
            LGBuild.Height(row.gameObject, 40);
            var t = LGBuild.Label(row, text, 13, Muted, TextAnchor.MiddleLeft, wrap: true);
            t.rectTransform.Stretch(10, 0, 10, 0);
        }

        /// <summary>Сетка карт. blocker(c) != null — карта затемнена, клик объясняет причину.</summary>
        private void Grid(List<CardInstance> cards, System.Func<CardInstance, string> blocker, bool chosen)
        {
            float width = _list.rect.width - 16f;
            if (width < 300f) width = 1600f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((width + Gap) / (Card.x + Gap)));
            for (int i = 0; i < cards.Count; i += perRow)
            {
                var row = LGBuild.Rect(_list, "Row");
                LGBuild.Height(row.gameObject, Card.y + 14);
                for (int k = 0; k < perRow && i + k < cards.Count; k++)
                {
                    var c = cards[i + k];
                    var cv = CardView.Create(row, new BCard(c.D, c.Id, c.Stars), 0, FactionTraits.Key(0), Card, true);
                    cv.Rt.anchorMin = cv.Rt.anchorMax = new Vector2(0, 1);
                    cv.Rt.anchoredPosition = new Vector2(8 + Card.x * 0.5f + k * (Card.x + Gap), -Card.y * 0.5f - 6);
                    cv.HoverLift = 8f;
                    cv.HoverScale = 0.06f;
                    HookPreview(cv);
                    string why = blocker(c);
                    if (c.Wounded) cv.SetStatus(Loc.F("РАНЕН {0} дн.", c.WoundDays), new Color(1f, 0.55f, 0.45f));
                    else if (c.D.Size > 1) cv.SetStatus(Loc.F("МЕСТ {0}", c.D.Size), Cyan);
                    if (why != null) cv.SetAlpha(0.42f);
                    cv.OnClick = _ => { if (why != null && !chosen) Warn(why); else Toggle(c); };
                }
            }
        }

        // ---------------------------------------------------------------- предпросмотр

        private void HookPreview(CardView cv)
        {
            cv.SetHoverable(true);
            cv.OnHoverIn = ShowPreview;
            cv.OnHoverOut = _ => HidePreview();
        }

        private void ShowPreview(CardView v)
        {
            HidePreview();
            if (v == null || v.Card == null) return;
            _preview = CardView.Create(_previewRoot, v.Card, 0, FactionTraits.Key(0), BigCard, false);
            _preview.Rt.anchoredPosition = Vector2.zero;
            _preview.Punch(0.04f);
            foreach (var g in _preview.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
            _previewRoot.SetAsLastSibling();
        }

        private void HidePreview()
        {
            if (_preview != null) Destroy(_preview.gameObject);
            _preview = null;
        }
    }
}
