using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Cam;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;
using StellarisClone.Generation;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Ряд значков-алертов под верхней панелью (как в Stellaris):
    /// «свободный слот науки», «научный корабль простаивает», «дефицит энергии» и т.д.
    ///   • появляются "выпрыгивая" с пружинным перелётом и волной-кольцом;
    ///   • пока свежие — пульсируют неоном;
    ///   • при наведении — подсказка, по клику — переход к нужному действию;
    ///   • правый клик — скрыть: алерт не вернётся, пока проблема не исчезнет (и тогда сможет появиться снова);
    ///   • когда проблема решена — сжимаются и тают, соседи плавно съезжаются.
    /// </summary>
    public class AlertBar : MonoBehaviour
    {
        public static AlertBar Instance { get; private set; }

        private const float Size = 40f;
        private const float Gap = 8f;
        private const float EvalInterval = 0.5f;

        /// <summary>Оповещение: что случилось и что сделать (Action — переход к делу).</summary>
        public struct AlertInfo
        {
            public string Id;
            public LGIcon Icon;
            public Color Color;
            public string Title;
            public string Body;
            public string ActionHint;
            public int Count;
            public System.Action Action;
        }

        private sealed class AlertView
        {
            public string Id;
            public RectTransform Rt;
            public CanvasGroup Group;
            public Image Bg, Icon;
            public Image Rim, Glow, RippleImg, IconGlow;
            public AlertHoverFlag HoverFlag;
            public float Hover;
            public RectTransform Ripple;
            public Text Count;
            public TooltipTrigger Tip;
            public System.Action Action;
            public Color Color;
            public float Age;
            public float X, XVel;
            public bool Dying;
            public float DieT;
        }

        private RectTransform _root;
        private readonly List<AlertView> _views = new List<AlertView>();
        private readonly List<AlertInfo> _current = new List<AlertInfo>();
        private readonly HashSet<string> _dismissed = new HashSet<string>();
        private readonly List<AlertInfo> _all = new List<AlertInfo>();

        /// <summary>Все текущие оповещения, включая скрытые правым кликом (для «Требует внимания» в обзоре империи).</summary>
        public IReadOnlyList<AlertInfo> All => _all;
        private float _evalTimer;
        private GalaxyGenerator _gen;

        public void Build(Transform hud)
        {
            Instance = this;
            _root = LGBuild.Rect(hud, "[UI] AlertBar");
            // Под правым крылом ресурсов (оно кончается на 72 px от верха), правее шкалы скорости (+150 от середины
            // экрана) вправо; над рядом — подпись «Требует внимания»
            _root.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(150f, -96f), new Vector2(10, Size));
            _root.gameObject.SetActive(false);

            // Подпись над рядом — видна, пока есть хотя бы один алерт
            _caption = LGBuild.Label(_root, Loc.T("ТРЕБУЕТ ВНИМАНИЯ"), 9, new Color(0.56f, 0.66f, 0.71f, 1f), TextAnchor.MiddleLeft, bold: true);
            _caption.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(0f, 6f), new Vector2(240f, 12f));
            _caption.Track(2.4f);
            _caption.enabled = false;
        }

        private Text _caption;

        public void SetVisible(bool v)
        {
            if (_root != null) _root.gameObject.SetActive(v);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (_root == null || !_root.gameObject.activeInHierarchy) return;
            float dt = Time.unscaledDeltaTime;

            _evalTimer -= dt;
            if (_evalTimer <= 0f)
            {
                _evalTimer = EvalInterval;
                Evaluate();
                _all.Clear();
                _all.AddRange(_current);
                // Скрытые правым кликом: забываем, когда проблема ушла; пока она есть — не показываем
                _dismissed.RemoveWhere(id => !_current.Exists(a => a.Id == id));
                _current.RemoveAll(a => _dismissed.Contains(a.Id));
                Sync();
                if (_caption != null) _caption.enabled = _current.Count > 0;
            }
            Animate(dt);
        }

        // ================================================================ Синхронизация

        private void Sync()
        {
            // уходящие
            foreach (var v in _views)
            {
                if (v.Dying) continue;
                bool still = false;
                foreach (var a in _current) if (a.Id == v.Id) { still = true; break; }
                if (!still) { v.Dying = true; v.DieT = 0f; v.Group.blocksRaycasts = false; }
            }

            // новые и обновлённые
            foreach (var a in _current)
            {
                AlertView view = null;
                foreach (var v in _views) if (v.Id == a.Id && !v.Dying) { view = v; break; }
                if (view == null)
                {
                    view = CreateView(a);
                    _views.Add(view);
                    PlayAlertSound(a);
                }
                view.Action = a.Action;
                view.Tip.SetText(Loc.F("<b>{0}</b>\n{1}\n\n<color=#8AA2A8>ЛКМ — {2}  ·  ПКМ — скрыть</color>", a.Title, a.Body, a.ActionHint));
                view.Count.text = a.Count > 1 ? a.Count.ToString() : "";
                view.Count.transform.parent.gameObject.SetActive(a.Count > 1);
            }

            // порядок = приоритет
            _views.Sort((x, y) => IndexOf(x.Id).CompareTo(IndexOf(y.Id)));
        }

        /// <summary>
        /// Новое оповещение звучит по смыслу: беда — тревожно, совет — тихим блипом.
        /// Иначе «свободный слот науки» и «система в осаде» звучали бы одинаково.
        /// </summary>
        private static void PlayAlertSound(AlertInfo a)
        {
            if (GameAudio.Suppressed) return;
            var eco = EconomyManager.Instance;
            switch (a.Id)
            {
                case "siege_def":
                    SFXManager.Play(Sfx.RedAlert);
                    AlarmFeedback.Attack();
                    return;
                case "energy":
                    // Банкротство и его угрозу озвучивает GameAudio — в момент смены состояния
                    if (eco != null && (eco.IsBankrupt || eco.BankruptcyLooming)) return;
                    SFXManager.Play(Sfx.AlertUrgent, 0.8f);
                    return;
            }
            if (a.Id.StartsWith("offer_")) { SFXManager.Play(Sfx.DealPlace); return; }
            SFXManager.Play(a.Color == UIManager.DS.Red ? Sfx.AlertUrgent : Sfx.AlertPop);
        }

        /// <summary>Цвет значка: насыщеннее и чуть светлее исходного цвета алерта.</summary>
        private static Color Saturate(Color c)
        {
            Color.RGBToHSV(c, out float h, out float sat, out float val);
            return Color.HSVToRGB(h, Mathf.Clamp01(sat * 0.85f), Mathf.Clamp01(val * 0.80f));   // приглушённо: значки не перебивают HUD
        }

        private int IndexOf(string id)
        {
            for (int i = 0; i < _current.Count; i++) if (_current[i].Id == id) return i;
            return 1000;
        }

        private AlertView CreateView(AlertInfo a)
        {
            var v = new AlertView { Id = a.Id, Color = a.Color };
            var rt = LGBuild.Rect(_root, "Alert_" + a.Id);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(Size, Size);
            v.Rt = rt;
            v.Group = rt.gameObject.AddComponent<CanvasGroup>();
            v.Group.alpha = 0f;

            LG.Ignore(rt.gameObject);

            // Свечение цвета алерта под плиткой (пульсирует, пока алерт свежий)
            v.Glow = LGBuild.Panel(rt, "Glow", new Color(a.Color.r, a.Color.g, a.Color.b, 0.25f));
            v.Glow.sprite = FactionSelectScreen.RadialSprite();
            v.Glow.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Size + 44f, Size + 44f));

            // Волна-кольцо при появлении
            v.RippleImg = LGBuild.Panel(rt, "Ripple", new Color(a.Color.r, a.Color.g, a.Color.b, 0.9f));
            v.RippleImg.sprite = HudArt.Ring(0.92f, 1f);
            v.RippleImg.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Size, Size));
            v.Ripple = v.RippleImg.rectTransform;

            // Вариант «Прибор»: без плитки — светящийся значок цвета алерта и цветная черта под ним.
            // Bg — невидимая зона клика на всю ячейку.
            v.Bg = LGBuild.Panel(rt, "Hit", new Color(0f, 0f, 0f, 0f), raycast: true);
            v.Bg.rectTransform.Stretch();
            var iconGlow = LGBuild.Panel(v.Bg.transform, "IconGlow", new Color(a.Color.r, a.Color.g, a.Color.b, 0.16f));
            iconGlow.sprite = FactionSelectScreen.RadialSprite();
            iconGlow.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 4f), new Vector2(Size + 4f, Size + 4f));
            var btn = v.Bg.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => { v.Action?.Invoke(); SFXManager.Play("ui_click", 1f, 1.05f); });
            v.Bg.gameObject.AddComponent<AlertRightClick>().OnRight = () => Dismiss(v.Id);
            v.Tip = v.Bg.gameObject.AddComponent<TooltipTrigger>();
            v.HoverFlag = v.Bg.gameObject.AddComponent<AlertHoverFlag>();
            v.IconGlow = iconGlow;

            v.Icon = LGIcons.Create(v.Bg.transform, a.Icon, 26f, Saturate(a.Color));
            var ish = v.Icon.gameObject.AddComponent<Shadow>();
            ish.effectColor = new Color(0f, 0f, 0f, 0.75f);
            ish.effectDistance = new Vector2(0f, -1.5f);
            v.Icon.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 4f), new Vector2(26f, 26f));

            // Черта под значком (она же «рамка» для пульса свежего алерта) и её свечение
            var barGlow = LGBuild.Panel(v.Bg.transform, "BarGlow", new Color(a.Color.r, a.Color.g, a.Color.b, 0.25f));
            barGlow.sprite = FactionSelectScreen.RadialSprite();
            barGlow.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 3f), new Vector2(44f, 14f));
            v.Rim = LGBuild.Panel(v.Bg.transform, "Bar", new Color(a.Color.r, a.Color.g, a.Color.b, 0.9f));
            v.Rim.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 3f), new Vector2(26f, 3f));

            // Счётчик (например, число простаивающих кораблей) — кружок в правом верхнем углу
            // кружок цвета алерта, чуть за углом плитки (как в референсе)
            var badge = LGBuild.Panel(rt, "Badge", Color.Lerp(a.Color, Color.white, 0.15f));
            badge.sprite = HudArt.CutPanel(4);
            badge.type = Image.Type.Sliced;
            badge.rectTransform.At(new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-4, -2), new Vector2(18, 14));
            v.Count = LGBuild.Label(badge.transform, "", 10, new Color(0.01f, 0.05f, 0.07f), TextAnchor.MiddleCenter, bold: true);
            badge.gameObject.SetActive(false);

            // стартовая позиция — там, где появится
            int n = 0;
            foreach (var w in _views) if (!w.Dying) n++;
            v.X = TargetX(n, n + 1);
            rt.anchoredPosition = new Vector2(v.X, 0);
            rt.localScale = Vector3.one * 0.2f;
            return v;
        }

        /// <summary>Скрыть алерт правым кликом — до тех пор, пока проблема не исчезнет.</summary>
        private void Dismiss(string id)
        {
            if (string.IsNullOrEmpty(id) || !_dismissed.Add(id)) return;
            SFXManager.Play(Sfx.AlertDismiss);
            foreach (var v in _views)
                if (v.Id == id && !v.Dying) { v.Dying = true; v.DieT = 0f; v.Group.blocksRaycasts = false; }
            _current.RemoveAll(a => a.Id == id);
            TooltipSystem.ForceHide();   // ПКМ при нажатии закрепил подсказку алерта — снимаем и её
        }

        private static float TargetX(int index, int count)
        {
            // Ряд растёт вправо от левого края правого крыла (как в референсе)
            return Size * 0.5f + index * (Size + Gap);
        }

        private void Animate(float dt)
        {
            int alive = 0;
            foreach (var v in _views) if (!v.Dying) alive++;

            int idx = 0;
            foreach (var v in _views)
            {
                if (v.Rt == null) continue;
                if (v.Dying)
                {
                    v.DieT += dt / 0.28f;
                    float k = Mathf.Clamp01(v.DieT);
                    v.Group.alpha = 1f - LGEase.OutCubic(k);
                    v.Rt.localScale = Vector3.one * Mathf.Lerp(1f, 0.4f, LGEase.InCubic(k));
                    continue;
                }

                v.Age += dt;
                float target = TargetX(idx, alive);
                idx++;
                v.X = LGEase.Spring(v.X, target, ref v.XVel, 3.2f, 0.7f, dt);

                // Появление: "выпрыгивает" сверху вниз с перелётом
                float tIn = Mathf.Clamp01(v.Age / 0.55f);
                float s = Mathf.LerpUnclamped(0.2f, 1f, LGEase.OutBack(tIn, 2.2f));
                v.Rt.localScale = Vector3.one * s;
                v.Group.alpha = LGEase.OutCubic(Mathf.Clamp01(v.Age / 0.25f));
                float drop = (1f - LGEase.OutCubic(tIn)) * 18f;
                v.Rt.anchoredPosition = new Vector2(v.X, drop);

                // Волна-кольцо
                if (v.Ripple != null)
                {
                    float tr = Mathf.Clamp01(v.Age / 0.9f);
                    if (tr < 1f)
                    {
                        v.Ripple.localScale = Vector3.one * Mathf.Lerp(1f, 2.1f, LGEase.OutCubic(tr)) / Mathf.Max(0.2f, s);
                        v.RippleImg.color = new Color(v.Color.r, v.Color.g, v.Color.b, 0.9f * (1f - tr));
                    }
                    else if (v.Ripple.gameObject.activeSelf) v.Ripple.gameObject.SetActive(false);
                }

                // Пока свежий — пульсирует, затем спокойно светится
                float attention = Mathf.Clamp01(1f - (v.Age - 0.5f) / 3.5f);
                float pulse = attention * (0.35f + 0.35f * Mathf.Sin(v.Age * 6f));
                // Наведение: значок подрастает и всплывает, свечение ярче, черта шире
                v.Hover = Mathf.MoveTowards(v.Hover, v.HoverFlag != null && v.HoverFlag.On ? 1f : 0f, dt * 8f);
                float hv = LGEase.OutCubic(v.Hover);
                v.Glow.color = new Color(v.Color.r, v.Color.g, v.Color.b, 0.05f + 0.10f * pulse + 0.25f * hv);
                v.Rim.color = new Color(v.Color.r, v.Color.g, v.Color.b, Mathf.Max(0.45f + 0.15f * pulse, hv));
                v.Rim.rectTransform.sizeDelta = new Vector2(26f + 12f * hv, 3f);
                v.Icon.rectTransform.localScale = Vector3.one * (1f + 0.16f * hv);
                v.Icon.rectTransform.anchoredPosition = new Vector2(0f, 4f + 2f * hv);
                if (v.IconGlow != null) v.IconGlow.color = new Color(v.Color.r, v.Color.g, v.Color.b, 0.16f + 0.35f * hv);
            }

            for (int i = _views.Count - 1; i >= 0; i--)
            {
                var v = _views[i];
                if (v.Dying && v.DieT >= 1f)
                {
                    if (v.Rt != null) Destroy(v.Rt.gameObject);
                    _views.RemoveAt(i);
                }
            }
        }

        // ================================================================ Логика алертов

        private void Evaluate()
        {
            _current.Clear();
            if (_gen == null) _gen = FindAnyObjectByType<GalaxyGenerator>();
            var eco = EconomyManager.Instance;
            var tm = TechnologyManager.Instance;
            var fm = FleetManager.Instance;
            var gen = _gen;

            // 1. Энергия
            if (eco != null && (eco.IsBankrupt || eco.MonthlyEnergyIncome < 0f))
            {
                float months = eco.MonthsUntilEmpty;
                string forecast = float.IsInfinity(months) ? ""
                    : months < 1f ? Loc.T(" Казна опустеет меньше чем через месяц!")
                    : Loc.F(" Казна опустеет примерно через {0} мес.", Mathf.CeilToInt(months));
                var rep = eco.Report;
                _current.Add(new AlertInfo
                {
                    Id = "energy", Icon = LGIcon.Energy, Color = eco.IsBankrupt || eco.BankruptcyLooming ? UIManager.DS.Red : UIManager.DS.Gold,
                    Title = eco.IsBankrupt ? Loc.T("Банкротство!") : eco.BankruptcyLooming ? Loc.T("Угроза банкротства") : Loc.T("Дефицит гелия-3"),
                    Body = eco.IsBankrupt
                        ? Loc.T("Запасы энергии исчерпаны — производство урезано вдвое. Долгое банкротство приведёт к поражению.")
                        : Loc.F("Расход превышает доход ({0:0.#} / мес).{1} ", eco.MonthlyEnergyIncome, forecast) +
                          Loc.F("Содержание: флот {0:0.#}, форпосты {1:0.#}. ", rep.FleetUpkeep + rep.CivilianUpkeep, rep.OutpostUpkeepTotal) +
                          Loc.T("Стройте генераторы и добывающие станции или сократите флот."),
                    ActionHint = Loc.T("открыть обзор империи"),
                    Action = () => UIManager.Instance?.OpenEmpireOverviewModal()
                });
            }

            // 2. Свободные слоты науки
            if (tm != null)
            {
                int free = 0;
                foreach (var s in tm.Slots) if (s.CurrentTech == null && s.Queue.Count == 0) free++;
                if (free > 0)
                    _current.Add(new AlertInfo
                    {
                        Id = "research", Icon = LGIcon.Research, Color = UIManager.DS.Green, Count = free,
                        Title = Loc.T("Свободный слот исследований"),
                        Body = free == 1 ? Loc.T("Один исследовательский слот простаивает.") : Loc.F("Простаивает слотов: {0}.", free),
                        ActionHint = Loc.T("выбрать технологию"),
                        Action = () => { UIManager.Instance?.ShowModalDimPublic(); TechTreePanel.Instance?.Open(); }
                    });
            }

            if (fm != null && gen != null)
            {
                // 3. Простаивающие научные корабли
                FleetView firstSci = null; int sciIdle = 0;
                FleetView firstCon = null; int conIdle = 0;
                foreach (var fv in fm.AllFleets)
                {
                    if (fv == null || fv.Data == null || fv.Data.Destroyed || fv.Data.OwnerId != 0 || !IsIdle(fv.Data)) continue;
                    if (fv.Data.Type == FleetType.Science) { sciIdle++; if (firstSci == null) firstSci = fv; }
                    else if (fv.Data.Type == FleetType.Constructor) { conIdle++; if (firstCon == null) firstCon = fv; }
                }

                var unsurveyed = FindFrontier(gen, surveyed: false);
                if (sciIdle > 0 && unsurveyed != null)
                {
                    var ship = firstSci;
                    _current.Add(new AlertInfo
                    {
                        Id = "science", Icon = LGIcon.Sensors, Color = UIManager.DS.NeonCyan, Count = sciIdle,
                        Title = Loc.T("Научный корабль простаивает"),
                        Body = Loc.F("Рядом есть неизученные системы (например, {0}). Выделите корабль и щёлкните ПКМ по системе.", unsurveyed.Name),
                        ActionHint = Loc.T("выбрать корабль"),
                        Action = () => Focus(ship)
                    });
                }

                // 4. Можно ставить форпост
                var surveyed = FindFrontier(gen, surveyed: true);
                float influenceCost = 25f - (tm != null ? tm.StarbaseCostDiscount : 0f);
                bool canAffordOutpost = eco != null && eco.Alloys >= 50f && eco.Influence >= influenceCost;
                if (conIdle > 0 && surveyed != null && canAffordOutpost)
                {
                    var sys = surveyed;
                    _current.Add(new AlertInfo
                    {
                        Id = "outpost", Icon = LGIcon.Starbase, Color = UIManager.DS.Gold, Count = conIdle,
                        Title = Loc.T("Можно расширить границы"),
                        Body = Loc.F("Система {0} изучена и свободна. Строительный корабль ждёт приказа — закажите «Форпост» в инспекторе системы.", sys.Name),
                        ActionHint = Loc.T("показать систему"),
                        Action = () => Focus(sys)
                    });
                }

                // 5. Планета для колонии / 6. безработица / 7. жильё
                StarSystem colonySys = null; PlanetData colonyPlanet = null;
                StarSystem idleSys = null; int idleTotal = 0;
                StarSystem crowdSys = null; PlanetData crowded = null;
                foreach (var s in gen.Systems)
                {
                    if (s.OwnerId != 0) continue;
                    foreach (var p in s.Planets)
                    {
                        // Колонисты уже в пути — планета больше не «ждёт» заселения
                        if (colonyPlanet == null && s.IsSurveyed && p.CanColonize && !p.IsColonizing) { colonySys = s; colonyPlanet = p; }
                        if (p.IdlePops > 0) { idleTotal += p.IdlePops; if (idleSys == null) idleSys = s; }
                        if (crowded == null && p.Population > p.HousingCapacity) { crowdSys = s; crowded = p; }
                    }
                }

                bool canAffordColony = eco != null && eco.Minerals >= 80f && eco.Alloys >= 20f && eco.Influence >= 25f;
                if (colonyPlanet != null && canAffordColony)
                {
                    var sys = colonySys; var p = colonyPlanet;
                    _current.Add(new AlertInfo
                    {
                        Id = "colony", Icon = LGIcon.Planet, Color = UIManager.DS.Green,
                        Title = Loc.T("Планета пригодна для колонии"),
                        Body = Loc.F("{0} ({1}, пригодность {2}%). Щёлкните — откроется планета, там кнопка «Основать колонию».", p.Name, p.ClassDisplayName, p.HabitabilityPercent),
                        ActionHint = Loc.T("открыть планету"),
                        Action = () => OpenPlanet(sys, p)
                    });
                }

                if (crowded != null)
                {
                    var sys = crowdSys; var p = crowded;
                    _current.Add(new AlertInfo
                    {
                        Id = "housing", Icon = LGIcon.Housing, Color = UIManager.DS.Red,
                        Title = Loc.T("Нехватка жилья"),
                        Body = Loc.F("На {0} живёт {1} при жилье на {2}. Постройте городской район — иначе рост остановится.", p.Name, p.Population, p.HousingCapacity),
                        ActionHint = Loc.T("показать систему"),
                        Action = () => Focus(sys)
                    });
                }

                if (idleTotal > 0)
                {
                    var sys = idleSys;
                    _current.Add(new AlertInfo
                    {
                        Id = "unemployed", Icon = LGIcon.Population, Color = UIManager.DS.Gold, Count = idleTotal,
                        Title = Loc.T("Безработное население"),
                        Body = Loc.F("Без работы: {0}. Стройте районы (горнодобыча, энергетика, промышленность), чтобы занять жителей.", idleTotal),
                        ActionHint = Loc.T("показать систему"),
                        Action = () => Focus(sys)
                    });
                }

                // Устаревшие корабли: есть новый основной проект корпуса — старые стоит модернизировать
                var dmgr = ShipDesignManager.Instance;
                if (dmgr != null)
                {
                    var old = dmgr.OutdatedShips(0);
                    if (old.Count > 0)
                    {
                        float cost = 0f;
                        foreach (var f in old) cost += dmgr.RetrofitCost(f);
                        _current.Add(new AlertInfo
                        {
                            Id = "outdated", Icon = LGIcon.Wrench, Color = UIManager.DS.Gold, Count = old.Count,
                            Title = Loc.T("Корабли устарели"),
                            Body = Loc.F("Кораблей старого проекта: {0}. На верфи они перевооружатся по актуальному проекту корпуса ", old.Count) +
                                   Loc.F("(всего {0:0} сплавов) — станут сильнее.", cost),
                            ActionHint = Loc.T("открыть верфь"),
                            Action = () => ShipyardWindow.Instance?.Open()
                        });
                    }
                }

                // 8. Дипломатические предложения (от каждой империи ИИ — своё)
                foreach (var ai in AIEmpireManager.All)
                {
                    if (ai.PendingOffer == AIEmpireManager.OfferKind.None || !Contacts.PlayerMet(ai.OwnerId)) continue;
                    var kind = ai.PendingOffer;
                    int owner = ai.OwnerId;
                    var (icon, color, title) = kind switch
                    {
                        AIEmpireManager.OfferKind.Peace => (LGIcon.Peace, UIManager.DS.Green, Loc.F("{0} предлагает мир", ai.AIName)),
                        AIEmpireManager.OfferKind.Pact => (LGIcon.Handshake, UIManager.DS.Green, Loc.F("{0} предлагает пакт", ai.AIName)),
                        AIEmpireManager.OfferKind.Alliance => (LGIcon.Defense, UIManager.DS.Green, Loc.F("{0} предлагает союз", ai.AIName)),
                        AIEmpireManager.OfferKind.Demand => (LGIcon.Warning, UIManager.DS.Red, Loc.F("{0} требует дань", ai.AIName)),
                        _ => (LGIcon.Trade, UIManager.DS.Gold, Loc.F("{0} предлагает сделку", ai.AIName))
                    };
                    _current.Add(new AlertInfo
                    {
                        Id = "offer_" + owner, Icon = icon, Color = color,
                        Title = title,
                        Body = Loc.F("Причина: {0}. Предложение в силе ещё {1} дн.", ai.PendingOfferReason, ai.PendingOfferDays),
                        ActionHint = Loc.T("открыть дипломатию"),
                        Action = () => DiplomacyModal.Instance?.Open(owner)
                    });
                }

                // 9. Осады
                var sm = SiegeManager.Instance;
                if (sm != null)
                {
                    StarSystem lost = null, taking = null; int defend = 0, attack = 0;
                    foreach (var sg in sm.Sieges)
                    {
                        var sys = sg.SystemId >= 0 && sg.SystemId < gen.Systems.Count ? gen.Systems[sg.SystemId] : null;
                        if (sys == null || !sg.Active) continue;
                        if (sys.OwnerId == 0) { defend++; if (lost == null) lost = sys; }
                        else if (sg.Attacker == 0) { attack++; if (taking == null) taking = sys; }
                    }
                    if (lost != null)
                    {
                        var target = lost;
                        _current.Add(new AlertInfo
                        {
                            Id = "siege_def", Icon = LGIcon.Siege, Color = UIManager.DS.Red, Count = defend,
                            Title = Loc.T("Ваша система в осаде"),
                            Body = Loc.F("{0}: враг блокирует систему. Если не прислать военный флот, через {1:0} дн. осады она перейдёт к противнику.", target.Name, SiegeManager.RequiredDays(target)),
                            ActionHint = Loc.T("показать систему"),
                            Action = () => Focus(target)
                        });
                    }
                    if (taking != null)
                    {
                        var target = taking;
                        var sg = sm.GetSiege(target.Id);
                        _current.Add(new AlertInfo
                        {
                            Id = "siege_att", Icon = LGIcon.Siege, Color = UIManager.DS.Green, Count = attack,
                            Title = Loc.T("Идёт осада"),
                            Body = Loc.F("{0}: {1:0} / {2:0} дн.", target.Name, (sg != null ? sg.Progress : 0f), SiegeManager.RequiredDays(target)) +
                                   (sg != null && sg.BaseHolding ? Loc.T(" Осада стоит — сначала подавите звёздную базу.")
                                    : sg != null && sg.Contested ? Loc.T(" Осада стоит — на орбите флот защитника.") : Loc.T(" Не уводите флот до захвата.")),
                            ActionHint = Loc.T("показать систему"),
                            Action = () => Focus(target)
                        });
                    }
                }

                // 10. Угроза: самая сильная из враждебных империй, если она сильнее вас
                AIEmpireManager threat = null;
                foreach (var ai in AIEmpireManager.Alive)
                    if (ai.AtWar && fm.GetMilitaryPower(ai.OwnerId) > fm.GetMilitaryPower(0)
                        && (threat == null || fm.GetMilitaryPower(ai.OwnerId) > fm.GetMilitaryPower(threat.OwnerId)))
                        threat = ai;
                if (threat != null)
                {
                    var t = threat;
                    _current.Add(new AlertInfo
                    {
                        Id = "threat", Icon = LGIcon.Fleet, Color = UIManager.DS.Red,
                        Title = Loc.F("Угроза: {0}", t.AIName),
                        Body = Loc.F("Враждебная империя сильнее ({0:N0} против {1:N0}). Стройте флот на верфи столицы или предложите мир.", fm.GetMilitaryPower(t.OwnerId), fm.GetMilitaryPower(0)),
                        ActionHint = Loc.T("открыть дипломатию"),
                        Action = () => DiplomacyModal.Instance?.Open(t.OwnerId)
                    });
                }
            }
        }

        private static bool IsIdle(FleetData d)
            => d.State == FleetState.Orbiting && d.Path.Count == 0 && d.TargetSystemId < 0
               && d.SurveyTargetSystemId < 0 && d.BuildTargetSystemId < 0;

        private static StarSystem FindFrontier(GalaxyGenerator gen, bool surveyed)
        {
            foreach (var s in gen.Systems)
            {
                if (s.OwnerId != 0) continue;
                foreach (int id in s.ConnectedSystemIds)
                {
                    if (id < 0 || id >= gen.Systems.Count) continue;
                    var n = gen.Systems[id];
                    if (n.OwnerId >= 0) continue;
                    if (n.IsSurveyed == surveyed) return n;
                }
            }
            return null;
        }

        private static void Focus(StarSystem sys)
        {
            if (sys == null) return;
            var cam = FindAnyObjectByType<StrategyCameraController>();
            if (cam != null) cam.FocusOn(sys.Position);
            GalaxyView.Instance?.SelectSystem(sys.Id);
            UIManager.Instance?.ShowSystemPanel(sys);
        }

        /// <summary>Сразу к планете: обзор планеты с её действиями, без входа в систему.</summary>
        private static void OpenPlanet(StarSystem sys, PlanetData planet)
        {
            if (sys == null || planet == null) return;
            var svm = SystemViewManager.Instance;
            if (svm == null || !svm.IsInSystemView) Focus(sys);
            UIManager.Instance?.ShowPlanetInspector(planet, sys);
        }

        private static void Focus(FleetView fv)
        {
            if (fv == null) return;
            var cam = FindAnyObjectByType<StrategyCameraController>();
            if (cam != null) cam.FocusOn(fv.transform.position);
            FleetManager.Instance?.SelectFleet(fv);
        }
    }

    /// <summary>Правый клик по алерту (кнопка Unity реагирует только на левый).</summary>
    public sealed class AlertRightClick : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
    {
        public System.Action OnRight;

        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData e)
        {
            if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Right) OnRight?.Invoke();
        }
    }

    /// <summary>Курсор над алертом (для эффекта наведения).</summary>
    public sealed class AlertHoverFlag : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        public bool On;
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e)
        {
            On = true;
            SFXManager.Play(StellarisClone.Core.Audio.Sfx.UiHover, 0.4f);
        }
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => On = false;
        private void OnDisable() => On = false;
    }
}
