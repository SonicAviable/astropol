using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Следит за узлами силы и сообщает игроку главное: «узел найден», «узел под вашим контролем»,
    /// «узел потерян» и «империя заняла узел». После загрузки партии прежнее состояние молча запоминается.
    /// </summary>
    public class LandmarkWatcher : MonoBehaviour
    {
        private readonly Dictionary<int, int> _holder = new Dictionary<int, int>();
        private readonly HashSet<int> _announced = new HashSet<int>();
        private bool _init;

        private void Start()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayPassed += HandleDay;
        }

        private void OnDestroy()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayPassed -= HandleDay;
        }

        private void HandleDay(int day, int month, int year)
        {
            if (!UIManager.IsGameStarted) return;
            var all = Landmarks.All;
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                bool known = Vision.PlayerKnows(s.Id) || s.OwnerId == 0;
                int holder = s.HasStarbase ? s.OwnerId : -1;

                if (!_init)
                {
                    _holder[s.Id] = holder;
                    if (known) _announced.Add(s.Id);
                    continue;
                }

                string name = Landmarks.Name(s.Landmark);
                if (known && _announced.Add(s.Id))
                    NotificationCenter.Show(Loc.T("Найден узел силы"), Loc.F("{0} · система {1}. {2}.", name, s.Name, Capital(Landmarks.Effect(s.Landmark))),
                        NotificationCenter.Kind.Success, 8f);

                _holder.TryGetValue(s.Id, out int prev);
                if (!_holder.ContainsKey(s.Id)) prev = -1;
                if (prev == holder) continue;
                _holder[s.Id] = holder;

                if (holder == 0)
                    NotificationCenter.Show(Loc.T("Узел силы под вашим контролем"), $"{name}: {Landmarks.Effect(s.Landmark)}.",
                        NotificationCenter.Kind.Success, 8f);
                else if (prev == 0)
                    NotificationCenter.Show(Loc.T("Узел силы потерян"), Loc.F("{0} перешёл к {1}. Бонус больше не действует.", name, AIEmpireManager.NameOf(holder, Loc.T("другой империи"))),
                        NotificationCenter.Kind.Danger, 9f);
                else if (holder > 0 && known)
                    NotificationCenter.Show(Loc.T("Узел силы занят"), Loc.F("{0} установила контроль над узлом «{1}».", AIEmpireManager.NameOf(holder, Loc.T("Империя")), name),
                        NotificationCenter.Kind.Warning, 7f);
            }
            _init = true;
        }

        private static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);
    }
}
