using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Укрепление звёздной базы системы: форпост → укреплённая база → крепость → цитадель.
    /// Каждый уровень — стройка в очереди системы (как районы): база крепче и бьёт сильнее, осада идёт дольше,
    /// а содержание растёт. Захват снимает один уровень (часть укреплений разрушена), передача по договору — нет.
    /// Правила одинаковы для игрока и ИИ.
    /// </summary>
    public static class Fortifications
    {
        public const int MaxLevel = 3;
        /// <summary>Содержание гелием-3 за каждый уровень укрепления в месяц.</summary>
        public const float UpkeepPerLevel = 1f;

        public static string Name(int level) => level switch
        {
            1 => Loc.T("Укреплённая база"),
            2 => Loc.T("Крепость"),
            3 => Loc.T("Цитадель"),
            _ => Loc.T("Форпост")
        };

        /// <summary>Множитель корпуса, брони и щитов базы.</summary>
        public static float DurabilityMult(int level) => 1f + 0.75f * Mathf.Clamp(level, 0, MaxLevel);
        /// <summary>Множитель урона батарей базы.</summary>
        public static float DamageMult(int level) => 1f + 0.6f * Mathf.Clamp(level, 0, MaxLevel);
        /// <summary>Во сколько раз дольше осада.</summary>
        public static float SiegeMult(int level) => 1f + 0.25f * Mathf.Clamp(level, 0, MaxLevel);

        /// <summary>Цена перехода на уровень toLevel (1…3).</summary>
        public static float Minerals(int toLevel) => toLevel <= 1 ? 80f : toLevel == 2 ? 120f : 160f;
        public static float Alloys(int toLevel) => toLevel <= 1 ? 60f : toLevel == 2 ? 120f : 200f;
        public static float Days(int toLevel) => toLevel <= 1 ? 90f : toLevel == 2 ? 120f : 150f;

        /// <summary>Укрепление этой системы уже строится.</summary>
        public static ConstructionJob PendingJob(int systemId)
        {
            var cm = ConstructionManager.Instance;
            if (cm == null) return null;
            foreach (var j in cm.Jobs) if (j != null && j.Kind == JobKind.Fortify && j.SystemId == systemId) return j;
            return null;
        }

        /// <summary>Почему нельзя начать укрепление (null — можно; ресурсы проверяет вызывающий).</summary>
        public static string Blocker(StarSystem sys, int owner)
        {
            if (sys == null || sys.OwnerId != owner || !sys.HasStarbase) return Loc.T("Нужна ваша звёздная база");
            if (sys.Fortification >= MaxLevel) return Loc.T("Достигнут наивысший уровень — цитадель");
            if (PendingJob(sys.Id) != null) return Loc.T("Укрепление уже строится");
            var siege = SiegeManager.Instance?.GetSiege(sys.Id);
            if (siege != null && siege.Active) return Loc.T("Система в осаде — сначала прогоните врага");
            if (CombatManager.Instance?.GetBattle(sys.Id) != null) return Loc.T("В системе идёт бой");
            return null;
        }

        /// <summary>Заказ игрока: оплатить и поставить в очередь.</summary>
        public static bool TryStartPlayer(StarSystem sys)
        {
            if (Blocker(sys, 0) != null) return false;
            int to = sys.Fortification + 1;
            float m = Minerals(to), a = Alloys(to);
            var eco = EconomyManager.Instance;
            var cm = ConstructionManager.Instance;
            if (eco == null || cm == null || !eco.TrySpend(0f, m, a, 0f)) return false;
            cm.EnqueueSystemJob(JobKind.Fortify, 0, sys.Id, Days(to), 0f, m, a, 0f);
            eco.RecalculateAll();
            return true;
        }

        /// <summary>Стройка завершена: уровень растёт, база сразу получает новые параметры.</summary>
        public static void Complete(ConstructionJob j)
        {
            var sys = EmpireStats.GetSystem(j.SystemId);
            if (sys == null || sys.OwnerId != j.Owner || !sys.HasStarbase) return;
            sys.Fortification = Mathf.Min(MaxLevel, sys.Fortification + 1);
            CombatManager.Instance?.RefreshStarbase(sys.Id);
            if (j.Owner == 0)
                NotificationCenter.Show(Loc.T("Укрепление построено"), Loc.F("{0}: {1}", sys.Name, Name(sys.Fortification)),
                    NotificationCenter.Kind.Success, 5f);
        }

        /// <summary>Содержание укреплений владельца (гелий-3 в месяц).</summary>
        public static float Upkeep(int owner)
        {
            float s = 0f;
            foreach (var sys in EmpireStats.Systems)
                if (sys.OwnerId == owner && sys.HasStarbase) s += sys.Fortification * UpkeepPerLevel;
            return s;
        }
    }
}
