// Гиперкоридор «Гиперпоток»: светящийся поток по дуге между краями систем.
//   • тонкая яркая сердцевина и мягкий ореол поперёк линии;
//   • по потоку бегут волны энергии и редкие яркие сгустки (_Time, без скриптов);
//   • ширину сужает к концам LineRenderer.widthCurve, яркость к концам гаснет здесь.
// LineRenderer с textureMode = Stretch: uv.x — 0..1 вдоль линии, uv.y — поперёк. Цвет и прозрачность концов —
// из градиента вершин (владелец коридора, туман войны). _Len — длина в мире, _Phase — сдвиг узора (MaterialPropertyBlock).
Shader "Astropolity/LaneFlow"
{
    Properties
    {
        _Len ("Length", Float) = 10
        _Phase ("Phase", Float) = 0
        _Flow ("Flow speed", Float) = 2.4
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
            float _Len, _Phase, _Flow;
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
                float x = i.uv.x;
                float y = abs(i.uv.y - 0.5) * 2.0;                 // 0 — ось, 1 — край ленты
                float core = exp(-y * y * 30.0);
                float halo = exp(-y * y * 4.0) * 0.26;
                float t = _Time.y * _Flow;
                // Волны энергии (длина волны ~5 ед.) и редкие сгустки (~раз в 23 ед.), бегут вдоль потока
                float wave = 0.55 + 0.45 * sin(x * _Len / 5.0 - t + _Phase);
                float spark = pow(saturate(sin(x * _Len / 23.0 - t * 0.35 + _Phase * 2.0)), 22.0);
                float ends = smoothstep(0.0, 0.08, x) * smoothstep(0.0, 0.08, 1.0 - x);
                float taper = 0.35 + 0.65 * sin(x * 3.14159265);
                float a = (core * (0.55 + 0.45 * wave + spark) + halo * (0.6 + 0.4 * wave)) * taper * ends;
                float3 c = lerp(i.color.rgb, float3(1, 1, 1), saturate(core * (0.3 * wave + spark)));
                return fixed4(c, saturate(a) * i.color.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
