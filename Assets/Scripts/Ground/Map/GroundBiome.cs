using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Биом поля боя по типу планеты: палитра земли и неба, рельеф, растительность, погода.
    ///   • Континентальная — трава, леса (укрытие), река с бродами.
    ///   • Пустынная — дюны, каньоны, песчаные бури (обзор падает до 25 м на 30 с).
    ///   • Вулканическая — лавовые реки (непроходимы, жгут рядом), обсидиановые гряды (тяжёлое укрытие), пепел (дальность −20%).
    ///   • Безжизненная — кратеры (готовые укрытия), низкая гравитация (ошмётки летят выше и медленнее).
    /// </summary>
    public sealed class GroundBiome
    {
        public PlanetType Type;
        public Color GroundA, GroundB, GroundC, Rock, Sky, Fog, Ambient, Sun;
        public float FogStart = 90f, FogEnd = 260f;
        public float HillAmp = 3f, HillScale = 0.018f;
        public float Trees, Rocks, Craters;
        public bool River, LavaRiver, Dunes, Sandstorms, Ash;
        /// <summary>Множитель гравитации для ошмётков (низкая гравитация на безжизненных мирах).</summary>
        public float Gravity = 1f;
        /// <summary>Множитель дальности стрельбы (пепел).</summary>
        public float RangeMult = 1f;
        public float SunIntensity = 1.2f;
        public Vector3 SunEuler = new Vector3(48f, -35f, 0f);

        public static string Name(PlanetType t) => t switch
        {
            PlanetType.Desert => Loc.T("пустыня"),
            PlanetType.Molten => Loc.T("вулканический мир"),
            PlanetType.Barren => Loc.T("безжизненный мир"),
            _ => Loc.T("континентальный мир")
        };

        public static GroundBiome For(PlanetType t)
        {
            switch (t)
            {
                case PlanetType.Desert:
                    return new GroundBiome
                    {
                        Type = t, GroundA = new Color(0.78f, 0.62f, 0.38f), GroundB = new Color(0.66f, 0.48f, 0.28f), GroundC = new Color(0.86f, 0.74f, 0.5f),
                        Rock = new Color(0.55f, 0.38f, 0.25f), Sky = new Color(0.78f, 0.62f, 0.45f), Fog = new Color(0.82f, 0.66f, 0.46f),
                        Ambient = new Color(0.55f, 0.46f, 0.36f), Sun = new Color(1f, 0.9f, 0.72f), SunIntensity = 1.45f,
                        HillAmp = 4.5f, HillScale = 0.02f, Rocks = 0.6f, Dunes = true, Sandstorms = true, FogStart = 110f, FogEnd = 300f
                    };
                case PlanetType.Molten:
                    return new GroundBiome
                    {
                        Type = t, GroundA = new Color(0.16f, 0.13f, 0.12f), GroundB = new Color(0.25f, 0.18f, 0.15f), GroundC = new Color(0.09f, 0.08f, 0.08f),
                        Rock = new Color(0.07f, 0.06f, 0.08f), Sky = new Color(0.22f, 0.09f, 0.06f), Fog = new Color(0.3f, 0.12f, 0.07f),
                        Ambient = new Color(0.42f, 0.22f, 0.16f), Sun = new Color(1f, 0.55f, 0.35f), SunIntensity = 0.9f,
                        HillAmp = 3.5f, Rocks = 1.1f, LavaRiver = true, Ash = true, RangeMult = 0.8f, FogStart = 60f, FogEnd = 200f
                    };
                case PlanetType.Barren:
                    return new GroundBiome
                    {
                        Type = t, GroundA = new Color(0.46f, 0.46f, 0.48f), GroundB = new Color(0.36f, 0.36f, 0.39f), GroundC = new Color(0.56f, 0.55f, 0.55f),
                        Rock = new Color(0.3f, 0.3f, 0.32f), Sky = new Color(0.03f, 0.04f, 0.07f), Fog = new Color(0.08f, 0.09f, 0.12f),
                        Ambient = new Color(0.32f, 0.33f, 0.38f), Sun = new Color(0.95f, 0.97f, 1f), SunIntensity = 1.6f, SunEuler = new Vector3(32f, -60f, 0f),
                        HillAmp = 2.2f, Rocks = 0.7f, Craters = 1f, Gravity = 0.38f, FogStart = 160f, FogEnd = 420f
                    };
                default:
                    return new GroundBiome
                    {
                        Type = PlanetType.Continental, GroundA = new Color(0.3f, 0.45f, 0.2f), GroundB = new Color(0.22f, 0.36f, 0.16f), GroundC = new Color(0.45f, 0.42f, 0.26f),
                        Rock = new Color(0.42f, 0.42f, 0.4f), Sky = new Color(0.55f, 0.68f, 0.8f), Fog = new Color(0.6f, 0.7f, 0.78f),
                        Ambient = new Color(0.45f, 0.5f, 0.55f), Sun = new Color(1f, 0.96f, 0.88f), SunIntensity = 1.25f,
                        HillAmp = 3f, Trees = 1f, Rocks = 0.4f, River = true, FogStart = 120f, FogEnd = 320f
                    };
            }
        }
    }
}
