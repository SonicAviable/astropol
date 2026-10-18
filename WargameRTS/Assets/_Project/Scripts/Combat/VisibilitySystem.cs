using UnityEngine;
using WargameRTS.Core;
using WargameRTS.Map;
using WargameRTS.Units;

namespace WargameRTS.Combat
{
    /// <summary>
    /// Fog of war: a unit is visible to a team if any unit of that team has it
    /// inside its optics range (scaled by the target's concealment) and in line of sight.
    /// </summary>
    public sealed class VisibilitySystem : MonoBehaviour
    {
        [SerializeField] float updateInterval = 0.25f;
        [SerializeField] bool fogOfWar = true;

        float timer;

        public static VisibilitySystem Instance { get; private set; }

        public bool FogOfWar
        {
            get => fogOfWar;
            set
            {
                fogOfWar = value;
                ApplyToViews();
            }
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            timer -= Time.deltaTime;
            if (timer > 0f)
                return;

            timer = updateInterval;
            Recompute();
            ApplyToViews();
        }

        static void Recompute()
        {
            var units = Unit.All;
            for (int t = 0; t < units.Count; t++)
            {
                var target = units[t];
                for (int team = 0; team < TeamExtensions.Count; team++)
                {
                    var observerTeam = (Team)team;
                    if (observerTeam == target.Team)
                        continue;
                    target.SetVisibleTo(observerTeam, IsSpottedBy(observerTeam, target, units));
                }
            }
        }

        static bool IsSpottedBy(Team observerTeam, Unit target, System.Collections.Generic.IReadOnlyList<Unit> units)
        {
            float concealment = Concealment(target);
            Vector3 targetEye = target.EyePosition;
            for (int o = 0; o < units.Count; o++)
            {
                var observer = units[o];
                if (observer.Team != observerTeam)
                    continue;

                float range = observer.Definition.OpticsRange * concealment;
                if ((observer.Position - target.Position).sqrMagnitude > range * range)
                    continue;

                if (LineOfSight.Check(observer.EyePosition, targetEye))
                    return true;
            }
            return false;
        }

        /// <summary>Multiplier to enemy optics range: forests and towns hide, movement and firing reveal.</summary>
        public static float Concealment(Unit target)
        {
            float concealment = target.Definition.Concealment;
            var map = GameMap.Instance;
            if (map != null)
            {
                if (map.IsInForest(target.Position))
                    concealment *= 0.5f;
                else if (target.Definition.IsInfantry && map.IsInTown(target.Position))
                    concealment *= 0.6f;
            }

            if (target.IsMoving)
                concealment *= 1.25f;
            if (target.RecentlyFired)
                concealment = Mathf.Max(concealment, 0.9f);
            return Mathf.Min(concealment, 1f);
        }

        void ApplyToViews()
        {
            foreach (var unit in Unit.All)
                unit.View.SetVisible(!fogOfWar || unit.IsVisibleTo(TeamExtensions.Player));
        }
    }
}
