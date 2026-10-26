using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Ground
{
    /// <summary>Сторона боя: ресурсы, тикеты, отряды, точка высадки, лимит мест.</summary>
    public sealed class BattleSide
    {
        public int Index;
        public GroundSideSetup Setup;
        public Color Color;
        public string FactionKey;
        public bool IsPlayer;
        public float Requisition = 400f, Energy = 100f;
        public float Tickets = 500f;
        public readonly List<Squad> Squads = new List<Squad>();
        public Vector3 Beacon;
        public int SlotLimit = 40;
        public float IncomeMult = 1f;
        public int LostMen, LostVehicles, Kills;
        public float OrbitalCooldown, MilitiaTimer = 30f, HeroRespawn = -1f, SpecialCooldown;
        public GroundCommanderAI Ai;

        public int SlotsUsed
        {
            get
            {
                int n = 0;
                foreach (var s in Squads) if (!s.Destroyed) n += s.Def.Vehicle ? 6 : s.Def.Special ? 0 : s.Def.Size;
                return n;
            }
        }

        public int AliveSquads
        {
            get { int n = 0; foreach (var s in Squads) if (!s.Destroyed) n++; return n; }
        }
    }

    public enum SquadOrder { None, Move, AttackMove, Attack, Retreat }

    /// <summary>
    /// Отряд — главная единица боя, как в Dawn of War: сержант и бойцы, общая мораль и подавление, приказы,
    /// ветеранство, пополнение у маяка. Выбирает цель (видимый враг в радиусе), сам расходится по укрытиям
    /// после остановки, ломается при потере морали и бежит к точке высадки.
    /// </summary>
    public sealed class Squad
    {
        private static int s_nextId = 1;

        public readonly int Id = s_nextId++;
        public BattleSide Side;
        public UnitDef Def;
        public readonly List<Soldier> Members = new List<Soldier>();
        public float Morale, MaxMorale = 100f;
        public float Suppression;
        public bool Pinned;
        public bool Retreating;
        public int Kills, Veterancy;
        public SquadOrder Order;
        public Vector3 Destination;
        public Squad AttackTarget;
        public Destructible AttackStructure;
        /// <summary>С кем сейчас воюет (автовыбор или приказ).</summary>
        public Squad Engaged;
        public Destructible EngagedStructure;
        public readonly bool[] VisibleTo = new bool[2];
        public bool Selected;
        public bool Destroyed;
        public float LastHurt = -100f, ReinforceTimer;
        public bool InCover;
        /// <summary>Текст выкрика над отрядом и когда он погаснет.</summary>
        public string Callout;
        public float CalloutUntil;
        public float CalloutCooldown;
        /// <summary>Способность: перезарядка, сек.</summary>
        public float AbilityCooldown;
        /// <summary>Ловушка спор (Тэрра'ан): держит на месте.</summary>
        public float RootedUntil;
        public bool Charging;

        private float _think;
        private bool _settled;
        private readonly List<CoverPoint> _scratch = new List<CoverPoint>();

        public bool Broken => !Def.Fearless && Morale < MaxMorale * 0.25f;

        public int Alive
        {
            get { int n = 0; foreach (var m in Members) if (m != null && !m.Dead) n++; return n; }
        }

        public Vector3 Center
        {
            get
            {
                Vector3 c = Vector3.zero; int n = 0;
                foreach (var m in Members) if (m != null && !m.Dead) { c += m.Pos; n++; }
                return n > 0 ? c / n : Destination;
            }
        }

        public Soldier Leader
        {
            get { foreach (var m in Members) if (m != null && !m.Dead) return m; return null; }
        }

        public float MaxRange
        {
            get
            {
                float r = 0f;
                foreach (var m in Members) if (m != null && !m.Dead && m.Weapon != null) r = Mathf.Max(r, Combat.Range(m));
                return r;
            }
        }

        public float Health01
        {
            get
            {
                float hp = 0f, max = Def.Hp * Def.Size;
                foreach (var m in Members) if (m != null && !m.Dead) hp += m.Hp / Mathf.Max(1f, m.MaxHp) * Def.Hp;
                return max <= 0f ? 0f : Mathf.Clamp01(hp / max);
            }
        }

        // ================================================================ Приказы

        public void OrderMove(Vector3 p, bool attackMove)
        {
            if (Destroyed) return;
            if (Retreating && Morale <= 0.5f) return;   // сломленные не слушаются, пока не добегут
            Retreating = false;
            Order = attackMove ? SquadOrder.AttackMove : SquadOrder.Move;
            Destination = p;
            AttackTarget = null;
            AttackStructure = null;
            _settled = false;
            Charging = false;
            MoveFormation(p);
        }

        public void OrderAttack(Squad target)
        {
            if (Destroyed || target == null) return;
            Retreating = false;
            Order = SquadOrder.Attack;
            AttackTarget = target;
            AttackStructure = null;
            _settled = false;
        }

        public void OrderAttack(Destructible d)
        {
            if (Destroyed || d == null) return;
            Retreating = false;
            Order = SquadOrder.Attack;
            AttackStructure = d;
            AttackTarget = null;
            _settled = false;
        }

        public void OrderRetreat()
        {
            if (Destroyed) return;
            Retreating = true;
            Order = SquadOrder.Retreat;
            AttackTarget = null;
            Charging = false;
            Destination = Side.Beacon;
            foreach (var m in Members)
                if (m != null && !m.Dead && m.Agent != null && m.Agent.enabled)
                {
                    m.Agent.speed = m.Def.Speed * Side.Setup.SpeedMult * 1.35f;
                    m.MoveTo(Side.Beacon + new Vector3(Random.Range(-5f, 5f), 0f, Random.Range(-5f, 5f)));
                }
            Shout(GroundCallouts.Retreat(this));
        }

        /// <summary>Расставить бойцов строем вокруг точки (две шеренги поперёк движения).</summary>
        public void MoveFormation(Vector3 p)
        {
            var c = Center;
            Vector3 fwd = p - c; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            int i = 0, n = Alive;
            float spacing = Def.Vehicle ? 7f * Mathf.Max(1f, Def.Scale) : Def.Role == UnitRole.Swarm ? 1.1f : 1.9f;
            int perRow = Def.Role == UnitRole.Swarm ? 6 : Mathf.Max(1, Mathf.CeilToInt(n / 2f));
            var map = GroundBattleManager.Instance.Map;
            foreach (var m in Members)
            {
                if (m == null || m.Dead) continue;
                int row = i / perRow, col = i % perRow;
                float off = (col - (perRow - 1) * 0.5f) * spacing;
                var pos = p + right * off - fwd * row * spacing;
                m.MoveTo(map.Clamp(pos));
                i++;
            }
        }

        // ================================================================ Такт

        public void Tick(float dt, float now)
        {
            if (Destroyed) return;
            Members.RemoveAll(m => m == null || m.Dead);
            if (Members.Count == 0) { Destroyed = true; Selected = false; return; }

            foreach (var m in Members) m.Tick(dt, now);
            if (Members.Count == 0) return;

            TickMorale(dt, now);
            if (CalloutCooldown > 0f) CalloutCooldown -= dt;
            if (AbilityCooldown > 0f) AbilityCooldown -= dt;

            // Скорость: подавление и корни Тэрра'ан
            float speedK = Pinned ? 0.4f : 1f;
            if (now < RootedUntil) speedK = 0f;
            foreach (var m in Members)
                if (m.Agent != null && m.Agent.enabled && !Retreating)
                    m.Agent.speed = m.Def.Speed * Side.Setup.SpeedMult * speedK * (Charging ? 1.8f : 1f);

            // Отступление: добежали — мораль восстанавливается
            if (Retreating)
            {
                if ((Center - Side.Beacon).sqrMagnitude < 16f * 16f)
                {
                    Retreating = false;
                    Order = SquadOrder.None;
                    Morale = Mathf.Max(Morale, MaxMorale * 0.6f);
                    Suppression = 0f;
                    Pinned = false;
                    foreach (var m in Members) if (m.Agent != null && m.Agent.enabled) m.Agent.speed = m.Def.Speed * Side.Setup.SpeedMult;
                }
                TickReinforce(dt);
                return;
            }

            if ((_think -= dt) <= 0f)
            {
                _think = 0.4f + Random.value * 0.15f;
                Think();
            }
            TickReinforce(dt);
            if (Def.Vehicle) Combat.CrushInfantry(this);
        }

        private void Think()
        {
            var gm = GroundBattleManager.Instance;
            // Цель по приказу
            if (AttackTarget != null && (AttackTarget.Destroyed || !AttackTarget.VisibleTo[Side.Index])) { if (AttackTarget.Destroyed) { AttackTarget = null; Order = SquadOrder.None; } }
            if (AttackStructure != null && AttackStructure.Dead) { AttackStructure = null; Order = SquadOrder.None; }

            float range = MaxRange;
            Engaged = null;
            EngagedStructure = null;
            if (AttackTarget != null && AttackTarget.VisibleTo[Side.Index])
            {
                Engaged = AttackTarget;
                float d = DistanceTo(AttackTarget);
                if (d > range * 0.92f || Charging) MoveFormation(AttackTarget.Center);
                else if (!_settled) { HoldHere(); }
            }
            else if (AttackStructure != null)
            {
                EngagedStructure = AttackStructure;
                float d = Vector3.Distance(Center, AttackStructure.Pos) - AttackStructure.Radius;
                if (d > range * 0.9f) MoveFormation(AttackStructure.Pos);
                else if (!_settled) HoldHere();
            }
            if (Engaged == null)
            {
                // Автоцель: ближайший видимый враг в радиусе стрельбы (+запас)
                Engaged = gm.NearestEnemySquad(this, range + 4f);
                if (Engaged == null && EngagedStructure == null) EngagedStructure = gm.NearestEnemyStructure(this, range);
            }

            // Атака с ходу: увидели врага — остановились и дерёмся
            if (Order == SquadOrder.AttackMove && Engaged != null && !_settled) HoldHere();
            else if (Order == SquadOrder.AttackMove && Engaged == null && _settled && (Center - Destination).sqrMagnitude > 9f) { _settled = false; MoveFormation(Destination); }

            // Тяжёлая пехота и герои сами сближаются для рукопашной
            if (Engaged != null && Def.Melee != null && (Def.Role == UnitRole.Heavy || Def.Role == UnitRole.Hero || Def.Role == UnitRole.Walker || Def.Role == UnitRole.Swarm)
                && Order != SquadOrder.Move && DistanceTo(Engaged) < 26f)
                MoveFormation(Engaged.Center);

            // Пришли — расходимся по укрытиям
            if (!_settled && Order != SquadOrder.Attack && AllArrived())
            {
                _settled = true;
                if (Order == SquadOrder.Move) Order = SquadOrder.None;
                TakeCover(Destination);
            }
            InCover = CountInCover() * 2 >= Alive;

            if (Engaged != null && CalloutCooldown <= 0f && Random.value < 0.08f) Shout(GroundCallouts.Combat(this));
        }

        private void HoldHere()
        {
            _settled = true;
            foreach (var m in Members) m.Stop();
            TakeCover(Center);
        }

        private bool AllArrived()
        {
            foreach (var m in Members) if (!m.Arrived) return false;
            return true;
        }

        /// <summary>Бойцы занимают ближайшие свободные укрытия в радиусе 8 м.</summary>
        public void TakeCover(Vector3 around)
        {
            if (Def.Vehicle || Def.Role == UnitRole.Swarm) return;
            var map = GroundBattleManager.Instance.Map;
            map.Cover.Query(around, 8f, _scratch);
            _scratch.RemoveAll(c => !c.Free || c.Kind == CoverKind.Danger);
            _scratch.Sort((a, b) => (b.Kind == CoverKind.Heavy ? 1 : 0).CompareTo(a.Kind == CoverKind.Heavy ? 1 : 0) * 1000
                                     + ((a.Pos - around).sqrMagnitude).CompareTo((b.Pos - around).sqrMagnitude));
            int k = 0;
            foreach (var m in Members)
            {
                if (k >= _scratch.Count) break;
                var c = _scratch[k++];
                c.Occupant = m;
                m.MoveTo(c.Pos);
                m.Cover = c;
            }
        }

        private int CountInCover()
        {
            int n = 0;
            foreach (var m in Members) if (m.Cover != null && (m.Pos - m.Cover.Pos).sqrMagnitude < 2.5f) n++;
            return n;
        }

        public float DistanceTo(Squad other) => other == null ? float.MaxValue : Vector3.Distance(Center, other.Center);

        public Soldier NearestEnemyMember(Squad enemy, Vector3 from, float maxDist)
        {
            Soldier best = null;
            float bd = maxDist * maxDist;
            foreach (var m in enemy.Members)
            {
                if (m == null || m.Dead || m.InSyncKill) continue;
                float d = (m.Pos - from).sqrMagnitude;
                if (d < bd) { bd = d; best = m; }
            }
            return best;
        }

        /// <summary>Цель для бойца: ближайший к нему живой член вражеского отряда в радиусе (с небольшим разбросом).</summary>
        public Soldier PickTarget(Squad enemy, Soldier shooter)
        {
            Soldier best = null;
            float bd = float.MaxValue;
            float r = Combat.Range(shooter);
            foreach (var m in enemy.Members)
            {
                if (m == null || m.Dead) continue;
                float d = (m.Pos - shooter.Pos).sqrMagnitude;
                if (d > r * r) continue;
                d *= Random.Range(0.7f, 1.3f);
                if (d < bd) { bd = d; best = m; }
            }
            return best;
        }

        // ================================================================ Мораль и подавление

        public void AddSuppression(float v)
        {
            if (Def.Fearless || Def.Vehicle) return;
            Suppression = Mathf.Min(100f, Suppression + v);
            LastHurt = Time.time;
            if (!Pinned && Suppression >= 80f)
            {
                Pinned = true;
                Shout(GroundCallouts.Pinned(this));
            }
        }

        public void OnMemberHurt() => LastHurt = Time.time;

        public void OnMemberDied(Soldier s)
        {
            if (Def.Fearless) return;
            Morale -= s.Sergeant ? 30f : 8f;
            if (s.Sergeant)
            {
                // Командование переходит к следующему бойцу
                foreach (var m in Members) if (m != null && !m.Dead && m != s) { m.Sergeant = true; break; }
                Shout(GroundCallouts.SergeantDown(this));
            }
        }

        private void TickMorale(float dt, float now)
        {
            if (Def.Fearless) { Morale = MaxMorale; Suppression = 0f; Pinned = false; return; }
            bool hot = now - LastHurt < 4f;
            var gm = GroundBattleManager.Instance;
            // Страшные враги рядом и аура генерала
            if (gm.TerrorNear(this, 16f)) Morale -= 3f * dt;
            if (gm.HeroNear(this, 22f)) Morale += 5f * dt;
            if (Pinned) Morale -= 2.5f * dt;
            if (!hot) { Morale += 4f * dt; Suppression -= 14f * dt; }
            else Suppression -= 5f * dt;
            Suppression = Mathf.Clamp(Suppression, 0f, 100f);
            Morale = Mathf.Clamp(Morale, 0f, MaxMorale);
            if (Pinned && Suppression < 30f) Pinned = false;
            if (Morale <= 0.5f && !Retreating) OrderRetreat();
        }

        // ================================================================ Пополнение и ветераны

        private void TickReinforce(float dt)
        {
            if (Def.Vehicle || Def.Special || Members.Count >= Def.Size || Retreating && (Center - Side.Beacon).sqrMagnitude > 18f * 18f) { ReinforceTimer = 0f; return; }
            var gm = GroundBattleManager.Instance;
            if (!gm.NearReinforcePoint(this)) { ReinforceTimer = 0f; return; }
            float cost = Mathf.Ceil(Def.Requisition / (float)Def.Size);
            if (Side.Requisition < cost) return;
            ReinforceTimer += dt;
            if (ReinforceTimer < 8f) return;
            ReinforceTimer = 0f;
            Side.Requisition -= cost;
            gm.SpawnMember(this, false, Center + new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f)));
        }

        public void AddKill()
        {
            Kills++;
            Side.Kills++;
            int lvl = Kills >= 30 ? 3 : Kills >= 15 ? 2 : Kills >= 6 ? 1 : 0;
            if (lvl > Veterancy)
            {
                Veterancy = lvl;
                MaxMorale = 100f * Side.Setup.MoraleMult * (1f + 0.1f * Veterancy);
                Morale = Mathf.Min(MaxMorale, Morale + 20f);
                foreach (var m in Members) if (m != null && !m.Dead) { m.MaxHp *= 1.1f; m.Hp *= 1.1f; }
                Shout(Loc.T("Ветераны!"));
            }
        }

        public void Shout(string text)
        {
            if (string.IsNullOrEmpty(text) || CalloutCooldown > 0f) return;
            Callout = text;
            CalloutUntil = Time.time + 2.6f;
            CalloutCooldown = 6f + Random.value * 4f;
        }
    }
}
