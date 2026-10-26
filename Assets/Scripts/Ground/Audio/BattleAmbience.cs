using System.Threading.Tasks;
using UnityEngine;
using StellarisClone.Core;
using SfxId = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Фон боя: петля далёкого фронта (ветер, гул, раскаты артиллерии, очереди за холмами). Синтезируется в фоне
    /// при старте боя (SfxLibrary — чистый C#, без UnityEngine.Random), склеивается в бесшовную петлю
    /// равномощным перекрёстным затуханием и плавно появляется. Громкость — от настройки звуков.
    /// </summary>
    public sealed class BattleAmbience : MonoBehaviour
    {
        private const float Crossfade = 1.2f;
        private Task<float[]> _job;
        private int _sr;
        private AudioSource _src;
        private AudioClip _clip;
        private float _fade;

        public static BattleAmbience Create(Transform parent)
        {
            var go = new GameObject("Ambience");
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<BattleAmbience>();
            a._sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            int sr = a._sr;
            a._job = Task.Run(() => Core.Audio.SfxLibrary.Render(SfxId.GroundAmbience, 0, sr));
            return a;
        }

        private void Update()
        {
            if (_src == null && _job != null && _job.IsCompleted)
            {
                if (_job.Status == TaskStatus.RanToCompletion && _job.Result != null && _job.Result.Length > 0) Begin(_job.Result);
                _job = null;
            }
            if (_src == null) return;
            _fade = Mathf.MoveTowards(_fade, 1f, Time.unscaledDeltaTime / 3f);
            _src.volume = _fade * 0.55f * GameSettings.SfxVolume;
        }

        private void Begin(float[] data)
        {
            // Бесшовная петля: последние Crossfade секунд накладываются на первые
            int frames = data.Length / 2;
            int f = Mathf.Min(frames / 4, Mathf.RoundToInt(Crossfade * _sr));
            int outFrames = frames - f;
            var loop = new float[outFrames * 2];
            System.Array.Copy(data, 0, loop, 0, outFrames * 2);
            for (int i = 0; i < f; i++)
            {
                float x = i / (float)f;
                float gIn = Mathf.Sin(x * Mathf.PI * 0.5f), gOut = Mathf.Cos(x * Mathf.PI * 0.5f);
                int a = i * 2, b = (outFrames + i) * 2;
                loop[a] = data[a] * gIn + data[b] * gOut;
                loop[a + 1] = data[a + 1] * gIn + data[b + 1] * gOut;
            }
            _clip = AudioClip.Create("GroundAmbience", outFrames, 2, _sr, false);
            _clip.SetData(loop, 0);
            _src = gameObject.AddComponent<AudioSource>();
            _src.clip = _clip;
            _src.loop = true;
            _src.spatialBlend = 0f;
            _src.priority = 200;
            _src.volume = 0f;
            _src.Play();
        }

        private void OnDestroy()
        {
            if (_src != null) _src.Stop();
            if (_clip != null) Destroy(_clip);
        }
    }
}
