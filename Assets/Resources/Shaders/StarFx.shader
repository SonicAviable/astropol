// Слой эффекта звезды (лучи пульсара, оболочки, петли, туманности): текстура с альфой × цвет, складывается со светом.
// _Color — sRGB-вектор (как у AstroCorona), a — прозрачность; _Intensity — яркость HDR под Bloom.
// Цвет текстуры тоже участвует: у туманностей он «запечён» в текстуру, у остальных она белая.
Shader "Astropolity/Star/Fx"
{
    Properties
    {
        _MainTex ("Shape (RGBA)", 2D) = "white" {}
        _Color ("Color (sRGB) + alpha", Vector) = (1,1,1,1)
        _Intensity ("Intensity", Float) = 1.6
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend One One
        ZWrite Off
        Cull Off
        Lighting Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _Color;
            float _Intensity;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 s = tex2D(_MainTex, i.uv);
                float3 c = pow(saturate(_Color.rgb), 2.2) * pow(s.rgb, 2.2) * s.a * _Color.a * _Intensity;
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
