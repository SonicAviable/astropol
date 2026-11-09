// Космическая фауна: «Солнечный скат» (и плавники Странника). Плоское тело-крыло из сетки: форму, стреловидность
// и взмах (волна от центра к кончикам) задаёт вершинный шейдер по uv — позиции вершин меша не важны.
//   uv.x — размах (0..1 → −1..1), uv.y — от хвоста (0) к голове (1). Голова смотрит по +Z.
// Светится кромка крыла, хребет и два ряда огней вдоль спины; тело тёмное, полупрозрачное.
// _Phase, _Flap, _Glow, _Body, _Alpha — через MaterialPropertyBlock.
Shader "Astropolity/FaunaRay"
{
    Properties
    {
        _Glow ("Glow", Color) = (0.36, 0.95, 0.88, 1)
        _Body ("Body", Color) = (0.05, 0.12, 0.22, 1)
        _Phase ("Phase", Float) = 0
        _Flap ("Flap speed", Float) = 2.1
        _Alpha ("Alpha", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Glow, _Body;
            float _Phase, _Flap, _Alpha;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 s : TEXCOORD0; float f : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                float s = v.uv.x * 2.0 - 1.0, l = v.uv.y;
                float w = pow(sin(l * 3.14159265), 0.75) * (0.5 + 0.5 * smoothstep(0.1, 0.65, l)) * (1.0 - smoothstep(0.82, 1.0, l) * 0.5);
                float x = s * w * 1.6;
                float z = (l - 0.5) * 1.7 - abs(s) * w * 0.6;
                float f = sin(_Time.y * _Flap + _Phase - abs(x) * 1.2);
                float y = f * x * x * 0.34;
                o.pos = UnityObjectToClipPos(float4(x, y, z, 1.0));
                o.s = float2(s, l);
                o.f = f;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float s = i.s.x, l = i.s.y;
                float edge = smoothstep(0.8, 1.0, abs(s));
                float spine = exp(-s * s * 120.0) * smoothstep(0.1, 0.45, l);
                float spots = smoothstep(0.6, 1.0, sin(l * 46.0)) * smoothstep(0.08, 0.0, abs(abs(s) - 0.42)) * smoothstep(0.15, 0.5, l);
                float vein = pow(abs(sin(s * 9.0 + l * 5.0)), 40.0) * 0.35;
                float light = (edge * 1.25 + spine * 0.9 + spots * 1.1 + vein) * (0.75 + 0.35 * i.f);
                float3 c = _Body.rgb * (0.55 + 0.45 * l) + _Glow.rgb * light;
                return fixed4(c, (0.86 + edge * 0.14) * _Alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
