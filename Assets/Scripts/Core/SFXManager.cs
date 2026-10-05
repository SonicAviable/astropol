using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Менеджер звуковых эффектов. Автоматически загружает все файлы из Resources/SFX.
    /// Играет через пул AudioSource — можно накладывать до 8 звуков одновременно.
    /// 
    /// Использование:
    ///   SFXManager.Play("ui_click");
    ///   SFXManager.Play("unit_select", 1f, 1.1f);          // с питчем
    ///   SFXManager.PlayRandom(new[] { "ui_click_1", "ui_click_2" });
    ///   if (SFXManager.Has("ui_deselect")) { ... }
    /// </summary>
    public class SFXManager : MonoBehaviour
    {
        public static SFXManager Instance { get; private set; }

        [Header("Настройки")]
        [Range(0f, 1f)] [SerializeField] private float masterVolume = 0.7f;
        [SerializeField] private int poolSize = 8;
        [SerializeField] private bool logLoadedClips = true;

        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly List<AudioSource> _pool = new List<AudioSource>();
        private int _poolIndex;

        // ==================== ЖИЗНЕННЫЙ ЦИКЛ ====================

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(this); return; }

            // Создаём пул AudioSource для наложения звуков
            for (int i = 0; i < poolSize; i++)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;   // 2D
                src.volume = masterVolume;
                src.loop = false;
                _pool.Add(src);
            }

            LoadClipsFromResources();
            DontDestroyOnLoad(gameObject);
        }

        private void LoadClipsFromResources()
        {
            var loaded = Resources.LoadAll<AudioClip>("SFX");
            if (loaded == null || loaded.Length == 0)
            {
                Debug.LogWarning("[SFXManager] Resources/SFX пусто. Положи аудиофайлы туда.");
                return;
            }

            foreach (var clip in loaded)
            {
                if (clip == null) continue;
                // Ключ — имя файла без расширения, в нижнем регистре
                _clips[clip.name.ToLower()] = clip;
            }

            if (logLoadedClips)
                Debug.Log($"[SFXManager] Загружено {_clips.Count} SFX: {string.Join(", ", _clips.Keys)}");
        }

        // ==================== ПУБЛИЧНОЕ API ====================

        /// <summary>Проиграть звук по имени файла (без расширения).</summary>
        public static void Play(string clipName, float volumeScale = 1f, float pitch = 1f)
        {
            Instance?.PlayInternal(clipName, volumeScale, pitch);
        }

        /// <summary>
        /// Проиграть случайный звук из массива имён.
        /// Если какой-то из них отсутствует — просто пропускается.
        /// Если ни одного нет — ничего не играет (без ошибок).
        /// </summary>
        public static void PlayRandom(string[] clipNames, float volumeScale = 1f)
        {
            if (Instance == null || clipNames == null || clipNames.Length == 0) return;

            // Собираем существующие
            var available = new List<string>();
            for (int i = 0; i < clipNames.Length; i++)
            {
                if (!string.IsNullOrEmpty(clipNames[i]) && Instance._clips.ContainsKey(clipNames[i].ToLower()))
                    available.Add(clipNames[i]);
            }

            if (available.Count == 0) return;

            var pick = available[Random.Range(0, available.Count)];
            Instance.PlayInternal(pick, volumeScale, Random.Range(0.97f, 1.03f));
        }

        /// <summary>Есть ли такой звук в загруженных.</summary>
        public static bool Has(string clipName)
        {
            if (Instance == null || string.IsNullOrEmpty(clipName)) return false;
            return Instance._clips.ContainsKey(clipName.ToLower());
        }

        /// <summary>Проиграть звук с рандомным питчем в диапазоне (для живости).</summary>
        public static void PlayVaried(string clipName, float volumeScale = 1f,
            float pitchMin = 0.96f, float pitchMax = 1.04f)
        {
            if (Instance == null) return;
            float pitch = Random.Range(pitchMin, pitchMax);
            Instance.PlayInternal(clipName, volumeScale, pitch);
        }

        /// <summary>Проиграть звук с задержкой (для цепочек звуков).</summary>
        public static void PlayDelayed(string clipName, float delay, float volumeScale = 1f, float pitch = 1f)
        {
            if (Instance == null) return;
            Instance.StartCoroutine(Instance.PlayDelayedRoutine(clipName, delay, volumeScale, pitch));
        }

        public void SetVolume(float v) => masterVolume = Mathf.Clamp01(v);

        public static void SetMasterVolume(float v)
        {
            if (Instance != null) Instance.masterVolume = Mathf.Clamp01(v);
        }

        // ==================== ВНУТРЕННЕЕ ====================

        private void PlayInternal(string clipName, float volumeScale, float pitch)
        {
            if (string.IsNullOrEmpty(clipName)) return;

            string key = clipName.ToLower();
            if (!_clips.TryGetValue(key, out var clip))
            {
                Debug.LogWarning($"[SFXManager] Звук '{clipName}' не найден в Resources/SFX.");
                return;
            }

            var src = GetFreeSource();
            src.clip = clip;
            src.volume = masterVolume * Mathf.Clamp01(volumeScale);
            src.pitch = Mathf.Clamp(pitch, 0.5f, 3f);
            src.Play();
        }

        private System.Collections.IEnumerator PlayDelayedRoutine(string clipName, float delay,
            float volumeScale, float pitch)
        {
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            PlayInternal(clipName, volumeScale, pitch);
        }

        private AudioSource GetFreeSource()
        {
            // Первый проход — ищем свободный
            for (int i = 0; i < _pool.Count; i++)
            {
                var src = _pool[_poolIndex];
                _poolIndex = (_poolIndex + 1) % _pool.Count;
                if (!src.isPlaying) return src;
            }

            // Все заняты — берём первый по кругу
            var fallback = _pool[_poolIndex];
            _poolIndex = (_poolIndex + 1) % _pool.Count;
            return fallback;
        }

        // ==================== ОТЛАДКА ====================

        /// <summary>Список всех загруженных звуков (для консоли разработчика).</summary>
        public static string[] GetAllClipNames()
        {
            if (Instance == null) return new string[0];
            var arr = new string[Instance._clips.Count];
            Instance._clips.Keys.CopyTo(arr, 0);
            return arr;
        }

        /// <summary>Перезагрузить звуки из Resources (полезно после обновления файлов).</summary>
        public static void Reload()
        {
            if (Instance == null) return;
            Instance._clips.Clear();
            Instance.LoadClipsFromResources();
        }
    }
}