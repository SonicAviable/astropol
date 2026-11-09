// Astropolity — цветная туманность региона галактики (квад под картой).
// Процедурные облака: fbm с искажением координат, медленно текут; два цвета смешиваются по второму шуму;
// край облака рваный (шум по радиусу). Аддитивно — облака светятся, как в Stellaris / Master of Orion.
// Объём: слой толщиной _Thickness, _Steps выборок по глубине вдоль луча взгляда (параллакс внутри облака);
// рельеф — подсветка склонов со стороны ядра галактики (_LightDir в координатах квада).
// _T, _Intensity, _Steps выставляет GalaxyView каждый кадр.
Shader "Astropolity/GalaxyNebula"
{
    Properties
    {
        _ColorA ("Color A", Color) = (0.2, 0.8, 0.8, 1)
        _ColorB ("Color B", Color) = (0.6, 0.3, 0.9, 1)
        _Seed ("Seed", Float) = 0
        _T ("Time", Float) = 0
        _Intensity ("Intensity", Float) = 1
        _Scale ("Noise scale", Float) = 3
        _Steps ("Depth steps (1-4)", Float) = 1
        _Thickness ("Slab thickness (world units)", Float) = 0
        _Relief ("Relief light", Float) = 1
        _LightDir ("Light dir (uv)", Vector) = (1, 0, 0, 0)
        _LightColor ("Light color", Color) = (1, 0.9, 0.76, 1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent-50" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 view : TEXCOORD1; };

            fixed4 _ColorA, _ColorB, _LightColor;
            float _Seed, _T, _Intensity, _Scale, _Steps, _Thickness, _Relief;
            float4 _LightDir;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                // Луч от камеры к точке квада в пространстве объекта: xy — в долях квада (как uv),
                // z — в мировых единицах вглубь (масштаб квада по z = 1)
                float3 camObj = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1.0)).xyz;
                o.view = v.vertex.xyz - camObj;
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }
            float Fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int k = 0; k < 5; k++) { v += a * Noise(p); p = p * 2.02 + 13.7; a *= 0.5; }
                return v;
            }
            // Четыре октавы — для выборок по глубине (их несколько на пиксель)
            float Fbm4(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int k = 0; k < 4; k++) { v += a * Noise(p); p = p * 2.02 + 13.7; a *= 0.5; }
                return v * 1.0333;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv * _Scale + _Seed;

                // течение: координаты медленно искажаются
                float2 q = float2(Fbm(p + float2(0.0, _T * 0.012)), Fbm(p + float2(5.2, 1.3) - _T * 0.010));
                float2 w = p + 2.2 * q + float2(_T * 0.006, -_T * 0.004);
                float m = Fbm(w * 1.7 + 3.1);
                // рваный край (тот же шум, что и течение, — без лишней выборки)
                float edgeN = (q.x - 0.5) * 0.7;

                // Объём: облако — слой толщиной _Thickness вокруг квада. Луч взгляда пересекает его на нескольких
                // глубинах, выборки сдвигаются по наклону луча — при движении камеры слои плывут друг относительно друга
                float vz = (i.view.z >= 0.0 ? 1.0 : -1.0) * max(abs(i.view.z), 0.001);
                float2 sh = i.view.xy / vz * (_Thickness * 0.5);
                sh *= min(1.0, 0.14 / max(length(sh), 1e-5));              // не дальше 0,14 квада
                sh *= saturate(abs(i.view.z) / (_Thickness + 0.001));      // камера у самой плоскости — без сдвига

                int steps = (int)clamp(_Steps, 1.0, 4.0);
                float dens = 0.0, wsum = 0.0, nAvg = 0.0;
                for (int s = 0; s < 4; s++)
                {
                    if (s >= steps) break;
                    float f = steps > 1 ? (float)s / (float)(steps - 1) * 2.0 - 1.0 : 0.0;   // −1 верх слоя … +1 низ
                    float2 off = sh * f;
                    float r = length(i.uv + off - 0.5) * 2.0;
                    float mask = 1.0 - smoothstep(0.35, 1.0, r + edgeN);
                    // рисунок меняется с глубиной; к верху и низу слоя облако редеет — линза, а не плита
                    float n = Fbm4(w + off * _Scale + f * float2(0.83, -0.57));
                    float d = saturate(n * 1.6 - 0.45 - 0.12 * f * f) * mask;
                    d = d * d * (3.0 - 2.0 * d);
                    float prof = 1.0 - 0.4 * f * f;
                    dens += d * prof;
                    nAvg += n * prof;
                    wsum += prof;
                }
                dens = dens / wsum * (1.0 + 0.07 * (steps - 1));           // усреднение слоёв чуть гасит пики
                nAvg /= wsum;

                // прожилки ярче
                float vein = pow(saturate(1.0 - abs(m - 0.5) * 3.0), 3.0) * 0.5;

                // Рельеф: свет идёт от ядра галактики — обращённые к нему склоны облаков светлее, обратные темнее
                float lit = 0.0;
                if (steps > 1)
                    lit = (nAvg - Fbm4(w + _LightDir.xy * 0.22)) * 2.6 * _Relief;
                float shade = clamp(1.0 + lit, 0.65, 1.9);

                float3 col = lerp(_ColorA.rgb, _ColorB.rgb, saturate(m * 1.4 - 0.2));
                float3 o = col * (dens * 0.55 + vein * dens * 1.2) * shade
                         + _LightColor.rgb * (saturate(lit) * dens * 0.12);

                // у края квада сдвинутые выборки не должны обрываться ступенькой
                float2 e = min(i.uv, 1.0 - i.uv);
                o *= smoothstep(0.0, 0.05, min(e.x, e.y));
                return fixed4(o * _Intensity, 1.0);
            }
            ENDCG
        }
    }
}
