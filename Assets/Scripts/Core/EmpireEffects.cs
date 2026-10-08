using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    [Serializable]
    public class TimedEffect
    {
        public string Id, Title, Detail;
        public int MonthsLeft;
        public float Energy, Minerals, Alloys, Influence, Science;
    }

    /// <summary>
    /// Временные эффекты империи игрока: законы, указы, контракты, видения. Каждый добавляет
    /// ежемесячный доход (или расход) на заданное число месяцев. Эффект вписывается в базовый доход
    /// при начале и вычитается при окончании, поэтому сохранённый доход уже учитывает действующие эффекты.
    /// </summary>
    public static class EmpireEffects
    {
        private static List<TimedEffect> _list = new List<TimedEffect>();

        public static IReadOnlyList<TimedEffect> Active => _list;

        public static void Reset() => _list = new List<TimedEffect>();

        public static bool Has(string id) => _list.Exists(e => e.Id == id);

        /// <summary>Начать эффект (тот же id заменяет прежний — срок обновляется).</summary>
        public static void Add(string id, string title, string detail, int months,
                               float energy = 0f, float minerals = 0f, float alloys = 0f, float influence = 0f, float science = 0f)
        {
            Remove(id, silent: true);
            var e = new TimedEffect
            {
                Id = id, Title = title, Detail = detail, MonthsLeft = Mathf.Max(1, months),
                Energy = energy, Minerals = minerals, Alloys = alloys, Influence = influence, Science = science
            };
            _list.Add(e);
            Apply(e, 1f);
        }

        public static void Remove(string id, bool silent = false)
        {
            int i = _list.FindIndex(e => e.Id == id);
            if (i < 0) return;
            var e = _list[i];
            _list.RemoveAt(i);
            Apply(e, -1f);
            if (!silent) NotificationCenter.Show("Срок действия истёк", e.Title, NotificationCenter.Kind.Info, 5f);
        }

        public static void MonthlyTick()
        {
            for (int i = _list.Count - 1; i >= 0; i--)
                if (--_list[i].MonthsLeft <= 0) Remove(_list[i].Id);
        }

        /// <summary>Краткая сводка доходов эффекта: «+5 энергии · −2 сплавов».</summary>
        public static string Summary(TimedEffect e)
        {
            var parts = new List<string>();
            void P(float v, string what) { if (Mathf.Abs(v) >= 0.05f) parts.Add($"{v:+0.#;−0.#} {what}"); }
            P(e.Energy, "энергии"); P(e.Minerals, "минералов"); P(e.Alloys, "сплавов");
            P(e.Influence, "влияния"); P(e.Science, "науки");
            return parts.Count > 0 ? string.Join("  ·  ", parts) + " в месяц" : "";
        }

        private static void Apply(TimedEffect e, float sign)
        {
            EconomyManager.Instance?.AddIncome(e.Energy * sign, e.Minerals * sign, e.Alloys * sign, e.Influence * sign);
            var tm = TechnologyManager.Instance;
            if (tm != null && Mathf.Abs(e.Science) > 0f)
            {
                float s = e.Science * sign;
                if (s > 0f) tm.AddMonthlyScience(s);
                else tm.RemoveMonthlyScience(-s);
            }
        }

        public static List<TimedEffect> Capture() => new List<TimedEffect>(_list);

        /// <summary>Доход в сохранении уже учитывает эффекты — только восстанавливаем сроки.</summary>
        public static void Restore(List<TimedEffect> list) => _list = list != null ? new List<TimedEffect>(list) : new List<TimedEffect>();
    }
}
