using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Стратегия ИИ — как у живого игрока: война не «случается» по броску кубика, её планируют.
    ///   1. Выбор цели (раз в месяц, в мирное время): сосед, с которым есть счёты, и которого можно победить —
    ///      с учётом того, что он уже воюет с кем-то ещё (занят), обанкротился, сколько его колоний у нашей границы.
    ///      Цель — игрок или другая империя ИИ: правила одни.
    ///   2. Подготовка (AIDifficulty.WarPrepMonths): флот достраивается до нужного перевеса, собирается у границы
    ///      с целью; разведка игрока может заметить сбор войск (на низкой сложности — всегда).
    ///   3. Объявление: подготовка закончена, перевес есть, флот на месте. Если цель усилилась, отношения
    ///      наладились (подарки, договоры) или условия пропали — план отменяется.
    /// Пока идёт подготовка против одной империи, ИИ ищет пакт с остальными — прикрыть тыл.
    /// </summary>
    public partial class AIEmpireManager
    {
        /// <summary>Цель подготовки к войне: 0 — игрок, больше 0 — империя ИИ, −1 — плана нет.</summary>
        public int WarPlanTarget { get; private set; } = -1;
        public bool IsPreparingWar => WarPlanTarget >= 0;
        private int _warPlanMonths, _warPlanCooldown;
        private bool _warPlanWarned;

        /// <summary>Сколько лишних месяцев ждать готовности, прежде чем отказаться от плана.</summary>
        private const int PlanPatienceMonths = 10;
        /// <summary>Доля флота, которая должна стоять у границы перед объявлением войны.</summary>
        private const float PlanAssembled = 0.65f;

        private void MonthlyStrategy()
        {
            if (_warPlanCooldown > 0) _warPlanCooldown--;
            if (AtWarWithAnyone || _bankrupt || IsEliminated) { CancelWarPlan(false); return; }
            if (WarPlanTarget >= 0) { AdvanceWarPlan(); return; }
            if (_warPlanCooldown > 0 || _warCooldownDays > 0) return;
            if (YearsPlayed < AIDifficulty.PeaceYears) return;
            PickWarTarget();
        }

        private void CancelWarPlan(bool gaveUp)
        {
            if (WarPlanTarget < 0) return;
            WarPlanTarget = -1;
            _warPlanMonths = 0;
            _warPlanWarned = false;
            if (gaveUp) _warPlanCooldown = 12;
        }

        // ==================== ВЫБОР ЦЕЛИ ====================

        /// <summary>Можно ли вообще готовить войну против owner; opinion — отношение к нему.</summary>
        private bool CanTarget(int owner, out float opinion)
        {
            opinion = 0f;
            if (owner < 0 || owner == OwnerId || EmpireStats.SystemCount(owner) == 0) return false;
            if (owner == 0)
            {
                if (!Contacts.PlayerMet(OwnerId) || TruceDays > 0 || _warCooldownDays > 0) return false;
                if (YearsPlayed < AIDifficulty.PlayerWarMinYear) return false;
                if (AICoalition.AreAllies(0, OwnerId) || HasAlliance) return false;
                if (HasPact && !(Personality == AIPersonality.Militarist && Opinion <= -60f)) return false;
                opinion = Opinion;
            }
            else
            {
                var t = For(owner);
                if (t == null || t.IsEliminated || !Contacts.Met(OwnerId, owner)) return false;
                if (AICoalition.AreAllies(OwnerId, owner) || !AIRelations.CanDeclare(OwnerId, owner)) return false;
                opinion = AIRelations.Opinion(this, t);
            }
            // Нужна общая граница; воинственным достаточно соседства в 3 прыжках
            if (EmpireStats.SharedBorderCount(OwnerId, owner) > 0) return true;
            return Personality == AIPersonality.Militarist && NearOwner(owner, 3);
        }

        private bool NearOwner(int owner, int jumps)
        {
            foreach (var kv in DistancesFromOwner(owner, jumps))
            {
                var s = EmpireStats.GetSystem(kv.Key);
                if (s != null && s.OwnerId == OwnerId) return true;
            }
            return false;
        }

        /// <summary>Сила цели с поправкой на обстановку: занятый другой войной или банкрот слабее, чем кажется.</summary>
        private float EffectivePower(int owner)
        {
            float p = Mathf.Max(1f, EmpireStats.MilitaryPower(owner));
            bool busy = owner == 0 ? PlanetStability.AtWar(0) : For(owner) != null && For(owner).AtWarWithAnyone;
            if (busy) p *= 0.65f;
            if (owner == 0 ? EconomyManager.Instance != null && EconomyManager.Instance.IsBankrupt : For(owner) != null && For(owner).IsBankrupt) p *= 0.85f;
            return p;
        }

        /// <summary>Порог «счётов» для войны: чем хуже отношение, тем охотнее; на высокой сложности ИИ вспыльчивее.</summary>
        private float GrievanceLimit(int owner)
            => (owner == 0 ? Profile.WarOpinionThreshold / AggressionFactor : Profile.WarOpinionThreshold)
             + (Personality == AIPersonality.Militarist ? 15f : 5f) + (AIDifficulty.Tier >= 3 ? 5f : 0f);

        /// <summary>Сколько колоний цели прямо у нашей границы — добыча.</summary>
        private int BorderPrize(int owner)
        {
            int n = 0;
            foreach (var s in EmpireStats.Systems)
            {
                if (s.OwnerId != owner || !BordersOwn(s)) continue;
                foreach (var p in s.Planets) if (p.Population > 0) { n++; break; }
            }
            return n;
        }

        private float WarTargetScore(int owner)
        {
            if (!CanTarget(owner, out float opinion)) return 0f;
            float limit = GrievanceLimit(owner);
            if (opinion > limit) return 0f;

            float need = Profile.WarPowerRatio;
            float ratio = EmpireStats.MilitaryPower(OwnerId) / EffectivePower(owner);
            if (ratio < need * 0.75f) return 0f;   // даже с подготовкой не догнать

            float hate = Mathf.Clamp01((limit + 30f - opinion) / 60f);
            float score = Mathf.Min(ratio / need, 2f) * (0.6f + hate) * (1f + 0.08f * BorderPrize(owner));

            bool busy = owner == 0 ? PlanetStability.AtWar(0) : For(owner) != null && For(owner).AtWarWithAnyone;
            if (owner == 0) score *= AIDifficulty.PlayerTargetMult * (busy ? AIDifficulty.DogpileMult : 1f);
            else if (busy) score *= 1.2f;

            score *= Personality switch
            {
                AIPersonality.Militarist => 1.3f,
                AIPersonality.Trader => 0.8f,
                AIPersonality.Scientific => 0.85f,
                AIPersonality.Gardener => 0.8f,
                _ => 1f
            };
            return score * Random.Range(0.85f, 1.15f);
        }

        private void PickWarTarget()
        {
            int best = -1;
            float bestScore = 1f;
            foreach (int o in OtherEmpires())
            {
                float s = WarTargetScore(o);
                if (s > bestScore) { bestScore = s; best = o; }
            }
            if (best < 0) return;
            WarPlanTarget = best;
            _warPlanMonths = 0;
            _warPlanWarned = false;
        }

        // ==================== ПОДГОТОВКА ====================

        private void AdvanceWarPlan()
        {
            int target = WarPlanTarget;
            // Цель стала недоступна (пакт, союз, перемирие) или отношения наладились — отбой
            if (!CanTarget(target, out float opinion) || opinion > GrievanceLimit(target) + 25f) { CancelWarPlan(true); return; }
            _warPlanMonths++;

            if (target == 0 && !_warPlanWarned && _warPlanMonths >= 2)
            {
                _warPlanWarned = true;
                if (Random.value < AIDifficulty.TelegraphChance)
                    NotificationCenter.Show(Loc.T("Разведка: угроза войны"),
                        Loc.F("{0} стягивает флот к вашей границе и достраивает корабли. Похоже, готовится нападение", AIName),
                        NotificationCenter.Kind.Warning, 9f);
            }

            float ratio = EmpireStats.MilitaryPower(OwnerId) / EffectivePower(target);
            bool ready = _warPlanMonths >= AIDifficulty.WarPrepMonths && ratio >= Profile.WarPowerRatio && ArmyAssembled();
            if (ready) { LaunchPlannedWar(target, ratio); return; }
            if (_warPlanMonths > AIDifficulty.WarPrepMonths + PlanPatienceMonths) CancelWarPlan(true);
        }

        /// <summary>Флот стоит у границы с целью (точка сбора — StagingSystem, она смотрит на цель плана).</summary>
        private bool ArmyAssembled()
        {
            int staging = StagingSystem();
            float total = 0f, there = 0f;
            foreach (var f in OwnFleets(FleetType.Military))
            {
                float p = CombatMath.Power(f.Data);
                total += p;
                if (f.Data.CurrentSystemId == staging && IsIdle(f.Data)) there += p;
            }
            return total > 0f && there >= total * PlanAssembled;
        }

        private void LaunchPlannedWar(int target, float ratio)
        {
            WarPlanTarget = -1;
            _warPlanMonths = 0;
            if (target == 0) { DeclareWarOnPlayer(ratio); return; }
            var t = For(target);
            if (t != null) AIRelations.DeclarePlannedWar(this, t);
        }

        /// <summary>Желаемая мощь флота с учётом подготовки к войне: перевес над целью с запасом.</summary>
        private float PlannedWarPower()
            => WarPlanTarget >= 0 ? EffectivePower(WarPlanTarget) * Profile.WarPowerRatio * 1.1f : 0f;

        // ==================== СОХРАНЕНИЕ ====================

        private void CaptureStrategy(AISave s)
        {
            s.WarPlanTarget = WarPlanTarget;
            s.WarPlanMonths = _warPlanMonths;
            s.WarPlanCooldown = _warPlanCooldown;
            s.WarPlanWarned = _warPlanWarned;
        }

        private void RestoreStrategy(AISave s)
        {
            WarPlanTarget = s.WarPlanTarget;
            _warPlanMonths = s.WarPlanMonths;
            _warPlanCooldown = s.WarPlanCooldown;
            _warPlanWarned = s.WarPlanWarned;
        }
    }
}
