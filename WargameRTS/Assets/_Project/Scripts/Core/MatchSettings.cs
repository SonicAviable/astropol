using UnityEngine;

namespace WargameRTS.Core
{
    public enum GameMode
    {
        /// <summary>Two armies, enemy AI, victory/defeat.</summary>
        Skirmish,
        /// <summary>Test mode: spawn anything for either side, time controls, cheats.</summary>
        Sandbox
    }

    /// <summary>Parameters chosen in the main menu and read by the battle scene.</summary>
    public static class MatchSettings
    {
        public const string MainMenuScene = "MainMenu";
        public const string BattleScene = "Battle";

        public static GameMode Mode = GameMode.Sandbox;
        public static int Seed = 1944;
        public static float MapSize = 2000f;
        public static bool FogOfWar = true;
        public static bool StartingArmies = true;
        public static bool EnemyAI = true;

        /// <summary>True once the main menu configured a match (false when Battle is played directly).</summary>
        public static bool ConfiguredFromMenu;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Mode = GameMode.Sandbox;
            Seed = 1944;
            MapSize = 2000f;
            FogOfWar = true;
            StartingArmies = true;
            EnemyAI = true;
            ConfiguredFromMenu = false;
        }

        public static void RandomizeSeed() => Seed = Random.Range(1, 99999);
    }

    /// <summary>Runtime switches of the current battle (toggled from the sandbox panel).</summary>
    public static class BattleRules
    {
        public static GameMode Mode = GameMode.Skirmish;
        public static bool EnemyAIEnabled = true;
        public static bool ControlBothSides;
        public static bool Invulnerable;

        public static bool IsSandbox => Mode == GameMode.Sandbox;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Reset(GameMode.Skirmish);

        public static void Reset(GameMode mode)
        {
            Mode = mode;
            EnemyAIEnabled = true;
            ControlBothSides = false;
            Invulnerable = false;
        }

        /// <summary>Whether the local player may select and command units of this team.</summary>
        public static bool CanCommand(Team team) => team == TeamExtensions.Player || (IsSandbox && ControlBothSides);
    }
}
