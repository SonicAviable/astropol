using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Способности отрядов (Z/X/C): граната, рывок тяжёлой пехоты, «Видение» Провидца (Иридия), споры-ловушки
    /// и роща-укрытие (Тэрра'ан). У каждой — перезарядка отряда; ИИ пользуется ими по тем же правилам.
    /// </summary>
    public static class Abilities
    {
        public static string Name(string id) => id switch
        {
            "grenade" => Loc.T("Граната"),
            "charge" => Loc.T("Рывок"),
            "vision" => Loc.T("Видение"),
            "spores" => Loc.T("Споры-ловушки"),
            "grove" => Loc.T("Роща"),
            _ => id
        };

        public static string Tip(string id) => id switch
        {
            "grenade" => Loc.T("Бросок на 18 м: урон по площади, ломает лёгкие укрытия, выкуривает из окопов."),
            "charge" => Loc.T("Отряд срывается в рукопашную вдвое быстрее и прижимает врага."),
            "vision" => Loc.T("10 с: видны все вражеские отряды и пунктир их маршрутов."),
            "spores" => Loc.T("Корни держат вражеские отряды в радиусе 6 м на 4 с."),
            "grove" => Loc.T("За секунды вырастают деревья — лёгкое укрытие."),
            _ => ""
        };

        public static float Cooldown(string id) => id switch
        {
            "grenade" => 25f, "charge" => 30f, "vision" => 45f, "spores" => 30f, "grove" => 40f, _ => 20f
        };

        public static float Range(string id) => id switch { "grenade" => 18f, "spores" => 30f, "grove" => 25f, _ => 999f };

        /// <summary>Способность без прицела (срабатывает сразу).</summary>
        public static bool Instant(string id) => id == "vision";

        private static readonly WeaponDef Grenade = new WeaponDef { Kind = WeaponKind.Explosive, Damage = 55f, Splash = 3.6f, Suppression = 35f, Pierce = 0.4f, StructureMult = 1.2f };

        /// <summary>Применить способность выделенных отрядов к точке (enemy — отряд под курсором, для рывка).</summary>
        public static void UseAt(IReadOnlyList<Squad> squads, string id, Vector3 at, Squad enemy)
        {
            var gm = GroundBattleManager.Instance;
            if (gm == null) return;
            bool any = false;
            foreach (var sq in squads)
            {
                if (sq.Destroyed || sq.AbilityCooldown > 0f || System.Array.IndexOf(sq.Def.Abilities, id) < 0) continue;
                if (Use(gm, sq, id, at, enemy)) { any = true; if (id != "grenade") break; }
            }
            SFXManager.Play(any ? Core.Audio.Sfx.UiConfirm : Core.Audio.Sfx.UiDenied, 0.6f);
        }

        public static bool Use(GroundBattleManager gm, Squad sq, string id, Vector3 at, Squad enemy)
        {
            if (sq.Destroyed || sq.AbilityCooldown > 0f || sq.Retreating) return false;
            var leader = sq.Leader;
            if (leader == null) return false;
            float dist = Vector3.Distance(leader.Pos, at);
            switch (id)
            {
                case "grenade":
                {
                    if (dist > Range(id) + 2f) return false;
                    Vector3 target = gm.Map.Snap(at + Random.insideUnitSphere.WithY(0f) * 1.5f);
                    gm.Fx.Shell(leader.Chest, target, 1.1f, () => Combat.Explode(target, Grenade, sq, sq.Side.Setup.DamageMult));
                    sq.Shout(Loc.T("Граната!"));
                    break;
                }
                case "charge":
                {
                    var target = enemy ?? gm.NearestEnemySquad(sq, 30f);
                    if (target == null) return false;
                    sq.Charging = true;
                    sq.OrderAttack(target);
                    sq.Shout(GroundCallouts.BattleCry(sq));
                    gm.Sfx.Roar(leader.Pos);
                    gm.Fx.Delayed(4f, () => { sq.Charging = false; });
                    break;
                }
                case "vision":
                    gm.Hud?.BeginVision(sq.Side.Index, 10f);
                    sq.Shout(Loc.T("Я вижу их пути…"));
                    break;
                case "spores":
                {
                    if (dist > Range(id) + 2f) return false;
                    var p = gm.Map.Snap(at);
                    GoreFx.Dust(p, 3f);
                    foreach (var e in gm.Sides[1 - sq.Side.Index].Squads)
                        if (!e.Destroyed && !e.Def.Vehicle && (e.Center - p).sqrMagnitude < 36f) { e.RootedUntil = Time.time + 4f; e.Shout(Loc.T("Корни! Не вырваться!")); }
                    gm.Fx.Fire(p + Vector3.up * 0.3f, 0.3f);
                    break;
                }
                case "grove":
                {
                    if (dist > Range(id) + 2f) return false;
                    gm.GrowGrove(gm.Map.Snap(at));
                    break;
                }
                default: return false;
            }
            sq.AbilityCooldown = Cooldown(id);
            return true;
        }
    }
}
