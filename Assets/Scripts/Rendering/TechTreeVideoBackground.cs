using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Видео-фон для окна исследований.
    /// Загружает видео из StreamingAssets/TechVideos/tech_XXX.mp4.
    /// Меняется при переключении вкладки дерева.
    /// </summary>
    public class TechTreeVideoBackground : MonoBehaviour
    {
        [SerializeField] private int renderWidth = 1280;
        [SerializeField] private int renderHeight = 720;
        [SerializeField] private float fadeDuration = 0.5f;
        [Range(0f, 1f)] [SerializeField] private float dimAlpha = 0.28f;

        private static readonly System.Collections.Generic.HashSet<string> s_warned = new System.Collections.Generic.HashSet<string>();
        private RawImage _image;
        private CanvasGroup _group;
        private VideoPlayer _player;
        private RenderTexture _rt;
        private string _loadedClip = "";
        private bool _isOpen;

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
            _image.color = Color.white;
            _image.raycastTarget = false;

            _group = go.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;

            _rt = new RenderTexture(renderWidth, renderHeight, 0, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "TechTreeVideoRT"
            };
            _rt.Create();
            _image.texture = _rt;

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

        /// <summary>Установить видео для ветки: "all", "physics", "society", "engineering".</summary>
        public void SetBranch(string clipKey)
        {
            string path = Path.Combine(Application.streamingAssetsPath, "TechVideos", $"tech_{clipKey}.mp4");
            if (!File.Exists(path))
            {
                // Видео для ветки нет — просто гасим фон (без спама в консоль)
                if (s_warned.Add(clipKey)) Debug.Log($"[TechVideo] Нет видео для ветки «{clipKey}»: {path}");
                _loadedClip = "";
                if (_player != null && _player.isPlaying) _player.Stop();
                if (isActiveAndEnabled) StartCoroutine(FadeTo(0f));
                return;
            }

            if (_loadedClip == clipKey) return;
            _loadedClip = clipKey;

            _player.Stop();
            _player.url = path;
            _player.prepareCompleted -= OnPrepared;
            _player.prepareCompleted += OnPrepared;
            _player.Prepare();
        }

        private void OnPrepared(VideoPlayer vp)
        {
            _player.prepareCompleted -= OnPrepared;
            if (!_isOpen) return;
            _player.Play();
            StartCoroutine(FadeTo(dimAlpha));
        }

        public void OnOpen(string startClip)
        {
            _isOpen = true;
            SetBranch(startClip);
            if (string.IsNullOrEmpty(_loadedClip)) return;   // видео для ветки нет
            if (_player.isPrepared && !_player.isPlaying) _player.Play();
            StartCoroutine(FadeTo(dimAlpha));
        }

        public void OnClose()
        {
            _isOpen = false;
            if (_player != null && _player.isPlaying) _player.Pause();
            StartCoroutine(FadeTo(0f));
        }

        private IEnumerator FadeTo(float target)
        {
            float start = _group.alpha;
            float t = 0f;
            while (t < fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                _group.alpha = Mathf.Lerp(start, target, t / fadeDuration);
                yield return null;
            }
            _group.alpha = target;
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