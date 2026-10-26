using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Поле боя: рельеф (аналитическая функция высоты — та же, что у меша), точки, укрытия, разрушаемые объекты,
    /// зоны высадки, лава и вода. Координаты «локальные» — относительно Origin (корень боя глубоко под галактикой).
    /// </summary>
    public sealed class GroundMap
    {
        public const float Half = 130f;

        public Vector3 Origin;
        public GroundBiome Biome;
        public readonly List<StrategicPoint> Points = new List<StrategicPoint>();
        public readonly CoverMap Cover = new CoverMap();
        public readonly List<Destructible> Destructibles = new List<Destructible>();
        /// <summary>Мины защитника (локальные координаты).</summary>
        public readonly List<Vector3> Mines = new List<Vector3>();
        public Vector3 AttackerBeacon, DefenderBeacon;
        public Destructible Hq, ShieldDome;
        public GameObject Terrain;

        // ---- Рельеф ----
        internal readonly List<(Vector2 c, float r)> Flats = new List<(Vector2, float)>();
        internal readonly List<(Vector2 c, float r, float depth)> Craters = new List<(Vector2, float, float)>();
        internal float NoiseOffX, NoiseOffZ;
        /// <summary>Река или лава: z = RiverZ(x), полуширина RiverHalf; броды — по X.</summary>
        internal float RiverBaseZ = -38f, RiverHalf = 5f;
        internal readonly List<float> Fords = new List<float>();
        internal bool HasRiver;

        public Vector3 ToWorld(float x, float z) => Origin + new Vector3(x, Height(x, z), z);
        public Vector3 ToWorld(Vector3 local) => ToWorld(local.x, local.z);
        public Vector3 ToLocal(Vector3 world) => world - Origin;

        /// <summary>Поставить мировую точку на землю.</summary>
        public Vector3 Snap(Vector3 world)
        {
            var l = world - Origin;
            return Origin + new Vector3(l.x, Height(l.x, l.z), l.z);
        }

        public bool Inside(Vector3 world, float margin = 4f)
        {
            var l = world - Origin;
            return Mathf.Abs(l.x) < Half - margin && Mathf.Abs(l.z) < Half - margin;
        }

        public Vector3 Clamp(Vector3 world, float margin = 6f)
        {
            var l = world - Origin;
            l.x = Mathf.Clamp(l.x, -Half + margin, Half - margin);
            l.z = Mathf.Clamp(l.z, -Half + margin, Half - margin);
            return ToWorld(l.x, l.z);
        }

        public float RiverZ(float x) => RiverBaseZ + Mathf.Sin(x * 0.031f) * 9f + Mathf.Sin(x * 0.077f + 1.3f) * 3f;

        private float FordFactor(float x)
        {
            float f = 0f;
            foreach (var fx in Fords) f = Mathf.Max(f, 1f - Mathf.Clamp01((Mathf.Abs(x - fx) - 5f) / 3f));
            return f;
        }

        /// <summary>Точка в русле реки/лавы (не на броде): для лавы — непроходимо и жжёт, для воды — опасно и медленно.</summary>
        public bool InRiver(Vector3 world, float extra = 0f)
        {
            if (!HasRiver) return false;
            var l = world - Origin;
            if (FordFactor(l.x) > 0.5f) return false;
            return Mathf.Abs(l.z - RiverZ(l.x)) < RiverHalf + extra;
        }

        public float Height(float x, float z)
        {
            var b = Biome;
            float h = (Mathf.PerlinNoise(x * b.HillScale + NoiseOffX, z * b.HillScale + NoiseOffZ) - 0.5f) * 2f * b.HillAmp
                    + (Mathf.PerlinNoise(x * 0.07f + NoiseOffZ, z * 0.07f + NoiseOffX) - 0.5f) * 0.7f;
            if (b.Dunes) h += Mathf.Sin(x * 0.09f + Mathf.PerlinNoise(x * 0.01f, z * 0.01f) * 6f) * 1.3f * Mathf.PerlinNoise(z * 0.02f + 3f, x * 0.02f);

            // Края карты приподняты — естественная рамка
            float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            h += Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Half - 22f, Half, edge)) * 7f;

            // Ровные площадки под точки, штаб и высадку
            foreach (var f in Flats)
            {
                float d = Vector2.Distance(new Vector2(x, z), f.c);
                if (d < f.r * 1.6f)
                {
                    float k = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(f.r, f.r * 1.6f, d));
                    h = Mathf.Lerp(h, 0.2f, k);
                }
            }

            // Кратеры: чаша с валом
            foreach (var c in Craters)
            {
                float d = Vector2.Distance(new Vector2(x, z), c.c) / c.r;
                if (d < 1.4f)
                {
                    if (d < 1f) h -= c.depth * (1f - d * d);
                    h += c.depth * 0.45f * Mathf.Exp(-(d - 1f) * (d - 1f) * 18f);
                }
            }

            // Русло
            if (HasRiver)
            {
                float dz = Mathf.Abs(z - RiverZ(x));
                float bed = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RiverHalf - 1f, RiverHalf + 4f, dz));
                h -= bed * Mathf.Lerp(1.8f, 0.25f, FordFactor(x));
            }
            return h;
        }

        public Vector3 Normal(float x, float z)
        {
            const float e = 0.8f;
            float hx = Height(x + e, z) - Height(x - e, z);
            float hz = Height(x, z + e) - Height(x, z - e);
            return new Vector3(-hx, 2f * e, -hz).normalized;
        }

        public StrategicPoint PointNear(Vector3 world, float r)
        {
            StrategicPoint best = null;
            float bd = r * r;
            foreach (var p in Points)
            {
                float d = (p.Pos - world).sqrMagnitude;
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }
    }
}
