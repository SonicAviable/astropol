using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using WargameRTS.Combat;
using WargameRTS.Core;
using Random = UnityEngine.Random;

namespace WargameRTS.Units
{
    public enum OrderType
    {
        None,
        Move,
        AttackMove,
        Attack
    }

    /// <summary>
    /// A controllable unit: health, orders, target selection and weapons.
    /// Movement is done by the NavMeshAgent, visuals by <see cref="UnitView"/>.
    /// </summary>
    public sealed class Unit : MonoBehaviour
    {
        const float TargetSearchInterval = 0.4f;
        const float RepathInterval = 1f;
        const float RevealAfterFiringSeconds = 3f;

        static readonly List<Unit> all = new List<Unit>();

        /// <summary>All living units.</summary>
        public static IReadOnlyList<Unit> All => all;

        public static event Action<Unit> Died;

        sealed class WeaponState
        {
            public WeaponDefinition Definition;
            public float Cooldown;
        }

        readonly bool[] visibleTo = new bool[TeamExtensions.Count];
        NavMeshAgent agent;
        UnitView view;
        WeaponState[] weapons = Array.Empty<WeaponState>();

        OrderType order;
        Unit attackTarget;
        Unit currentTarget;
        Vector3 destination;
        Vector3? facing;
        float targetSearchTimer;
        float repathTimer;
        float lastFiredTime = float.NegativeInfinity;

        public UnitDefinition Definition { get; private set; }
        public Team Team { get; private set; }
        public float Health { get; private set; }
        public bool IsAlive => Health > 0f;
        public bool IsSelected { get; private set; }
        public UnitView View => view;
        public OrderType Order => order;
        public Unit CurrentTarget => currentTarget;
        public Vector3 Destination => destination;
        public Vector3 Position => transform.position;
        public Vector3 EyePosition => transform.position + Vector3.up * (Definition.IsInfantry ? 1.6f : 2.6f);
        public bool IsMoving => agent != null && agent.enabled && agent.velocity.sqrMagnitude > 0.25f;
        public bool RecentlyFired => Time.time - lastFiredTime < RevealAfterFiringSeconds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            all.Clear();
            Died = null;
        }

        public void Initialize(UnitDefinition definition, Team team, NavMeshAgent navAgent, UnitView unitView, Vector3 initialFacing)
        {
            Definition = definition;
            Team = team;
            Health = definition.MaxHealth;
            agent = navAgent;
            view = unitView;
            facing = initialFacing;
            targetSearchTimer = Random.Range(0f, TargetSearchInterval);

            weapons = new WeaponState[definition.Weapons.Count];
            for (int i = 0; i < weapons.Length; i++)
            {
                var weapon = definition.Weapons[i];
                weapons[i] = new WeaponState
                {
                    Definition = weapon,
                    Cooldown = Random.Range(0f, weapon.FireInterval)
                };
            }
        }

        void OnEnable() => all.Add(this);

        void OnDisable() => all.Remove(this);

        // ------------------------------------------------------------------
        // Visibility & selection
        // ------------------------------------------------------------------

        public bool IsVisibleTo(Team observer) => observer == Team || visibleTo[(int)observer];

        public void SetVisibleTo(Team observer, bool visible) => visibleTo[(int)observer] = visible;

        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            view.SetSelected(selected);
        }

        // ------------------------------------------------------------------
        // Orders
        // ------------------------------------------------------------------

        public void MoveTo(Vector3 point, Vector3? faceDirection, bool attackMove)
        {
            if (!IsAlive || !agent.isOnNavMesh)
                return;

            order = attackMove ? OrderType.AttackMove : OrderType.Move;
            attackTarget = null;
            destination = point;
            facing = faceDirection;
            agent.isStopped = false;
            agent.SetDestination(point);
        }

        public void Attack(Unit target)
        {
            if (!IsAlive || target == null || target.Team == Team)
                return;

            order = OrderType.Attack;
            attackTarget = target;
            facing = null;
            repathTimer = 0f;
            targetSearchTimer = 0f;
        }

        public void Stop()
        {
            order = OrderType.None;
            attackTarget = null;
            facing = null;
            if (agent.isOnNavMesh)
            {
                agent.isStopped = false;
                agent.ResetPath();
            }
        }

        // ------------------------------------------------------------------
        // Simulation
        // ------------------------------------------------------------------

        void Update()
        {
            if (!IsAlive)
                return;

            float deltaTime = Time.deltaTime;
            UpdateTargeting(deltaTime);
            UpdateOrder(deltaTime);
            UpdateIdleRotation(deltaTime);
            UpdateWeapons(deltaTime);
        }

        void UpdateTargeting(float deltaTime)
        {
            if (currentTarget != null && !IsValidTarget(currentTarget))
                currentTarget = null;

            targetSearchTimer -= deltaTime;
            if (targetSearchTimer > 0f)
                return;
            targetSearchTimer = TargetSearchInterval;

            if (order == OrderType.Attack && IsValidTarget(attackTarget) && CanEngage(attackTarget))
            {
                currentTarget = attackTarget;
                return;
            }

            currentTarget = FindBestTarget();
        }

        void UpdateOrder(float deltaTime)
        {
            switch (order)
            {
                case OrderType.Move:
                case OrderType.AttackMove:
                    if (order == OrderType.AttackMove)
                        agent.isStopped = currentTarget != null;

                    if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.5f)
                    {
                        // Arrived: release the path so the unit can turn to its formation facing.
                        order = OrderType.None;
                        agent.isStopped = false;
                        agent.ResetPath();
                    }
                    break;

                case OrderType.Attack:
                    if (!IsValidTarget(attackTarget))
                    {
                        Stop();
                        break;
                    }

                    if (currentTarget == attackTarget)
                    {
                        if (agent.hasPath)
                            agent.ResetPath();
                    }
                    else
                    {
                        repathTimer -= deltaTime;
                        if (repathTimer <= 0f)
                        {
                            repathTimer = RepathInterval;
                            agent.isStopped = false;
                            agent.SetDestination(attackTarget.Position);
                        }
                    }
                    break;
            }
        }

        void UpdateIdleRotation(float deltaTime)
        {
            if (order != OrderType.None || !facing.HasValue || agent.hasPath)
                return;

            Vector3 direction = facing.Value;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f)
                return;

            var targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, Definition.TurnRate * deltaTime);
        }

        void UpdateWeapons(float deltaTime)
        {
            foreach (var weapon in weapons)
                weapon.Cooldown -= deltaTime;

            if (currentTarget == null)
            {
                view.ClearAim();
                return;
            }

            view.AimAt(currentTarget.Position);

            bool moving = IsMoving;
            float distance = Vector3.Distance(Position, currentTarget.Position);
            var armorFacing = CombatMath.GetFacing(currentTarget.transform, Position);
            int armor = CombatMath.GetArmor(currentTarget.Definition, armorFacing);

            foreach (var weapon in weapons)
            {
                var definition = weapon.Definition;
                if (weapon.Cooldown > 0f || distance > definition.Range)
                    continue;
                if (moving && !definition.CanFireOnMove)
                    continue;

                float damage = CombatMath.Damage(definition, armor);
                if (damage < 0.05f)
                    continue;

                Fire(weapon, currentTarget, distance, damage);
                if (!currentTarget.IsAlive)
                {
                    currentTarget = null;
                    return;
                }
            }
        }

        void Fire(WeaponState weapon, Unit target, float distance, float damage)
        {
            var definition = weapon.Definition;
            weapon.Cooldown = definition.FireInterval * Random.Range(0.85f, 1.15f);
            lastFiredTime = Time.time;

            bool hit = Random.value < CombatMath.HitChance(definition, this, target, distance);
            Vector3 impact = target.Position + Vector3.up;
            if (!hit)
            {
                Vector3 scatter = Random.insideUnitSphere * (4f + distance * 0.01f);
                scatter.y = Mathf.Abs(scatter.y) * 0.3f;
                impact += scatter;
            }

            EffectsManager.Shot(view.MuzzlePosition, impact, definition.TracerColor, definition.TracerWidth);

            if (hit)
            {
                if (CombatMath.IsInCover(target))
                    damage *= CombatMath.CoverDamageMultiplier;
                target.ApplyDamage(damage);
            }
        }

        public void ApplyDamage(float damage)
        {
            if (!IsAlive)
                return;

            Health = Mathf.Max(0f, Health - damage);
            if (Health <= 0f)
                Die();
        }

        void Die()
        {
            order = OrderType.None;
            currentTarget = null;
            attackTarget = null;
            if (IsSelected)
                SetSelected(false);

            agent.enabled = false;
            var hitbox = GetComponent<Collider>();
            if (hitbox != null)
                hitbox.enabled = false;

            view.SetDestroyed();
            EffectsManager.Explosion(Position + Vector3.up * 1.5f, Definition.IsInfantry ? 4f : 9f);
            Died?.Invoke(this);

            // Disabling removes the unit from Unit.All; the wreck stays for a while.
            enabled = false;
            Destroy(gameObject, 60f);
        }

        // ------------------------------------------------------------------
        // Target selection
        // ------------------------------------------------------------------

        bool IsValidTarget(Unit target) =>
            target != null && target.IsAlive && target.Team != Team && target.IsVisibleTo(Team);

        bool CanEngage(Unit target)
        {
            float distance = Vector3.Distance(Position, target.Position);
            if (BestDamageAgainst(target, distance) <= 0f)
                return false;
            return LineOfSight.Check(EyePosition, target.EyePosition);
        }

        float BestDamageAgainst(Unit target, float distance)
        {
            var armorFacing = CombatMath.GetFacing(target.transform, Position);
            int armor = CombatMath.GetArmor(target.Definition, armorFacing);
            float best = 0f;
            foreach (var weapon in weapons)
            {
                if (distance > weapon.Definition.Range)
                    continue;
                float perSecond = CombatMath.Damage(weapon.Definition, armor) * weapon.Definition.Accuracy /
                                  Mathf.Max(0.1f, weapon.Definition.FireInterval);
                best = Mathf.Max(best, perSecond);
            }
            return best < 0.01f ? 0f : best;
        }

        Unit FindBestTarget()
        {
            Unit best = null;
            float bestScore = 0f;
            foreach (var candidate in all)
            {
                if (!IsValidTarget(candidate))
                    continue;

                float distance = Vector3.Distance(Position, candidate.Position);
                float damage = BestDamageAgainst(candidate, distance);
                if (damage <= 0f)
                    continue;

                // Prefer targets we hurt most, then closer ones; keep the current target if comparable.
                float score = damage / (1f + distance / 1000f);
                if (candidate == currentTarget)
                    score *= 1.3f;
                if (score <= bestScore)
                    continue;

                if (!LineOfSight.Check(EyePosition, candidate.EyePosition))
                    continue;

                best = candidate;
                bestScore = score;
            }
            return best;
        }
    }
}
