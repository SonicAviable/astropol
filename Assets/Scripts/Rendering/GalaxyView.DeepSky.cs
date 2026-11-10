using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Глубина космоса под картой галактики — слои на разной высоте, при движении камеры плывут с разной скоростью:
    ///   • средние звёзды — между диском и дальними звёздами фона (точки, TwinklePoints);
    ///   • яркие звёзды с дифракционными лучами и далёкие галактики — глубоко под картой (GalaxyFarSprites);
    ///   • объём туманностей: число выборок по глубине зависит от настроек графики.
    /// Расположение детерминировано картой. Всё лежит в _nebulaRoot и прячется вместе с ним в режиме системы.
    /// </summary>
    public partial class GalaxyView
    {
        private Material _midStarsMat, _farSkyMat;

        /// <summary>Выборок по глубине в туманностях: «Низкое» — плоские (как раньше), «Среднее» — 3, выше — 4.</summary>
        private static float NebulaSteps()
        {
            if (!GraphicsQuality.PostFx) return 1f;
            return GraphicsQuality.ScaleIndex >= 4 ? 4f : 3f;
        }

        private void BuildDeepSky(float R)
        {
            if (_nebulaRoot == null || _generator == null) return;
            // Свой генератор: общий rng диска дальше расставляет астероидные поля — его последовательность не трогаем
            var rng = new System.Random(_generator.Systems.Count * 7919 + Mathf.RoundToInt(R * 13f));
            BuildMidStars(R, rng);
            BuildFarSprites(R, rng);
        }

        /// <summary>Средний слой звёзд: ближе дальних звёзд фона, поэтому при панораме обгоняет их.</summary>
        private void BuildMidStars(float R, System.Random rng)
        {
            const int count = 2600;
            var verts = new Vector3[count];
            var cols = new Color32[count];
            var idx = new int[count];
            for (int k = 0; k < count; k++)
            {
                verts[k] = new Vector3(((float)rng.NextDouble() - 0.5f) * R * 5f, -R * (0.1f + (float)rng.NextDouble() * 0.35f), ((float)rng.NextDouble() - 0.5f) * R * 5f);
                float b = 0.1f + Mathf.Pow((float)rng.NextDouble(), 4f) * 0.5f;
                Color c = Color.Lerp(new Color(0.75f, 0.82f, 1f), new Color(1f, 0.92f, 0.82f), (float)rng.NextDouble());
                cols[k] = new Color(c.r, c.g, c.b, b);
                idx[k] = k;
            }
            var mesh = new Mesh { name = "GalaxyMidStars", vertices = verts, colors32 = cols };
            mesh.SetIndices(idx, MeshTopology.Points, 0);
            mesh.bounds = new Bounds(new Vector3(0f, -R * 0.275f, 0f), new Vector3(R * 5f, R * 0.4f, R * 5f));
            var go = new GameObject("GalaxyMidStars");
            go.transform.SetParent(_nebulaRoot.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var twinkle = Resources.Load<Shader>("Shaders/TwinklePoints");
            _midStarsMat = new Material(twinkle != null ? twinkle : GetSpriteShader()) { renderQueue = 2932 };
            mr.sharedMaterial = _midStarsMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private static readonly Color[] FarStarTints =
        {
            new Color(0.62f, 0.74f, 1f),      // голубая
            new Color(0.86f, 0.91f, 1f),      // бело-голубая
            new Color(1f, 0.97f, 0.9f),       // белая
            new Color(1f, 0.88f, 0.66f),      // жёлтая
            new Color(1f, 0.7f, 0.46f),       // оранжевая
            new Color(1f, 0.52f, 0.4f)        // красная
        };

        /// <summary>Яркие звёзды с лучами и далёкие галактики — одна сетка билбордов, один вызов отрисовки.</summary>
        private void BuildFarSprites(float R, System.Random rng)
        {
            var shader = Resources.Load<Shader>("Shaders/GalaxyFarSprites");
            if (shader == null) return;
            const int stars = 70, galaxies = 3;      // далёкие галактики — лишь намёк, не россыпь спрайтов
            const float SpikeAngle = 0.26f;      // лучи у всех звёзд под одним углом — как на снимке телескопа
            int n = stars + galaxies;
            var verts = new List<Vector3>(n * 4);
            var uv0 = new List<Vector4>(n * 4);
            var uv1 = new List<Vector4>(n * 4);
            var cols = new List<Color>(n * 4);
            var tris = new int[n * 6];
            int made = 0;
            float U() => (float)rng.NextDouble();

            void Add(Vector3 p, float half, float type, float angle, float squash, Color c)
            {
                float seed = U();
                int v0 = made * 4, t0 = made * 6;
                for (int k = 0; k < 4; k++)
                {
                    verts.Add(p);
                    uv0.Add(new Vector4(k == 1 || k == 2 ? 1f : -1f, k >= 2 ? 1f : -1f, seed, 0f));
                    uv1.Add(new Vector4(half, type, angle, squash));
                    cols.Add(c);
                }
                tris[t0] = v0; tris[t0 + 1] = v0 + 1; tris[t0 + 2] = v0 + 2;
                tris[t0 + 3] = v0; tris[t0 + 4] = v0 + 2; tris[t0 + 5] = v0 + 3;
                made++;
            }

            for (int k = 0; k < stars; k++)
            {
                float ang = U() * Mathf.PI * 2f, dist = R * 2.2f * Mathf.Sqrt(U());
                float b = 0.28f + Mathf.Pow(U(), 3f) * 1.1f;         // почти все скромные, несколько — ярче порога Bloom
                Color c = FarStarTints[rng.Next(FarStarTints.Length)];
                c.a = b;
                Add(new Vector3(Mathf.Cos(ang) * dist, -R * (0.45f + U() * 0.55f), Mathf.Sin(ang) * dist),
                    R * (0.022f + 0.022f * b), 0f, SpikeAngle, 1f, c);
            }
            for (int k = 0; k < galaxies; k++)
            {
                // По кругу с разбросом — видны сквозь карту в разных её частях, а не кучей
                float ang = (k + U() * 0.8f) / galaxies * Mathf.PI * 2f, dist = R * (0.25f + 1.45f * Mathf.Sqrt(U()));
                Color c = Color.Lerp(new Color(0.7f, 0.8f, 1f), new Color(1f, 0.86f, 0.7f), U());
                c.a = 0.04f + U() * 0.06f;
                Add(new Vector3(Mathf.Cos(ang) * dist, -R * (0.9f + U() * 0.5f), Mathf.Sin(ang) * dist),
                    R * (0.05f + U() * 0.08f), 1f, U() * Mathf.PI, 0.25f + U() * 0.75f, c);
            }

            var mesh = new Mesh { name = "GalaxyFarSprites" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(new Vector3(0f, -R * 0.9f, 0f), new Vector3(R * 5f, R * 1.4f, R * 5f));

            var go = new GameObject("GalaxyFarSprites");
            go.transform.SetParent(_nebulaRoot.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            _farSkyMat = new Material(shader) { renderQueue = 2925 };      // позади дальних звёзд (2930) и туманностей
            mr.sharedMaterial = _farSkyMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
    }
}
