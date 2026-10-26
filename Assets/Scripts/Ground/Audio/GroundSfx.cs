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
                case WeaponKind.Energy: PlayAt(SfxId.WeaponEnergy, at, 0.45f, 1.6f); break;
                case WeaponKind.Sniper: PlayAt(SfxId.GroundSniper, at, 1f); break;
            }
        }

        public void Explosion(Vector3 at, float r)
        {
            PlayAt(r >= 3.5f ? SfxId.ExplosionLarge : SfxId.ExplosionSmall, at, Mathf.Clamp(0.5f + r * 0.12f, 0.5f, 1.2f), r >= 3.5f ? 0.85f : 1.05f);
        }

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
