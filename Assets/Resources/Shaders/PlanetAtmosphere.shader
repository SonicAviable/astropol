// Атмосфера планеты на оболочке чуть больше планеты (радиус оболочки _ShellRadius в долях радиуса планеты).
// Проход 1 (задние грани): ореол вокруг диска — яркий у самого края планеты и тающий к краю оболочки;
//   внутри диска задние грани закрыты самой планетой по глубине.
// Проход 2 (передние грани): дымка по краю диска (френель).
// Оба прохода светятся только с дневной стороны и у терминатора (_SunDir — направление на звезду, мир).
Shader "Astropolity/Planet/Atmosphere"
{
    Properties
    {
        _Color ("Color", Color) = (0.35, 0.65, 1, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1.2
        _ShellRadius ("Shell radius (planet = 1)", Float) = 1.06
        _SunDir ("Sun direction (world)", Vector) = (0, 0, 1, 0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+2" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Lighting Off

        CGINCLUDE
        #include "UnityCG.cginc"
        float4 _Color;
        float _Intensity;
        float _ShellRadius;
        float4 _SunDir;

        struct v2f
        {
            float4 pos : SV_POSITION;
            float3 wp  : TEXCOORD0;
            float3 wn  : TEXCOORD1;
        };

        v2f vert(appdata_base v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
            o.wn = UnityObjectToWorldNormal(v.normal);
            return o;
        }

        float3 Center() { return mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz; }
        // Радиус планеты в мире: оболочка в _ShellRadius раз больше
        float PlanetRadius() { return length(mul((float3x3)unity_ObjectToWorld, float3(1, 0, 0))) / max(_ShellRadius, 1.0001); }
        float DayLight(float3 n) { return saturate(dot(n, normalize(_SunDir.xyz)) * 0.85 + 0.30); }
        ENDCG

        Pass
        {
            Name "Halo"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragHalo

            float4 fragHalo(v2f i) : SV_Target
            {
                float3 cam = _WorldSpaceCameraPos;
                float3 c = Center();
                float r = PlanetRadius();
                float3 dir = normalize(i.wp - cam);
                float3 closest = cam + dir * dot(c - cam, dir);      // ближайшая к центру точка луча
                float b = length(closest - c) / r;                   // 1 — край планеты, _ShellRadius — край оболочки
                float x = saturate((b - 1.0) / max(_ShellRadius - 1.0, 1e-3));
                float glow = pow(1.0 - x, 3.0) * step(1.0, b);
                float3 n = normalize(closest - c);
                float a = glow * DayLight(n) * _Intensity;
                // Альфа — для рендер-текстуры экрана колонии (прозрачный фон): иначе ореол за краем диска пропал бы
                return float4(_Color.rgb * a, saturate(a));
            }
            ENDCG
        }

        Pass
        {
            Name "Rim"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragRim

            float4 fragRim(v2f i) : SV_Target
            {
                float3 n = normalize(i.wn);
                float3 v = normalize(_WorldSpaceCameraPos - i.wp);
                float fres = pow(1.0 - saturate(dot(n, v)), 3.0);
                float a = fres * DayLight(n) * _Intensity * 0.55;
                return float4(_Color.rgb * a, 0);
            }
            ENDCG
        }
    }
}
