using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Добивания (sync kills): короткая постановка из трёх фаз на кривых — рывок к жертве, удар (или подъём
    /// над землёй), бросок/отбрасывание с фонтаном крови. У героев и техники камера (по настройке) на 0,4 с
    /// уходит в замедление ×0,35 с наездом; каждая третья обычная казнь — без замедления.
    /// Шагоход Ксарна поднимает танк и бросает его.
    /// </summary>
    public static class SyncKills
    {
        private sealed class Kill
        {
            public Soldier A, V;
            public float T;
            public Vector3 Start, VStart;
            public int Variant;
            public bool Lifted, Struck;
        }

        private static readonly List<Kill> s_active = new List<Kill>();
        private static int s_counter;
        private static float s_slowUntil;
        private static SyncKillTicker s_ticker;

        public static bool SlowMotion => Time.unscaledTime < s_slowUntil;

        public static void Reset()
        {
            s_active.Clear();
            s_slowUntil = 0f;
        }

        public static void Begin(Soldier a, Soldier v)
        {
            if (a == null || v == null || a.Dead || v.Dead) return;
            a.InSyncKill = true;
            v.InSyncKill = true;
            a.Stop();
            v.Stop();
            if (v.Agent != null) v.Agent.enabled = false;
            if (a.Agent != null) a.Agent.enabled = false;
            var k = new Kill { A = a, V = v, Start = a.Pos, VStart = v.Pos, Variant = Random.Range(0, 3) };
            s_active.Add(k);
            if (s_ticker == null) s_ticker = new GameObject("[Ground] SyncKills").AddComponent<SyncKillTicker>();
            var gm = GroundBattleManager.Instance;
            gm?.Sfx.Roar(a.Pos);
            a.Squad.Shout(GroundCallouts.Execution(a.Squad));

            // Замедление: герои и техника; каждая третья обычная казнь — без него
            bool big = a.Def.Role == UnitRole.Hero || a.Def.Role == UnitRole.Walker || v.IsVehicle;
            s_counter++;
            if (GameSettings.KillCam && (big || s_counter % 3 != 0))
            {
                s_slowUntil = Time.unscaledTime + 0.4f;
                Time.timeScale = 0.35f;
                BattleCamera.Punch(v.Pos, 6f);
            }
        }

        private static readonly AnimationCurve Dash = new AnimationCurve(new Keyframe(0f, 0f, 0f, 2.5f), new Keyframe(1f, 1f, 0f, 0f));

        internal static void Tick()
        {
            float dt = Time.deltaTime;
            if (s_slowUntil > 0f && Time.unscaledTime >= s_slowUntil)
            {
                s_slowUntil = 0f;
                GroundBattleManager.Instance?.ApplyTimeScale();
            }
            for (int i = s_active.Count - 1; i >= 0; i--)
            {
                var k = s_active[i];
                if (k.A == null || k.V == null) { Finish(k); s_active.RemoveAt(i); continue; }
                k.T += dt;
                Vector3 dir = (k.VStart - k.Start); dir.y = 0f;
                if (dir.sqrMagnitude < 0.01f) dir = k.A.transform.forward;
                dir.Normalize();
                k.A.transform.rotation = Quaternion.LookRotation(dir);
                k.V.transform.rotation = Quaternion.LookRotation(-dir);
                float reach = k.V.IsVehicle ? 4f : 1.1f * Mathf.Max(1f, k.A.Def.Scale);

                // Фаза 1 (0–0,25 с): рывок
                if (k.T < 0.25f)
                {
                    float p = Dash.Evaluate(k.T / 0.25f);
                    k.A.transform.position = Vector3.Lerp(k.Start, k.VStart - dir * reach, p);
                    if (k.A.Rig?.ArmL != null) k.A.Rig.ArmL.localRotation = Quaternion.Euler(-160f * p, 20f, 0f);
                    continue;
                }
                // Фаза 2 (0,25–0,7 с): удар или подъём над землёй
                if (k.T < 0.7f)
                {
                    float p = (k.T - 0.25f) / 0.45f;
                    if (k.Variant == 0 || k.V.IsVehicle)
                    {
                        // Подъём: жертва (или танк) в воздухе
                        k.V.transform.position = k.VStart + Vector3.up * Mathf.Sin(p * Mathf.PI * 0.5f) * (k.V.IsVehicle ? 6f : 1.4f);
                        k.Lifted = true;
                    }
                    else if (k.A.Rig?.ArmL != null)
                    {
                        k.A.Rig.ArmL.localRotation = Quaternion.Euler(Mathf.Lerp(-160f, 40f, p), 20f, 0f);
                    }
                    if (p > 0.5f && !k.Struck) { k.Struck = true; GoreFx.BurstFor(k.V, 26); GroundBattleManager.Instance?.Sfx.Crunch(k.V.Pos); BattleCamera.Shake(k.V.Pos, 0.5f); }
                    continue;
                }
                // Фаза 3: бросок / отбрасывание и смерть
                Finish(k);
                s_active.RemoveAt(i);
            }
        }

        private static void Finish(Kill k)
        {
            if (k.A != null)
            {
                k.A.InSyncKill = false;
                if (k.A.Agent != null && !k.A.Dead)
                {
                    k.A.Agent.enabled = true;
                    if (k.A.Agent.isOnNavMesh) k.A.Agent.Warp(k.A.transform.position);
                }
            }
            if (k.V == null || k.V.Dead) return;
            k.V.InSyncKill = false;
            Vector3 dir = k.A != null ? (k.V.Pos - k.A.Pos).WithY(0f).normalized : Vector3.forward;
            if (k.V.IsVehicle)
            {
                // Танк брошен: летит и взрывается при падении
                var gm = GroundBattleManager.Instance;
                var v = k.V;
                Vector3 land = gm != null ? gm.Map.Snap(v.Pos + dir * 10f) : v.Pos;
                v.transform.position = land + Vector3.up * 0.2f;
                v.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 180f);
            }
            Combat.Kill(k.V, k.V.IsVehicle ? Combat.CrushDeath : (k.Lifted ? Combat.CrushDeath : new WeaponDef { Kind = WeaponKind.Melee }), dir + Vector3.up, k.A != null ? k.A.Squad : null);
        }
    }

    public sealed class SyncKillTicker : MonoBehaviour
    {
        private void Update() => SyncKills.Tick();
    }
}
