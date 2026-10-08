using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    [Serializable]
    public class ExploredSave
    {
        public int Owner;
        public List<int> Systems = new List<int>();
    }

    /// <summary>
    /// Туман войны (как в Civilization VI) для игрока и для каждой империи ИИ:
    ///   • видно — системы рядом с собственными системами (столица — дальше) и флотами;
    ///     союзники по коалиции делятся видимостью;
    ///   • исследовано — всё, что хоть раз было видно; это помнится и сохраняется;
    ///   • чужие флоты видны только в видимых системах; неисследованные системы скрыты.
    /// Пересчёт — несколько раз в секунду (VisionTicker), рисование подписывается на OnPlayerChanged.
    /// В главном меню тумана нет.
    /// </summary>
    public static class Vision
    {
        /// <summary>Слой, на который убирают скрытые туманом объекты: его не рисует ни одна камера.</summary>
        public const int HiddenLayer = 30;

        private static readonly Dictionary<int, HashSet<int>> _visible = new Dictionary<int, HashSet<int>>();
        private static readonly Dictionary<int, HashSet<int>> _explored = new Dictionary<int, HashSet<int>>();
        private static int _playerVersion;

        /// <summary>Видимость или исследованность игрока изменилась — пора обновить карту.</summary>
        public static event Action OnPlayerChanged;

        /// <summary>Растёт при каждом изменении у игрока — для дешёвой проверки «пора ли перерисовать».</summary>
        public static int PlayerVersion => _playerVersion;

        public static bool Active => UIManager.IsGameStarted && !MenuAtmosphere.IsActive && EmpireStats.Systems.Count > 0;

        public static void Reset()
        {
            _visible.Clear();
            _explored.Clear();
            _playerVersion++;
        }

        private static HashSet<int> Set(Dictionary<int, HashSet<int>> d, int owner)
        {
            if (!d.TryGetValue(owner, out var s)) d[owner] = s = new HashSet<int>();
            return s;
        }

        public static bool IsVisible(int owner, int systemId)
        {
            if (!Active) return true;
            if (_visible.TryGetValue(owner, out var s) && s.Contains(systemId)) return true;
            var sys = EmpireStats.GetSystem(systemId);
            return sys != null && sys.OwnerId == owner && owner >= 0;
        }

        public static bool IsExplored(int owner, int systemId)
        {
            if (!Active) return true;
            if (_explored.TryGetValue(owner, out var s) && s.Contains(systemId)) return true;
            var sys = EmpireStats.GetSystem(systemId);
            return sys != null && sys.OwnerId == owner && owner >= 0;
        }

        public static bool PlayerSees(int systemId) => IsVisible(0, systemId);
        public static bool PlayerKnows(int systemId) => IsExplored(0, systemId);

        /// <summary>Видит ли наблюдатель этот флот: свой — всегда, чужой — если он в видимой системе (в пути — у видимого конца коридора).</summary>
        public static bool CanSeeFleet(int viewer, FleetData d)
        {
            if (d == null || d.Destroyed) return false;
            if (!Active || d.OwnerId == viewer) return true;
            if (d.State == FleetState.InHyperlane)
                return IsVisible(viewer, d.CurrentSystemId) || (d.TargetSystemId >= 0 && IsVisible(viewer, d.TargetSystemId));
            return IsVisible(viewer, d.CurrentSystemId);
        }

        /// <summary>Открыть систему (обмен звёздными картами, события).</summary>
        public static void MarkExplored(int owner, int systemId)
        {
            if (systemId < 0) return;
            if (Set(_explored, owner).Add(systemId) && owner == 0)
            {
                _playerVersion++;
                OnPlayerChanged?.Invoke();
            }
        }

        // ==================== ПЕРЕСЧЁТ ====================

        public static void Recompute()
        {
            if (!Active) return;
            var systems = EmpireStats.Systems;

            var owners = new List<int> { 0 };
            foreach (var ai in AIEmpireManager.All) if (!ai.IsEliminated) owners.Add(ai.OwnerId);

            var fresh = new Dictionary<int, HashSet<int>>();
            foreach (int o in owners) fresh[o] = new HashSet<int>();

            // Свои системы: сама система и соседи; столица — на два прыжка
            int playerCapital = EconomyManager.PlayerCapitalId;
            foreach (var s in systems)
            {
                if (s.OwnerId < 0 || !s.HasStarbase || !fresh.TryGetValue(s.OwnerId, out var set)) continue;
                bool capital = s.OwnerId == 0 ? s.Id == playerCapital : AIEmpireManager.For(s.OwnerId)?.CapitalSystemId == s.Id;
                Spread(set, s.Id, capital ? 2 : 1);
            }

            // Флоты: система, где флот стоит (в пути — оба конца коридора), и соседи
            var fm = FleetManager.Instance;
            if (fm != null)
                foreach (var f in fm.AllFleets)
                {
                    var d = f?.Data;
                    if (d == null || d.Destroyed || !fresh.TryGetValue(d.OwnerId, out var set)) continue;
                    if (d.CurrentSystemId >= 0) Spread(set, d.CurrentSystemId, 1);
                    if (d.State == FleetState.InHyperlane && d.TargetSystemId >= 0) Spread(set, d.TargetSystemId, 1);
                }

            // Союзники по коалиции делятся тем, что видят
            if (AICoalition.IsActive)
            {
                var allies = new List<int>();
                foreach (int o in owners) if (AICoalition.IsMember(o)) allies.Add(o);
                if (allies.Count > 1)
                {
                    var shared = new HashSet<int>();
                    foreach (int o in allies) shared.UnionWith(fresh[o]);
                    foreach (int o in allies) fresh[o].UnionWith(shared);
                }
            }

            bool playerChanged = false;
            foreach (var kv in fresh)
            {
                var old = Set(_visible, kv.Key);
                var explored = Set(_explored, kv.Key);
                int before = explored.Count;
                explored.UnionWith(kv.Value);
                if (kv.Key == 0 && (explored.Count != before || !old.SetEquals(kv.Value))) playerChanged = true;
                _visible[kv.Key] = kv.Value;
            }
            if (playerChanged)
            {
                _playerVersion++;
                OnPlayerChanged?.Invoke();
            }
        }

        private static void Spread(HashSet<int> set, int from, int jumps)
        {
            var systems = EmpireStats.Systems;
            if (from < 0 || from >= systems.Count) return;
            set.Add(from);
            if (jumps <= 0) return;
            foreach (int n in systems[from].ConnectedSystemIds)
            {
                if (n < 0 || n >= systems.Count) continue;
                if (jumps == 1) set.Add(n);
                else Spread(set, n, jumps - 1);
            }
        }

        // ==================== СОХРАНЕНИЕ ====================

        public static List<ExploredSave> Capture()
        {
            var list = new List<ExploredSave>();
            foreach (var kv in _explored) list.Add(new ExploredSave { Owner = kv.Key, Systems = new List<int>(kv.Value) });
            return list;
        }

        /// <summary>Старое сохранение без тумана: игрок знает то, что разведал, ИИ — то же.</summary>
        public static void Restore(List<ExploredSave> list)
        {
            Reset();
            if (list != null && list.Count > 0)
            {
                foreach (var e in list) if (e?.Systems != null) Set(_explored, e.Owner).UnionWith(e.Systems);
                return;
            }
            foreach (var s in EmpireStats.Systems)
            {
                if (s.IsSurveyedBy(0)) Set(_explored, 0).Add(s.Id);
                foreach (var ai in AIEmpireManager.All)
                    if (s.IsSurveyedBy(ai.OwnerId)) Set(_explored, ai.OwnerId).Add(s.Id);
            }
        }
    }

    /// <summary>Пересчитывает туман войны несколько раз в секунду и убирает скрытый слой со всех камер.</summary>
    public class VisionTicker : MonoBehaviour
    {
        private float _timer, _cameraTimer;

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if ((_cameraTimer -= dt) <= 0f)
            {
                _cameraTimer = 0.5f;
                int bit = 1 << Vision.HiddenLayer;
                foreach (var cam in Camera.allCameras)
                    if ((cam.cullingMask & bit) != 0) cam.cullingMask &= ~bit;
            }
            if ((_timer -= dt) > 0f) return;
            _timer = 0.2f;
            Vision.Recompute();
        }
    }
}
