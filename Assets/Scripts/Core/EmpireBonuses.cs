using System;
using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Накопленные бонусы технологий одной империи. У игрока — TechnologyManager.Bonuses,
    /// у ИИ — AIEmpireManager.Bonuses. Боевые множители урона/точности/уклонения считаются
    /// в момент выстрела, прочность (корпус/броня/щиты) «запекается» в корабль при постройке
    /// и пересчитывается у всего флота, когда изучена новая технология.
    /// </summary>
    [Serializable]
    public class EmpireBonuses
    {
        // Оружие
        public float KineticDamage = 1f;
        public float EnergyDamage = 1f;
        public float AllDamage = 1f;
        /// <summary>Точность: на столько пунктов уменьшается уклонение цели.</summary>
        public float Accuracy;

        // Защита
        public float HullMult = 1f;
        public float ArmorMult = 1f;
        public float ShieldMult = 1f;
        public float EvasionMult = 1f;

        // Перемещение и разведка
        public float HyperlaneSpeed = 1f;
        public float SurveySpeed = 1f;

        // Экономика
        public float MineralsMult = 1f;
        public float AlloysMult = 1f;
        public float EnergyFlat;
        public float ResearchMult = 1f;
        public float ShipCostMult = 1f;
        public float OutpostInfluenceDiscount;
        public bool DestroyerUnlocked;

        public float DamageMult(WeaponDamageType t)
        {
            float k = t == WeaponDamageType.Kinetic ? KineticDamage
                    : t == WeaponDamageType.Energy ? EnergyDamage
                    : 1f;
            return AllDamage * k;
        }

        public EmpireBonuses Clone() => (EmpireBonuses)MemberwiseClone();

        /// <summary>Бонусы владельца флота/системы (0 — игрок, иначе ИИ).</summary>
        public static EmpireBonuses For(int ownerId)
        {
            if (ownerId == 0) return TechnologyManager.Instance != null ? TechnologyManager.Instance.Bonuses : Neutral;
            var ai = AIEmpireManager.For(ownerId);
            return ai != null ? ai.Bonuses : Neutral;
        }

        private static readonly EmpireBonuses Neutral = new EmpireBonuses();
    }

    /// <summary>Что даёт каждая технология. Общая таблица для игрока и ИИ.</summary>
    public static class TechEffects
    {
        /// <summary>Применить бонус. Возвращает true, если технология даёт слот исследования.</summary>
        public static bool Apply(EmpireBonuses b, string key)
        {
            switch (key)
            {
                case "weap_kin_1": b.KineticDamage += 0.10f; break;
                case "weap_kin_2": b.KineticDamage += 0.15f; break;
                case "weap_las_1": b.EnergyDamage += 0.10f; b.Accuracy += 5f; break;
                case "weap_las_2": b.EnergyDamage += 0.15f; b.Accuracy += 5f; break;
                case "weap_kin_3": b.KineticDamage += 0.20f; break;
                case "weap_las_3": b.EnergyDamage += 0.20f; b.Accuracy += 5f; break;
                case "weap_pls_1": b.AllDamage += 0.20f; break;
                case "weap_lnc_1": b.AllDamage += 0.25f; break;

                case "def_arm_1": b.HullMult += 0.10f; b.ArmorMult += 0.10f; break;
                case "def_arm_2": b.HullMult += 0.15f; b.ArmorMult += 0.15f; break;
                case "def_shl_1": b.ShieldMult += 0.10f; break;
                case "def_shl_2": b.ShieldMult += 0.15f; break;
                case "def_arm_3": b.HullMult += 0.20f; b.ArmorMult += 0.20f; break;
                case "def_shl_3": b.ShieldMult += 0.20f; break;
                case "def_aeg_1": b.HullMult += 0.15f; b.ArmorMult += 0.15f; b.ShieldMult += 0.15f; break;

                case "prp_hyp_1": b.HyperlaneSpeed += 0.15f; break;
                case "prp_hyp_2": b.HyperlaneSpeed += 0.20f; break;
                case "prp_eng_1": b.EvasionMult += 0.10f; break;
                case "prp_fld_1": b.HyperlaneSpeed += 0.25f; break;
                case "prp_ion_1": b.EvasionMult += 0.10f; break;
                case "prp_inr_1": b.EvasionMult += 0.15f; break;

                case "sen_bas_1": b.Accuracy += 5f; b.SurveySpeed += 0.20f; break;
                case "sen_grv_1": b.Accuracy += 5f; b.SurveySpeed += 0.20f; break;
                case "sen_tac_1": b.Accuracy += 10f; break;
                case "sen_qc_1": b.ResearchMult += 0.20f; break;

                case "ind_min_1": b.MineralsMult += 0.20f; break;
                case "ind_min_2": b.MineralsMult += 0.15f; break;
                case "ind_min_3": b.MineralsMult += 0.20f; break;
                case "ind_all_1": b.AlloysMult += 0.20f; break;
                case "ind_all_2": b.AlloysMult += 0.15f; break;
                case "ind_all_3": b.AlloysMult += 0.20f; break;
                case "ind_meg_1": b.AlloysMult += 0.25f; b.MineralsMult += 0.15f; break;

                case "rct_fus_1": b.EnergyFlat += 4f; break;
                case "rct_fus_2": b.EnergyFlat += 8f; break;
                case "rct_ant_1": b.EnergyFlat += 12f; break;
                case "rct_zpe_1": b.EnergyFlat += 18f; break;
                case "rct_sc_1": b.EnergyFlat += 3f; b.ShieldMult += 0.10f; break;

                case "cns_sta_1": b.ShipCostMult -= 0.20f; break;
                case "cns_col_1": b.OutpostInfluenceDiscount += 10f; break;
                case "cns_dst_1": b.DestroyerUnlocked = true; break;
                case "cns_dck_1": b.ShipCostMult -= 0.10f; break;
                case "cns_mod_1": b.ShipCostMult -= 0.08f; break;

                case "soc_sci_1": b.ResearchMult += 0.15f; break;
                case "soc_sci_2": b.ResearchMult += 0.25f; break;
                case "soc_sci_3": b.ResearchMult += 0.20f; break;
                case "soc_sci_4": b.ResearchMult += 0.30f; break;
                case "soc_geo_1": b.SurveySpeed += 0.35f; break;

                case "col_xgeo_1": b.MineralsMult += 0.15f; b.SurveySpeed += 0.15f; break;
                case "col_hab_1": b.MineralsMult += 0.10f; b.AlloysMult += 0.10f; break;
                case "col_frt_1": b.OutpostInfluenceDiscount += 5f; b.HyperlaneSpeed += 0.10f; break;

                case "doc_flt_1": b.Accuracy += 5f; b.EvasionMult += 0.05f; break;
                case "doc_drl_1": b.AllDamage += 0.10f; break;
                case "doc_net_1": b.AllDamage += 0.10f; b.Accuracy += 5f; break;
                case "doc_log_1": b.ShipCostMult -= 0.05f; b.HyperlaneSpeed += 0.10f; break;

                case "slot+1": return true;
            }
            return false;
        }

        /// <summary>Технология меняет прочность кораблей — нужно пересчитать существующий флот.</summary>
        public static bool AffectsDurability(string key)
            => key == "def_arm_1" || key == "def_arm_2" || key == "def_arm_3" || key == "def_shl_1" || key == "def_shl_2"
            || key == "def_shl_3" || key == "def_aeg_1" || key == "rct_sc_1";
    }

    /// <summary>Боевая оценка кораблей с учётом технологий и текущих повреждений.</summary>
    public static class CombatMath
    {
        public static float Power(FleetData d)
        {
            if (d == null || d.Destroyed || d.Type != FleetType.Military) return 0f;
            var b = EmpireBonuses.For(d.OwnerId);
            float dps = 0f;
            foreach (var w in d.Weapons) dps += w.Dps * b.DamageMult(w.Type);
            return dps * 12f + d.HullPoints * 0.35f + d.ArmorPoints * 0.2f + d.ShieldPoints * 0.15f;
        }

        /// <summary>Применить множители прочности владельца к «чистым» значениям проекта.</summary>
        public static void ApplyDurability(FleetData d)
        {
            if (d == null || d.Type != FleetType.Military) return;
            var b = EmpireBonuses.For(d.OwnerId);
            d.MaxHullPoints = d.HullPoints = d.MaxHullPoints * b.HullMult;
            d.MaxArmorPoints = d.ArmorPoints = d.MaxArmorPoints * b.ArmorMult;
            d.MaxShieldPoints = d.ShieldPoints = d.MaxShieldPoints * b.ShieldMult;
        }

        /// <summary>Пересчитать прочность уже построенных кораблей после новой технологии.</summary>
        public static void RescaleDurability(FleetData d, EmpireBonuses before, EmpireBonuses after)
        {
            if (d == null || d.Type != FleetType.Military) return;
            float kh = after.HullMult / Mathf.Max(0.01f, before.HullMult);
            float ka = after.ArmorMult / Mathf.Max(0.01f, before.ArmorMult);
            float ks = after.ShieldMult / Mathf.Max(0.01f, before.ShieldMult);
            d.MaxHullPoints *= kh; d.HullPoints *= kh;
            d.MaxArmorPoints *= ka; d.ArmorPoints *= ka;
            d.MaxShieldPoints *= ks; d.ShieldPoints *= ks;
        }
    }
}
