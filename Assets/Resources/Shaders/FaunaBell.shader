// Космическая фауна: купол «Пустотной медузы». Полусфера (y 0..1, верх — 1): пульсирует (край сжимается сильнее),
// светится по Френелю — края ярче центра; радиальные каналы и огоньки по кромке купола. Аддитивно, без записи глубины.
Shader "Astropolity/FaunaBell"
{
    Properties
    {
        _In ("Inner", Color) = (0.35, 0.2, 0.5, 1)
        _Rim ("Rim", Color) = (0.85, 0.55, 1, 1)
        _Phase ("Phase", Float) = 0
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
            float4 _In, _Rim;
            float _Phase, _Alpha;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; float3 p : TEXCOORD2; };

            v2f vert(appdata v)
            {
                v2f o;
                float pulse = sin(_Time.y * 1.7 + _Phase);
                float3 p = v.vertex.xyz;
                p.xz *= 1.0 + 0.16 * pulse * (1.0 - p.y);
                p.y *= 1.0 - 0.12 * pulse;
                float3 wp = mul(unity_ObjectToWorld, float4(p, 1.0)).xyz;
                o.pos = UnityWorldToClipPos(wp);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.v = normalize(_WorldSpaceCameraPos - wp);
                o.p = v.vertex.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float f = 1.0 - abs(dot(normalize(i.n), normalize(i.v)));
                float rim = pow(f, 2.2);
                float ang = atan2(i.p.z, i.p.x);
                float canals = pow(abs(cos(ang * 4.0)), 40.0) * smoothstep(0.0, 0.8, i.p.y);
                float edge = smoothstep(0.22, 0.0, i.p.y);
                float spots = smoothstep(0.9, 1.0, sin(ang * 16.0)) * edge;
                float3 c = _In.rgb * 0.28 + _Rim.rgb * (rim * 1.05 + canals * 0.55 + edge * 0.65 + spots * 1.2);
                return fixed4(c, _Alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
