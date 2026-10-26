using System;
using System.Threading.Tasks;
using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Параллельный цикл для чистых расчётов (только чтение общих данных, запись — в свою ячейку массива).
    /// Внутри нельзя трогать объекты Unity, UnityEngine.Random, события и общие коллекции на запись —
    /// только математику над заранее скопированными массивами.
    /// Если потоки недоступны или что-то пошло не так — тот же цикл выполняется в главном потоке,
    /// результат одинаковый в обоих случаях.
    /// </summary>
    public static class Threaded
    {
        /// <summary>Меньше этого — накладные расходы на потоки дороже самой работы.</summary>
        private const int MinParallel = 64;

        private static bool s_disabled = Application.platform == RuntimePlatform.WebGLPlayer;

        public static bool Available => !s_disabled && SystemInfo.processorCount > 1;

        public static void For(int count, Action<int> body)
        {
            if (count <= 0) return;
            if (Available && count >= MinParallel)
            {
                try
                {
                    Parallel.For(0, count, body);
                    return;
                }
                catch (Exception e)
                {
                    // Тело чистое — повторный проход в главном потоке перезапишет все ячейки тем же результатом
                    s_disabled = true;
                    Debug.LogWarning("[Threaded] Параллельный расчёт не удался, дальше — в главном потоке: " + e.GetBaseException().Message);
                }
            }
            for (int i = 0; i < count; i++) body(i);
        }
    }
}
