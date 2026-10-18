using System.Collections.Generic;
using UnityEngine;
using WargameRTS.Core;
using WargameRTS.Map;
using WargameRTS.Selection;
using WargameRTS.Units;

namespace WargameRTS.AI
{
    /// <summary>
    /// Placeholder opponent: holds for a while, then periodically sends small
    /// groups of idle units forward (attack-move) towards spotted enemies or the map center.
    /// </summary>
    public sealed class SimpleEnemyAI : MonoBehaviour
    {
        [SerializeField] Team team = Team.Red;
        [SerializeField] float initialDelay = 25f;
        [SerializeField] float waveInterval = 18f;
        [SerializeField] int minGroupSize = 3;
        [SerializeField] int maxGroupSize = 6;

        readonly List<Unit> idle = new List<Unit>();
        float timer;

        public void Configure(Team aiTeam) => team = aiTeam;

        void Start() => timer = initialDelay;

        void Update()
        {
            timer -= Time.deltaTime;
            if (timer > 0f)
                return;
            timer = waveInterval;
            SendWave();
        }

        void SendWave()
        {
            idle.Clear();
            foreach (var unit in Unit.All)
            {
                if (unit.Team == team && unit.Order == OrderType.None && unit.CurrentTarget == null)
                    idle.Add(unit);
            }
            if (idle.Count == 0)
                return;

            Vector3 objective = PickObjective();

            // Closest idle units to the objective go first.
            idle.Sort((a, b) => (a.Position - objective).sqrMagnitude.CompareTo((b.Position - objective).sqrMagnitude));
            int groupSize = Mathf.Min(idle.Count, Random.Range(minGroupSize, maxGroupSize + 1));
            var group = idle.GetRange(0, groupSize);

            Vector3 facing = objective - FormationPlanner.Centroid(group);
            facing.y = 0f;
            if (facing.sqrMagnitude < 1f)
                facing = Vector3.forward;
            facing.Normalize();

            foreach (var slot in FormationPlanner.AroundPoint(group, objective, facing))
                slot.Unit.MoveTo(slot.Position, facing, attackMove: true);
        }

        Vector3 PickObjective()
        {
            var enemy = team.Enemy();
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (var unit in Unit.All)
            {
                if (unit.Team != enemy || !unit.IsVisibleTo(team))
                    continue;
                sum += unit.Position;
                count++;
            }

            if (count > 0)
                return sum / count;

            // Nothing spotted: advance to a random point around the map center.
            var map = GameMap.Instance;
            float spread = map != null ? map.Size * 0.2f : 300f;
            var point = new Vector3(Random.Range(-spread, spread), 0f, Random.Range(-spread, spread));
            return map != null ? map.OnGround(point) : point;
        }
    }
}
