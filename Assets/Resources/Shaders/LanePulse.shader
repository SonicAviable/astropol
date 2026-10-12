// Световые импульсы по гиперкоридору: «кометы» (яркая голова, гаснущий хвост) бегут от начала линии к концу.
// Для LineRenderer с textureMode = Stretch: uv.x — 0..1 вдоль линии, uv.y — поперёк.
// _Len — длина линии в мире (задаётся через MaterialPropertyBlock), шаг и скорость — в мировых единицах.
Shader "Astropolity/LanePulse"
{
    Properties
    {
        _Color ("Color", Color) = (0.4, 0.95, 1, 1)
        _Len ("Length", Float) = 10
        _Spacing ("Pulse spacing", Float) = 4
        _Speed ("Pulse speed", Float) = 5
        _Base ("Base glow", Float) = 0.22
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Len, _Spacing, _Speed, _Base;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float s = i.uv.x * _Len;                                   // расстояние от начала линии
                float ph = frac((s - _Time.y * _Speed) / max(_Spacing, 0.01));
                float pulse = pow(ph, 7.0);                                // голова впереди, хвост позади
                float across = 1.0 - abs(i.uv.y * 2.0 - 1.0);
                across = across * across * (3.0 - 2.0 * across);
                float ends = smoothstep(0.0, 0.8, s) * smoothstep(0.0, 0.8, _Len - s);
                float a = (_Base + pulse * 1.8) * across * ends;
                return fixed4(_Color.rgb * i.color.rgb, saturate(a * _Color.a * i.color.a));
            }
            ENDCG
        }
    }
}
