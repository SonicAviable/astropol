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

        [Header("Indirect fire (artillery)")]
        [Tooltip("Fires without line of sight at targets spotted by the team; shells fly with a delay.")]
        public bool Indirect;

        [Tooltip("Minimum range, meters (artillery cannot hit close targets).")]
        public float MinRange;

        [Tooltip("Splash radius, meters. HE damage falls off linearly to the edge.")]
        public float SplashRadius;

        [Tooltip("Shots per volley (rocket artillery) and seconds between them.")]
        public int Salvo = 1;
        public float SalvoInterval = 0.25f;

        [Tooltip("Tracer color and width (visual only).")]
        public Color TracerColor = new Color(1f, 0.85f, 0.4f);
        public float TracerWidth = 0.35f;

        public bool CanFireOnMove => MovingAccuracyMultiplier > 0f;

        /// <summary>Configures this weapon as artillery. Returns itself for chaining.</summary>
        public WeaponDefinition AsArtillery(float minRange, float splashRadius, int salvo = 1, float salvoInterval = 0.25f)
        {
            Indirect = true;
            MinRange = minRange;
            SplashRadius = splashRadius;
            Salvo = Mathf.Max(1, salvo);
            SalvoInterval = salvoInterval;
            MovingAccuracyMultiplier = 0f;
            return this;
        }

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
