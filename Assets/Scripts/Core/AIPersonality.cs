using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    public enum AIPersonality { Militarist, Scientific, Trader, Mystic }

    /// <summary>
    /// Характер ИИ определяется фракцией: Ксарн — воинственный, Астрея — научная, Аквила — торговая, Иридия — мистическая.
    /// Профиль задаёт, на что тратятся ресурсы, что изучается в первую очередь и как ИИ ведёт себя в дипломатии.
    /// </summary>
    public class AIProfile
    {
        public AIPersonality Kind;
        public string Name;
        public string Summary;

        // --- Экономика ---
        /// <summary>Доля свободных сплавов, которая уходит на флот.</summary>
        public float FleetShare;
        /// <summary>Множитель науки (институты, культура).</summary>
        public float ScienceMult;
        /// <summary>Вес ценности колоний при выборе систем.</summary>
        public float ColonyWeight;
        /// <summary>Вес залежей при выборе систем.</summary>
        public float ResourceWeight;
        /// <summary>Отношение к соседству с игроком при экспансии (&lt;0 — избегает, &gt;0 — тянется к фронту).</summary>
        public float FrontierBias;
        /// <summary>Какой районы строить, когда базовые потребности закрыты.</summary>
        public DistrictType PreferredDistrict;

        // --- Военное дело ---
        /// <summary>Желаемая мощь флота относительно игрока.</summary>
        public float DesiredPowerRatio;
        public WeaponDamageType PreferredWeapon;
        public WeaponDamageType SecondaryWeapon;
        /// <summary>Во сколько раз врагу нужно превосходить группу, чтобы ИИ отступил.</summary>
        public float RetreatRatio;

        // --- Дипломатия ---
        public float BaseOpinion;
        public float ThreatSensitivity;
        /// <summary>Мнение, ниже которого ИИ готов объявить войну.</summary>
        public float WarOpinionThreshold;
        /// <summary>Перевес сил (ИИ/игрок), нужный для объявления войны.</summary>
        public float WarPowerRatio;
        /// <summary>Скорость накопления усталости от войны.</summary>
        public float WearinessRate;
        /// <summary>Усталость, при которой ИИ сам предлагает мир.</summary>
        public float PeaceWeariness;
        /// <summary>Сдвиг готовности принять мир.</summary>
        public float PeaceBias;
        /// <summary>Мнение, при котором ИИ согласен на пакт о ненападении.</summary>
        public float PactOpinion;
        public float TradeOpinionMult;
        public float GiftOpinionMult;

        // --- Наука ---
        public readonly Dictionary<TechCategory, float> TechWeights = new Dictionary<TechCategory, float>();

        public float TechWeight(TechCategory c) => TechWeights.TryGetValue(c, out var w) ? w : 1f;

        public static AIPersonality FromFactionName(string factionName)
        {
            if (string.IsNullOrEmpty(factionName)) return AIPersonality.Militarist;
            if (factionName.Contains("Ксарн")) return AIPersonality.Militarist;
            if (factionName.Contains("Астре")) return AIPersonality.Scientific;
            if (factionName.Contains("Аквил")) return AIPersonality.Trader;
            if (factionName.Contains("Ирид")) return AIPersonality.Mystic;
            return AIPersonality.Militarist;
        }

        public static AIProfile Get(AIPersonality kind)
        {
            var p = new AIProfile { Kind = kind };
            switch (kind)
            {
                case AIPersonality.Scientific:
                    p.Name = "Научная";
                    p.Summary = "Ценит знания и стабильность. Вооружается, только когда чувствует угрозу, охотно идёт на мир и пакты.";
                    p.FleetShare = 0.35f; p.ScienceMult = 1.35f;
                    p.ColonyWeight = 1.4f; p.ResourceWeight = 0.9f; p.FrontierBias = -4f;
                    p.PreferredDistrict = DistrictType.Urban;
                    p.DesiredPowerRatio = 0.85f; p.RetreatRatio = 1.15f;
                    p.PreferredWeapon = WeaponDamageType.Energy; p.SecondaryWeapon = WeaponDamageType.Energy;
                    p.BaseOpinion = 10f; p.ThreatSensitivity = 1.2f;
                    p.WarOpinionThreshold = -50f; p.WarPowerRatio = 1.6f;
                    p.WearinessRate = 1.6f; p.PeaceWeariness = 40f; p.PeaceBias = 10f;
                    p.PactOpinion = 5f; p.TradeOpinionMult = 1f; p.GiftOpinionMult = 1.1f;
                    p.TechWeights[TechCategory.Society] = 3f;
                    p.TechWeights[TechCategory.Sensors] = 2.5f;
                    p.TechWeights[TechCategory.Reactor] = 2f;
                    p.TechWeights[TechCategory.Defense] = 1.5f;
                    p.TechWeights[TechCategory.Colonization] = 1.8f;
                    break;

                case AIPersonality.Trader:
                    p.Name = "Торговая";
                    p.Summary = "Считает выгоду. Строит добычу и промышленность, ценит торговлю, воюет лишь при явном перевесе.";
                    p.FleetShare = 0.45f; p.ScienceMult = 1f;
                    p.ColonyWeight = 1f; p.ResourceWeight = 1.5f; p.FrontierBias = -2f;
                    p.PreferredDistrict = DistrictType.Mining;
                    p.DesiredPowerRatio = 0.95f; p.RetreatRatio = 1.2f;
                    p.PreferredWeapon = WeaponDamageType.Kinetic; p.SecondaryWeapon = WeaponDamageType.Energy;
                    p.BaseOpinion = 0f; p.ThreatSensitivity = 1f;
                    p.WarOpinionThreshold = -40f; p.WarPowerRatio = 1.35f;
                    p.WearinessRate = 1.3f; p.PeaceWeariness = 50f; p.PeaceBias = 5f;
                    p.PactOpinion = 15f; p.TradeOpinionMult = 1.6f; p.GiftOpinionMult = 1.25f;
                    p.TechWeights[TechCategory.Industry] = 3f;
                    p.TechWeights[TechCategory.Reactor] = 2.5f;
                    p.TechWeights[TechCategory.Construction] = 2f;
                    p.TechWeights[TechCategory.Propulsion] = 1.5f;
                    p.TechWeights[TechCategory.Colonization] = 2f;
                    break;

                case AIPersonality.Mystic:
                    p.Name = "Мистическая";
                    p.Summary = "Видит угрозы заранее. Держит крепкую оборону, сторонится чужих границ, редко начинает войну первой, но долго помнит обиды.";
                    p.FleetShare = 0.45f; p.ScienceMult = 1.2f;
                    p.ColonyWeight = 1.2f; p.ResourceWeight = 1f; p.FrontierBias = -3f;
                    p.PreferredDistrict = DistrictType.Generator;
                    p.DesiredPowerRatio = 1f; p.RetreatRatio = 1.3f;
                    p.PreferredWeapon = WeaponDamageType.Energy; p.SecondaryWeapon = WeaponDamageType.Explosive;
                    p.BaseOpinion = 0f; p.ThreatSensitivity = 1.4f;
                    p.WarOpinionThreshold = -45f; p.WarPowerRatio = 1.4f;
                    p.WearinessRate = 1.2f; p.PeaceWeariness = 55f; p.PeaceBias = 0f;
                    p.PactOpinion = 10f; p.TradeOpinionMult = 0.9f; p.GiftOpinionMult = 1.15f;
                    p.TechWeights[TechCategory.Sensors] = 3f;
                    p.TechWeights[TechCategory.Society] = 2.5f;
                    p.TechWeights[TechCategory.Defense] = 2.5f;
                    p.TechWeights[TechCategory.Reactor] = 1.8f;
                    p.TechWeights[TechCategory.Doctrine] = 1.5f;
                    break;

                default:
                    p.Name = "Воинственная";
                    p.Summary = "Уважает только силу. Держит большой флот, тянется к границе и нападает, едва почувствует перевес.";
                    p.FleetShare = 0.65f; p.ScienceMult = 0.9f;
                    p.ColonyWeight = 0.8f; p.ResourceWeight = 1.1f; p.FrontierBias = 2f;
                    p.PreferredDistrict = DistrictType.Industrial;
                    p.DesiredPowerRatio = 1.25f; p.RetreatRatio = 1.45f;
                    p.PreferredWeapon = WeaponDamageType.Explosive; p.SecondaryWeapon = WeaponDamageType.Kinetic;
                    p.BaseOpinion = -10f; p.ThreatSensitivity = 0.8f;
                    p.WarOpinionThreshold = -20f; p.WarPowerRatio = 1.05f;
                    p.WearinessRate = 1f; p.PeaceWeariness = 70f; p.PeaceBias = -15f;
                    p.PactOpinion = 30f; p.TradeOpinionMult = 0.5f; p.GiftOpinionMult = 0.8f;
                    p.TechWeights[TechCategory.Weapons] = 3f;
                    p.TechWeights[TechCategory.Defense] = 2.5f;
                    p.TechWeights[TechCategory.Construction] = 2f;
                    p.TechWeights[TechCategory.Propulsion] = 1.5f;
                    p.TechWeights[TechCategory.Doctrine] = 2.5f;
                    break;
            }
            return p;
        }
    }

    /// <summary>Одна строка разбора отношения ИИ к игроку.</summary>
    public struct OpinionModifier
    {
        public string Label;
        public string Detail;
        public float Value;
        public StellarisClone.Rendering.LGIcon Icon;

        public OpinionModifier(string label, string detail, float value, StellarisClone.Rendering.LGIcon icon)
        {
            Label = label; Detail = detail; Value = value; Icon = icon;
        }
    }

    /// <summary>
    /// Отношения сторон: война — состояние, а не порог мнения. Игрок воюет с каждым ИИ отдельно,
    /// империи ИИ — между собой (AIRelations). Неизвестные владельцы (будущие монстры) враждебны всем.
    /// </summary>
    public static class Diplomacy
    {
        public static bool AtWar(int a, int b)
        {
            if (a == b || a < 0 || b < 0) return false;
            if (a == 0 || b == 0)
            {
                var ai = AIEmpireManager.For(a == 0 ? b : a);
                return ai != null ? ai.AtWar : true;
            }
            if (AIEmpireManager.IsAI(a) && AIEmpireManager.IsAI(b)) return AIRelations.AtWar(a, b);
            return true;
        }
    }
}
