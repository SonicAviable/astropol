// Кольца газового гиганта: плоское кольцо в плоскости XZ объекта (uv.x — радиус 0..1 от внутреннего края к внешнему).
// Цвет и прозрачность — из полоски-текстуры (RGBA), тень планеты (радиус 1 в осях объекта) — по _SunDir.
Shader "Astropolity/Planet/AstroRing"
{
    Properties
    {
        _MainTex ("Ring strip (RGBA)", 2D) = "white" {}
        _Hue ("Hue shift", Float) = 0
        _Sat ("Saturation", Float) = 1
        _Bright ("Brightness", Float) = 1.1
        _SunDir ("Sun direction (world)", Vector) = (0,0,1,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
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
            float _Hue, _Sat, _Bright;
            float4 _SunDir;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 obj : TEXCOORD1; };
            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                o.obj = v.vertex.xyz;
                return o;
            }
            float Lum(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }
            float3 HueShift(float3 c, float a) { const float3 k = 0.57735; float ca = cos(a); return c * ca + cross(k, c) * sin(a) + k * dot(k, c) * (1 - ca); }
            fixed4 frag(v2f i) : SV_Target
            {
                float4 rt = tex2D(_MainTex, float2(saturate(i.uv.x), 0.5));
                float3 c = HueShift(rt.rgb, _Hue);
                c = max(lerp(Lum(c).xxx, c, _Sat), 0);
                float3 L = normalize(mul((float3x3)unity_WorldToObject, _SunDir.xyz));
                float3 pr = i.obj;
                float bb = dot(pr, L), cc = dot(pr, pr) - 1, disc = bb * bb - cc;
                float shade = (disc > 0 && -bb > 0) ? 0.1 : 1;
                return fixed4(c * shade * _Bright, rt.a * 0.92);
            }
            ENDCG
        }
    }
    Fallback Off
}
