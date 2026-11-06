using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Esc обрабатывается один раз за кадр. Окно, закрывшее себя по Esc в своём Update, отмечает это здесь —
    /// общий обработчик (GameFlowUI) в том же кадре уже не откроет меню паузы, в каком бы порядке ни шли их Update.
    /// </summary>
    public static class UIEsc
    {
        private static int s_frame = -1;

        public static void Consume() => s_frame = Time.frameCount;

        public static bool ConsumedThisFrame => s_frame == Time.frameCount;
    }
}
