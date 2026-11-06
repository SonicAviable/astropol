using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Подсказки ресурсов верхней панели (вариант A «Сводка со шкалами»): шапка ресурса, крупно запас и доход,
    /// шкала склада, источники дохода и статьи расхода строками с полоской доли (нулевые приглушены),
    /// выделенный итог и короткое пояснение. Разметка — блоки TooltipSystem ([[big]], [[meter]], [[src]], [[total]]).
    /// </summary>
    public partial class UIManager
    {
        private const string TipGreen = "#5CE08F", TipRed = "#FF8A7A", TipMuted = "#8AA2A8";

        /// <summary>Сборщик одной подсказки ресурса: источники копятся, доли считаются от самой крупной статьи.</summary>
        private sealed class ResTip
        {
            private readonly StringBuilder _sb = new StringBuilder();
            private readonly List<(LGIcon icon, string name, float v, bool expense)> _inc = new List<(LGIcon, string, float, bool)>();
            private readonly List<(LGIcon icon, string name, float v, bool expense)> _exp = new List<(LGIcon, string, float, bool)>();
            private readonly string _hex;

            public ResTip(LGIcon icon, string hex, string title, string sub)
            {
                _hex = hex;
                _sb.Append($"[[hdr|{hex}|{sub}||-]]\n[[{icon}:{hex}]]<b>{title}</b>\n");
            }

            public ResTip Text(string t) { if (!string.IsNullOrEmpty(t)) _sb.Append(t.TrimEnd('\n')).Append('\n'); return this; }

            /// <summary>Крупно: запас (или иное главное число) и изменение в месяц.</summary>
            public ResTip Big(string value, string cap, float perMonth, string perCap = null)
            {
                string pm = $"<color={(perMonth >= 0f ? TipGreen : TipRed)}>{Signed(perMonth)}</color>";
                _sb.Append($"[[big|{value}|{cap}|{pm}|{perCap ?? Loc.T("в месяц")}]]\n");
                return this;
            }

            public ResTip Meter(float k, string left, string right)
            {
                _sb.Append($"[[meter|{_hex}|{k.ToString("0.###", CultureInfo.InvariantCulture)}|{left}|{right}]]\n");
                return this;
            }

            public ResTip Income(LGIcon icon, string name, float v) { _inc.Add((icon, name, v, false)); return this; }
            public ResTip Expense(LGIcon icon, string name, float v) { _exp.Add((icon, name, Mathf.Abs(v), true)); return this; }

            /// <summary>Разделы «Доход» и «Расход» (пустой раздел не выводится) и итог.</summary>
            public ResTip Rows(float net, string totalCap = null, string unit = null)
            {
                float max = 0.0001f;
                foreach (var r in _inc) max = Mathf.Max(max, Mathf.Abs(r.v));
                foreach (var r in _exp) max = Mathf.Max(max, Mathf.Abs(r.v));
                void Section(string cap, List<(LGIcon icon, string name, float v, bool expense)> rows)
                {
                    if (rows.Count == 0) return;
                    _sb.Append($"[[sec|{cap}|#6F8794]]\n");
                    foreach (var r in rows)
                    {
                        string val = Mathf.Abs(r.v) < 0.05f ? "0" : r.expense ? "−" + r.v.ToString("0.#", CultureInfo.CurrentCulture) : Signed(r.v);
                        string col = r.expense ? TipRed : TipGreen;
                        float share = Mathf.Abs(r.v) < 0.05f ? 0f : Mathf.Abs(r.v) / max;
                        _sb.Append($"[[src|{r.icon}|{col}|{r.name}|{val}|{share.ToString("0.###", CultureInfo.InvariantCulture)}]]\n");
                    }
                }
                Section(Loc.T("ДОХОД"), _inc);
                Section(Loc.T("РАСХОД"), _exp);
                _sb.Append($"[[total|{totalCap ?? Loc.T("Итого в месяц")}|{Signed(net)}{unit ?? ""}|{(net >= 0f ? TipGreen : TipRed)}]]\n");
                return this;
            }

            public string Build() => _sb.ToString().TrimEnd('\n');
        }

        private static string Signed(float v) => (v >= 0f ? "+" : "−") + Mathf.Abs(v).ToString("0.#", CultureInfo.CurrentCulture);
        private static string Num(float v) => Mathf.FloorToInt(v).ToString("N0", CultureInfo.CurrentCulture);

        /// <summary>Когда склад заполнится / опустеет при текущем доходе.</summary>
        private static string StorageEta(float stock, float cap, float net)
        {
            if (cap > 0f && stock >= cap - 0.5f) return $"<color=#F2C747>{Loc.T("склад полон — излишки пропадают")}</color>";
            if (net > 0.05f && cap > 0f) return Loc.F("заполнится через ~{0} мес", Mathf.CeilToInt((cap - stock) / net));
            if (net < -0.05f) return $"<color={TipRed}>{Loc.F("кончится через ~{0} мес", Mathf.Max(1, Mathf.CeilToInt(stock / -net)))}</color>";
            return "";
        }

        private string EnergyTip(EconomyManager eco)
        {
            float net = eco.MonthlyEnergyIncome;
            var rep = eco.Report;
            string warn = eco.IsBankrupt
                ? Loc.T("<color=#FF5555><b>БАНКРОТСТВО</b> — производство урезано вдвое</color>")
                : eco.BankruptcyLooming
                    ? Loc.F("<color=#FFAA55><b>Угроза банкротства</b>: казна опустеет через ~{0} мес.</color>", Mathf.Max(1, Mathf.CeilToInt(eco.MonthsUntilEmpty)))
                    : null;
            var t = new ResTip(LGIcon.Energy, "#F2C747", Loc.T("Гелий-3"), Loc.T("энергия империи · содержание флота"))
                .Text(warn)
                .Big(Num(eco.EnergyCredits), Loc.T("запас"), net)
                .Income(LGIcon.Globe, Loc.T("База империи"), eco.BaseEnergyIncome)
                .Income(LGIcon.Power, Loc.T("Районы-генераторы"), eco.PlanetEnergyOutput)
                .Income(LGIcon.Starbase, Loc.T("Добывающие станции"), eco.StationEnergyOutput);
            if (eco.TechEnergyOutput > 0f) t.Income(LGIcon.Reactor, Loc.T("Реакторные технологии"), eco.TechEnergyOutput);
            t.Expense(LGIcon.Fleet, rep.NavalUsed > rep.NavalCapacity ? Loc.F("Военный флот (×{0:0.##})", rep.OverCapacityMult) : Loc.T("Военный флот"), rep.FleetUpkeep)
             .Expense(LGIcon.ConstructorShip, Loc.T("Гражданские суда"), rep.CivilianUpkeep)
             .Expense(LGIcon.Starbase, Loc.F("Форпосты ({0})", rep.Outposts), rep.Outposts * EmpireEconomy.OutpostUpkeep);
            if (Fortifications.Upkeep(0) > 0f) t.Expense(LGIcon.ShieldDome, Loc.T("Укрепления баз"), Fortifications.Upkeep(0));
            if (rep.ArmyUpkeep > 0.01f) t.Expense(LGIcon.Swords, Loc.T("Армия (карты)"), rep.ArmyUpkeep);
            return t.Rows(net)
                .Text(Loc.F("<color=#8AA2A8>Флотский лимит <b>{0} / {1}</b> (корвет 1, фрегат 2, эсминец 3; +{2} за колонию). Сверх лимита содержание всего флота растёт.</color>",
                    rep.NavalUsed, rep.NavalCapacity, EmpireEconomy.NavalCapacityPerColony))
                .Build();
        }

        private string MineralsTip(EconomyManager eco)
        {
            float net = eco.MonthlyMineralsIncome, cap = eco.MineralStorage;
            return new ResTip(LGIcon.Minerals, "#4DE6CC", Loc.T("Титан"), Loc.T("строительство · корабли · районы"))
                .Big(Num(eco.Minerals), Loc.T("запас"), net)
                .Meter(cap > 0f ? eco.Minerals / cap : 0f, Loc.F("Склад {0} / {1}", Num(eco.Minerals), Num(cap)), StorageEta(eco.Minerals, cap, net))
                .Income(LGIcon.Globe, Loc.T("База империи"), eco.BaseMineralsIncome)
                .Income(LGIcon.Minerals, Loc.T("Горные районы"), eco.PlanetMineralsOutput)
                .Income(LGIcon.Starbase, Loc.T("Добывающие станции"), eco.StationMineralsOutput)
                .Expense(LGIcon.Industry, Loc.T("Переработка на заводах"), eco.RefiningMinerals)
                .Rows(net)
                .Text(Loc.F("<color=#8AA2A8>Склады вмещают <b>{0}</b> (+{1:0} за колонию) — излишки пропадают. Лишний титан — в решения империи.</color>",
                    Num(cap), EmpireEconomy.MineralStoragePerColony))
                .Build();
        }

        private string AlloysTip(EconomyManager eco)
        {
            float net = eco.MonthlyAlloysIncome;
            return new ResTip(LGIcon.Alloys, "#F29E5C", Loc.T("Сплавы"), Loc.T("корабли · станции · укрепления"))
                .Big(Num(eco.Alloys), Loc.T("запас"), net)
                .Income(LGIcon.Globe, Loc.T("База империи"), eco.BaseAlloysIncome)
                .Income(LGIcon.Planet, Loc.T("Планеты и заводы"), eco.PlanetAlloysOutput)
                .Rows(net)
                .Text(Loc.T("<color=#8AA2A8>Заводы промышленных районов перерабатывают титан в сплавы.</color>"))
                .Build();
        }

        private string InfluenceTip(EconomyManager eco)
        {
            float net = eco.MonthlyInfluenceIncome, laws = EmpireLaws.InfluenceUpkeep;
            var t = new ResTip(LGIcon.Influence, "#FF7A94", Loc.T("Влияние"), Loc.T("экспансия · дипломатия · законы"))
                .Big(Num(eco.Influence), Loc.T("запас"), net)
                .Income(LGIcon.Influence, Loc.T("Доход империи"), net + laws);
            if (laws > 0.01f) t.Expense(LGIcon.Menu, Loc.T("Законы (L)"), laws);
            string sprawl = FleetManager.SprawlInfluence(0) > 0f
                ? Loc.F(" Форпост дороже на {0:0}: империя разрослась (+{1:0.#} за каждую систему сверх {2}).",
                    FleetManager.SprawlInfluence(0), FleetManager.SprawlInfluencePerSystem, FleetManager.SprawlFreeSystems)
                : "";
            return t.Rows(net)
                .Text(Loc.F("<color=#8AA2A8>Тратится на форпосты ({0:0}), колонии ({1:0}), пакты ({2:0}), мирные предложения ({3:0}) и терраформинг.{4}</color>",
                    FleetManager.OutpostInfluenceCost(0), PlanetData.ColonyInfluenceCost, AIEmpireManager.PactInfluenceCost, AIEmpireManager.PeaceInfluenceCost, sprawl))
                .Build();
        }

        private string ResearchTip(TechnologyManager tm)
        {
            int active = 0;
            foreach (var s in tm.Slots) if (s.CurrentTech != null && !s.IsPaused) active++;
            float mult = tm.GlobalResearchSpeedMultiplier;
            float assist = ResearchAssist.Science(0, out int ships);
            var t = new ResTip(LGIcon.Research, "#5CF59A", Loc.T("Наука"), Loc.T("исследования технологий"))
                .Big($"{active} / {tm.Slots.Count}", Loc.T("активных слотов"), tm.MonthlyResearchIncome)
                .Income(LGIcon.Globe, Loc.T("Администрация"), TechnologyManager.BaseScience)
                .Income(LGIcon.Population, Loc.F("Население ({0} жит.)", tm.Population), tm.ScienceFromPopulation);
            if (tm.FlatScience > 0f) t.Income(LGIcon.Star, Loc.T("Открытия и события"), tm.FlatScience);
            if (ships > 0) t.Income(LGIcon.ScienceShip, Loc.F("Научные корабли у колоний ({0})", ships), assist);
            t.Rows(tm.MonthlyResearchIncome);
            if (Mathf.Abs(mult - 1f) > 0.01f) t.Text(Loc.F("<color=#8AA2A8>Множитель технологий и событий: <b>×{0:0.##}</b></color>", mult));
            return t.Text(Loc.T("<color=#8AA2A8>Наука растёт с населением — заселяйте планеты и стройте жильё. Скорость делится между слотами: один слот идёт быстрее, чем каждый из нескольких.</color>"))
                .Build();
        }

        /// <summary>Стабильность: средняя крупно, колонии строками — полоска показывает стабильность планеты (0–100) цветом её уровня.</summary>
        private static string StabilityTip(List<PlanetData> colonies, float avg, StabilityTier tier, string hex)
        {
            var sb = new StringBuilder();
            sb.Append($"[[hdr|#F08CDB|{PlanetStability.TierName(tier)}||-]]\n[[Society:#F08CDB]]<b>{Loc.T("Стабильность")}</b>\n");
            sb.Append($"[[big|<color=#{hex}>{avg:0}</color>|{Loc.T("средняя")}|{colonies.Count}|{Loc.T("колоний")}]]\n");
            sb.Append($"[[meter|#{hex}|{(avg / 100f).ToString("0.###", CultureInfo.InvariantCulture)}|{PlanetStability.TierName(tier)}|{avg:0} / 100]]\n");
            sb.Append($"<color=#8AA2A8>{PlanetStability.TierEffect(tier)}</color>\n");
            sb.Append($"[[sec|{Loc.T("САМЫЕ НЕСПОКОЙНЫЕ")}|#6F8794]]\n");
            for (int i = 0; i < colonies.Count && i < 6; i++)
            {
                var p = colonies[i];
                string ph = PlanetStability.Hex(PlanetStability.TierColor(p.Tier));
                sb.Append($"[[src|Planet|#{ph}|{PlanetNames.Title(p.Name)}|{p.Stability:0}|{Mathf.Max(0.02f, p.Stability / 100f).ToString("0.###", CultureInfo.InvariantCulture)}]]\n");
            }
            if (colonies.Count > 6) sb.Append(Loc.F("<color=#8AA2A8>…и ещё {0}</color>\n", colonies.Count - 6));
            sb.Append(Loc.T("<color=#8AA2A8>Средняя — по числу жителей. Нажмите, чтобы открыть управление колониями.</color>"));
            return sb.ToString();
        }
    }
}
