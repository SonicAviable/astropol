using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    [Serializable]
    public class ReputationSave
    {
        public float Value;
        public List<string> Labels = new List<string>();
        public List<float> Deltas = new List<float>();
        public List<int> Years = new List<int>();
    }

    /// <summary>
    /// Репутация игрока — «честное слово» империи, которое видят все знакомые соперники.
    ///   • Падает за вероломство: войну без повода, нарушенный пакт или союз, брошенного союзника, сепаратный мир.
    ///   • Растёт медленно — за годы соблюдённых пактов и союзов и за помощь союзникам.
    ///   • Плохая репутация со временем забывается (примерно наполовину за четыре года).
    /// Влияет на отношение всех знакомых империй, на премию, которую просят в сделках,
    /// на согласие на пакт и союз и на готовность соседей объединиться в коалицию против вас.
    /// </summary>
    public static class Reputation
    {
        public const float Min = -100f, Max = 40f;
        private const int HistoryLimit = 6;

        public static float Value { get; private set; }

        private struct Entry { public string Label; public float Delta; public int Year; }
        private static readonly List<Entry> _history = new List<Entry>();

        public static void Reset()
        {
            Value = 0f;
            _history.Clear();
        }

        /// <summary>Изменить репутацию и записать причину в историю (видна в подсказке).</summary>
        public static void Add(float delta, string reason)
        {
            if (Mathf.Abs(delta) < 0.01f) return;
            float before = Value;
            Value = Mathf.Clamp(Value + delta, Min, Max);
            int year = TimeManager.Instance != null ? TimeManager.Instance.Year : 0;
            _history.Insert(0, new Entry { Label = reason, Delta = delta, Year = year });
            if (_history.Count > HistoryLimit) _history.RemoveAt(_history.Count - 1);
            if (delta <= -5f)
                NotificationCenter.Show(Loc.T("Репутация пострадала"), Loc.F("{0}: {1:+0;-0}. Все знакомые империи это запомнят", reason, delta),
                    NotificationCenter.Kind.Warning, 7f);
            if (Label(before) != Label(Value)) AIEmpireManager.NotifyDiplomacyChanged();
        }

        /// <summary>Раз в месяц: дурная слава понемногу забывается, соблюдённые договоры копят доверие.</summary>
        public static void Monthly()
        {
            if (Value < 0f) Value = Mathf.Min(0f, Value * 0.985f + 0.05f);
            float grow = 0f;
            foreach (var ai in AIEmpireManager.Alive)
            {
                if (ai.AtWar || !Contacts.PlayerMet(ai.OwnerId)) continue;
                if (ai.HasAlliance) grow += 0.25f;
                else if (ai.HasPact) grow += 0.12f;
            }
            if (grow > 0f && Value < 25f) Value = Mathf.Min(25f, Value + grow);
        }

        public static string Label(float v) =>
              v >= 25f ? Loc.T("Безупречная")
            : v >= 10f ? Loc.T("Надёжная")
            : v > -10f ? Loc.T("Обычная")
            : v > -30f ? Loc.T("Сомнительная")
            : Loc.T("Вероломная");

        public static string Current => Label(Value);

        public static Color ColorOf(float v) =>
              v >= 10f ? new Color(0.44f, 0.88f, 0.60f)
            : v > -10f ? new Color(0.82f, 0.88f, 0.92f)
            : v > -30f ? new Color(1f, 0.70f, 0.35f)
            : new Color(1f, 0.45f, 0.45f);

        /// <summary>Слагаемое отношения каждой знакомой империи к игроку.</summary>
        public static float OpinionEffect => Value * 0.4f;

        /// <summary>Сколько дополнительной выгоды просят в сделках с империей дурной славы.</summary>
        public static float DealPremium => Mathf.Max(0f, -Value) * 0.15f;

        /// <summary>Подсказка: что такое репутация и последние события, которые на неё повлияли.</summary>
        public static string Tooltip()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Loc.F("<b>Репутация: {0} ({1:+0;-0;0})</b>\n", Current, Value));
            sb.Append(Loc.T("Её видят все знакомые империи: она меняет их отношение к вам, цену сделок и согласие на пакты и союзы.\n"));
            sb.Append(Loc.T("<color=#8AA2A8>Падает за войну без повода, нарушенный пакт, брошенного союзника. Растёт за соблюдённые договоры. Дурная слава со временем забывается.</color>"));
            if (_history.Count > 0)
            {
                sb.Append(Loc.T("\n\n<b>Последние события</b>"));
                foreach (var e in _history)
                {
                    string c = e.Delta >= 0f ? "#6FE09A" : "#FF8080";
                    sb.Append($"\n<color={c}>{e.Delta:+0;-0}</color>  {e.Label}");
                    if (e.Year > 0) sb.Append($" <color=#8AA2A8>· {e.Year}</color>");
                }
            }
            return sb.ToString();
        }

        // ==================== СОХРАНЕНИЕ ====================

        public static ReputationSave Capture()
        {
            var s = new ReputationSave { Value = Value };
            foreach (var e in _history) { s.Labels.Add(e.Label); s.Deltas.Add(e.Delta); s.Years.Add(e.Year); }
            return s;
        }

        public static void Restore(ReputationSave s)
        {
            Reset();
            if (s == null) return;
            Value = Mathf.Clamp(s.Value, Min, Max);
            int n = Mathf.Min(s.Labels?.Count ?? 0, Mathf.Min(s.Deltas?.Count ?? 0, s.Years?.Count ?? 0));
            for (int i = 0; i < n && i < HistoryLimit; i++)
                _history.Add(new Entry { Label = s.Labels[i], Delta = s.Deltas[i], Year = s.Years[i] });
        }
    }
}
