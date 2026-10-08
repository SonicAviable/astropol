namespace StellarisClone.Core
{
    /// <summary>
    /// Темп игры — все «ритмообразующие» длительности в одном месте.
    /// Ориентир — Stellaris: прыжок по гиперкоридору занимает около месяца, разведка системы —
    /// пару месяцев, форпост строится несколько месяцев. Один игровой день на скорости ×1 — секунда.
    /// </summary>
    public static class GamePace
    {
        /// <summary>Реальных секунд на игровой день при скорости ×1 (×2 — вдвое быстрее, ×4 — вчетверо).</summary>
        public const float SecondsPerDay = 1.0f;

        /// <summary>Базовый прыжок между соседними системами, дней (технологии, двигатели и адмиралы ускоряют).</summary>
        public const float JumpDays = 30f;

        /// <summary>Разведка системы научным кораблём, дней (ускоряют технологии и учёный).</summary>
        public const float SurveyDays = 75f;

        /// <summary>Постройка форпоста строительным кораблём, дней.</summary>
        public const float OutpostDays = 120f;

        /// <summary>Множитель длительности осады систем.</summary>
        public const float SiegeMult = 2f;

        /// <summary>Постройка научного и строительного корабля, дней.</summary>
        public const float CivilianShipDays = 90f;
    }
}
