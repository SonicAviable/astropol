// Голографический перелив для неоновых контуров интерфейса боя (board_lines, holo_ring_*, holo_rays).
// Свет складывается со сценой; цвет контура плывёт по радуге вдоль экрана, по диагонали время от времени
// пробегает белый блик — как перелив голограммы на коллекционной карте.
Shader "Astropolity/UI/Holo"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Iri ("Iridescence", Range(0,1)) = 0.45
        _HueScale ("Hue scale", Float) = 1.2
        _HueSpeed ("Hue speed", Float) = 0.05
        _SweepSpeed ("Sweep speed", Float) = 0.12
        _SweepWidth ("Sweep width", Float) = 0.06
        _SweepGain ("Sweep gain", Float) = 1.6
        _Pulse ("Pulse", Range(0,1)) = 0.12
        _Gain ("Gain", Float) = 1.0
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
            float _Iri, _HueScale, _HueSpeed, _SweepSpeed, _SweepWidth, _SweepGain, _Pulse, _Gain;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.wpos = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            float3 Hue(float h)
            {
                return saturate(abs(frac(h + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);
                float t = _Time.y;
                float3 baseRgb = tex.rgb * tex.a * i.color.rgb;
                float lum = dot(baseRgb, float3(0.3, 0.59, 0.11));
                float3 iri = lerp(float3(1, 1, 1), Hue(dot(i.uv, float2(_HueScale, _HueScale * 0.55)) - t * _HueSpeed), 0.65);
                float3 rgb = lerp(baseRgb, iri * lum * 1.6, _Iri);
                // блик по диагонали: проходит экран и пропадает, потом снова
                float s = i.uv.x * 0.8 + i.uv.y * 0.45;
                float ph = frac(t * _SweepSpeed) * 2.6 - 0.6;
                float d = (s - ph) / max(0.001, _SweepWidth);
                float band = exp(-d * d);
                rgb += (iri * 0.5 + 0.5) * tex.a * band * _SweepGain * i.color.rgb;
                rgb *= (1.0 + _Pulse * sin(t * 2.0)) * _Gain;
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
