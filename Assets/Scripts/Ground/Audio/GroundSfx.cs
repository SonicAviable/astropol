using UnityEngine;
using StellarisClone.Core;
using SfxId = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Звук наземного боя через SFXManager (процедурный синтез SfxLibrary): громкость гаснет с расстоянием
    /// до фокуса камеры, панорама — по экрану. Выстрелы с разбросом высоты, частые очереди прореживаются,
    /// чтобы 200 стволов не превращались в шум.
    /// </summary>
    public sealed class GroundSfx
    {
        private float _rifleBudget;
        private float _lastRifle;

        private static bool Locate(Vector3 at, out float vol, out float pan)
        {
            vol = 0f; pan = 0f;
            var cam = BattleCamera.Instance;
            if (cam == null || cam.Cam == null) return false;
            float d = Vector3.Distance(at, cam.Focus);
            vol = Mathf.Clamp01(1.15f - d / 140f);
            if (vol <= 0.02f) return false;
            var sp = cam.Cam.WorldToViewportPoint(at);
            pan = Mathf.Clamp((sp.x - 0.5f) * 1.4f, -0.85f, 0.85f);
            return true;
        }

        public void Play(SfxId id, float vol, float pitch = 1f) => SFXManager.Play(id, vol, pitch);

        public static void PlayAt(SfxId id, Vector3 at, float vol = 1f, float pitch = 1f)
        {
            if (!Locate(at, out float v, out float pan)) return;
            SFXManager.Play(id, vol * v, pitch * Random.Range(0.94f, 1.06f), pan);
        }

        public void Shot(WeaponDef w, Vector3 at, int side)
        {
            switch (w.Kind)
            {
                case WeaponKind.Bullet:
                    // Прореживание: не больше ~25 винтовочных выстрелов в секунду
                    float now = Time.unscaledTime;
                    _rifleBudget = Mathf.Min(6f, _rifleBudget + (now - _lastRifle) * 25f);
                    _lastRifle = now;
                    if (_rifleBudget < 1f) return;
                    _rifleBudget -= 1f;
                    PlayAt(SfxId.GroundRifle, at, 0.9f, side == 0 ? 1f : 0.92f);
                    break;
                case WeaponKind.Heavy: PlayAt(SfxId.GroundMg, at, 0.8f); break;
                case WeaponKind.Explosive: PlayAt(w.Indirect || w.Damage > 100f ? SfxId.GroundCannon : SfxId.WeaponMissile, at, 0.9f); break;
                case WeaponKind.Flame: PlayAt(SfxId.GroundFlamer, at, 0.8f); break;
                case WeaponKind.Energy: PlayAt(SfxId.GroundRifle, at, 0.6f, 1.25f); PlayAt(SfxId.WeaponEnergy, at, 0.18f, 0.9f); break;
                case WeaponKind.Sniper: PlayAt(SfxId.GroundSniper, at, 1f); break;
            }
        }

        public void Explosion(Vector3 at, float r)
        {
            // Взрыв на земле — с дождём комьев; крупные (снаряды, орбита, техника) — тяжелее и ниже
            bool big = r >= 3.5f;
            PlayAt(big ? SfxId.GroundBlastBig : SfxId.GroundBlast, at, Mathf.Clamp(0.6f + r * 0.1f, 0.6f, 1.25f), big ? Mathf.Lerp(1f, 0.8f, Mathf.Clamp01((r - 3.5f) / 6f)) : 1.05f);
        }

        private static float s_impactBudget, s_lastImpact;

        /// <summary>Попадание или промах: шлепок в грунт, изредка — вой рикошета (прореживается, чтобы не трещало).</summary>
        public static void Impact(Vector3 at, WeaponDef w)
        {
            if (w == null || w.Kind == WeaponKind.Flame || w.Kind == WeaponKind.Explosive || w.Kind == WeaponKind.Melee) return;
            float now = Time.unscaledTime;
            s_impactBudget = Mathf.Min(4f, s_impactBudget + (now - s_lastImpact) * 10f);
            s_lastImpact = now;
            if (s_impactBudget < 1f) return;
            s_impactBudget -= 1f;
            if (Random.value < 0.06f) PlayAt(SfxId.GroundRicochet, at, 0.8f);
            else PlayAt(SfxId.GroundImpact, at, 0.7f);
        }

        /// <summary>Отклик рации на приказ (вместо интерфейсного писка).</summary>
        public static void Radio(float vol = 0.8f) => SFXManager.Play(SfxId.GroundRadio, vol);

        public void Melee(Vector3 at) => PlayAt(SfxId.HitArmor, at, 0.6f, 1.4f);
        public void Crunch(Vector3 at) => PlayAt(SfxId.GroundCrunch, at, 1f);
        public void Roar(Vector3 at) => PlayAt(SfxId.GroundRoar, at, 1f);

        public static void Whistle(Vector3 at, float leadTime)
        {
            if (leadTime > 1.3f) SFXManager.PlayDelayed(SfxId.ShellWhistle, leadTime - 1.25f, 0.8f);
            else PlayAt(SfxId.ShellWhistle, at, 0.8f);
        }

        public static void Charge(Vector3 at) => PlayAt(SfxId.HoloScan, at, 1f, 0.5f);

        public static void Scream(Soldier v)
        {
            if (v == null || v.IsVehicle || v.Def.Role == UnitRole.Swarm) return;
            if (v.Squad.Side.Setup.Blood == BloodKind.Coolant) { PlayAt(SfxId.HitArmor, v.Pos, 0.5f, 1.8f); return; }
            if (Random.value > 0.55f) return;
            PlayAt(SfxId.GroundScream, v.Pos, 0.85f, v.Def.Role == UnitRole.Heavy || v.Def.Role == UnitRole.Hero ? 0.8f : 1f);
        }
    }
}
