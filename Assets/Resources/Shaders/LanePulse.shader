// Маршрут флота по гиперкоридору — тонкая голографическая линия:
//   • яркая сердцевина-волосок и едва заметный ореол (не сплошная полоса);
//   • короткие штрихи, спокойно бегущие к цели;
//   • редкая «комета» — яркая голова с коротким хвостом;
//   • линия гаснет у начала (у корабля) и у конца (не лезет в свечение звезды).
// Для LineRenderer с textureMode = Stretch: uv.x — 0..1 вдоль линии, uv.y — поперёк.
// _Len — длина линии в мире (MaterialPropertyBlock), остальные величины — в мировых единицах.
Shader "Astropolity/LanePulse"
{
    Properties
    {
        _Color ("Color", Color) = (0.4, 0.95, 1, 1)
        _Len ("Length", Float) = 10
        _Spacing ("Comet spacing", Float) = 9
        _Speed ("Comet speed", Float) = 4
        _Dash ("Dash period", Float) = 0.9
        _Base ("Base line", Float) = 0.18
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
            float _Len, _Spacing, _Speed, _Dash, _Base;

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
                float s = i.uv.x * _Len;                              // расстояние от начала линии
                float y = abs(i.uv.y * 2.0 - 1.0);                    // 0 — ось линии, 1 — край
                float core = exp(-y * y * 30.0);                       // волосок по центру
                float halo = exp(-y * y * 4.0) * 0.16;                 // мягкий ореол

                // Штрихи: короткие отрезки, бегущие к цели
                float dp = frac((s - _Time.y * _Speed * 0.3) / max(_Dash, 0.05));
                float dash = smoothstep(0.0, 0.08, dp) * (1.0 - smoothstep(0.5, 0.58, dp));

                // Редкая комета: яркая голова, короткий хвост позади
                float cp = frac((s - _Time.y * _Speed) / max(_Spacing, 0.1));
                float comet = pow(cp, 16.0);

                float ends = smoothstep(0.0, 1.4, s) * smoothstep(0.0, 2.4, _Len - s);
                float a = (core * (_Base + 0.5 * dash + 1.7 * comet) + halo * (0.35 + comet)) * ends;
                float3 c = _Color.rgb * i.color.rgb;
                c = lerp(c, float3(1, 1, 1), comet * core * 0.5);      // голова кометы — почти белая
                return fixed4(c, saturate(a * _Color.a * i.color.a));
            }
            ENDCG
        }
    }
}
