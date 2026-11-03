using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace StellarisClone.Core
{
    /// <summary>
    /// Отправка обратной связи (баги, идеи) на почту разработчика.
    ///
    /// Письмо уходит через FormSubmit (https://formsubmit.co): игра делает HTTPS POST, сервис пересылает его на
    /// <see cref="Email"/>. Ни паролей, ни ключей в сборке нет. Самое первое письмо FormSubmit не пересылает, а
    /// присылает на этот ящик просьбу подтвердить адрес — после нажатия «Activate» письма идут как обычно.
    ///
    /// Если сеть недоступна или сервис ответил ошибкой — <see cref="OpenMailClient"/> открывает почтовую программу
    /// игрока с уже заполненным письмом.
    /// </summary>
    public static class FeedbackSender
    {
        public const string Email = "astropolitymail@gmail.com";
        private const string Endpoint = "https://formsubmit.co/ajax/" + Email;
        private const int LogTailChars = 8000;

        public static readonly string[] CategoryKeys = { "Баг", "Идея", "Другое" };

        [Serializable]
        private sealed class Payload
        {
            public string _subject;
            public string _template = "box";
            public string _captcha = "false";
            public string category;
            public string message;
            public string contact;
            public string version;
            public string system;
            public string log;
        }

        /// <summary>Отправляет письмо; onDone(true) — сервис принял, onDone(false, причина) — нет.</summary>
        public static IEnumerator Send(int category, string message, string contact, bool attachInfo, Action<bool, string> onDone)
        {
            string cat = CategoryKeys[Mathf.Clamp(category, 0, CategoryKeys.Length - 1)];
            var p = new Payload
            {
                _subject = $"Astropolity {GameVersion.Current} · {cat} · {Shorten(message, 60)}",
                category = cat,
                message = message,
                contact = string.IsNullOrWhiteSpace(contact) ? "—" : contact.Trim(),
                version = GameVersion.Current,
                system = attachInfo ? SystemReport() : "—",
                log = attachInfo ? LogTail() : "—",
            };
            byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(p));

            using (var req = new UnityWebRequest(Endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(body);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Accept", "application/json");
                req.timeout = 20;
                yield return req.SendWebRequest();

                string text = req.downloadHandler != null ? req.downloadHandler.text : "";
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[Обратная связь] Не отправлено: {req.error} {text}");
                    onDone?.Invoke(false, req.error);
                    yield break;
                }
                // FormSubmit отвечает {"success":"true", ...} или {"success":"false","message":"..."}
                bool ok = !text.Contains("\"success\":\"false\"") && !text.Contains("\"success\":false");
                if (!ok) Debug.LogWarning("[Обратная связь] Сервис отклонил письмо: " + text);
                onDone?.Invoke(ok, ok ? null : text);
            }
        }

        /// <summary>Открывает почтовую программу игрока с заполненным письмом (без журнала — он не влезет в ссылку).</summary>
        public static void OpenMailClient(int category, string message, string contact, bool attachInfo)
        {
            string cat = CategoryKeys[Mathf.Clamp(category, 0, CategoryKeys.Length - 1)];
            var sb = new StringBuilder();
            sb.AppendLine(message);
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(contact)) sb.AppendLine("Contact: " + contact.Trim());
            sb.AppendLine("Version: " + GameVersion.Current);
            if (attachInfo) sb.AppendLine(SystemReport());
            string subject = $"Astropolity {GameVersion.Current} · {cat}";
            Application.OpenURL($"mailto:{Email}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(sb.ToString())}");
        }

        /// <summary>Версия, ОС, железо, экран, язык, качество графики — всё, что помогает воспроизвести баг.</summary>
        public static string SystemReport()
        {
            var r = Screen.currentResolution;
            var sb = new StringBuilder();
            sb.AppendLine($"Version: {GameVersion.Current} ({Application.platform})");
            sb.AppendLine($"OS: {SystemInfo.operatingSystem}");
            sb.AppendLine($"CPU: {SystemInfo.processorType} · {SystemInfo.processorCount} threads");
            sb.AppendLine($"RAM: {SystemInfo.systemMemorySize} MB");
            sb.AppendLine($"GPU: {SystemInfo.graphicsDeviceName} · {SystemInfo.graphicsMemorySize} MB · {SystemInfo.graphicsDeviceVersion}");
            sb.AppendLine($"Screen: {Screen.width}×{Screen.height} (display {r.width}×{r.height}) · {Screen.fullScreenMode}");
            sb.AppendLine($"Language: {Loc.Current}");
            sb.AppendLine($"Quality preset: {GraphicsQuality.CurrentPreset}");
            sb.AppendLine($"Scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            var tm = TimeManager.Instance;
            if (tm != null) sb.AppendLine($"Game date: {tm.GetFormattedDate()}");
            return sb.ToString();
        }

        /// <summary>Хвост журнала игры (Player.log) — там видны исключения, предшествовавшие багу.</summary>
        public static string LogTail()
        {
            try
            {
                string path = Application.consoleLogPath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "—";
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long start = Math.Max(0, fs.Length - LogTailChars * 2);
                    fs.Seek(start, SeekOrigin.Begin);
                    using (var rd = new StreamReader(fs, Encoding.UTF8))
                    {
                        string s = rd.ReadToEnd();
                        if (s.Length > LogTailChars) s = s.Substring(s.Length - LogTailChars);
                        return s;
                    }
                }
            }
            catch (Exception e)
            {
                return "log unavailable: " + e.Message;
            }
        }

        private static string Shorten(string s, int max)
        {
            s = (s ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }
    }
}
