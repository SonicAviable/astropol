using System.Collections.Generic;
using UnityEngine;
using WargameRTS.Core;
using WargameRTS.Map;
using WargameRTS.Units;

namespace WargameRTS.Combat
{
    /// <summary>
    /// Artillery shells in flight: each lands after a delay proportional to distance
    /// and deals splash damage (no friendly fire) around the impact point.
    /// </summary>
    public sealed class ShellManager : MonoBehaviour
    {
        const float ShellSpeed = 450f;          // m/s, average along the arc
        const float ScatterPerMeter = 0.012f;   // scatter radius per meter of distance

        struct Shell
        {
            public Team Team;
            public Unit Shooter;
            public WeaponDefinition Weapon;
            public Vector3 Impact;
            public float LandsAt;
        }

        static ShellManager instance;
        readonly List<Shell> shells = new List<Shell>();

        void Awake() => instance = this;

        void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        /// <summary>Fires one shell from <paramref name="shooter"/> at <paramref name="aimPoint"/>.</summary>
        public static void Launch(Unit shooter, WeaponDefinition weapon, Vector3 aimPoint)
        {
            if (instance == null)
                return;

            float distance = Vector3.Distance(shooter.Position, aimPoint);
            float scatter = distance * ScatterPerMeter * (1.5f - weapon.Accuracy);
            Vector2 offset = Random.insideUnitCircle * scatter;
            Vector3 impact = aimPoint + new Vector3(offset.x, 0f, offset.y);
            var map = GameMap.Instance;
            if (map != null)
                impact = map.OnGround(impact);

            instance.shells.Add(new Shell
            {
                Team = shooter.Team,
                Shooter = shooter,
                Weapon = weapon,
                Impact = impact,
                LandsAt = Time.time + 1.5f + distance / ShellSpeed
            });

            EffectsManager.Explosion(shooter.View.MuzzlePosition, 3f);
        }

        void Update()
        {
            float now = Time.time;
            for (int i = shells.Count - 1; i >= 0; i--)
            {
                if (now < shells[i].LandsAt)
                    continue;
                var shell = shells[i];
                shells.RemoveAt(i);
                Detonate(shell);
            }
        }

        static readonly List<Unit> victims = new List<Unit>();

        static void Detonate(Shell shell)
        {
            float radius = Mathf.Max(1f, shell.Weapon.SplashRadius);
            EffectsManager.Explosion(shell.Impact + Vector3.up * 2f, radius * 0.6f);

            // Copy first: damage can kill units and modify Unit.All.
            victims.Clear();
            foreach (var unit in Unit.All)
            {
                if (unit.Team != shell.Team && (unit.Position - shell.Impact).sqrMagnitude <= radius * radius)
                    victims.Add(unit);
            }

            foreach (var unit in victims)
            {
                float falloff = 1f - Vector3.Distance(unit.Position, shell.Impact) / radius;
                float damage = CombatMath.Damage(shell.Weapon, unit.Definition.ArmorRear) * Mathf.Clamp01(falloff + 0.25f);
                if (CombatMath.IsInCover(unit))
                    damage *= CombatMath.CoverDamageMultiplier;

                if (shell.Shooter != null)
                    unit.NotifyShotAt(shell.Shooter);
                unit.ApplyDamage(damage);
            }
        }
    }
}
