using System.Collections.Generic;
using UnityEngine;
using WargameRTS.Combat;

namespace WargameRTS.Units
{
    /// <summary>
    /// Sandbox unit roster created in code. Later these will become
    /// ScriptableObject assets under Assets/_Project/Data.
    /// Ranges are scaled down (~0.6) compared to Wargame to fit a 2 km map.
    /// </summary>
    public static class UnitCatalog
    {
        static List<UnitDefinition> all;

        public static UnitDefinition Tank { get; private set; }
        public static UnitDefinition Ifv { get; private set; }
        public static UnitDefinition Infantry { get; private set; }
        public static UnitDefinition Recon { get; private set; }
        public static UnitDefinition AntiTank { get; private set; }

        public static IReadOnlyList<UnitDefinition> All
        {
            get
            {
                EnsureCreated();
                return all;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => all = null;

        public static void EnsureCreated()
        {
            if (all != null)
                return;

            var shell = new Color(1f, 0.8f, 0.35f);
            var bullets = new Color(1f, 0.95f, 0.6f);
            var missile = new Color(1f, 0.5f, 0.2f);

            var tankGun = WeaponDefinition.Create("Пушка 120 мм", 1400f, 18, 2f, 0.55f, 0.35f, 6.5f, shell, 0.6f);
            var coaxMg = WeaponDefinition.Create("Пулемёт 7.62", 700f, 0, 0.35f, 0.35f, 0.6f, 1.2f, bullets, 0.2f);
            var autocannon = WeaponDefinition.Create("Автопушка 30 мм", 1050f, 5, 0.8f, 0.35f, 0.5f, 1.6f, shell, 0.35f);
            var atgm = WeaponDefinition.Create("ПТУР", 1800f, 22, 1f, 0.65f, 0f, 11f, missile, 0.5f);
            var rifles = WeaponDefinition.Create("Автоматы", 450f, 0, 0.4f, 0.4f, 0.25f, 1.1f, bullets, 0.15f);
            var rpg = WeaponDefinition.Create("Гранатомёт", 350f, 12, 1f, 0.4f, 0f, 9f, missile, 0.35f);
            var hmg = WeaponDefinition.Create("Пулемёт 12.7", 800f, 2, 0.5f, 0.35f, 0.5f, 0.9f, bullets, 0.25f);

            Tank = Create("Танк «Молот»", "ТНК", UnitCategory.Tank, 10f, 14, 4, 2, 55f, 70f, 1300f, 1f, tankGun, coaxMg);
            Ifv = Create("БМП «Шквал»", "БМП", UnitCategory.InfantryFightingVehicle, 10f, 4, 2, 1, 60f, 110f, 1300f, 1f, autocannon, coaxMg);
            Infantry = Create("Мотострелки", "ПЕХ", UnitCategory.Infantry, 10f, 0, 0, 0, 20f, 360f, 900f, 0.6f, rifles, rpg);
            Recon = Create("Разведка «Сова»", "РАЗ", UnitCategory.Recon, 6f, 1, 1, 1, 80f, 160f, 2000f, 0.55f, hmg);
            AntiTank = Create("ПТРК «Игла»", "ПТР", UnitCategory.AntiTank, 8f, 2, 1, 1, 60f, 110f, 1300f, 0.85f, atgm);

            all = new List<UnitDefinition> { Tank, Ifv, Infantry, Recon, AntiTank };
        }

        static UnitDefinition Create(string displayName, string shortName, UnitCategory category, float health,
            int front, int side, int rear, float speedKmh, float turnRate, float optics, float concealment,
            params WeaponDefinition[] weapons)
        {
            var unit = ScriptableObject.CreateInstance<UnitDefinition>();
            unit.name = displayName;
            unit.DisplayName = displayName;
            unit.ShortName = shortName;
            unit.Category = category;
            unit.MaxHealth = health;
            unit.ArmorFront = front;
            unit.ArmorSide = side;
            unit.ArmorRear = rear;
            unit.SpeedKmh = speedKmh;
            unit.TurnRate = turnRate;
            unit.OpticsRange = optics;
            unit.Concealment = concealment;
            unit.Weapons = new List<WeaponDefinition>(weapons);
            return unit;
        }
    }
}
