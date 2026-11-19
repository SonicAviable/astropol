// Свечение мегаструктур (чудес фракций), аддитивно. Режим _Mode:
//   0 — ровное свечение (решётки, прожилки, кромки) с лёгкой пульсацией;
//   1 — горизонт Врат: закрученная воронка по полярным координатам uv (диск 0..1);
//   2 — поток плазмы вдоль линии (uv.x — вдоль, uv.y — поперёк), бегущие сгустки;
//   3 — голограмма строительных лесов: мерцание и бегущая строка сканирования по высоте.
// Цвет — _Color × цвет вершин; HDR-яркость задаётся величиной цвета.
Shader "Astropolity/WonderFx"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 0.8, 0.4, 1)
        _Mode ("Mode", Float) = 0
        _Speed ("Speed", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Mode, _Speed;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; float3 wpos : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float4 frag(v2f i) : SV_Target
            {
                float t = _Time.y * _Speed;
                float a = 1.0;
                float3 c = _Color.rgb;

                if (_Mode < 0.5)
                {
                    a = 0.8 + 0.2 * sin(t * 2.0 + i.wpos.x * 0.7 + i.wpos.z * 0.5);
                }
                else if (_Mode < 1.5)
                {
                    float2 p = i.uv * 2.0 - 1.0;
                    float r = length(p);
                    float ang = atan2(p.y, p.x);
                    float swirl = sin(ang * 5.0 + r * 14.0 - t * 2.6) * 0.5 + 0.5;
                    float swirl2 = sin(ang * 3.0 - r * 9.0 + t * 1.7) * 0.5 + 0.5;
                    float rim = smoothstep(0.62, 0.98, r) * (1.0 - smoothstep(0.98, 1.0, r));
                    float core = exp(-r * r * 6.0);
                    float body = (swirl * 0.55 + swirl2 * 0.35) * (1.0 - smoothstep(0.85, 1.0, r));
                    a = saturate(body * 0.55 + rim * 1.6 + core * 0.9);
                    c = lerp(c, float3(1, 1, 1), core * 0.6 + rim * 0.3);
                }
                else if (_Mode < 2.5)
                {
                    float y = abs(i.uv.y * 2.0 - 1.0);
                    float core = exp(-y * y * 9.0);
                    float blobs = pow(frac(i.uv.x * 6.0 - t * 1.4), 3.0);
                    float ends = smoothstep(0.0, 0.08, i.uv.x) * smoothstep(0.0, 0.08, 1.0 - i.uv.x);
                    a = core * (0.45 + 0.9 * blobs) * ends;
                    c = lerp(c, float3(1, 0.95, 0.85), core * blobs * 0.5);
                }
                else
                {
                    float scan = frac(i.wpos.y * 0.6 + i.wpos.x * 0.15 - t * 0.45);
                    float band = smoothstep(0.0, 0.04, scan) * (1.0 - smoothstep(0.08, 0.16, scan));
                    float flick = 0.75 + 0.25 * hash(floor(float2(t * 14.0, i.wpos.x * 3.0)));
                    a = (0.35 + 1.2 * band) * flick;
                }
                return float4(c * i.color.rgb, saturate(a * _Color.a * i.color.a));
            }
            ENDCG
        }
    }
}
