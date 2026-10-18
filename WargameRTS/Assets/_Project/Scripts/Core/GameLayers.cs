using UnityEngine;

namespace WargameRTS.Core
{
    /// <summary>
    /// Physics layers declared in ProjectSettings/TagManager.asset.
    /// Falls back to Default if a layer is missing so the game still runs.
    /// </summary>
    public static class GameLayers
    {
        public static int Ground => Resolve("Ground");
        public static int Units => Resolve("Units");
        public static int Obstacles => Resolve("Obstacles");

        public static int GroundMask => 1 << Ground;
        public static int UnitsMask => 1 << Units;
        public static int ObstaclesMask => 1 << Obstacles;

        /// <summary>Everything that blocks line of sight.</summary>
        public static int SightBlockersMask => GroundMask | ObstaclesMask;

        static int Resolve(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            return layer < 0 ? 0 : layer;
        }
    }
}
