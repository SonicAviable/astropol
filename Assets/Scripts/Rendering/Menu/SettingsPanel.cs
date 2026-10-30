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

        private static readonly string[] TabNames = { Loc.T("ГРАФИКА"), Loc.T("ЗВУК"), Loc.T("ИНТЕРФЕЙС"), Loc.T("ИГРА") };
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
                NotificationCenter.Show(Loc.T("Настройки сброшены"), Loc.T("Восстановлены значения по умолчанию"), NotificationCenter.Kind.Info, 3f);
            }, null, Loc.T("ПО УМОЛЧАНИЮ"), 12);
            ((RectTransform)reset.transform).At(new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(210, 44));

            var hint = LGBuild.Label(rt, Loc.T("Изменения применяются и сохраняются сразу"), 11, UIManager.DS.TextMuted, TextAnchor.MiddleRight);
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
            y = LGControls.Section(_content, y, Loc.T("ЭКРАН"), LGIcon.Monitor);
#if UNITY_EDITOR
            var note = LGControls.Row(_content, ref y, Loc.T("Режим экрана и разрешение"), Loc.T("В редакторе Unity не меняются — работают в собранной игре."), 300f);
            var nt = LGBuild.Label(note, Loc.T("только в сборке"), 12, UIManager.DS.TextMuted, TextAnchor.MiddleRight);
            nt.fontStyle = FontStyle.Italic;
#endif
            LGControls.Selector(LGControls.Row(_content, ref y, Loc.T("Режим экрана"), Loc.T("Полный экран без рамки удобнее для переключения окон")),
                GameSettings.ScreenModeNames, GameSettings.ScreenMode, GameSettings.SetScreenMode);

            var res = new List<string>();
            for (int i = 0; i < GameSettings.Resolutions.Count; i++) res.Add(GameSettings.ResolutionName(i));
            LGControls.Selector(LGControls.Row(_content, ref y, Loc.T("Разрешение"), null),
                res, GameSettings.ResolutionIndex, GameSettings.SetResolution);

            LGControls.Switch(LGControls.Row(_content, ref y, Loc.T("Вертикальная синхронизация"), Loc.T("Убирает разрывы кадра")),
                GameSettings.VSync, GameSettings.SetVSync);

            var fps = new List<string>();
            foreach (int f in GameSettings.FpsOptions) fps.Add(f < 0 ? Loc.T("Без ограничения") : f + Loc.T(" кадров/с"));
            LGControls.Selector(LGControls.Row(_content, ref y, Loc.T("Ограничение частоты кадров"), Loc.T("Работает при выключенной синхронизации")),
                fps, GameSettings.FpsIndex, GameSettings.SetFps);

            y += 6f;
            y = LGControls.Section(_content, y, Loc.T("КАЧЕСТВО"), LGIcon.Star);
            BuildQuality(ref y);

            LGControls.Slider(LGControls.Row(_content, ref y, Loc.T("Неоновое свечение"), Loc.T("Яркость ореола вокруг линий и выделенных элементов")),
                GameSettings.Glow, 0f, 0.8f, v => Mathf.RoundToInt(v / 0.8f * 100f) + "%", GameSettings.SetGlow);
        }

        // Пресет и отдельные пункты качества: пресет выставляет все пункты, ручная правка пункта делает пресет «Своим»
        private LGSelector _qPreset, _qScale, _qUpscale, _qSharp, _qAA, _qShadows, _qPost, _qTex;

        private void BuildQuality(ref float y)
        {
            GraphicsQuality.EnsureLoaded();
            _qPreset = LGControls.Selector(LGControls.Row(_content, ref y, Loc.T("Качество графики"), Loc.T("Набор настроек ниже: от «Низкого» для слабых ПК до «Ультра»")),
                GraphicsQuality.PresetNames, GraphicsQuality.CurrentPreset, i =>
                {
                    if (i == GraphicsQuality.PresetCustom) return;
                    GameSettings.GraphicsChanged(() => GraphicsQuality.SetPreset(i));
                    SyncQuality();
                });
            _qScale = LGControls.Selector(LGControls.Row(_content, ref y, Loc.T("Масштаб рендера"), Loc.T("Ниже — быстрее, но галактика мягче; интерфейс остаётся чётким")),
                GraphicsQuality.ScaleNames, GraphicsQuality.ScaleIndex, i => QualityItem(() => GraphicsQuality.SetScale(i)));
            _qUpscale = LGControls.Selector(LGControls.Row(_content, ref y, Loc.T("Масштабирование"), Loc.T("AMD FSR 1.0 — чётче обычного растяжения при масштабе рендера ниже 100%; на 100% не действует")),
                GraphicsQuality.UpscalerNames, GraphicsQuality.Upscaler, i => GameSettings.GraphicsChanged(() => GraphicsQuality.SetUpscaler(i)));
            _qSharp = LGControls.Selector(LGControls.Row(_content, ref y, Loc.T("Резкость FSR"), Loc.T("Насколько подчёркивать края после масштабирования")),
                GraphicsQuality.SharpnessNames, GraphicsQuality.Sharpness, i => GameSettings.GraphicsChanged(() => GraphicsQuality.SetSharpness(i)));
            _qAA = LGControls.Selector(LGControls.Row(_content, ref y, Loc.T("Сглаживание"), Loc.T("Убирает «лесенку» на краях кораблей и планет")),
                GraphicsQuality.AANames, GraphicsQuality.AntiAliasing, i => QualityItem(() => GraphicsQuality.SetAntiAliasing(i)));
            _qShadows = LGControls.Selector(LGControls.Row(_content, ref y, Loc.T("Тени"), Loc.T("Тени планет и кораблей вблизи")),
                GraphicsQuality.ShadowNames, GraphicsQuality.Shadows, i => QualityItem(() => GraphicsQuality.SetShadows(i)));
            _qPost = LGControls.Selector(LGControls.Row(_content, ref y, Loc.T("Постобработка"), Loc.T("Свечение звёзд, тонирование и виньетка")),
                GraphicsQuality.OnOffNames, GraphicsQuality.PostFx ? 1 : 0, i => QualityItem(() => GraphicsQuality.SetPostFx(i == 1)));
            _qTex = LGControls.Selector(LGControls.Row(_content, ref y, Loc.T("Текстуры"), Loc.T("Половинные экономят видеопамять на слабых видеокартах")),
                GraphicsQuality.TextureNames, GraphicsQuality.FullTextures ? 1 : 0, i => QualityItem(() => GraphicsQuality.SetFullTextures(i == 1)));
        }

        private void QualityItem(System.Action set)
        {
            GameSettings.GraphicsChanged(set);
            if (_qPreset != null) _qPreset.SetIndex(GraphicsQuality.CurrentPreset, false);
        }

        private void SyncQuality()
        {
            _qScale?.SetIndex(GraphicsQuality.ScaleIndex, false);
            _qUpscale?.SetIndex(GraphicsQuality.Upscaler, false);
            _qSharp?.SetIndex(GraphicsQuality.Sharpness, false);
            _qAA?.SetIndex(GraphicsQuality.AntiAliasing, false);
            _qShadows?.SetIndex(GraphicsQuality.Shadows, false);
            _qPost?.SetIndex(GraphicsQuality.PostFx ? 1 : 0, false);
            _qTex?.SetIndex(GraphicsQuality.FullTextures ? 1 : 0, false);
        }

        private void BuildAudio(ref float y)
        {
            y = LGControls.Section(_content, y, Loc.T("ГРОМКОСТЬ"), LGIcon.Speaker);
            LGControls.Slider(LGControls.Row(_content, ref y, Loc.T("Общая громкость"), null),
                GameSettings.MasterVolume, 0f, 1f, Percent, GameSettings.SetMaster);
            LGControls.Slider(LGControls.Row(_content, ref y, Loc.T("Музыка"), Loc.T("Фоновый саундтрек")),
                GameSettings.MusicVolume, 0f, 1f, Percent, GameSettings.SetMusic);
            LGControls.Slider(LGControls.Row(_content, ref y, Loc.T("Звуки и интерфейс"), Loc.T("Щелчки, корабли, уведомления")),
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
            y = LGControls.Section(_content, y, Loc.T("ПОВЕДЕНИЕ"), LGIcon.Gear);
            LGControls.Switch(LGControls.Row(_content, ref y, Loc.T("Тишина в фоне"), Loc.T("Приглушать звук, когда окно игры неактивно")),
                GameSettings.MuteInBackground, GameSettings.SetMuteInBackground);
            LGControls.Switch(LGControls.Row(_content, ref y, Loc.T("Вспышка и вибрация при тревоге"), Loc.T("При атаке и банкротстве края экрана вспыхивают, геймпад вибрирует")),
                GameSettings.AlarmFx, GameSettings.SetAlarmFx);
        }

        private float _lastSfxPreview;

        private void BuildInterface(ref float y)
        {
            y = LGControls.Section(_content, y, Loc.T("ИНТЕРФЕЙС"), LGIcon.Menu);

            // Язык: подпись на обоих языках, чтобы её нашёл любой игрок; применяется после перезапуска
            int lang = System.Array.IndexOf(Loc.Codes, Loc.Saved);
            var langRow = LGControls.Row(_content, ref y, "Язык · Language",
                Loc.Saved == Loc.Current ? "Перезапустите игру после смены · Restart the game to apply"
                                         : "<color=#F2C14E>Перезапустите игру, чтобы сменить язык · Restart the game to apply</color>");
            LGControls.Selector(langRow, Loc.Names, Mathf.Max(0, lang), i =>
            {
                Loc.Save(Loc.Codes[i]);
                NotificationCenter.Show(Loc.Codes[i] == "en" ? "Language: English" : "Язык: русский",
                    Loc.Codes[i] == Loc.Current ? (Loc.English ? "No restart needed" : "Перезапуск не нужен")
                                                : "Перезапустите игру · Restart the game to apply", NotificationCenter.Kind.Info, 6f);
            });
            var scales = new List<string>();
            foreach (float s in GameSettings.UIScaleOptions) scales.Add(Mathf.RoundToInt(s * 100f) + "%");
            LGControls.Selector(LGControls.Row(_content, ref y, Loc.T("Масштаб интерфейса"), Loc.T("Крупнее — для больших экранов и 4K")),
                scales, GameSettings.UIScaleIndex, GameSettings.SetUIScale);

            LGControls.Slider(LGControls.Row(_content, ref y, Loc.T("Яркость интерфейса"), Loc.T("Светлее панели окон, кнопок и списков")),
                GameSettings.UIBrightness, 0f, 1f, Percent, GameSettings.SetUIBrightness);

            LGControls.Switch(LGControls.Row(_content, ref y, Loc.T("Всплывающие подсказки"), Loc.T("Пояснения при наведении на элементы")),
                GameSettings.Tooltips, GameSettings.SetTooltips);

            y += 6f;
            y = LGControls.Section(_content, y, Loc.T("КАМЕРА"), LGIcon.Target);
            LGControls.Slider(LGControls.Row(_content, ref y, Loc.T("Скорость камеры"), Loc.T("Перемещение клавишами WASD / стрелками")),
                GameSettings.CameraSpeed, 0.4f, 2.5f, v => $"×{v:0.0}", GameSettings.SetCameraSpeed);
            LGControls.Switch(LGControls.Row(_content, ref y, Loc.T("Прокрутка у края экрана"), Loc.T("Камера двигается, когда курсор у края")),
                GameSettings.EdgeScroll, GameSettings.SetEdgeScroll);
        }

        private void BuildGame(ref float y)
        {
            y = LGControls.Section(_content, y, Loc.T("СОВЕТНИК"), LGIcon.Info);
            LGControls.Switch(LGControls.Row(_content, ref y, Loc.T("Советы по ходу партии"), Loc.T("ОРАКУЛ-7 подскажет: простаивающие корабли, дефицит, угроза колонии, шанс на штурм")),
                GameSettings.Advisor, GameSettings.SetAdvisor);
            y += 6f;

            y = LGControls.Section(_content, y, Loc.T("СОХРАНЕНИЯ"), LGIcon.Save);
            var auto = new List<string>();
            foreach (int m in GameSettings.AutosaveOptions)
                auto.Add(m == 0 ? Loc.T("Выключено") : m == 1 ? Loc.T("Каждый месяц") : m == 12 ? Loc.T("Каждый год") : Loc.F("Каждые {0} мес.", m));
            LGControls.Selector(LGControls.Row(_content, ref y, Loc.T("Автосохранение"), Loc.T("По игровому календарю, в слот «Автосохранение»")),
                auto, GameSettings.AutosaveIndex, GameSettings.SetAutosave);

            var keys = LGControls.Row(_content, ref y, Loc.T("Быстрое сохранение"), Loc.T("F5 — сохранить, F9 — загрузить"), 300f);
            var k = LGBuild.Label(keys, "<color=#4DF2DB><b>F5</b></color>  /  <color=#4DF2DB><b>F9</b></color>", 15,
                                  UIManager.DS.TextPrimary, TextAnchor.MiddleRight);
            k.rectTransform.offsetMax = new Vector2(-8, 0);

            var folder = LGControls.Row(_content, ref y, Loc.T("Папка сохранений"), SaveSystem.Folder.Replace('\\', '/'), 300f, 70f);
            var open = LGBuild.Button(folder, "Open", UIManager.DS.BtnNeutral, new Color(1, 1, 1, 0.3f),
                () => Application.OpenURL("file:///" + SaveSystem.Folder.Replace('\\', '/')), LGIcon.Load, Loc.T("ОТКРЫТЬ"), 11);
            ((RectTransform)open.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(150, 36));
        }

        private static string Percent(float v) => Mathf.RoundToInt(v * 100f) + "%";
    }
}
