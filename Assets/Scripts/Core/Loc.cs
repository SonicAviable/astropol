using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Локализация. Исходный язык — русский: ключ перевода — сама русская строка (для строк с подстановками —
    /// шаблон string.Format: «Не хватает {0} влияния»). Переводы — Resources/Localization/en.txt,
    /// строки «ключ⇥перевод» (\n, \t и \\ экранированы).
    ///
    ///   Loc.T("Империя")                  → «Empire» в английской версии, «Империя» в русской
    ///   Loc.F("Не хватает {0} влияния", n) → «Not enough influence: {0}» с подстановкой
    ///
    /// Язык выбирается при запуске (настройка «Язык», по умолчанию — язык системы) и не меняется до перезапуска:
    /// многие названия собираются один раз при загрузке. Нет перевода — показывается русский текст.
    /// </summary>
    public static class Loc
    {
        public const string PrefKey = "set.lang";
        public static readonly string[] Codes = { "ru", "en" };
        public static readonly string[] Names = { "Русский", "English" };

        /// <summary>Язык этого запуска.</summary>
        public static string Current { get; private set; } = "ru";
        public static bool English => Current == "en";

        private static Dictionary<string, string> _table;
        private static HashSet<string> _missing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            string def = Application.systemLanguage == SystemLanguage.Russian || Application.systemLanguage == SystemLanguage.Ukrainian
                         || Application.systemLanguage == SystemLanguage.Belarusian ? "ru" : "en";
            Current = PlayerPrefs.GetString(PrefKey, def);
            if (Current != "en") Current = "ru";
            _table = null;
            _missing = null;
            if (English) Load();
        }

        /// <summary>Язык, выбранный в настройках (вступит в силу после перезапуска).</summary>
        public static string Saved => PlayerPrefs.GetString(PrefKey, Current);

        public static void Save(string code)
        {
            PlayerPrefs.SetString(PrefKey, code);
            PlayerPrefs.Save();
        }

        private static void Load()
        {
            _table = new Dictionary<string, string>(8192);
            _missing = new HashSet<string>();
            var asset = Resources.Load<TextAsset>("Localization/" + Current);
            if (asset == null) { Debug.LogWarning("[Loc] Нет файла перевода Resources/Localization/" + Current); return; }
            foreach (var raw in asset.text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                int tab = line.IndexOf('\t');
                if (tab <= 0) continue;
                string key = Unescape(line.Substring(0, tab));
                string val = Unescape(line.Substring(tab + 1));
                if (val.Length > 0) _table[key] = val;
            }
        }

        private static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n == 'r' ? '\r' : n);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Перевод строки (без подстановок).</summary>
        public static string T(string ru)
        {
            if (!English || string.IsNullOrEmpty(ru) || _table == null) return ru;
            if (_table.TryGetValue(ru, out var en)) return en;
            Missing(ru);
            return ru;
        }

        /// <summary>Перевод шаблона и подстановка: Loc.F("Осталось {0} дн.", days).</summary>
        public static string F(string ruFormat, params object[] args)
        {
            string fmt = T(ruFormat);
            try { return string.Format(fmt, args); }
            catch (System.FormatException)
            {
                // Ошибка в переводе (лишняя или пропавшая {n}) — показываем оригинал, а не падаем
                try { return string.Format(ruFormat, args); }
                catch (System.FormatException) { return ruFormat; }
            }
        }

        private static void Missing(string ru)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_missing != null && _missing.Add(ru) && _missing.Count <= 50)
                Debug.Log("[Loc] Нет перевода: " + ru);
#endif
        }
    }
}
