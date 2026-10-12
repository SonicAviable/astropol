using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Тактика в бою — решения, которые меняют ход сражения.
    ///   Стойка стороны:
    ///     • Равновесие — без бонусов и штрафов;
    ///     • Натиск — урон +20%, сближение вдвое короче, строй ближе к врагу; уклонение −25%, входящий урон +10%,
    ///       гиперпривод для отступления разгоняется на 15% дольше;
    ///     • Оборона — входящий урон −15%, уклонение +25%, строй дальше, ГПД разгоняется на 30% быстрее;
    ///       свой урон −15%, сближение на 30% дольше;
    ///     • Фокус огня — вся сторона бьёт одну цель: самую повреждённую из приоритетных. Быстрее выбивает
    ///       корабли (меньше вражеских орудий), но часть огня уходит «в перелёт».
    ///   Приоритет целей: любые · мелкие (корветы) · крупные (эсминцы, база) · звёздная база.
    /// Игрок выбирает на панели боя (выбор запоминается на следующие бои); ИИ — сам, раз в день боя:
    /// сильнее в полтора раза — натиск, слабее — оборона, есть подбитая цель — фокус.
    /// </summary>
    public partial class CombatManager
    {
        public enum Stance { Balanced, Assault, Defensive, Focus }
        public enum TargetPriority { Any, Small, Large, Starbase }
        public enum LogKind { Phase, Kill, Shield, Disengage, Tactic }

        public struct LogEntry
        {
            public string Text;
            public LogKind Kind;
            public int Owner;          // чей корабль/событие (-1 — общее)
            public float Time;         // Time.unscaledTime
        }

        private static Stance s_playerStance = Stance.Balanced;
        private static TargetPriority s_playerPriority = TargetPriority.Any;

        public static string StanceName(Stance s) => s switch
        {
            Stance.Assault => "Натиск",
            Stance.Defensive => "Оборона",
            Stance.Focus => "Фокус огня",
            _ => "Равновесие"
        };

        public static string StanceDesc(Stance s) => s switch
        {
            Stance.Assault => "Урон +20%, сближение вдвое короче, строй ближе к врагу.\nУклонение −25%, входящий урон +10%, ГПД разгоняется дольше.",
            Stance.Defensive => "Входящий урон −15%, уклонение +25%, ГПД для отступления разгоняется на 30% быстрее.\nСвой урон −15%, сближение дольше.",
            Stance.Focus => "Все корабли бьют одну цель — самую повреждённую из приоритетных.\nВражеские корабли выбывают быстрее, но часть огня уходит впустую.",
            _ => "Без бонусов и штрафов. Каждый корабль сам выбирает цель."
        };

        public static string PriorityName(TargetPriority p) => p switch
        {
            TargetPriority.Small => "Мелкие цели",
            TargetPriority.Large => "Крупные цели",
            TargetPriority.Starbase => "Звёздная база",
            _ => "Любые цели"
        };

        public static string PriorityDesc(TargetPriority p) => p switch
        {
            TargetPriority.Small => "Сначала корветы и фрегаты — быстро снижает число вражеских орудий.",
            TargetPriority.Large => "Сначала эсминцы и звёздная база — самые опасные цели.",
            TargetPriority.Starbase => "Всё на станцию: без неё нельзя начать осаду системы.",
            _ => "Цели по размеру: крупные чаще, но достаётся всем."
        };

        public static Stance StanceOf(Battle b, int owner)
            => b != null && b.Stances.TryGetValue(owner, out var s) ? s : Stance.Balanced;

        public static TargetPriority PriorityOf(Battle b, int owner)
            => b != null && b.Priorities.TryGetValue(owner, out var p) ? p : TargetPriority.Any;

        private static float StanceDamageMult(Battle b, int owner) => StanceOf(b, owner) switch
        {
            Stance.Assault => 1.20f, Stance.Defensive => 0.85f, _ => 1f
        };

        private static float StanceIncomingMult(Battle b, int owner) => StanceOf(b, owner) switch
        {
            Stance.Assault => 1.10f, Stance.Defensive => 0.85f, _ => 1f
        };

        private static float StanceEvasionMult(Battle b, int owner) => StanceOf(b, owner) switch
        {
            Stance.Assault => 0.75f, Stance.Defensive => 1.25f, _ => 1f
        };

        private static float StanceApproachMult(Battle b, int owner) => StanceOf(b, owner) switch
        {
            Stance.Assault => 0.5f, Stance.Defensive => 1.3f, _ => 1f
        };

        private static float StanceSpoolMult(Battle b, int owner) => StanceOf(b, owner) switch
        {
            Stance.Assault => 1.15f, Stance.Defensive => 0.70f, _ => 1f
        };

        private static float StanceRadiusMult(Battle b, int owner) => StanceOf(b, owner) switch
        {
            Stance.Assault => 0.8f, Stance.Defensive => 1.2f, _ => 1f
        };

        /// <summary>Множитель веса цели по приоритету стороны.</summary>
        private static float PriorityWeight(Combatant p, TargetPriority prio)
        {
            if (prio == TargetPriority.Any) return 1f;
            bool isBase = p.Fleet == null;
            var cls = isBase ? ShipClass.Destroyer : p.Fleet.Data.HullClass;
            bool civil = !isBase && p.Fleet.Data.Type != FleetType.Military;
            switch (prio)
            {
                case TargetPriority.Small: return civil ? 1f : isBase ? 0.4f : cls == ShipClass.Corvette ? 3f : cls == ShipClass.Frigate ? 1.6f : 0.6f;
                case TargetPriority.Large: return civil ? 0.5f : isBase ? 2.5f : cls == ShipClass.Destroyer ? 3f : cls == ShipClass.Frigate ? 1.4f : 0.5f;
                case TargetPriority.Starbase: return isBase ? 12f : 1f;
            }
            return 1f;
        }

        /// <summary>Общая цель стороны в «Фокусе огня»: прежняя, пока жива; иначе самая повреждённая с учётом приоритета.</summary>
        private Combatant? FocusPick(int owner, List<Combatant> parts, Battle battle)
        {
            if (battle.FocusTarget.TryGetValue(owner, out int key))
                foreach (var p in parts)
                    if (p.Key == key && p.Alive && Diplomacy.AtWar(owner, p.Owner)) return p;

            var prio = PriorityOf(battle, owner);
            Combatant? best = null;
            float bestScore = float.MaxValue;
            foreach (var p in parts)
            {
                if (!p.Alive || !Diplomacy.AtWar(owner, p.Owner)) continue;
                float integrity;
                if (p.Fleet != null)
                {
                    var d = p.Fleet.Data;
                    integrity = (d.HullPoints + d.ArmorPoints) / Mathf.Max(1f, d.MaxHullPoints + d.MaxArmorPoints);
                }
                else integrity = p.Base.Integrity;
                float score = (0.25f + integrity) / (TargetWeight(p) * PriorityWeight(p, prio));
                if (score < bestScore) { bestScore = score; best = p; }
            }
            if (best.HasValue) battle.FocusTarget[owner] = best.Value.Key;
            return best;
        }

        /// <summary>ИИ выбирает стойку раз в день боя по соотношению сил (игрок не трогается).</summary>
        private void ThinkAiTactics(Battle b)
        {
            foreach (var kv in b.Sides)
            {
                int owner = kv.Key;
                if (owner == 0) continue;
                var me = kv.Value;
                float enemyPower = 0f;
                bool weakTarget = false, enemyBase = false;
                foreach (var o in b.Sides)
                {
                    if (o.Key == owner || !Diplomacy.AtWar(owner, o.Key)) continue;
                    enemyPower += o.Value.Power;
                    if (o.Value.StartHp > 0f && o.Value.Hp / o.Value.StartHp < 0.55f) weakTarget = true;
                    if (o.Value.HasStarbase) enemyBase = true;
                }
                if (me.Ships == 0 && !me.HasStarbase) continue;
                float ratio = me.Power / Mathf.Max(1f, enemyPower);
                var stance = ratio >= 1.5f ? Stance.Assault
                           : ratio <= 0.7f ? Stance.Defensive
                           : weakTarget ? Stance.Focus
                           : Stance.Balanced;
                // Заметно сильнее и у врага есть база — давим станцию, чтобы начать осаду
                var prio = ratio >= 1.2f && enemyBase ? TargetPriority.Starbase : TargetPriority.Any;
                var old = StanceOf(b, owner);
                b.Stances[owner] = stance;
                b.Priorities[owner] = prio;
                if (old != stance && b.Days > 0.05f && b.PlayerInvolved)
                    AddLog(b, $"{AIEmpireManager.NameOf(owner, "Противник")}: {StanceName(stance).ToLower()}", LogKind.Tactic, owner);
            }
        }

        /// <summary>Тактика игрока в бою системы (запоминается для следующих боёв).</summary>
        public void SetPlayerStance(int systemId, Stance stance)
        {
            s_playerStance = stance;
            var b = GetBattle(systemId);
            if (b == null) return;
            if (StanceOf(b, 0) == stance) return;
            b.Stances[0] = stance;
            b.FocusTarget.Remove(0);
            ClearTargetsOf(0);
            AddLog(b, $"Приказ: {StanceName(stance).ToLower()}", LogKind.Tactic, 0);
        }

        public void SetPlayerPriority(int systemId, TargetPriority prio)
        {
            s_playerPriority = prio;
            var b = GetBattle(systemId);
            if (b == null) return;
            if (PriorityOf(b, 0) == prio) return;
            b.Priorities[0] = prio;
            b.FocusTarget.Remove(0);
            ClearTargetsOf(0);
            AddLog(b, $"Цели: {PriorityName(prio).ToLower()}", LogKind.Tactic, 0);
        }

        /// <summary>Корабли владельца забывают прежние цели — новый приказ действует сразу.</summary>
        private void ClearTargetsOf(int owner)
        {
            var drop = new List<int>();
            foreach (var kv in _targets)
            {
                if (kv.Key <= -1000) { if (_generator != null && _generator.Systems[-1000 - kv.Key].OwnerId == owner) drop.Add(kv.Key); continue; }
                if (_viewById.TryGetValue(kv.Key, out var fv) && fv?.Data != null && fv.Data.OwnerId == owner) drop.Add(kv.Key);
            }
            foreach (int k in drop) _targets.Remove(k);
        }

        private static void AddLog(Battle b, string text, LogKind kind, int owner = -1)
        {
            if (b == null || !b.PlayerInvolved) return;
            b.Log.Add(new LogEntry { Text = text, Kind = kind, Owner = owner, Time = Time.unscaledTime });
            if (b.Log.Count > 24) b.Log.RemoveAt(0);
        }
    }
}
