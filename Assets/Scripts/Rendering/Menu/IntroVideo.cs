using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Заставка при запуске игры (StreamingAssets/Intro/intro.mp4) — один раз за запуск, поверх экрана
    /// загрузки и главного меню (они готовятся за ней). Видео заканчивается кадром «Press any key to start»:
    /// на нём заставка останавливается и ждёт любую клавишу или клик. Пропустить можно и раньше — любой клавишей.
    /// Музыка меню на время заставки приглушена. Нет файла или видео не открылось — заставки просто нет.
    /// </summary>
    public class IntroVideo : MonoBehaviour
    {
        private const string FileName = "intro.mp4";
        private const float FadeOut = 0.6f, PrepareTimeout = 6f, SkipLock = 0.35f;

        private static bool s_played;

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
            string path = Path.Combine(Application.streamingAssetsPath, "Intro", FileName);
#if !UNITY_WEBGL || UNITY_EDITOR
            if (!File.Exists(path)) return;
#endif
            var go = new GameObject("[UI] IntroVideo");
            DontDestroyOnLoad(go);
            go.AddComponent<IntroVideo>().Begin(path);
        }

        private void Begin(string path)
        {
            IsPlaying = true;

            // Холст поверх всего, включая экран загрузки (7000)
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 8000;
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

            _rt = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32) { name = "IntroRT", filterMode = FilterMode.Bilinear };
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
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private void OnPrepared(VideoPlayer vp)
        {
            if (_fade >= 0f) return;
            _audio.volume = 1f;   // общая громкость уже учтена в AudioListener (GameSettings)
            _player.Play();
            _started = true;
        }

        /// <summary>Видео дошло до конца — останавливаемся на последнем кадре («Press any key to start»).</summary>
        private void OnFinished(VideoPlayer vp)
        {
            _ended = true;
            _player.Pause();
        }

        private void OnError(VideoPlayer vp, string message)
        {
            Debug.LogWarning("[Intro] Заставка не воспроизводится: " + message);
            Close();
        }

        private void Update()
        {
            _age += Time.unscaledDeltaTime;
            SFXManager.ExternalDuck = 1f;   // музыка меню молчит, пока идёт заставка

            if (_fade >= 0f)
            {
                _fade += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(_fade / FadeOut);
                _group.alpha = 1f - k;
                if (_audio != null) _audio.volume = 1f - k;
                if (k >= 1f) Finish();
                return;
            }

            // Первый кадр готов — плавно проявляем видео
            if (_started && _image.color.a < 1f)
                _image.color = new Color(1f, 1f, 1f, Mathf.Min(1f, _image.color.a + Time.unscaledDeltaTime * 4f));

            if (!_started && _age > PrepareTimeout) { Close(); return; }

            bool pressed = Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
            if (pressed && _age > SkipLock) Close();
            else if (_ended && _player != null && _player.isPlaying) _player.Pause();
        }

        private void Close()
        {
            if (_fade < 0f) _fade = 0f;
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
