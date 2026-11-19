// Космическая фауна: «Кристаллид». Гранёный меш (плоские нормали): перламутровый перелив по углу обзора
// (косинусная палитра), резкий блик и вспышка _Flash, когда по кристаллу проходит луч пульсара.
Shader "Astropolity/FaunaCrystal"
{
    Properties
    {
        _Tint ("Tint", Color) = (0.23, 0.49, 0.85, 1)
        _Flash ("Flash", Float) = 0
        _Alpha ("Alpha", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Tint;
            float _Flash, _Alpha;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityWorldToClipPos(wp);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.v = normalize(_WorldSpaceCameraPos - wp);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.n), v = normalize(i.v);
                float f = 1.0 - abs(dot(n, v));
                float3 irid = 0.5 + 0.5 * cos(6.2831853 * (float3(0.0, 0.33, 0.67) + f * 1.3 + _Time.y * 0.05 + n.y * 0.35));
                float3 l = normalize(float3(0.3, 0.8, 0.5));
                float spec = pow(saturate(dot(reflect(-v, n), l)), 22.0);
                float3 c = _Tint.rgb * 0.18 + irid * (0.18 + f * 0.85) + spec * 1.1 + _Flash * float3(0.8, 0.95, 1.2);
                return fixed4(c, 0.92 * _Alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
