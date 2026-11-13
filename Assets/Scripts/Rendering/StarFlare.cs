using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Процедурный блик объектива у объёмной звезды (URP Lens Flare, без текстур): ореол, анаморфная полоса
    /// и пара шестиугольных «призраков». Сила — по размеру звезды на экране: на дальнем зуме, где звёзд много
    /// и они мелкие, блики гаснут, вблизи раскрываются. Выключается вместе с постобработкой.
    /// </summary>
    public sealed class StarFlare : MonoBehaviour
    {
        private const float MinPx = 14f;   // радиус звезды на экране, с которого блик начинает проявляться
        private const float FullPx = 70f;  // и с которого он в полную силу
        private const float MaxIntensity = 1f;

        private static readonly Dictionary<int, LensFlareDataSRP> s_data = new Dictionary<int, LensFlareDataSRP>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => s_data.Clear();

        private LensFlareComponentSRP _flare;
        private float _radius;
        private float _shown;

        /// <summary>Вешает блик на объект звезды; hot — горячий цвет звезды (sRGB), radius — мировой радиус сферы.</summary>
        public static void Attach(GameObject star, Color hot, float radius)
        {
            if (star == null) return;
            var f = star.AddComponent<StarFlare>();
            f._radius = radius;
            f._flare = star.AddComponent<LensFlareComponentSRP>();
            f._flare.lensFlareData = Data(hot);
            f._flare.intensity = 0f;
            f._flare.scale = 1f;
            f._flare.useOcclusion = false;
            f._flare.attenuationByLightShape = false;
            f._flare.allowOffScreen = false;
        }

        private static LensFlareDataSRP Data(Color hot)
        {
            int key = Mathf.RoundToInt(hot.r * 31f) * 1024 + Mathf.RoundToInt(hot.g * 31f) * 32 + Mathf.RoundToInt(hot.b * 31f);
            if (s_data.TryGetValue(key, out var d) && d != null) return d;

            Color cool = new Color(0.55f, 0.72f, 1f);
            d = ScriptableObject.CreateInstance<LensFlareDataSRP>();
            d.hideFlags = HideFlags.DontSave;
            d.elements = new[]
            {
                // Слабый мягкий ореол (сильнее — ложится белым пятном поверх поверхности звезды)
                Circle(0f, 0.7f, Vector2.one, hot, 0.12f, 0.1f),
                // Призраки вдоль оси к центру экрана
                Hex(0.55f, 0.10f, Color.Lerp(hot, cool, 0.3f), 0.10f),
                Hex(1.10f, 0.18f, Color.Lerp(hot, cool, 0.7f), 0.06f),
                Circle(1.55f, 0.07f, Vector2.one, hot, 0.08f, 0.2f),
            };
            s_data[key] = d;
            return d;
        }

        private static LensFlareDataElementSRP Circle(float pos, float scale, Vector2 size, Color tint, float intensity, float falloff)
        {
            var e = new LensFlareDataElementSRP();
            e.flareType = SRPLensFlareType.Circle;
            e.position = pos;
            e.uniformScale = scale;
            e.sizeXY = size;
            e.tint = tint;
            e.localIntensity = intensity;
            e.fallOff = falloff;
            e.blendMode = SRPLensFlareBlendMode.Additive;
            e.modulateByLightColor = false;
            return e;
        }

        private static LensFlareDataElementSRP Hex(float pos, float scale, Color tint, float intensity)
        {
            var e = new LensFlareDataElementSRP();
            e.flareType = SRPLensFlareType.Polygon;
            e.sideCount = 6;
            e.position = pos;
            e.uniformScale = scale;
            e.sizeXY = Vector2.one;
            e.tint = tint;
            e.localIntensity = intensity;
            e.fallOff = 0.6f;
            e.edgeOffset = 0.15f;
            e.blendMode = SRPLensFlareBlendMode.Additive;
            e.modulateByLightColor = false;
            return e;
        }

        private void LateUpdate()
        {
            if (_flare == null) return;
            float want = 0f;
            var cam = Camera.main;
            if (cam != null && GraphicsQuality.PostFx)
            {
                // Радиус звезды на экране в пикселях
                Vector3 p = transform.position;
                float dist = Vector3.Distance(cam.transform.position, p);
                if (dist > 0.01f)
                {
                    float worldR = _radius * transform.lossyScale.x;
                    float px = worldR / (dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) * (cam.pixelHeight * 0.5f);
                    want = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(MinPx, FullPx, px)) * MaxIntensity;
                }
            }
            _shown = Mathf.MoveTowards(_shown, want, Time.unscaledDeltaTime * 2.5f);
            _flare.intensity = _shown;
            _flare.enabled = _shown > 0.001f;
        }
    }
}
