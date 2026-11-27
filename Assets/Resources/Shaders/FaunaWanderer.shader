// Космическая фауна: сегмент тела «Странника» (единичная сфера, растянутая в эллипсоид). Тёмное тело,
// голубая кайма по Френелю, по спине бежит волна света (фаза — номер сегмента _K = 0 голова … 1 хвост),
// по бокам у каждого сегмента — огонёк, вместе они дают ряды огней вдоль тела.
Shader "Astropolity/FaunaWanderer"
{
    Properties
    {
        _K ("Segment 0..1", Float) = 0
        _Alpha ("Alpha", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _K, _Alpha;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; float3 lp : TEXCOORD2; };

            v2f vert(appdata v)
            {
                v2f o;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityWorldToClipPos(wp);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.v = normalize(_WorldSpaceCameraPos - wp);
                o.lp = v.vertex.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.n), v = normalize(i.v);
                float f = 1.0 - abs(dot(n, v));
                float rim = f * f * f;
                float3 lp = i.lp;
                float back = smoothstep(0.35, 0.95, lp.y);
                float wave = pow(0.5 + 0.5 * sin(_K * 34.0 - _Time.y * 2.6), 5.0);
                float stripe = exp(-lp.x * lp.x * 14.0) * back;
                float sx = (abs(lp.x) - 0.9) * 10.0, sy = lp.y + 0.05;
                float side = exp(-sx * sx - lp.z * lp.z * 14.0 - sy * sy * 14.0);
                float3 body = lerp(float3(0.025, 0.04, 0.10), float3(0.05, 0.08, 0.16), back);
                float3 c = body + float3(0.35, 0.55, 1.0) * rim * 0.8
                         + float3(0.45, 0.95, 1.0) * stripe * (0.15 + wave * 1.6)
                         + float3(0.6, 0.75, 1.0) * side * (0.6 + 0.4 * sin(_Time.y * 1.3 + _K * 20.0));
                return fixed4(c, _Alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
