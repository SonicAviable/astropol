using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    public enum StarSpectralClass { ClassM, ClassK, ClassG, ClassA, ClassB, BlackHole }

    [Serializable]
    public class StarSystem
    {
        public int Id;
        public string Name;
        public Vector3 Position;
        public StarSpectralClass SpectralClass;
        public List<int> ConnectedSystemIds = new List<int>();
        public List<PlanetData> Planets = new List<PlanetData>();
        public bool HasGeneratedPlanets;

        public int OwnerId = -1;
        public bool HasStarbase;
        /// <summary>Система изучена игроком (видны планеты, можно строить форпост).</summary>
        public bool IsSurveyed = false;
        /// <summary>
        /// Какие империи ИИ изучили систему (бит на номер владельца). Разведка у каждой империи своя:
        /// чужая не «закрывает» систему ни для вас, ни для другого ИИ.
        /// </summary>
        public int AISurveyMask;

        public bool IsSurveyedBy(int owner)
            => owner == 0 ? IsSurveyed : owner > 0 && owner < 31 && (AISurveyMask & (1 << owner)) != 0;

        public void MarkSurveyedBy(int owner)
        {
            if (owner == 0) IsSurveyed = true;
            else if (owner > 0 && owner < 31) AISurveyMask |= 1 << owner;
        }

        /// <summary>Система стала чьей-то территорией — её знают все.</summary>
        public void MarkSurveyedByAll()
        {
            IsSurveyed = true;
            AISurveyMask = ~1;
        }

        public int TotalEnergy { get; private set; }
        public int TotalMinerals { get; private set; }
        public int HarvestedEnergy { get; private set; }
        public int HarvestedMinerals { get; private set; }

        public int PotentialEnergy => Mathf.Max(0, TotalEnergy - HarvestedEnergy);
        public int PotentialMinerals => Mathf.Max(0, TotalMinerals - HarvestedMinerals);

        public StarSystem(int id, string name, Vector3 position, StarSpectralClass spectralClass)
        {
            Id = id; Name = name; Position = position; SpectralClass = spectralClass;
        }

        public void GeneratePlanets()
        {
            if (HasGeneratedPlanets) return;

            int count = UnityEngine.Random.Range(3, 7);
            float currentRadius = 8f;

            TotalEnergy = 0;
            TotalMinerals = 0;

            for (int i = 0; i < count; i++)
            {
                currentRadius += UnityEngine.Random.Range(5f, 9f);

                // Тип планеты: с гарантией хотя бы одной жилой
                PlanetType type;
                if (i == 0)
                    type = UnityEngine.Random.value < 0.5f ? PlanetType.Continental : PlanetType.Desert;
                else
                    type = (PlanetType)UnityEngine.Random.Range(0, 5);

                float speed = UnityEngine.Random.Range(8f, 22f) / (i + 1);
                float size = type == PlanetType.GasGiant
                    ? UnityEngine.Random.Range(2.2f, 3.0f)
                    : UnityEngine.Random.Range(1.1f, 1.7f);

                Color col = type switch
                {
                    PlanetType.Continental => new Color(0.2f, 0.7f, 0.3f),
                    PlanetType.Desert      => new Color(0.85f, 0.75f, 0.35f),
                    PlanetType.Molten      => new Color(0.85f, 0.25f, 0.1f),
                    PlanetType.GasGiant    => new Color(0.8f, 0.5f, 0.85f),
                    _                      => new Color(0.5f, 0.5f, 0.55f)
                };

                int energy   = UnityEngine.Random.value > 0.4f ? UnityEngine.Random.Range(2, 6) : 0;
                int minerals = UnityEngine.Random.value > 0.3f ? UnityEngine.Random.Range(2, 8) : 0;

                TotalEnergy += energy;
                TotalMinerals += minerals;

                Planets.Add(new PlanetData(PlanetNames.Make(Name, i, type),
                    type, currentRadius, speed, size, col, energy, minerals));
            }
            HasGeneratedPlanets = true;
            LinkPlanets();
            RecalculateHarvest();
        }

        /// <summary>Проставить планетам ссылку на систему (владелец, принадлежность).</summary>
        public void LinkPlanets()
        {
            foreach (var p in Planets) { p.ParentSystem = this; PlanetNames.MarkUsed(p.Name); }
        }

        /// <summary>Пересчитать запасы системы по планетам (после загрузки).</summary>
        public void RestoreDerivedTotals()
        {
            LinkPlanets();
            TotalEnergy = 0;
            TotalMinerals = 0;
            foreach (var p in Planets)
            {
                TotalEnergy += p.EnergyDeposit;
                TotalMinerals += p.MineralDeposit;
            }
            RecalculateHarvest();
        }

        public void RecalculateHarvest()
        {
            HarvestedEnergy = 0;
            HarvestedMinerals = 0;

            for (int i = 0; i < Planets.Count; i++)
            {
                if (Planets[i].HasMiningStation)
                {
                    HarvestedEnergy += Planets[i].EnergyDeposit;
                    HarvestedMinerals += Planets[i].MineralDeposit;
                }
            }
        }

    }

    public class Hyperlane
    {
        public int SystemA;
        public int SystemB;

        public Hyperlane(int a, int b)
        {
            SystemA = Mathf.Min(a, b);
            SystemB = Mathf.Max(a, b);
        }

        public override bool Equals(object obj)
            => obj is Hyperlane o && SystemA == o.SystemA && SystemB == o.SystemB;

        public override int GetHashCode() => HashCode.Combine(SystemA, SystemB);
    }

    /// <summary>
    /// Имена планет как у настоящих экзопланет: имя звезды и строчная буква по порядку орбит
    /// («Дата b», «HD 40307 c»). Часть миров — особенно пригодные для жизни — носит собственное имя
    /// в духе имён, которые МАС присваивает экзопланетам. Собственные имена в партии не повторяются.
    /// </summary>
    public static class PlanetNames
    {
        private static readonly string[] Proper =
        {
            "Дагон", "Галилей", "Браге", "Янссен", "Харриот", "Липперсгей", "Димидий", "Тадмор", "Арион", "Аркас",
            "Кихот", "Росинант", "Санчо", "Саффар", "Самх", "Маджрити", "Смертриос", "Фестиада", "Веритате", "Орбитар",
            "Эгир", "Аматеру", "Хайтан", "Сараби", "Лаваркс", "Ахра", "Тумбаго", "Ванг", "Набу", "Кемуру",
            "Пирмах", "Исагель", "Айтхан", "Дзалнава", "Эйрини", "Аумин", "Тапуни", "Мелуна", "Ганимедея", "Иоланта",
            "Новая Каледония", "Элизиум", "Аркадия", "Гиперион", "Персефона", "Церера", "Кассиопея", "Мнемозина",
            "Таласса", "Ниобея", "Ифигения", "Эвридика", "Пандора", "Калипсо", "Феакия", "Итака", "Тирея", "Мириада"
        };

        private static readonly HashSet<string> _used = new HashSet<string>();

        public static void Reset() => _used.Clear();

        /// <summary>Имя для заголовка заглавными, но буква орбиты остаётся строчной: «ДАТА b».</summary>
        public static string Title(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int sp = name.LastIndexOf(' ');
            if (sp > 0 && sp == name.Length - 2 && char.IsLower(name[sp + 1]))
                return name.Substring(0, sp).ToUpper() + " " + name[sp + 1];
            return name.ToUpper();
        }

        /// <summary>Имя уже занято (планеты из сохранения) — новые планеты его не получат.</summary>
        public static void MarkUsed(string name) { if (!string.IsNullOrEmpty(name)) _used.Add(name); }

        public static string Designation(string star, int orbitIndex) =>
            $"{star} {(char)('b' + Mathf.Clamp(orbitIndex, 0, 24))}";

        public static string Make(string star, int orbitIndex, PlanetType type)
        {
            bool habitable = type == PlanetType.Continental || type == PlanetType.Desert;
            float chance = habitable ? 0.45f : type == PlanetType.GasGiant ? 0.2f : 0.08f;
            if (UnityEngine.Random.value < chance && _used.Count < Proper.Length)
            {
                for (int tries = 0; tries < 8; tries++)
                {
                    string n = Proper[UnityEngine.Random.Range(0, Proper.Length)];
                    if (_used.Add(n)) return n;
                }
            }
            return Designation(star, orbitIndex);
        }
    }
}
