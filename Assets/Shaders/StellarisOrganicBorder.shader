Shader "Stellaris/OrganicBorders"
{
    Properties
    {
        _PlayerColor ("Player Border Color", Color) = (0.28, 0.92, 1.0, 1.0)
        _EnemyColor ("Enemy Border Color", Color) = (0.95, 0.25, 0.25, 1.0)
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
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            fixed4 _PlayerColor;
            fixed4 _EnemyColor;

            // Координаты систем игрока (X, Z, Radius, 0)
            int _PlayerStarCount;
            float4 _PlayerStars[40];

            // Сегменты гиперкоридоров игрока (X1, Z1, X2, Z2)
            int _PlayerSegmentCount;
            float4 _PlayerSegments[40];

            // Системы ИИ-империй
            int _EnemyStarCount;
            float4 _EnemyStars[40];
            int _EnemySegmentCount;
            float4 _EnemySegments[40];

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            // Расстояние от точки P до отрезка AB в плоскости XZ
            float DistToSegment(float2 p, float2 a, float2 b)
            {
                float2 ba = b - a;
                float2 pa = p - a;
                float l2 = dot(ba, ba);
                if (l2 < 0.0001) return length(pa);
                float t = clamp(dot(pa, ba) / l2, 0.0, 1.0);
                return length(pa - ba * t);
            }

            // Расчёт поля метаболов (гладкая полиномиальная функция Wyvill/Blinn)
            float EvaluateField(float2 p, float4 stars[40], int starCount, float4 segs[40], int segCount)
            {
                float field = 0.0;

                // Влияние звёзд
                for (int i = 0; i < starCount; i++)
                {
                    float r = stars[i].z;
                    float d = distance(p, stars[i].xy);
                    if (d < r)
                    {
                        float q = 1.0 - (d / r);
                        field += q * q * (3.0 - 2.0 * q); // Гладкий S-образный метабол
                    }
                }

                // Влияние коридоров (связывает звёзды в слитные рукава)
                for (int j = 0; j < segCount; j++)
                {
                    float rSeg = 15.0;
                    float dSeg = DistToSegment(p, segs[j].xy, segs[j].zw);
                    if (dSeg < rSeg)
                    {
                        float q = 1.0 - (dSeg / rSeg);
                        field += q * q * (3.0 - 2.0 * q) * 0.85;
                    }
                }

                return field;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 p = i.worldPos.xz;

                float pField = EvaluateField(p, _PlayerStars, _PlayerStarCount, _PlayerSegments, _PlayerSegmentCount);
                float eField = EvaluateField(p, _EnemyStars, _EnemyStarCount, _EnemySegments, _EnemySegmentCount);

                float field = max(pField, eField);
                if (field < 0.15) discard;

                fixed4 empColor = (pField >= eField) ? _PlayerColor : _EnemyColor;

                // Граница Stellaris:
                // threshold = 0.50
                // 1. Внешняя яркая неоновая линия (от 0.44 до 0.54)
                float edgeDelta = abs(field - 0.50);
                if (edgeDelta < 0.06)
                {
                    float neonGlow = 1.0 - (edgeDelta / 0.06);
                    neonGlow = smoothstep(0.0, 1.0, neonGlow);
                    fixed3 edgeRgb = lerp(empColor.rgb * 1.5, fixed3(1, 1, 1), neonGlow * 0.4);
                    return fixed4(edgeRgb, neonGlow * 0.95);
                }

                // 2. Внутренняя мягкая вуаль империи (field > 0.50)
                if (field > 0.50)
                {
                    float depth = saturate((field - 0.50) * 1.5);
                    float fillAlpha = lerp(0.30, 0.48, depth);
                    fixed3 fillRgb = empColor.rgb * 0.42;
                    return fixed4(fillRgb, fillAlpha);
                }

                // Внешнее рассеянное свечение за границей (field < 0.50)
                float outerGlow = saturate((field - 0.15) / 0.35);
                return fixed4(empColor.rgb * 0.5, outerGlow * 0.18);
            }
            ENDCG
        }
    }
}