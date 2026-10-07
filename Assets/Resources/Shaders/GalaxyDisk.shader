// Astropolity — живой диск галактики. Текстура плотности (по реальным системам) + поверх неё:
//   • пыль рукавов медленно закручивается (дифференциальное вращение: внутри быстрее, снаружи медленнее);
//     два слоя шума со сдвигом во времени плавно сменяют друг друга, чтобы рисунок не «накручивался» бесконечно;
//   • ядро мягко дышит светом.
// _T, _Color (затухание на зуме) выставляет GalaxyView.
Shader "Astropolity/GalaxyDisk"
{
    Properties
    {
        _MainTex ("Disk", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _T ("Time", Float) = 0
        _RadiusUv ("Galaxy radius in uv", Float) = 0.385
        _Swirl ("Swirl strength", Float) = 0.55
        _CoreColor ("Core color", Color) = (1, 0.82, 0.55, 1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent-40" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
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

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            sampler2D _MainTex;
            fixed4 _Color, _CoreColor;
            float _T, _RadiusUv, _Swirl;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
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
                for (int k = 0; k < 4; k++) { v += a * Noise(p); p = p * 2.03 + 11.3; a *= 0.5; }
                return v;
            }

            float2 Rot(float2 p, float a) { float s = sin(a), c = cos(a); return float2(c * p.x - s * p.y, s * p.x + c * p.y); }

            // Шум пыли, повёрнутый на угол, зависящий от радиуса (внутри — быстрее)
            float Dust(float2 p, float r, float t)
            {
                float ang = t * 0.045 / (r + 0.25);
                float2 q = Rot(p, ang);
                // вытягиваем вдоль окружности — получаются волокна
                float2 polarish = float2(length(q) * 9.0, 0.0) + q * 3.2;
                return Fbm(polarish + q * 6.0);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);
                float2 p = (i.uv - 0.5) / _RadiusUv;      // 1 = край галактики
                float r = length(p);

                // два слоя со сдвигом на полпериода — рисунок течёт без бесконечной накрутки
                const float Period = 90.0;
                float ph = frac(_T / Period);
                float t1 = ph * Period, t2 = frac(ph + 0.5) * Period;
                float w = abs(ph * 2.0 - 1.0);
                float d = lerp(Dust(p, r, t1), Dust(p + 3.7, r, t2), w);

                float swirl = lerp(1.0, 0.45 + 1.1 * d, _Swirl);
                float3 col = tex.rgb * swirl;
                float a = tex.a * lerp(1.0, 0.55 + 0.9 * d, _Swirl * 0.6);

                // дыхание ядра
                float core = exp(-r * r / (2.0 * 0.07 * 0.07)) + 0.35 * exp(-r * r / (2.0 * 0.16 * 0.16));
                float pulse = 0.5 + 0.5 * sin(_T * 0.55) * 0.6 + 0.2 * sin(_T * 1.3 + 1.1);
                float coreA = core * 0.22 * pulse;
                col = (col * a + _CoreColor.rgb * coreA) / max(a + coreA, 1e-4);
                a = saturate(a + coreA);

                return fixed4(col, a) * i.color;
            }
            ENDCG
        }
    }
}
