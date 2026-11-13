using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Справочник типов звёзд: названия, цвета (карта, интерфейс, поверхность), размер, редкость и влияние на систему.
    /// Одно место вместо разрозненных switch по классам — новый тип добавляется здесь.
    /// </summary>
    public static class StarTypes
    {
        // ---------- Названия ----------

        /// <summary>Полное название с буквой класса: «Жёлтая (G)», «Пульсар».</summary>
        public static string Name(StarSpectralClass c) => c switch
        {
            StarSpectralClass.ClassM => Loc.T("Красный карлик (M)"),
            StarSpectralClass.ClassK => Loc.T("Оранжевая (K)"),
            StarSpectralClass.ClassG => Loc.T("Жёлтая (G)"),
            StarSpectralClass.ClassA => Loc.T("Белая (A)"),
            StarSpectralClass.ClassB => Loc.T("Голубой гигант (B)"),
            StarSpectralClass.BlackHole => Loc.T("Чёрная дыра"),
            StarSpectralClass.ClassO => Loc.T("Голубой сверхгигант (O)"),
            StarSpectralClass.RedGiant => Loc.T("Красный гигант"),
            StarSpectralClass.WhiteDwarf => Loc.T("Белый карлик"),
            StarSpectralClass.BrownDwarf => Loc.T("Коричневый карлик"),
            StarSpectralClass.Binary => Loc.T("Двойная звезда"),
            StarSpectralClass.WolfRayet => Loc.T("Звезда Вольфа-Райе"),
            StarSpectralClass.Pulsar => Loc.T("Пульсар"),
            StarSpectralClass.Magnetar => Loc.T("Магнетар"),
            StarSpectralClass.Protostar => Loc.T("Протозвезда"),
            StarSpectralClass.PlanetaryNebula => Loc.T("Планетарная туманность"),
            _ => c.ToString()
        };

        /// <summary>Строчное название для подписей и подсказок: «жёлтая звезда».</summary>
        public static string Hint(StarSpectralClass c) => c switch
        {
            StarSpectralClass.ClassM => Loc.T("красный карлик"),
            StarSpectralClass.ClassK => Loc.T("оранжевая звезда"),
            StarSpectralClass.ClassG => Loc.T("жёлтая звезда"),
            StarSpectralClass.ClassA => Loc.T("белая звезда"),
            StarSpectralClass.ClassB => Loc.T("голубой гигант"),
            StarSpectralClass.BlackHole => Loc.T("чёрная дыра"),
            StarSpectralClass.ClassO => Loc.T("голубой сверхгигант"),
            StarSpectralClass.RedGiant => Loc.T("красный гигант"),
            StarSpectralClass.WhiteDwarf => Loc.T("белый карлик"),
            StarSpectralClass.BrownDwarf => Loc.T("коричневый карлик"),
            StarSpectralClass.Binary => Loc.T("двойная звезда"),
            StarSpectralClass.WolfRayet => Loc.T("звезда Вольфа-Райе"),
            StarSpectralClass.Pulsar => Loc.T("пульсар"),
            StarSpectralClass.Magnetar => Loc.T("магнетар"),
            StarSpectralClass.Protostar => Loc.T("протозвезда"),
            StarSpectralClass.PlanetaryNebula => Loc.T("планетарная туманность"),
            _ => c.ToString()
        };

        /// <summary>Короткая метка для плитки инспектора: буква класса или знак типа.</summary>
        public static string Label(StarSpectralClass c) => c switch
        {
            StarSpectralClass.ClassM => "M",
            StarSpectralClass.ClassK => "K",
            StarSpectralClass.ClassG => "G",
            StarSpectralClass.ClassA => "A",
            StarSpectralClass.ClassB => "B",
            StarSpectralClass.ClassO => "O",
            StarSpectralClass.BlackHole => Loc.T("Дыра"),
            StarSpectralClass.RedGiant => Loc.T("Гигант"),
            StarSpectralClass.WhiteDwarf => "D",
            StarSpectralClass.BrownDwarf => "L",
            StarSpectralClass.Binary => Loc.T("×2"),
            StarSpectralClass.WolfRayet => "WR",
            StarSpectralClass.Pulsar => "PSR",
            StarSpectralClass.Magnetar => "SGR",
            StarSpectralClass.Protostar => Loc.T("Прото"),
            StarSpectralClass.PlanetaryNebula => "PN",
            _ => "?"
        };

        /// <summary>Короткая строка влияния на систему (подсказка, инспектор); пусто — обычная звезда.</summary>
        public static string EffectText(StarSpectralClass c) => c switch
        {
            StarSpectralClass.ClassO => Loc.T("гелий-3 +25% · обитаемых миров нет"),
            StarSpectralClass.RedGiant => Loc.T("титан +20%"),
            StarSpectralClass.WhiteDwarf => Loc.T("мало планет"),
            StarSpectralClass.BrownDwarf => Loc.T("гелий-3 ×2 · обитаемых миров нет"),
            StarSpectralClass.Binary => Loc.T("+2 планеты"),
            StarSpectralClass.WolfRayet => Loc.T("залежи +50% · обитаемых миров нет"),
            StarSpectralClass.Pulsar => Loc.T("титан +30% · обитаемых миров нет"),
            StarSpectralClass.Magnetar => Loc.T("залежи ×2 · обитаемых миров нет"),
            StarSpectralClass.Protostar => Loc.T("только раскалённые миры"),
            StarSpectralClass.PlanetaryNebula => Loc.T("гелий-3 +50% · обитаемых миров нет"),
            _ => ""
        };

        // ---------- Цвета и размер ----------

        /// <summary>Цвет звезды на карте (спрайты, свет в виде системы).</summary>
        public static Color MapColor(StarSpectralClass c) => c switch
        {
            StarSpectralClass.ClassM => new Color(1.00f, 0.35f, 0.25f),
            StarSpectralClass.ClassK => new Color(1.00f, 0.65f, 0.30f),
            StarSpectralClass.ClassG => new Color(1.00f, 0.92f, 0.55f),
            StarSpectralClass.ClassA => new Color(0.95f, 0.98f, 1.00f),
            StarSpectralClass.ClassB => new Color(0.45f, 0.75f, 1.00f),
            StarSpectralClass.ClassO => new Color(0.62f, 0.56f, 1.00f),
            StarSpectralClass.RedGiant => new Color(1.00f, 0.30f, 0.14f),
            StarSpectralClass.WhiteDwarf => new Color(0.88f, 0.95f, 1.00f),
            StarSpectralClass.BrownDwarf => new Color(0.70f, 0.26f, 0.36f),
            StarSpectralClass.Binary => new Color(1.00f, 0.82f, 0.50f),
            StarSpectralClass.WolfRayet => new Color(0.55f, 0.95f, 1.00f),
            StarSpectralClass.Pulsar => new Color(0.70f, 0.90f, 1.00f),
            StarSpectralClass.Magnetar => new Color(0.80f, 0.50f, 1.00f),
            StarSpectralClass.Protostar => new Color(1.00f, 0.62f, 0.38f),
            StarSpectralClass.PlanetaryNebula => new Color(0.45f, 0.95f, 0.92f),
            _ => Color.white
        };

        /// <summary>Мягкий цвет для интерфейса (плитки, шапки, акценты).</summary>
        public static Color Tint(StarSpectralClass c) => c switch
        {
            StarSpectralClass.ClassM => new Color(1f, 0.55f, 0.42f),
            StarSpectralClass.ClassK => new Color(1f, 0.76f, 0.46f),
            StarSpectralClass.ClassG => new Color(1f, 0.94f, 0.66f),
            StarSpectralClass.ClassA => new Color(0.86f, 0.93f, 1f),
            StarSpectralClass.ClassB => new Color(0.62f, 0.78f, 1f),
            StarSpectralClass.BlackHole => new Color(0.70f, 0.50f, 1f),
            StarSpectralClass.ClassO => new Color(0.72f, 0.68f, 1f),
            StarSpectralClass.RedGiant => new Color(1f, 0.50f, 0.36f),
            StarSpectralClass.WhiteDwarf => new Color(0.90f, 0.95f, 1f),
            StarSpectralClass.BrownDwarf => new Color(0.86f, 0.50f, 0.58f),
            StarSpectralClass.Binary => new Color(1f, 0.86f, 0.60f),
            StarSpectralClass.WolfRayet => new Color(0.55f, 0.95f, 0.95f),
            StarSpectralClass.Pulsar => new Color(0.65f, 0.90f, 1f),
            StarSpectralClass.Magnetar => new Color(0.80f, 0.55f, 1f),
            StarSpectralClass.Protostar => new Color(1f, 0.68f, 0.45f),
            StarSpectralClass.PlanetaryNebula => new Color(0.50f, 0.95f, 0.92f),
            _ => new Color(0.70f, 0.50f, 1f)
        };

        /// <summary>Тёмный и горячий цвета поверхности (sRGB) — для шейдера звезды.</summary>
        public static void Palette(StarSpectralClass c, out Color cool, out Color hot)
        {
            switch (c)
            {
                case StarSpectralClass.ClassM: cool = Hex(0x6e1a06); hot = Hex(0xff7434); break;
                case StarSpectralClass.ClassK: cool = Hex(0x9c3a0b); hot = Hex(0xffb05e); break;
                case StarSpectralClass.ClassA: cool = Hex(0xc98a30); hot = Hex(0xfff4dc); break;
                case StarSpectralClass.ClassB: cool = Hex(0x2f5fc8); hot = Hex(0xd2e6ff); break;
                case StarSpectralClass.ClassO: cool = Hex(0x3a2fb8); hot = Hex(0xe6ecff); break;
                case StarSpectralClass.RedGiant: cool = Hex(0x4a0c03); hot = Hex(0xff6a2a); break;
                case StarSpectralClass.WhiteDwarf: cool = Hex(0x8fb4ff); hot = Hex(0xffffff); break;
                case StarSpectralClass.BrownDwarf: cool = Hex(0x2a0c18); hot = Hex(0x9a4a44); break;
                case StarSpectralClass.WolfRayet: cool = Hex(0x2a6ad8); hot = Hex(0xe8fbff); break;
                case StarSpectralClass.Pulsar: cool = Hex(0x7ac8ff); hot = Hex(0xffffff); break;
                case StarSpectralClass.Magnetar: cool = Hex(0x8a5cff); hot = Hex(0xfff0ff); break;
                case StarSpectralClass.Protostar: cool = Hex(0xc8501a); hot = Hex(0xffd2a0); break;
                case StarSpectralClass.PlanetaryNebula: cool = Hex(0xa9c8ff); hot = Hex(0xffffff); break;
                default: cool = Hex(0xb8530e); hot = Hex(0xffe4a0); break;   // G и двойная (главная звезда)
            }
        }

        /// <summary>Яркость поверхности (у коричневого карлика — тусклая).</summary>
        public static float SurfaceIntensity(StarSpectralClass c) => c switch
        {
            StarSpectralClass.BrownDwarf => 1.1f,
            StarSpectralClass.RedGiant => 1.9f,
            StarSpectralClass.WhiteDwarf => 3.4f,
            StarSpectralClass.Pulsar => 3.6f,
            StarSpectralClass.Magnetar => 3.2f,
            _ => 2.4f
        };

        /// <summary>Множитель размера звезды (карта, вид системы).</summary>
        public static float SizeMul(StarSpectralClass c) => c switch
        {
            StarSpectralClass.ClassM => 0.80f,
            StarSpectralClass.ClassK => 0.90f,
            StarSpectralClass.ClassG => 1.00f,
            StarSpectralClass.ClassA => 1.12f,
            StarSpectralClass.ClassB => 1.32f,
            StarSpectralClass.ClassO => 1.70f,
            StarSpectralClass.RedGiant => 2.00f,
            StarSpectralClass.WhiteDwarf => 0.45f,
            StarSpectralClass.BrownDwarf => 0.70f,
            StarSpectralClass.Binary => 0.95f,
            StarSpectralClass.WolfRayet => 0.85f,
            StarSpectralClass.Pulsar => 0.34f,
            StarSpectralClass.Magnetar => 0.36f,
            StarSpectralClass.Protostar => 0.55f,
            StarSpectralClass.PlanetaryNebula => 0.40f,
            _ => 1.1f
        };

        /// <summary>Экзотика: не обычная звезда главной последовательности (и не чёрная дыра).</summary>
        public static bool IsExotic(StarSpectralClass c) => c >= StarSpectralClass.ClassO;

        /// <summary>Могут ли у звезды быть обитаемые миры (столицы, вольные миры, гарантия жилой планеты).</summary>
        public static bool Habitable(StarSpectralClass c) => c switch
        {
            StarSpectralClass.BlackHole => false,
            StarSpectralClass.ClassO => false,
            StarSpectralClass.BrownDwarf => false,
            StarSpectralClass.WolfRayet => false,
            StarSpectralClass.Pulsar => false,
            StarSpectralClass.Magnetar => false,
            StarSpectralClass.Protostar => false,
            StarSpectralClass.PlanetaryNebula => false,
            _ => true
        };

        // ---------- Генерация ----------

        // Редкость (веса): обычные звёзды ~78%, необычные ~18%, чёрные дыры ~4%
        private static readonly (StarSpectralClass c, float w)[] Weights =
        {
            (StarSpectralClass.ClassM, 24f), (StarSpectralClass.ClassK, 20f), (StarSpectralClass.ClassG, 17f),
            (StarSpectralClass.ClassA, 10f), (StarSpectralClass.ClassB, 7f),
            (StarSpectralClass.ClassO, 2f), (StarSpectralClass.RedGiant, 4f), (StarSpectralClass.WhiteDwarf, 3f),
            (StarSpectralClass.BrownDwarf, 3f), (StarSpectralClass.Binary, 5f), (StarSpectralClass.WolfRayet, 1f),
            (StarSpectralClass.Pulsar, 1.5f), (StarSpectralClass.Magnetar, 0.8f), (StarSpectralClass.Protostar, 1.5f),
            (StarSpectralClass.PlanetaryNebula, 1.2f), (StarSpectralClass.BlackHole, 4f),
        };

        /// <summary>Случайный тип звезды; столица — только обычная жёлтая/оранжевая/белая.</summary>
        public static StarSpectralClass Roll(bool capital)
        {
            if (capital)
            {
                float r0 = Random.value;
                return r0 < 0.45f ? StarSpectralClass.ClassG : r0 < 0.8f ? StarSpectralClass.ClassK : StarSpectralClass.ClassA;
            }
            float sum = 0f;
            foreach (var w in Weights) sum += w.w;
            float r = Random.value * sum;
            foreach (var w in Weights)
            {
                if (r < w.w) return w.c;
                r -= w.w;
            }
            return StarSpectralClass.ClassM;
        }

        /// <summary>Сдвиг числа планет системы.</summary>
        public static int PlanetDelta(StarSpectralClass c) => c switch
        {
            StarSpectralClass.Binary => 2,
            StarSpectralClass.WhiteDwarf => -1,
            StarSpectralClass.Pulsar => -1,
            StarSpectralClass.Magnetar => -1,
            StarSpectralClass.Protostar => -2,
            _ => 0
        };

        /// <summary>Множители залежей: гелий-3 (энергия) и титан (минералы).</summary>
        public static void DepositMul(StarSpectralClass c, out float energy, out float minerals)
        {
            energy = 1f; minerals = 1f;
            switch (c)
            {
                case StarSpectralClass.ClassO: energy = 1.25f; break;
                case StarSpectralClass.RedGiant: minerals = 1.2f; break;
                case StarSpectralClass.BrownDwarf: energy = 2f; break;
                case StarSpectralClass.WolfRayet: energy = 1.5f; minerals = 1.5f; break;
                case StarSpectralClass.Pulsar: minerals = 1.3f; break;
                case StarSpectralClass.Magnetar: energy = 2f; minerals = 2f; break;
                case StarSpectralClass.PlanetaryNebula: energy = 1.5f; break;
            }
        }

        /// <summary>Тип планеты с учётом звезды: у необитаемых звёзд жилые миры становятся пустошами и раскалёнными.</summary>
        public static PlanetType AdjustPlanet(StarSpectralClass c, PlanetType t, int index)
        {
            if (c == StarSpectralClass.Protostar) return index % 2 == 0 ? PlanetType.Molten : PlanetType.Barren;
            if (Habitable(c)) return t;
            if (t == PlanetType.Continental) return PlanetType.Barren;
            if (t == PlanetType.Desert) return c == StarSpectralClass.BrownDwarf ? PlanetType.Barren : PlanetType.Molten;
            return t;
        }

        private static Color Hex(int v) => new Color((v >> 16 & 255) / 255f, (v >> 8 & 255) / 255f, (v & 255) / 255f, 1f);
    }
}
