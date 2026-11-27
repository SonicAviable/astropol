using System;

namespace StellarisClone.Core.Audio
{
    // =====================================================================================
    //  Примитивы синтеза для процедурных звуков. Всё считается офлайн в float-буферы
    //  (можно в фоновом потоке — тут нет обращений к Unity API).
    //
    //  • Adsr          — огибающая с плавными (экспоненциальными) сегментами; атака начинается
    //                    ровно с нуля, релиз заканчивается ровно в нуле — щелчков нет.
    //  • Osc           — синус / треугольник / пила и меандр с PolyBLEP (без алиасинга).
    //  • Noise         — белый, розовый (Kellet), коричневый шум; детерминированный генератор.
    //  • Svf           — фильтр состояния (Cytomic TPT): ФНЧ/ФВЧ/полоса, стабилен при модуляции.
    //  • Reverb        — стерео Freeverb (8 гребёнок + 4 всепропускающих на канал).
    //  • PingPong      — стерео-задержка с затуханием и фильтром в петле.
    //  • Stereo        — буфер L/R: микширование, сатурация, DC-фильтр, нормализация,
    //                    обрезка тишины и финальные «страховочные» фейды.
    // =====================================================================================

    /// <summary>ADSR-огибающая. A, D, Hold, R — в секундах; S — уровень удержания (0..1).</summary>
    public struct Adsr
    {
        public float A, D, S, Hold, R;
        /// <summary>Крутизна экспоненциальных спадов (4 — естественно, 8 — резко «щипково»).</summary>
        public float Curve;

        public Adsr(float a, float d, float s, float hold, float r, float curve = 4f)
        {
            A = Math.Max(0.0015f, a);     // минимум 1,5 мс — атака никогда не бывает мгновенной
            D = Math.Max(0.001f, d);
            S = s;
            Hold = Math.Max(0f, hold);
            R = Math.Max(0.004f, r);      // минимум 4 мс релиза
            Curve = curve;
        }

        public float Length => A + D + Hold + R;

        /// <summary>Перкуссионная огибающая: атака → экспоненциальный спад до нуля.</summary>
        public static Adsr Pluck(float attack, float decay, float curve = 5f) => new Adsr(attack, 0.001f, 1f, 0f, decay, curve);

        public float Value(float t)
        {
            if (t <= 0f) return 0f;
            if (t < A)
            {
                float x = t / A;
                return x * x * (3f - 2f * x);                       // smoothstep: нулевая производная на старте
            }
            t -= A;
            if (t < D) return S + (1f - S) * ExpFall(t / D, Curve);
            t -= D;
            if (t < Hold) return S;
            t -= Hold;
            if (t < R) return S * ExpFall(t / R, Curve);
            return 0f;
        }

        /// <summary>Экспоненциальный спад 1 → 0 на отрезке [0..1], приведённый так, чтобы в конце был ровно 0.</summary>
        private static float ExpFall(float x, float k)
        {
            if (k <= 0.01f) return 1f - x;
            float e = (float)Math.Exp(-k);
            return ((float)Math.Exp(-k * x) - e) / (1f - e);
        }
    }

    public enum Wave { Sine, Triangle, Saw, Square }

    /// <summary>Осциллятор с фазовым аккумулятором. Пила и меандр — с PolyBLEP-сглаживанием разрывов.</summary>
    public struct Osc
    {
        public double Phase;   // 0..1
        public Wave Wave;
        public float Pulse;    // скважность меандра

        public Osc(Wave wave, double phase = 0, float pulse = 0.5f) { Wave = wave; Phase = phase; Pulse = pulse; }

        public float Next(float freq, float sr)
        {
            double dt = Math.Min(0.49, Math.Abs(freq) / sr);
            double p = Phase;
            float y;
            switch (Wave)
            {
                case Wave.Sine:
                    y = (float)Math.Sin(p * 2.0 * Math.PI);
                    break;
                case Wave.Triangle:
                    y = (float)(p < 0.5 ? 4.0 * p - 1.0 : 3.0 - 4.0 * p);
                    break;
                case Wave.Saw:
                    y = (float)(2.0 * p - 1.0 - PolyBlep(p, dt));
                    break;
                default:
                    double pw = Pulse;
                    double sq = p < pw ? 1.0 : -1.0;
                    sq += PolyBlep(p, dt);
                    double p2 = p - pw; if (p2 < 0) p2 += 1.0;
                    sq -= PolyBlep(p2, dt);
                    y = (float)sq;
                    break;
            }
            Phase += dt;
            if (Phase >= 1.0) Phase -= 1.0;
            return y;
        }

        private static double PolyBlep(double t, double dt)
        {
            if (dt <= 0) return 0;
            if (t < dt) { t /= dt; return t + t - t * t - 1.0; }
            if (t > 1.0 - dt) { t = (t - 1.0) / dt; return t * t + t + t + 1.0; }
            return 0;
        }
    }

    /// <summary>Детерминированный генератор шума (xorshift) — у каждого звука свой сид, варианты различаются.</summary>
    public sealed class Noise
    {
        private uint _s;
        private float _b0, _b1, _b2, _b3, _b4, _b5, _b6, _brown;

        public Noise(int seed) { _s = (uint)(seed * 2654435761u) | 1u; }

        public float White()
        {
            _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
            return (_s & 0xFFFFFF) / 8388607.5f - 1f;
        }

        public float Value01() => (White() + 1f) * 0.5f;
        public float Range(float a, float b) => a + (b - a) * Value01();

        /// <summary>Розовый шум (метод Пола Келлета) — мягче белого, «воздушный».</summary>
        public float Pink()
        {
            float w = White();
            _b0 = 0.99886f * _b0 + w * 0.0555179f;
            _b1 = 0.99332f * _b1 + w * 0.0750759f;
            _b2 = 0.96900f * _b2 + w * 0.1538520f;
            _b3 = 0.86650f * _b3 + w * 0.3104856f;
            _b4 = 0.55000f * _b4 + w * 0.5329522f;
            _b5 = -0.7616f * _b5 - w * 0.0168980f;
            float y = _b0 + _b1 + _b2 + _b3 + _b4 + _b5 + _b6 + w * 0.5362f;
            _b6 = w * 0.115926f;
            return y * 0.11f;
        }

        /// <summary>Коричневый шум — низкий гул, рокот двигателей и взрывов.</summary>
        public float Brown()
        {
            _brown = (_brown + 0.02f * White()) / 1.02f;
            return _brown * 3.5f;
        }
    }

    public enum SvfMode { LowPass, HighPass, BandPass, Notch }

    /// <summary>Фильтр переменных состояний (topology-preserving transform, Э. Симпер).</summary>
    public struct Svf
    {
        private float _ic1, _ic2;
        private float _lastFc, _lastQ, _k, _a1, _a2, _a3;
        public SvfMode Mode;

        public Svf(SvfMode mode) { Mode = mode; _ic1 = _ic2 = 0f; _lastFc = _lastQ = -1f; _k = _a1 = _a2 = _a3 = 0f; }

        /// <param name="cutoff">Частота среза, Гц.</param>
        /// <param name="q">Добротность (0,5 — мягко, 5+ — резонанс).</param>
        public float Process(float x, float cutoff, float q, float sr)
        {
            float fc = Math.Max(15f, Math.Min(cutoff, sr * 0.45f));
            if (fc != _lastFc || q != _lastQ)
            {
                // Коэффициенты пересчитываются только при изменении среза (tan — дорогой)
                float g = (float)Math.Tan(Math.PI * fc / sr);
                _k = 1f / Math.Max(0.3f, q);
                _a1 = 1f / (1f + g * (g + _k));
                _a2 = g * _a1;
                _a3 = g * _a2;
                _lastFc = fc; _lastQ = q;
            }
            float v3 = x - _ic2;
            float v1 = _a1 * _ic1 + _a2 * v3;
            float v2 = _ic2 + _a2 * _ic1 + _a3 * v3;
            _ic1 = 2f * v1 - _ic1;
            _ic2 = 2f * v2 - _ic2;
            switch (Mode)
            {
                case SvfMode.LowPass: return v2;
                case SvfMode.HighPass: return x - _k * v1 - v2;
                case SvfMode.BandPass: return v1 * _k;     // нормировано: единичное усиление на резонансе
                default: return x - _k * v1;
            }
        }
    }

    /// <summary>Стерео-реверберация Freeverb (Jezar). Тюнинги масштабируются под частоту дискретизации.</summary>
    public sealed class Reverb
    {
        private static readonly int[] CombT = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
        private static readonly int[] AllT = { 556, 441, 341, 225 };
        private const int Spread = 23;

        private readonly float[][] _cL = new float[8][], _cR = new float[8][], _aL = new float[4][], _aR = new float[4][];
        private readonly int[] _ciL = new int[8], _ciR = new int[8], _aiL = new int[4], _aiR = new int[4];
        private readonly float[] _fsL = new float[8], _fsR = new float[8];
        private readonly float _feedback, _damp;

        /// <param name="room">Размер помещения 0..1 (длина хвоста).</param>
        /// <param name="damp">Затухание верхов 0..1.</param>
        public Reverb(float sr, float room, float damp)
        {
            float scale = sr / 44100f;
            for (int i = 0; i < 8; i++)
            {
                _cL[i] = new float[Math.Max(1, (int)(CombT[i] * scale))];
                _cR[i] = new float[Math.Max(1, (int)((CombT[i] + Spread) * scale))];
            }
            for (int i = 0; i < 4; i++)
            {
                _aL[i] = new float[Math.Max(1, (int)(AllT[i] * scale))];
                _aR[i] = new float[Math.Max(1, (int)((AllT[i] + Spread) * scale))];
            }
            _feedback = 0.7f + 0.28f * Clamp01(room);
            _damp = 0.4f * Clamp01(damp);
        }

        public void Process(float inL, float inR, out float outL, out float outR)
        {
            float input = (inL + inR) * 0.015f;
            float l = 0f, r = 0f;
            for (int i = 0; i < 8; i++)
            {
                l += Comb(_cL[i], ref _ciL[i], ref _fsL[i], input);
                r += Comb(_cR[i], ref _ciR[i], ref _fsR[i], input);
            }
            for (int i = 0; i < 4; i++)
            {
                l = AllPass(_aL[i], ref _aiL[i], l);
                r = AllPass(_aR[i], ref _aiR[i], r);
            }
            outL = l; outR = r;
        }

        private float Comb(float[] buf, ref int idx, ref float store, float input)
        {
            float o = buf[idx];
            store = o * (1f - _damp) + store * _damp;
            buf[idx] = input + store * _feedback;
            if (++idx >= buf.Length) idx = 0;
            return o;
        }

        private static float AllPass(float[] buf, ref int idx, float input)
        {
            float b = buf[idx];
            float o = -input + b;
            buf[idx] = input + b * 0.5f;
            if (++idx >= buf.Length) idx = 0;
            return o;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }

    /// <summary>Стерео-буфер звука с утилитами пост-обработки.</summary>
    public sealed class Stereo
    {
        public readonly float[] L, R;
        public readonly int Sr;
        public int Length => L.Length;

        public Stereo(float seconds, int sr)
        {
            Sr = sr;
            int n = Math.Max(16, (int)(seconds * sr));
            L = new float[n];
            R = new float[n];
        }

        public float Time(int i) => i / (float)Sr;
        public int Index(float t) => Math.Max(0, Math.Min(Length, (int)(t * Sr)));

        /// <summary>Добавить моно-сэмпл с панорамой (равномощный закон, pan −1..1).</summary>
        public void AddPan(int i, float v, float pan = 0f)
        {
            if ((uint)i >= (uint)L.Length) return;
            float a = (pan + 1f) * 0.25f * (float)Math.PI;
            L[i] += v * (float)Math.Cos(a) * 1.4142f;
            R[i] += v * (float)Math.Sin(a) * 1.4142f;
        }

        public void Add(int i, float l, float r)
        {
            if ((uint)i >= (uint)L.Length) return;
            L[i] += l; R[i] += r;
        }

        /// <summary>Мягкое насыщение (tanh) — плотность и «аналоговость» без жёсткого клиппинга.</summary>
        public void Saturate(float drive)
        {
            if (drive <= 0f) return;
            float norm = 1f / (float)Math.Tanh(1f + drive);
            for (int i = 0; i < L.Length; i++)
            {
                L[i] = (float)Math.Tanh(L[i] * (1f + drive)) * norm;
                R[i] = (float)Math.Tanh(R[i] * (1f + drive)) * norm;
            }
        }

        /// <summary>
        /// Подмешать реверберацию (хвост ложится в оставшуюся часть буфера).
        /// preDelay — отделяет звук от хвоста; lowCut — срез низов на входе ревера, чтобы хвост не «гудел».
        /// </summary>
        public void ApplyReverb(float wet, float room, float damp, float dry = 1f, float preDelay = 0.02f, float lowCut = 0f)
        {
            if (wet <= 0f) return;
            var rv = new Reverb(Sr, room, damp);
            int pre = Math.Max(1, (int)(preDelay * Sr));
            var ringL = new float[pre]; var ringR = new float[pre];
            var hl = new Svf(SvfMode.HighPass); var hr = new Svf(SvfMode.HighPass);
            int idx = 0;
            for (int i = 0; i < L.Length; i++)
            {
                float dl = L[i], dr = R[i];
                float inL = ringL[idx], inR = ringR[idx];
                if (lowCut > 0f) { inL = hl.Process(inL, lowCut, 0.6f, Sr); inR = hr.Process(inR, lowCut, 0.6f, Sr); }
                ringL[idx] = dl; ringR[idx] = dr;
                if (++idx >= pre) idx = 0;
                rv.Process(inL, inR, out float wl, out float wr);
                L[i] = dl * dry + wl * wet * 3f;   // 3 — штатный масштаб «мокрого» сигнала Freeverb
                R[i] = dr * dry + wr * wet * 3f;
            }
        }

        /// <summary>Подмешать другой буфер со сдвигом (offset может быть отрицательным), при желании — задом наперёд.</summary>
        public void MixFrom(Stereo src, int offset, float gain = 1f, bool reverse = false)
        {
            int n = src.Length;
            for (int k = 0; k < n; k++)
            {
                int i = offset + k;
                if (i < 0) continue;
                if (i >= L.Length) break;
                int j = reverse ? n - 1 - k : k;
                L[i] += src.L[j] * gain;
                R[i] += src.R[j] * gain;
            }
        }

        /// <summary>Стерео пинг-понг задержка с ФНЧ в петле.</summary>
        public void ApplyPingPong(float time, float feedback, float wet, float lpHz = 5000f)
        {
            int d = Math.Max(1, (int)(time * Sr));
            var bl = new float[d]; var br = new float[d];
            var fl = new Svf(SvfMode.LowPass); var fr = new Svf(SvfMode.LowPass);
            int idx = 0;
            for (int i = 0; i < L.Length; i++)
            {
                float dl = bl[idx], dr = br[idx];
                float inMono = (L[i] + R[i]) * 0.5f;
                bl[idx] = fl.Process(inMono + dr * feedback, lpHz, 0.6f, Sr);
                br[idx] = fr.Process(dl * feedback, lpHz, 0.6f, Sr);
                L[i] += dl * wet;
                R[i] += dr * wet;
                if (++idx >= d) idx = 0;
            }
        }

        /// <summary>Плавно увести в ноль последние seconds секунд (квадратичный фейд).</summary>
        public void FadeTail(float seconds)
        {
            int n = Math.Min(L.Length, Math.Max(1, (int)(seconds * Sr)));
            for (int k = 0; k < n; k++)
            {
                int i = L.Length - 1 - k;
                float x = (float)k / n, g = x * x;
                L[i] *= g; R[i] *= g;
            }
        }

        /// <summary>Фильтр по всему буферу (например, финальный ФВЧ от инфранизов).</summary>
        public void Filter(SvfMode mode, float cutoff, float q = 0.707f)
        {
            var a = new Svf(mode); var b = new Svf(mode);
            for (int i = 0; i < L.Length; i++)
            {
                L[i] = a.Process(L[i], cutoff, q, Sr);
                R[i] = b.Process(R[i], cutoff, q, Sr);
            }
        }

        /// <summary>Удаление постоянной составляющей (ФВЧ 1-го порядка ~10 Гц).</summary>
        public void DcBlock()
        {
            float r = 1f - 2f * (float)Math.PI * 10f / Sr;
            float xl = 0, yl = 0, xr = 0, yr = 0;
            for (int i = 0; i < L.Length; i++)
            {
                float l = L[i] - xl + r * yl; xl = L[i]; yl = l; L[i] = l;
                float rr = R[i] - xr + r * yr; xr = R[i]; yr = rr; R[i] = rr;
            }
        }

        public float Peak()
        {
            float p = 0f;
            for (int i = 0; i < L.Length; i++)
            {
                float a = Math.Abs(L[i]); if (a > p) p = a;
                float b = Math.Abs(R[i]); if (b > p) p = b;
            }
            return p;
        }

        public void Gain(float g)
        {
            for (int i = 0; i < L.Length; i++) { L[i] *= g; R[i] *= g; }
        }

        /// <summary>
        /// Финализация: DC-фильтр, нормализация к пику, обрезка тишины в хвосте, страховочные фейды
        /// (2 мс на входе, 25 мс на выходе) — первый и последний сэмпл гарантированно нулевые.
        /// </summary>
        public float[] Finish(float peakDb = -1f, float lowPassHz = 0f)
        {
            DcBlock();
            if (lowPassHz > 0f) Filter(SvfMode.LowPass, lowPassHz, 0.6f);
            float peak = Peak();
            if (peak > 1e-6f) Gain((float)Math.Pow(10, peakDb / 20f) / peak);

            // Обрезаем хвост ниже −66 дБ
            float thr = 0.0005f;
            int end = L.Length;
            while (end > Sr / 20 && Math.Abs(L[end - 1]) < thr && Math.Abs(R[end - 1]) < thr) end--;
            end = Math.Min(L.Length, end + Sr / 50);

            int fadeIn = Math.Max(2, Sr / 500);
            int fadeOut = Math.Min(end / 2, Sr / 40);
            var outBuf = new float[end * 2];
            for (int i = 0; i < end; i++)
            {
                float g = 1f;
                if (i < fadeIn) g *= (float)i / fadeIn;
                int fromEnd = end - 1 - i;
                if (fromEnd < fadeOut) { float x = (float)fromEnd / fadeOut; g *= x * x; }
                outBuf[i * 2] = L[i] * g;
                outBuf[i * 2 + 1] = R[i] * g;
            }
            return outBuf;
        }
    }

    /// <summary>Музыкальные утилиты.</summary>
    public static class Note
    {
        /// <summary>Частота MIDI-ноты (69 = A4 = 440 Гц).</summary>
        public static float Hz(float midi) => 440f * (float)Math.Pow(2.0, (midi - 69f) / 12f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * (t < 0f ? 0f : t > 1f ? 1f : t);
        /// <summary>Экспоненциальная интерполяция частоты (звучит как ровный глиссандо).</summary>
        public static float ExpLerp(float a, float b, float t)
        {
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return a * (float)Math.Pow(b / a, t);
        }
        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
