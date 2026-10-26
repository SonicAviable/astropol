using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Автобой — та же модель силы, что и в ручном бою, одной формулой с разбросом:
    ///   A = места × урон × генерал × ветераны;  D = (гарнизон + ополчение + укрепления) × 1.25 (защита дома);
    ///   шанс атакующего = A / (A + D) ± 10%; потери пропорциональны силе проигравшего.
    /// Чистая функция от Setup (случайность — через переданный System.Random), годится и для ИИ против ИИ.
    /// </summary>
    public static class GroundAutoResolve
    {
        /// <summary>Сила атакующего десанта.</summary>
        public static float AttackerStrength(GroundBattleSetup s)
        {
            var a = s.Attacker;
            float general = a.GeneralLeaderId >= 0 ? 1.1f + 0.03f * a.GeneralLevel : 1f;
            float vets = 1f + 0.05f * Mathf.Clamp(a.StartVeterancy, 0, 3);
            return a.Slots * 10f * a.DamageMult * Mathf.Sqrt(a.HpMult) * general * vets;
        }

        /// <summary>Сила обороны колонии.</summary>
        public static float DefenderStrength(GroundBattleSetup s)
        {
            var d = s.Defender;
            float garrison = s.GarrisonSquads * 6f * 10f;
            float militia = s.Urban * 4f * 6f;
            float forts = s.Fortification * 70f;
            float general = d.GeneralLeaderId >= 0 ? 1.1f + 0.03f * d.GeneralLevel : 1f;
            return (garrison + militia + forts) * d.DamageMult * Mathf.Sqrt(d.HpMult) * general * 1.25f;
        }

        public static float AttackerChance(GroundBattleSetup s)
        {
            float a = AttackerStrength(s), d = DefenderStrength(s);
            return a + d <= 0f ? 0.5f : a / (a + d);
        }

        public static GroundBattleResult Resolve(GroundBattleSetup s, System.Random rng)
        {
            rng ??= new System.Random(s.Seed);
            float a = AttackerStrength(s), d = DefenderStrength(s);
            float chance = Mathf.Clamp01(AttackerChance(s) + ((float)rng.NextDouble() * 0.2f - 0.1f));
            bool won = rng.NextDouble() < chance;
            var r = new GroundBattleResult { AttackerWon = won, AutoResolved = true };

            // Потери: проигравший теряет большую часть, победитель — по соотношению сил
            float atkMen = s.Attacker.Slots, defMen = s.GarrisonSquads * 6f + s.Urban * 4f;
            float ratio = d / Mathf.Max(1f, a + d);
            float atkLoss = won ? Mathf.Lerp(0.15f, 0.6f, ratio) : Mathf.Lerp(0.6f, 0.95f, ratio);
            float defLoss = won ? Mathf.Lerp(0.95f, 0.7f, ratio) : Mathf.Lerp(0.3f, 0.65f, 1f - ratio);
            r.AttackerLostMen = Mathf.RoundToInt(atkMen * atkLoss);
            r.AttackerLostVehicles = Mathf.RoundToInt(atkMen / 12f * atkLoss * (float)rng.NextDouble());
            r.DefenderLostMen = Mathf.RoundToInt(defMen * defLoss);
            r.DefenderLostVehicles = s.Industrial > 0 ? Mathf.RoundToInt(s.Industrial * defLoss) : 0;

            // Разрушения и погибшие жители: чем упорнее бой, тем больше
            float fury = 0.5f + 0.5f * (1f - Mathf.Abs(chance - 0.5f) * 2f);
            void Ruin(DistrictType t, int n)
            {
                int lose = Mathf.FloorToInt(n * 0.5f * fury * (float)rng.NextDouble());
                for (int i = 0; i < lose; i++) r.DestroyedDistricts.Add(t);
            }
            Ruin(DistrictType.Urban, s.Urban);
            Ruin(DistrictType.Industrial, s.Industrial);
            Ruin(DistrictType.Generator, s.Generator);
            Ruin(DistrictType.Mining, s.Mining);
            r.CiviliansKilled = Mathf.Clamp(Mathf.RoundToInt(s.Population * 0.25f * fury * (float)rng.NextDouble()), 0, Mathf.Max(0, s.Population - 1));
            r.VeteranSquads = won ? Mathf.Clamp(Mathf.RoundToInt(s.Attacker.Slots / 20f), 0, 3) : 0;
            r.Duration = 0f;
            return r;
        }
    }
}
