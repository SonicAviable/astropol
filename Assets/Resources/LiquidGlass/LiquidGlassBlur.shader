// Astropolity — размытие фона для Liquid Glass (dual Kawase).
// Используется LiquidGlassBackdropPass через Blitter (Render Graph).
Shader "Hidden/Astropolity/LiquidGlassBlur"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        HLSLINCLUDE
        #pragma target 3.0
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        // xy — размер texel источника, z — смещение выборки
        float4 _LGBlurParams;

        half3 Src(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
        }

        half4 FragDown(Varyings i) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            float2 uv = i.texcoord;
            float2 o = _LGBlurParams.xy * _LGBlurParams.z;
            half3 s = Src(uv) * 4.0;
            s += Src(uv + float2(-o.x, -o.y));
            s += Src(uv + float2( o.x, -o.y));
            s += Src(uv + float2(-o.x,  o.y));
            s += Src(uv + float2( o.x,  o.y));
            return half4(s * 0.125, 1.0);
        }

        half4 FragUp(Varyings i) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            float2 uv = i.texcoord;
            float2 o = _LGBlurParams.xy * _LGBlurParams.z;
            half3 s = Src(uv + float2(-o.x * 2.0, 0.0));
            s += Src(uv + float2(-o.x,  o.y)) * 2.0;
            s += Src(uv + float2( 0.0,  o.y * 2.0));
            s += Src(uv + float2( o.x,  o.y)) * 2.0;
            s += Src(uv + float2( o.x * 2.0, 0.0));
            s += Src(uv + float2( o.x, -o.y)) * 2.0;
            s += Src(uv + float2( 0.0, -o.y * 2.0));
            s += Src(uv + float2(-o.x, -o.y)) * 2.0;
            return half4(s / 12.0, 1.0);
        }
        ENDHLSL

        Pass
        {
            Name "LiquidGlassDown"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDown
            ENDHLSL
        }

        Pass
        {
            Name "LiquidGlassUp"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragUp
            ENDHLSL
        }
    }
}
