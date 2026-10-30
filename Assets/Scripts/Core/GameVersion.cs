namespace StellarisClone.Core
{
    /// <summary>
    /// Версия игры — одно место. Меню показывает её отсюда, а редактор при загрузке и перед сборкой записывает её
    /// в настройки проекта (PlayerSettings.bundleVersion), чтобы Application.version и сборка совпадали.
    /// </summary>
    public static class GameVersion
    {
        public const string Current = "1.6.5";
    }
}
