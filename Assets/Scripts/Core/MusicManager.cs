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
        private bool _paused;
        private readonly System.Collections.Generic.List<int> _history = new System.Collections.Generic.List<int>();

        /// <summary>Сменился трек, пауза или перемешивание — для плеера в интерфейсе.</summary>
        public event System.Action OnStateChanged;

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
    shuffle = PlayerPrefs.GetInt("music.shuffle", shuffle ? 1 : 0) == 1;
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
            _paused = false;
            StartCoroutine(FadeOutAll());
        }

        public void SetVolume(float v)
        {
            musicVolume = Mathf.Clamp01(v);
        }

        // ==================== ПЛЕЕР ====================

        public int TrackCount => tracks != null ? tracks.Length : 0;
        public int CurrentIndex => _currentIndex;
        public bool IsPaused => _paused || !_isRunning;
        public bool Shuffle => shuffle;
        public float Volume => musicVolume;

        private AudioSource Active => _usingA ? _sourceA : _sourceB;   // тот же источник, что ведёт Update

        public float CurrentTime => Active != null && Active.clip != null ? Active.time : 0f;
        public float CurrentLength => Active != null && Active.clip != null ? Active.clip.length : 0f;

        public string TrackTitle(int i) { SplitName(i, out var t, out _); return t; }
        public string TrackArtist(int i) { SplitName(i, out _, out var a); return a; }

        public void Next()
        {
            if (TrackCount == 0) return;
            _isRunning = true;
            Resume();
            PlayNextTrack();
        }

        public void Previous()
        {
            if (TrackCount == 0) return;
            // В начале трека — предыдущий из истории, иначе — этот трек сначала
            if (CurrentTime > 4f || _history.Count < 2) { PlayIndex(_currentIndex < 0 ? 0 : _currentIndex, false); return; }
            _history.RemoveAt(_history.Count - 1);
            int prev = _history[_history.Count - 1];
            _history.RemoveAt(_history.Count - 1);
            PlayIndex(prev, true);
        }

        public void PlayTrack(int index)
        {
            if (index < 0 || index >= TrackCount) return;
            PlayIndex(index, true);
            _manualPick = true;   // выбранный вручную трек не перебиваем сменой настроения
        }

        // ==================== НАСТРОЕНИЕ: МИР / ВОЙНА ====================
        // Треки с префиксом «war__» в имени файла — боевые, остальные — мирные. Пока империя игрока воюет,
        // играют боевые; началась или закончилась война — музыка сразу плавно переходит в нужное настроение.

        public bool WarMood { get; private set; }
        private float _moodCheck;
        private bool _manualPick;

        public bool IsWarTrack(int i) => i >= 0 && i < TrackCount && tracks[i] != null && tracks[i].name.StartsWith("war__");

        private static bool PlayerAtWar()
        {
            if (!StellarisClone.Rendering.UIManager.IsGameStarted) return false;
            foreach (var ai in AIEmpireManager.All)
                if (ai != null && !ai.IsEliminated && ai.AtWar) return true;
            return false;
        }

        private void CheckMood()
        {
            _moodCheck -= Time.unscaledDeltaTime;
            if (_moodCheck > 0f) return;
            _moodCheck = 2f;
            bool war = PlayerAtWar();
            if (war == WarMood) return;
            WarMood = war;
            if (_isRunning && !_paused && !_manualPick && IsWarTrack(_currentIndex) != war) PlayNextTrack();
            _manualPick = false;
            OnStateChanged?.Invoke();
        }

        /// <summary>Треки под текущее настроение (если таких нет — все).</summary>
        private System.Collections.Generic.List<int> MoodPool()
        {
            var pool = new System.Collections.Generic.List<int>();
            for (int i = 0; i < TrackCount; i++) if (IsWarTrack(i) == WarMood) pool.Add(i);
            if (pool.Count == 0) for (int i = 0; i < TrackCount; i++) pool.Add(i);
            return pool;
        }

        public void TogglePause()
        {
            if (!_isRunning) { StartMusic(); return; }
            if (_paused) Resume(); else Pause();
        }

        private void Pause()
        {
            if (_paused) return;
            _paused = true;
            _sourceA.Pause();
            _sourceB.Pause();
            OnStateChanged?.Invoke();
        }

        private void Resume()
        {
            if (!_paused) return;
            _paused = false;
            _sourceA.UnPause();
            _sourceB.UnPause();
            OnStateChanged?.Invoke();
        }

        public void SetShuffle(bool on)
        {
            shuffle = on;
            PlayerPrefs.SetInt("music.shuffle", on ? 1 : 0);
            OnStateChanged?.Invoke();
        }

        /// <summary>
        /// Имя файла → название и автор: «474864__andrewkn__ambient-space-texture» → «Ambient Space Texture» / «andrewkn»,
        /// «blackbird1977-pulsar-dreams» → «Pulsar Dreams» / «blackbird1977».
        /// </summary>
        private void SplitName(int i, out string title, out string artist)
        {
            title = artist = "";
            if (i < 0 || i >= TrackCount || tracks[i] == null) return;
            string raw = tracks[i].name;
            string t = raw, a = "";
            var parts = raw.Split(new[] { "__" }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                t = parts[parts.Length - 1];
                a = parts[parts.Length - 2];
            }
            else
            {
                int dash = raw.IndexOf('-');
                if (dash > 0 && raw.Substring(0, dash).IndexOfAny("0123456789".ToCharArray()) >= 0)
                {
                    a = raw.Substring(0, dash);
                    t = raw.Substring(dash + 1);
                }
            }
            // «Sci-Fi 1» оставляем как есть; дефисы вместо пробелов («pulsar-dreams») — разбиваем
            if (t.IndexOf(' ') < 0) t = t.Replace('-', ' ');
            var words = t.Replace('_', ' ').Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            for (int w = 0; w < words.Length; w++)
                words[w] = char.ToUpperInvariant(words[w][0]) + words[w].Substring(1);
            title = words.Length > 0 ? string.Join(" ", words) : raw;
            artist = a;
        }

        private float _duck;

        /// <summary>Приглушение музыки (0 — нет, 1 — тишина) на время важных звуков. Вызывает SFXManager каждый кадр.</summary>
        public void SetDuck(float amount) => _duck = Mathf.Clamp01(amount);

        // ==================== ЛОГИКА ====================

        private void Update()
        {
            CheckMood();
            if (!_isRunning || _paused) return;

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
            _manualPick = false;

            var pool = MoodPool();
            int next;
            if (shuffle && pool.Count > 1)
            {
                do { next = pool[Random.Range(0, pool.Count)]; }
                while (next == _currentIndex);
            }
            else
            {
                // По порядку внутри своего настроения: следующий после текущего
                next = pool[0];
                foreach (int i in pool) if (i > _currentIndex) { next = i; break; }
            }
            PlayIndex(next, true);
        }

        /// <summary>Включить трек с кроссфейдом; remember — записать в историю (для «предыдущего»).</summary>
        private void PlayIndex(int next, bool remember)
        {
            if (tracks == null || next < 0 || next >= tracks.Length) return;
            _isRunning = true;
            if (_paused) { _paused = false; _sourceA.UnPause(); _sourceB.UnPause(); }
            _currentIndex = next;
            if (remember)
            {
                _history.Add(next);
                if (_history.Count > 32) _history.RemoveAt(0);
            }

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
            OnStateChanged?.Invoke();
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