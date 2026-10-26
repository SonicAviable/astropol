using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Ground
{
    /// <summary>Одна сторона наземного боя в исходных данных: кто, сколько мест, бонусы, генерал.</summary>
    [Serializable]
    public class GroundSideSetup
    {
        public int Owner = -1;
        public string FactionKey;
        public Color Color = Color.white;
        /// <summary>Лимит мест (пехота — по бойцу, техника — 6).</summary>
        public int Slots = 40;
        /// <summary>Множители урона и прочности бойцов (из бонусов империи, черт генерала и вида).</summary>
        public float DamageMult = 1f, HpMult = 1f, SpeedMult = 1f, MoraleMult = 1f;
        /// <summary>Генерал: адмирал флота у атакующего, губернатор у защитника (−1 — нет).</summary>
        public int GeneralLeaderId = -1;
        public string GeneralName;
        public int GeneralLevel;
        /// <summary>Стартовые шевроны отрядов (ветеранский десант флота, 0…3).</summary>
        public int StartVeterancy;
        /// <summary>Мощь флота на орбите (для орбитальных ударов и автобоя).</summary>
        public float FleetPower;
        /// <summary>Бой ведёт игрок (иначе — тактический ИИ).</summary>
        public bool IsPlayer;
        public BloodKind Blood = BloodKind.Red;
    }

    /// <summary>Цвет крови вида: люди — красная, Ксарн — фиолетовая, Тэрра'ан — зелёная, машины — охлаждающая жидкость.</summary>
    public enum BloodKind { Red, Violet, Green, Coolant }

    public static class FactionBlood
    {
        /// <summary>Кровь бойцов фракции: Иридия — фиолетовая, Тэрра'ан — зелёный сок, «Ноль» — хладагент, остальные — красная.</summary>
        public static BloodKind Faction(string key) => key switch
        {
            "iridia" => BloodKind.Violet,
            "terraan" => BloodKind.Green,
            "nol" => BloodKind.Coolant,
            _ => BloodKind.Red
        };
    }

    /// <summary>Входные данные наземного боя. Собираются из стратегии (BuildFor) или для песочницы (Sandbox).</summary>
    [Serializable]
    public class GroundBattleSetup
    {
        public int SystemId = -1;
        public int PlanetIndex = -1;
        public string PlanetName = "";
        public PlanetType Planet = PlanetType.Continental;
        public int Fortification;
        public int Population;
        public float Stability = 60f;
        /// <summary>Районы колонии по типам (для раскладки карты и итогов).</summary>
        public int Urban, Mining, Generator, Industrial;
        public GroundSideSetup Attacker = new GroundSideSetup();
        public GroundSideSetup Defender = new GroundSideSetup();
        /// <summary>Стартовый гарнизон защитника, отрядов.</summary>
        public int GarrisonSquads = 2;
        public int Seed;
        public bool Sandbox;

        public GroundSideSetup Side(int i) => i == 0 ? Attacker : Defender;
    }

    /// <summary>Итоги наземного боя (ручного или автобоя).</summary>
    [Serializable]
    public class GroundBattleResult
    {
        public bool AttackerWon;
        public bool AutoResolved;
        public int AttackerLostMen, AttackerLostVehicles;
        public int DefenderLostMen, DefenderLostVehicles;
        /// <summary>Разрушенные районы по типам.</summary>
        public List<DistrictType> DestroyedDistricts = new List<DistrictType>();
        public int CiviliansKilled;
        public int VeteranSquads;
        public float Duration;
        /// <summary>Атакующий сдался или вышел из боя.</summary>
        public bool Retreated;
    }
}
