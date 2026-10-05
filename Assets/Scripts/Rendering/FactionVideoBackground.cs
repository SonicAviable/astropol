using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Видео внутри карточки фракции.
    /// Видео играет ВСЕГДА на всех карточках. Когда карточка не в фокусе — затемнено,
    /// когда в фокусе или под курсором — на полную яркость.
    /// </summary>
    public class FactionVideoBackground : MonoBehaviour
    {
        [SerializeField] private int renderWidth = 640;
        [SerializeField] private int renderHeight = 360;
        [SerializeField] private float fadeDuration = 0.3f;

        [Header("Яркость")]
        [Range(0f, 1f)] [SerializeField] private float dimAlpha = 0.35f;
        [Range(0f, 1f)] [SerializeField] private float brightAlpha = 1f;

        private RawImage _image;
        private CanvasGroup _group;
        private VideoPlayer _player;
        private RenderTexture _rt;
        private string _loadedClip = "";
        private float _targetAlpha;
        private bool _isHovered;
        private bool _isReady;

        public void Initialize(RectTransform videoZone)
        {
            var go = new GameObject("VideoLayer");
            go.transform.SetParent(videoZone, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _image = go.AddComponent<RawImage>();
            _image.texture = null;
            _image.color = Color.white;
            _image.raycastTarget = false;
            _image.enabled = false;

            _group = go.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _targetAlpha = 0f;

            _rt = new RenderTexture(renderWidth, renderHeight, 0, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "FactionVideoRT"
            };
            _rt.Create();

            _player = go.AddComponent<VideoPlayer>();
            _player.playOnAwake = false;
            _player.isLooping = true;
            _player.source = VideoSource.Url;
            _player.renderMode = VideoRenderMode.RenderTexture;
            _player.targetTexture = _rt;
            _player.audioOutputMode = VideoAudioOutputMode.None;
            _player.skipOnDrop = true;
            _player.waitForFirstFrame = true;
        }

        public void Preload(string clipKey)
        {
            if (string.IsNullOrEmpty(clipKey) || clipKey == _loadedClip) return;

            string path = Path.Combine(Application.streamingAssetsPath, "FactionVideos", $"faction_{clipKey}.mp4");
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[FactionVideo] Файл не найден: {path}");
                return;
            }

            _loadedClip = clipKey;
            _isReady = false;

            _player.url = path;
            _player.prepareCompleted -= OnPrepared;
            _player.prepareCompleted += OnPrepared;
            _player.Prepare();
        }

        private void OnPrepared(VideoPlayer vp)
        {
            _player.prepareCompleted -= OnPrepared;

            // Запускаем воспроизведение в цикле
            _player.Play();
            _isReady = true;

            // Показываем RawImage с RT
            _image.texture = _rt;
            _image.enabled = true;

            // Стартовая яркость — тусклая (если карточка не в фокусе)
            _targetAlpha = _isHovered ? brightAlpha : dimAlpha;
        }

        public void Play()
        {
            // Вызывается при наведении / фокусе
            _isHovered = true;

            // Если воспроизведение приостановлено — вернуть
            if (_player != null && _player.isPrepared && !_player.isPlaying)
                _player.Play();

            _targetAlpha = brightAlpha;
        }

        public void Pause()
        {
            // НЕ ставим на паузу — просто притушить
            _isHovered = false;
            _targetAlpha = dimAlpha;
        }

        private void Update()
        {
            if (_group == null) return;
            _group.alpha = Mathf.MoveTowards(_group.alpha, _targetAlpha, Time.unscaledDeltaTime / fadeDuration);
        }

        private void OnDestroy()
        {
            if (_player != null)
            {
                _player.prepareCompleted -= OnPrepared;
                _player.Stop();
            }
            if (_rt != null)
            {
                if (_player != null) _player.targetTexture = null;
                _rt.Release();
                Destroy(_rt);
            }
        }
    }
}