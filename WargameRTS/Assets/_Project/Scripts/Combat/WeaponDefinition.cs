using UnityEngine;

namespace WargameRTS.Combat
{
    /// <summary>
    /// Weapon stats. Damage model (see <see cref="CombatMath"/>):
    /// armour-piercing part works only if <see cref="ArmorPiercing"/> beats the armour,
    /// high-explosive part is reduced by armour and is the main damage against infantry.
    /// </summary>
    [CreateAssetMenu(menuName = "WargameRTS/Weapon Definition", fileName = "Weapon")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        public string DisplayName = "Weapon";

        [Tooltip("Maximum range, meters.")]
        public float Range = 1000f;

        [Tooltip("Armour-piercing value. 0 = no AP (small arms, HE).")]
        public int ArmorPiercing;

        [Tooltip("High-explosive / small-arms damage per hit against unarmoured targets.")]
        public float HighExplosive = 1f;

        [Range(0f, 1f)] public float Accuracy = 0.5f;

        [Tooltip("Accuracy multiplier while the shooter moves. 0 = cannot fire on the move.")]
        [Range(0f, 1f)] public float MovingAccuracyMultiplier = 0.4f;

        [Tooltip("Seconds between shots.")]
        public float FireInterval = 5f;

        [Tooltip("Tracer color and width (visual only).")]
        public Color TracerColor = new Color(1f, 0.85f, 0.4f);
        public float TracerWidth = 0.35f;

        public bool CanFireOnMove => MovingAccuracyMultiplier > 0f;

        public static WeaponDefinition Create(string displayName, float range, int armorPiercing, float highExplosive,
            float accuracy, float movingMultiplier, float fireInterval, Color tracerColor, float tracerWidth)
        {
            var weapon = CreateInstance<WeaponDefinition>();
            weapon.name = displayName;
            weapon.DisplayName = displayName;
            weapon.Range = range;
            weapon.ArmorPiercing = armorPiercing;
            weapon.HighExplosive = highExplosive;
            weapon.Accuracy = accuracy;
            weapon.MovingAccuracyMultiplier = movingMultiplier;
            weapon.FireInterval = fireInterval;
            weapon.TracerColor = tracerColor;
            weapon.TracerWidth = tracerWidth;
            return weapon;
        }
    }
}
