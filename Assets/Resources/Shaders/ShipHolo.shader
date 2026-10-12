// Голографический «каркас» корабля для конструктора (ShipHoloStudio).
// Корпус почти прозрачный: светятся контуры (френель), по поверхности идёт сетка «проволоки»
// в координатах модели, снизу вверх бежит полоса сканирования. Аддитивно — на чёрном фоне студии.
Shader "Astropolity/FX/ShipHolo"
{
    Properties
    {
        _Color ("Color", Color) = (0.35, 0.9, 1, 1)
        _GridScale ("Grid cells per unit", Float) = 3
        _ScanSpeed ("Scan speed", Float) = 0.35
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Back
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _GridScale, _ScanSpeed;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 n : TEXCOORD0;
                float3 v : TEXCOORD1;
                float3 obj : TEXCOORD2;
                float h : TEXCOORD3;
            };

            v2f vert(appdata i)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(i.vertex);
                float3 wp = mul(unity_ObjectToWorld, i.vertex).xyz;
                o.n = UnityObjectToWorldNormal(i.normal);
                o.v = normalize(_WorldSpaceCameraPos - wp);
                o.obj = i.vertex.xyz;
                o.h = wp.y;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.n);
                float fres = pow(1.0 - saturate(abs(dot(n, normalize(i.v)))), 2.4);

                // Сетка «проволоки» по координатам модели: у каждой оси 1 — на линии сетки
                float3 g = abs(frac(i.obj * _GridScale) - 0.5) * 2.0;
                float wire = smoothstep(0.90, 0.975, max(max(g.x, g.y), g.z));

                // Полоса сканирования снизу вверх
                float scan = frac(i.h * 0.35 - _Time.y * _ScanSpeed);
                float band = exp(-pow((scan - 0.5) * 14.0, 2.0));

                float a = 0.05 + fres * 0.95 + wire * 0.35 + band * 0.25;
                float3 c = _Color.rgb * (0.6 + fres * 0.8 + band * 0.6) + wire * _Color.rgb * 0.4;
                return fixed4(c, saturate(a) * _Color.a);
            }
            ENDCG
        }
    }
}
