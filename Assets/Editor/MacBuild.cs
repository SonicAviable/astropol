using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Сборка игры для macOS — одной командой из меню «Astropolity».
///
///   • «Собрать для Mac (Intel)» — для Mac на процессоре Intel (2019 и старше);
///   • «Собрать для Mac (Intel + Apple Silicon)» — универсальная сборка для любых Mac.
///
/// Собирать можно на Windows: нужен модуль «Mac Build Support (Mono)» в Unity Hub.
/// На выходе — Builds/macOS/Astropolity-macOS.tar.gz. Это не zip намеренно: zip, собранный на Windows,
/// теряет у программы признак «исполняемый», и на Mac игра не запускается. tar.gz хранит права файлов,
/// а Mac распаковывает его двойным щелчком. Рядом кладётся «Как запустить.txt».
///
/// Имя программы и идентификатор меняются только на время сборки — сохранения на Windows не затрагиваются.
/// Unity при сборке под другую систему переключает активную платформу редактора — после сборки она
/// возвращается обратно, иначе обычная кнопка Build продолжала бы собирать под Mac.
/// Для GitHub Actions (game-ci): buildMethod MacBuild.BuildFromCommandLine.
/// </summary>
public static class MacBuild
{
    private const string AppName = "Astropolity";
    private const string BundleId = "com.astropolity.game";
    private const string OutDir = "Builds/macOS";

    [MenuItem("Astropolity/Собрать для Mac (Intel)", priority = 1)]
    public static void BuildIntel() => Build(universal: false, interactive: true);

    [MenuItem("Astropolity/Собрать для Mac (Intel + Apple Silicon)", priority = 2)]
    public static void BuildUniversal() => Build(universal: true, interactive: true);

    /// <summary>Сборка из командной строки / CI. Аргумент -macUniversal — универсальная сборка.</summary>
    public static void BuildFromCommandLine()
    {
        bool universal = Environment.GetCommandLineArgs().Contains("-macUniversal");
        bool ok = Build(universal, interactive: false);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    private static bool Build(bool universal, bool interactive)
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX))
        {
            const string msg = "Не установлен модуль сборки для Mac.\n\nUnity Hub → Installs → ваша версия Unity → Add modules → " +
                               "«Mac Build Support (Mono)». После установки перезапустите Unity и повторите.";
            Debug.LogError("[MacBuild] " + msg);
            if (interactive) EditorUtility.DisplayDialog("Сборка для Mac", msg, "Понятно");
            return false;
        }

        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            Debug.LogError("[MacBuild] В Build Settings нет включённых сцен");
            return false;
        }

        string root = Path.GetFullPath(OutDir);
        string app = Path.Combine(root, AppName + ".app");
        if (Directory.Exists(app)) Directory.Delete(app, true);
        Directory.CreateDirectory(root);

        var standalone = NamedBuildTarget.Standalone;
        var oldTarget = EditorUserBuildSettings.activeBuildTarget;
        var oldGroup = BuildPipeline.GetBuildTargetGroup(oldTarget);
        string oldName = PlayerSettings.productName;
        string oldId = PlayerSettings.GetApplicationIdentifier(standalone);
        var oldBackend = PlayerSettings.GetScriptingBackend(standalone);
        bool ok = false;
        try
        {
            PlayerSettings.productName = AppName;
            PlayerSettings.SetApplicationIdentifier(standalone, BundleId);
            // IL2CPP для Mac собирается только на самом Mac — Mono работает с любой системы
            PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.Mono2x);
            SetArchitecture(universal ? "x64ARM64" : "x64");

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = app,
                target = BuildTarget.StandaloneOSX,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            });
            ok = report.summary.result == BuildResult.Succeeded;
            if (!ok)
            {
                Debug.LogError($"[MacBuild] Сборка не удалась: {report.summary.result}, ошибок: {report.summary.totalErrors}");
                if (interactive) EditorUtility.DisplayDialog("Сборка для Mac", "Сборка не удалась — подробности в Console.", "OK");
                return false;
            }

            File.WriteAllText(Path.Combine(root, "Как запустить.txt"), HowTo(universal), new UTF8Encoding(false));
            string archive = Path.Combine(root, AppName + "-macOS.tar.gz");
            TarGz.Pack(archive, root, new[] { AppName + ".app", "Как запустить.txt" });

            long mb = new FileInfo(archive).Length / (1024 * 1024);
            Debug.Log($"[MacBuild] Готово: {archive} ({mb} МБ)");
            if (interactive)
            {
                EditorUtility.DisplayDialog("Сборка для Mac",
                    $"Готово!\n\n{archive}\n({mb} МБ)\n\nПеренесите этот файл на Mac и следуйте «Как запустить.txt» внутри архива.", "Открыть папку");
                EditorUtility.RevealInFinder(archive);
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            if (interactive) EditorUtility.DisplayDialog("Сборка для Mac", "Ошибка: " + e.Message, "OK");
            return false;
        }
        finally
        {
            PlayerSettings.productName = oldName;
            PlayerSettings.SetApplicationIdentifier(standalone, oldId);
            PlayerSettings.SetScriptingBackend(standalone, oldBackend);
            AssetDatabase.SaveAssets();
            if (interactive && EditorUserBuildSettings.activeBuildTarget != oldTarget)
                EditorUserBuildSettings.SwitchActiveBuildTarget(oldGroup, oldTarget);
        }
    }

    /// <summary>
    /// Архитектура Mac-сборки. Класс настроек живёт в модуле Mac Build Support, поэтому — через отражение:
    /// без модуля проект всё равно компилируется.
    /// </summary>
    private static void SetArchitecture(string value)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("UnityEditor.OSXStandalone.UserBuildSettings", false);
            var p = t?.GetProperty("architecture", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (p == null || !p.CanWrite || !p.PropertyType.IsEnum) continue;
            try
            {
                p.SetValue(null, Enum.Parse(p.PropertyType, value));
                Debug.Log($"[MacBuild] Архитектура: {value}");
                return;
            }
            catch (Exception e) { Debug.LogWarning("[MacBuild] Не удалось задать архитектуру: " + e.Message); }
        }
        Debug.LogWarning("[MacBuild] Настройка архитектуры не найдена — используется текущая из Build Settings");
    }

    private static string HowTo(bool universal) =>
$@"ASTROPOLITY ДЛЯ MAC
Сборка: {(universal ? "универсальная (Intel и Apple Silicon)" : "для Mac на процессоре Intel")}
Нужно: macOS 12 Monterey или новее.

УСТАНОВКА
1. Перетащите Astropolity.app в папку «Программы» (Applications).

ПЕРВЫЙ ЗАПУСК
Игра не подписана сертификатом Apple, поэтому при первом запуске macOS её заблокирует.
Это нормально для игр не из App Store. Разрешить можно одним из способов.

Способ А (через Терминал — самый надёжный):
  1. Откройте Терминал (Программы → Утилиты → Терминал).
  2. Вставьте строку и нажмите Enter:
       xattr -cr /Applications/Astropolity.app
  3. Запустите игру обычным двойным щелчком.

Способ Б (без Терминала):
  1. Дважды щёлкните по Astropolity.app — появится предупреждение, нажмите «Готово» / «Отмена».
  2. Откройте Системные настройки → Конфиденциальность и безопасность.
  3. Внизу будет строка про Astropolity — нажмите «Всё равно открыть» и подтвердите паролем.
  (На macOS 14 и старше можно проще: правый щелчок по игре → «Открыть» → «Открыть».)

ЕСЛИ НЕ ЗАПУСКАЕТСЯ («Не удаётся открыть программу», «программа повреждена» или ничего не происходит)
  В Терминале, по одной строке:
    xattr -cr /Applications/Astropolity.app
    chmod +x /Applications/Astropolity.app/Contents/MacOS/*
    codesign --force --deep --sign - /Applications/Astropolity.app
  Последняя строка заново подписывает игру «локально» — подпись, собранная на Windows, macOS не устраивает.
  Если всё равно не открывается, запустите напрямую — Терминал покажет причину:
    /Applications/Astropolity.app/Contents/MacOS/Astropolity

ГДЕ СОХРАНЕНИЯ
  ~/Library/Application Support/{PlayerSettings.companyName}/{AppName}/Saves
  (в Finder: Переход → Переход к папке… и вставьте путь)

СОВЕТ ДЛЯ MAC 2019 ГОДА
  Если игра идёт рывками — Настройки → Графика: снизьте качество и разрешение,
  выключите вертикальную синхронизацию. На встроенной графике Intel лучше играть в окне 1600×900 или 1280×720.
";

    /// <summary>
    /// Минимальный упаковщик tar.gz (ustar + длинные имена GNU), который сохраняет права файлов:
    /// программе в Contents/MacOS и библиотекам .dylib — 755, остальному — 644.
    /// </summary>
    private static class TarGz
    {
        public static void Pack(string archive, string baseDir, string[] entries)
        {
            if (File.Exists(archive)) File.Delete(archive);
            long mtime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using (var fs = File.Create(archive))
            using (var gz = new GZipStream(fs, System.IO.Compression.CompressionLevel.Optimal))
            {
                foreach (string e in entries)
                {
                    string full = Path.Combine(baseDir, e);
                    if (Directory.Exists(full)) AddDir(gz, baseDir, full, mtime);
                    else if (File.Exists(full)) AddFile(gz, baseDir, full, mtime);
                }
                gz.Write(new byte[1024], 0, 1024);   // конец архива — два пустых блока
            }
        }

        private static string Rel(string baseDir, string path)
            => path.Substring(baseDir.Length).TrimStart('/', '\\').Replace('\\', '/');

        private static void AddDir(Stream s, string baseDir, string dir, long mtime)
        {
            WriteHeader(s, Rel(baseDir, dir) + "/", 0x1ED /*755*/, 0, mtime, '5');
            foreach (string f in Directory.GetFiles(dir).OrderBy(x => x, StringComparer.Ordinal)) AddFile(s, baseDir, f, mtime);
            foreach (string d in Directory.GetDirectories(dir).OrderBy(x => x, StringComparer.Ordinal)) AddDir(s, baseDir, d, mtime);
        }

        private static void AddFile(Stream s, string baseDir, string file, long mtime)
        {
            string rel = Rel(baseDir, file);
            bool exec = rel.Contains(".app/Contents/MacOS/") || rel.EndsWith(".dylib", StringComparison.Ordinal);
            long size = new FileInfo(file).Length;
            WriteHeader(s, rel, exec ? 0x1ED /*755*/ : 0x1A4 /*644*/, size, mtime, '0');
            using (var src = File.OpenRead(file)) src.CopyTo(s);
            int pad = (int)((512 - size % 512) % 512);
            if (pad > 0) s.Write(new byte[pad], 0, pad);
        }

        private static void WriteHeader(Stream s, string name, int mode, long size, long mtime, char type)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            if (nameBytes.Length > 100)
            {
                // Длинное имя: запись GNU «././@LongLink» с полным именем перед основной
                byte[] full = new byte[nameBytes.Length + 1];
                Buffer.BlockCopy(nameBytes, 0, full, 0, nameBytes.Length);
                s.Write(Block(Encoding.ASCII.GetBytes("././@LongLink"), 0x1A4, full.Length, 0, 'L'), 0, 512);
                s.Write(full, 0, full.Length);
                int pad = (512 - full.Length % 512) % 512;
                if (pad > 0) s.Write(new byte[pad], 0, pad);
                nameBytes = nameBytes.Take(100).ToArray();
            }
            s.Write(Block(nameBytes, mode, size, mtime, type), 0, 512);
        }

        private static byte[] Block(byte[] name, int mode, long size, long mtime, char type)
        {
            var h = new byte[512];
            Buffer.BlockCopy(name, 0, h, 0, Math.Min(100, name.Length));
            Octal(h, 100, 8, mode);
            Octal(h, 108, 8, 0);                     // uid
            Octal(h, 116, 8, 0);                     // gid
            Octal(h, 124, 12, size);
            Octal(h, 136, 12, mtime);
            for (int i = 148; i < 156; i++) h[i] = (byte)' ';   // контрольная сумма считается с пробелами
            h[156] = (byte)type;
            Ascii(h, 257, "ustar");                  // magic "ustar\0"
            Ascii(h, 263, "00");                     // version
            Ascii(h, 265, "player");                 // uname
            Ascii(h, 297, "staff");                  // gname
            int sum = 0;
            for (int i = 0; i < 512; i++) sum += h[i];
            string cs = Convert.ToString(sum, 8).PadLeft(6, '0');
            Ascii(h, 148, cs);
            h[154] = 0;
            h[155] = (byte)' ';
            return h;
        }

        /// <summary>Восьмеричное число, дополненное нулями, с завершающим \0.</summary>
        private static void Octal(byte[] h, int at, int len, long value)
        {
            string o = Convert.ToString(value, 8).PadLeft(len - 1, '0');
            Ascii(h, at, o);
            h[at + len - 1] = 0;
        }

        private static void Ascii(byte[] h, int at, string s)
        {
            byte[] b = Encoding.ASCII.GetBytes(s);
            Buffer.BlockCopy(b, 0, h, at, b.Length);
        }
    }
}
