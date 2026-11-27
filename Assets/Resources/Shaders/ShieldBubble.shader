// Пузырь щита: невидим, пока в него не попадают. Каждое попадание — яркая точка удара
// и расходящаяся по сфере волна, в которой проступает шестигранная сетка поля; край пузыря светится (френель).
// При пробитии щита (_Collapse) вся сфера вспыхивает сеткой и гаснет, ячейки распадаются вразнобой.
// Попадания задаются в пространстве объекта (направление на единичной сфере) и времени _Now.
Shader "Astropolity/FX/ShieldBubble"
{
    Properties
    {
        _Color ("Color", Color) = (0.35, 0.75, 1, 1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+45" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float4 _Hits[6];
            float _Now;
            float _Collapse;
            float _Strength;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 op  : TEXCOORD0;
                float3 wn  : TEXCOORD1;
                float3 wv  : TEXCOORD2;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.op = v.vertex.xyz;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.wv = WorldSpaceViewDir(v.vertex);
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Шестигранная сетка: x — близость к ребру ячейки (1 на ребре), y — случайное число ячейки
            float2 hexGrid(float2 p)
            {
                const float2 r = float2(1.0, 1.7320508);
                float2 h = r * 0.5;
                float2 a = p - r * floor(p / r) - h;
                float2 b = (p - h) - r * floor((p - h) / r) - h;
                float2 g = dot(a, a) < dot(b, b) ? a : b;
                float2 id = p - g;
                float2 q = abs(g);
                float d = max(dot(q, normalize(r)), q.x);
                return float2(smoothstep(0.38, 0.49, d), hash21(floor(id * 4.0)));
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.op);
                float facing = abs(dot(normalize(i.wn), normalize(i.wv)));
                float fres = pow(1.0 - saturate(facing), 2.2);

                // Полюса сетки — по бокам (ось X): камера смотрит сверху, сходящиеся ячейки уходят на край силуэта
                float2 uv = float2(atan2(n.z, n.y) / 6.2831853 + 0.5, acos(clamp(n.x, -1.0, 1.0)) / 3.14159265);
                float2 hx = hexGrid(uv * float2(30.0, 15.0));
                float hex = hx.x;

                float wave = 0.0, spot = 0.0;
                [unroll]
                for (int k = 0; k < 6; k++)
                {
                    float age = _Now - _Hits[k].w;
                    if (age < 0.0 || age > 1.3) continue;
                    float3 hp = normalize(_Hits[k].xyz + 1e-5);
                    float d = acos(clamp(dot(n, hp), -1.0, 1.0));     // угловое расстояние до удара
                    float front = age * 2.4;                            // фронт волны бежит по сфере
                    float fade = pow(saturate(1.0 - age / 1.3), 1.6);
                    float ring = exp(-pow((d - front) / 0.16, 2.0)) * fade;
                    float trail = saturate(1.0 - (front - d) / 0.9) * step(d, front) * fade * 0.35;   // поле за фронтом ещё дрожит
                    wave += ring + trail;
                    spot += exp(-d * d / 0.025) * exp(-age * 6.5) + exp(-d * d / 0.18) * exp(-age * 3.0) * 0.35;
                }

                float ca = _Now - _Collapse;
                float coll = 0.0;
                if (ca >= 0.0 && ca < 1.5)
                {
                    // Сетка вспыхивает и распадается: каждая ячейка гаснет в своё время
                    float t = ca / 1.5;
                    float cellAlive = step(t, hx.y * 0.85 + 0.1);
                    coll = (1.0 - t) * (0.25 + hex * 1.6 * cellAlive) + exp(-ca * 10.0) * 1.2;
                }

                float flick = 0.88 + 0.12 * sin(_Now * 55.0 + n.y * 30.0 + n.x * 17.0);
                float glow = wave * (0.18 + hex * 1.5) * flick
                           + spot * 2.4
                           + fres * (wave * 0.9 + spot * 0.8 + coll * 1.2)
                           + coll * (0.6 + 0.4 * flick);
                glow *= _Strength;
                float3 col = _Color.rgb * glow + float3(1, 1, 1) * spot * 0.6 * _Strength;
                return float4(col, 1.0);
            }
            ENDCG
        }
    }
}
