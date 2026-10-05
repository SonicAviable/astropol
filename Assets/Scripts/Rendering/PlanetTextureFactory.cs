using UnityEngine;

namespace StellarisClone.Rendering
{
    public enum PlanetVisualType
    {
        Continental, // Землеподобная (океаны, суша, облака)
        Desert,      // Пустынная/Марсианская (каньоны, пески)
        Ocean,       // Глубокий океан с рифами
        Ice,         // Ледяная/Европа (трещины льда, снег)
        GasGiant,    // Юпитер/Сатурн (атмосферные полосы, бури)
        Molten       // Лавовый мир (трещины магмы, темный базальт)
    }

    public static class PlanetTextureFactory
    {
        public static Texture2D GeneratePlanetTexture(PlanetVisualType type, int seed)
        {
            int width = 256;
            int height = 128;
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;

            Random.InitState(seed);
            float offsetX = Random.Range(0f, 1000f);
            float offsetY = Random.Range(0f, 1000f);

            for (int y = 0; y < height; y++)
            {
                float v = (float)y / height;
                for (int x = 0; x < width; x++)
                {
                    float u = (float)x / width;

                    // Шум Перлина для ландшафта
                    float scale = type == PlanetVisualType.GasGiant ? 6f : 4f;
                    float n1 = Mathf.PerlinNoise(u * scale + offsetX, v * scale + offsetY);
                    float n2 = Mathf.PerlinNoise(u * scale * 2.5f + offsetX + 10f, v * scale * 2.5f + offsetY + 10f) * 0.5f;
                    float combinedNoise = Mathf.Clamp01(n1 + n2);

                    Color pixelColor = GetColorForType(type, combinedNoise, v, u + offsetX);
                    tex.SetPixel(x, y, pixelColor);
                }
            }

            tex.Apply();
            return tex;
        }

        private static Color GetColorForType(PlanetVisualType type, float noise, float vCoord, float uCoord)
        {
            switch (type)
            {
                case PlanetVisualType.Continental:
                    if (noise < 0.42f) return new Color(0.08f, 0.22f, 0.45f); // Глубокий океан
                    if (noise < 0.48f) return new Color(0.15f, 0.45f, 0.65f); // Мелководье
                    if (noise < 0.68f) return new Color(0.18f, 0.48f, 0.22f); // Леса и равнины
                    if (noise < 0.82f) return new Color(0.48f, 0.42f, 0.25f); // Горы
                    return new Color(0.9f, 0.95f, 1.0f);                      // Снежные пики

                case PlanetVisualType.Desert:
                    Color sand = new Color(0.85f, 0.55f, 0.30f);
                    Color rock = new Color(0.55f, 0.28f, 0.15f);
                    return Color.Lerp(rock, sand, noise);

                case PlanetVisualType.Ocean:
                    Color deep = new Color(0.03f, 0.15f, 0.35f);
                    Color shallow = new Color(0.08f, 0.55f, 0.65f);
                    return Color.Lerp(deep, shallow, Mathf.Pow(noise, 1.5f));

                case PlanetVisualType.Ice:
                    Color darkIce = new Color(0.55f, 0.70f, 0.85f);
                    Color brightSnow = new Color(0.95f, 0.98f, 1.0f);
                    return Color.Lerp(darkIce, brightSnow, noise);

                case PlanetVisualType.GasGiant:
                    // Горизонтальные полосы атмосферы
                    float bands = Mathf.Sin(vCoord * 35f + noise * 6f);
                    bands = (bands + 1f) * 0.5f;
                    Color bandA = new Color(0.82f, 0.65f, 0.45f); // Кремово-песочный
                    Color bandB = new Color(0.62f, 0.35f, 0.22f); // Ржаво-коричневый
                    return Color.Lerp(bandA, bandB, bands);

                case PlanetVisualType.Molten:
                    if (noise > 0.65f) return new Color(1.0f, 0.35f, 0.05f); // Лавовые разломы
                    Color basaltDark = new Color(0.12f, 0.10f, 0.12f);
                    Color basaltLight = new Color(0.25f, 0.18f, 0.20f);
                    return Color.Lerp(basaltDark, basaltLight, noise);

                default:
                    return Color.gray;
            }
        }
    }
}