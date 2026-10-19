using System.Collections.Generic;
using UnityEngine;
using WargameRTS.Units;

namespace WargameRTS.Campaign
{
    /// <summary>How a mission is won (see TZ section 5.3).</summary>
    public enum ObjectiveType
    {
        /// <summary>Own every sector at the same time.</summary>
        CaptureAll,
        /// <summary>Hold at least <see cref="MissionDefinition.RequiredSectors"/> when the timer runs out.</summary>
        Hold,
        /// <summary>Destroy every enemy unit and leave the enemy unable to buy another one.</summary>
        Eliminate
    }

    /// <summary>A number of units of one type in a starting army.</summary>
    public sealed class ArmyEntry
    {
        public readonly UnitDefinition Unit;
        public readonly int Count;

        public ArmyEntry(UnitDefinition unit, int count)
        {
            Unit = unit;
            Count = count;
        }
    }

    /// <summary>Static description of one campaign mission. Built in code by <see cref="CampaignCatalog"/>.</summary>
    public sealed class MissionDefinition
    {
        public string Title;
        public string Briefing;

        /// <summary>1..3 (shown as stars; sets how often the AI buys reinforcements).</summary>
        public int Difficulty = 1;

        public ObjectiveType Objective;
        public int SectorCount;

        /// <summary>Hold only: sectors the player must own when the timer ends.</summary>
        public int RequiredSectors;

        /// <summary>Hold only: length of the hold in seconds.</summary>
        public float HoldSeconds;

        public float MapSize = 2000f;

        /// <summary>Added to the campaign seed so every mission has its own map.</summary>
        public int SeedOffset;

        /// <summary>Place sectors on towns first (urban missions).</summary>
        public bool SectorsOnTowns;

        public int PlayerStartPoints;
        public int EnemyStartPoints;

        /// <summary>Command points per second for each owned sector.</summary>
        public float PlayerIncomePerSector = 1.2f;
        public float EnemyIncomePerSector = 1f;

        public List<ArmyEntry> PlayerArmy = new List<ArmyEntry>();
        public List<ArmyEntry> EnemyArmy = new List<ArmyEntry>();

        /// <summary>Units the player can buy as reinforcements in this mission.</summary>
        public List<UnitDefinition> PlayerPool = new List<UnitDefinition>();

        /// <summary>Units the AI can buy as reinforcements in this mission.</summary>
        public List<UnitDefinition> EnemyPool = new List<UnitDefinition>();

        public string ObjectiveText()
        {
            switch (Objective)
            {
                case ObjectiveType.CaptureAll:
                    return $"Захватить все сектора ({SectorCount})";
                case ObjectiveType.Hold:
                    return $"Удерживать {Mathf.RoundToInt(HoldSeconds / 60f)} мин — минимум {RequiredSectors} из {SectorCount} секторов";
                default:
                    return "Уничтожить все силы противника";
            }
        }

        public int ArmySize(List<ArmyEntry> army)
        {
            int total = 0;
            foreach (var entry in army)
                total += entry.Count;
            return total;
        }
    }
}
