// Astropolity — тёмные пылевые облака (как прожилки на фото настоящих галактик).
// Обычное смешивание: облако заслоняет туманности, диск и ядро, лежащие ниже, — это и даёт глубину.
// Волокнистый fbm, вытянутый вдоль квада; рваный край; медленное течение.
Shader "Astropolity/GalaxyDarkDust"
{
    Properties
    {
        _Tint ("Tint", Color) = (0.05, 0.035, 0.04, 1)
        _Seed ("Seed", Float) = 0
        _T ("Time", Float) = 0
        _Opacity ("Opacity", Float) = 0.7
    }
    SubShader
    {
        Tags { "Queue"="Transparent-37" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
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

            fixed4 _Tint;
            float _Seed, _T, _Opacity;

            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

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
                for (int k = 0; k < 5; k++) { v += a * Noise(p); p = p * 2.05 + 9.1; a *= 0.5; }
                return v;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 c = i.uv - 0.5;
                // вытянутая форма: длинная ось — x
                float r = length(c * float2(2.0, 4.2));
                float2 p = float2(i.uv.x * 3.0, i.uv.y * 9.0) + _Seed;   // волокна вдоль x
                float2 w = p + float2(Fbm(p + _T * 0.01), Fbm(p + 4.3 - _T * 0.008)) * 1.6;
                float n = Fbm(w);
                float edge = r + (Fbm(p * 0.6 + 2.0) - 0.5) * 0.7;
                float mask = 1.0 - smoothstep(0.45, 1.0, edge);
                float dens = saturate(n * 1.9 - 0.6) * mask;
                // тонкие прожилки плотнее
                float fil = pow(saturate(1.0 - abs(Fbm(w * 1.8 + 1.7) - 0.5) * 4.0), 2.0) * mask;
                float a = saturate(dens * 0.75 + fil * 0.45) * _Opacity;
                return fixed4(_Tint.rgb, a);
            }
            ENDCG
        }
    }
}
