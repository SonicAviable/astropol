// Эффекты карточного боя со «своим светом»: яркость — в альфе текстуры (флипбуки fx2_*, Tools/card_fx/gen_fx.py).
// Тусклые края окрашиваются оттенком, яркое ядро уходит в белый — как раскалённая вспышка в больших карточных играх.
// Одна текстура — любой цвет: золото усиления, бирюза захвата, красный урона. Складывается со столом как свет.
Shader "Astropolity/UI/FxRamp"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Core ("Core threshold", Range(0,1)) = 0.55
        _Gain ("Gain", Float) = 1.5
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
        Blend SrcAlpha One
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            struct appdata_t { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 wpos : TEXCOORD1; };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float4 _ClipRect;
            float _Core, _Gain;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.wpos = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float lum = tex2D(_MainTex, i.uv).a;
                float core = saturate((lum - _Core) / max(0.001, 1.0 - _Core));
                float3 rgb = lerp(i.color.rgb, float3(1, 1, 1), core) * lum * _Gain;
                float k = i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                k *= UnityGet2DClipping(i.wpos.xy, _ClipRect);
                #endif
                return fixed4(rgb, k);
            }
            ENDCG
        }
    }
}
