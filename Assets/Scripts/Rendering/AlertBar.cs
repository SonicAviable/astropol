using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Cam;
using StellarisClone.Core;
using StellarisClone.Generation;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Ряд значков-алертов под верхней панелью (как в Stellaris):
    /// «свободный слот науки», «научный корабль простаивает», «дефицит энергии» и т.д.
    ///   • появляются "выпрыгивая" с пружинным перелётом и волной-кольцом;
    ///   • пока свежие — пульсируют неоном;
    ///   • при наведении — подсказка, по клику — переход к нужному действию;
    ///   • когда проблема решена — сжимаются и тают, соседи плавно съезжаются.
    /// </summary>
    public class AlertBar : MonoBehaviour
    {
        public static AlertBar Instance { get; private set; }

        private const float Size = 42f;
        private const float Gap = 10f;
        private const float EvalInterval = 0.5f;

        private struct AlertInfo
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
            public LiquidGlassEffect Fx;
            public RectTransform Ripple;
            public LiquidGlassEffect RippleFx;
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
        private float _evalTimer;
        private GalaxyGenerator _gen;

        public void Build(Transform hud)
        {
            Instance = this;
            _root = LGBuild.Rect(hud, "[UI] AlertBar");
            _root.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -64), new Vector2(10, Size));
            _root.gameObject.SetActive(false);
        }

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
                Sync();
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
                    SFXManager.Play("ui_click", 0.6f, 1.35f);
                }
                view.Action = a.Action;
                view.Tip.SetText($"<b>{a.Title}</b>\n{a.Body}\n\n<color=#8AA2A8>ЛКМ — {a.ActionHint}</color>");
                view.Count.text = a.Count > 1 ? a.Count.ToString() : "";
                view.Count.transform.parent.gameObject.SetActive(a.Count > 1);
            }

            // порядок = приоритет
            _views.Sort((x, y) => IndexOf(x.Id).CompareTo(IndexOf(y.Id)));
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

            // Волна-кольцо при появлении (под значком)
            var ripple = LGBuild.Panel(rt, "Ripple", new Color(0, 0, 0, 0));
            ripple.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Size, Size));
            v.RippleFx = LG.Border(ripple.gameObject, new Color(a.Color.r, a.Color.g, a.Color.b, 0.9f));
            v.RippleFx.Radius = Size * 0.5f;
            v.Ripple = ripple.rectTransform;

            v.Bg = LGBuild.Panel(rt, "Bg", new Color(a.Color.r * 0.28f, a.Color.g * 0.28f, a.Color.b * 0.28f, 1f), raycast: true);
            v.Bg.rectTransform.Stretch();
            var btn = v.Bg.gameObject.AddComponent<Button>();
            btn.onClick.AddListener(() => { v.Action?.Invoke(); SFXManager.Play("ui_click", 1f, 1.05f); });
            v.Fx = LG.Button(v.Bg.gameObject, new Color(a.Color.r, a.Color.g, a.Color.b, 0.75f), Size * 0.5f);
            v.Fx.FillMultiplier = 1.4f;
            v.Fx.GlowMultiplier = 0.8f;
            v.Fx.GetComponent<LGInteractive>().hoverScale = 1.08f;
            v.Tip = v.Bg.gameObject.AddComponent<TooltipTrigger>();

            v.Icon = LGIcons.Create(v.Bg.transform, a.Icon, 22f, Color.Lerp(a.Color, Color.white, 0.15f));

            // Счётчик (например, число простаивающих кораблей)
            var badge = LGBuild.Panel(v.Bg.transform, "Badge", new Color(0.9f, 0.25f, 0.28f, 1f));
            badge.rectTransform.At(new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-6, 6), new Vector2(18, 18));
            var bFx = LG.Apply(badge.gameObject, LiquidGlassEffect.Role.Fill, 9f);
            bFx.GlowMultiplier = 0.4f;
            v.Count = LGBuild.Label(badge.transform, "", 10, Color.white, TextAnchor.MiddleCenter, bold: true);
            badge.gameObject.SetActive(false);

            // стартовая позиция — там, где появится
            int n = 0;
            foreach (var w in _views) if (!w.Dying) n++;
            v.X = TargetX(n, n + 1);
            rt.anchoredPosition = new Vector2(v.X, 0);
            rt.localScale = Vector3.one * 0.2f;
            return v;
        }

        private static float TargetX(int index, int count)
        {
            float width = count * Size + Mathf.Max(0, count - 1) * Gap;
            return -width * 0.5f + Size * 0.5f + index * (Size + Gap);
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
                        v.RippleFx.SetRim(new Color(v.Color.r, v.Color.g, v.Color.b, 0.9f * (1f - tr)));
                    }
                    else if (v.Ripple.gameObject.activeSelf) v.Ripple.gameObject.SetActive(false);
                }

                // Пока свежий — пульсирует, затем спокойно светится
                float attention = Mathf.Clamp01(1f - (v.Age - 0.5f) / 3.5f);
                v.Fx.SetPulse(attention * (0.35f + 0.35f * Mathf.Sin(v.Age * 6f)));
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
                    : months < 1f ? " Казна опустеет меньше чем через месяц!"
                    : $" Казна опустеет примерно через {Mathf.CeilToInt(months)} мес.";
                var rep = eco.Report;
                _current.Add(new AlertInfo
                {
                    Id = "energy", Icon = LGIcon.Energy, Color = eco.IsBankrupt || eco.BankruptcyLooming ? UIManager.DS.Red : UIManager.DS.Gold,
                    Title = eco.IsBankrupt ? "Банкротство!" : eco.BankruptcyLooming ? "Угроза банкротства" : "Дефицит гелия-3",
                    Body = eco.IsBankrupt
                        ? "Запасы энергии исчерпаны — производство урезано вдвое. Долгое банкротство приведёт к поражению."
                        : $"Расход превышает доход ({eco.MonthlyEnergyIncome:0.#} / мес).{forecast} " +
                          $"Содержание: флот {rep.FleetUpkeep + rep.CivilianUpkeep:0.#}, форпосты {rep.OutpostUpkeepTotal:0.#}. " +
                          "Стройте генераторы и добывающие станции или сократите флот.",
                    ActionHint = "открыть обзор империи",
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
                        Title = "Свободный слот исследований",
                        Body = free == 1 ? "Один исследовательский слот простаивает." : $"Простаивает слотов: {free}.",
                        ActionHint = "выбрать технологию",
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
                        Title = "Научный корабль простаивает",
                        Body = $"Рядом есть неизученные системы (например, {unsurveyed.Name}). Выделите корабль и щёлкните ПКМ по системе.",
                        ActionHint = "выбрать корабль",
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
                        Title = "Можно расширить границы",
                        Body = $"Система {sys.Name} изучена и свободна. Строительный корабль ждёт приказа — закажите «Форпост» в инспекторе системы.",
                        ActionHint = "показать систему",
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
                        if (colonyPlanet == null && s.IsSurveyed && p.CanColonize) { colonySys = s; colonyPlanet = p; }
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
                        Title = "Планета пригодна для колонии",
                        Body = $"{p.Name} ({p.ClassDisplayName}, пригодность {p.HabitabilityPercent}%). Войдите в систему {sys.Name} двойным кликом и нажмите «Основать колонию».",
                        ActionHint = "показать систему",
                        Action = () => Focus(sys)
                    });
                }

                if (crowded != null)
                {
                    var sys = crowdSys; var p = crowded;
                    _current.Add(new AlertInfo
                    {
                        Id = "housing", Icon = LGIcon.Housing, Color = UIManager.DS.Red,
                        Title = "Нехватка жилья",
                        Body = $"На {p.Name} живёт {p.Population} при жилье на {p.HousingCapacity}. Постройте городской район — иначе рост остановится.",
                        ActionHint = "показать систему",
                        Action = () => Focus(sys)
                    });
                }

                if (idleTotal > 0)
                {
                    var sys = idleSys;
                    _current.Add(new AlertInfo
                    {
                        Id = "unemployed", Icon = LGIcon.Population, Color = UIManager.DS.Gold, Count = idleTotal,
                        Title = "Безработное население",
                        Body = $"Без работы: {idleTotal}. Стройте районы (горнодобыча, энергетика, промышленность), чтобы занять жителей.",
                        ActionHint = "показать систему",
                        Action = () => Focus(sys)
                    });
                }

                // 8. Дипломатическое предложение
                var ai = AIEmpireManager.Instance;
                if (ai != null && ai.PendingOffer != AIEmpireManager.OfferKind.None)
                {
                    bool peace = ai.PendingOffer == AIEmpireManager.OfferKind.Peace;
                    _current.Add(new AlertInfo
                    {
                        Id = "offer", Icon = peace ? LGIcon.Peace : LGIcon.Handshake, Color = UIManager.DS.Green,
                        Title = peace ? $"{ai.AIName} предлагает мир" : $"{ai.AIName} предлагает пакт",
                        Body = $"Причина: {ai.PendingOfferReason}. Предложение в силе ещё {ai.PendingOfferDays} дн.",
                        ActionHint = "открыть дипломатию",
                        Action = () => DiplomacyModal.Instance?.Open()
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
                            Title = "Ваша система в осаде",
                            Body = $"{target.Name}: враг блокирует систему. Если не прислать военный флот, через {SiegeManager.RequiredDays(target):0} дн. осады она перейдёт к противнику.",
                            ActionHint = "показать систему",
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
                            Title = "Идёт осада",
                            Body = $"{target.Name}: {(sg != null ? sg.Progress : 0f):0} / {SiegeManager.RequiredDays(target):0} дн." +
                                   (sg != null && sg.Contested ? " Осада стоит — на орбите флот защитника." : " Не уводите флот до захвата."),
                            ActionHint = "показать систему",
                            Action = () => Focus(target)
                        });
                    }
                }

                // 10. Угроза
                if (ai != null && ai.AtWar && fm.GetMilitaryPower(0) < fm.GetMilitaryPower(AIEmpireManager.AIOwnerId))
                {
                    _current.Add(new AlertInfo
                    {
                        Id = "threat", Icon = LGIcon.Fleet, Color = UIManager.DS.Red,
                        Title = $"Угроза: {ai.AIName}",
                        Body = $"Враждебная империя сильнее ({fm.GetMilitaryPower(AIEmpireManager.AIOwnerId):N0} против {fm.GetMilitaryPower(0):N0}). Стройте флот на верфи столицы или предложите мир.",
                        ActionHint = "открыть дипломатию",
                        Action = () => DiplomacyModal.Instance?.Open()
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

        private static void Focus(FleetView fv)
        {
            if (fv == null) return;
            var cam = FindAnyObjectByType<StrategyCameraController>();
            if (cam != null) cam.FocusOn(fv.transform.position);
            FleetManager.Instance?.SelectFleet(fv);
        }
    }
}
