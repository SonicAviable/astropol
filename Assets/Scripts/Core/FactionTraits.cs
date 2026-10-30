using System.Collections.Generic;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Особенности фракций сверх доходов (FactionInfo). Сейчас — Хлорофиловый Конклав Тэрра'ан:
    ///   + Живая броня: корпус заживает 1% в день вне боя (2% в своих системах), поверх ремонта в доках.
    ///   + Сенсорная сеть мицелия: разведка +20%, научные корабли видят на 2 прыжка.
    ///   + Дипломатия корней: влияние +35% (FactionInfo), договоры на 25% дольше, союзники по коалиции делятся картой.
    ///   + Биокора: корпус +15%, щиты +10%.   + Живые форпосты: звёздная база на 20% дешевле по сплавам.
    ///   − Медленный рост: корабли и форпосты строятся на 20% дольше.   − Гиперскорость −10%.
    ///   − Энергетическое оружие бьёт по их кораблям на 15% сильнее.   − Минералы −10% (FactionInfo).
    ///   ★ Пробуждение сада: враг в системе Конклава — база на 3 дня получает +30% брони,
    ///     вражеские флоты уходят из системы на 15% медленнее (CombatManager).
    ///
    /// Машинный Консенсус «Ноль»:
    ///   + Синтеты: вид-основатель не бунтует, климат ему безразличен; население собирают фабрики (рост −25%, без стабильности).
    ///   + Дешёвая сборка: роботы вдвое дешевле, и вместо роботов собираются полноценные синтеты.
    ///   + Ассимиляция: чужие органики на мирах Консенсуса понемногу становятся синтетами.
    ///   + Титан +20%, сплавы +10% (FactionInfo), корпус +10%.
    ///   − Влияние −25% (FactionInfo).   − Органики не доверяют машинам: −10 к отношению с каждой органической империей.
    /// Черты применяются и к игроку, и к ИИ — по фракции владельца.
    /// </summary>
    public static class FactionTraits
    {
        public const float LivingArmorPerDay = 0.01f;
        public const float LivingArmorOwnPerDay = 0.02f;
        public const float EnergyVulnerability = 1.15f;
        public const float BuildTime = 1.2f;
        public const float StarbaseDiscount = 0.8f;
        public const float TreatyLength = 1.25f;
        public const float GardenArmor = 1.3f;
        public const float GardenSlow = 0.85f;
        public const int GardenDays = 3;

        private const float HullBonus = 0.15f, ShieldBonus = 0.10f, SurveyBonus = 0.20f, HyperPenalty = 0.10f;

        // «Ноль»
        public const float MachineDistrust = 10f;
        public const float RobotCostMult = 0.5f;
        /// <summary>Шанс в месяц, что на мире Консенсуса один чужой житель станет синтетом.</summary>
        public const float AssimilationChance = 1f / 12f;
        private const float MachineHull = 0.10f, MachineGrowthPenalty = 0.25f;

        private static readonly Dictionary<int, (FactionInfo f, string key)> _cache = new Dictionary<int, (FactionInfo, string)>();

        public static FactionInfo FactionOf(int owner)
            => owner == 0 ? UIManager.Instance?.SelectedFaction : AIEmpireManager.For(owner)?.Faction;

        private static string KeyOf(int owner)
        {
            if (owner < 0) return null;
            var f = FactionOf(owner);
            if (_cache.TryGetValue(owner, out var c) && ReferenceEquals(c.f, f)) return c.key;
            string k = FactionRegistry.KeyOf(f);
            _cache[owner] = (f, k);
            return k;
        }

        /// <summary>Ключ фракции владельца (astrea, xarn, aquila, iridia, terraan, nol).</summary>
        public static string Key(int owner) => KeyOf(owner);
        public static bool IsTerraan(int owner) => KeyOf(owner) == "terraan";
        public static bool IsMachine(int owner) => KeyOf(owner) == "nol";

        /// <summary>Стоимость сборки роботов (сплавы и гелий-3) для владельца.</summary>
        public static float RobotAlloys(int owner) => PlanetData.RobotAlloysCost * (IsMachine(owner) ? RobotCostMult : 1f);
        public static float RobotEnergy(int owner) => PlanetData.RobotEnergyCost * (IsMachine(owner) ? RobotCostMult : 1f);

        /// <summary>Кого собирают фабрики владельца: у Консенсуса — синтетов-основателей, у остальных — роботов.</summary>
        public static int AssembledSpecies(int owner) => IsMachine(owner) ? SpeciesRegistry.FounderOf(owner) : SpeciesRegistry.RobotId;

        /// <summary>Ассимиляция: раз в месяц на мире Консенсуса один чужой органик может стать синтетом.</summary>
        public static void TryAssimilate(PlanetData p)
        {
            if (p == null || p.Population <= 0 || !IsMachine(p.OwnerId)) return;
            if (UnityEngine.Random.value >= AssimilationChance) return;
            int founder = SpeciesRegistry.FounderOf(p.OwnerId);
            int from = p.DominantOrganic;
            var sp = SpeciesRegistry.Get(from);
            if (sp == null || sp.Machine || from == founder) return;
            if (p.ConvertPops(from, founder, 1) > 0 && p.OwnerId == 0)
                NotificationCenter.Show(Loc.T("Ассимиляция"), Loc.F("{0}: один из жителей ({1}) стал синтетом Консенсуса", p.Name, sp.InText),
                    NotificationCenter.Kind.Info, 5f);
        }

        public static float BuildTimeMult(int owner) => IsTerraan(owner) ? BuildTime : 1f;
        public static float OutpostDays(int owner) => GamePace.OutpostDays * BuildTimeMult(owner);
        public static float StarbaseAlloys(int owner) => FleetManager.StarbaseAlloysCost * (IsTerraan(owner) ? StarbaseDiscount : 1f);
        public static int VisionJumps(FleetData d) => d != null && d.Type == FleetType.Science && IsTerraan(d.OwnerId) ? 2 : 1;

        /// <summary>Множитель урона по кораблю владельца от оружия этого типа.</summary>
        public static float IncomingDamage(int owner, WeaponDamageType weapon)
            => weapon == WeaponDamageType.Energy && IsTerraan(owner) ? EnergyVulnerability : 1f;

        /// <summary>
        /// Привести бонусы империи в соответствие её фракции: снять черты прежней, наложить черты новой.
        /// Сдвиги аддитивные — технологии тоже прибавляют, поэтому снятие точное.
        /// </summary>
        public static void Sync(EmpireBonuses b, FactionInfo applied, FactionInfo wanted)
        {
            if (b == null || ReferenceEquals(applied, wanted)) return;
            if (FactionRegistry.IsTerraan(applied)) Shift(b, -1f);
            if (FactionRegistry.IsTerraan(wanted)) Shift(b, 1f);
            if (FactionRegistry.IsMachine(applied)) ShiftMachine(b, -1f);
            if (FactionRegistry.IsMachine(wanted)) ShiftMachine(b, 1f);
        }

        private static void ShiftMachine(EmpireBonuses b, float sign)
        {
            b.HullMult += MachineHull * sign;
            b.PopGrowth -= MachineGrowthPenalty * sign;
        }

        private static void Shift(EmpireBonuses b, float sign)
        {
            b.HullMult += HullBonus * sign;
            b.ShieldMult += ShieldBonus * sign;
            b.SurveySpeed += SurveyBonus * sign;
            b.HyperlaneSpeed -= HyperPenalty * sign;
        }
    }
}
