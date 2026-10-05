using System;

namespace StellarisClone.Core.Audio
{
    /// <summary>Все звуки игры. Каждый синтезируется в коде (см. SfxLibrary.Render).</summary>
    public enum Sfx
    {
        // Интерфейс
        UiClick, UiHover, UiConfirm, UiBack, UiDenied, UiTab, WindowOpen, WindowClose,
        // Уведомления
        NotifyInfo, NotifySuccess, NotifyWarning, NotifyDanger,
        // Время
        TimePause, TimeResume, TimeSpeed,
        // Карта и флоты
        SystemSelect, SystemEnter, SystemExit,
        SelectMilitary, SelectScience, SelectConstructor,
        OrderMove, OrderQueue, OrderStop,
        FtlJump, FtlArrive, SurveyComplete, OutpostBuilt, Anomaly,
        // Строительство и наука
        BuildQueued, BuildComplete, ShipLaunched, ColonyFounded, ResearchComplete, ResearchStart,
        // Бой
        WeaponKinetic, WeaponEnergy, WeaponMissile, HitShield, HitArmor,
        ExplosionSmall, ExplosionLarge, StarbaseDown, BattleStart, BattleVictory, BattleDefeat,
        Disengage, FtlFail,
        // Стратегия
        WarDeclared, PeaceSigned, PactSigned, SiegeStart, SystemCaptured, SystemLost,
        GameVictory, GameDefeat, GameStart,
    }

    public enum SfxBus { Ui, World, Stinger }

    /// <summary>Параметры воспроизведения звука.</summary>
    public sealed class SfxDef
    {
        public float Volume = 0.5f;
        public int MaxVoices = 3;          // одновременно звучащих копий
        public float Cooldown = 0.03f;     // минимальный интервал между запусками, с
        public float PitchJitter = 0.02f;  // случайный разброс высоты (±)
        public int Variants = 1;           // сколько вариантов синтезировать (разные сиды)
        public int Priority = 1;           // 0 — фон, 3 — важнейшее (не вытесняется)
        public SfxBus Bus = SfxBus.Ui;
        public float DuckMusic;            // насколько приглушить музыку (0..1) на время звука
    }

    // ================================================================================
    //  Описание слоёв
    // ================================================================================

    /// <summary>Тональный слой: осциллятор(ы) → фильтр → огибающая.</summary>
    public sealed class ToneLayer
    {
        public float Start;
        public Adsr Env = Adsr.Pluck(0.002f, 0.2f);
        public Wave Wave = Wave.Sine;
        public float PulseWidth = 0.5f;
        public Func<float, float> Freq = _ => 440f;
        public float Gain = 0.5f;
        public float Pan;
        public int Unison = 1;
        public float DetuneCents;          // разброс голосов унисона
        public float Spread = 0.6f;        // стерео-ширина унисона
        public float FmRatio;              // фазовая модуляция (только синус)
        public Func<float, float> FmIndex;
        public float VibratoHz, VibratoCents;
        public SvfMode? Filter;
        public Func<float, float> Cutoff;
        public float Q = 0.707f;
        public float Drive;
        public Func<float, float> Amp;     // дополнительная амплитудная модуляция (стаккато, тремоло)
    }

    public enum NoiseColor { White, Pink, Brown }

    /// <summary>Шумовой слой: шум → фильтр → огибающая. Width — декорреляция каналов.</summary>
    public sealed class NoiseLayer
    {
        public float Start;
        public Adsr Env = Adsr.Pluck(0.002f, 0.2f);
        public NoiseColor Color = NoiseColor.Pink;
        public float Gain = 0.5f;
        public float Pan;
        public float Width = 0.5f;
        public SvfMode? Filter;
        public Func<float, float> Cutoff;
        public float Q = 0.707f;
        public Func<float, float> Amp;
    }

    // ================================================================================
    //  Синтезатор
    // ================================================================================

    public static class SfxLibrary
    {
        private static readonly SfxDef[] Defs = BuildDefs();

        public static SfxDef Def(Sfx id) => Defs[(int)id];
        public static int Count => Defs.Length;

        private static SfxDef[] BuildDefs()
        {
            var d = new SfxDef[Enum.GetValues(typeof(Sfx)).Length];
            for (int i = 0; i < d.Length; i++) d[i] = new SfxDef();
            void S(Sfx id, float vol, int voices = 3, float cd = 0.03f, float jit = 0.02f, int variants = 1,
                   int prio = 1, SfxBus bus = SfxBus.Ui, float duck = 0f)
                => d[(int)id] = new SfxDef { Volume = vol, MaxVoices = voices, Cooldown = cd, PitchJitter = jit,
                                             Variants = variants, Priority = prio, Bus = bus, DuckMusic = duck };

            S(Sfx.UiClick, 0.42f, 3, 0.045f, 0.03f, 3);
            S(Sfx.UiHover, 0.13f, 2, 0.06f, 0.03f, 2, 0);
            S(Sfx.UiConfirm, 0.38f, 2, 0.06f);
            S(Sfx.UiBack, 0.34f, 2, 0.06f);
            S(Sfx.UiDenied, 0.40f, 1, 0.15f, 0f);
            S(Sfx.UiTab, 0.30f, 2, 0.05f, 0.03f, 2);
            S(Sfx.WindowOpen, 0.34f, 2, 0.12f, 0.02f);
            S(Sfx.WindowClose, 0.30f, 2, 0.12f, 0.02f);

            S(Sfx.NotifyInfo, 0.30f, 2, 0.30f, 0f, 1, 1);
            S(Sfx.NotifySuccess, 0.36f, 2, 0.35f, 0f, 1, 2);
            S(Sfx.NotifyWarning, 0.34f, 1, 0.50f, 0f, 1, 2);
            S(Sfx.NotifyDanger, 0.40f, 1, 0.80f, 0f, 1, 2);

            S(Sfx.TimePause, 0.30f, 1, 0.10f, 0f);
            S(Sfx.TimeResume, 0.30f, 1, 0.10f, 0f);
            S(Sfx.TimeSpeed, 0.26f, 1, 0.06f, 0f);

            S(Sfx.SystemSelect, 0.26f, 2, 0.08f, 0.02f);
            S(Sfx.SystemEnter, 0.40f, 1, 0.40f, 0f);
            S(Sfx.SystemExit, 0.36f, 1, 0.40f, 0f);
            S(Sfx.SelectMilitary, 0.42f, 1, 0.10f, 0.03f, 2);
            S(Sfx.SelectScience, 0.34f, 1, 0.10f, 0.02f, 2);
            S(Sfx.SelectConstructor, 0.38f, 1, 0.10f, 0.03f, 2);
            S(Sfx.OrderMove, 0.32f, 2, 0.07f, 0.02f);
            S(Sfx.OrderQueue, 0.26f, 2, 0.05f, 0.02f);
            S(Sfx.OrderStop, 0.30f, 1, 0.08f, 0.02f);
            S(Sfx.FtlJump, 0.30f, 3, 0.18f, 0.05f, 2, 1, SfxBus.World);
            S(Sfx.FtlArrive, 0.24f, 3, 0.18f, 0.05f, 2, 1, SfxBus.World);
            S(Sfx.SurveyComplete, 0.34f, 1, 0.30f, 0f, 1, 2);
            S(Sfx.OutpostBuilt, 0.40f, 1, 0.40f, 0f, 1, 2);
            S(Sfx.Anomaly, 0.42f, 1, 1.00f, 0f, 1, 2, SfxBus.Stinger, 0.4f);

            S(Sfx.BuildQueued, 0.34f, 2, 0.08f, 0.03f, 2);
            S(Sfx.BuildComplete, 0.38f, 2, 0.25f, 0.02f, 1, 2);
            S(Sfx.ShipLaunched, 0.38f, 1, 0.40f, 0.03f, 1, 2);
            S(Sfx.ColonyFounded, 0.40f, 1, 1.00f, 0f, 1, 3, SfxBus.Stinger, 0.35f);
            S(Sfx.ResearchComplete, 0.42f, 1, 0.80f, 0f, 1, 3, SfxBus.Stinger, 0.35f);
            S(Sfx.ResearchStart, 0.26f, 1, 0.10f, 0.02f);

            S(Sfx.WeaponKinetic, 0.26f, 5, 0.045f, 0.06f, 3, 0, SfxBus.World);
            S(Sfx.WeaponEnergy, 0.22f, 5, 0.045f, 0.06f, 3, 0, SfxBus.World);
            S(Sfx.WeaponMissile, 0.24f, 4, 0.08f, 0.05f, 2, 0, SfxBus.World);
            S(Sfx.HitShield, 0.18f, 4, 0.05f, 0.08f, 3, 0, SfxBus.World);
            S(Sfx.HitArmor, 0.20f, 4, 0.05f, 0.08f, 3, 0, SfxBus.World);
            S(Sfx.ExplosionSmall, 0.44f, 4, 0.07f, 0.06f, 3, 2, SfxBus.World);
            S(Sfx.ExplosionLarge, 0.52f, 2, 0.25f, 0.04f, 2, 3, SfxBus.World);
            S(Sfx.StarbaseDown, 0.55f, 1, 1.00f, 0f, 1, 3, SfxBus.World);
            S(Sfx.BattleStart, 0.46f, 1, 2.50f, 0f, 1, 3, SfxBus.Stinger, 0.4f);
            S(Sfx.BattleVictory, 0.46f, 1, 2.00f, 0f, 1, 3, SfxBus.Stinger, 0.5f);
            S(Sfx.BattleDefeat, 0.44f, 1, 2.00f, 0f, 1, 3, SfxBus.Stinger, 0.5f);
            S(Sfx.Disengage, 0.30f, 2, 0.15f, 0.04f, 1, 1, SfxBus.World);
            S(Sfx.FtlFail, 0.30f, 2, 0.15f, 0.03f, 1, 1, SfxBus.World);

            S(Sfx.WarDeclared, 0.55f, 1, 2.00f, 0f, 1, 3, SfxBus.Stinger, 0.6f);
            S(Sfx.PeaceSigned, 0.45f, 1, 2.00f, 0f, 1, 3, SfxBus.Stinger, 0.5f);
            S(Sfx.PactSigned, 0.40f, 1, 1.50f, 0f, 1, 3, SfxBus.Stinger, 0.4f);
            S(Sfx.SiegeStart, 0.36f, 1, 1.00f, 0f, 1, 2);
            S(Sfx.SystemCaptured, 0.44f, 1, 1.00f, 0f, 1, 3, SfxBus.Stinger, 0.4f);
            S(Sfx.SystemLost, 0.44f, 1, 1.00f, 0f, 1, 3, SfxBus.Stinger, 0.4f);
            S(Sfx.GameVictory, 0.55f, 1, 5.00f, 0f, 1, 3, SfxBus.Stinger, 0.8f);
            S(Sfx.GameDefeat, 0.55f, 1, 5.00f, 0f, 1, 3, SfxBus.Stinger, 0.8f);
            S(Sfx.GameStart, 0.48f, 1, 3.00f, 0f, 1, 3, SfxBus.Stinger, 0.5f);
            return d;
        }

        // ================================================================================
        //  Рендер слоёв
        // ================================================================================

        public static void Tone(Stereo b, ToneLayer s, Noise rng)
        {
            int sr = b.Sr;
            int i0 = b.Index(s.Start);
            int n = (int)(s.Env.Length * sr) + 1;
            int voices = Math.Max(1, s.Unison);
            var osc = new Osc[voices];
            var pmPhase = new double[voices];
            var modPhase = new double[voices];
            var ratio = new float[voices];
            var vpanL = new float[voices];
            var vpanR = new float[voices];
            for (int v = 0; v < voices; v++)
            {
                // Унисон стартует со случайной фазы — без «флэнджера» от синфазных голосов
                osc[v] = new Osc(s.Wave, voices > 1 ? rng.Value01() : 0.0, s.PulseWidth);
                float x = voices == 1 ? 0.5f : v / (float)(voices - 1);
                float cents = voices == 1 ? 0f : (x * 2f - 1f) * s.DetuneCents;
                ratio[v] = (float)Math.Pow(2.0, cents / 1200.0);
                float p = voices == 1 ? 0f : (x * 2f - 1f) * s.Spread;
                float a = (p + 1f) * 0.25f * (float)Math.PI;
                vpanL[v] = (float)Math.Cos(a) * 1.4142f;
                vpanR[v] = (float)Math.Sin(a) * 1.4142f;
            }
            float uniNorm = 1f / (float)Math.Sqrt(voices);
            var fl = new Svf(s.Filter ?? SvfMode.LowPass);
            var fr = new Svf(s.Filter ?? SvfMode.LowPass);
            float panL = s.Pan > 0f ? 1f - s.Pan : 1f, panR = s.Pan < 0f ? 1f + s.Pan : 1f;
            float driveNorm = s.Drive > 0f ? 1f / (float)Math.Tanh(1f + s.Drive) : 1f;

            for (int k = 0; k < n; k++)
            {
                int i = i0 + k;
                if (i >= b.Length) break;
                float t = k / (float)sr;
                float env = s.Env.Value(t);
                float f = s.Freq(t);
                if (s.VibratoCents > 0f)
                    f *= (float)Math.Pow(2.0, s.VibratoCents / 1200.0 * Math.Sin(2.0 * Math.PI * s.VibratoHz * t));
                float idx = s.FmIndex != null ? s.FmIndex(t) : 0f;

                float l = 0f, r = 0f;
                for (int v = 0; v < voices; v++)
                {
                    float fv = f * ratio[v];
                    float y;
                    if (s.Wave == Wave.Sine && s.FmRatio > 0f)
                    {
                        double mod = Math.Sin(2.0 * Math.PI * modPhase[v]) * idx;
                        y = (float)Math.Sin(2.0 * Math.PI * pmPhase[v] + mod);
                        pmPhase[v] += fv / sr; if (pmPhase[v] >= 1.0) pmPhase[v] -= 1.0;
                        modPhase[v] += fv * s.FmRatio / sr; if (modPhase[v] >= 1.0) modPhase[v] -= 1.0;
                    }
                    else y = osc[v].Next(fv, sr);
                    l += y * vpanL[v];
                    r += y * vpanR[v];
                }
                l *= uniNorm; r *= uniNorm;

                if (s.Filter.HasValue)
                {
                    float c = s.Cutoff != null ? s.Cutoff(t) : 2000f;
                    l = fl.Process(l, c, s.Q, sr);
                    r = fr.Process(r, c, s.Q, sr);
                }
                if (s.Drive > 0f)
                {
                    l = (float)Math.Tanh(l * (1f + s.Drive)) * driveNorm;
                    r = (float)Math.Tanh(r * (1f + s.Drive)) * driveNorm;
                }
                float g = env * s.Gain * (s.Amp != null ? s.Amp(t) : 1f);
                b.Add(i, l * g * panL, r * g * panR);
            }
        }

        public static void NoiseL(Stereo b, NoiseLayer s, int seed)
        {
            int sr = b.Sr;
            int i0 = b.Index(s.Start);
            int n = (int)(s.Env.Length * sr) + 1;
            var common = new Noise(seed * 3 + 1);
            var nl = new Noise(seed * 3 + 2);
            var nr = new Noise(seed * 3 + 3);
            var fl = new Svf(s.Filter ?? SvfMode.LowPass);
            var fr = new Svf(s.Filter ?? SvfMode.LowPass);
            float w = Note.Clamp01(s.Width);
            float panL = s.Pan > 0f ? 1f - s.Pan : 1f, panR = s.Pan < 0f ? 1f + s.Pan : 1f;

            for (int k = 0; k < n; k++)
            {
                int i = i0 + k;
                if (i >= b.Length) break;
                float t = k / (float)sr;
                float c = Gen(common, s.Color);
                float l = c * (1f - w) + Gen(nl, s.Color) * w;
                float r = c * (1f - w) + Gen(nr, s.Color) * w;
                if (s.Filter.HasValue)
                {
                    float cut = s.Cutoff != null ? s.Cutoff(t) : 2000f;
                    l = fl.Process(l, cut, s.Q, sr);
                    r = fr.Process(r, cut, s.Q, sr);
                }
                float g = s.Env.Value(t) * s.Gain * (s.Amp != null ? s.Amp(t) : 1f);
                b.Add(i, l * g * panL, r * g * panR);
            }
        }

        private static float Gen(Noise n, NoiseColor c) => c == NoiseColor.White ? n.White() : c == NoiseColor.Pink ? n.Pink() : n.Brown();

        // ---------- готовые «инструменты» ----------

        /// <summary>FM-колокольчик: яркая атака, тембр темнеет по мере затухания.</summary>
        private static void Bell(Stereo b, Noise rng, float start, float midi, float decay, float gain,
                                 float pan = 0f, float ratio = 3.5f, float index = 2f)
        {
            float hz = Note.Hz(midi);
            Tone(b, new ToneLayer
            {
                Start = start, Env = Adsr.Pluck(0.0025f, decay, 4.5f), Freq = _ => hz, Gain = gain, Pan = pan,
                FmRatio = ratio, FmIndex = t => index * (float)Math.Exp(-t * 7f / Math.Max(0.05f, decay)),
            }, rng);
            // Чистый обертон октавой выше — «стекло»
            Tone(b, new ToneLayer
            {
                Start = start, Env = Adsr.Pluck(0.002f, decay * 0.45f, 5f), Freq = _ => hz * 2.001f, Gain = gain * 0.22f, Pan = -pan * 0.5f,
            }, rng);
        }

        /// <summary>Удар с падением высоты: «бочка», корпусный удар, саб у взрывов.</summary>
        private static void Thump(Stereo b, Noise rng, float start, float f0, float f1, float sweep, float decay, float gain)
        {
            Tone(b, new ToneLayer
            {
                Start = start, Env = Adsr.Pluck(0.0018f, decay, 4f), Gain = gain,
                Freq = t => Note.ExpLerp(f0, f1, t / sweep),
            }, rng);
        }

        /// <summary>Металлический резонанс: набор неравномерных обертонов с собственным затуханием.</summary>
        private static void Modal(Stereo b, Noise rng, float start, float baseHz, float[] ratios, float decay, float gain, float pan = 0f)
        {
            for (int i = 0; i < ratios.Length; i++)
            {
                float hz = baseHz * ratios[i] * (1f + rng.Range(-0.004f, 0.004f));
                float d = decay / (1f + i * 0.45f);
                Tone(b, new ToneLayer
                {
                    Start = start, Env = Adsr.Pluck(0.0015f, d, 5f), Freq = _ => hz,
                    Gain = gain / (1f + i * 0.6f), Pan = Note.Clamp01(Math.Abs(pan)) * Math.Sign(pan) + rng.Range(-0.25f, 0.25f),
                }, rng);
            }
        }

        /// <summary>Треск обломков: редкие импульсы через резонансный полосовой фильтр.</summary>
        private static void Crackle(Stereo b, Noise rng, float start, float dur, float density, float gain, float cutoff)
        {
            int sr = b.Sr, i0 = b.Index(start), n = (int)(dur * sr);
            var fl = new Svf(SvfMode.BandPass); var fr = new Svf(SvfMode.BandPass);
            float p = density / sr;
            float fade = Math.Max(1, Math.Min(n / 6, sr / 50));
            for (int k = 0; k < n; k++)
            {
                int i = i0 + k; if (i >= b.Length) break;
                float x = k / (float)n;
                float env = (1f - x) * (1f - x) * Math.Min(1f, k / fade);
                float imp = rng.Value01() < p ? rng.Range(-1f, 1f) * env : 0f;
                float pan = rng.Range(-0.7f, 0.7f);
                float l = fl.Process(imp * (1f - pan), cutoff, 2.5f, sr);
                float r = fr.Process(imp * (1f + pan), cutoff * 1.07f, 2.5f, sr);
                b.Add(i, l * gain * 4f, r * gain * 4f);
            }
        }

        /// <summary>Медь: пилы в унисоне с фильтром, раскрывающимся вслед за огибающей.</summary>
        private static void Brass(Stereo b, Noise rng, float start, float[] midis, float hold, float gain, float bright = 1800f, float dark = 280f)
        {
            var env = new Adsr(0.09f, 0.18f, 0.78f, hold, 0.55f, 3.5f);
            foreach (float m in midis)
            {
                float hz = Note.Hz(m);
                var e = env;
                Tone(b, new ToneLayer
                {
                    Start = start, Env = env, Wave = Wave.Saw, Unison = 3, DetuneCents = 9f, Spread = 0.5f,
                    Freq = _ => hz, VibratoHz = 5.2f, VibratoCents = 6f, Gain = gain / midis.Length * 1.6f,
                    Filter = SvfMode.LowPass, Q = 1.1f, Cutoff = t => dark + bright * e.Value(t),
                }, rng);
            }
        }

        /// <summary>Пэд: мягкий аккорд из расстроенных пил.</summary>
        private static void Pad(Stereo b, Noise rng, float start, float[] midis, Adsr env, float gain, float cutoff, float cutoffEnd = -1f)
        {
            float len = env.Length;
            foreach (float m in midis)
            {
                float hz = Note.Hz(m);
                Tone(b, new ToneLayer
                {
                    Start = start, Env = env, Wave = Wave.Saw, Unison = 3, DetuneCents = 11f, Spread = 0.8f,
                    Freq = _ => hz, Gain = gain / midis.Length * 1.8f, Filter = SvfMode.LowPass, Q = 0.8f,
                    Cutoff = cutoffEnd > 0f ? t => Note.ExpLerp(cutoff, cutoffEnd, t / len) : (Func<float, float>)(_ => cutoff),
                }, rng);
            }
        }

        private static Func<float, float> Glide(float a, float b, float dur) => t => Note.ExpLerp(a, b, t / dur);
        private static Func<float, float> Const(float v) => _ => v;
        /// <summary>Стаккато-гейт со сглаженными фронтами (без щелчков).</summary>
        private static Func<float, float> Stutter(float hz, float duty = 0.55f) => t =>
        {
            double ph = t * hz % 1.0;
            double edge = 0.12;
            if (ph < edge) return (float)(0.5 - 0.5 * Math.Cos(Math.PI * ph / edge));
            if (ph < duty) return 1f;
            if (ph < duty + edge) return (float)(0.5 + 0.5 * Math.Cos(Math.PI * (ph - duty) / edge));
            return 0f;
        };

        // ================================================================================
        //  Рецепты
        // ================================================================================

        /// <summary>Синтезировать звук. Возвращает интерлив-стерео float[] (L,R,L,R…), нормализованный.</summary>
        public static float[] Render(Sfx id, int variant, int sr)
        {
            int seed = (int)id * 7919 + variant * 104729 + 17;
            var rng = new Noise(seed);
            Stereo b;

            switch (id)
            {
                // ───────────────────────────── ИНТЕРФЕЙС ─────────────────────────────
                case Sfx.UiClick:
                {
                    b = new Stereo(0.22f, sr);
                    float f = 2300f + variant * 140f;
                    Tone(b, new ToneLayer { Env = Adsr.Pluck(0.0015f, 0.05f, 6f), Freq = Glide(f, f * 0.66f, 0.03f), Gain = 0.6f }, rng);
                    Tone(b, new ToneLayer { Env = Adsr.Pluck(0.002f, 0.07f, 5f), Freq = Const(820f + variant * 30f), Gain = 0.3f }, rng);
                    NoiseL(b, new NoiseLayer { Env = Adsr.Pluck(0.001f, 0.018f, 6f), Color = NoiseColor.White, Gain = 0.45f,
                        Filter = SvfMode.BandPass, Cutoff = Const(5200f), Q = 1.2f, Width = 0.3f }, seed);
                    b.ApplyReverb(0.08f, 0.3f, 0.6f);
                    break;
                }
                case Sfx.UiHover:
                {
                    b = new Stereo(0.15f, sr);
                    Tone(b, new ToneLayer { Env = Adsr.Pluck(0.003f, 0.035f, 5f), Freq = Glide(3400f + variant * 200f, 3000f, 0.03f), Gain = 0.5f }, rng);
                    NoiseL(b, new NoiseLayer { Env = Adsr.Pluck(0.002f, 0.025f, 5f), Gain = 0.25f, Filter = SvfMode.HighPass, Cutoff = Const(6000f) }, seed);
                    b.ApplyReverb(0.06f, 0.3f, 0.6f);
                    break;
                }
                case Sfx.UiConfirm:
                {
                    b = new Stereo(0.9f, sr);
                    Bell(b, rng, 0.00f, 88, 0.32f, 0.45f, -0.15f, 3.5f, 1.4f);
                    Bell(b, rng, 0.07f, 95, 0.38f, 0.40f, 0.15f, 3.5f, 1.4f);
                    b.ApplyPingPong(0.11f, 0.25f, 0.18f, 4500f);
                    b.ApplyReverb(0.14f, 0.55f, 0.5f);
                    break;
                }
                case Sfx.UiBack:
                {
                    b = new Stereo(0.7f, sr);
                    Bell(b, rng, 0.00f, 91, 0.22f, 0.4f, 0.1f, 2f, 1.0f);
                    Bell(b, rng, 0.06f, 84, 0.30f, 0.4f, -0.1f, 2f, 1.0f);
                    b.ApplyReverb(0.12f, 0.5f, 0.5f);
                    break;
                }
                case Sfx.UiDenied:
                {
                    b = new Stereo(0.45f, sr);
                    for (int i = 0; i < 2; i++)
                    {
                        float st = i * 0.11f;
                        Tone(b, new ToneLayer { Start = st, Env = new Adsr(0.004f, 0.02f, 0.7f, 0.05f, 0.04f), Wave = Wave.Square,
                            Unison = 2, DetuneCents = 12f, Freq = Const(155f), Gain = 0.45f, Filter = SvfMode.LowPass, Cutoff = Const(1400f), Q = 1.5f }, rng);
                        Tone(b, new ToneLayer { Start = st, Env = new Adsr(0.004f, 0.02f, 0.7f, 0.05f, 0.05f), Freq = Const(77.5f), Gain = 0.4f }, rng);
                    }
                    b.ApplyReverb(0.1f, 0.35f, 0.6f);
                    break;
                }
                case Sfx.UiTab:
                {
                    b = new Stereo(0.3f, sr);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(0.012f, 0.02f, 0.6f, 0.03f, 0.05f), Gain = 0.5f, Filter = SvfMode.BandPass,
                        Cutoff = Glide(1500f, 4000f + variant * 300f, 0.09f), Q = 1.3f, Width = 0.7f }, seed);
                    Tone(b, new ToneLayer { Start = 0.02f, Env = Adsr.Pluck(0.0015f, 0.05f, 6f), Freq = Glide(2000f, 1500f, 0.03f), Gain = 0.35f }, rng);
                    b.ApplyReverb(0.08f, 0.35f, 0.5f);
                    break;
                }
                case Sfx.WindowOpen:
                {
                    b = new Stereo(1.1f, sr);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(0.18f, 0.05f, 0.7f, 0.02f, 0.18f, 3f), Gain = 0.6f, Filter = SvfMode.BandPass,
                        Cutoff = Glide(300f, 3500f, 0.32f), Q = 1.4f, Width = 0.7f }, seed);
                    Tone(b, new ToneLayer { Env = new Adsr(0.12f, 0.1f, 0.5f, 0f, 0.25f), Freq = Glide(330f, 660f, 0.3f), Gain = 0.22f, FmRatio = 2f, FmIndex = Const(0.6f) }, rng);
                    Bell(b, rng, 0.18f, 100, 0.4f, 0.1f, -0.3f, 2f, 0.6f);
                    Bell(b, rng, 0.22f, 107, 0.35f, 0.07f, 0.3f, 2f, 0.6f);
                    b.ApplyReverb(0.25f, 0.6f, 0.5f);
                    break;
                }
                case Sfx.WindowClose:
                {
                    b = new Stereo(0.8f, sr);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(0.03f, 0.05f, 0.6f, 0.02f, 0.15f), Gain = 0.55f, Filter = SvfMode.BandPass,
                        Cutoff = Glide(3000f, 350f, 0.25f), Q = 1.4f, Width = 0.7f }, seed);
                    Tone(b, new ToneLayer { Env = new Adsr(0.01f, 0.08f, 0.4f, 0f, 0.15f), Freq = Glide(700f, 330f, 0.2f), Gain = 0.18f }, rng);
                    b.ApplyReverb(0.18f, 0.55f, 0.5f);
                    break;
                }

                // ───────────────────────────── УВЕДОМЛЕНИЯ ─────────────────────────────
                case Sfx.NotifyInfo:
                {
                    b = new Stereo(1.4f, sr);
                    Bell(b, rng, 0.00f, 84, 0.6f, 0.45f, -0.2f, 2f, 1.2f);
                    Bell(b, rng, 0.09f, 91, 0.7f, 0.40f, 0.2f, 2f, 1.2f);
                    b.ApplyPingPong(0.14f, 0.3f, 0.22f, 4000f);
                    b.ApplyReverb(0.22f, 0.7f, 0.5f);
                    break;
                }
                case Sfx.NotifySuccess:
                {
                    b = new Stereo(2.0f, sr);
                    float[] arp = { 72, 76, 79, 84 };
                    for (int i = 0; i < arp.Length; i++) Bell(b, rng, i * 0.07f, arp[i], 0.9f, 0.32f, -0.3f + i * 0.2f, 3f, 1.3f);
                    Pad(b, rng, 0.05f, new float[] { 60, 67, 72 }, new Adsr(0.15f, 0.3f, 0.5f, 0.2f, 0.8f), 0.18f, 1800f);
                    b.ApplyReverb(0.28f, 0.75f, 0.45f);
                    break;
                }
                case Sfx.NotifyWarning:
                {
                    b = new Stereo(0.9f, sr);
                    float[] seq = { 81, 77, 81, 77 };
                    for (int i = 0; i < seq.Length; i++)
                    {
                        float hz = Note.Hz(seq[i]);
                        float st = i * 0.13f;
                        Tone(b, new ToneLayer { Start = st, Env = new Adsr(0.005f, 0.03f, 0.7f, 0.06f, 0.04f), Wave = Wave.Square, PulseWidth = 0.3f,
                            Freq = Const(hz), Gain = 0.3f, Filter = SvfMode.LowPass, Cutoff = Const(2200f), Q = 0.9f }, rng);
                        Tone(b, new ToneLayer { Start = st, Env = new Adsr(0.005f, 0.03f, 0.7f, 0.06f, 0.05f), Freq = Const(hz * 0.5f), Gain = 0.3f }, rng);
                    }
                    b.ApplyReverb(0.15f, 0.5f, 0.5f);
                    break;
                }
                case Sfx.NotifyDanger:
                {
                    b = new Stereo(1.4f, sr);
                    for (int i = 0; i < 2; i++)
                    {
                        float st = i * 0.42f;
                        Tone(b, new ToneLayer { Start = st, Env = new Adsr(0.02f, 0.05f, 0.85f, 0.22f, 0.08f), Wave = Wave.Saw, Unison = 2, DetuneCents = 15f,
                            Freq = Glide(520f, 390f, 0.35f), Gain = 0.4f, Filter = SvfMode.LowPass, Cutoff = Const(2600f), Q = 2f, Drive = 0.6f }, rng);
                        Tone(b, new ToneLayer { Start = st, Env = new Adsr(0.02f, 0.05f, 0.85f, 0.22f, 0.1f), Freq = Glide(260f, 195f, 0.35f), Gain = 0.35f }, rng);
                    }
                    b.ApplyReverb(0.24f, 0.6f, 0.5f);
                    break;
                }

                // ───────────────────────────── ВРЕМЯ ─────────────────────────────
                case Sfx.TimePause:
                {
                    b = new Stereo(0.7f, sr);
                    var env = new Adsr(0.005f, 0.05f, 0.8f, 0.15f, 0.15f);
                    Tone(b, new ToneLayer { Env = env, Wave = Wave.Saw, Freq = Glide(220f, 40f, 0.35f), Gain = 0.4f,
                        Filter = SvfMode.LowPass, Cutoff = Glide(3000f, 200f, 0.35f), Q = 1.2f }, rng);
                    Tone(b, new ToneLayer { Env = env, Freq = Glide(110f, 25f, 0.35f), Gain = 0.35f }, rng);
                    b.ApplyReverb(0.12f, 0.4f, 0.6f);
                    break;
                }
                case Sfx.TimeResume:
                {
                    b = new Stereo(0.6f, sr);
                    var env = new Adsr(0.03f, 0.05f, 0.8f, 0.12f, 0.12f);
                    Tone(b, new ToneLayer { Env = env, Wave = Wave.Saw, Freq = Glide(60f, 330f, 0.25f), Gain = 0.38f,
                        Filter = SvfMode.LowPass, Cutoff = Glide(300f, 3000f, 0.25f), Q = 1.2f }, rng);
                    Tone(b, new ToneLayer { Env = env, Freq = Glide(30f, 165f, 0.25f), Gain = 0.3f }, rng);
                    b.ApplyReverb(0.12f, 0.4f, 0.6f);
                    break;
                }
                case Sfx.TimeSpeed:
                {
                    b = new Stereo(0.4f, sr);
                    Tone(b, new ToneLayer { Env = Adsr.Pluck(0.002f, 0.06f), Freq = Glide(1000f, 1500f, 0.04f), Gain = 0.45f }, rng);
                    Tone(b, new ToneLayer { Start = 0.05f, Env = Adsr.Pluck(0.002f, 0.08f), Freq = Glide(1500f, 2000f, 0.04f), Gain = 0.4f }, rng);
                    b.ApplyReverb(0.1f, 0.4f, 0.5f);
                    break;
                }

                // ───────────────────────────── КАРТА ─────────────────────────────
                case Sfx.SystemSelect:
                {
                    b = new Stereo(1.8f, sr);
                    Tone(b, new ToneLayer { Env = Adsr.Pluck(0.004f, 0.9f, 4f), Freq = Glide(1350f, 1300f, 0.2f), Gain = 0.5f }, rng);
                    Tone(b, new ToneLayer { Env = Adsr.Pluck(0.002f, 0.25f, 5f), Freq = Const(2637f), Gain = 0.12f }, rng);
                    b.ApplyPingPong(0.19f, 0.38f, 0.32f, 3500f);
                    b.ApplyReverb(0.28f, 0.8f, 0.5f);
                    break;
                }
                case Sfx.SystemEnter:
                {
                    b = new Stereo(2.2f, sr);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(0.45f, 0.1f, 0.5f, 0f, 0.6f, 3f), Gain = 0.55f, Filter = SvfMode.LowPass,
                        Cutoff = Glide(200f, 4000f, 0.6f), Q = 1.1f, Width = 0.8f }, seed);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(0.3f, 0.3f, 0.6f, 0.2f, 0.6f), Color = NoiseColor.Brown, Gain = 0.6f,
                        Filter = SvfMode.LowPass, Cutoff = Const(180f) }, seed + 1);
                    Tone(b, new ToneLayer { Env = new Adsr(0.4f, 0.2f, 0.5f, 0.1f, 0.6f), Wave = Wave.Saw, Unison = 3, DetuneCents = 18f,
                        Freq = Glide(55f, 110f, 0.6f), Gain = 0.25f, Filter = SvfMode.LowPass, Cutoff = Const(600f) }, rng);
                    Bell(b, rng, 0.45f, 88, 0.8f, 0.12f, -0.3f, 2f, 0.8f);
                    Bell(b, rng, 0.52f, 95, 0.8f, 0.10f, 0.3f, 2f, 0.8f);
                    b.ApplyReverb(0.32f, 0.8f, 0.5f);
                    break;
                }
                case Sfx.SystemExit:
                {
                    b = new Stereo(1.6f, sr);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(0.05f, 0.1f, 0.6f, 0.1f, 0.45f), Gain = 0.55f, Filter = SvfMode.LowPass,
                        Cutoff = Glide(4000f, 200f, 0.55f), Q = 1.1f, Width = 0.8f }, seed);
                    Tone(b, new ToneLayer { Env = new Adsr(0.05f, 0.2f, 0.5f, 0.05f, 0.4f), Wave = Wave.Saw, Unison = 3, DetuneCents = 18f,
                        Freq = Glide(110f, 50f, 0.6f), Gain = 0.25f, Filter = SvfMode.LowPass, Cutoff = Const(600f) }, rng);
                    b.ApplyReverb(0.28f, 0.75f, 0.5f);
                    break;
                }
                case Sfx.SelectMilitary:
                {
                    b = new Stereo(0.9f, sr);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(0.002f, 0.02f, 0.4f, 0.02f, 0.04f), Color = NoiseColor.White, Gain = 0.3f,
                        Filter = SvfMode.BandPass, Cutoff = Const(2500f), Q = 2f }, seed);
                    float n1 = variant == 0 ? 76 : 74, n2 = variant == 0 ? 81 : 79;
                    Tone(b, new ToneLayer { Start = 0.03f, Env = new Adsr(0.003f, 0.02f, 0.7f, 0.03f, 0.03f), Wave = Wave.Square, PulseWidth = 0.25f,
                        Freq = Const(Note.Hz(n1)), Gain = 0.25f, Filter = SvfMode.LowPass, Cutoff = Const(3000f) }, rng);
                    Tone(b, new ToneLayer { Start = 0.10f, Env = new Adsr(0.003f, 0.02f, 0.7f, 0.05f, 0.04f), Wave = Wave.Square, PulseWidth = 0.25f,
                        Freq = Const(Note.Hz(n2)), Gain = 0.25f, Filter = SvfMode.LowPass, Cutoff = Const(3000f) }, rng);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(0.06f, 0.1f, 0.6f, 0.15f, 0.35f), Color = NoiseColor.Brown, Gain = 0.7f,
                        Filter = SvfMode.LowPass, Cutoff = Const(150f) }, seed + 5);
                    Thump(b, rng, 0f, 110f, 55f, 0.08f, 0.18f, 0.5f);
                    Modal(b, rng, 0f, 320f, new[] { 1f, 2.32f, 4.25f, 6.63f }, 0.12f, 0.2f);
                    b.ApplyReverb(0.12f, 0.4f, 0.5f);
                    break;
                }
                case Sfx.SelectScience:
                {
                    b = new Stereo(1.1f, sr);
                    float[] scale = { 84, 86, 88, 91, 93, 96 };
                    for (int i = 0; i < 6; i++)
                    {
                        float m = scale[(i * (variant + 2) + variant) % scale.Length];
                        if (i == 5) m = 96;
                        Bell(b, rng, i * 0.035f, m, 0.18f, 0.22f, i % 2 == 0 ? -0.4f : 0.4f, 2f, 0.7f);
                    }
                    Tone(b, new ToneLayer { Env = new Adsr(0.05f, 0.1f, 0.5f, 0.1f, 0.3f), Freq = Const(1760f), Gain = 0.06f,
                        Amp = t => 0.6f + 0.4f * (float)Math.Sin(t * 2 * Math.PI * 18) }, rng);
                    b.ApplyPingPong(0.09f, 0.35f, 0.28f, 5000f);
                    b.ApplyReverb(0.18f, 0.6f, 0.5f);
                    break;
                }
                case Sfx.SelectConstructor:
                {
                    b = new Stereo(1.0f, sr);
                    Modal(b, rng, 0f, 180f + variant * 25f, new[] { 1f, 1.47f, 2.09f, 2.56f, 3.9f }, 0.45f, 0.45f);
                    NoiseL(b, new NoiseLayer { Start = 0.05f, Env = new Adsr(0.02f, 0.1f, 0.5f, 0.1f, 0.25f), Color = NoiseColor.White, Gain = 0.22f,
                        Filter = SvfMode.BandPass, Cutoff = Glide(6000f, 2500f, 0.3f), Q = 1.2f, Width = 0.8f }, seed);
                    Tone(b, new ToneLayer { Start = 0.1f, Env = new Adsr(0.03f, 0.05f, 0.6f, 0.08f, 0.08f), Wave = Wave.Saw,
                        Freq = Glide(400f, 600f, 0.15f), Gain = 0.13f, Filter = SvfMode.LowPass, Cutoff = Const(1500f) }, rng);
                    Thump(b, rng, 0f, 90f, 50f, 0.1f, 0.2f, 0.5f);
                    b.ApplyReverb(0.15f, 0.45f, 0.5f);
                    break;
                }
                case Sfx.OrderMove:
                {
                    b = new Stereo(0.8f, sr);
                    Bell(b, rng, 0.00f, 79, 0.12f, 0.35f, -0.1f, 2f, 0.9f);
                    Bell(b, rng, 0.06f, 86, 0.16f, 0.35f, 0.1f, 2f, 0.9f);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(0.03f, 0.05f, 0.4f, 0.02f, 0.12f), Gain = 0.22f, Filter = SvfMode.BandPass,
                        Cutoff = Glide(800f, 2500f, 0.12f), Q = 1.3f, Width = 0.7f }, seed);
                    b.ApplyPingPong(0.08f, 0.2f, 0.15f, 4000f);
                    b.ApplyReverb(0.12f, 0.5f, 0.5f);
                    break;
                }
                case Sfx.OrderQueue:
                {
                    b = new Stereo(0.6f, sr);
                    Bell(b, rng, 0.00f, 91, 0.12f, 0.35f, 0.2f, 2f, 0.8f);
                    Bell(b, rng, 0.04f, 96, 0.14f, 0.25f, 0.3f, 2f, 0.8f);
                    b.ApplyReverb(0.1f, 0.45f, 0.5f);
                    break;
                }
                case Sfx.OrderStop:
                {
                    b = new Stereo(0.7f, sr);
                    Bell(b, rng, 0.00f, 86, 0.12f, 0.35f, 0f, 2f, 0.8f);
                    Bell(b, rng, 0.06f, 79, 0.18f, 0.35f, 0f, 2f, 0.8f);
                    Thump(b, rng, 0.06f, 120f, 60f, 0.06f, 0.12f, 0.4f);
                    b.ApplyReverb(0.1f, 0.45f, 0.5f);
                    break;
                }
                case Sfx.FtlJump:
                {
                    b = new Stereo(2.4f, sr);
                    float c = 0.55f + variant * 0.05f;
                    var charge = new Adsr(c - 0.12f, 0.05f, 0.9f, 0.05f, 0.12f);
                    Tone(b, new ToneLayer { Env = charge, Wave = Wave.Saw, Unison = 4, DetuneCents = 20f, Freq = Glide(80f, 900f, c),
                        Gain = 0.32f, Filter = SvfMode.LowPass, Cutoff = Glide(300f, 6000f, c), Q = 1.6f }, rng);
                    Tone(b, new ToneLayer { Env = charge, Freq = Glide(40f, 160f, c), Gain = 0.3f }, rng);
                    NoiseL(b, new NoiseLayer { Start = c, Env = new Adsr(0.005f, 0.08f, 0.5f, 0.05f, 0.45f), Gain = 0.7f, Filter = SvfMode.LowPass,
                        Cutoff = Glide(8000f, 300f, 0.5f), Width = 0.8f }, seed);
                    Thump(b, rng, c, 120f, 35f, 0.2f, 0.35f, 0.7f);
                    b.ApplyPingPong(0.15f, 0.3f, 0.15f, 3000f);
                    b.ApplyReverb(0.3f, 0.85f, 0.5f);
                    break;
                }
                case Sfx.FtlArrive:
                {
                    b = new Stereo(1.6f, sr);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(0.002f, 0.05f, 0.4f, 0.05f, 0.4f), Gain = 0.6f, Filter = SvfMode.LowPass,
                        Cutoff = Glide(6000f, 400f, 0.4f), Width = 0.8f }, seed);
                    Tone(b, new ToneLayer { Env = new Adsr(0.003f, 0.1f, 0.5f, 0.05f, 0.25f), Wave = Wave.Saw, Unison = 3, DetuneCents = 15f,
                        Freq = Glide(700f + variant * 80f, 90f, 0.35f), Gain = 0.28f, Filter = SvfMode.LowPass, Cutoff = Glide(4000f, 400f, 0.35f) }, rng);
                    Thump(b, rng, 0f, 90f, 40f, 0.12f, 0.3f, 0.6f);
                    b.ApplyReverb(0.25f, 0.8f, 0.5f);
                    break;
                }
                case Sfx.SurveyComplete:
                {
                    b = new Stereo(2.0f, sr);
                    for (int i = 0; i < 9; i++)
                    {
                        float hz = rng.Range(1500f, 4000f);
                        Tone(b, new ToneLayer { Start = i * 0.026f, Env = Adsr.Pluck(0.0015f, 0.03f, 5f), Freq = Const(hz),
                            Gain = 0.2f, Pan = rng.Range(-0.6f, 0.6f) }, rng);
                    }
                    Bell(b, rng, 0.25f, 84, 0.9f, 0.25f, -0.2f, 3f, 1.2f);
                    Bell(b, rng, 0.25f, 88, 0.9f, 0.22f, 0f, 3f, 1.2f);
                    Bell(b, rng, 0.25f, 91, 0.9f, 0.20f, 0.2f, 3f, 1.2f);
                    b.ApplyReverb(0.28f, 0.7f, 0.5f);
                    break;
                }
                case Sfx.OutpostBuilt:
                {
                    b = new Stereo(3.0f, sr);
                    Thump(b, rng, 0f, 70f, 40f, 0.15f, 0.5f, 0.8f);
                    Modal(b, rng, 0f, 140f, new[] { 1f, 2.76f, 5.4f, 8.93f }, 0.8f, 0.35f);
                    Pad(b, rng, 0.05f, new float[] { 55, 62, 67, 74 }, new Adsr(0.2f, 0.3f, 0.6f, 0.4f, 1.0f), 0.2f, 1200f);
                    Bell(b, rng, 0.30f, 79, 1.0f, 0.2f, -0.25f);
                    Bell(b, rng, 0.38f, 86, 1.0f, 0.18f, 0.25f);
                    b.ApplyReverb(0.35f, 0.8f, 0.5f);
                    break;
                }
                case Sfx.Anomaly:
                {
                    b = new Stereo(4.2f, sr);
                    var env = new Adsr(0.6f, 0.4f, 0.7f, 0.6f, 1.2f, 3f);
                    float[] notes = { 61, 67, 68, 73 };
                    foreach (float m in notes)
                        Tone(b, new ToneLayer { Env = env, Freq = Const(Note.Hz(m)), Gain = 0.16f, FmRatio = 1.41f, FmIndex = t => 0.8f + 0.5f * (float)Math.Sin(t * 1.3f),
                            VibratoHz = 0.4f + m * 0.002f, VibratoCents = 12f, Pan = (m - 66f) / 10f }, rng);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(0.5f, 0.5f, 0.5f, 0.5f, 1.0f), Gain = 0.18f, Filter = SvfMode.BandPass,
                        Cutoff = t => 1600f + 800f * (float)Math.Sin(t * 2.1f), Q = 3f, Width = 0.9f }, seed);
                    Bell(b, rng, 0.9f, 97, 1.6f, 0.06f, 0.5f, 1.41f, 2f);
                    b.ApplyPingPong(0.31f, 0.45f, 0.25f, 3000f);
                    b.ApplyReverb(0.5f, 0.92f, 0.4f);
                    break;
                }

                // ───────────────────────────── СТРОИТЕЛЬСТВО И НАУКА ─────────────────────────────
                case Sfx.BuildQueued:
                {
                    b = new Stereo(0.6f, sr);
                    for (int i = 0; i < 3; i++)
                        NoiseL(b, new NoiseLayer { Start = i * 0.045f, Env = Adsr.Pluck(0.001f, 0.02f, 6f), Color = NoiseColor.White, Gain = 0.45f,
                            Filter = SvfMode.BandPass, Cutoff = Const(3000f + i * 400f + variant * 200f), Q = 3f, Width = 0.3f }, seed + i);
                    Modal(b, rng, 0.0f, 600f, new[] { 1f, 2.3f, 3.7f }, 0.08f, 0.15f);
                    Thump(b, rng, 0.12f, 140f, 70f, 0.06f, 0.12f, 0.5f);
                    b.ApplyReverb(0.1f, 0.4f, 0.5f);
                    break;
                }
                case Sfx.BuildComplete:
                {
                    b = new Stereo(1.8f, sr);
                    Thump(b, rng, 0f, 100f, 50f, 0.1f, 0.25f, 0.7f);
                    Modal(b, rng, 0f, 260f, new[] { 1f, 2.76f, 5.4f }, 0.6f, 0.35f);
                    NoiseL(b, new NoiseLayer { Env = Adsr.Pluck(0.001f, 0.08f, 5f), Gain = 0.35f, Filter = SvfMode.LowPass, Cutoff = Const(3000f) }, seed);
                    Bell(b, rng, 0.12f, 84, 0.7f, 0.25f, -0.2f);
                    Bell(b, rng, 0.18f, 91, 0.8f, 0.22f, 0.2f);
                    b.ApplyReverb(0.25f, 0.65f, 0.5f);
                    break;
                }
                case Sfx.ShipLaunched:
                {
                    b = new Stereo(2.2f, sr);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(0.3f, 0.2f, 0.6f, 0.2f, 0.6f), Color = NoiseColor.Brown, Gain = 0.8f,
                        Filter = SvfMode.LowPass, Cutoff = Glide(100f, 1200f, 0.7f) }, seed);
                    NoiseL(b, new NoiseLayer { Start = 0.2f, Env = new Adsr(0.2f, 0.2f, 0.5f, 0.2f, 0.5f), Gain = 0.25f, Filter = SvfMode.BandPass,
                        Cutoff = Glide(800f, 2500f, 0.8f), Q = 1.2f, Width = 0.8f }, seed + 1);
                    Tone(b, new ToneLayer { Env = new Adsr(0.25f, 0.2f, 0.6f, 0.2f, 0.6f), Wave = Wave.Saw, Unison = 3, DetuneCents = 14f,
                        Freq = Glide(55f, 110f, 0.8f), Gain = 0.25f, Filter = SvfMode.LowPass, Cutoff = Glide(400f, 1500f, 0.8f) }, rng);
                    Bell(b, rng, 0.05f, 79, 0.6f, 0.15f, 0f);
                    b.ApplyReverb(0.3f, 0.8f, 0.5f);
                    break;
                }
                case Sfx.ColonyFounded:
                {
                    b = new Stereo(4.2f, sr);
                    Pad(b, rng, 0f, new float[] { 48, 55, 60, 64, 67, 72 }, new Adsr(0.5f, 0.5f, 0.7f, 0.5f, 1.5f, 3f), 0.22f, 400f, 2200f);
                    float[] arp = { 79, 84, 88, 91 };
                    for (int i = 0; i < arp.Length; i++) Bell(b, rng, 0.4f + i * 0.1f, arp[i], 1.4f, 0.2f, -0.4f + i * 0.27f, 3f, 1.2f);
                    b.ApplyReverb(0.42f, 0.88f, 0.45f);
                    break;
                }
                case Sfx.ResearchComplete:
                {
                    b = new Stereo(4.0f, sr);
                    float[] arp = { 72, 79, 84, 88, 91, 96 };
                    for (int i = 0; i < arp.Length; i++) Bell(b, rng, i * 0.06f, arp[i], 1.2f, 0.22f, (i % 2 == 0 ? -1 : 1) * 0.35f, 3f, 1.5f);
                    var env = new Adsr(0.4f, 0.4f, 0.6f, 0.4f, 1.4f, 3f);
                    foreach (float m in new float[] { 60, 67, 74, 76 })
                        Tone(b, new ToneLayer { Start = 0.05f, Env = env, Freq = Const(Note.Hz(m)), Unison = 2, DetuneCents = 6f, Gain = 0.07f }, rng);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(0.3f, 0.1f, 0.3f, 0.1f, 0.4f), Gain = 0.08f, Filter = SvfMode.HighPass,
                        Cutoff = Glide(3000f, 9000f, 0.4f), Width = 1f }, seed);
                    b.ApplyPingPong(0.17f, 0.35f, 0.2f, 5000f);
                    b.ApplyReverb(0.42f, 0.9f, 0.45f);
                    break;
                }
                case Sfx.ResearchStart:
                {
                    b = new Stereo(1.0f, sr);
                    Bell(b, rng, 0.00f, 91, 0.4f, 0.3f, -0.2f, 3f, 1f);
                    Bell(b, rng, 0.05f, 96, 0.5f, 0.25f, 0.2f, 3f, 1f);
                    b.ApplyReverb(0.2f, 0.6f, 0.5f);
                    break;
                }

                // ───────────────────────────── БОЙ ─────────────────────────────
                case Sfx.WeaponKinetic:
                {
                    b = new Stereo(0.8f, sr);
                    NoiseL(b, new NoiseLayer { Env = Adsr.Pluck(0.0008f, 0.015f, 6f), Color = NoiseColor.White, Gain = 0.6f,
                        Filter = SvfMode.HighPass, Cutoff = Const(2000f), Width = 0.3f }, seed);
                    Thump(b, rng, 0f, 160f + variant * 15f, 45f, 0.12f, 0.22f, 0.9f);
                    NoiseL(b, new NoiseLayer { Env = Adsr.Pluck(0.002f, 0.25f, 5f), Color = NoiseColor.Brown, Gain = 0.5f,
                        Filter = SvfMode.LowPass, Cutoff = Const(600f) }, seed + 1);
                    Modal(b, rng, 0f, 900f * rng.Range(0.85f, 1.15f), new[] { 1f, 1.7f, 2.9f }, 0.12f, 0.15f);
                    b.Saturate(0.5f);
                    b.ApplyReverb(0.15f, 0.5f, 0.5f);
                    break;
                }
                case Sfx.WeaponEnergy:
                {
                    b = new Stereo(0.8f, sr);
                    float top = 2000f * (1f + variant * 0.12f);
                    var env = new Adsr(0.002f, 0.06f, 0.5f, 0.04f, 0.12f);
                    Tone(b, new ToneLayer { Env = env, Wave = Wave.Saw, Unison = 2, DetuneCents = 8f, Freq = Glide(top, 280f, 0.22f), Gain = 0.32f,
                        Filter = SvfMode.LowPass, Cutoff = Glide(9000f, 800f, 0.22f), Q = 4f }, rng);
                    Tone(b, new ToneLayer { Env = env, Freq = Glide(top * 0.8f, 220f, 0.22f), Gain = 0.4f }, rng);
                    NoiseL(b, new NoiseLayer { Env = Adsr.Pluck(0.001f, 0.04f, 5f), Gain = 0.2f, Filter = SvfMode.BandPass, Cutoff = Const(4000f) }, seed);
                    b.ApplyPingPong(0.07f, 0.25f, 0.18f, 4000f);
                    b.ApplyReverb(0.14f, 0.5f, 0.5f);
                    break;
                }
                case Sfx.WeaponMissile:
                {
                    b = new Stereo(1.2f, sr);
                    Thump(b, rng, 0f, 200f, 80f, 0.06f, 0.1f, 0.5f);
                    NoiseL(b, new NoiseLayer { Env = Adsr.Pluck(0.001f, 0.05f, 5f), Color = NoiseColor.White, Gain = 0.3f,
                        Filter = SvfMode.HighPass, Cutoff = Const(1500f) }, seed);
                    NoiseL(b, new NoiseLayer { Start = 0.02f, Env = new Adsr(0.06f, 0.1f, 0.6f, 0.15f, 0.3f), Gain = 0.6f, Filter = SvfMode.BandPass,
                        Cutoff = Glide(600f, 3000f + variant * 400f, 0.45f), Q = 1.6f, Width = 0.6f }, seed + 1);
                    Tone(b, new ToneLayer { Env = new Adsr(0.05f, 0.1f, 0.5f, 0.15f, 0.3f), Wave = Wave.Saw, Unison = 2, DetuneCents = 20f,
                        Freq = Const(90f), Gain = 0.15f, Filter = SvfMode.LowPass, Cutoff = Const(500f) }, rng);
                    b.ApplyReverb(0.2f, 0.6f, 0.5f);
                    break;
                }
                case Sfx.HitShield:
                {
                    b = new Stereo(0.8f, sr);
                    float c = 900f * (1f + variant * 0.1f);
                    Tone(b, new ToneLayer { Env = new Adsr(0.002f, 0.05f, 0.4f, 0.05f, 0.18f), Freq = Glide(c, c * 0.66f, 0.25f), Gain = 0.35f,
                        FmRatio = 3.7f, FmIndex = t => 0.4f + 2.2f * (float)Math.Exp(-t * 20f) }, rng);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(0.002f, 0.04f, 0.4f, 0.03f, 0.12f), Color = NoiseColor.White, Gain = 0.35f,
                        Filter = SvfMode.BandPass, Cutoff = Glide(4200f, 1300f, 0.2f), Q = 2.2f, Width = 0.8f }, seed);
                    Crackle(b, rng, 0f, 0.14f, 400f, 0.12f, 3200f);
                    b.ApplyReverb(0.2f, 0.55f, 0.5f);
                    break;
                }
                case Sfx.HitArmor:
                {
                    b = new Stereo(0.9f, sr);
                    NoiseL(b, new NoiseLayer { Env = Adsr.Pluck(0.0008f, 0.03f, 6f), Color = NoiseColor.White, Gain = 0.7f,
                        Filter = SvfMode.BandPass, Cutoff = Const(2500f), Q = 0.8f }, seed);
                    Modal(b, rng, 0f, rng.Range(220f, 380f), new[] { 1f, 2.4f, 3.9f, 5.6f, 7.3f }, 0.35f, 0.35f);
                    Thump(b, rng, 0f, 120f, 60f, 0.05f, 0.12f, 0.5f);
                    b.ApplyReverb(0.15f, 0.5f, 0.5f);
                    break;
                }
                case Sfx.ExplosionSmall:
                    b = Explosion(sr, rng, seed, 1f, variant);
                    break;
                case Sfx.ExplosionLarge:
                    b = Explosion(sr, rng, seed, 1.9f, variant);
                    break;
                case Sfx.StarbaseDown:
                {
                    b = Explosion(sr, rng, seed, 2.2f, 0, 4.8f);
                    Tone(b, new ToneLayer { Start = 0.15f, Env = new Adsr(0.01f, 0.1f, 0.7f, 0.9f, 0.5f), Wave = Wave.Saw, Unison = 2, DetuneCents = 10f,
                        Freq = Glide(800f, 60f, 1.4f), Gain = 0.18f, Filter = SvfMode.LowPass, Cutoff = Glide(3000f, 300f, 1.4f), Q = 1.5f }, rng);
                    break;
                }
                case Sfx.BattleStart:
                {
                    b = new Stereo(3.4f, sr);
                    Brass(b, rng, 0f, new float[] { 45, 52 }, 0.35f, 0.32f, 1600f);
                    Brass(b, rng, 0.55f, new float[] { 52, 57, 64 }, 0.7f, 0.34f, 2000f);
                    for (int i = 0; i < 2; i++)
                    {
                        float st = i * 0.55f;
                        Thump(b, rng, st, 70f, 40f, 0.15f, 0.6f, 0.9f);
                        NoiseL(b, new NoiseLayer { Start = st, Env = Adsr.Pluck(0.001f, 0.2f, 5f), Gain = 0.3f, Filter = SvfMode.BandPass,
                            Cutoff = Const(1800f), Q = 0.9f }, seed + i);
                    }
                    b.ApplyReverb(0.42f, 0.85f, 0.45f);
                    break;
                }
                case Sfx.BattleVictory:
                {
                    b = new Stereo(4.2f, sr);
                    Brass(b, rng, 0.00f, new float[] { 60, 64 }, 0.08f, 0.26f);
                    Brass(b, rng, 0.18f, new float[] { 64, 67 }, 0.08f, 0.26f);
                    Brass(b, rng, 0.36f, new float[] { 60, 67, 72, 76 }, 1.2f, 0.36f, 2400f);
                    Thump(b, rng, 0.36f, 80f, 45f, 0.15f, 0.7f, 0.8f);
                    Bell(b, rng, 0.4f, 84, 1.6f, 0.15f, -0.3f);
                    Bell(b, rng, 0.48f, 88, 1.6f, 0.12f, 0.3f);
                    b.ApplyReverb(0.45f, 0.88f, 0.45f);
                    break;
                }
                case Sfx.BattleDefeat:
                {
                    b = new Stereo(4.4f, sr);
                    Brass(b, rng, 0.0f, new float[] { 57, 60, 64 }, 0.35f, 0.28f, 1200f, 220f);
                    Brass(b, rng, 0.6f, new float[] { 53, 57, 60 }, 0.35f, 0.28f, 1000f, 200f);
                    Brass(b, rng, 1.2f, new float[] { 50, 53, 57 }, 1.0f, 0.3f, 800f, 180f);
                    Thump(b, rng, 1.2f, 60f, 32f, 0.2f, 1.0f, 0.8f);
                    b.ApplyReverb(0.48f, 0.9f, 0.5f);
                    break;
                }
                case Sfx.Disengage:
                {
                    b = new Stereo(1.4f, sr);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(0.01f, 0.05f, 0.6f, 0.2f, 0.3f), Gain = 0.5f, Filter = SvfMode.BandPass,
                        Cutoff = Glide(500f, 5000f, 0.45f), Q = 1.6f, Width = 0.8f, Amp = Stutter(22f, 0.6f) }, seed);
                    Tone(b, new ToneLayer { Env = new Adsr(0.01f, 0.05f, 0.6f, 0.2f, 0.3f), Wave = Wave.Saw, Unison = 3, DetuneCents = 25f,
                        Freq = Glide(120f, 1100f, 0.5f), Gain = 0.25f, Filter = SvfMode.LowPass, Cutoff = Glide(600f, 6000f, 0.5f), Q = 1.4f }, rng);
                    b.ApplyReverb(0.28f, 0.8f, 0.5f);
                    break;
                }
                case Sfx.FtlFail:
                {
                    b = new Stereo(1.2f, sr);
                    Tone(b, new ToneLayer { Env = new Adsr(0.005f, 0.05f, 0.7f, 0.25f, 0.15f), Wave = Wave.Saw, Unison = 2, DetuneCents = 18f,
                        Freq = Glide(400f, 50f, 0.4f), Gain = 0.35f, Filter = SvfMode.LowPass, Cutoff = Glide(2500f, 300f, 0.4f), Q = 1.8f,
                        Amp = Stutter(18f, 0.5f) }, rng);
                    Crackle(b, rng, 0.02f, 0.4f, 120f, 0.2f, 2500f);
                    b.ApplyReverb(0.2f, 0.6f, 0.5f);
                    break;
                }

                // ───────────────────────────── СТРАТЕГИЯ ─────────────────────────────
                case Sfx.WarDeclared:
                {
                    b = new Stereo(6.0f, sr);
                    for (int i = 0; i < 2; i++)
                    {
                        float st = i * 0.45f, g = i == 0 ? 1f : 0.7f;
                        Thump(b, rng, st, 55f, 30f, 0.3f, 1.4f, g);
                        NoiseL(b, new NoiseLayer { Start = st, Env = Adsr.Pluck(0.004f, 0.8f, 4f), Color = NoiseColor.Brown, Gain = 0.6f * g,
                            Filter = SvfMode.LowPass, Cutoff = Const(300f) }, seed + i);
                    }
                    var env = new Adsr(0.6f, 0.6f, 0.7f, 1.2f, 1.6f, 3f);
                    foreach (float m in new float[] { 33, 34, 40 })
                        Tone(b, new ToneLayer { Env = env, Wave = Wave.Saw, Unison = 5, DetuneCents = 25f, Spread = 0.9f, Freq = Const(Note.Hz(m)),
                            Gain = 0.18f, Filter = SvfMode.LowPass, Cutoff = t => 350f + 250f * env.Value(t), Q = 1.2f, Drive = 0.4f }, rng);
                    Modal(b, rng, 0.02f, 610f, new[] { 1f, 1.38f, 2.13f, 2.97f }, 1.5f, 0.1f);
                    b.ApplyReverb(0.55f, 0.95f, 0.4f);
                    break;
                }
                case Sfx.PeaceSigned:
                {
                    b = new Stereo(5.0f, sr);
                    Pad(b, rng, 0f, new float[] { 53, 60, 65, 69, 72 }, new Adsr(0.6f, 0.6f, 0.7f, 0.8f, 2.0f, 3f), 0.2f, 1500f);
                    float[] arp = { 77, 81, 84, 89 };
                    for (int i = 0; i < arp.Length; i++) Bell(b, rng, 0.3f + i * 0.25f, arp[i], 1.5f, 0.2f, -0.3f + i * 0.2f, 2f, 1f);
                    b.ApplyReverb(0.5f, 0.9f, 0.45f);
                    break;
                }
                case Sfx.PactSigned:
                {
                    b = new Stereo(3.4f, sr);
                    Pad(b, rng, 0f, new float[] { 60, 67, 72 }, new Adsr(0.3f, 0.4f, 0.6f, 0.4f, 1.4f, 3f), 0.18f, 1600f);
                    Bell(b, rng, 0.1f, 72, 1.6f, 0.2f, -0.3f);
                    Bell(b, rng, 0.1f, 79, 1.6f, 0.18f, 0f);
                    Bell(b, rng, 0.1f, 84, 1.6f, 0.16f, 0.3f);
                    b.ApplyReverb(0.42f, 0.85f, 0.45f);
                    break;
                }
                case Sfx.SiegeStart:
                {
                    b = new Stereo(1.2f, sr);
                    for (int i = 0; i < 3; i++)
                    {
                        float st = i * 0.14f;
                        Tone(b, new ToneLayer { Start = st, Env = new Adsr(0.005f, 0.02f, 0.8f, 0.06f, 0.03f), Wave = Wave.Square, Freq = Const(880f),
                            Gain = 0.3f, Filter = SvfMode.LowPass, Cutoff = Const(3000f) }, rng);
                        Tone(b, new ToneLayer { Start = st, Env = new Adsr(0.005f, 0.02f, 0.8f, 0.06f, 0.05f), Freq = Const(440f), Gain = 0.25f }, rng);
                    }
                    Thump(b, rng, 0f, 80f, 45f, 0.1f, 0.4f, 0.5f);
                    b.ApplyReverb(0.22f, 0.65f, 0.5f);
                    break;
                }
                case Sfx.SystemCaptured:
                {
                    b = new Stereo(3.0f, sr);
                    Brass(b, rng, 0.00f, new float[] { 60 }, 0.05f, 0.28f);
                    Brass(b, rng, 0.14f, new float[] { 67 }, 0.05f, 0.28f);
                    Brass(b, rng, 0.28f, new float[] { 60, 64, 72 }, 0.7f, 0.34f, 2200f);
                    Thump(b, rng, 0.28f, 80f, 45f, 0.12f, 0.5f, 0.6f);
                    b.ApplyReverb(0.4f, 0.85f, 0.45f);
                    break;
                }
                case Sfx.SystemLost:
                {
                    b = new Stereo(3.4f, sr);
                    Brass(b, rng, 0.0f, new float[] { 57, 64 }, 0.2f, 0.28f, 1000f, 200f);
                    Brass(b, rng, 0.4f, new float[] { 53, 60 }, 0.2f, 0.28f, 900f, 200f);
                    Brass(b, rng, 0.8f, new float[] { 50, 57 }, 0.8f, 0.3f, 700f, 180f);
                    Thump(b, rng, 0.8f, 60f, 32f, 0.2f, 0.9f, 0.7f);
                    b.ApplyReverb(0.45f, 0.88f, 0.5f);
                    break;
                }
                case Sfx.GameVictory:
                {
                    b = new Stereo(7.0f, sr);
                    Brass(b, rng, 0.0f, new float[] { 48, 55, 60, 64 }, 0.6f, 0.3f, 1800f);
                    Brass(b, rng, 0.9f, new float[] { 53, 57, 60, 65 }, 0.6f, 0.3f, 2000f);
                    Brass(b, rng, 1.8f, new float[] { 48, 55, 60, 64, 67, 72 }, 2.2f, 0.38f, 2600f);
                    for (int i = 0; i < 8; i++) Thump(b, rng, 1.2f + i * 0.075f, 75f, 50f, 0.05f, 0.25f, 0.25f + i * 0.06f);
                    Thump(b, rng, 1.8f, 70f, 38f, 0.2f, 1.2f, 1f);
                    float[] arp = { 79, 84, 88, 91, 96 };
                    for (int i = 0; i < arp.Length; i++) Bell(b, rng, 1.9f + i * 0.09f, arp[i], 2f, 0.14f, -0.4f + i * 0.2f);
                    b.ApplyReverb(0.5f, 0.92f, 0.45f);
                    break;
                }
                case Sfx.GameDefeat:
                {
                    b = new Stereo(7.0f, sr);
                    var env = new Adsr(1.0f, 0.8f, 0.7f, 2.0f, 2.2f, 3f);
                    foreach (float m in new float[] { 33, 40, 45, 48 })
                        Tone(b, new ToneLayer { Env = env, Wave = Wave.Saw, Unison = 4, DetuneCents = 20f, Freq = Glide(Note.Hz(m), Note.Hz(m - 2), 5f),
                            Gain = 0.16f, Filter = SvfMode.LowPass, Cutoff = Glide(900f, 200f, 5f), Q = 1.1f }, rng);
                    Thump(b, rng, 0f, 50f, 28f, 0.4f, 2.0f, 1f);
                    Bell(b, rng, 0.6f, 69, 2.5f, 0.12f, -0.3f, 1.41f, 1.5f);
                    Bell(b, rng, 1.5f, 63, 2.5f, 0.12f, 0.3f, 1.41f, 1.5f);
                    b.ApplyReverb(0.55f, 0.95f, 0.45f);
                    break;
                }
                case Sfx.GameStart:
                {
                    b = new Stereo(5.0f, sr);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(1.4f, 0.05f, 0.4f, 0f, 0.15f, 2f), Gain = 0.5f, Filter = SvfMode.BandPass,
                        Cutoff = Glide(300f, 7000f, 1.45f), Q = 1.5f, Width = 0.9f }, seed);
                    Tone(b, new ToneLayer { Env = new Adsr(1.4f, 0.05f, 0.5f, 0f, 0.15f, 2f), Wave = Wave.Saw, Unison = 5, DetuneCents = 30f, Spread = 0.9f,
                        Freq = Glide(55f, 440f, 1.45f), Gain = 0.25f, Filter = SvfMode.LowPass, Cutoff = Glide(300f, 5000f, 1.45f), Q = 1.3f }, rng);
                    Thump(b, rng, 1.5f, 70f, 30f, 0.3f, 1.6f, 1f);
                    NoiseL(b, new NoiseLayer { Start = 1.5f, Env = new Adsr(0.005f, 0.3f, 0.4f, 0.2f, 1.4f), Gain = 0.5f, Filter = SvfMode.LowPass,
                        Cutoff = Glide(6000f, 200f, 1.5f), Width = 0.9f }, seed + 1);
                    Pad(b, rng, 1.5f, new float[] { 45, 52, 57, 64, 69 }, new Adsr(0.05f, 0.6f, 0.6f, 0.6f, 1.8f, 3f), 0.25f, 2500f, 600f);
                    b.ApplyReverb(0.5f, 0.92f, 0.45f);
                    break;
                }
                default:
                    b = new Stereo(0.2f, sr);
                    Tone(b, new ToneLayer { Env = Adsr.Pluck(0.002f, 0.08f), Freq = Const(1000f), Gain = 0.3f }, rng);
                    break;
            }

            return b.Finish(-1f);
        }

        /// <summary>
        /// Взрыв: треск → огненный шум с закрывающимся фильтром → саб-удар → рокот → разлёт обломков.
        /// size 1 — корвет, 2 — эсминец/база.
        /// </summary>
        private static Stereo Explosion(int sr, Noise rng, int seed, float size, int variant, float seconds = -1f)
        {
            var b = new Stereo(seconds > 0f ? seconds : 1.6f + size * 1.4f, sr);
            float body = 0.55f * size + 0.15f;
            NoiseL(b, new NoiseLayer { Env = Adsr.Pluck(0.0008f, 0.03f, 6f), Color = NoiseColor.White, Gain = 0.5f,
                Filter = SvfMode.HighPass, Cutoff = Const(1500f), Width = 0.4f }, seed);
            NoiseL(b, new NoiseLayer { Env = new Adsr(0.003f, 0.15f * size, 0.5f, 0.1f * size, body, 4f), Gain = 0.9f, Filter = SvfMode.LowPass,
                Cutoff = Glide(5000f + variant * 600f, 250f, body), Q = 0.9f, Width = 0.6f }, seed + 1);
            Thump(b, rng, 0f, 80f / Math.Max(1f, size * 0.7f), 26f, 0.25f * size, 0.5f * size + 0.1f, 1f);
            NoiseL(b, new NoiseLayer { Env = new Adsr(0.01f, 0.2f * size, 0.6f, 0.2f * size, 0.8f * size, 4f), Color = NoiseColor.Brown, Gain = 0.8f,
                Filter = SvfMode.LowPass, Cutoff = Const(220f) }, seed + 2);
            Crackle(b, rng, 0.05f, 0.9f * size, 60f, 0.25f, 3000f);
            if (size > 1.5f)
            {
                // Вторичные детонации
                Thump(b, rng, 0.25f + variant * 0.05f, 65f, 30f, 0.2f, 0.6f, 0.6f);
                NoiseL(b, new NoiseLayer { Start = 0.25f, Env = new Adsr(0.003f, 0.1f, 0.4f, 0.05f, 0.5f), Gain = 0.5f, Filter = SvfMode.LowPass,
                    Cutoff = Glide(3500f, 300f, 0.6f), Width = 0.8f, Pan = -0.3f }, seed + 3);
                Thump(b, rng, 0.55f, 55f, 28f, 0.2f, 0.7f, 0.5f);
                NoiseL(b, new NoiseLayer { Start = 0.55f, Env = new Adsr(0.003f, 0.1f, 0.4f, 0.05f, 0.6f), Gain = 0.45f, Filter = SvfMode.LowPass,
                    Cutoff = Glide(3000f, 250f, 0.7f), Width = 0.8f, Pan = 0.3f }, seed + 4);
            }
            b.Saturate(0.8f);
            b.ApplyReverb(size > 1.5f ? 0.42f : 0.28f, size > 1.5f ? 0.9f : 0.75f, 0.5f);
            return b;
        }
    }
}
