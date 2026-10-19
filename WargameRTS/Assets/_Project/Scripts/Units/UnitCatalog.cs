using System.Collections.Generic;
using UnityEngine;
using WargameRTS.Combat;

namespace WargameRTS.Units
{
    /// <summary>
    /// Unit roster created in code. Later these will become ScriptableObject
    /// assets under Assets/_Project/Data. Ranges are scaled down (~0.6) compared
    /// to Wargame to fit 2–3 km maps. Names are fictional.
    /// </summary>
    public static class UnitCatalog
    {
        static List<UnitDefinition> all;

        public static UnitDefinition Tank { get; private set; }
        public static UnitDefinition HeavyTank { get; private set; }
        public static UnitDefinition LightTank { get; private set; }
        public static UnitDefinition Ifv { get; private set; }
        public static UnitDefinition Apc { get; private set; }
        public static UnitDefinition Infantry { get; private set; }
        public static UnitDefinition SpecialForces { get; private set; }
        public static UnitDefinition Recon { get; private set; }
        public static UnitDefinition ReconTeam { get; private set; }
        public static UnitDefinition AntiTank { get; private set; }
        public static UnitDefinition AntiAir { get; private set; }
        public static UnitDefinition Howitzer { get; private set; }
        public static UnitDefinition RocketArtillery { get; private set; }

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

            // Direct fire                                   range  AP  HE    acc  move  interval
            var tankGun = WeaponDefinition.Create("Пушка 120 мм", 1400f, 18, 2f, 0.55f, 0.35f, 6.5f, shell, 0.6f);
            var heavyGun = WeaponDefinition.Create("Пушка 130 мм", 1500f, 21, 2.5f, 0.55f, 0.3f, 8f, shell, 0.7f);
            var lightGun = WeaponDefinition.Create("Пушка 105 мм", 1250f, 13, 1.6f, 0.5f, 0.45f, 5.5f, shell, 0.5f);
            var coaxMg = WeaponDefinition.Create("Пулемёт 7.62", 700f, 0, 0.35f, 0.35f, 0.6f, 1.2f, bullets, 0.2f);
            var autocannon = WeaponDefinition.Create("Автопушка 30 мм", 1050f, 5, 0.8f, 0.35f, 0.5f, 1.6f, shell, 0.35f);
            var twinCannon = WeaponDefinition.Create("Спарка 30 мм", 1200f, 4, 0.9f, 0.4f, 0.5f, 0.8f, shell, 0.35f);
            var atgm = WeaponDefinition.Create("ПТУР", 1800f, 22, 1f, 0.65f, 0f, 11f, missile, 0.5f);
            var rifles = WeaponDefinition.Create("Автоматы", 450f, 0, 0.4f, 0.4f, 0.25f, 1.1f, bullets, 0.15f);
            var sfRifles = WeaponDefinition.Create("Автоматы с ПБС", 500f, 0, 0.5f, 0.5f, 0.3f, 1f, bullets, 0.15f);
            var rpg = WeaponDefinition.Create("Гранатомёт", 350f, 12, 1f, 0.4f, 0f, 9f, missile, 0.35f);
            var manpat = WeaponDefinition.Create("Носимый ПТРК", 600f, 17, 1f, 0.55f, 0f, 12f, missile, 0.4f);
            var hmg = WeaponDefinition.Create("Пулемёт 12.7", 800f, 2, 0.5f, 0.35f, 0.5f, 0.9f, bullets, 0.25f);

            // Indirect fire
            var howitzerGun = WeaponDefinition.Create("Гаубица 152 мм", 3500f, 0, 4f, 0.5f, 0f, 9f, shell, 0f)
                .AsArtillery(minRange: 400f, splashRadius: 28f);
            var rockets = WeaponDefinition.Create("РСЗО 122 мм", 3200f, 0, 2.6f, 0.35f, 0f, 40f, missile, 0f)
                .AsArtillery(minRange: 500f, splashRadius: 22f, salvo: 12, salvoInterval: 0.18f);

            //                    name                 short  model                      category                              cost  hp  F   S  R  km/h turn optics conceal
            Tank = Create("Танк «Молот»", "ТНК", UnitModel.MediumTank, UnitCategory.Tank, 120, 10f, 14, 4, 2, 55f, 70f, 1300f, 1f, tankGun, coaxMg);
            HeavyTank = Create("Тяжёлый танк «Бастион»", "ТТ", UnitModel.HeavyTank, UnitCategory.Tank, 185, 12f, 19, 6, 3, 42f, 55f, 1300f, 1f, heavyGun, coaxMg);
            LightTank = Create("Лёгкий танк «Рысь»", "ЛТ", UnitModel.LightTank, UnitCategory.Tank, 75, 9f, 6, 2, 1, 72f, 100f, 1500f, 0.9f, lightGun, coaxMg);
            Ifv = Create("БМП «Шквал»", "БМП", UnitModel.TrackedIfv, UnitCategory.InfantryFightingVehicle, 60, 10f, 4, 2, 1, 60f, 110f, 1300f, 1f, autocannon, coaxMg);
            Apc = Create("БТР «Вепрь»", "БТР", UnitModel.WheeledApc, UnitCategory.InfantryFightingVehicle, 30, 9f, 2, 1, 1, 85f, 140f, 1200f, 1f, hmg);
            Infantry = Create("Мотострелки", "ПЕХ", UnitModel.InfantrySquad, UnitCategory.Infantry, 25, 10f, 0, 0, 0, 20f, 360f, 900f, 0.6f, rifles, rpg);
            SpecialForces = Create("Спецназ «Гроза»", "СПН", UnitModel.InfantryTeam, UnitCategory.Infantry, 55, 8f, 0, 0, 0, 24f, 360f, 1100f, 0.4f, sfRifles, manpat);
            Recon = Create("Разведка «Сова»", "РАЗ", UnitModel.ReconCar, UnitCategory.Recon, 35, 6f, 1, 1, 1, 80f, 160f, 2000f, 0.55f, hmg);
            ReconTeam = Create("Разведгруппа", "РГ", UnitModel.InfantryTeam, UnitCategory.Recon, 30, 6f, 0, 0, 0, 22f, 360f, 2200f, 0.35f, rifles);
            AntiTank = Create("ПТРК «Игла»", "ПТР", UnitModel.AtgmCarrier, UnitCategory.AntiTank, 70, 8f, 2, 1, 1, 60f, 110f, 1300f, 0.85f, atgm);
            AntiAir = Create("ЗСУ «Оса»", "ЗСУ", UnitModel.Spaag, UnitCategory.AntiAir, 65, 8f, 3, 2, 1, 60f, 110f, 1400f, 1f, twinCannon);
            Howitzer = Create("САУ «Гиацинт»", "САУ", UnitModel.Howitzer, UnitCategory.Artillery, 110, 8f, 3, 1, 1, 50f, 60f, 900f, 1f, howitzerGun);
            RocketArtillery = Create("РСЗО «Ливень»", "РСЗ", UnitModel.RocketLauncher, UnitCategory.Artillery, 150, 6f, 1, 1, 1, 70f, 90f, 900f, 1f, rockets);

            all = new List<UnitDefinition>
            {
                Tank, HeavyTank, LightTank, Ifv, Apc, Infantry, SpecialForces,
                Recon, ReconTeam, AntiTank, AntiAir, Howitzer, RocketArtillery
            };
        }

        static UnitDefinition Create(string displayName, string shortName, UnitModel model, UnitCategory category,
            int cost, float health, int front, int side, int rear, float speedKmh, float turnRate, float optics,
            float concealment, params WeaponDefinition[] weapons)
        {
            var unit = ScriptableObject.CreateInstance<UnitDefinition>();
            unit.name = displayName;
            unit.DisplayName = displayName;
            unit.ShortName = shortName;
            unit.Model = model;
            unit.Category = category;
            unit.Cost = cost;
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

        public static string CategoryName(UnitCategory category)
        {
            switch (category)
            {
                case UnitCategory.Tank: return "Танки";
                case UnitCategory.InfantryFightingVehicle: return "БМП/БТР";
                case UnitCategory.Infantry: return "Пехота";
                case UnitCategory.Recon: return "Разведка";
                case UnitCategory.AntiTank: return "ПТ";
                case UnitCategory.AntiAir: return "ПВО";
                case UnitCategory.Artillery: return "Артиллерия";
                default: return category.ToString();
            }
        }
    }
}
