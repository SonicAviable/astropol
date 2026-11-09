// Космическая фауна: искры (Рой Искр, шлейфы). Один меш из квадов: у каждой вершины — центр искры (POSITION, мир),
// угол квада uv.xy (−1..1) и размер в мировых единицах uv.z. Квад разворачивается к камере в вершинном шейдере;
// издалека искра не меньше ~1,6 пикселя, чтобы рой не пропадал на дальней карте. Аддитивно, мягкая точка.
Shader "Astropolity/FaunaSpark"
{
    Properties
    {
        _Alpha ("Alpha", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Alpha;
            struct appdata { float4 vertex : POSITION; float4 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                float3 wc = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                float4 vc = mul(UNITY_MATRIX_V, float4(wc, 1.0));
                float depth = max(0.01, -vc.z);
                float wpp = 2.0 * depth / (UNITY_MATRIX_P[1][1] * _ScreenParams.y);
                float size = max(v.uv.z, 1.6 * wpp);
                vc.xy += v.uv.xy * size * 0.5;
                o.pos = mul(UNITY_MATRIX_P, vc);
                o.uv = v.uv.xy;
                o.color = v.color;
                o.color.a *= lerp(0.55, 1.0, saturate(v.uv.z / size));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float d = length(i.uv);
                float m = saturate(1.0 - d);
                m = m * m * (0.35 + 0.65 * exp(-d * d * 10.0));
                return fixed4(i.color.rgb, saturate(m * i.color.a * _Alpha * 1.6));
            }
            ENDCG
        }
    }
    Fallback Off
}
