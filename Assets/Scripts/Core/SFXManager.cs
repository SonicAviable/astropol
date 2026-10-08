using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using StellarisClone.Core.Audio;

namespace StellarisClone.Core
{
    /// <summary>
    /// Звуковые эффекты. Аудиофайлов нет: каждый звук синтезируется в коде (SfxLibrary) —
    /// осцилляторы, шумы, фильтры, ADSR-огибающие, реверберация.
    ///
    ///   • Генерация идёт в фоновом потоке при старте (сначала интерфейсные звуки); если звук нужен
    ///     раньше, чем готов, — он досчитывается синхронно (несколько миллисекунд).
    ///   • Пул голосов: лимит копий одного звука, минимальный интервал повторов, приоритеты.
    ///     Вытесняемые голоса гасятся плавным фейдом — без щелчков.
    ///   • PlayAt — звук в мире: громкость и панорама по положению на экране и высоте камеры.
    ///   • Важные звуки (война, победа, открытие) приглушают музыку на время звучания.
    ///
    /// Использование:
    ///   SFXManager.Play(Sfx.UiConfirm);
    ///   SFXManager.PlayAt(Sfx.ExplosionSmall, worldPos);
    ///   SFXManager.Play("ui_click");     // старые строковые имена тоже работают
    /// </summary>
    public class SFXManager : MonoBehaviour
    {
        public static SFXManager Instance { get; private set; }

        [Range(0f, 1f)] [SerializeField] private float masterVolume = 0.7f;
        private const int MaxSources = 40;
        private const int HardMaxSources = 48;
        private const float StealFade = 0.03f;

        private int _sr = 48000;
        private readonly ConcurrentDictionary<int, Lazy<float[]>> _pcm = new ConcurrentDictionary<int, Lazy<float[]>>();
        private readonly Dictionary<int, AudioClip> _clips = new Dictionary<int, AudioClip>();
        private readonly ConcurrentQueue<int> _ready = new ConcurrentQueue<int>();
        private CancellationTokenSource _cts;

        private class Voice
        {
            public AudioSource Src;
            public int Id = -1;
            public float BaseVolume;
            public float Fade = -1f;   // > 0 — гасится, секунд осталось
            public float FadeFrom;
            public float Started;
            public int Priority;
            public float Duck;
        }

        private readonly List<Voice> _voices = new List<Voice>();
        private readonly float[] _lastPlayed = new float[SfxLibrary.Count];

        // ==================== ЖИЗНЕННЫЙ ЦИКЛ ====================

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            GameSettings.EnsureLoaded();
            masterVolume = GameSettings.SfxVolume;
            for (int i = 0; i < _lastPlayed.Length; i++) _lastPlayed[i] = -10f;
            for (int i = 0; i < 16; i++) NewVoice();
            Prewarm();
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            if (Instance == this) Instance = null;
        }

        private Voice NewVoice()
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.loop = false;
            src.dopplerLevel = 0f;
            var v = new Voice { Src = src };
            _voices.Add(v);
            return v;
        }

        /// <summary>Фоновый синтез всех звуков: интерфейс и уведомления — первыми.</summary>
        private void Prewarm()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var order = new List<int>();
            var defs = new List<(Sfx id, SfxDef def)>();
            foreach (Sfx id in Enum.GetValues(typeof(Sfx))) defs.Add((id, SfxLibrary.Def(id)));
            // Порядок: Ui → Stinger → World (боевые нужны только в игре)
            foreach (var bus in new[] { SfxBus.Ui, SfxBus.Stinger, SfxBus.World })
            {
                // Заставка новой партии нужна сразу после меню — считаем её первой среди «стингеров»
                if (bus == SfxBus.Stinger) order.Add(Key(Sfx.GameStart, 0));
                foreach (var (id, def) in defs)
                    if (def.Bus == bus && id != Sfx.GameStart)
                        for (int v = 0; v < def.Variants; v++) order.Add(Key(id, v));
            }

            // Несколько рабочих потоков; порядок примерно сохраняется (интерфейс готов первым)
            int workers = Mathf.Clamp(SystemInfo.processorCount - 1, 1, 4);
            int next = -1;
            s_prewarmTotal = order.Count;
            s_prewarmDone = 0;
            for (int w = 0; w < workers; w++)
            {
                Task.Run(() =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        int i = Interlocked.Increment(ref next);
                        if (i >= order.Count) return;
                        int key = order[i];
                        try
                        {
                            Pcm(key);
                            _ready.Enqueue(key);
                        }
                        catch (Exception e) { Debug.LogWarning($"[SFX] Ошибка синтеза {(Sfx)(key >> 4)}: {e.Message}"); }
                        Interlocked.Increment(ref s_prewarmDone);
                    }
                }, token);
            }
        }

        private static int Key(Sfx id, int variant) => ((int)id << 4) | variant;

        private static int s_prewarmTotal, s_prewarmDone;

        /// <summary>Доля уже синтезированных звуков (0..1) — для экрана загрузки.</summary>
        public static float PrewarmProgress => s_prewarmTotal <= 0 ? (Instance != null ? 0f : 1f)
                                                 : Mathf.Clamp01(s_prewarmDone / (float)s_prewarmTotal);

        private float[] Pcm(int key)
        {
            var lazy = _pcm.GetOrAdd(key, k => new Lazy<float[]>(
                () => SfxLibrary.Render((Sfx)(k >> 4), k & 15, _sr), LazyThreadSafetyMode.ExecutionAndPublication));
            return lazy.Value;
        }

        private AudioClip Clip(int key)
        {
            if (_clips.TryGetValue(key, out var c) && c != null) return c;
            var data = Pcm(key);   // если ещё не готов — досчитаем здесь
            c = AudioClip.Create($"sfx_{(Sfx)(key >> 4)}_{key & 15}", data.Length / 2, 2, _sr, false);
            c.SetData(data, 0);
            _clips[key] = c;
            // Данные уже в клипе — промежуточный буфер больше не нужен (экономия десятков МБ)
            _pcm[key] = new Lazy<float[]>(() => Array.Empty<float>());
            return c;
        }

        private void Update()
        {
            // Готовые буферы превращаем в клипы понемногу, чтобы не было рывков
            for (int n = 0; n < 3 && _ready.TryDequeue(out int key); n++)
                if (!_clips.ContainsKey(key)) Clip(key);

            float dt = Time.unscaledDeltaTime;
            float duck = 0f;
            foreach (var v in _voices)
            {
                if (v.Fade > 0f)
                {
                    v.Fade -= dt;
                    v.Src.volume = Mathf.Max(0f, v.FadeFrom * (v.Fade / StealFade));
                    if (v.Fade <= 0f) { v.Src.Stop(); v.Fade = -1f; v.Id = -1; }
                    continue;
                }
                if (v.Id >= 0 && !v.Src.isPlaying) { v.Id = -1; continue; }
                if (v.Id >= 0 && v.Duck > 0f)
                {
                    // Приглушение держится, пока звучит основная часть звука
                    float left = v.Src.clip != null ? (v.Src.clip.length - v.Src.time) : 0f;
                    duck = Mathf.Max(duck, v.Duck * Mathf.Clamp01(left / 0.8f));
                }
            }
            MusicManager.Instance?.SetDuck(Mathf.Max(duck, ExternalDuck));
        }

        /// <summary>Внешнее приглушение музыки (0..1) — например, пока говорит советник обучения.</summary>
        public static float ExternalDuck;

        // ==================== ПУБЛИЧНОЕ API ====================

        /// <summary>Проиграть звук. pan −1..1 (лево/право).</summary>
        public static void Play(Sfx id, float volumeScale = 1f, float pitch = 1f, float pan = 0f)
            => Instance?.PlayInternal(id, volumeScale, pitch, pan);

        /// <summary>Звук в мире: тише за краем экрана и при отдалённой камере, панорама — по экрану.</summary>
        public static void PlayAt(Sfx id, Vector3 worldPos, float volumeScale = 1f, float pitch = 1f)
        {
            if (Instance == null) return;
            var cam = Camera.main;
            if (cam == null) { Play(id, volumeScale, pitch); return; }
            Vector3 vp = cam.WorldToViewportPoint(worldPos);
            if (vp.z <= 0f) return;
            float off = Mathf.Max(0f, Mathf.Abs(vp.x - 0.5f) - 0.5f, Mathf.Abs(vp.y - 0.5f) - 0.5f);
            float screenAtt = Mathf.Clamp01(1f - off * 2.5f);
            if (screenAtt <= 0.03f) return;
            float zoomAtt = Mathf.Lerp(1f, 0.35f, Mathf.InverseLerp(30f, 260f, vp.z));
            float pan = Mathf.Clamp((vp.x - 0.5f) * 1.3f, -0.8f, 0.8f);
            Instance.PlayInternal(id, volumeScale * screenAtt * zoomAtt, pitch, pan);
        }

        /// <summary>Звук в мире с задержкой (реальное время): попадание, когда снаряд долетел.</summary>
        public static void PlayAtDelayed(Sfx id, Vector3 worldPos, float delay, float volumeScale = 1f, float pitch = 1f)
        {
            if (Instance == null) return;
            if (delay <= 0.01f) { PlayAt(id, worldPos, volumeScale, pitch); return; }
            Instance.StartCoroutine(Instance.DelayedAt(id, worldPos, delay, volumeScale, pitch));
        }

        private System.Collections.IEnumerator DelayedAt(Sfx id, Vector3 pos, float delay, float volumeScale, float pitch)
        {
            yield return new WaitForSecondsRealtime(delay);
            PlayAt(id, pos, volumeScale, pitch);
        }

        /// <summary>Проиграть с задержкой (реальное время).</summary>
        public static void PlayDelayed(Sfx id, float delay, float volumeScale = 1f, float pitch = 1f)
        {
            if (Instance == null) return;
            Instance.StartCoroutine(Instance.Delayed(id, delay, volumeScale, pitch));
        }

        private System.Collections.IEnumerator Delayed(Sfx id, float delay, float volumeScale, float pitch)
        {
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            PlayInternal(id, volumeScale, pitch, 0f);
        }

        // ---------- уведомления ----------

        private int _pendingNotify = -1;
        private float _lastImportant = -10f;

        /// <summary>
        /// Звук всплывающего уведомления. Играет в конце кадра и только если в этот момент не прозвучал
        /// собственный звук события (взрыв, открытие, война…) — чтобы звуки не накладывались.
        /// </summary>
        public static void Notify(NotificationCenter.Kind kind)
        {
            if (Instance == null) return;
            var id = kind switch
            {
                NotificationCenter.Kind.Success => Sfx.NotifySuccess,
                NotificationCenter.Kind.Warning => Sfx.NotifyWarning,
                NotificationCenter.Kind.Danger => Sfx.NotifyDanger,
                _ => Sfx.NotifyInfo
            };
            // Из нескольких уведомлений за кадр звучит самое важное
            if ((int)id > Instance._pendingNotify) Instance._pendingNotify = (int)id;
        }

        private static bool IsNotify(Sfx id) => id >= Sfx.NotifyInfo && id <= Sfx.NotifyDanger;

        private void LateUpdate()
        {
            if (_pendingNotify < 0) return;
            var id = (Sfx)_pendingNotify;
            _pendingNotify = -1;
            if (GameAudio.Suppressed) return;
            if (Time.unscaledTime - _lastImportant < 0.4f) return;
            PlayInternal(id, 1f, 1f, 0f);
        }

        public void SetVolume(float v) => masterVolume = Mathf.Clamp01(v);
        public static void SetMasterVolume(float v) { if (Instance != null) Instance.masterVolume = Mathf.Clamp01(v); }

        // ---------- совместимость со строковыми именами ----------

        public static void Play(string clipName, float volumeScale = 1f, float pitch = 1f)
        {
            if (TryMap(clipName, out var id)) Play(id, volumeScale, pitch);
        }

        public static void PlayRandom(string[] clipNames, float volumeScale = 1f)
        {
            if (clipNames == null) return;
            foreach (var n in clipNames)
                if (TryMap(n, out var id)) { Play(id, volumeScale); return; }
        }

        public static void PlayVaried(string clipName, float volumeScale = 1f, float pitchMin = 0.96f, float pitchMax = 1.04f)
            => Play(clipName, volumeScale, UnityEngine.Random.Range(pitchMin, pitchMax));

        public static void PlayDelayed(string clipName, float delay, float volumeScale = 1f, float pitch = 1f)
        {
            if (TryMap(clipName, out var id)) PlayDelayed(id, delay, volumeScale, pitch);
        }

        public static bool Has(string clipName) => TryMap(clipName, out _);

        public static string[] GetAllClipNames() => Enum.GetNames(typeof(Sfx));

        private static bool TryMap(string name, out Sfx id)
        {
            id = Sfx.UiClick;
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            switch (n)
            {
                case "ui_click": id = Sfx.UiClick; return true;
                case "ui_hover": id = Sfx.UiHover; return true;
                case "ui_deselect": id = Sfx.UiBack; return true;
                case "ui_open": id = Sfx.SystemEnter; return true;
                case "ui_confirm": id = Sfx.UiConfirm; return true;
                case "ui_error": id = Sfx.UiDenied; return true;
            }
            if (n.StartsWith("unit_select_military")) { id = Sfx.SelectMilitary; return true; }
            if (n.StartsWith("unit_select_science")) { id = Sfx.SelectScience; return true; }
            if (n.StartsWith("unit_select_constructor")) { id = Sfx.SelectConstructor; return true; }
            if (n.StartsWith("unit_select")) { id = Sfx.UiConfirm; return true; }
            return Enum.TryParse(name, true, out id);
        }

        // ==================== ВНУТРЕННЕЕ ====================

        private void PlayInternal(Sfx id, float volumeScale, float pitch, float pan)
        {
            if (masterVolume <= 0.001f || volumeScale <= 0.001f) return;
            var def = SfxLibrary.Def(id);
            float now = Time.unscaledTime;
            int idx = (int)id;
            if (now - _lastPlayed[idx] < def.Cooldown) return;

            // Лимит одновременных копий: самую старую плавно гасим
            Voice oldest = null;
            int count = 0;
            foreach (var v in _voices)
            {
                if (v.Id != idx || v.Fade > 0f) continue;
                count++;
                if (oldest == null || v.Started < oldest.Started) oldest = v;
            }
            if (count >= def.MaxVoices && oldest != null) FadeOut(oldest);

            var voice = Acquire(def.Priority);
            if (voice == null) return;

            int variant = def.Variants > 1 ? UnityEngine.Random.Range(0, def.Variants) : 0;
            AudioClip clip;
            try { clip = Clip(Key(id, variant)); }
            catch (Exception e) { Debug.LogWarning($"[SFX] {id}: {e.Message}"); return; }

            _lastPlayed[idx] = now;
            if (def.Priority >= 2 && !IsNotify(id)) _lastImportant = now;
            float jitter = def.PitchJitter > 0f ? UnityEngine.Random.Range(-def.PitchJitter, def.PitchJitter) : 0f;

            voice.Id = idx;
            voice.Priority = def.Priority;
            voice.Started = now;
            voice.Fade = -1f;
            voice.Duck = def.DuckMusic;
            voice.BaseVolume = Mathf.Clamp01(def.Volume * volumeScale * masterVolume);
            var src = voice.Src;
            src.Stop();
            src.clip = clip;
            src.volume = voice.BaseVolume;
            src.pitch = Mathf.Clamp(pitch * (1f + jitter), 0.5f, 2.5f);
            src.panStereo = Mathf.Clamp(pan, -1f, 1f);
            src.priority = 128 - def.Priority * 30;
            src.Play();
        }

        private void FadeOut(Voice v)
        {
            if (v.Fade > 0f || !v.Src.isPlaying) { v.Id = -1; return; }
            v.FadeFrom = v.Src.volume;
            v.Fade = StealFade;
        }

        /// <summary>Свободный голос; при нехватке — вытесняем самый старый из наименее важных (с фейдом).</summary>
        private Voice Acquire(int priority)
        {
            foreach (var v in _voices)
                if (v.Id < 0 && v.Fade <= 0f && !v.Src.isPlaying) return v;
            if (_voices.Count < MaxSources) return NewVoice();

            Voice victim = null;
            foreach (var v in _voices)
            {
                if (v.Fade > 0f || v.Priority > priority) continue;
                if (victim == null || v.Priority < victim.Priority || (v.Priority == victim.Priority && v.Started < victim.Started)) victim = v;
            }
            if (victim == null) return null;           // всё занято более важными звуками
            FadeOut(victim);
            return _voices.Count < HardMaxSources ? NewVoice() : null;
        }
    }
}
