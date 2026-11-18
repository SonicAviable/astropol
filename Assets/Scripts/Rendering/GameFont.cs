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

        /// <summary>Обычный шрифт (IBM Plex Sans) — для текста, описаний, подписей. Нет файла — Play.</summary>
        public static Font Regular
        {
            get
            {
                if (_regular != null) return _regular;

                _regular = Resources.Load<Font>("Fonts/IBMPlexSans-Regular");
                if (_regular == null) _regular = Resources.Load<Font>("Fonts/Play-Regular");
                if (_regular == null)
                {
                    Debug.LogWarning("[GameFont] Play-Regular.ttf не найден в Resources/Fonts/. Использую fallback.");
                    _regular = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                            ?? Font.CreateDynamicFontFromOSFont("Arial", 14);
                }
                return _regular;
            }
        }

        /// <summary>Жирный шрифт (IBM Plex Sans SemiBold) — для подписей, вкладок, чисел. Нет файла — Play Bold.</summary>
        public static Font Bold
        {
            get
            {
                if (_bold != null) return _bold;

                _bold = Resources.Load<Font>("Fonts/IBMPlexSans-SemiBold");
                if (_bold == null) _bold = Resources.Load<Font>("Fonts/Play-Bold");
                if (_bold == null)
                {
                    // Если Bold нет — используем Regular
                    Debug.LogWarning("[GameFont] Play-Bold.ttf не найден, использую Regular.");
                    _bold = Regular;
                }
                return _bold;
            }
        }

        private static Font _display;

        /// <summary>Заголовочный шрифт «Атласа» (Spectral SemiBold) — заголовки окон и карточек, крупные числа. Нет файла — Russo One, затем Bold.</summary>
        public static Font Display
        {
            get
            {
                if (_display != null) return _display;
                _display = Resources.Load<Font>("Fonts/Spectral-SemiBold");
                if (_display == null) _display = Resources.Load<Font>("Fonts/RussoOne-Regular");
                if (_display == null) _display = Bold;
                return _display;
            }
        }

        private static Font _mono, _monoMedium;

        /// <summary>Моноширинный шрифт «Атласа» (IBM Plex Mono) — шкалы, даты, служебные подписи, кнопки. Нет файла — Regular.</summary>
        public static Font Mono
        {
            get
            {
                if (_mono != null) return _mono;
                _mono = Resources.Load<Font>("Fonts/IBMPlexMono-Regular");
                if (_mono == null) _mono = Regular;
                return _mono;
            }
        }

        /// <summary>Моноширинный полужирный (IBM Plex Mono Medium) — цифры и подписи кнопок. Нет файла — Mono.</summary>
        public static Font MonoMedium
        {
            get
            {
                if (_monoMedium != null) return _monoMedium;
                _monoMedium = Resources.Load<Font>("Fonts/IBMPlexMono-Medium");
                if (_monoMedium == null) _monoMedium = Mono;
                return _monoMedium;
            }
        }

        private static Font _serif, _serifItalic;

        /// <summary>Книжный шрифт с засечками (PT Serif) — летопись фракций. Нет файла — обычный Play.</summary>
        public static Font Serif
        {
            get
            {
                if (_serif != null) return _serif;
                _serif = Resources.Load<Font>("Fonts/PTSerif-Regular");
                if (_serif == null) _serif = Regular;
                return _serif;
            }
        }

        /// <summary>Курсив PT Serif — субтитры летописи. Нет файла — прямой Serif.</summary>
        public static Font SerifItalic
        {
            get
            {
                if (_serifItalic != null) return _serifItalic;
                _serifItalic = Resources.Load<Font>("Fonts/PTSerif-Italic");
                if (_serifItalic == null) _serifItalic = Serif;
                return _serifItalic;
            }
        }

        /// <summary>Сбросить кэш (на случай, если менял файлы во время игры).</summary>
        public static void Reload()
        {
            _regular = null;
            _bold = null;
            _display = null;
            _mono = _monoMedium = null;
            _serif = _serifItalic = null;
        }
    }
}