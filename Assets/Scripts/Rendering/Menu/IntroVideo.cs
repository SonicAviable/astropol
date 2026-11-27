using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Заставка при запуске игры — один раз за запуск, поверх экрана загрузки и главного меню (они готовятся за ней):
    /// пролог «Великое Молчание» (Intro/lore_&lt;язык&gt;.mp4, Tools/lore_intro/generate.py) — титры «Студия SmartBiz
    /// представляет» и «ASTROPOLITY», затем история Строителей под музыку и голос рассказчика; после него — главное меню.
    /// Пропуск — повторное нажатие любой клавиши (первое показывает подсказку) или Esc.
    /// Нет файла пролога — играет старый логотип (intro.mp4, ждёт клавишу на кадре «Press any key to start»).
    /// Музыка меню на время заставки приглушена. Нет файла или видео не открылось — этот ролик просто пропускается.
    /// </summary>
    public class IntroVideo : MonoBehaviour
    {
        private const string FileName = "intro.mp4";
        private const float FadeOut = 0.6f, PrepareTimeout = 6f, SkipLock = 0.35f, SwitchFade = 0.5f, SkipHintTime = 3f;

        /// <summary>Ролики по порядку: путь и «ждать клавишу на последнем кадре».</summary>
        private readonly System.Collections.Generic.List<(string path, bool hold)> _clips = new System.Collections.Generic.List<(string, bool)>();
        private int _clip;
        private float _switch = -1f, _hintAge = -1f;
        private Text _hint;
        private CanvasGroup _hintGroup;

        private static bool s_played;
        private IntroWarnings _warn;
        private bool _prepared;

        /// <summary>
        /// В проекте выключена перезагрузка домена при входе в Play Mode: статический флаг переживал запуски,
        /// и в редакторе заставка шла только в первый раз. Сбрасываем его в начале каждого запуска —
        /// вступление показывается всегда.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_played = false;
            IsPlaying = false;
        }

        private VideoPlayer _player;
        private AudioSource _audio;
        private RenderTexture _rt;
        private RawImage _image;
        private CanvasGroup _group;
        private float _age, _fade = -1f;
        private bool _started, _ended;

        public static bool IsPlaying { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnLaunch()
        {
            if (s_played) return;
            s_played = true;
            string dir = Path.Combine(Application.streamingAssetsPath, "Intro");
            var clips = new System.Collections.Generic.List<(string, bool)>();
            // Пролог начинается с титров «Студия SmartBiz представляет» и «ASTROPOLITY» — он и есть заставка.
            // Старый логотип (intro.mp4) — запасной вариант, если файла пролога нет.
            string lore = Path.Combine(dir, "lore_" + Loc.Current + ".mp4");
            if (!Exists(lore)) lore = Path.Combine(dir, "lore_ru.mp4");
            string logo = Path.Combine(dir, FileName);
            if (Exists(lore)) clips.Add((lore, false));
            else if (Exists(logo)) clips.Add((logo, true));
            if (clips.Count == 0) return;
            var go = new GameObject("[UI] IntroVideo");
            DontDestroyOnLoad(go);
            var iv = go.AddComponent<IntroVideo>();
            iv._clips.AddRange(clips);
            iv.Begin(clips[0].Item1);
        }

        private static bool Exists(string path)
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            return File.Exists(path);
#else
            return true;
#endif
        }

        private void Begin(string path)
        {
            IsPlaying = true;

            // Холст поверх всего, включая экран загрузки (7000)
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 8000;
            var scaler = gameObject.AddComponent<CanvasScaler>();   // предупреждения размечены в 1920×1080
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            _group = gameObject.AddComponent<CanvasGroup>();

            var bgGo = new GameObject("Black");
            bgGo.transform.SetParent(transform, false);
            var bg = bgGo.AddComponent<Image>();
            bg.color = Color.black;
            Stretch(bg.rectTransform);

            var imgGo = new GameObject("Video");
            imgGo.transform.SetParent(transform, false);
            _image = imgGo.AddComponent<RawImage>();
            _image.color = new Color(1f, 1f, 1f, 0f);
            Stretch(_image.rectTransform);
            // Сохраняем пропорции 16:9 на любом экране (по краям — чёрные поля)
            var fit = imgGo.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 16f / 9f;

            _rt = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32) { name = "IntroRT", filterMode = FilterMode.Bilinear };
            _rt.Create();
            _image.texture = _rt;

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;

            _player = gameObject.AddComponent<VideoPlayer>();
            _player.playOnAwake = false;
            _player.isLooping = false;
            _player.source = VideoSource.Url;
            _player.url = path;
            _player.renderMode = VideoRenderMode.RenderTexture;
            _player.targetTexture = _rt;
            _player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _player.controlledAudioTrackCount = 1;
            _player.EnableAudioTrack(0, true);
            _player.SetTargetAudioSource(0, _audio);
            _player.skipOnDrop = true;
            _player.waitForFirstFrame = true;
            _player.prepareCompleted += OnPrepared;
            _player.loopPointReached += OnFinished;
            _player.errorReceived += OnError;
            _player.Prepare();

            // Предупреждения (эпилепсия, автосохранение, «события вымышлены») — пока они идут, ролик готовится
            _warn = IntroWarnings.Create(transform);

            // Подсказка пропуска пролога (внизу справа)
            var hintGo = new GameObject("SkipHint");
            hintGo.transform.SetParent(transform, false);
            _hintGroup = hintGo.AddComponent<CanvasGroup>();
            _hintGroup.alpha = 0f;
            _hint = hintGo.AddComponent<Text>();
            _hint.font = GameFont.Regular;
            _hint.fontSize = 18;
            _hint.alignment = TextAnchor.LowerRight;
            _hint.color = new Color(0.925f, 0.898f, 0.827f, 0.85f);
            _hint.text = Loc.T("Нажмите любую клавишу ещё раз, чтобы пропустить");
            _hint.raycastTarget = false;
            var hrt = _hint.rectTransform;
            hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(1f, 0f);
            hrt.anchoredPosition = new Vector2(-40f, 32f);
            hrt.sizeDelta = new Vector2(900f, 30f);
            hintGo.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.8f);
        }

        private bool CurrentHolds => _clip < _clips.Count && _clips[_clip].hold;
        private bool HasNext => _clip + 1 < _clips.Count;

        /// <summary>Перейти к следующему ролику: картинка гаснет, затем готовится следующий.</summary>
        private void StartNext()
        {
            _clip++;
            _switch = -1f;
            _hintAge = -1f;
            _hintGroup.alpha = 0f;
            _started = _ended = false;
            _age = 0f;
            _player.Stop();
            _prepared = false;
            _player.url = _clips[_clip].path;
            _player.Prepare();
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private void OnPrepared(VideoPlayer vp)
        {
            _prepared = true;
            TryPlay();
        }

        /// <summary>Ролик готов и предупреждения позади — запускаем.</summary>
        private void TryPlay()
        {
            if (!_prepared || _started || _fade >= 0f || _switch >= 0f || _warn != null) return;
            _audio.volume = 1f;   // общая громкость уже учтена в AudioListener (GameSettings)
            _player.Play();
            _started = true;
        }

        /// <summary>Логотип дошёл до конца — стоим на последнем кадре («Press any key to start»); пролог — сразу дальше.</summary>
        private void OnFinished(VideoPlayer vp)
        {
            _ended = true;
            if (CurrentHolds) _player.Pause();
            else Close();
        }

        private void OnError(VideoPlayer vp, string message)
        {
            Debug.LogWarning("[Intro] Заставка не воспроизводится: " + message);
            if (HasNext && _switch < 0f) StartNext();
            else Close();
        }

        private void Update()
        {
            _age += Time.unscaledDeltaTime;
            SFXManager.ExternalDuck = 1f;   // музыка меню молчит, пока идёт заставка

            // Сначала — экраны предупреждений (клавиши обрабатывают они сами)
            if (_warn != null)
            {
                if (!_warn.Done) return;
                Destroy(_warn.gameObject);
                _warn = null;
                _age = 0f;
                TryPlay();
            }

            if (_fade >= 0f)
            {
                _fade += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(_fade / FadeOut);
                _group.alpha = 1f - k;
                if (_audio != null) _audio.volume = 1f - k;
                if (k >= 1f) Finish();
                return;
            }

            // Смена ролика: картинка и звук гаснут, потом готовится следующий
            if (_switch >= 0f)
            {
                _switch += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(_switch / SwitchFade);
                _image.color = new Color(1f, 1f, 1f, 1f - k);
                if (_audio != null) _audio.volume = 1f - k;
                if (k >= 1f) StartNext();
                return;
            }

            // Первый кадр готов — плавно проявляем видео
            if (_started && _image.color.a < 1f)
                _image.color = new Color(1f, 1f, 1f, Mathf.Min(1f, _image.color.a + Time.unscaledDeltaTime * 4f));

            if (!_started && _age > PrepareTimeout) { Close(); return; }

            // Подсказка пропуска гаснет сама
            if (_hintAge >= 0f)
            {
                _hintAge += Time.unscaledDeltaTime;
                _hintGroup.alpha = Mathf.Clamp01(Mathf.Min(_hintAge / 0.25f, (SkipHintTime - _hintAge) / 0.5f));
                if (_hintAge >= SkipHintTime) { _hintAge = -1f; _hintGroup.alpha = 0f; }
            }

            bool pressed = Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
            if (pressed && _age > SkipLock)
            {
                // Логотип пропускается сразу; пролог — вторым нажатием (или Esc), первое показывает подсказку
                if (CurrentHolds || _hintAge >= 0f || Input.GetKeyDown(KeyCode.Escape)) Close();
                else _hintAge = 0f;
            }
            else if (_ended && CurrentHolds && _player != null && _player.isPlaying) _player.Pause();
        }

        /// <summary>Ролик закончен или пропущен: дальше — следующий ролик, если он есть, иначе меню.</summary>
        private void Close()
        {
            if (_fade >= 0f || _switch >= 0f) return;
            if (HasNext) { _switch = 0f; _hintAge = -1f; _hintGroup.alpha = 0f; return; }
            _fade = 0f;
        }

        private void Finish()
        {
            IsPlaying = false;
            SFXManager.ExternalDuck = 0f;
            if (_player != null) _player.Stop();
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (IsPlaying) { IsPlaying = false; SFXManager.ExternalDuck = 0f; }
        }
    }
}
