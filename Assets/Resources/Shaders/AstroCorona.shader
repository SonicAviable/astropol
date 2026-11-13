// Корона звезды на квадрате-билборде через центр звезды (квад ±1 в осях объекта = _Extent радиусов звезды).
// Диск звезды закрывает центр по глубине; снаружи — лучи, медленно меняющиеся во времени. Аддитивно.
Shader "Astropolity/Star/Corona"
{
    Properties
    {
        _Color ("Corona color (sRGB)", Vector) = (1,0.9,0.63,0)
        _Extent ("Quad half-size in star radii", Float) = 4
        _Seed ("Seed", Float) = 0
        _Intensity ("Intensity", Float) = 1.6
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off
        Lighting Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "AstroNoise.cginc"
            float4 _Color;
            float _Extent, _Seed, _Intensity;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy * 2 - 1;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;
                float2 uv = i.uv * _Extent;
                float r = length(uv);
                float d = max(r - 1, 0);
                float an = atan2(uv.y, uv.x);
                float rays = 0.55 + 0.45 * snoise(float3(cos(an) * 3, sin(an) * 3, t * 0.25 + _Seed))
                           + 0.25 * snoise(float3(cos(an) * 9, sin(an) * 9, t * 0.4));
                float3 c = pow(_Color.rgb, 2.2) * (exp(-d * 6) * 1.6 * max(rays, 0.2) + exp(-d * 1.8) * 0.35);
                float fade = 1 - smoothstep(0.75, 1, length(i.uv));
                return fixed4(c * fade * _Intensity, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
