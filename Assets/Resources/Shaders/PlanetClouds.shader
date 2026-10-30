// Облака планеты: полупрозрачная оболочка над поверхностью; плотность — из маски (_MainTex.r).
// Освещение — по направлению на звезду (_SunDir, мир): мягкий терминатор, ночью облака почти не видны.
Shader "Astropolity/Planet/Clouds"
{
    Properties
    {
        _MainTex ("Clouds mask (R)", 2D) = "black" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.9
        _Ambient ("Night ambient", Range(0, 1)) = 0.05
        _SunDir ("Sun direction (world)", Vector) = (0, 0, 1, 0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+1" "RenderType"="Transparent" "IgnoreProjector"="True" }
        // Альфа отдельно: в рендер-текстуре планета под облаками остаётся непрозрачной
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _Opacity;
            float _Ambient;
            float4 _SunDir;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
                float3 wn  : TEXCOORD1;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.wn = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float m = tex2D(_MainTex, i.uv).r;
                float nl = dot(normalize(i.wn), normalize(_SunDir.xyz));
                float lit = saturate(nl * 1.15 + 0.12);
                float3 col = _Color.rgb * lerp(_Ambient, 1.0, lit);
                float a = m * _Opacity * (0.30 + 0.70 * saturate(nl + 0.45));
                return float4(col, a);
            }
            ENDCG
        }
    }
}
