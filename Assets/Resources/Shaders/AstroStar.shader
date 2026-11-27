// Звезда системы: кипящая грануляция, пятна, потемнение к краю. Цвет HDR — светится под Bloom.
// _A — тёмный цвет (пятна, межгранулярные тени), _B — горячий. Оба — sRGB-векторы.
Shader "Astropolity/Star/Surface"
{
    Properties
    {
        _A ("Cool color", Vector) = (0.72,0.33,0.05,0)
        _B ("Hot color", Vector) = (1,0.9,0.63,0)
        _Seed ("Seed", Float) = 0
        _Spin ("Spin (rad/s)", Float) = 0.03
        _Intensity ("Intensity", Float) = 2.4
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" "IgnoreProjector"="True" }
        Pass
        {
            Lighting Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "AstroNoise.cginc"
            float4 _A, _B;
            float _Seed, _Spin, _Intensity;
            struct v2f { float4 pos : SV_POSITION; float3 obj : TEXCOORD0; float3 wpos : TEXCOORD1; };
            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.obj = v.vertex.xyz;
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;
                float3 pn = normalize(i.obj);
                float3 p = RotY(pn, t * _Spin);
                float3 N = normalize(mul((float3x3)unity_ObjectToWorld, pn));
                float nv = saturate(dot(N, normalize(_WorldSpaceCameraPos - i.wpos)));
                float3 A = pow(_A.rgb, 2.2), B = pow(_B.rgb, 2.2);
                float3 q = p * 5 + _Seed;
                float g = fbm(q + 0.8 * fbm(q * 0.6 + t * 0.07, 2) + float3(0, 0, t * 0.05), 5);
                float3 c = lerp(A, B, smoothstep(-0.35, 0.45, g));
                float sp = smoothstep(0.6, 0.75, snoise(p * 2.2 + _Seed + 3) * 0.5 + 0.5);
                c *= 1 - 0.65 * sp;
                c *= (0.3 + 0.9 * pow(nv, 0.45)) * _Intensity;
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
