using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Тайловые детальные текстуры земли (повтор каждые ~6 м): серая «множительная» карта цвета (0,5 — без изменений)
    /// и карта нормалей. Безжизненный мир — щебень и реголит, пустыня — рябь песка, вулканический — трещины базальта,
    /// континентальный — земля с травяными штрихами и камешками. Вся генерация бесшовная (периодический шум).
    /// </summary>
    public static class GroundDetail
    {
        private const int N = 256;

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 2147483647;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0x7fffffff) / (float)0x7fffffff;
            }
        }

        /// <summary>Периодический сглаженный шум (период p клеток).</summary>
        private static float Value(float x, float y, int p, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            int x0 = ((xi % p) + p) % p, y0 = ((yi % p) + p) % p, x1 = (x0 + 1) % p, y1 = (y0 + 1) % p;
            float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed), c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float Fbm(float u, float v, int basePeriod, int oct, int seed)
        {
            float s = 0f, amp = 0.5f, norm = 0f;
            int p = basePeriod;
            for (int i = 0; i < oct; i++)
            {
                s += Value(u * p, v * p, p, seed + i * 31) * amp;
                norm += amp; amp *= 0.5f; p *= 2;
            }
            return s / norm;
        }

        /// <summary>Периодический клеточный шум: f1 — до ближайшей точки, f2 — до второй (края = f2 − f1).</summary>
        private static void Worley(float u, float v, int p, int seed, out float f1, out float f2, out float id)
        {
            float x = u * p, y = v * p;
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            f1 = f2 = 9f; id = 0f;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = xi + dx, cy = yi + dy;
                    int wx = ((cx % p) + p) % p, wy = ((cy % p) + p) % p;
                    float px = cx + Hash(wx, wy, seed), py = cy + Hash(wx, wy, seed + 7);
                    float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                    if (d < f1) { f2 = f1; f1 = d; id = Hash(wx, wy, seed + 13); }
                    else if (d < f2) f2 = d;
                }
        }

        /// <summary>Высота и затенение (0–1) микрорельефа для биома.</summary>
        private static void Sample(PlanetType t, float u, float v, int seed, out float h, out float tone)
        {
            switch (t)
            {
                case PlanetType.Desert:
                {
                    // Рябь песка, изогнутая шумом, и редкие камешки
                    float warp = Fbm(u, v, 4, 3, seed) * 2.5f;
                    float ripple = Mathf.Sin((v * 18f + warp) * Mathf.PI * 2f) * 0.5f + 0.5f;
                    Worley(u, v, 22, seed + 5, out float f1, out _, out float id);
                    float pebble = id > 0.82f ? Mathf.Clamp01(1f - f1 * 3.2f) : 0f;
                    float grain = Value(u * 128f, v * 128f, 128, seed + 9);
                    h = ripple * 0.55f + pebble * 0.8f + grain * 0.12f;
                    tone = 0.5f + (ripple - 0.5f) * 0.14f + (grain - 0.5f) * 0.12f - pebble * 0.12f;
                    break;
                }
                case PlanetType.Molten:
                {
                    // Плиты базальта, разделённые тёмными трещинами
                    Worley(u, v, 9, seed, out float f1, out float f2, out float id);
                    float crack = Mathf.Clamp01((f2 - f1) * 6f);
                    float rough = Fbm(u, v, 16, 4, seed + 3);
                    h = crack * 0.7f + rough * 0.3f;
                    tone = 0.5f * Mathf.Lerp(0.35f, 1f, crack) + (rough - 0.5f) * 0.18f + (id - 0.5f) * 0.1f;
                    break;
                }
                case PlanetType.Barren:
                {
                    // Щебень разного размера в пыли реголита
                    Worley(u, v, 14, seed, out float f1, out float f2, out float id);
                    Worley(u, v, 38, seed + 21, out float g1, out _, out float id2);
                    float big = id > 0.45f ? Mathf.Clamp01(1f - f1 * 2.2f) : 0f;
                    float small = id2 > 0.5f ? Mathf.Clamp01(1f - g1 * 2.6f) : 0f;
                    float dust = Fbm(u, v, 8, 5, seed + 4);
                    h = Mathf.Max(big, small * 0.6f) + dust * 0.25f;
                    float edge = Mathf.Clamp01((f2 - f1) * 4f);
                    tone = 0.5f + (dust - 0.5f) * 0.25f + big * (id - 0.5f) * 0.4f - (1f - edge) * big * 0.06f + small * 0.05f;
                    break;
                }
                default:
                {
                    // Почва, примятая трава штрихами, мелкие камни
                    float soil = Fbm(u, v, 8, 5, seed);
                    float blades = Value(u * 96f, v * 24f, 96, seed + 2) * Value(u * 24f, v * 96f, 96, seed + 3);
                    Worley(u, v, 26, seed + 6, out float f1, out _, out float id);
                    float stone = id > 0.88f ? Mathf.Clamp01(1f - f1 * 3f) : 0f;
                    h = soil * 0.5f + blades * 0.35f + stone * 0.7f;
                    tone = 0.5f + (soil - 0.5f) * 0.3f + (blades - 0.25f) * 0.2f + stone * 0.08f;
                    break;
                }
            }
        }

        public static void Make(PlanetType type, int seed, out Texture2D albedo, out Texture2D normal)
        {
            var hgt = new float[N * N];
            var col = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    Sample(type, x / (float)N, y / (float)N, seed, out float h, out float tone);
                    hgt[y * N + x] = h;
                    float g = Mathf.Clamp01(tone);
                    col[y * N + x] = new Color(g, g, g, 1f);
                }
            albedo = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "GroundDetailAlbedo", wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.DontSave, anisoLevel = 8 };
            albedo.SetPixels(col);
            albedo.Apply(true);

            // Нормали из высоты (бесшовно: соседи по кругу). xy в R/G, A = 1: URP на ПК читает x как A×R, на мобильных — как R
            float strength = type == PlanetType.Desert ? 2.2f : type == PlanetType.Molten ? 3.4f : 3f;
            var nrm = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float l = hgt[y * N + (x + N - 1) % N], r = hgt[y * N + (x + 1) % N];
                    float d = hgt[((y + N - 1) % N) * N + x], u = hgt[((y + 1) % N) * N + x];
                    var n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                    float nx = n.x * 0.5f + 0.5f, ny = n.y * 0.5f + 0.5f;
                    nrm[y * N + x] = new Color(nx, ny, n.z * 0.5f + 0.5f, 1f);
                }
            normal = new Texture2D(N, N, TextureFormat.RGBA32, true, true) { name = "GroundDetailNormal", wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.DontSave, anisoLevel = 8 };
            normal.SetPixels(nrm);
            normal.Apply(true);
        }

        /// <summary>Карта нормалей всей карты (средний масштаб: валы воронок, колеи, бугры) из функции высоты.</summary>
        public static Texture2D MacroNormal(GroundMap map)
        {
            const int M = 512;
            var px = new Color[M * M];
            float step = GroundMap.Half * 2f / (M - 1);
            var hgt = new float[M * M];
            for (int y = 0; y < M; y++)
                for (int x = 0; x < M; x++)
                {
                    float lx = -GroundMap.Half + x * step, lz = -GroundMap.Half + y * step;
                    hgt[y * M + x] = map.Height(lx, lz) + (Mathf.PerlinNoise(lx * 0.9f, lz * 0.9f) - 0.5f) * 0.12f;
                }
            for (int y = 0; y < M; y++)
                for (int x = 0; x < M; x++)
                {
                    float l = hgt[y * M + Mathf.Max(0, x - 1)], r = hgt[y * M + Mathf.Min(M - 1, x + 1)];
                    float d = hgt[Mathf.Max(0, y - 1) * M + x], u = hgt[Mathf.Min(M - 1, y + 1) * M + x];
                    // Геометрия уже даёт крупный наклон — здесь только мелкая часть
                    var n = new Vector3((l - r) / (2f * step) * 0.6f, (d - u) / (2f * step) * 0.6f, 1f).normalized;
                    float nx = n.x * 0.5f + 0.5f, ny = n.y * 0.5f + 0.5f;
                    px[y * M + x] = new Color(nx, ny, n.z * 0.5f + 0.5f, 1f);
                }
            var tex = new Texture2D(M, M, TextureFormat.RGBA32, true, true) { name = "GroundMacroNormal", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }
    }
}
