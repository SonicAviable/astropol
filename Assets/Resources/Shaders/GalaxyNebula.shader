// Astropolity — цветная туманность региона галактики (квад под картой).
// Процедурные облака: fbm с искажением координат, медленно текут; два цвета смешиваются по второму шуму;
// край облака рваный (шум по радиусу). Аддитивно — облака светятся, как в Stellaris / Master of Orion.
// _T, _Intensity выставляет GalaxyView каждый кадр.
Shader "Astropolity/GalaxyNebula"
{
    Properties
    {
        _ColorA ("Color A", Color) = (0.2, 0.8, 0.8, 1)
        _ColorB ("Color B", Color) = (0.6, 0.3, 0.9, 1)
        _Seed ("Seed", Float) = 0
        _T ("Time", Float) = 0
        _Intensity ("Intensity", Float) = 1
        _Scale ("Noise scale", Float) = 3
    }
    SubShader
    {
        Tags { "Queue"="Transparent-50" "RenderType"="Transparent" "IgnoreProjector"="True" }
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

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            fixed4 _ColorA, _ColorB;
            float _Seed, _T, _Intensity, _Scale;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }
            float Fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int k = 0; k < 5; k++) { v += a * Noise(p); p = p * 2.02 + 13.7; a *= 0.5; }
                return v;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 c = i.uv - 0.5;
                float r = length(c) * 2.0;
                float2 p = i.uv * _Scale + _Seed;

                // течение: координаты медленно искажаются
                float2 q = float2(Fbm(p + float2(0.0, _T * 0.012)), Fbm(p + float2(5.2, 1.3) - _T * 0.010));
                float2 w = p + 2.2 * q + float2(_T * 0.006, -_T * 0.004);
                float n = Fbm(w);
                float m = Fbm(w * 1.7 + 3.1);

                // рваный край
                float edge = r + (Fbm(p * 0.8 + 7.0) - 0.5) * 0.55;
                float mask = 1.0 - smoothstep(0.35, 1.0, edge);

                float dens = saturate(n * 1.6 - 0.45) * mask;
                dens = dens * dens * (3.0 - 2.0 * dens);
                // прожилки ярче
                float vein = pow(saturate(1.0 - abs(m - 0.5) * 3.0), 3.0) * mask * 0.5;

                float3 col = lerp(_ColorA.rgb, _ColorB.rgb, saturate(m * 1.4 - 0.2));
                float3 o = col * (dens * 0.55 + vein * dens * 1.2);
                return fixed4(o * _Intensity, 1.0);
            }
            ENDCG
        }
    }
}
