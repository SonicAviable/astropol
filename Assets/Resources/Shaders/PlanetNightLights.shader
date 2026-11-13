// Огни городов на ночной стороне: тонкая оболочка у самой поверхности, складывается со светом.
// Ночная маска — по направлению на звезду (_SunDir, мир); у терминатора огни плавно зажигаются.
Shader "Astropolity/Planet/NightLights"
{
    Properties
    {
        _MainTex ("Lights", 2D) = "black" {}
        _Color ("Tint", Color) = (1, 0.82, 0.55, 1)
        _Intensity ("Intensity", Range(0, 6)) = 2.2
        _SunDir ("Sun direction (world)", Vector) = (0, 0, 1, 0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One
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
            float _Intensity;
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
                float3 l = tex2D(_MainTex, i.uv).rgb;
                float nl = dot(normalize(i.wn), normalize(_SunDir.xyz));
                float night = smoothstep(0.05, -0.18, nl);
                float lum = max(l.r, max(l.g, l.b));
                float3 c = _Color.rgb * smoothstep(0.06, 0.6, lum) * night * _Intensity;
                return float4(c, 0);
            }
            ENDCG
        }
    }
}
