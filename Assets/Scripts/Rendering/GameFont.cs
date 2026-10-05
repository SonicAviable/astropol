using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Единая точка загрузки шрифтов UI.
    /// Меняешь имя шрифта здесь — оно применяется везде во всех скриптах.
    /// </summary>
    public static class GameFont
    {
        private static Font _regular;
        private static Font _bold;

        /// <summary>Обычный шрифт — для текста, описаний, подписей.</summary>
        public static Font Regular
        {
            get
            {
                if (_regular != null) return _regular;

                _regular = Resources.Load<Font>("Fonts/Play-Regular");
                if (_regular == null)
                {
                    Debug.LogWarning("[GameFont] Play-Regular.ttf не найден в Resources/Fonts/. Использую fallback.");
                    _regular = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                            ?? Font.CreateDynamicFontFromOSFont("Arial", 14);
                }
                return _regular;
            }
        }

        /// <summary>Жирный шрифт — для заголовков, кнопок, чисел.</summary>
        public static Font Bold
        {
            get
            {
                if (_bold != null) return _bold;

                _bold = Resources.Load<Font>("Fonts/Play-Bold");
                if (_bold == null)
                {
                    // Если Bold нет — используем Regular
                    Debug.LogWarning("[GameFont] Play-Bold.ttf не найден, использую Regular.");
                    _bold = Regular;
                }
                return _bold;
            }
        }

        /// <summary>Сбросить кэш (на случай, если менял файлы во время игры).</summary>
        public static void Reload()
        {
            _regular = null;
            _bold = null;
        }
    }
}