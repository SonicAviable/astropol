using UnityEngine;

namespace WargameRTS.Core
{
    public enum Team
    {
        Blue = 0,
        Red = 1
    }

    public static class TeamExtensions
    {
        public const int Count = 2;

        /// <summary>The team controlled by the local player.</summary>
        public const Team Player = Team.Blue;

        public static Team Enemy(this Team team) => team == Team.Blue ? Team.Red : Team.Blue;

        public static Color ToColor(this Team team) =>
            team == Team.Blue ? new Color(0.30f, 0.55f, 1.00f) : new Color(1.00f, 0.32f, 0.25f);

        public static Color ToVehicleColor(this Team team) =>
            team == Team.Blue ? new Color(0.36f, 0.42f, 0.30f) : new Color(0.50f, 0.44f, 0.30f);

        public static string DisplayName(this Team team) => team == Team.Blue ? "Синие" : "Красные";
    }
}
