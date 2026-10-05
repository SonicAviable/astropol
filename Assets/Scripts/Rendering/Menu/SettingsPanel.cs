using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Настройки игры: вкладки «Графика / Звук / Интерфейс / Игра».
    /// Строится внутри любого контейнера (главное меню, меню паузы). Изменения применяются сразу.
    /// </summary>
    public class SettingsPanel : MonoBehaviour
    {
        private static int s_lastTab;

        private RectTransform _content;
        private ScrollRect _scroll;
        private LGTabs _tabs;

        private static readonly string[] TabNames = { "ГРАФИКА", "ЗВУК", "ИНТЕРФЕЙС", "ИГРА" };
        private static readonly LGIcon[] TabIcons = { LGIcon.Monitor, LGIcon.Speaker, LGIcon.Menu, LGIcon.Gear };

        public static SettingsPanel Build(RectTransform host)
        {
            var rt = LGBuild.Rect(host, "Settings");
            rt.Stretch();
            var p = rt.gameObject.AddComponent<SettingsPanel>();
            p.BuildLayout(rt);
            return p;
        }

        private void BuildLayout(RectTransform rt)
        {
            GameSettings.EnsureLoaded();

            var tabsHost = LGBuild.Rect(rt, "TabsHost");
            tabsHost.TopBand(0, 46);
            _tabs = LGControls.Tabs(tabsHost, TabNames, TabIcons, s_lastTab, ShowTab);

            var view = LGBuild.Rect(rt, "View");
            view.Stretch(0, 58, 0, 60);
            view.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(0, 16);
            var hit = view.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            LG.Ignore(view.gameObject, includeChildren: false);

            _content = LGBuild.Rect(view, "Content");
            _content.anchorMin = new Vector2(0, 1);
            _content.anchorMax = new Vector2(1, 1);
            _content.pivot = new Vector2(0.5f, 1);
            _content.sizeDelta = Vector2.zero;

            _scroll = view.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Elastic;
            _scroll.elasticity = 0.08f;
            _scroll.scrollSensitivity = 36f;
            _scroll.viewport = view;
            _scroll.content = _content;

            // Низ: сброс и подсказка
            var reset = LGBuild.Button(rt, "Reset", UIManager.DS.BtnNeutral, new Color(1f, 0.82f, 0.4f, 0.45f), () =>
            {
                GameSettings.ResetDefaults();
                ShowTab(_tabs.Index);
                NotificationCenter.Show("Настройки сброшены", "Восстановлены значения по умолчанию", NotificationCenter.Kind.Info, 3f);
            }, LGIcon.Back, "ПО УМОЛЧАНИЮ", 12);
            ((RectTransform)reset.transform).At(new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(210, 44));

            var hint = LGBuild.Label(rt, "Изменения применяются и сохраняются сразу", 11, UIManager.DS.TextMuted, TextAnchor.MiddleRight);
            hint.rectTransform.anchorMin = new Vector2(0, 0);
            hint.rectTransform.anchorMax = new Vector2(1, 0);
            hint.rectTransform.pivot = new Vector2(0.5f, 0);
            hint.rectTransform.offsetMin = new Vector2(230, 0);
            hint.rectTransform.offsetMax = new Vector2(-6, 44);

            ShowTab(s_lastTab);
        }

        private void ShowTab(int tab)
        {
            s_lastTab = tab;
            LGBuild.Clear(_content);
            float y = 4f;
            switch (tab)
            {
                case 0: BuildGraphics(ref y); break;
                case 1: BuildAudio(ref y); break;
                case 2: BuildInterface(ref y); break;
                default: BuildGame(ref y); break;
            }
            _content.sizeDelta = new Vector2(0, y + 8f);
            _content.anchoredPosition = Vector2.zero;
            _scroll.velocity = Vector2.zero;

            // Мягкое появление строк
            var cg = _content.GetComponent<CanvasGroup>();
            if (cg == null) cg = _content.gameObject.AddComponent<CanvasGroup>();
            StopAllCoroutines();
            StartCoroutine(FadeContent(cg));
        }

        private System.Collections.IEnumerator FadeContent(CanvasGroup cg)
        {
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / 0.25f;
                float k = LGEase.OutCubic(t);
                cg.alpha = k;
                _content.anchoredPosition = new Vector2(0, -12f * (1f - k));
                yield return null;
            }
            cg.alpha = 1f;
            _content.anchoredPosition = Vector2.zero;
        }

        // ------------------------------------------------------------------ Вкладки

        private void BuildGraphics(ref float y)
        {
            y = LGControls.Section(_content, y, "ЭКРАН", LGIcon.Monitor);
#if UNITY_EDITOR
            var note = LGControls.Row(_content, ref y, "Режим экрана и разрешение", "В редакторе Unity не меняются — работают в собранной игре.", 300f);
            var nt = LGBuild.Label(note, "только в сборке", 12, UIManager.DS.TextMuted, TextAnchor.MiddleRight);
            nt.fontStyle = FontStyle.Italic;
#endif
            LGControls.Selector(LGControls.Row(_content, ref y, "Режим экрана", "Полный экран без рамки удобнее для переключения окон"),
                GameSettings.ScreenModeNames, GameSettings.ScreenMode, GameSettings.SetScreenMode);

            var res = new List<string>();
            for (int i = 0; i < GameSettings.Resolutions.Count; i++) res.Add(GameSettings.ResolutionName(i));
            LGControls.Selector(LGControls.Row(_content, ref y, "Разрешение", null),
                res, GameSettings.ResolutionIndex, GameSettings.SetResolution);

            LGControls.Switch(LGControls.Row(_content, ref y, "Вертикальная синхронизация", "Убирает разрывы кадра"),
                GameSettings.VSync, GameSettings.SetVSync);

            var fps = new List<string>();
            foreach (int f in GameSettings.FpsOptions) fps.Add(f < 0 ? "Без ограничения" : f + " кадров/с");
            LGControls.Selector(LGControls.Row(_content, ref y, "Ограничение частоты кадров", "Работает при выключенной синхронизации"),
                fps, GameSettings.FpsIndex, GameSettings.SetFps);

            y += 6f;
            y = LGControls.Section(_content, y, "КАЧЕСТВО", LGIcon.Star);
            var q = new List<string>(QualitySettings.names);
            if (q.Count > 0)
                LGControls.Selector(LGControls.Row(_content, ref y, "Качество графики", "Тени, сглаживание и постобработка"),
                    q, Mathf.Clamp(GameSettings.Quality, 0, q.Count - 1), GameSettings.SetQuality);

            LGControls.Switch(LGControls.Row(_content, ref y, "Размытие под стеклом", "Эффект «жидкого стекла» у панелей. Выключите на слабых ПК"),
                GameSettings.GlassBlur, GameSettings.SetGlassBlur);

            LGControls.Slider(LGControls.Row(_content, ref y, "Неоновое свечение", "Яркость ореола вокруг окон и кнопок"),
                GameSettings.Glow, 0f, 0.8f, v => Mathf.RoundToInt(v / 0.8f * 100f) + "%", GameSettings.SetGlow);
        }

        private void BuildAudio(ref float y)
        {
            y = LGControls.Section(_content, y, "ГРОМКОСТЬ", LGIcon.Speaker);
            LGControls.Slider(LGControls.Row(_content, ref y, "Общая громкость", null),
                GameSettings.MasterVolume, 0f, 1f, Percent, GameSettings.SetMaster);
            LGControls.Slider(LGControls.Row(_content, ref y, "Музыка", "Фоновый саундтрек"),
                GameSettings.MusicVolume, 0f, 1f, Percent, GameSettings.SetMusic);
            LGControls.Slider(LGControls.Row(_content, ref y, "Звуки и интерфейс", "Щелчки, корабли, уведомления"),
                GameSettings.SfxVolume, 0f, 1f, Percent, v =>
                {
                    GameSettings.SetSfx(v);
                    if (Time.unscaledTime - _lastSfxPreview > 0.12f)
                    {
                        _lastSfxPreview = Time.unscaledTime;
                        SFXManager.Play("ui_click", 0.6f, 1.2f);
                    }
                });

            y += 6f;
            y = LGControls.Section(_content, y, "ПОВЕДЕНИЕ", LGIcon.Gear);
            LGControls.Switch(LGControls.Row(_content, ref y, "Тишина в фоне", "Приглушать звук, когда окно игры неактивно"),
                GameSettings.MuteInBackground, GameSettings.SetMuteInBackground);
        }

        private float _lastSfxPreview;

        private void BuildInterface(ref float y)
        {
            y = LGControls.Section(_content, y, "ИНТЕРФЕЙС", LGIcon.Menu);
            var scales = new List<string>();
            foreach (float s in GameSettings.UIScaleOptions) scales.Add(Mathf.RoundToInt(s * 100f) + "%");
            LGControls.Selector(LGControls.Row(_content, ref y, "Масштаб интерфейса", "Крупнее — для больших экранов и 4K"),
                scales, GameSettings.UIScaleIndex, GameSettings.SetUIScale);

            LGControls.Switch(LGControls.Row(_content, ref y, "Всплывающие подсказки", "Пояснения при наведении на элементы"),
                GameSettings.Tooltips, GameSettings.SetTooltips);

            y += 6f;
            y = LGControls.Section(_content, y, "КАМЕРА", LGIcon.Target);
            LGControls.Slider(LGControls.Row(_content, ref y, "Скорость камеры", "Перемещение клавишами WASD / стрелками"),
                GameSettings.CameraSpeed, 0.4f, 2.5f, v => $"×{v:0.0}", GameSettings.SetCameraSpeed);
            LGControls.Switch(LGControls.Row(_content, ref y, "Прокрутка у края экрана", "Камера двигается, когда курсор у края"),
                GameSettings.EdgeScroll, GameSettings.SetEdgeScroll);
        }

        private void BuildGame(ref float y)
        {
            y = LGControls.Section(_content, y, "СОХРАНЕНИЯ", LGIcon.Save);
            var auto = new List<string>();
            foreach (int m in GameSettings.AutosaveOptions)
                auto.Add(m == 0 ? "Выключено" : m == 1 ? "Каждый месяц" : m == 12 ? "Каждый год" : $"Каждые {m} мес.");
            LGControls.Selector(LGControls.Row(_content, ref y, "Автосохранение", "По игровому календарю, в слот «Автосохранение»"),
                auto, GameSettings.AutosaveIndex, GameSettings.SetAutosave);

            var keys = LGControls.Row(_content, ref y, "Быстрое сохранение", "F5 — сохранить, F9 — загрузить", 300f);
            var k = LGBuild.Label(keys, "<color=#4DF2DB><b>F5</b></color>  /  <color=#4DF2DB><b>F9</b></color>", 15,
                                  UIManager.DS.TextPrimary, TextAnchor.MiddleRight);
            k.rectTransform.offsetMax = new Vector2(-8, 0);

            var folder = LGControls.Row(_content, ref y, "Папка сохранений", SaveSystem.Folder.Replace('\\', '/'), 300f, 70f);
            var open = LGBuild.Button(folder, "Open", UIManager.DS.BtnNeutral, new Color(1, 1, 1, 0.3f),
                () => Application.OpenURL("file:///" + SaveSystem.Folder.Replace('\\', '/')), LGIcon.Load, "ОТКРЫТЬ", 11);
            ((RectTransform)open.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(150, 36));
        }

        private static string Percent(float v) => Mathf.RoundToInt(v * 100f) + "%";
    }
}
