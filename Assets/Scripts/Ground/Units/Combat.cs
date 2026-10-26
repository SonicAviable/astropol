using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Боевая математика: попадание, укрытия, броня техники по сторонам, подавление, взрывы по площади,
    /// рукопашная, давка техникой и смерть с правильным видом гибели (падение, ошмётки, обугливание).
    /// </summary>
    public static class Combat
    {
        private static GroundBattleManager G => GroundBattleManager.Instance;
        private static readonly List<CoverPoint> s_scratch = new List<CoverPoint>();

        public static readonly WeaponDef FlameDeath = new WeaponDef { Kind = WeaponKind.Flame, Damage = 0f };
        public static readonly WeaponDef CrushDeath = new WeaponDef { Kind = WeaponKind.Crush, Damage = 9999f };
        public static readonly WeaponDef PodImpact = new WeaponDef { Kind = WeaponKind.Crush, Damage = 9999f };
        public static readonly WeaponDef LavaBurn = new WeaponDef { Kind = WeaponKind.Flame, Damage = 0f };

        /// <summary>Дальность оружия бойца с учётом биома (пепел −20%).</summary>
        public static float Range(Soldier s)
        {
            if (s?.Weapon == null) return 0f;
            float r = s.Weapon.Range * (s.Weapon.Indirect ? 1f : G.Map.Biome.RangeMult) * G.WeatherRangeMult;
            return r;
        }

        public static bool InRange(Soldier s, Soldier t)
        {
            float r = Range(s);
            return (t.Pos - s.Pos).sqrMagnitude <= r * r;
        }

        /// <summary>Прямая видимость: луч от груди к груди не упирается в здания и рельеф.</summary>
        public static bool LineOfSight(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1f) return true;
            return !Physics.Raycast(a, d / len, len - 0.8f, ~(1 << 2), QueryTriggerInteraction.Ignore);
        }

        private static float DamageMult(Soldier s)
        {
            var sq = s.Squad;
            float k = sq.Side.Setup.DamageMult * (1f + 0.1f * sq.Veterancy);
            if (sq.Broken) k *= 0.8f;
            return k;
        }

        public static float HitChance(Soldier s, Soldier t, float dist)
        {
            var w = s.Weapon;
            float c = w.Accuracy * (1.15f - 0.5f * Mathf.Clamp01(dist / Mathf.Max(1f, Range(s))));
            if (s.Moving) c *= 0.6f;
            if (s.Squad.Broken) c *= 0.5f;
            if (s.Squad.Pinned) c *= 0.5f;
            if (t.Moving) c *= 0.85f;
            if (t.IsVehicle) c *= 1.5f;
            else if (t.ProneAmount > 0.5f) c *= 0.7f;
            c *= G.WeatherAccuracyMult;
            return Mathf.Clamp(c, 0.05f, 0.95f);
        }

        // ================================================================ Выстрелы

        public static void Shoot(Soldier s, Soldier t)
        {
            var w = s.Weapon;
            Vector3 from = s.Rig.Muzzle != null ? s.Rig.Muzzle.position : s.Chest;
            float dist = Vector3.Distance(s.Pos, t.Pos);
            if (!w.Indirect && !LineOfSight(s.Chest, t.Chest)) { s.Target = null; return; }
            G.Sfx.Shot(w, from, s.SideIndex);

            if (w.Indirect)
            {
                // Навесной снаряд: падает с разбросом, по площади, через полторы секунды
                Vector3 impact = t.Pos + Random.insideUnitSphere.WithY(0f) * (1f - HitChance(s, t, dist)) * 9f;
                G.Fx.Shell(from, G.Map.Snap(impact), 1.4f, () => Explode(G.Map.Snap(impact), w, s.Squad, DamageMult(s)));
                return;
            }

            bool hit = Random.value < HitChance(s, t, dist);
            Vector3 aim = t.Chest + (hit ? Vector3.zero : Random.insideUnitSphere * (1.2f + dist * 0.04f));
            G.Fx.Tracer(from, aim, w, s.Squad.Side.Color);
            G.Fx.MuzzleFlash(from, w);

            if (w.Splash > 0f)
            {
                Vector3 p = hit ? t.Pos : G.Map.Snap(aim);
                Explode(p, w, s.Squad, DamageMult(s));
                return;
            }
            // Пули рядом прижимают, даже если не попали
            t.Squad.AddSuppression(w.Suppression * (t.Squad.InCover ? 0.6f : 1f));
            if (!hit) { G.Fx.Impact(G.Map.Snap(aim), w); return; }
            Damage(t, w.Damage * DamageMult(s), w, from, s.Squad);
        }

        public static void ShootStructure(Soldier s, Destructible d)
        {
            var w = s.Weapon;
            Vector3 from = s.Rig.Muzzle != null ? s.Rig.Muzzle.position : s.Chest;
            Vector3 aim = d.Pos + Vector3.up * Random.Range(1f, 3f) + Random.insideUnitSphere * d.Radius * 0.4f;
            G.Sfx.Shot(w, from, s.SideIndex);
            G.Fx.MuzzleFlash(from, w);
            if (w.Indirect)
            {
                G.Fx.Shell(from, aim, 1.4f, () => Explode(G.Map.Snap(aim), w, s.Squad, DamageMult(s)));
                return;
            }
            G.Fx.Tracer(from, aim, w, s.Squad.Side.Color);
            if (w.Splash > 0f) { Explode(aim, w, s.Squad, DamageMult(s)); return; }
            G.DamageStructure(d, w.Damage * w.StructureMult * DamageMult(s), aim, s.Squad);
            G.Fx.Impact(aim, w);
        }

        /// <summary>Турель или бункер стреляет по бойцу.</summary>
        public static void TurretShoot(Destructible t, Soldier target)
        {
            var w = t.Weapon;
            Vector3 from = t.Pos + Vector3.up * 2f;
            float dist = Vector3.Distance(from, target.Pos);
            G.Sfx.Shot(w, from, 1);
            G.Fx.MuzzleFlash(from, w);
            bool hit = Random.value < Mathf.Clamp(w.Accuracy * (1.1f - 0.5f * dist / w.Range) * (target.IsVehicle ? 1.5f : 1f), 0.05f, 0.9f);
            Vector3 aim = target.Chest + (hit ? Vector3.zero : Random.insideUnitSphere * 2f);
            G.Fx.Tracer(from, aim, w, G.Sides[1].Color);
            if (w.Splash > 0f) { Explode(hit ? target.Pos : G.Map.Snap(aim), w, null, G.Sides[1].Setup.DamageMult, 1); return; }
            target.Squad.AddSuppression(w.Suppression);
            if (hit) Damage(target, w.Damage * G.Sides[1].Setup.DamageMult, w, from, null);
            else G.Fx.Impact(G.Map.Snap(aim), w);
        }

        // ================================================================ Урон

        /// <summary>Урон бойцу с учётом укрытия, отступления, брони техники по сторонам.</summary>
        public static void Damage(Soldier v, float dmg, WeaponDef w, Vector3 from, Squad attacker, bool splash = false)
        {
            if (v == null || v.Dead || dmg <= 0f) return;
            if (v.IsVehicle)
            {
                // Броня: лоб держит лучше, корма — хуже
                Vector3 toShooter = from - v.Pos; toShooter.y = 0f;
                float facing = toShooter.sqrMagnitude < 0.01f ? 0.8f : Vector3.Dot(v.transform.forward, toShooter.normalized);
                float armorK = facing > 0.5f ? 1f : facing > -0.5f ? 0.8f : 0.55f;
                float armor = v.Def.Armor * armorK;
                dmg *= Mathf.Lerp(1f - armor, 1f, w.Pierce);
                if (w.Kind == WeaponKind.Melee) dmg *= v.Def.Role == UnitRole.Walker ? 0.4f : 0.6f;
            }
            else
            {
                var cover = G.Map.Cover.CoverAt(v.Pos, from, s_scratch);
                bool explosive = w.Kind == WeaponKind.Explosive || w.Kind == WeaponKind.Flame || splash;
                float k = 1f;
                if (cover == CoverKind.Heavy) k = explosive ? 0.75f : 0.4f;
                else if (cover == CoverKind.Light) k = explosive ? 1f : 0.7f;
                else if (cover == CoverKind.Danger) k = 1.25f;
                if (w.IgnoresCoverHalf && k < 1f) k = 1f - (1f - k) * 0.5f;
                if (w.Kind == WeaponKind.Melee || w.Kind == WeaponKind.Crush) k = 1f;
                dmg *= k;
                if (v.Squad.Retreating) dmg *= 0.5f;
            }
            v.Hp -= dmg;
            v.Squad.OnMemberHurt();
            if (!v.Def.Vehicle && v.Def.Role != UnitRole.Swarm) GoreFx.Hit(v, from, w, dmg);
            else G.Fx.Sparks(v.Chest, 0.6f);
            if (w.Kind == WeaponKind.Flame && !v.IsVehicle && v.Def.Role != UnitRole.Swarm && v.BurnLeft <= 0f && Random.value < 0.35f)
                v.BurnLeft = Random.Range(2.5f, 4.5f);
            if (v.Hp <= 0f) Kill(v, w, (v.Pos - from).normalized, attacker);
        }

        /// <summary>Взрыв: урон по площади со спадом, ошмётки, разрушение лёгких укрытий, воронка, тряска.</summary>
        public static void Explode(Vector3 pos, WeaponDef w, Squad source, float dmgMult, int sourceSide = -1)
        {
            int side = source != null ? source.Side.Index : sourceSide;
            float r = Mathf.Max(1f, w.Splash);
            G.Fx.Explosion(pos, r);
            G.Sfx.Explosion(pos, r);
            BattleCamera.Shake(pos, Mathf.Clamp(r * 0.25f, 0.2f, 1.6f));
            if (r >= 2.5f) G.AddCrater(pos, r * 0.6f);
            var victims = G.SoldiersInRadius(pos, r + 1f);
            foreach (var v in victims)
            {
                float d = Vector3.Distance(pos, v.Pos);
                if (d > r + (v.IsVehicle ? 2f : 0f)) continue;
                float k = Mathf.Pow(1f - Mathf.Clamp01(d / (r + 0.01f)), 0.6f);
                // Свои тоже страдают, но меньше (кроме артиллерии)
                if (v.SideIndex == side && !w.Indirect) k *= 0.25f;
                v.Squad.AddSuppression(w.Suppression * (0.5f + k));
                Damage(v, w.Damage * dmgMult * Mathf.Max(0.2f, k), w, pos + Vector3.up * 0.5f, source, true);
            }
            // Сооружения: здания, мешки, турели
            G.DamageStructuresInRadius(pos, r, w.Damage * dmgMult * w.StructureMult, source);
        }

        /// <summary>Удар в рукопашной. Элита с шансом 25% проводит добивание.</summary>
        public static void Melee(Soldier s, Soldier t)
        {
            var w = s.Def.Melee;
            float dmg = w.Damage * DamageMult(s) * Random.Range(0.8f, 1.2f);
            G.Sfx.Melee(s.Pos);
            if (s.Def.SyncKills && !t.InSyncKill && !s.InSyncKill && (s.Sergeant || s.Def.Role == UnitRole.Hero || s.Def.Role == UnitRole.Walker)
                && t.Hp <= dmg * 2.2f && Random.value < 0.25f && (!t.IsVehicle || s.Def.Role == UnitRole.Walker))
            {
                SyncKills.Begin(s, t);
                return;
            }
            Damage(t, dmg, w, s.Pos, s.Squad);
            t.Squad.AddSuppression(4f);
        }

        /// <summary>Техника давит пехоту под собой (не своих, если они успевают отскочить — шанс).</summary>
        public static void CrushInfantry(Squad sq)
        {
            foreach (var m in sq.Members)
            {
                if (m.Dead || m.Speed < 1.2f) continue;
                float r = 1.8f * Mathf.Max(1f, m.Def.Scale);
                var near = G.SoldiersInRadius(m.Pos + m.transform.forward * r * 0.6f, r);
                foreach (var v in near)
                {
                    if (v.IsVehicle || v.Dead || v.InSyncKill) continue;
                    if (v.SideIndex == m.SideIndex && Random.value < 0.9f) continue;
                    Kill(v, CrushDeath, m.transform.forward, sq);
                }
            }
        }

        /// <summary>Смерть: правильный вид гибели, потеря морали и тикетов, опыт убийце.</summary>
        public static void Kill(Soldier v, WeaponDef w, Vector3 dir, Squad killer)
        {
            if (v == null || v.Dead) return;
            v.Dead = true;
            v.Hp = 0f;
            var side = v.Squad.Side;
            if (v.IsVehicle) { side.LostVehicles++; side.Tickets -= 8f; }
            else { side.LostMen++; side.Tickets -= 1f; }
            v.Squad.OnMemberDied(v);
            if (killer != null && killer.Side != side) killer.AddKill();
            if (v.Cover != null && v.Cover.Occupant == v) v.Cover.Occupant = null;
            G.OnSoldierDied(v, w, dir);
        }
    }

    public static class VecExt
    {
        public static Vector3 WithY(this Vector3 v, float y) { v.y = y; return v; }
    }
}
