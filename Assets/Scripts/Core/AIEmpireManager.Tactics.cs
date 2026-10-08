using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Тактика ИИ. Умение зависит от сложности:
    ///   0 (лёгкая)     — прежнее поведение, оружие по вкусу фракции;
    ///   1 (нормальная) — разведка защиты врага, переход на оружие, которое её пробивает,
    ///                    оценка боя с учётом типов урона;
    ///   2 (сложная)    — то же и перехват вражеских флотов ещё в пути.
    /// </summary>
    public partial class AIEmpireManager
    {
        public int Smart => Mathf.Clamp(GameSession.Settings.Difficulty, 0, 2);

        public WeaponDamageType DesignPrimary { get; private set; } = WeaponDamageType.Energy;
        public WeaponDamageType DesignSecondary { get; private set; } = WeaponDamageType.Kinetic;

        /// <summary>Последняя смена тактики для подсказки в интерфейсе (пусто, пока всё по-старому).</summary>
        public string TacticsNote { get; private set; } = "";

        // Доли щита, брони и корпуса в боевых кораблях врага, какими ИИ их видел (сумма = 1)
        private AITactics.Defense _intel;
        private int _intelShips;
        private int _designCooldownMonths;

        private void ResetTactics()
        {
            DesignPrimary = Profile.PreferredWeapon;
            DesignSecondary = Profile.SecondaryWeapon;
            TacticsNote = "";
            _intel = default;
            _intelShips = 0;
            _designCooldownMonths = 0;
            _designs.Clear();
        }

        private void MonthlyTactics()
        {
            if (Smart < 1 || FleetManager.Instance == null) return;
            if (_designCooldownMonths > 0) _designCooldownMonths--;
            UpdateIntel();
            AdaptDesigns();
        }

        // ==================== РАЗВЕДДАННЫЕ ====================

        /// <summary>Что ИИ знает о защите врага: корабли в системах, которые он сам изучил или которыми владеет.</summary>
        private void UpdateIntel()
        {
            var seen = new List<FleetData>();
            int focus = FocusRival();
            foreach (var f in FleetManager.Instance.AllFleets)
            {
                var d = f?.Data;
                if (d == null || d.Destroyed || d.Type != FleetType.Military || d.OwnerId == OwnerId) continue;
                if (d.State == FleetState.InHyperlane) continue;
                if (!IsEnemy(d.OwnerId) && d.OwnerId != focus) continue;
                var s = EmpireStats.GetSystem(d.CurrentSystemId);
                if (s == null || !(s.OwnerId == OwnerId || s.IsSurveyedBy(OwnerId))) continue;
                seen.Add(d);
            }
            _intelShips = seen.Count;
            if (seen.Count == 0) return;

            var now = AITactics.DefenseOf(seen, false);
            float total = now.Total;
            if (total <= 0f) return;
            now = new AITactics.Defense { Shield = now.Shield / total, Armor = now.Armor / total, Hull = now.Hull / total };
            _intel = _intel.Total <= 0f ? now : AITactics.Defense.Lerp(_intel, now, 0.6f);
        }

        private float WeaponScore(WeaponDamageType t)
        {
            float pref = t == Profile.PreferredWeapon ? 1.08f : t == Profile.SecondaryWeapon ? 1.04f : 1f;
            return AITactics.Effectiveness(t, _intel) * Bonuses.DamageMult(t) * pref;
        }

        /// <summary>Новые корабли строятся под ту защиту, которую ИИ видит у врага (не чаще раза в полгода).</summary>
        private void AdaptDesigns()
        {
            if (_intelShips < 2 || _intel.Total <= 0f || _designCooldownMonths > 0) return;

            WeaponDamageType best = DesignPrimary;
            float bestScore = -1f;
            foreach (var t in AITactics.AllTypes)
            {
                float s = WeaponScore(t);
                if (s > bestScore) { bestScore = s; best = t; }
            }
            if (best == DesignPrimary || bestScore < WeaponScore(DesignPrimary) * 1.10f) return;

            // Запасное оружие — любимое оружие фракции, если оно не совпало с основным
            WeaponDamageType second = Profile.PreferredWeapon != best ? Profile.PreferredWeapon : Profile.SecondaryWeapon;
            if (second == best) second = best == WeaponDamageType.Energy ? WeaponDamageType.Kinetic : WeaponDamageType.Energy;

            DesignPrimary = best;
            DesignSecondary = second;
            _designs.Clear();
            _designCooldownMonths = 6;

            string why = best == WeaponDamageType.Explosive ? "у вас смешанная защита"
                       : _intel.Shield >= _intel.Armor ? "у вас много щитов" : "у вас много брони";
            string against = best == WeaponDamageType.Explosive ? "смешанной защиты"
                           : _intel.Shield >= _intel.Armor ? "щитов" : "брони";
            TacticsNote = $"Тактика: {AITactics.TypeShort(best)} против {against}";
            if (AtWar)
                NotificationCenter.Show("Враг меняет тактику", $"{AIName} переоснащает флот: {AITactics.TypeName(best)} оружие, потому что {why}",
                    NotificationCenter.Kind.Warning, 7f);
        }

        // ==================== ОЦЕНКА БОЯ ====================

        private static List<FleetData> DataOf(List<FleetView> ships)
        {
            var list = new List<FleetData>(ships.Count);
            foreach (var s in ships) if (s?.Data != null) list.Add(s.Data);
            return list;
        }

        /// <summary>Боевые флоты в системе: свои или всех врагов (на орбите, не в пути).</summary>
        private List<FleetData> MilitaryAt(int sysId, bool enemies)
        {
            var list = new List<FleetData>();
            var fm = FleetManager.Instance;
            if (fm == null) return list;
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d == null || d.Destroyed || d.Type != FleetType.Military) continue;
                if (d.State == FleetState.InHyperlane || d.CurrentSystemId != sysId) continue;
                if (enemies ? !IsEnemy(d.OwnerId) : d.OwnerId != OwnerId) continue;
                list.Add(d);
            }
            return list;
        }

        /// <summary>Поправка силы нашей группы против врагов в системе: оружие и защита «против друг друга».</summary>
        private float MatchupVs(IList<FleetData> mine, int sysId)
            => Smart >= 1 ? AITactics.MatchupFactor(mine, MilitaryAt(sysId, true)) : 1f;

        /// <summary>То же для боя, который уже идёт: все наши и все вражеские корабли в системе.</summary>
        private float MatchupAt(int sysId)
            => Smart >= 1 ? AITactics.MatchupFactor(MilitaryAt(sysId, false), MilitaryAt(sysId, true)) : 1f;

        // ==================== ПЕРЕХВАТ ====================

        /// <summary>Враги, летящие к нашим системам: самая ценная цель и их суммарная сила.</summary>
        private int FindIncoming(out float power, out List<FleetData> incoming)
        {
            power = 0f;
            incoming = new List<FleetData>();
            var fm = FleetManager.Instance;
            if (fm == null) return -1;

            var byDest = new Dictionary<int, List<FleetData>>();
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d == null || d.Destroyed || d.Type != FleetType.Military || !IsEnemy(d.OwnerId)) continue;
                if (d.State != FleetState.InHyperlane && d.Path.Count == 0) continue;
                int dest = FinalDestination(d);
                var s = EmpireStats.GetSystem(dest);
                if (s == null || s.OwnerId != OwnerId) continue;
                if (!byDest.TryGetValue(dest, out var l)) byDest[dest] = l = new List<FleetData>();
                l.Add(d);
            }

            int best = -1;
            float bestPriority = 0f;
            foreach (var kv in byDest)
            {
                var s = EmpireStats.GetSystem(kv.Key);
                float priority = 100f;
                foreach (var p in s.Planets) if (p.Population > 0) priority += 50f + p.Population * 5f;
                if (kv.Key == CapitalSystemId) priority += 300f;
                if (priority <= bestPriority) continue;
                bestPriority = priority;
                best = kv.Key;
                incoming = kv.Value;
            }
            if (best >= 0) foreach (var d in incoming) power += CombatMath.Power(d);
            return best;
        }

        // ==================== СОХРАНЕНИЕ ====================

        private void CaptureTactics(AISave s)
        {
            s.DesignPrimary = (int)DesignPrimary;
            s.DesignSecondary = (int)DesignSecondary;
            s.TacticsNote = TacticsNote;
        }

        private void RestoreTactics(AISave s)
        {
            if (s.DesignPrimary >= 0 && s.DesignPrimary <= 2) DesignPrimary = (WeaponDamageType)s.DesignPrimary;
            if (s.DesignSecondary >= 0 && s.DesignSecondary <= 2) DesignSecondary = (WeaponDamageType)s.DesignSecondary;
            TacticsNote = s.TacticsNote ?? "";
            _designs.Clear();
        }
    }
}
