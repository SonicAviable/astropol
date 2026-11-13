// Astropolity — факел двигателя.
//
// Квад вдоль локальной оси −Z (z = 0 у сопла, z = −1 — конец факела), ширина по X (−0.5…0.5).
// Вершинный шейдер разворачивает квад к камере вокруг оси факела (цилиндрический билборд),
// поэтому факел выглядит объёмным с любого ракурса. Длину и ширину задаёт масштаб объекта.
//
// Во фрагменте — горячее белое ядро у сопла, сужающаяся струя цвета двигателя
// и скачки уплотнения («ромбы»), которые видны у работающего на полную тягу двигателя.
// _Throttle 0…1 — тяга: яркость, плотность ромбов и мерцание.
Shader "Astropolity/EnginePlume"
{
    Properties
    {
        _Color ("Exhaust color", Color) = (0.45, 0.8, 1, 1)
        _Throttle ("Throttle", Range(0, 1)) = 1
        _Intensity ("Intensity", Float) = 2.2
        _Seed ("Seed", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Throttle, _Intensity, _Seed;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                float3 origin = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                float3 axisW = mul((float3x3)unity_ObjectToWorld, float3(0, 0, 1));
                float3 sideW = mul((float3x3)unity_ObjectToWorld, float3(1, 0, 0));
                float len = length(axisW);
                float width = length(sideW);
                float3 axis = axisW / max(len, 1e-5);

                float3 p = origin + axis * (v.vertex.z * len);
                float3 toCam = normalize(_WorldSpaceCameraPos - p);
                float3 side = cross(axis, toCam);
                float sl = length(side);
                side = sl > 1e-4 ? side / sl : normalize(sideW);
                p += side * (v.vertex.x * width);

                o.pos = mul(UNITY_MATRIX_VP, float4(p, 1));
                o.uv = float2(v.vertex.x * 2.0, -v.vertex.z);   // u: −1…1 поперёк, v: 0 у сопла … 1 в конце
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y + _Seed;
                float v = saturate(i.uv.y);
                float u = i.uv.x;

                // Струя сужается к концу; лёгкое дрожание радиуса
                float wob = 1.0 + 0.06 * sin(t * 37.0 + v * 9.0) * _Throttle;
                float r = lerp(0.62, 0.12, pow(v, 0.8)) * wob;
                float d = abs(u) / max(r, 1e-3);
                float body = exp(-d * d * 3.2);

                // Затухание вдоль струи и мягкое начало у самого сопла
                float fade = pow(1.0 - v, 1.6) * smoothstep(0.0, 0.04, v);

                // Ядро — узкое и белое у сопла
                float core = exp(-d * d * 14.0) * pow(1.0 - v, 4.0);

                // Скачки уплотнения: стоячие яркие узлы вдоль струи
                float diamonds = pow(0.5 + 0.5 * cos(v * 34.0), 6.0) * exp(-d * d * 6.0) * (1.0 - v) * _Throttle;

                float flicker = 0.9 + 0.1 * sin(t * 53.0) * sin(t * 21.0 + 1.3);
                float a = (body * fade * 0.55 + diamonds * 0.65 + core) * flicker;

                float3 col = lerp(_Color.rgb, float3(1, 1, 1), saturate(core * 1.4 + diamonds * 0.5));
                return fixed4(col * a * _Intensity * saturate(_Throttle * 1.2), 1);
            }
            ENDCG
        }
    }
}
