// Astropolity — ядро галактики (плоский квад в центре, аддитивно).
//   • раскалённый бело-золотой центр, вокруг — балдж и тёплое гало
//   • два закрученных рукава уходят из ядра (логарифмическая спираль) с тёмными пылевыми прожилками
//   • рукава заметно вращаются, ядро дышит, в рукавах вспыхивают звёзды-искры
// _T, _Intensity выставляет GalaxyView.
Shader "Astropolity/GalaxyCore"
{
    Properties
    {
        _T ("Time", Float) = 0
        _Intensity ("Intensity", Float) = 1
        _Hot ("Hot color", Color) = (1, 0.97, 0.9, 1)
        _Warm ("Warm color", Color) = (1, 0.78, 0.45, 1)
        _Outer ("Outer color", Color) = (0.95, 0.5, 0.3, 1)
        _Arms ("Arm count", Float) = 2
        _Twist ("Arm twist", Float) = 2.6
    }
    SubShader
    {
        Tags { "Queue"="Transparent-30" "RenderType"="Transparent" "IgnoreProjector"="True" }
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

            float _T, _Intensity, _Arms, _Twist;
            fixed4 _Hot, _Warm, _Outer;

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
                for (int k = 0; k < 5; k++) { v += a * Noise(p); p = p * 2.03 + 7.7; a *= 0.5; }
                return v;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * 2.0;          // -1..1, 1 = край квада
                float r = length(p);
                if (r > 1.0) return 0;
                float ang = atan2(p.y, p.x);

                // дыхание ядра: заметное, но плавное (≈7 с), с лёгкой второй гармоникой
                float pulse = 1.0 + 0.18 * sin(_T * 0.9) + 0.06 * sin(_T * 2.3 + 1.7);
                float haloPulse = 1.0 + 0.10 * sin(_T * 0.9 - 0.8);

                // вращение рукавов целиком: ≈50 с на оборот (без накрутки со временем)
                float spin = _T * 0.125;
                float spiral = ang * _Arms - log(max(r, 0.02)) * _Twist * _Arms + spin * _Arms;
                float arm = 0.5 + 0.5 * cos(spiral);
                arm = pow(arm, 2.2);

                // неровность рукавов и пылевые прожилки — крутятся вместе с рукавами
                float a2 = ang + spin;
                float2 q = float2(cos(a2), sin(a2)) * r;
                float n = Fbm(q * 6.0 + 3.0);
                float lanes = smoothstep(0.35, 0.75, 0.5 + 0.5 * cos(spiral + 1.9 + n * 2.0));

                // искры: звёзды, вспыхивающие в рукавах (ячейки во вращающихся координатах)
                float2 cell = floor(q * 38.0);
                float2 fr = frac(q * 38.0) - 0.5;
                float h = Hash(cell);
                float tw = 0.5 + 0.5 * sin(_T * (1.2 + h * 2.5) + h * 40.0);
                float spark = step(0.86, h) * exp(-dot(fr, fr) * 60.0) * tw * tw
                            * exp(-r * r / (2.0 * 0.4 * 0.4)) * smoothstep(0.04, 0.12, r) * (0.4 + arm);

                float hot = exp(-r * r / (2.0 * 0.035 * 0.035)) * 2.2;
                float bulge = exp(-r * r / (2.0 * 0.11 * 0.11)) * 0.9;
                float halo = exp(-r * r / (2.0 * 0.30 * 0.30)) * 0.32;
                float armsI = arm * (0.35 + 0.65 * n) * exp(-r * r / (2.0 * 0.45 * 0.45)) * smoothstep(0.05, 0.2, r) * 0.6;
                float dust = 1.0 - lanes * 0.55 * smoothstep(0.06, 0.18, r) * (1.0 - smoothstep(0.55, 0.9, r));

                float3 col = _Hot.rgb * hot * pulse
                           + _Warm.rgb * bulge * pulse
                           + lerp(_Warm.rgb, _Outer.rgb, saturate(r * 1.6)) * (halo * haloPulse + armsI);
                col *= dust;
                col += _Hot.rgb * spark * 1.6;
                col *= 1.0 - smoothstep(0.8, 1.0, r);
                return fixed4(col * _Intensity, 1.0);
            }
            ENDCG
        }
    }
}
