using System.Collections.Generic;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Особенности фракций сверх доходов (FactionInfo). Сейчас — Хлорофиловый Конклав Тэрра'ан:
    ///   + Живая броня: корпус заживает 1% в день вне боя (2% в своих системах), поверх ремонта в доках.
    ///   + Сенсорная сеть мицелия: разведка +20%, научные корабли видят на 2 прыжка.
    ///   + Дипломатия корней: влияние +25% (FactionInfo), договоры на 25% дольше, союзники по коалиции делятся картой.
    ///   + Биокора: корпус +15%, щиты +10%.   + Живые форпосты: звёздная база на 20% дешевле по сплавам.
    ///   − Медленный рост: корабли и форпосты строятся на 20% дольше.   − Гиперскорость −10%.
    ///   − Энергетическое оружие бьёт по их кораблям на 15% сильнее.   − Минералы −15% (FactionInfo).
    ///   ★ Пробуждение сада: враг в системе Конклава — база на 3 дня получает +30% брони,
    ///     вражеские флоты уходят из системы на 15% медленнее (CombatManager).
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

        private static readonly Dictionary<int, (FactionInfo f, bool terraan)> _cache = new Dictionary<int, (FactionInfo, bool)>();

        public static FactionInfo FactionOf(int owner)
            => owner == 0 ? UIManager.Instance?.SelectedFaction : AIEmpireManager.For(owner)?.Faction;

        public static bool IsTerraan(int owner)
        {
            if (owner < 0) return false;
            var f = FactionOf(owner);
            if (_cache.TryGetValue(owner, out var c) && ReferenceEquals(c.f, f)) return c.terraan;
            bool t = FactionRegistry.IsTerraan(f);
            _cache[owner] = (f, t);
            return t;
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
