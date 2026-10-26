using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Тактический ИИ стороны. Раз в пару секунд: распределяет свободные отряды по точкам (ближайшие ничьи и вражеские,
    /// защитник прикрывает свои), стягивает силы к самой «горячей» точке, отводит сломленные отряды к маяку,
    /// заказывает подкрепления капсулами, бьёт с орбиты по скоплениям и пользуется способностями отрядов.
    /// Играет по тем же правилам, что и игрок; сложность — от <see cref="AIDifficulty"/>.
    /// </summary>
    public sealed class GroundCommanderAI
    {
        private readonly BattleSide _side;
        private float _think = 2f, _buy = 6f, _orbital = 30f, _special = 20f;
        private readonly int _tier;
        private readonly Dictionary<Squad, StrategicPoint> _assigned = new Dictionary<Squad, StrategicPoint>();
        private readonly List<StrategicPoint> _targets = new List<StrategicPoint>();

        public GroundCommanderAI(BattleSide side)
        {
            _side = side;
            _tier = Mathf.Clamp(AIDifficulty.Tier, 0, 4);
            // Сложнее — шустрее думает и чуть богаче
            _side.IncomeMult *= 0.85f + 0.075f * _tier;
        }

        private float ThinkInterval => 3.2f - 0.4f * _tier;

        public void Tick(float dt)
        {
            var gm = GroundBattleManager.Instance;
            if (gm == null || gm.Map == null) return;
            if ((_think -= dt) <= 0f) { _think = ThinkInterval; Think(gm); }
            if ((_buy -= dt) <= 0f) { _buy = 5f - 0.5f * _tier; Buy(gm); }
            if ((_orbital -= dt) <= 0f) { _orbital = 6f; TryOrbital(gm); }
            if ((_special -= dt) <= 0f) { _special = 10f; if (_side.SpecialCooldown <= 0f) gm.UseFactionSpecial(_side); }
        }

        // ================================================================ Приказы

        private void Think(GroundBattleManager gm)
        {
            var map = gm.Map;
            bool defender = _side.Index == 1;
            int me = _side.Index;

            _targets.Clear();
            foreach (var p in map.Points) if (p.Owner != me) _targets.Add(p);
            // Защитнику нужны и свои точки — держать
            StrategicPoint threatened = null;
            float threat = 0f;
            foreach (var p in map.Points)
            {
                if (p.Owner != me) continue;
                float t = EnemyPressure(gm, p.Pos, 24f);
                if (t > threat) { threat = t; threatened = p; }
            }

            var stale = new List<Squad>();
            foreach (var kv in _assigned) if (kv.Key.Destroyed || kv.Value.Owner == me && kv.Value != threatened) stale.Add(kv.Key);
            foreach (var s in stale) _assigned.Remove(s);

            foreach (var sq in _side.Squads)
            {
                if (sq.Destroyed || sq.Retreating) continue;
                if (sq.Def.Role == UnitRole.Artillery) { ArtilleryThink(gm, sq); continue; }

                // Отступление при низкой морали или потерях — сохранить отряд
                if (!sq.Def.Fearless && (sq.Morale < sq.MaxMorale * 0.25f || sq.Alive <= Mathf.Max(1, sq.Def.Size / 3) && !sq.Def.Vehicle && sq.Def.Size > 2))
                {
                    if (!gm.NearReinforcePoint(sq)) { sq.OrderRetreat(); _assigned.Remove(sq); continue; }
                }

                UseAbilities(gm, sq);

                // Уже воюет — не дёргаем (кроме рукопашников, они и так сближаются)
                if (sq.Engaged != null && sq.Order != SquadOrder.Move) continue;

                // Защита своей точки под натиском
                if (threatened != null && threat > 0f && (defender || Random.value < 0.4f) && (sq.Center - threatened.Pos).sqrMagnitude < 70f * 70f
                    && (!_assigned.TryGetValue(sq, out var cur) || cur != threatened))
                {
                    _assigned[sq] = threatened;
                    sq.OrderMove(Around(gm, threatened.Pos, 6f), true);
                    continue;
                }

                if (_assigned.TryGetValue(sq, out var tgt) && tgt.Owner != me) continue;

                var pick = PickPoint(gm, sq);
                if (pick != null)
                {
                    _assigned[sq] = pick;
                    sq.OrderMove(Around(gm, pick.Pos, 5f), true);
                }
                else if (!defender && map.Hq != null && !map.Hq.Dead && !gm.ShieldUp)
                {
                    // Все точки наши — на штаб
                    sq.OrderAttack(map.Hq);
                }
            }
        }

        /// <summary>Ближайшая нужная точка: ничьи дешевле, вражеские — если хватает сил; не больше трёх отрядов на точку.</summary>
        private StrategicPoint PickPoint(GroundBattleManager gm, Squad sq)
        {
            StrategicPoint best = null;
            float bs = float.MaxValue;
            var c = sq.Center;
            foreach (var p in _targets)
            {
                int already = 0;
                foreach (var kv in _assigned) if (kv.Value == p && !kv.Key.Destroyed) already++;
                if (already >= 3) continue;
                float d = Vector3.Distance(c, p.Pos);
                float score = d * (p.Owner < 0 ? 1f : 1.35f) + already * 18f + EnemyPressure(gm, p.Pos, 20f) * 6f;
                if (p.Kind == PointKind.Generator || p.Kind == PointKind.Mining) score *= 0.9f;
                if (score < bs) { bs = score; best = p; }
            }
            return best;
        }

        private float EnemyPressure(GroundBattleManager gm, Vector3 at, float r)
        {
            float n = 0f;
            foreach (var e in gm.Sides[1 - _side.Index].Squads)
                if (!e.Destroyed && e.VisibleTo[_side.Index] && (e.Center - at).sqrMagnitude < r * r)
                    n += e.Def.Vehicle ? 3f : e.Alive * 0.5f;
            return n;
        }

        private static Vector3 Around(GroundBattleManager gm, Vector3 p, float r)
            => gm.Map.Clamp(p + new Vector3(Random.Range(-r, r), 0f, Random.Range(-r, r)));

        private void ArtilleryThink(GroundBattleManager gm, Squad sq)
        {
            // Артиллерия держится позади, цель берёт сама (нужна видимость союзником)
            var target = gm.NearestEnemySquad(sq, 120f);
            if (target != null) { if (sq.AttackTarget != target) sq.OrderAttack(target); return; }
            if (sq.Order == SquadOrder.None && (sq.Center - _side.Beacon).sqrMagnitude > 30f * 30f)
                sq.OrderMove(Around(gm, _side.Beacon, 8f), false);
        }

        // ================================================================ Способности

        private void UseAbilities(GroundBattleManager gm, Squad sq)
        {
            if (sq.AbilityCooldown > 0f || sq.Def.Abilities == null || sq.Def.Abilities.Length == 0) return;
            if (Random.value > 0.35f + 0.1f * _tier) return;
            foreach (var id in sq.Def.Abilities)
            {
                switch (id)
                {
                    case "grenade":
                    {
                        var e = gm.NearestEnemySquad(sq, Abilities.Range(id));
                        if (e != null && !e.Def.Vehicle && (e.InCover || e.Alive >= 3) && Abilities.Use(gm, sq, id, e.Center, e)) return;
                        break;
                    }
                    case "charge":
                    {
                        var e = gm.NearestEnemySquad(sq, 26f);
                        if (e != null && !e.Def.Vehicle && Abilities.Use(gm, sq, id, e.Center, e)) return;
                        break;
                    }
                    case "vision":
                        if (sq.Engaged != null && Abilities.Use(gm, sq, id, sq.Center, null)) return;
                        break;
                    case "spores":
                    {
                        var e = gm.NearestEnemySquad(sq, Abilities.Range(id));
                        if (e != null && !e.Def.Vehicle && Abilities.Use(gm, sq, id, e.Center, e)) return;
                        break;
                    }
                    case "grove":
                        if (sq.Engaged != null && !sq.InCover)
                        {
                            var dir = (sq.Engaged.Center - sq.Center).normalized;
                            if (Abilities.Use(gm, sq, id, sq.Center + dir * 4f, null)) return;
                        }
                        break;
                }
            }
        }

        // ================================================================ Подкрепления и орбита

        private void Buy(GroundBattleManager gm)
        {
            var list = UnitCatalog.Buyable(_side.FactionKey);
            if (list.Count == 0) return;
            // Чего не хватает: против техники — ракетчики, против окопавшихся — тяжёлая пехота/огнемёт, иначе по кругу
            int enemyVehicles = 0, enemyInCover = 0;
            foreach (var e in gm.Sides[1 - _side.Index].Squads)
            {
                if (e.Destroyed || !e.VisibleTo[_side.Index]) continue;
                if (e.Def.Vehicle) enemyVehicles++;
                else if (e.InCover) enemyInCover++;
            }
            int mine = 0, myAT = 0;
            foreach (var s in _side.Squads)
            {
                if (s.Destroyed) continue;
                mine++;
                if (s.Def.Role == UnitRole.Rocket) myAT++;
            }

            UnitDef want = null;
            if (enemyVehicles > myAT) want = Find(list, UnitRole.Rocket);
            if (want == null && enemyInCover >= 2) want = Find(list, Random.value < 0.5f ? UnitRole.Flamer : UnitRole.Heavy);
            if (want == null && mine < 3) want = Find(list, UnitRole.Assault);
            if (want == null)
            {
                // Случайная покупка, перекос к пехоте; технику берём, когда богаты
                var pool = new List<UnitDef>();
                foreach (var d in list)
                {
                    if (d.Role == UnitRole.Mercenary) continue;
                    if (d.Vehicle && _side.Requisition < d.Requisition + 150f) continue;
                    int w = d.Vehicle ? 1 : d.Role == UnitRole.Assault ? 4 : 2;
                    for (int i = 0; i < w; i++) pool.Add(d);
                }
                if (pool.Count > 0) want = pool[Random.Range(0, pool.Count)];
            }
            if (want == null || !gm.CanAfford(_side, want, out _)) return;

            // Капсула — к своей точке поближе к фронту, иначе у маяка
            Vector3 at = _side.Beacon + new Vector3(Random.Range(-10f, 10f), 0f, Random.Range(-4f, 10f) * (_side.Index == 0 ? 1f : -1f));
            float best = float.MaxValue;
            var enemyBeacon = gm.Sides[1 - _side.Index].Beacon;
            foreach (var p in gm.Map.Points)
            {
                if (p.Owner != _side.Index || EnemyPressure(gm, p.Pos, 18f) > 0f) continue;
                float d = (p.Pos - enemyBeacon).sqrMagnitude;
                if (d < best) { best = d; at = p.Pos + new Vector3(Random.Range(-6f, 6f), 0f, Random.Range(-6f, 6f)); }
            }
            gm.OrderReinforcement(_side, want, at);
        }

        private static UnitDef Find(List<UnitDef> list, UnitRole role)
        {
            foreach (var d in list) if (d.Role == role) return d;
            return null;
        }

        private void TryOrbital(GroundBattleManager gm)
        {
            if (_side.OrbitalCooldown > 0f || _side.Energy < GroundBattleManager.OrbitalEnergy) return;
            if (_side.Index == 1) return;   // у защитника нет флота на орбите
            // Скопление врага (≥2 отрядов в радиусе 10 м) вдали от своих
            Squad best = null;
            int bestN = 1;
            var enemies = gm.Sides[1 - _side.Index].Squads;
            foreach (var e in enemies)
            {
                if (e.Destroyed || !e.VisibleTo[_side.Index]) continue;
                int n = 0;
                foreach (var o in enemies) if (!o.Destroyed && (o.Center - e.Center).sqrMagnitude < 100f) n++;
                bool friendly = false;
                foreach (var m in _side.Squads) if (!m.Destroyed && (m.Center - e.Center).sqrMagnitude < 144f) { friendly = true; break; }
                if (!friendly && n > bestN) { bestN = n; best = e; }
            }
            if (best != null) gm.OrbitalStrike(_side, best.Center);
        }
    }
}
