Shader "Stellaris/OrganicBorders"
{
    // Территории империй в духе Stellaris.
    //  • Территория империи — гладкое объединение кругов вокруг её систем (smooth-min SDF):
    //    соседние системы сливаются в единую область с округлыми краями.
    //  • Между двумя империями граница проходит посередине, у каждой стороны — своя цветная кромка.
    //  • Кромка постоянной толщины в пикселях экрана (fwidth), заливка полупрозрачная
    //    и светлее у края — как «подсвеченная» граница в Stellaris.
    //  • До 1023 точек (предел массива в шейдерах Unity) (xy — позиция X/Z, z — владелец: 0 игрок, 1–5 — империи ИИ, 6 — угрозы): системы плюс промежуточные
    //    точки вдоль своих коридоров, чтобы территория не рвалась на длинных переходах.
    Properties
    {
        _PlayerColor ("Player Color", Color) = (0.22, 0.92, 0.86, 1.0)
        _EnemyColor ("Enemy Color", Color) = (1.0, 0.30, 0.30, 1.0)
        _Enemy2Color ("Second Enemy Color", Color) = (0.74, 0.48, 1.0, 1.0)
        _Enemy3Color ("Third Enemy Color", Color) = (1.0, 0.74, 0.25, 1.0)
        _Enemy4Color ("Fourth Enemy Color", Color) = (0.44, 0.86, 0.34, 1.0)
        _Enemy5Color ("Fifth Enemy Color", Color) = (0.86, 0.91, 0.97, 1.0)
        _ThreatColor ("Threat Color", Color) = (0.8, 0.3, 0.3, 1.0)
        _ClaimRadius ("Claim Radius", Float) = 15
        _Smooth ("Smooth Union", Float) = 12
        _LinePx ("Border Width (px)", Float) = 2.6
        _FillAlpha ("Fill Alpha", Float) = 0.22
        _BandAlpha ("Edge Band Alpha", Float) = 0.14
        _LineAlpha ("Line Alpha", Float) = 0.95
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
            fixed4 _Enemy3Color;
            fixed4 _Enemy4Color;
            fixed4 _Enemy5Color;
            fixed4 _ThreatColor;
            float _ClaimRadius;
            float _Smooth;
            float _LinePx;
            float _FillAlpha;
            float _BandAlpha;
            float _LineAlpha;

            // Пространственная сетка (строит GalaxyView.UploadBorderGrid): ячейка хранит (начало, число) своего списка
            // точек в _BorderPts — только тех, что ближе 4×радиуса владения. Дальние точки на результат не влияют,
            // а перебор всех точек в каждом пикселе съедал видеокарту на больших картах.
            sampler2D _BorderGrid;
            sampler2D _BorderPts;
            float4 _GridParams;   // x, y — угол сетки (мир, xz); z — 1/размер ячейки; w — число ячеек по стороне
            float4 _PtsParams;    // x — ширина текстуры точек; y — 1/ширина; z — 1/высота

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
                // Размер пикселя в мировых единицах — по координатам, а не fwidth(e): на стыке ячеек сетки соседние
                // пиксели считают e по разным спискам точек, и fwidth(e) давал пунктир по краям ячеек.
                float px = max((length(ddx(p)) + length(ddy(p))) * 0.6, 1e-4);
                float d0 = 10000.0, d1 = 10000.0, d2 = 10000.0, d3 = 10000.0, d4 = 10000.0, d5 = 10000.0, d6 = 10000.0;
                float gw = _GridParams.w;
                float2 cell = clamp(floor((p - _GridParams.xy) * _GridParams.z), 0.0, gw - 1.0);
                float2 sc = tex2Dlod(_BorderGrid, float4((cell + 0.5) / gw, 0, 0)).xy;
                int n = (int)sc.y;
                if (n <= 0) discard;

                [loop]
                for (int k = 0; k < 1024; k++)
                {
                    if (k >= n) break;
                    float idx = sc.x + k;
                    float row = floor(idx * _PtsParams.y);
                    float col = idx - row * _PtsParams.x;
                    float4 s = tex2Dlod(_BorderPts, float4((col + 0.5) * _PtsParams.y, (row + 0.5) * _PtsParams.z, 0, 0));
                    float d = distance(p, s.xy) - _ClaimRadius;
                    if (s.z < 0.5)      d0 = smin(d0, d, _Smooth);
                    else if (s.z < 1.5) d1 = smin(d1, d, _Smooth);
                    else if (s.z < 2.5) d2 = smin(d2, d, _Smooth);
                    else if (s.z < 3.5) d3 = smin(d3, d, _Smooth);
                    else if (s.z < 4.5) d4 = smin(d4, d, _Smooth);
                    else if (s.z < 5.5) d5 = smin(d5, d, _Smooth);
                    else                d6 = smin(d6, d, _Smooth);
                }

                // Своя территория — ближайшая (при равенстве — меньший номер владельца);
                // граница — посередине до второй по близости
                float dSelf = d0;
                int own = 0;
                fixed3 col = _PlayerColor.rgb;
                if (d1 < dSelf) { dSelf = d1; own = 1; col = _EnemyColor.rgb; }
                if (d2 < dSelf) { dSelf = d2; own = 2; col = _Enemy2Color.rgb; }
                if (d3 < dSelf) { dSelf = d3; own = 3; col = _Enemy3Color.rgb; }
                if (d4 < dSelf) { dSelf = d4; own = 4; col = _Enemy4Color.rgb; }
                if (d5 < dSelf) { dSelf = d5; own = 5; col = _Enemy5Color.rgb; }
                if (d6 < dSelf) { dSelf = d6; own = 6; col = _ThreatColor.rgb; }
                float dOther = 10000.0;
                if (own != 0) dOther = min(dOther, d0);
                if (own != 1) dOther = min(dOther, d1);
                if (own != 2) dOther = min(dOther, d2);
                if (own != 3) dOther = min(dOther, d3);
                if (own != 4) dOther = min(dOther, d4);
                if (own != 5) dOther = min(dOther, d5);
                if (own != 6) dOther = min(dOther, d6);
                // Расстояние до края своей территории (положительно внутри):
                // либо внешний край, либо середина между двумя империями
                float e = min(-dSelf, (dOther - dSelf) * 0.5);

                float ePx = e / px;                       // то же расстояние, но в пикселях экрана
                if (ePx < -3.0) discard;

                // Кромка: яркая линия у самого края, лёгкий ореол снаружи
                float lineA = 1.0 - smoothstep(_LinePx * 0.5, _LinePx * 0.5 + 1.2, abs(ePx - _LinePx * 0.5));
                float outer = (1.0 - smoothstep(0.0, 3.0, -ePx)) * step(ePx, 0.0) * 0.35;

                // Заливка: прозрачнее в глубине, плотнее у края (ширина полосы — в мировых единицах)
                float inside = smoothstep(-0.5, 0.5, ePx);
                float band = 1.0 - smoothstep(0.0, _ClaimRadius * 0.45, e);
                float fillA = inside * (_FillAlpha + _BandAlpha * band);

                float a = max(max(fillA, lineA * _LineAlpha), outer);
                fixed3 fillRgb = col * lerp(0.5, 0.85, band);
                fixed3 lineRgb = lerp(col, fixed3(1, 1, 1), 0.22);
                float lw = saturate(max(lineA * _LineAlpha, outer) / max(a, 1e-4));
                return fixed4(lerp(fillRgb, lineRgb, lw), a);
            }
            ENDCG
        }
    }
}
