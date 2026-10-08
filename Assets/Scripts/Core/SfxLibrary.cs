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

            // Интерфейс — тихо и мягко: премиальный UI слышен краем уха, а не «в лоб»
            S(Sfx.UiClick, 0.30f, 3, 0.045f, 0.03f, 3);
            S(Sfx.UiHover, 0.09f, 2, 0.06f, 0.03f, 2, 0);
            S(Sfx.UiConfirm, 0.30f, 2, 0.06f, 0.01f);
            S(Sfx.UiBack, 0.26f, 2, 0.06f, 0.01f);
            S(Sfx.UiDenied, 0.32f, 1, 0.15f, 0f);
            S(Sfx.UiTab, 0.22f, 2, 0.05f, 0.03f, 2);
            S(Sfx.WindowOpen, 0.24f, 2, 0.12f, 0.01f);
            S(Sfx.WindowClose, 0.20f, 2, 0.12f, 0.01f);

            S(Sfx.NotifyInfo, 0.26f, 2, 0.30f, 0f, 1, 1);
            S(Sfx.NotifySuccess, 0.32f, 2, 0.35f, 0f, 1, 2);
            S(Sfx.NotifyWarning, 0.30f, 1, 0.50f, 0f, 1, 2);
            S(Sfx.NotifyDanger, 0.36f, 1, 0.80f, 0f, 1, 2, SfxBus.Ui, 0.3f);

            S(Sfx.TimePause, 0.24f, 1, 0.10f, 0f);
            S(Sfx.TimeResume, 0.24f, 1, 0.10f, 0f);
            S(Sfx.TimeSpeed, 0.20f, 1, 0.06f, 0f);

            S(Sfx.SystemSelect, 0.22f, 2, 0.08f, 0.01f);
            S(Sfx.SystemEnter, 0.34f, 1, 0.40f, 0f);
            S(Sfx.SystemExit, 0.30f, 1, 0.40f, 0f);
            S(Sfx.SelectMilitary, 0.28f, 1, 0.10f, 0.01f);
            S(Sfx.SelectScience, 0.28f, 1, 0.10f, 0.01f);
            S(Sfx.SelectConstructor, 0.28f, 1, 0.10f, 0.01f);
            S(Sfx.OrderMove, 0.26f, 2, 0.07f, 0.01f);
            S(Sfx.OrderQueue, 0.20f, 2, 0.05f, 0.01f);
            S(Sfx.OrderStop, 0.24f, 1, 0.08f, 0.01f);
            S(Sfx.FtlJump, 0.26f, 3, 0.18f, 0.04f, 2, 1, SfxBus.World);
            S(Sfx.FtlArrive, 0.20f, 3, 0.18f, 0.04f, 2, 1, SfxBus.World);
            S(Sfx.SurveyComplete, 0.30f, 1, 0.30f, 0f, 1, 2);
            S(Sfx.OutpostBuilt, 0.36f, 1, 0.40f, 0f, 1, 2, SfxBus.Stinger, 0.25f);
            S(Sfx.Anomaly, 0.40f, 1, 1.00f, 0f, 1, 2, SfxBus.Stinger, 0.5f);

            S(Sfx.BuildQueued, 0.24f, 2, 0.08f, 0.02f, 2);
            S(Sfx.BuildComplete, 0.30f, 2, 0.25f, 0f, 1, 2);
            S(Sfx.ShipLaunched, 0.34f, 1, 0.40f, 0f, 1, 2, SfxBus.Stinger, 0.25f);
            S(Sfx.ColonyFounded, 0.38f, 1, 1.00f, 0f, 1, 3, SfxBus.Stinger, 0.45f);
            S(Sfx.ResearchComplete, 0.40f, 1, 0.80f, 0f, 1, 3, SfxBus.Stinger, 0.45f);
            S(Sfx.ResearchStart, 0.22f, 1, 0.10f, 0f);

            S(Sfx.WeaponKinetic, 0.24f, 5, 0.05f, 0.05f, 3, 0, SfxBus.World);
            S(Sfx.WeaponEnergy, 0.2f, 5, 0.05f, 0.05f, 3, 0, SfxBus.World);
            S(Sfx.WeaponMissile, 0.2f, 4, 0.09f, 0.05f, 3, 0, SfxBus.World);
            S(Sfx.HitShield, 0.15f, 4, 0.06f, 0.05f, 3, 0, SfxBus.World);
            S(Sfx.HitArmor, 0.17f, 4, 0.06f, 0.05f, 3, 0, SfxBus.World);
            S(Sfx.ExplosionSmall, 0.40f, 4, 0.07f, 0.06f, 3, 2, SfxBus.World);
            S(Sfx.ExplosionLarge, 0.48f, 2, 0.25f, 0.04f, 2, 3, SfxBus.World);
            S(Sfx.StarbaseDown, 0.52f, 1, 1.00f, 0f, 1, 3, SfxBus.World, 0.4f);
            S(Sfx.BattleStart, 0.44f, 1, 2.50f, 0f, 1, 3, SfxBus.Stinger, 0.5f);
            S(Sfx.BattleVictory, 0.42f, 1, 2.00f, 0f, 1, 3, SfxBus.Stinger, 0.55f);
            S(Sfx.BattleDefeat, 0.40f, 1, 2.00f, 0f, 1, 3, SfxBus.Stinger, 0.55f);
            S(Sfx.Disengage, 0.26f, 2, 0.15f, 0.04f, 1, 1, SfxBus.World);
            S(Sfx.FtlFail, 0.26f, 2, 0.15f, 0.03f, 1, 1, SfxBus.World);

            S(Sfx.WarDeclared, 0.52f, 1, 2.00f, 0f, 1, 3, SfxBus.Stinger, 0.7f);
            S(Sfx.PeaceSigned, 0.42f, 1, 2.00f, 0f, 1, 3, SfxBus.Stinger, 0.6f);
            S(Sfx.PactSigned, 0.38f, 1, 1.50f, 0f, 1, 3, SfxBus.Stinger, 0.5f);
            S(Sfx.SiegeStart, 0.34f, 1, 1.00f, 0f, 1, 2, SfxBus.Stinger, 0.3f);
            S(Sfx.SystemCaptured, 0.40f, 1, 1.00f, 0f, 1, 3, SfxBus.Stinger, 0.5f);
            S(Sfx.SystemLost, 0.40f, 1, 1.00f, 0f, 1, 3, SfxBus.Stinger, 0.5f);
            S(Sfx.GameVictory, 0.50f, 1, 5.00f, 0f, 1, 3, SfxBus.Stinger, 0.85f);
            S(Sfx.GameDefeat, 0.48f, 1, 5.00f, 0f, 1, 3, SfxBus.Stinger, 0.85f);
            S(Sfx.GameStart, 0.46f, 1, 3.00f, 0f, 1, 3, SfxBus.Stinger, 0.6f);
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
            float pan = Math.Max(-1f, Math.Min(1f, s.Pan));
            float panL = pan > 0f ? 1f - pan : 1f, panR = pan < 0f ? 1f + pan : 1f;
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
            float pan = Math.Max(-1f, Math.Min(1f, s.Pan));
            float panL = pan > 0f ? 1f - pan : 1f, panR = pan < 0f ? 1f + pan : 1f;

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
        //  «Премиальные» инструменты
        //  Вся тональная часть — в ре (лидийский лад для светлых событий, минор/фригийский для
        //  тревожных): звуки, наложившиеся друг на друга, звучат как один аккорд, а не как какофония.
        // ================================================================================

        /// <summary>
        /// Стеклянный тон: мягкая атака, два синуса с разницей 3 цента (медленное биение — «живой» хорус),
        /// тихие обертоны ×2, ×3, ×5,43, затухающие быстрее основного тона.
        /// </summary>
        private static void Glass(Stereo b, Noise rng, float start, float midi, float decay, float gain,
                                  float pan = 0f, float attack = 0.006f, float bright = 1f)
        {
            float hz = Note.Hz(midi);
            var env = Adsr.Pluck(attack, decay, 4f);
            Tone(b, new ToneLayer { Start = start, Env = env, Freq = _ => hz, Gain = gain * 0.7f, Pan = pan - 0.15f }, rng);
            Tone(b, new ToneLayer { Start = start, Env = env, Freq = _ => hz * 1.0017f, Gain = gain * 0.7f, Pan = pan + 0.15f }, rng);
            Tone(b, new ToneLayer { Start = start, Env = Adsr.Pluck(attack, decay * 0.55f, 4.5f), Freq = _ => hz * 2f, Gain = gain * 0.16f * bright, Pan = -pan }, rng);
            Tone(b, new ToneLayer { Start = start, Env = Adsr.Pluck(attack, decay * 0.3f, 5f), Freq = _ => hz * 3.01f, Gain = gain * 0.06f * bright, Pan = pan * 0.5f }, rng);
            Tone(b, new ToneLayer { Start = start, Env = Adsr.Pluck(attack * 0.6f, decay * 0.12f, 6f), Freq = _ => hz * 5.43f, Gain = gain * 0.025f * bright }, rng);
        }

        /// <summary>«Дыхание»: полосовой розовый шум со скольжением частоты — воздух, движение, перелёт.</summary>
        private static void Breath(Stereo b, int seed, float start, float dur, float fromHz, float toHz, float gain,
                                   float q = 0.9f, float attackFrac = 0.4f)
        {
            float a = Math.Max(0.004f, dur * attackFrac), r = Math.Max(0.01f, dur - a);
            NoiseL(b, new NoiseLayer { Start = start, Env = new Adsr(a, 0.001f, 1f, 0f, r, 3f), Gain = gain, Filter = SvfMode.BandPass,
                Cutoff = Glide(fromHz, toHz, dur), Q = q, Width = 0.9f }, seed + (int)(start * 1000f));
        }

        /// <summary>Суббас: чистый синус, при желании со скольжением высоты на всю длину огибающей.</summary>
        private static void Sub(Stereo b, Noise rng, float start, float hz, Adsr env, float gain, float toHz = -1f)
        {
            float len = env.Length;
            Tone(b, new ToneLayer { Start = start, Env = env, Freq = toHz > 0f ? Glide(hz, toHz, len) : Const(hz), Gain = gain }, rng);
        }

        /// <summary>
        /// Кинематографичный «браам»: 7 расстроенных пил на ноту, фильтр раскрывается вслед за атакой,
        /// насыщение, октава вниз меандром и саб. Голос войны, тревоги, триумфа.
        /// </summary>
        private static void Braam(Stereo b, Noise rng, float start, float[] midis, float hold, float gain,
                                  float open = 1600f, float attack = 0.12f, float release = 1.2f)
        {
            var env = new Adsr(attack, 0.35f, 0.75f, hold, release, 3f);
            var e = env;
            foreach (float m in midis)
            {
                float hz = Note.Hz(m);
                Tone(b, new ToneLayer { Start = start, Env = env, Wave = Wave.Saw, Unison = 7, DetuneCents = 22f, Spread = 0.95f,
                    Freq = _ => hz, Gain = gain / midis.Length * 1.8f, Filter = SvfMode.LowPass, Q = 1.3f, Drive = 0.8f,
                    Cutoff = t => 110f + open * e.Value(t) * (0.35f + 0.65f * Note.Clamp01(t / (attack + 0.25f))) }, rng);
            }
            float root = Note.Hz(midis[0]);
            Tone(b, new ToneLayer { Start = start, Env = env, Wave = Wave.Square, Freq = _ => root * 0.5f, Gain = gain * 0.35f,
                Filter = SvfMode.LowPass, Cutoff = _ => 260f, Q = 0.8f }, rng);
            Tone(b, new ToneLayer { Start = start, Env = env, Freq = _ => root * 0.5f, Gain = gain * 0.6f }, rng);
        }

        /// <summary>Мерцание: высокие синусы с медленным «дыханием» громкости и дрейфом высоты, разнесённые по стерео.</summary>
        private static void Shimmer(Stereo b, Noise rng, float start, float dur, float[] midis, float gain)
        {
            var env = new Adsr(dur * 0.35f, 0.2f, 0.8f, dur * 0.15f, dur * 0.5f, 3f);
            foreach (float m in midis)
            {
                float hz = Note.Hz(m);
                float ph1 = rng.Range(0f, 6.28f), ph2 = rng.Range(0f, 6.28f);
                float r1 = rng.Range(0.6f, 1.6f), r2 = rng.Range(2.5f, 5.5f);
                Tone(b, new ToneLayer { Start = start, Env = env, Freq = _ => hz, Gain = gain / midis.Length * 2f, Pan = rng.Range(-0.7f, 0.7f),
                    VibratoHz = rng.Range(0.15f, 0.4f), VibratoCents = 5f,
                    Amp = t => 0.55f + 0.3f * (float)Math.Sin(ph1 + t * r1 * 6.283f) + 0.15f * (float)Math.Sin(ph2 + t * r2 * 6.283f) }, rng);
            }
        }

        /// <summary>
        /// Реверс-реверберация: хвост стеклянного аккорда, проигранный задом наперёд, нарастает
        /// и «втягивается» точно в момент endTime — классический кинематографичный подвод к удару.
        /// </summary>
        private static void ReverseSwell(Stereo b, Noise rng, float endTime, float[] midis, float len, float gain, float room = 0.9f)
        {
            len = Math.Min(len, endTime);
            if (len < 0.05f) return;
            var tmp = new Stereo(len, b.Sr);
            foreach (float m in midis) Glass(tmp, rng, 0f, m, len * 0.5f, 1f / midis.Length, 0f, 0.003f, 0.6f);
            NoiseL(tmp, new NoiseLayer { Env = Adsr.Pluck(0.002f, len * 0.3f, 4f), Gain = 0.15f, Filter = SvfMode.BandPass,
                Cutoff = Const(2500f), Q = 0.8f, Width = 1f }, (int)(endTime * 1000f) + 7);
            tmp.ApplyReverb(1f, room, 0.4f, 0.15f, 0.01f, 200f);
            tmp.FadeTail(len * 0.45f);                       // после разворота станет плавным вступлением
            b.MixFrom(tmp, b.Index(endTime) - tmp.Length, gain, reverse: true);
        }

        // ================================================================================
        //  Рецепты
        // ================================================================================

        /// <summary>Синтезировать звук. Возвращает интерлив-стерео float[] (L,R,L,R…), нормализованный.</summary>
        public static float[] Render(Sfx id, int variant, int sr)
        {
            int seed = (int)id * 7919 + variant * 104729 + 17;
            var rng = new Noise(seed);
            Stereo b;
            float lp = 14000f;   // финальный ФНЧ: мягкий, «дорогой» верх без резкости

            switch (id)
            {
                // ───────────────────── ИНТЕРФЕЙС: тихо, мягко, тепло ─────────────────────
                case Sfx.UiClick:
                {
                    b = new Stereo(0.35f, sr);
                    float bp = 1600f + variant * 180f;
                    NoiseL(b, new NoiseLayer { Env = Adsr.Pluck(0.0015f, 0.022f, 5f), Gain = 0.55f, Filter = SvfMode.BandPass,
                        Cutoff = Const(bp), Q = 0.9f, Width = 0.25f }, seed);
                    Tone(b, new ToneLayer { Env = Adsr.Pluck(0.002f, 0.05f, 5f), Freq = Glide(420f, 290f, 0.03f), Gain = 0.35f }, rng);
                    Tone(b, new ToneLayer { Env = Adsr.Pluck(0.0015f, 0.014f, 6f), Freq = Const(2350f + variant * 60f), Gain = 0.07f }, rng);
                    b.ApplyReverb(0.07f, 0.4f, 0.7f, 1f, 0.012f, 400f);
                    lp = 9000f;
                    break;
                }
                case Sfx.UiHover:
                {
                    b = new Stereo(0.3f, sr);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(0.006f, 0.001f, 1f, 0f, 0.035f, 4f), Gain = 0.5f, Filter = SvfMode.BandPass,
                        Cutoff = Const(2600f + variant * 250f), Q = 1.4f, Width = 0.5f }, seed);
                    Tone(b, new ToneLayer { Env = Adsr.Pluck(0.004f, 0.06f, 4f), Freq = Const(Note.Hz(86 + variant * 2)), Gain = 0.1f }, rng);
                    b.ApplyReverb(0.06f, 0.5f, 0.6f, 1f, 0.01f, 600f);
                    lp = 9000f;
                    break;
                }
                case Sfx.UiConfirm:
                {
                    b = new Stereo(1.6f, sr);
                    Glass(b, rng, 0.000f, 74, 0.55f, 0.42f, -0.2f, 0.004f);
                    Glass(b, rng, 0.055f, 81, 0.65f, 0.34f, 0.2f, 0.004f);
                    Breath(b, seed, 0f, 0.12f, 900f, 2600f, 0.12f);
                    b.ApplyPingPong(0.16f, 0.2f, 0.1f, 3500f);
                    b.ApplyReverb(0.2f, 0.75f, 0.5f, 1f, 0.025f, 300f);
                    lp = 10000f;
                    break;
                }
                case Sfx.UiBack:
                {
                    b = new Stereo(1.3f, sr);
                    Glass(b, rng, 0.000f, 76, 0.4f, 0.36f, 0.15f, 0.004f, 0.7f);
                    Glass(b, rng, 0.050f, 69, 0.5f, 0.36f, -0.15f, 0.004f, 0.7f);
                    b.ApplyReverb(0.16f, 0.7f, 0.55f, 1f, 0.02f, 300f);
                    lp = 9000f;
                    break;
                }
                case Sfx.UiDenied:
                {
                    b = new Stereo(0.8f, sr);
                    for (int i = 0; i < 2; i++)
                    {
                        float st = i * 0.1f, f = i == 0 ? 98f : 92.5f;
                        Tone(b, new ToneLayer { Start = st, Env = Adsr.Pluck(0.004f, 0.12f, 4f), Freq = Glide(f * 1.12f, f, 0.03f), Gain = 0.6f }, rng);
                        Tone(b, new ToneLayer { Start = st, Env = Adsr.Pluck(0.004f, 0.09f, 4f), Wave = Wave.Triangle, Freq = Const(f * 2f), Gain = 0.25f,
                            Filter = SvfMode.LowPass, Cutoff = Const(600f) }, rng);
                        NoiseL(b, new NoiseLayer { Start = st, Env = Adsr.Pluck(0.002f, 0.03f, 5f), Gain = 0.2f, Filter = SvfMode.BandPass,
                            Cutoff = Const(420f), Q = 1.2f }, seed + i);
                    }
                    b.ApplyReverb(0.1f, 0.5f, 0.6f, 1f, 0.015f, 200f);
                    lp = 6000f;
                    break;
                }
                case Sfx.UiTab:
                {
                    b = new Stereo(0.5f, sr);
                    Breath(b, seed, 0f, 0.1f, 900f, 2400f + variant * 200f, 0.4f, 1.1f, 0.5f);
                    Tone(b, new ToneLayer { Start = 0.015f, Env = Adsr.Pluck(0.002f, 0.045f, 5f), Freq = Glide(360f, 260f, 0.03f), Gain = 0.25f }, rng);
                    b.ApplyReverb(0.1f, 0.55f, 0.6f, 1f, 0.015f, 400f);
                    lp = 9000f;
                    break;
                }
                case Sfx.WindowOpen:
                {
                    b = new Stereo(2.0f, sr);
                    Breath(b, seed, 0f, 0.45f, 250f, 2200f, 0.5f, 0.9f, 0.55f);
                    Sub(b, rng, 0f, 73.4f, new Adsr(0.12f, 0.15f, 0.6f, 0.05f, 0.35f, 3f), 0.3f);
                    Glass(b, rng, 0.16f, 86, 1.1f, 0.09f, -0.35f, 0.012f);
                    Glass(b, rng, 0.20f, 93, 1.0f, 0.05f, 0.35f, 0.012f);
                    b.ApplyReverb(0.28f, 0.85f, 0.5f, 1f, 0.03f, 250f);
                    lp = 11000f;
                    break;
                }
                case Sfx.WindowClose:
                {
                    b = new Stereo(1.4f, sr);
                    Breath(b, seed, 0f, 0.3f, 2000f, 300f, 0.45f, 0.9f, 0.2f);
                    Sub(b, rng, 0f, 110f, new Adsr(0.01f, 0.08f, 0.4f, 0f, 0.2f), 0.22f, 55f);
                    b.ApplyReverb(0.2f, 0.75f, 0.55f, 1f, 0.02f, 250f);
                    lp = 10000f;
                    break;
                }

                // ───────────────────── УВЕДОМЛЕНИЯ ─────────────────────
                case Sfx.NotifyInfo:
                {
                    b = new Stereo(2.6f, sr);
                    Glass(b, rng, 0.00f, 78, 1.1f, 0.32f, -0.25f, 0.008f);
                    Glass(b, rng, 0.07f, 85, 1.2f, 0.24f, 0.25f, 0.008f);
                    foreach (float m in new float[] { 50, 57 })
                        Tone(b, new ToneLayer { Env = new Adsr(0.05f, 0.2f, 0.4f, 0.2f, 0.8f, 3f), Freq = Const(Note.Hz(m)), Gain = 0.08f }, rng);
                    b.ApplyPingPong(0.21f, 0.28f, 0.12f, 3000f);
                    b.ApplyReverb(0.3f, 0.86f, 0.5f, 1f, 0.03f, 300f);
                    lp = 10000f;
                    break;
                }
                case Sfx.NotifySuccess:
                {
                    b = new Stereo(3.8f, sr);
                    ReverseSwell(b, rng, 0.35f, new float[] { 74, 81 }, 0.35f, 0.5f);
                    float[] ch = { 74, 78, 81, 88 };
                    for (int i = 0; i < ch.Length; i++) Glass(b, rng, 0.35f + i * 0.03f, ch[i], 1.6f, 0.22f, -0.3f + i * 0.2f, 0.006f);
                    Pad(b, rng, 0.3f, new float[] { 50, 57, 62, 66, 69 }, new Adsr(0.08f, 0.4f, 0.5f, 0.3f, 1.2f, 3f), 0.16f, 1400f);
                    Shimmer(b, rng, 0.4f, 2.2f, new float[] { 93, 97, 100 }, 0.05f);
                    b.ApplyReverb(0.38f, 0.9f, 0.45f, 1f, 0.03f, 250f);
                    lp = 11000f;
                    break;
                }
                case Sfx.NotifyWarning:
                {
                    b = new Stereo(2.8f, sr);
                    var env = new Adsr(0.04f, 0.1f, 0.7f, 0.45f, 0.5f, 3f);
                    foreach (float m in new float[] { 50, 56 })
                        Tone(b, new ToneLayer { Env = env, Wave = Wave.Saw, Unison = 3, DetuneCents = 12f, Freq = Const(Note.Hz(m)), Gain = 0.3f,
                            Filter = SvfMode.LowPass, Cutoff = Const(700f), Q = 1.4f, Amp = t => 0.6f + 0.4f * (float)Math.Cos(t * 6.283f * 4.5f) }, rng);
                    Sub(b, rng, 0f, 73.4f, env, 0.3f);
                    Glass(b, rng, 0.0f, 80, 0.9f, 0.1f, 0.3f, 0.01f, 0.6f);
                    b.ApplyReverb(0.26f, 0.8f, 0.5f, 1f, 0.025f, 250f);
                    lp = 8000f;
                    break;
                }
                case Sfx.NotifyDanger:
                {
                    b = new Stereo(3.4f, sr);
                    Braam(b, rng, 0f, new float[] { 38, 45, 51 }, 0.4f, 0.5f, 1400f, 0.06f, 1.0f);
                    Thump(b, rng, 0f, 55f, 32f, 0.25f, 0.9f, 0.6f);
                    b.ApplyReverb(0.35f, 0.88f, 0.5f, 1f, 0.03f, 200f);
                    lp = 9000f;
                    break;
                }

                // ───────────────────── ВРЕМЯ ─────────────────────
                case Sfx.TimePause:
                {
                    b = new Stereo(1.0f, sr);
                    var env = new Adsr(0.004f, 0.05f, 0.8f, 0.12f, 0.2f);
                    Tone(b, new ToneLayer { Env = env, Wave = Wave.Triangle, Freq = Glide(196f, 55f, 0.32f), Gain = 0.4f,
                        Filter = SvfMode.LowPass, Cutoff = Glide(1200f, 150f, 0.32f) }, rng);
                    Tone(b, new ToneLayer { Env = env, Freq = Glide(98f, 28f, 0.32f), Gain = 0.35f }, rng);
                    Breath(b, seed, 0f, 0.3f, 1500f, 250f, 0.15f, 0.8f, 0.1f);
                    b.ApplyReverb(0.14f, 0.6f, 0.6f, 1f, 0.02f, 200f);
                    lp = 7000f;
                    break;
                }
                case Sfx.TimeResume:
                {
                    b = new Stereo(1.2f, sr);
                    var env = new Adsr(0.04f, 0.05f, 0.8f, 0.1f, 0.18f);
                    Tone(b, new ToneLayer { Env = env, Wave = Wave.Triangle, Freq = Glide(70f, 196f, 0.22f), Gain = 0.38f,
                        Filter = SvfMode.LowPass, Cutoff = Glide(200f, 1500f, 0.22f) }, rng);
                    Tone(b, new ToneLayer { Env = env, Freq = Glide(35f, 98f, 0.22f), Gain = 0.3f }, rng);
                    Glass(b, rng, 0.2f, 74, 0.5f, 0.08f, 0f, 0.01f, 0.6f);
                    b.ApplyReverb(0.14f, 0.6f, 0.6f, 1f, 0.02f, 200f);
                    lp = 8000f;
                    break;
                }
                case Sfx.TimeSpeed:
                {
                    b = new Stereo(0.9f, sr);
                    Glass(b, rng, 0f, 81, 0.35f, 0.4f, 0f, 0.004f, 0.7f);
                    Breath(b, seed, 0f, 0.08f, 1200f, 2500f, 0.12f);
                    b.ApplyReverb(0.14f, 0.65f, 0.55f, 1f, 0.02f, 300f);
                    lp = 9000f;
                    break;
                }

                // ───────────────────── КАРТА ─────────────────────
                case Sfx.SystemSelect:
                {
                    b = new Stereo(2.8f, sr);
                    Tone(b, new ToneLayer { Env = Adsr.Pluck(0.006f, 1.4f, 3.5f), Freq = Glide(600f, 587.3f, 0.15f), Gain = 0.45f, Pan = -0.2f }, rng);
                    Tone(b, new ToneLayer { Env = Adsr.Pluck(0.006f, 1.3f, 3.5f), Freq = Const(588.8f), Gain = 0.25f, Pan = 0.4f }, rng);
                    Tone(b, new ToneLayer { Env = Adsr.Pluck(0.004f, 0.4f, 4f), Freq = Const(1174.7f), Gain = 0.07f }, rng);
                    Thump(b, rng, 0f, 90f, 60f, 0.05f, 0.15f, 0.12f);
                    b.ApplyPingPong(0.27f, 0.42f, 0.2f, 2500f);
                    b.ApplyReverb(0.32f, 0.92f, 0.5f, 1f, 0.03f, 300f);
                    lp = 8000f;
                    break;
                }
                case Sfx.SystemEnter:
                {
                    b = new Stereo(3.6f, sr);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(0.6f, 0.001f, 1f, 0f, 0.5f, 3f), Gain = 0.5f, Filter = SvfMode.LowPass,
                        Cutoff = Glide(300f, 5000f, 0.65f), Q = 1.1f, Width = 0.9f }, seed);
                    Sub(b, rng, 0f, 36.7f, new Adsr(0.5f, 0.3f, 0.6f, 0.1f, 0.9f, 3f), 0.45f);
                    foreach (float m in new float[] { 38, 45 })
                        Tone(b, new ToneLayer { Env = new Adsr(0.5f, 0.3f, 0.5f, 0.1f, 0.8f, 3f), Wave = Wave.Saw, Unison = 5, DetuneCents = 18f, Spread = 0.9f,
                            Freq = Const(Note.Hz(m)), Gain = 0.12f, Filter = SvfMode.LowPass, Cutoff = Glide(200f, 900f, 0.6f), Q = 1.1f }, rng);
                    Thump(b, rng, 0.62f, 70f, 40f, 0.15f, 0.5f, 0.4f);
                    Glass(b, rng, 0.62f, 74, 1.6f, 0.12f, -0.3f, 0.01f);
                    Glass(b, rng, 0.66f, 81, 1.6f, 0.09f, 0.3f, 0.01f);
                    b.ApplyReverb(0.38f, 0.92f, 0.5f, 1f, 0.03f, 200f);
                    lp = 10000f;
                    break;
                }
                case Sfx.SystemExit:
                {
                    b = new Stereo(2.6f, sr);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(0.04f, 0.1f, 0.6f, 0.1f, 0.45f), Gain = 0.5f, Filter = SvfMode.LowPass,
                        Cutoff = Glide(4000f, 250f, 0.55f), Q = 1.1f, Width = 0.9f }, seed);
                    Sub(b, rng, 0f, 80f, new Adsr(0.03f, 0.2f, 0.5f, 0.05f, 0.5f), 0.3f, 40f);
                    foreach (float m in new float[] { 45, 38 })
                        Tone(b, new ToneLayer { Env = new Adsr(0.05f, 0.2f, 0.5f, 0.05f, 0.5f), Wave = Wave.Saw, Unison = 5, DetuneCents = 18f,
                            Freq = Const(Note.Hz(m)), Gain = 0.1f, Filter = SvfMode.LowPass, Cutoff = Glide(800f, 200f, 0.6f) }, rng);
                    b.ApplyReverb(0.3f, 0.88f, 0.5f, 1f, 0.03f, 200f);
                    lp = 9000f;
                    break;
                }
                // Выбор корабля — «спутниковый» пинг: чистый тон с эхом. Тип корабля слышен по высоте:
                // военный ниже, научный выше, строитель посередине.
                case Sfx.SelectMilitary:
                    b = SatellitePing(sr, rng, 0.89f);
                    break;
                case Sfx.SelectScience:
                    b = SatellitePing(sr, rng, 1.12f);
                    break;
                case Sfx.SelectConstructor:
                    b = SatellitePing(sr, rng, 1f);
                    break;
                case Sfx.OrderMove:
                {
                    b = new Stereo(1.5f, sr);
                    Glass(b, rng, 0.00f, 69, 0.45f, 0.3f, -0.15f, 0.004f, 0.8f);
                    Glass(b, rng, 0.05f, 74, 0.55f, 0.3f, 0.15f, 0.004f, 0.8f);
                    Breath(b, seed, 0f, 0.15f, 600f, 1800f, 0.18f);
                    Thump(b, rng, 0f, 110f, 70f, 0.05f, 0.12f, 0.15f);
                    b.ApplyPingPong(0.12f, 0.2f, 0.1f, 3000f);
                    b.ApplyReverb(0.18f, 0.75f, 0.55f, 1f, 0.02f, 300f);
                    lp = 9000f;
                    break;
                }
                case Sfx.OrderQueue:
                {
                    b = new Stereo(1.0f, sr);
                    Glass(b, rng, 0.00f, 76, 0.3f, 0.3f, 0.2f, 0.004f, 0.7f);
                    Glass(b, rng, 0.04f, 78, 0.35f, 0.18f, 0.3f, 0.004f, 0.7f);
                    b.ApplyReverb(0.14f, 0.7f, 0.55f, 1f, 0.02f, 300f);
                    lp = 9000f;
                    break;
                }
                case Sfx.OrderStop:
                {
                    b = new Stereo(1.1f, sr);
                    Glass(b, rng, 0.00f, 74, 0.3f, 0.3f, 0f, 0.004f, 0.7f);
                    Glass(b, rng, 0.05f, 69, 0.4f, 0.3f, 0f, 0.004f, 0.7f);
                    Thump(b, rng, 0.05f, 100f, 55f, 0.06f, 0.15f, 0.35f);
                    b.ApplyReverb(0.12f, 0.65f, 0.55f, 1f, 0.02f, 250f);
                    lp = 8000f;
                    break;
                }
                case Sfx.FtlJump:
                {
                    b = new Stereo(3.2f, sr);
                    float c = 0.6f + variant * 0.06f;
                    var charge = new Adsr(c - 0.1f, 0.05f, 0.9f, 0.05f, 0.1f, 3f);
                    Tone(b, new ToneLayer { Env = charge, Wave = Wave.Saw, Unison = 5, DetuneCents = 25f, Spread = 0.9f, Freq = Glide(55f, 420f, c),
                        Gain = 0.26f, Filter = SvfMode.LowPass, Cutoff = Glide(200f, 3500f, c), Q = 1.2f }, rng);
                    Tone(b, new ToneLayer { Env = charge, Freq = Glide(36f, 110f, c), Gain = 0.3f }, rng);
                    Thump(b, rng, c, 90f, 30f, 0.3f, 0.6f, 0.8f);
                    NoiseL(b, new NoiseLayer { Start = c, Env = new Adsr(0.004f, 0.1f, 0.5f, 0.05f, 0.6f), Gain = 0.55f, Filter = SvfMode.LowPass,
                        Cutoff = Glide(6000f, 200f, 0.7f), Width = 0.9f }, seed);
                    Glass(b, rng, c + 0.02f, 86, 1.4f, 0.05f, 0.3f, 0.01f);
                    b.ApplyReverb(0.38f, 0.9f, 0.5f, 1f, 0.03f, 200f);
                    lp = 10000f;
                    break;
                }
                case Sfx.FtlArrive:
                {
                    b = new Stereo(2.0f, sr);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(0.01f, 0.05f, 0.4f, 0.05f, 0.4f), Gain = 0.45f, Filter = SvfMode.LowPass,
                        Cutoff = Glide(4000f, 300f, 0.4f), Width = 0.9f }, seed);
                    Thump(b, rng, 0f, 80f, 38f, 0.15f, 0.4f, 0.5f);
                    b.ApplyReverb(0.3f, 0.85f, 0.5f, 1f, 0.03f, 200f);
                    lp = 8000f;
                    break;
                }
                case Sfx.SurveyComplete:
                {
                    b = new Stereo(3.6f, sr);
                    float[] pent = { 86, 88, 90, 93, 95 };
                    for (int i = 0; i < 10; i++)
                        Glass(b, rng, i * 0.032f, pent[(int)(rng.Value01() * 4.99f)], 0.14f, 0.07f, rng.Range(-0.6f, 0.6f), 0.002f, 0.5f);
                    float t0 = 0.36f;
                    Glass(b, rng, t0, 74, 1.8f, 0.24f, -0.25f, 0.008f);
                    Glass(b, rng, t0 + 0.03f, 81, 1.8f, 0.2f, 0.25f, 0.008f);
                    Glass(b, rng, t0 + 0.06f, 88, 1.6f, 0.12f, 0f, 0.008f);
                    Pad(b, rng, t0, new float[] { 50, 57, 64 }, new Adsr(0.1f, 0.4f, 0.5f, 0.3f, 1.2f, 3f), 0.12f, 1200f);
                    b.ApplyPingPong(0.19f, 0.3f, 0.1f, 3500f);
                    b.ApplyReverb(0.35f, 0.9f, 0.5f, 1f, 0.03f, 250f);
                    lp = 11000f;
                    break;
                }
                case Sfx.OutpostBuilt:
                {
                    b = new Stereo(4.4f, sr);
                    Thump(b, rng, 0f, 60f, 35f, 0.2f, 0.7f, 0.8f);
                    Modal(b, rng, 0f, 110f, new[] { 1f, 2.76f, 5.4f }, 1.2f, 0.16f);
                    NoiseL(b, new NoiseLayer { Start = 0.03f, Env = new Adsr(0.01f, 0.1f, 0.4f, 0.1f, 0.4f), Gain = 0.12f, Filter = SvfMode.BandPass,
                        Cutoff = Const(3000f), Q = 1.2f, Width = 0.9f }, seed);
                    Pad(b, rng, 0.05f, new float[] { 50, 57, 62, 64, 69 }, new Adsr(0.3f, 0.4f, 0.6f, 0.5f, 1.4f, 3f), 0.2f, 300f, 1600f);
                    Glass(b, rng, 0.35f, 81, 1.8f, 0.12f, -0.3f, 0.01f);
                    Glass(b, rng, 0.42f, 88, 1.8f, 0.09f, 0.3f, 0.01f);
                    b.ApplyReverb(0.4f, 0.92f, 0.5f, 1f, 0.03f, 200f);
                    lp = 10000f;
                    break;
                }
                case Sfx.Anomaly:
                {
                    b = new Stereo(6.5f, sr);
                    ReverseSwell(b, rng, 1.0f, new float[] { 62, 68 }, 1.0f, 0.6f, 0.95f);
                    var env = new Adsr(0.8f, 0.5f, 0.7f, 0.8f, 2.0f, 3f);
                    foreach (float m in new float[] { 50, 62, 65, 68, 76 })
                        Tone(b, new ToneLayer { Start = 0.6f, Env = env, Freq = Const(Note.Hz(m)), Gain = 0.13f, FmRatio = 1.41f,
                            FmIndex = t => 0.5f + 0.4f * (float)Math.Sin(t * 0.9f), VibratoHz = 0.25f + m * 0.002f, VibratoCents = 10f, Pan = (m - 64f) / 18f }, rng);
                    Shimmer(b, rng, 1.0f, 3.5f, new float[] { 92, 97, 100 }, 0.05f);
                    NoiseL(b, new NoiseLayer { Start = 0.3f, Env = new Adsr(0.8f, 0.5f, 0.5f, 0.6f, 1.6f, 3f), Gain = 0.14f, Filter = SvfMode.BandPass,
                        Cutoff = t => 1400f + 700f * (float)Math.Sin(t * 1.7f), Q = 3f, Width = 1f }, seed);
                    Glass(b, rng, 1.0f, 50, 3.0f, 0.18f, 0f, 0.01f, 0.6f);
                    b.ApplyPingPong(0.37f, 0.45f, 0.18f, 2500f);
                    b.ApplyReverb(0.55f, 0.95f, 0.4f, 1f, 0.04f, 200f);
                    lp = 10000f;
                    break;
                }

                // ───────────────────── СТРОИТЕЛЬСТВО И НАУКА ─────────────────────
                case Sfx.BuildQueued:
                {
                    b = new Stereo(1.1f, sr);
                    for (int i = 0; i < 2; i++)
                        NoiseL(b, new NoiseLayer { Start = i * 0.05f, Env = Adsr.Pluck(0.001f, 0.02f, 6f), Color = NoiseColor.White, Gain = 0.3f,
                            Filter = SvfMode.BandPass, Cutoff = Const(2200f + i * 300f + variant * 150f), Q = 2f, Width = 0.3f }, seed + i);
                    Tone(b, new ToneLayer { Env = new Adsr(0.005f, 0.05f, 0.5f, 0.05f, 0.12f), Freq = Glide(140f, 110f, 0.15f), Gain = 0.28f }, rng);
                    Glass(b, rng, 0.08f, 74, 0.4f, 0.1f, 0.2f, 0.004f, 0.6f);
                    b.ApplyReverb(0.14f, 0.65f, 0.55f, 1f, 0.02f, 300f);
                    lp = 9000f;
                    break;
                }
                case Sfx.BuildComplete:
                {
                    b = new Stereo(2.6f, sr);
                    Thump(b, rng, 0f, 80f, 45f, 0.1f, 0.35f, 0.6f);
                    Modal(b, rng, 0f, 180f, new[] { 1f, 2.76f, 5.4f }, 0.8f, 0.14f);
                    Glass(b, rng, 0.04f, 69, 1.3f, 0.18f, -0.25f, 0.006f);
                    Glass(b, rng, 0.07f, 76, 1.3f, 0.15f, 0f, 0.006f);
                    Glass(b, rng, 0.10f, 81, 1.3f, 0.12f, 0.25f, 0.006f);
                    b.ApplyReverb(0.3f, 0.85f, 0.5f, 1f, 0.03f, 250f);
                    lp = 10000f;
                    break;
                }
                case Sfx.ShipLaunched:
                {
                    b = new Stereo(3.6f, sr);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(0.5f, 0.3f, 0.6f, 0.3f, 0.9f, 3f), Color = NoiseColor.Brown, Gain = 0.8f,
                        Filter = SvfMode.LowPass, Cutoff = Glide(80f, 900f, 0.7f) }, seed);
                    foreach (float m in new float[] { 38, 45 })
                        Tone(b, new ToneLayer { Env = new Adsr(0.5f, 0.3f, 0.6f, 0.2f, 0.9f, 3f), Wave = Wave.Saw, Unison = 5, DetuneCents = 16f,
                            Freq = Const(Note.Hz(m)), Gain = 0.12f, Filter = SvfMode.LowPass, Cutoff = Glide(200f, 1400f, 0.8f) }, rng);
                    Thump(b, rng, 0.7f, 70f, 35f, 0.25f, 0.8f, 0.6f);
                    NoiseL(b, new NoiseLayer { Start = 0.7f, Env = new Adsr(0.004f, 0.15f, 0.4f, 0.1f, 0.8f), Gain = 0.45f, Filter = SvfMode.LowPass,
                        Cutoff = Glide(4000f, 200f, 1.0f), Width = 0.9f }, seed + 1);
                    Glass(b, rng, 0.72f, 74, 1.8f, 0.08f, -0.3f, 0.01f);
                    Glass(b, rng, 0.76f, 81, 1.8f, 0.06f, 0.3f, 0.01f);
                    b.ApplyReverb(0.38f, 0.9f, 0.5f, 1f, 0.03f, 200f);
                    lp = 10000f;
                    break;
                }
                case Sfx.ColonyFounded:
                {
                    b = new Stereo(6.5f, sr);
                    ReverseSwell(b, rng, 0.5f, new float[] { 62, 69 }, 0.5f, 0.5f);
                    Pad(b, rng, 0.45f, new float[] { 50, 57, 62, 66, 69, 76 }, new Adsr(0.6f, 0.6f, 0.7f, 0.8f, 2.2f, 3f), 0.24f, 400f, 2000f);
                    Sub(b, rng, 0.45f, 73.4f, new Adsr(0.6f, 0.5f, 0.6f, 0.6f, 1.8f, 3f), 0.25f);
                    float[] arp = { 74, 78, 81, 88 };
                    for (int i = 0; i < arp.Length; i++) Glass(b, rng, 0.55f + i * 0.2f, arp[i], 2.2f, 0.14f, -0.4f + i * 0.27f, 0.008f);
                    Shimmer(b, rng, 0.8f, 3.5f, new float[] { 90, 93, 97 }, 0.05f);
                    b.ApplyReverb(0.48f, 0.93f, 0.45f, 1f, 0.035f, 200f);
                    lp = 11000f;
                    break;
                }
                case Sfx.ResearchComplete:
                {
                    b = new Stereo(6.5f, sr);
                    ReverseSwell(b, rng, 0.45f, new float[] { 62, 69, 74 }, 0.45f, 0.55f);
                    float[] bloom = { 74, 80, 81, 85, 88 };   // ре лидийский: соль-диез даёт «сияние»
                    for (int i = 0; i < bloom.Length; i++) Glass(b, rng, 0.45f + i * 0.045f, bloom[i], 2.4f, 0.14f, (i % 2 == 0 ? -1 : 1) * 0.35f, 0.006f);
                    Pad(b, rng, 0.42f, new float[] { 50, 57, 62, 66, 68, 73 }, new Adsr(0.3f, 0.6f, 0.6f, 0.8f, 2.0f, 3f), 0.18f, 600f, 2400f);
                    Sub(b, rng, 0.42f, 73.4f, new Adsr(0.4f, 0.5f, 0.5f, 0.5f, 1.6f, 3f), 0.2f);
                    Shimmer(b, rng, 0.6f, 3.6f, new float[] { 93, 97, 100 }, 0.045f);
                    b.ApplyPingPong(0.23f, 0.35f, 0.12f, 4000f);
                    b.ApplyReverb(0.5f, 0.94f, 0.45f, 1f, 0.035f, 250f);
                    lp = 12000f;
                    break;
                }
                case Sfx.ResearchStart:
                {
                    b = new Stereo(2.2f, sr);
                    Glass(b, rng, 0f, 74, 0.9f, 0.22f, -0.2f, 0.01f);
                    Glass(b, rng, 0.02f, 81, 0.9f, 0.18f, 0.2f, 0.01f);
                    Breath(b, seed, 0f, 0.2f, 1200f, 3000f, 0.1f);
                    b.ApplyReverb(0.26f, 0.85f, 0.5f, 1f, 0.03f, 300f);
                    lp = 10000f;
                    break;
                }

                // ───────────────────── БОЙ ─────────────────────
                // Оружие звучит тяжело и «издалека»: короткий треск, плотный шумовой залп, удар в саб, тёмный хвост.
                // Никаких музыкальных глиссандо — тон задают шум, фильтры и саб.
                case Sfx.WeaponKinetic:
                {
                    // Масс-драйвер: щелчок разряда, взрывной выброс, удар в грудь, механический лязг затвора, гул отдачи
                    b = new Stereo(1.5f, sr);
                    float k = 1f + variant * 0.06f;
                    NoiseL(b, new NoiseLayer { Env = Adsr.Pluck(0.0004f, 0.008f, 6f), Color = NoiseColor.White, Gain = 0.35f,
                        Filter = SvfMode.HighPass, Cutoff = Const(2500f), Width = 0.3f }, seed);
                    NoiseL(b, new NoiseLayer { Env = Adsr.Pluck(0.0006f, 0.03f, 6f), Gain = 0.12f,
                        Filter = SvfMode.BandPass, Cutoff = Glide(6000f, 2500f, 0.03f), Q = 1.2f, Width = 0.5f }, seed + 4);
                    NoiseL(b, new NoiseLayer { Env = Adsr.Pluck(0.001f, 0.2f, 5f), Gain = 0.85f,
                        Filter = SvfMode.LowPass, Cutoff = Glide(3500f * k, 280f, 0.13f), Q = 0.8f, Width = 0.5f }, seed + 1);
                    Thump(b, rng, 0f, 78f * k, 36f, 0.08f, 0.38f, 1f);
                    NoiseL(b, new NoiseLayer { Env = Adsr.Pluck(0.004f, 0.65f, 4f), Color = NoiseColor.Brown, Gain = 0.6f,
                        Filter = SvfMode.LowPass, Cutoff = Const(190f) }, seed + 2);
                    NoiseL(b, new NoiseLayer { Start = 0.035f, Env = Adsr.Pluck(0.001f, 0.045f, 6f), Color = NoiseColor.White, Gain = 0.13f,
                        Filter = SvfMode.BandPass, Cutoff = Const(950f * k), Q = 3f, Pan = 0.15f }, seed + 3);
                    b.Saturate(1.0f);
                    b.ApplyReverb(0.3f, 0.88f, 0.6f, 1f, 0.025f, 120f);
                    lp = 7000f;
                    break;
                }
                case Sfx.WeaponEnergy:
                {
                    // Плазменное копьё: треск разряда, глухой «тумм», гудящий электрический выброс с дрожью сети и искры
                    b = new Stereo(1.4f, sr);
                    float k = 1f + variant * 0.07f;
                    var env = new Adsr(0.002f, 0.05f, 0.55f, 0.08f, 0.24f);
                    Func<float, float> mains = t => 1f - 0.38f * (0.5f + 0.5f * (float)Math.Sin(2.0 * Math.PI * 85.0 * t));
                    NoiseL(b, new NoiseLayer { Env = Adsr.Pluck(0.0005f, 0.02f, 6f), Color = NoiseColor.White, Gain = 0.3f,
                        Filter = SvfMode.BandPass, Cutoff = Const(3200f), Q = 1f, Width = 0.4f }, seed);
                    Thump(b, rng, 0f, 105f * k, 48f, 0.06f, 0.26f, 0.72f);
                    NoiseL(b, new NoiseLayer { Env = env, Gain = 0.6f, Filter = SvfMode.BandPass,
                        Cutoff = Glide(1800f * k, 650f, 0.28f), Q = 1.2f, Width = 0.7f, Amp = mains }, seed + 1);
                    Tone(b, new ToneLayer { Env = env, Wave = Wave.Saw, Unison = 3, DetuneCents = 9f, Spread = 0.7f,
                        Freq = Const(55f * k), Gain = 0.2f, Filter = SvfMode.LowPass, Cutoff = Const(420f), Q = 1.4f, Drive = 1.5f, Amp = mains }, rng);
                    Crackle(b, rng, 0.01f, 0.28f, 900f, 0.06f, 4500f);
                    b.Saturate(0.6f);
                    b.ApplyReverb(0.28f, 0.86f, 0.6f, 1f, 0.02f, 140f);
                    lp = 8000f;
                    break;
                }
                case Sfx.WeaponMissile:
                {
                    // Пуск ракеты: хлопок зажигания и рёв двигателя, который уходит вдаль (фильтр закрывается)
                    b = new Stereo(1.9f, sr);
                    float k = 1f + variant * 0.08f;
                    NoiseL(b, new NoiseLayer { Env = Adsr.Pluck(0.0005f, 0.012f, 6f), Color = NoiseColor.White, Gain = 0.22f,
                        Filter = SvfMode.HighPass, Cutoff = Const(2000f) }, seed);
                    Thump(b, rng, 0f, 88f, 40f, 0.05f, 0.22f, 0.75f);
                    var roar = new Adsr(0.012f, 0.1f, 0.8f, 0.25f, 0.95f, 3f);
                    Func<float, float> burn = t => 1f - 0.16f * (float)Math.Sin(2.0 * Math.PI * 23.0 * t) - 0.08f * (float)Math.Sin(2.0 * Math.PI * 37.0 * t);
                    NoiseL(b, new NoiseLayer { Env = roar, Gain = 0.6f, Filter = SvfMode.LowPass, Width = 0.7f, Amp = burn,
                        Cutoff = t => t < 0.22f ? Note.ExpLerp(900f, 2300f * k, t / 0.22f) : Note.ExpLerp(2300f * k, 420f, (t - 0.22f) / 0.95f) }, seed + 1);
                    NoiseL(b, new NoiseLayer { Env = roar, Color = NoiseColor.Brown, Gain = 0.65f, Filter = SvfMode.LowPass,
                        Cutoff = Const(260f), Amp = burn }, seed + 2);
                    b.Saturate(0.7f);
                    b.ApplyReverb(0.3f, 0.88f, 0.6f, 1f, 0.03f, 120f);
                    lp = 8000f;
                    break;
                }
                case Sfx.HitShield:
                {
                    // Щит гасит удар: глухой «вумф», вспышка поля с дрожью, электрическое шипение
                    b = new Stereo(1.3f, sr);
                    float k = 1f + variant * 0.07f;
                    Thump(b, rng, 0f, 88f * k, 42f, 0.07f, 0.3f, 0.75f);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(0.002f, 0.04f, 0.45f, 0.05f, 0.32f), Gain = 0.5f, Filter = SvfMode.BandPass,
                        Cutoff = Glide(1500f * k, 420f, 0.32f), Q = 1.4f, Width = 0.9f }, seed);
                    Tone(b, new ToneLayer { Env = new Adsr(0.004f, 0.05f, 0.5f, 0.1f, 0.4f), Freq = Const(118f * k), Gain = 0.16f,
                        Amp = t => 1f - 0.5f * (0.5f + 0.5f * (float)Math.Sin(2.0 * Math.PI * 14.0 * t)) }, rng);
                    Crackle(b, rng, 0f, 0.3f, 1400f, 0.07f, 5200f);
                    b.ApplyReverb(0.26f, 0.84f, 0.6f, 1f, 0.02f, 140f);
                    lp = 9000f;
                    break;
                }
                case Sfx.HitArmor:
                {
                    // Пробитие брони: треск, тяжёлый удар, низкий стон металла корпуса, сыплющиеся обломки
                    b = new Stereo(1.3f, sr);
                    NoiseL(b, new NoiseLayer { Env = Adsr.Pluck(0.0005f, 0.016f, 6f), Color = NoiseColor.White, Gain = 0.4f,
                        Filter = SvfMode.BandPass, Cutoff = Const(3000f), Q = 0.7f }, seed);
                    Thump(b, rng, 0f, 96f, 40f, 0.06f, 0.3f, 0.95f);
                    Modal(b, rng, 0f, rng.Range(70f, 95f), new[] { 1f, 1.47f, 2.09f, 2.81f }, 0.55f, 0.2f);
                    NoiseL(b, new NoiseLayer { Env = Adsr.Pluck(0.003f, 0.42f, 4f), Color = NoiseColor.Brown, Gain = 0.45f,
                        Filter = SvfMode.LowPass, Cutoff = Const(300f) }, seed + 1);
                    Crackle(b, rng, 0.02f, 0.45f, 160f, 0.18f, 2200f);
                    b.Saturate(0.7f);
                    b.ApplyReverb(0.24f, 0.85f, 0.6f, 1f, 0.02f, 100f);
                    lp = 8000f;
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
                    b = new Stereo(5.0f, sr);
                    Braam(b, rng, 0f, new float[] { 38, 45, 50 }, 0.35f, 0.55f, 1600f, 0.05f, 0.9f);
                    Braam(b, rng, 0.75f, new float[] { 41, 48, 53 }, 0.6f, 0.55f, 1800f, 0.05f, 1.4f);
                    Thump(b, rng, 0f, 60f, 35f, 0.2f, 0.9f, 0.8f);
                    Thump(b, rng, 0.75f, 60f, 32f, 0.2f, 1.1f, 0.85f);
                    b.ApplyReverb(0.45f, 0.92f, 0.45f, 1f, 0.03f, 150f);
                    lp = 9000f;
                    break;
                }
                case Sfx.BattleVictory:
                {
                    b = new Stereo(5.5f, sr);
                    ReverseSwell(b, rng, 0.4f, new float[] { 62, 69 }, 0.4f, 0.5f);
                    Braam(b, rng, 0.4f, new float[] { 50, 57, 62, 66 }, 1.0f, 0.42f, 2200f, 0.08f, 1.6f);
                    Thump(b, rng, 0.4f, 70f, 40f, 0.15f, 0.8f, 0.7f);
                    float[] arp = { 74, 78, 81, 86 };
                    for (int i = 0; i < arp.Length; i++) Glass(b, rng, 0.45f + i * 0.08f, arp[i], 2.0f, 0.12f, -0.35f + i * 0.23f, 0.006f);
                    Shimmer(b, rng, 0.6f, 3f, new float[] { 93, 97 }, 0.04f);
                    b.ApplyReverb(0.48f, 0.92f, 0.45f, 1f, 0.03f, 200f);
                    lp = 11000f;
                    break;
                }
                case Sfx.BattleDefeat:
                {
                    b = new Stereo(6.0f, sr);
                    Braam(b, rng, 0f, new float[] { 38, 45, 48 }, 1.2f, 0.45f, 900f, 0.3f, 2.0f);
                    Sub(b, rng, 0f, 73.4f, new Adsr(0.3f, 0.5f, 0.6f, 1.0f, 2.0f, 3f), 0.3f, 55f);
                    Glass(b, rng, 0.5f, 65, 2.5f, 0.1f, -0.3f, 0.02f, 0.6f);
                    Glass(b, rng, 0.9f, 64, 2.5f, 0.1f, 0.3f, 0.02f, 0.6f);
                    b.ApplyReverb(0.5f, 0.93f, 0.5f, 1f, 0.035f, 150f);
                    lp = 8000f;
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
                // ───────────────────── СТРАТЕГИЯ ─────────────────────
                case Sfx.WarDeclared:
                {
                    b = new Stereo(8.0f, sr);
                    ReverseSwell(b, rng, 0.9f, new float[] { 38, 50 }, 0.9f, 0.7f, 0.95f);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(0.85f, 0.001f, 1f, 0f, 0.05f, 2f), Gain = 0.3f, Filter = SvfMode.LowPass,
                        Cutoff = Glide(200f, 3000f, 0.9f), Width = 1f }, seed);
                    Braam(b, rng, 0.9f, new float[] { 38, 45, 51 }, 1.6f, 0.6f, 1800f, 0.04f, 2.2f);
                    Thump(b, rng, 0.9f, 50f, 25f, 0.4f, 2.2f, 1f);
                    NoiseL(b, new NoiseLayer { Start = 0.9f, Env = Adsr.Pluck(0.004f, 1.0f, 4f), Color = NoiseColor.Brown, Gain = 0.6f,
                        Filter = SvfMode.LowPass, Cutoff = Const(300f) }, seed + 1);
                    Thump(b, rng, 1.6f, 55f, 30f, 0.3f, 1.4f, 0.6f);
                    Modal(b, rng, 0.92f, 610f, new[] { 1f, 1.38f, 2.13f, 2.97f }, 2.0f, 0.06f);
                    b.ApplyReverb(0.6f, 0.96f, 0.4f, 1f, 0.04f, 120f);
                    lp = 9000f;
                    break;
                }
                case Sfx.PeaceSigned:
                {
                    b = new Stereo(7.0f, sr);
                    ReverseSwell(b, rng, 0.5f, new float[] { 62, 66 }, 0.5f, 0.4f);
                    Pad(b, rng, 0.4f, new float[] { 50, 57, 62, 66, 69, 76 }, new Adsr(0.8f, 0.6f, 0.7f, 1.0f, 2.4f, 3f), 0.22f, 500f, 1800f);
                    float[] arp = { 78, 81, 85, 88 };
                    for (int i = 0; i < arp.Length; i++) Glass(b, rng, 0.6f + i * 0.28f, arp[i], 2.4f, 0.13f, -0.3f + i * 0.2f, 0.01f);
                    Shimmer(b, rng, 0.8f, 4.0f, new float[] { 93, 97, 100 }, 0.045f);
                    b.ApplyReverb(0.55f, 0.94f, 0.45f, 1f, 0.04f, 200f);
                    lp = 11000f;
                    break;
                }
                case Sfx.PactSigned:
                {
                    b = new Stereo(5.0f, sr);
                    Pad(b, rng, 0f, new float[] { 50, 57, 62, 64, 69 }, new Adsr(0.4f, 0.4f, 0.6f, 0.5f, 1.8f, 3f), 0.2f, 600f, 1600f);
                    Glass(b, rng, 0.15f, 74, 2.2f, 0.16f, -0.3f, 0.01f);
                    Glass(b, rng, 0.18f, 81, 2.2f, 0.14f, 0f, 0.01f);
                    Glass(b, rng, 0.21f, 88, 2.0f, 0.1f, 0.3f, 0.01f);
                    b.ApplyReverb(0.48f, 0.92f, 0.45f, 1f, 0.035f, 200f);
                    lp = 11000f;
                    break;
                }
                case Sfx.SiegeStart:
                {
                    b = new Stereo(3.6f, sr);
                    var env = new Adsr(0.15f, 0.2f, 0.7f, 0.8f, 0.6f, 3f);
                    foreach (float m in new float[] { 38, 44 })
                        Tone(b, new ToneLayer { Env = env, Wave = Wave.Saw, Unison = 5, DetuneCents = 15f, Freq = Const(Note.Hz(m)), Gain = 0.25f,
                            Filter = SvfMode.LowPass, Cutoff = t => 350f + 350f * (0.5f + 0.5f * (float)Math.Sin(t * 6.283f * 2.2f - 1.57f)), Q = 1.5f, Drive = 0.4f }, rng);
                    Sub(b, rng, 0f, 73.4f, env, 0.3f);
                    Glass(b, rng, 0f, 80, 1.2f, 0.07f, -0.3f, 0.01f, 0.6f);
                    Glass(b, rng, 0.02f, 81, 1.2f, 0.07f, 0.3f, 0.01f, 0.6f);
                    b.ApplyReverb(0.35f, 0.88f, 0.5f, 1f, 0.03f, 200f);
                    lp = 8000f;
                    break;
                }
                case Sfx.SystemCaptured:
                {
                    b = new Stereo(4.8f, sr);
                    Braam(b, rng, 0f, new float[] { 50, 57, 62, 66 }, 0.5f, 0.4f, 2000f, 0.06f, 1.4f);
                    Thump(b, rng, 0f, 70f, 40f, 0.15f, 0.7f, 0.7f);
                    Glass(b, rng, 0.05f, 74, 2.0f, 0.14f, -0.3f, 0.006f);
                    Glass(b, rng, 0.09f, 81, 2.0f, 0.12f, 0f, 0.006f);
                    Glass(b, rng, 0.13f, 86, 2.0f, 0.1f, 0.3f, 0.006f);
                    b.ApplyReverb(0.45f, 0.92f, 0.45f, 1f, 0.03f, 200f);
                    lp = 11000f;
                    break;
                }
                case Sfx.SystemLost:
                {
                    b = new Stereo(5.0f, sr);
                    Braam(b, rng, 0f, new float[] { 38, 45, 50, 53 }, 0.6f, 0.45f, 1000f, 0.08f, 1.8f);
                    Thump(b, rng, 0f, 55f, 30f, 0.25f, 1.2f, 0.8f);
                    Glass(b, rng, 0.3f, 77, 2.2f, 0.08f, -0.3f, 0.02f, 0.6f);
                    Glass(b, rng, 0.5f, 76, 2.2f, 0.08f, 0.3f, 0.02f, 0.6f);
                    b.ApplyReverb(0.48f, 0.93f, 0.5f, 1f, 0.035f, 150f);
                    lp = 8500f;
                    break;
                }
                case Sfx.GameVictory:
                {
                    b = new Stereo(9.5f, sr);
                    ReverseSwell(b, rng, 1.2f, new float[] { 50, 62, 69 }, 1.2f, 0.6f, 0.95f);
                    Braam(b, rng, 1.2f, new float[] { 38, 45, 50, 54, 57 }, 2.5f, 0.45f, 2400f, 0.1f, 2.8f);
                    Pad(b, rng, 1.2f, new float[] { 62, 66, 69, 76 }, new Adsr(0.6f, 0.8f, 0.7f, 1.6f, 2.8f, 3f), 0.15f, 800f, 2400f);
                    for (int i = 0; i < 8; i++) Thump(b, rng, 0.6f + i * 0.075f, 75f, 50f, 0.05f, 0.25f, 0.2f + i * 0.05f);
                    Thump(b, rng, 1.2f, 65f, 35f, 0.2f, 1.4f, 1f);
                    float[] arp = { 74, 78, 81, 86, 88, 93 };
                    for (int i = 0; i < arp.Length; i++) Glass(b, rng, 1.3f + i * 0.11f, arp[i], 2.6f, 0.11f, -0.45f + i * 0.18f, 0.008f);
                    Shimmer(b, rng, 1.5f, 5f, new float[] { 93, 97, 100, 105 }, 0.05f);
                    b.ApplyReverb(0.55f, 0.95f, 0.45f, 1f, 0.04f, 150f);
                    lp = 12000f;
                    break;
                }
                case Sfx.GameDefeat:
                {
                    b = new Stereo(9.0f, sr);
                    Braam(b, rng, 0f, new float[] { 38, 45, 48, 51 }, 2.2f, 0.45f, 800f, 0.6f, 3.0f);
                    Thump(b, rng, 0f, 50f, 26f, 0.4f, 2.2f, 1f);
                    Glass(b, rng, 0.8f, 65, 3.0f, 0.1f, -0.3f, 0.02f, 0.6f);
                    Glass(b, rng, 1.8f, 63, 3.0f, 0.1f, 0.3f, 0.02f, 0.6f);
                    Sub(b, rng, 0f, 73.4f, new Adsr(1.0f, 0.8f, 0.7f, 2.0f, 3.0f, 3f), 0.3f, 61.7f);
                    b.ApplyReverb(0.55f, 0.95f, 0.5f, 1f, 0.04f, 120f);
                    lp = 7500f;
                    break;
                }
                case Sfx.GameStart:
                {
                    b = new Stereo(8.0f, sr);
                    NoiseL(b, new NoiseLayer { Env = new Adsr(1.5f, 0.001f, 1f, 0f, 0.06f, 2f), Gain = 0.4f, Filter = SvfMode.BandPass,
                        Cutoff = Glide(250f, 5000f, 1.55f), Q = 1.2f, Width = 1f }, seed);
                    Tone(b, new ToneLayer { Env = new Adsr(1.5f, 0.001f, 1f, 0f, 0.06f, 2f), Wave = Wave.Saw, Unison = 7, DetuneCents = 30f, Spread = 0.95f,
                        Freq = Glide(36.7f, 293.7f, 1.55f), Gain = 0.22f, Filter = SvfMode.LowPass, Cutoff = Glide(200f, 3500f, 1.55f), Q = 1.2f }, rng);
                    ReverseSwell(b, rng, 1.55f, new float[] { 50, 62, 69 }, 1.2f, 0.5f, 0.95f);
                    Thump(b, rng, 1.55f, 60f, 28f, 0.35f, 1.8f, 1f);
                    NoiseL(b, new NoiseLayer { Start = 1.55f, Env = new Adsr(0.005f, 0.3f, 0.4f, 0.2f, 1.6f), Gain = 0.4f, Filter = SvfMode.LowPass,
                        Cutoff = Glide(5000f, 200f, 1.6f), Width = 1f }, seed + 1);
                    Pad(b, rng, 1.55f, new float[] { 38, 50, 57, 62, 64, 69 }, new Adsr(0.05f, 0.8f, 0.6f, 0.8f, 2.6f, 3f), 0.26f, 2400f, 500f);
                    Glass(b, rng, 1.6f, 74, 3.0f, 0.12f, -0.3f, 0.01f);
                    Glass(b, rng, 1.68f, 81, 3.0f, 0.1f, 0.3f, 0.01f);
                    Shimmer(b, rng, 1.8f, 4f, new float[] { 93, 97, 100 }, 0.045f);
                    b.ApplyReverb(0.52f, 0.95f, 0.45f, 1f, 0.04f, 150f);
                    lp = 12000f;
                    break;
                }
                default:
                    b = new Stereo(0.2f, sr);
                    Tone(b, new ToneLayer { Env = Adsr.Pluck(0.002f, 0.08f), Freq = Const(1000f), Gain = 0.3f }, rng);
                    break;
            }

            return b.Finish(-1f, lp);
        }

        /// <summary>«Спутниковый» пинг: синус с лёгким падением высоты, тихий обертон, пинг-понг эхо и зал.</summary>
        private static Stereo SatellitePing(int sr, Noise rng, float k)
        {
            var b = new Stereo(1.8f, sr);
            Tone(b, new ToneLayer { Env = Adsr.Pluck(0.004f, 0.9f, 4f), Freq = Glide(1350f * k, 1300f * k, 0.2f), Gain = 0.5f }, rng);
            Tone(b, new ToneLayer { Env = Adsr.Pluck(0.002f, 0.25f, 5f), Freq = Const(2637f * k), Gain = 0.12f }, rng);
            b.ApplyPingPong(0.19f, 0.38f, 0.32f, 3500f);
            b.ApplyReverb(0.28f, 0.8f, 0.5f);
            return b;
        }

        /// <summary>
        /// Взрыв: треск → огненный шум с закрывающимся фильтром → саб-удар → рокот → разлёт обломков.
        /// size 1 — корвет, 2 — эсминец/база.
        /// </summary>
        private static Stereo Explosion(int sr, Noise rng, int seed, float size, int variant, float seconds = -1f)
        {
            var b = new Stereo(seconds > 0f ? seconds : 2.0f + size * 1.6f, sr);
            float body = 0.6f * size + 0.2f;
            // Короткий треск детонации
            NoiseL(b, new NoiseLayer { Env = Adsr.Pluck(0.0006f, 0.02f, 6f), Color = NoiseColor.White, Gain = 0.3f,
                Filter = SvfMode.HighPass, Cutoff = Const(2500f), Width = 0.4f }, seed);
            // Огненный шар: плотный шум, фильтр закрывается
            NoiseL(b, new NoiseLayer { Env = new Adsr(0.003f, 0.16f * size, 0.55f, 0.12f * size, body, 4f), Gain = 0.95f, Filter = SvfMode.LowPass,
                Cutoff = Glide(3200f + variant * 400f, 180f, body), Q = 0.9f, Width = 0.7f }, seed + 1);
            // Середина: «мясо» взрыва
            NoiseL(b, new NoiseLayer { Env = new Adsr(0.004f, 0.1f * size, 0.5f, 0.08f * size, 0.6f * size, 4f), Gain = 0.45f, Filter = SvfMode.BandPass,
                Cutoff = Glide(900f, 250f, 0.5f * size), Q = 0.8f, Width = 0.8f }, seed + 5);
            // Удар и волна давления в саб
            Thump(b, rng, 0f, 66f / Math.Max(1f, size * 0.7f), 24f, 0.3f * size, 0.6f * size + 0.15f, 1f);
            Tone(b, new ToneLayer { Env = new Adsr(0.01f, 0.2f, 0.6f, 0.15f * size, 0.9f * size, 3f), Freq = Glide(40f, 27f, 1.2f * size), Gain = 0.45f }, rng);
            // Долгий рокот
            NoiseL(b, new NoiseLayer { Env = new Adsr(0.02f, 0.25f * size, 0.65f, 0.3f * size, 1.1f * size, 4f), Color = NoiseColor.Brown, Gain = 0.85f,
                Filter = SvfMode.LowPass, Cutoff = Const(200f) }, seed + 2);
            Crackle(b, rng, 0.06f, 1.1f * size, 70f, 0.22f, 2400f);
            if (size > 1.5f)
            {
                // Вторичные детонации
                Thump(b, rng, 0.25f + variant * 0.05f, 60f, 28f, 0.2f, 0.7f, 0.6f);
                NoiseL(b, new NoiseLayer { Start = 0.25f, Env = new Adsr(0.003f, 0.1f, 0.4f, 0.05f, 0.6f), Gain = 0.5f, Filter = SvfMode.LowPass,
                    Cutoff = Glide(2600f, 220f, 0.6f), Width = 0.8f, Pan = -0.3f }, seed + 3);
                Thump(b, rng, 0.55f, 52f, 26f, 0.2f, 0.8f, 0.5f);
                NoiseL(b, new NoiseLayer { Start = 0.55f, Env = new Adsr(0.003f, 0.1f, 0.4f, 0.05f, 0.7f), Gain = 0.45f, Filter = SvfMode.LowPass,
                    Cutoff = Glide(2300f, 200f, 0.7f), Width = 0.8f, Pan = 0.3f }, seed + 4);
            }
            b.Saturate(0.9f);
            b.ApplyReverb(size > 1.5f ? 0.42f : 0.32f, size > 1.5f ? 0.92f : 0.86f, 0.6f, 1f, 0.03f, 60f);
            return b;
        }
    }
}
