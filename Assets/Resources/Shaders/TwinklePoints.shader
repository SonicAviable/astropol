// Astropolity — неразрешённые звёзды галактики (облако точек): каждая спокойно мерцает в своём ритме.
// Фаза и скорость — из хэша позиции вершины. _Color — общее затухание на зуме.
Shader "Astropolity/TwinklePoints"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _T ("Time", Float) = 0
        _Amount ("Twinkle amount", Range(0,1)) = 0.55
    }
    SubShader
    {
        Tags { "Queue"="Transparent-35" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; };

            fixed4 _Color;
            float _T, _Amount;

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float h = Hash(v.vertex.xz);
                float sp = 0.4 + Hash(v.vertex.zx + 3.1) * 1.4;
                float tw = 0.5 + 0.5 * sin(_T * sp + h * 6.2832);
                float k = lerp(1.0, 0.35 + 0.95 * tw, _Amount);
                o.color = v.color * _Color;
                o.color.a *= k;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
