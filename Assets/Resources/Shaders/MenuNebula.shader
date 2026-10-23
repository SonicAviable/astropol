// Astropolity — «живая» туманность главного меню из статичной картинки.
//   • облака медленно текут (плавное смещение uv шумом, только там, где есть облака)
//   • облака едва заметно «дышат» яркостью
//   • звёзды (выделяются как резкие точки над размытым фоном) спокойно мерцают, каждая в своём ритме
// _T выставляет MenuAtmosphere каждый кадр.
Shader "Astropolity/UI/MenuNebula"
{
    Properties
    {
        [PerRendererData] _MainTex ("Nebula", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _T ("Time", Float) = 0
        _Flow ("Cloud flow amount (uv)", Float) = 0.006
        _Twinkle ("Star twinkle", Range(0,1)) = 0.6

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            float _T, _Flow, _Twinkle;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
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
                for (int k = 0; k < 4; k++) { v += a * Noise(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return v;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float2 dx = ddx(uv), dy = ddy(uv);

                // где облака — там и течение (по сильно размытому образу)
                float3 haze = tex2Dlod(_MainTex, float4(uv, 0, 5.0)).rgb;
                float cloud = saturate(dot(haze, float3(0.33, 0.34, 0.33)) * 3.5);

                float2 p = uv * float2(2.6, 1.5);
                float2 flow = float2(Fbm(p + float2(_T * 0.021, _T * 0.012)),
                                     Fbm(p + float2(5.2, 1.3) - float2(_T * 0.016, _T * 0.019))) - 0.5;
                float3 col = tex2Dgrad(_MainTex, uv + flow * _Flow * cloud, dx, dy).rgb;

                // медленное «дыхание» облаков
                col *= 1.0 + 0.07 * cloud * sin(_T * 0.23 + uv.x * 4.0 + uv.y * 2.0);

                // звёзды: резкие точки над размытым фоном
                float3 sharp = tex2Dgrad(_MainTex, uv, dx, dy).rgb;
                float3 soft = tex2Dlod(_MainTex, float4(uv, 0, 3.0)).rgb;
                float star = saturate((dot(sharp - soft, float3(0.3, 0.4, 0.3)) - 0.035) * 5.0);
                float2 cell = floor(uv * _MainTex_TexelSize.zw / 7.0);
                float ph = Hash(cell) * 6.2832;
                float sp = 0.5 + Hash(cell + 7.3) * 1.3;
                float tw = 0.5 + 0.5 * sin(_T * sp + ph);
                col = lerp(col, sharp * lerp(1.0, 0.45 + 0.85 * tw, _Twinkle), star);

                return fixed4(col, 1.0) * i.color;
            }
            ENDCG
        }
    }
}
