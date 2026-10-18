using UnityEngine;
using WargameRTS.Core;
using WargameRTS.Map;

namespace WargameRTS.Combat
{
    public static class LineOfSight
    {
        /// <summary>A forest deeper than this along the sight line blocks vision.</summary>
        public const float MaxForestDepth = 60f;

        public static bool Check(Vector3 eyeFrom, Vector3 eyeTo)
        {
            if (Physics.Linecast(eyeFrom, eyeTo, GameLayers.SightBlockersMask, QueryTriggerInteraction.Ignore))
                return false;

            var map = GameMap.Instance;
            return map == null || map.ForestLengthAlong(eyeFrom, eyeTo) <= MaxForestDepth;
        }
    }
}
