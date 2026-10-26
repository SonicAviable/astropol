using UnityEngine;
using UnityEngine.AI;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Одна модель на поле: боец отряда или машина. Движется по навмешу (NavMeshAgent), стреляет по целям отряда,
    /// дерётся врукопашную, горит, прижимается под огнём. Логику дёргает отряд (Tick) — порядок обновления
    /// предсказуем. Анимация процедурная: шаг ногами, качание рук, отдача, ничком под подавлением.
    /// </summary>
    public sealed class Soldier : MonoBehaviour
    {
        public Squad Squad;
        public UnitDef Def;
        public bool Sergeant;
        public float Hp, MaxHp;
        public bool Dead;
        public UnitRig Rig;
        public NavMeshAgent Agent;
        public WeaponDef Weapon;
        public Soldier Target;
        public Destructible TargetStructure;
        public CoverPoint Cover;
        /// <summary>Горит (огнемёт): бежит, кричит, получает урон.</summary>
        public float BurnLeft;
        public float LastShot = -100f;
        /// <summary>Пулемёт: сколько ещё разворачиваться.</summary>
        public float SetupLeft;
        /// <summary>Занят добиванием — не стреляет и не двигается.</summary>
        public bool InSyncKill;

        private float _cool, _reload;
        private int _burst;
        private float _walk, _recoil, _prone, _meleeCool;
        /// <summary>Сдвиг фазы покачивания (у каждого дрона роя свой).</summary>
        private float _bobPhase;
        private bool _visible = true;
        private Renderer _ring;
        private bool _ringSelected;
        private Vector3 _lastPos;

        public Vector3 Pos => transform.position;
        public Vector3 Chest => transform.position + Vector3.up * (Rig != null ? Rig.Height * 0.68f : 1.2f);
        public int SideIndex => Squad != null ? Squad.Side.Index : 0;
        public bool IsVehicle => Def.Vehicle;
        public float Speed => Agent != null && Agent.enabled ? Agent.velocity.magnitude : 0f;
        public bool Moving => Speed > 0.6f;

        public void Init(Squad squad, UnitDef def, bool sergeant, Vector3 pos, float hpMult, float speedMult)
        {
            Squad = squad;
            Def = def;
            _bobPhase = Random.value * 6.2832f;
            Sergeant = sergeant;
            MaxHp = Hp = def.Hp * hpMult * (sergeant && !def.Vehicle ? 1.25f : 1f);
            Weapon = sergeant && def.SergeantWeapon != null && !def.Vehicle ? def.SergeantWeapon : def.Weapon;
            if (def.Role == UnitRole.MachineGun && sergeant) Weapon = def.SergeantWeapon ?? def.Weapon;
            transform.position = pos;
            Rig = UnitMeshFactory.Build(transform, def, squad.Side.Color, sergeant, squad.Side.FactionKey);
            // Кольцо стороны под ногами (как в Dawn of War); выделенный отряд — ярче
            float ringSize = def.Vehicle ? (def.Role == UnitRole.Walker ? 9f : 6.2f) : def.Role == UnitRole.Swarm ? 0.9f : 1.25f * def.Scale * UnitMeshFactory.Heroic;
            _ring = GroundMats.MeshPart(transform, ProcMesh.FlatQuad, new Vector3(0f, 0.07f, 0f), new Vector3(ringSize, 1f, ringSize), GroundMats.Ring(squad.Side.Color, false), "Ring")
                .GetComponent<Renderer>();
            _ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ring.receiveShadows = false;

            Agent = gameObject.AddComponent<NavMeshAgent>();
            Agent.radius = def.Vehicle ? 1.6f * Mathf.Max(1f, def.Scale * 0.8f) : def.Role == UnitRole.Swarm ? 0.25f : 0.46f;
            Agent.height = Rig.Height;
            Agent.speed = def.Speed * speedMult;
            Agent.acceleration = def.Vehicle ? 6f : 18f;
            Agent.angularSpeed = def.Vehicle ? 90f : 540f;
            Agent.stoppingDistance = 0.4f;
            Agent.autoBraking = true;
            Agent.obstacleAvoidanceType = def.Vehicle ? ObstacleAvoidanceType.MedQualityObstacleAvoidance : ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            Agent.avoidancePriority = def.Vehicle ? 30 : 50 + Random.Range(0, 20);
            if (NavMesh.SamplePosition(pos, out var hit, 8f, NavMesh.AllAreas)) Agent.Warp(hit.position);
            _cool = Random.Range(0f, Weapon != null ? Weapon.Cooldown : 1f);
            _burst = Weapon != null ? Weapon.Burst : 1;
            SetupLeft = Weapon != null ? Weapon.SetupTime : 0f;
            _lastPos = transform.position;
        }

        public void MoveTo(Vector3 p)
        {
            if (Dead || Agent == null || !Agent.enabled || !Agent.isOnNavMesh) return;
            if (Cover != null) { if (Cover.Occupant == this) Cover.Occupant = null; Cover = null; }
            Agent.isStopped = false;
            Agent.SetDestination(p);
        }

        public void Stop()
        {
            if (Agent != null && Agent.enabled && Agent.isOnNavMesh) { Agent.ResetPath(); }
        }

        public bool Arrived => Agent == null || !Agent.enabled || !Agent.isOnNavMesh
                               || !Agent.pathPending && Agent.remainingDistance <= Agent.stoppingDistance + 0.6f;

        public void SetVisible(bool v)
        {
            if (v == _visible || Rig?.Renderers == null) return;
            _visible = v;
            foreach (var r in Rig.Renderers) if (r != null) r.enabled = v;
            if (_ring != null) _ring.enabled = v;
        }

        public bool VisibleNow => _visible;

        // ================================================================ Такт

        public void Tick(float dt, float now)
        {
            if (Dead) return;
            if (BurnLeft > 0f) TickBurning(dt);
            if (Dead) return;
            Animate(dt);
            if (_ring != null && Squad.Selected != _ringSelected)
            {
                _ringSelected = Squad.Selected;
                _ring.sharedMaterial = GroundMats.Ring(Squad.Side.Color, _ringSelected);
            }
            if (InSyncKill) return;

            var w = Weapon;
            if (w == null) return;
            if (Moving && w.SetupTime > 0f) SetupLeft = w.SetupTime;
            else if (SetupLeft > 0f) SetupLeft -= dt;

            if (_meleeCool > 0f) _meleeCool -= dt;
            if (_reload > 0f) { _reload -= dt; return; }
            if (_cool > 0f) { _cool -= dt; return; }
            if (Squad.Retreating || BurnLeft > 0f) return;

            // Рукопашная, если враг вплотную
            var enemy = Squad.Engaged;
            if (Def.Melee != null && enemy != null && !IsVehicle)
            {
                var close = Squad.NearestEnemyMember(enemy, Pos, Def.Melee.Range * Def.Scale + (enemy.Def.Vehicle ? 2.5f : 0f));
                if (close != null && _meleeCool <= 0f)
                {
                    _meleeCool = Def.Melee.Cooldown;
                    Face(close.Pos);
                    Combat.Melee(this, close);
                    return;
                }
            }

            if (SetupLeft > 0f) return;
            if (Moving && (w.Indirect || w.SetupTime > 0f || w.Kind == WeaponKind.Sniper)) return;

            // Цель: член вражеского отряда в радиусе или сооружение
            if (Target == null || Target.Dead || Target.Squad != enemy || !Combat.InRange(this, Target)) Target = enemy != null ? Squad.PickTarget(enemy, this) : null;
            if (Target != null)
            {
                Fire(now, () => Combat.Shoot(this, Target), Target.Chest);
                return;
            }
            var st = Squad.EngagedStructure;
            if (st != null && !st.Dead && (st.Pos - Pos).sqrMagnitude <= Combat.Range(this) * Combat.Range(this))
                Fire(now, () => Combat.ShootStructure(this, st), st.Pos + Vector3.up * 2f);
        }

        private void Fire(float now, System.Action shot, Vector3 aim)
        {
            var w = Weapon;
            float pinned = Squad.Pinned ? 2f : 1f;
            Face(aim);
            shot();
            LastShot = now;
            _recoil = 1f;
            if (--_burst <= 0)
            {
                _burst = w.Burst;
                _reload = w.Reload * pinned;
            }
            _cool = w.Cooldown * pinned * Random.Range(0.85f, 1.15f);
        }

        private void Face(Vector3 p)
        {
            if (IsVehicle)
            {
                if (Rig.Turret != null)
                {
                    var d = p - Rig.Turret.position; d.y = 0f;
                    if (d.sqrMagnitude > 0.01f) Rig.Turret.rotation = Quaternion.LookRotation(d);
                }
                return;
            }
            if (Moving) return;
            var dir = p - Pos; dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(dir);
        }

        // ================================================================ Огонь

        private float _burnPanic;

        private void TickBurning(float dt)
        {
            BurnLeft -= dt;
            Hp -= 14f * dt;
            _burnPanic -= dt;
            if (_burnPanic <= 0f && Agent != null && Agent.enabled && Agent.isOnNavMesh)
            {
                _burnPanic = 0.8f;
                Agent.SetDestination(Pos + new Vector3(Random.Range(-6f, 6f), 0f, Random.Range(-6f, 6f)));
            }
            GoreFx.Burn(this, dt);
            if (Hp <= 0f) Combat.Kill(this, Combat.FlameDeath, Vector3.up, null);
        }

        // ================================================================ Анимация

        private void Animate(float dt)
        {
            if (Rig == null) return;
            float sp = Speed;
            float k = Mathf.Clamp01(sp / Mathf.Max(1f, Def.Speed));

            if (Def.Role == UnitRole.Swarm)
            {
                if (Rig.Hips != null) Rig.Hips.localPosition = new Vector3(0f, 1.1f + Mathf.Sin(Time.time * 6f + _bobPhase) * 0.12f, 0f);
                return;
            }
            if (IsVehicle && Def.Role != UnitRole.Walker)
            {
                // Отдача ствола и лёгкое покачивание корпуса
                _recoil = Mathf.MoveTowards(_recoil, 0f, dt * 3f);
                if (Rig.Barrel != null) Rig.Barrel.localPosition = Rig.BarrelRest - Rig.Barrel.localRotation * Vector3.forward * (_recoil * (Def.Role == UnitRole.Artillery ? 0.9f : 0.5f));
                if (Rig.Turret != null && Target == null && Squad.EngagedStructure == null)
                    Rig.Turret.localRotation = Quaternion.Slerp(Rig.Turret.localRotation, Quaternion.identity, dt * 1.5f);
                return;
            }

            _walk += dt * (Def.Role == UnitRole.Walker ? 2.2f : 9f) * k;
            float swing = Mathf.Sin(_walk) * (Def.Role == UnitRole.Walker ? 22f : 34f) * k;
            if (Rig.LegL != null) Rig.LegL.localRotation = Quaternion.Euler(swing, 0f, 0f);
            if (Rig.LegR != null) Rig.LegR.localRotation = Quaternion.Euler(-swing, 0f, 0f);
            if (Def.Role == UnitRole.Walker)
            {
                // Топот: на каждом шаге — тряска камеры
                float step = Mathf.Sin(_walk);
                if (k > 0.2f && Mathf.Abs(step) > 0.98f && Time.time - _lastStomp > 0.4f) { _lastStomp = Time.time; BattleCamera.Shake(Pos, 0.6f); }
                if (Rig.Turret != null && Target != null)
                {
                    var d = Target.Pos - Pos; d.y = 0f;
                    if (d.sqrMagnitude > 1f) Rig.Turret.rotation = Quaternion.Slerp(Rig.Turret.rotation, Quaternion.LookRotation(d), dt * 2f);
                }
                return;
            }

            // Пригнуться под подавлением, ничком при прижатии
            float targetProne = Squad.Pinned && !Moving ? 1f : 0f;
            _prone = Mathf.MoveTowards(_prone, targetProne, dt * 2.5f);
            float bob = Mathf.Abs(Mathf.Sin(_walk)) * 0.06f * k;
            if (Rig.Hips != null) Rig.Hips.localPosition = new Vector3(0f, Rig.HipsY + bob - _prone * 0.62f, 0f);
            if (Rig.Torso != null)
            {
                float lean = 8f * k + _prone * 70f;
                _recoil = Mathf.MoveTowards(_recoil, 0f, dt * 8f);
                Rig.Torso.localRotation = Quaternion.Euler(lean - _recoil * 6f, 0f, 0f);
            }
            if (Rig.ArmR != null && Rig.Gun != null)
                Rig.Gun.localPosition = Rig.GunRest - new Vector3(0f, 0f, _recoil * 0.08f);
            // Голова чуть поворачивается к цели, при беге наклон вперёд, руки качаются в такт
            if (Rig.Head != null && Target != null && !Target.Dead)
            {
                var d = transform.InverseTransformPoint(Target.Pos);
                float yaw = Mathf.Clamp(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -40f, 40f);
                Rig.Head.localRotation = Quaternion.Slerp(Rig.Head.localRotation, Quaternion.Euler(0f, yaw, 0f), dt * 6f);
            }
            if (Rig.ArmL != null && k > 0.1f && _prone < 0.3f)
                Rig.ArmL.localRotation = Quaternion.Euler(-62f + Mathf.Sin(_walk) * 10f * k, 28f, 0f);
            if (_prone > 0.5f && Rig.LegL != null)
            {
                Rig.LegL.localRotation = Quaternion.Euler(-70f * _prone, 0f, 0f);
                Rig.LegR.localRotation = Quaternion.Euler(-70f * _prone, 0f, 0f);
            }
        }

        private float _lastStomp;

        public float ProneAmount => _prone;

        private void OnDestroy()
        {
            if (Cover != null && Cover.Occupant == this) Cover.Occupant = null;
        }
    }
}
