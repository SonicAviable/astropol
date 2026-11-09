// Astropolity — дальнее небо под картой галактики: яркие звёзды с дифракционными лучами и далёкие галактики.
// Одна сетка квадов-билбордов: вершина хранит центр спрайта, шейдер разворачивает квад лицом к камере.
//   uv0.xy — угол квада (−1…1), uv0.z — зерно;
//   uv1.x — полуразмер в мире, uv1.y — тип (0 звезда, 1 галактика), uv1.z — поворот, uv1.w — сплюснутость;
//   цвет вершины — оттенок, альфа — яркость (выше 1 — попадает в Bloom).
// Аддитивно. _T выставляет GalaxyView каждый кадр.
Shader "Astropolity/GalaxyFarSprites"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _T ("Time", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent-75" "RenderType"="Transparent" "IgnoreProjector"="True" }
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

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; float4 data : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float3 uv : TEXCOORD0; float3 data : TEXCOORD1; };

            fixed4 _Color;
            float _T;

            v2f vert(appdata v)
            {
                v2f o;
                float3 vc = UnityObjectToViewPos(v.vertex.xyz);
                vc.xy += v.uv.xy * v.data.x;                    // билборд: квад всегда лицом к камере
                o.pos = mul(UNITY_MATRIX_P, float4(vc, 1.0));
                o.uv = v.uv.xyz;
                o.data = v.data.yzw;
                o.color = v.color;
                o.color.rgb *= _Color.rgb;
                o.color.a *= _Color.a;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 c = i.uv.xy;
                float seed = i.uv.z;
                float sn, cs;
                sincos(i.data.y, sn, cs);
                float2 r = float2(c.x * cs - c.y * sn, c.x * sn + c.y * cs);
                float d = length(c);
                float px = fwidth(c.x) + fwidth(c.y);           // размер пикселя в долях квада
                float a;

                if (i.data.x < 0.5)
                {
                    // Звезда: ядро, мягкий ореол и четыре луча. Ядро и лучи не тоньше пикселя —
                    // иначе мелкие звёзды рябят при движении камеры; яркость при этом сохраняется
                    float cw = max(0.085, px * 0.9);
                    float core = exp(-d * d / (cw * cw)) * (0.085 * 0.085) / (cw * cw);
                    float halo = exp(-d * d * 9.0) * 0.16;
                    float sw = max(0.03, px * 0.9);
                    float spike = (exp(-abs(r.x) / sw) * exp(-abs(r.y) * 3.4)
                                 + exp(-abs(r.y) / sw) * exp(-abs(r.x) * 3.4)) * (0.03 / sw);
                    a = core + halo + spike * 0.55;
                    a *= 1.0 + 0.14 * sin(_T * (0.5 + seed) + seed * 40.0);
                }
                else
                {
                    // Далёкая галактика: сплюснутый диск со светлым ядром и намёком на два рукава
                    float2 g = float2(r.x, r.y / max(i.data.z, 0.15));
                    float gd = length(g);
                    float core = exp(-gd * gd * 38.0) * 0.9;
                    float arms = 0.5 + 0.5 * cos(2.0 * atan2(g.y, g.x) - gd * 9.0 + seed * 6.2832);
                    a = core + exp(-gd * 3.6) * (0.22 + 0.5 * arms * smoothstep(0.05, 0.3, gd));
                }

                a *= smoothstep(1.0, 0.8, d);                   // к краю квада — в ноль
                return fixed4(i.color.rgb * (a * i.color.a), 1.0);
            }
            ENDCG
        }
    }
}
