using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Отношения между империями ИИ: они спорят за границы, воюют и мирятся без участия игрока.
    ///   • Отношение складывается из характеров, длины общей границы, перекоса сил и старых обид.
    ///   • Воинственная империя нападает на соседа, если отношение ниже её порога и флот сильнее.
    ///   • Война идёт, пока одна из сторон не устанет (потери систем и кораблей ускоряют усталость),
    ///     затем — мир и перемирие на три года.
    /// Игрок узнаёт о войнах и мирах соседей из уведомлений и окна дипломатии.
    /// </summary>
    public static class AIRelations
    {
        public const int TruceLengthDays = 3 * 360;
        private const int InitialCooldownDays = 3 * 360;

        [Serializable]
        public class PairState
        {
            public int A, B;
            public bool AtWar;
            public int TruceDays;
            public int CooldownDays = InitialCooldownDays;
            public int WarMonths;
            public float WearA, WearB;
            public float Grudge;
        }

        private static readonly List<PairState> _pairs = new List<PairState>();

        public static void Reset()
        {
            _pairs.Clear();
            AICoalition.Reset();
        }

        private static PairState Get(int a, int b, bool create)
        {
            if (a == b) return null;
            int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
            foreach (var p in _pairs) if (p.A == lo && p.B == hi) return p;
            if (!create) return null;
            var n = new PairState { A = lo, B = hi };
            _pairs.Add(n);
            return n;
        }

        public static bool AtWar(int a, int b)
        {
            var p = Get(a, b, false);
            return p != null && p.AtWar;
        }

        public static int TruceDays(int a, int b) => Get(a, b, false)?.TruceDays ?? 0;

        /// <summary>Отношение одной империи ИИ к другой (симметричное), с разбором для подсказки.</summary>
        public static float Opinion(AIEmpireManager a, AIEmpireManager b, List<string> reasons = null)
        {
            if (a == null || b == null) return 0f;
            var p = Get(a.OwnerId, b.OwnerId, false);
            float v = 0f;
            void Add(string label, float x)
            {
                if (Mathf.Abs(x) < 0.5f) return;
                v += x;
                reasons?.Add($"{label} {x:+0;-0}");
            }

            Add("характеры", (a.Profile.BaseOpinion + b.Profile.BaseOpinion) * 0.5f + (a.Personality == b.Personality ? 5f : 0f));
            int border = EmpireStats.SharedBorderCount(a.OwnerId, b.OwnerId);
            if (border > 0) Add($"общая граница ({border})", -Mathf.Min(18f, border * 3f));
            float pa = Mathf.Max(1f, EmpireStats.MilitaryPower(a.OwnerId)), pb = Mathf.Max(1f, EmpireStats.MilitaryPower(b.OwnerId));
            if (pa / pb > 1.5f || pb / pa > 1.5f) Add("перекос сил", -6f);
            if (AICoalition.AreAllies(a.OwnerId, b.OwnerId)) Add("союзники по коалиции", 20f);
            else if (AICoalition.IsActive && (AICoalition.IsTarget(a.OwnerId) && AICoalition.IsMember(b.OwnerId)
                                              || AICoalition.IsTarget(b.OwnerId) && AICoalition.IsMember(a.OwnerId)))
                Add("коалиция против сильнейшего", -15f);
            if (p != null)
            {
                if (p.Grudge < -0.5f) Add("старые обиды", p.Grudge);
                if (p.AtWar) Add("война", -40f);
                else if (p.TruceDays > 0) Add("перемирие", 5f);
            }
            return Mathf.Clamp(v, -100f, 100f);
        }

        // ==================== ВРЕМЯ ====================

        public static void Tick(int day)
        {
            foreach (var p in _pairs)
            {
                if (p.TruceDays > 0) p.TruceDays--;
                if (p.CooldownDays > 0) p.CooldownDays--;
            }
            if (day == 1) Monthly();
        }

        private static void Monthly()
        {
            AICoalition.MonthlyUpdate();
            var alive = new List<AIEmpireManager>(AIEmpireManager.Alive);
            for (int i = 0; i < alive.Count; i++)
            for (int j = i + 1; j < alive.Count; j++)
            {
                var a = alive[i];
                var b = alive[j];
                var p = Get(a.OwnerId, b.OwnerId, true);
                p.Grudge *= 0.96f;

                // Союзники по коалиции мирятся и друг на друга не нападают
                if (AICoalition.AreAllies(a.OwnerId, b.OwnerId))
                {
                    if (p.AtWar) MakePeace(p, a, b);
                    continue;
                }

                if (p.AtWar)
                {
                    p.WarMonths++;
                    p.WearA = Mathf.Min(100f, p.WearA + 2.5f * a.Profile.WearinessRate);
                    p.WearB = Mathf.Min(100f, p.WearB + 2.5f * b.Profile.WearinessRate);
                    bool tired = p.WearA >= a.Profile.PeaceWeariness || p.WearB >= b.Profile.PeaceWeariness;
                    if ((tired && p.WarMonths >= 6 && UnityEngine.Random.value < 0.5f) || p.WarMonths >= 48)
                        MakePeace(p, a, b);
                    continue;
                }

                p.WearA = Mathf.Max(0f, p.WearA - 5f);
                p.WearB = Mathf.Max(0f, p.WearB - 5f);
                if (p.TruceDays > 0 || p.CooldownDays > 0) continue;
                if (EmpireStats.SharedBorderCount(a.OwnerId, b.OwnerId) == 0) continue;

                float opinion = Opinion(a, b);
                if (WantsWar(a, b, opinion)) StartWar(p, a, b);
                else if (WantsWar(b, a, opinion)) StartWar(p, b, a);
            }
        }

        private static bool WantsWar(AIEmpireManager attacker, AIEmpireManager target, float opinion)
        {
            if (attacker.IsBankrupt) return false;
            if (opinion > attacker.Profile.WarOpinionThreshold) return false;
            float ratio = EmpireStats.MilitaryPower(attacker.OwnerId) / Mathf.Max(1f, EmpireStats.MilitaryPower(target.OwnerId));
            float need = attacker.Profile.WarPowerRatio * (attacker.AtWar ? 1.5f : 1f);   // уже воюет с игроком — второй фронт осторожнее
            if (ratio < need) return false;
            return UnityEngine.Random.value < 0.3f;
        }

        /// <summary>Вступление в войну по обязательству коалиции: перемирие не мешает, если оно почти истекло.</summary>
        public static bool CoalitionWar(AIEmpireManager attacker, AIEmpireManager defender)
        {
            var p = Get(attacker.OwnerId, defender.OwnerId, true);
            if (p == null || p.AtWar || p.TruceDays > 360 || attacker.IsBankrupt) return false;
            p.TruceDays = 0;
            StartWar(p, attacker, defender);
            return true;
        }

        private static void StartWar(PairState p, AIEmpireManager attacker, AIEmpireManager defender)
        {
            p.AtWar = true;
            p.WarMonths = 0;
            p.WearA = p.WearB = 0f;
            NotificationCenter.Show("Война соседей", $"{attacker.AIName} объявляет войну империи {defender.AIName}",
                NotificationCenter.Kind.Warning, 8f);
            AIEmpireManager.NotifyDiplomacyChanged();
        }

        private static void MakePeace(PairState p, AIEmpireManager a, AIEmpireManager b)
        {
            p.AtWar = false;
            p.TruceDays = TruceLengthDays;
            p.CooldownDays = 2 * 360;
            p.Grudge = Mathf.Max(-40f, p.Grudge - 15f);
            SiegeManager.Instance?.ClearSieges(a.OwnerId, b.OwnerId);
            NotificationCenter.Show("Мир между соседями", $"{a.AIName} и {b.AIName} заключили мир. Перемирие — 3 года",
                NotificationCenter.Kind.Info, 7f);
            AIEmpireManager.NotifyDiplomacyChanged();
        }

        /// <summary>Потери в войне двух ИИ ускоряют усталость проигрывающей стороны.</summary>
        public static void AddWeariness(int loser, int winner, float amount)
        {
            var p = Get(loser, winner, false);
            if (p == null || !p.AtWar) return;
            if (p.A == loser) p.WearA = Mathf.Min(100f, p.WearA + amount);
            else p.WearB = Mathf.Min(100f, p.WearB + amount);
        }

        /// <summary>Империя пала — все её войны окончены.</summary>
        public static void EndAllWars(int owner)
        {
            foreach (var p in _pairs)
                if ((p.A == owner || p.B == owner) && p.AtWar) { p.AtWar = false; p.WarMonths = 0; }
        }

        // ==================== СОХРАНЕНИЕ ====================

        public static List<PairState> Capture()
        {
            var list = new List<PairState>();
            foreach (var p in _pairs)
                list.Add(new PairState
                {
                    A = p.A, B = p.B, AtWar = p.AtWar, TruceDays = p.TruceDays, CooldownDays = p.CooldownDays,
                    WarMonths = p.WarMonths, WearA = p.WearA, WearB = p.WearB, Grudge = p.Grudge
                });
            return list;
        }

        public static void Restore(List<PairState> list)
        {
            _pairs.Clear();
            if (list != null) _pairs.AddRange(list);
        }
    }
}
