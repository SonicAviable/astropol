using System.Collections.Generic;
using UnityEngine;

namespace WargameRTS.Core
{
    /// <summary>
    /// Arbitrates mouse input between game systems: a mode that wants the next
    /// click (order targeting, unit placement) captures input; menus block the game.
    /// </summary>
    public static class InputGate
    {
        static readonly HashSet<object> capturers = new HashSet<object>();
        static int consumedFrame = -1;

        /// <summary>A modal menu (pause, settings) is open: gameplay input is ignored.</summary>
        public static bool MenuOpen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            capturers.Clear();
            consumedFrame = -1;
            MenuOpen = false;
        }

        public static void SetCapture(object owner, bool capture)
        {
            if (capture)
                capturers.Add(owner);
            else
                capturers.Remove(owner);
        }

        public static bool IsCapturedBy(object owner) => capturers.Contains(owner);

        /// <summary>Marks the current frame's click/key as used so other systems ignore it.</summary>
        public static void Consume() => consumedFrame = Time.frameCount;

        public static bool ConsumedThisFrame => consumedFrame == Time.frameCount;

        /// <summary>True if plain selection clicks / Escape must be ignored this frame.</summary>
        public static bool BlocksSelection => MenuOpen || capturers.Count > 0 || ConsumedThisFrame;
    }
}
