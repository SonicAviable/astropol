Shader "Stellaris/OrganicBorders"
{
    // Территории империй в духе Stellaris.
    //  • Территория империи — гладкое объединение кругов вокруг её систем (smooth-min SDF):
    //    соседние системы сливаются в единую область с округлыми краями.
    //  • Между двумя империями граница проходит посередине, у каждой стороны — своя цветная кромка.
    //  • Кромка постоянной толщины в пикселях экрана (fwidth), заливка полупрозрачная
    //    и светлее у края — как «подсвеченная» граница в Stellaris.
    //  • До 192 точек (xy — позиция X/Z, z — владелец: 0 игрок, 1 и 2 — империи ИИ): системы плюс промежуточные
    //    точки вдоль своих коридоров, чтобы территория не рвалась на длинных переходах.
    Properties
    {
        _PlayerColor ("Player Color", Color) = (0.22, 0.92, 0.86, 1.0)
        _EnemyColor ("Enemy Color", Color) = (1.0, 0.30, 0.30, 1.0)
        _Enemy2Color ("Second Enemy Color", Color) = (0.74, 0.48, 1.0, 1.0)
        _ClaimRadius ("Claim Radius", Float) = 15
        _Smooth ("Smooth Union", Float) = 12
        _LinePx ("Border Width (px)", Float) = 2.6
        _FillAlpha ("Fill Alpha", Float) = 0.22
        _BandAlpha ("Edge Band Alpha", Float) = 0.14
    }
    SubShader
    {
        Tags { "Queue"="Transparent-20" "RenderType"="Transparent" "IgnoreProjector"="True" }
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

            struct appdata_t { float4 vertex : POSITION; };
            struct v2f { float4 vertex : SV_POSITION; float3 worldPos : TEXCOORD0; };

            fixed4 _PlayerColor;
            fixed4 _EnemyColor;
            fixed4 _Enemy2Color;
            float _ClaimRadius;
            float _Smooth;
            float _LinePx;
            float _FillAlpha;
            float _BandAlpha;

            float _SysCount;
            float4 _Sys[192];

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            // Полиномиальный smooth-min (И. Килез): гладкое объединение областей
            float smin(float a, float b, float k)
            {
                float h = max(k - abs(a - b), 0.0) / k;
                return min(a, b) - h * h * k * 0.25;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 p = i.worldPos.xz;
                float d0 = 10000.0, d1 = 10000.0, d2 = 10000.0;
                int n = (int)_SysCount;

                [loop]
                for (int k = 0; k < 192; k++)
                {
                    if (k >= n) break;
                    float4 s = _Sys[k];
                    float d = distance(p, s.xy) - _ClaimRadius;
                    if (s.z < 0.5)      d0 = smin(d0, d, _Smooth);
                    else if (s.z < 1.5) d1 = smin(d1, d, _Smooth);
                    else                d2 = smin(d2, d, _Smooth);
                }

                // Своя территория — ближайшая; граница — посередине до второй по близости
                bool own0 = d0 <= d1 && d0 <= d2;
                bool own1 = !own0 && d1 <= d2;
                float dSelf = min(d0, min(d1, d2));
                float dOther = own0 ? min(d1, d2) : (own1 ? min(d0, d2) : min(d0, d1));
                // Расстояние до края своей территории (положительно внутри):
                // либо внешний край, либо середина между двумя империями
                float e = min(-dSelf, (dOther - dSelf) * 0.5);

                float px = max(fwidth(e), 1e-4);
                float ePx = e / px;                       // то же расстояние, но в пикселях экрана
                if (ePx < -3.0) discard;

                fixed3 col = own0 ? _PlayerColor.rgb : (own1 ? _EnemyColor.rgb : _Enemy2Color.rgb);

                // Кромка: яркая линия у самого края, лёгкий ореол снаружи
                float lineA = 1.0 - smoothstep(_LinePx * 0.5, _LinePx * 0.5 + 1.2, abs(ePx - _LinePx * 0.5));
                float outer = (1.0 - smoothstep(0.0, 3.0, -ePx)) * step(ePx, 0.0) * 0.35;

                // Заливка: прозрачнее в глубине, плотнее у края (ширина полосы — в мировых единицах)
                float inside = smoothstep(-0.5, 0.5, ePx);
                float band = 1.0 - smoothstep(0.0, _ClaimRadius * 0.45, e);
                float fillA = inside * (_FillAlpha + _BandAlpha * band);

                float a = max(max(fillA, lineA * 0.95), outer);
                fixed3 fillRgb = col * lerp(0.5, 0.85, band);
                fixed3 lineRgb = lerp(col, fixed3(1, 1, 1), 0.22);
                float lw = saturate(max(lineA * 0.95, outer) / max(a, 1e-4));
                return fixed4(lerp(fillRgb, lineRgb, lw), a);
            }
            ENDCG
        }
    }
}
