using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Фоновая музыка с плавным кроссфейдом между треками.
    /// Создаётся автоматически через GameBootstrap, ничего в сцену тащить не нужно.
    /// </summary>
    public class MusicManager : MonoBehaviour
    {
        public static MusicManager Instance { get; private set; }

        [Header("Треки (перетащи сюда MP3/OGG из Assets/Audio/Music)")]
        [SerializeField] private AudioClip[] tracks;

        [Header("Настройки")]
        [Range(0f, 1f)] [SerializeField] private float musicVolume = 0.45f;
        [SerializeField] private bool shuffle = true;
        [SerializeField] private bool playOnStart = true;
        [SerializeField] private float fadeDuration = 2f;

        private AudioSource _sourceA;
        private AudioSource _sourceB;
        private bool _usingA = true;
        private int _currentIndex = -1;
        private bool _isRunning;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(this); return; }

            // Два источника для кроссфейда
            _sourceA = gameObject.AddComponent<AudioSource>();
            _sourceB = gameObject.AddComponent<AudioSource>();
            ConfigureSource(_sourceA);
            ConfigureSource(_sourceB);

            DontDestroyOnLoad(gameObject);
        }

       private void Start()
{
    LoadTracksFromResources();
    if (playOnStart) StartMusic();
}

private void LoadTracksFromResources()
{
    if (tracks != null && tracks.Length > 0) return; // уже заданы вручную

    var loaded = Resources.LoadAll<AudioClip>("Music");
    if (loaded != null && loaded.Length > 0)
    {
        tracks = loaded;
        Debug.Log($"[MusicManager] Загружено из Resources/Music: {tracks.Length} треков.");
    }
    else
    {
        Debug.LogWarning("[MusicManager] Resources/Music пусто. Положи MP3/OGG туда.");
    }
}

        private void ConfigureSource(AudioSource src)
        {
            src.loop = false;
            src.playOnAwake = false;
            src.volume = 0f;
            src.spatialBlend = 0f; // 2D-звук
        }

        // ==================== ПУБЛИЧНОЕ API ====================

        public void StartMusic()
        {
            if (_isRunning) return;
            if (tracks == null || tracks.Length == 0)
            {
                Debug.LogWarning("[MusicManager] Нет треков — музыка не играет.");
                return;
            }
            _isRunning = true;
            PlayNextTrack();
        }

        public void StopMusic()
        {
            _isRunning = false;
            StartCoroutine(FadeOutAll());
        }

        public void SetVolume(float v)
        {
            musicVolume = Mathf.Clamp01(v);
        }

        private float _duck;

        /// <summary>Приглушение музыки (0 — нет, 1 — тишина) на время важных звуков. Вызывает SFXManager каждый кадр.</summary>
        public void SetDuck(float amount) => _duck = Mathf.Clamp01(amount);

        // ==================== ЛОГИКА ====================

        private void Update()
        {
            if (!_isRunning) return;

            var active = _usingA ? _sourceA : _sourceB;

            // Если трек закончился — играем следующий с кроссфейдом
            if (!active.isPlaying)
            {
                PlayNextTrack();
                return;
            }

            // Плавно ведём громкость активного источника к целевой (с учётом приглушения);
            // уход вниз быстрый (~0,3 с), возврат — плавный
            float target = musicVolume * (1f - _duck);
            float rate = active.volume > target ? Mathf.Max(musicVolume, 0.05f) * 3f : 1f / fadeDuration;
            active.volume = Mathf.MoveTowards(active.volume, target, Time.unscaledDeltaTime * rate);

            // Плавно опускаем громкость неактивного
            var other = _usingA ? _sourceB : _sourceA;
            if (other.isPlaying)
                other.volume = Mathf.MoveTowards(other.volume, 0f, Time.unscaledDeltaTime / fadeDuration);
        }

        private void PlayNextTrack()
        {
            if (tracks.Length == 0) return;

            int next;
            if (shuffle && tracks.Length > 1)
            {
                do { next = Random.Range(0, tracks.Length); }
                while (next == _currentIndex);
            }
            else
            {
                next = (_currentIndex + 1) % tracks.Length;
            }
            _currentIndex = next;

            var incoming = _usingA ? _sourceB : _sourceA;
            var outgoing = _usingA ? _sourceA : _sourceB;

            incoming.clip = tracks[next];
            incoming.volume = 0f;
            incoming.loop = false;
            incoming.Play();

            // Если предыдущий ещё играет — плавно гасим
            if (outgoing.isPlaying)
            {
                // fade-out уже происходит в Update
            }

            _usingA = !_usingA;

            Debug.Log($"[MusicManager] Играет трек: {tracks[next].name}");
        }

        private System.Collections.IEnumerator FadeOutAll()
        {
            float elapsed = 0f;
            float startA = _sourceA.volume;
            float startB = _sourceB.volume;

            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = elapsed / fadeDuration;
                _sourceA.volume = Mathf.Lerp(startA, 0f, t);
                _sourceB.volume = Mathf.Lerp(startB, 0f, t);
                yield return null;
            }

            _sourceA.Stop();
            _sourceB.Stop();
            _sourceA.volume = 0f;
            _sourceB.volume = 0f;
        }
    }
}