// Astropolity — «живой» портрет правителя.
//
// Статичная картинка оживает без видео и без риггинга:
//   • медленный наезд камеры и лёгкий параллакс фона относительно фигуры
//   • дыхание: плечи и грудь чуть поднимаются, голова едва покачивается
//   • моргание: веко из размытого тона кожи + линия ресниц (координаты глаз — из кода)
//   • мерцание огней интерьера и контрового света (маска R из _FxTex)
//   • едва заметная полоса развёртки «канала связи» и редкий сбой сигнала
//
// _FxTex: R — огни и контровой свет, G — силуэт фигуры, B — вес дыхания.
// Параметры анимации выставляет LeaderPortraitView каждый кадр (_T, _Blink, _Glitch).
Shader "Astropolity/UI/LeaderPortrait"
{
    Properties
    {
        [PerRendererData] _MainTex ("Portrait", 2D) = "white" {}
        _FxTex ("FX mask (R lights, G figure, B breath)", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Accent ("Accent", Color) = (0.4,0.86,0.82,1)

        _EyeL ("Left eye (u, v, rx, ry)", Vector) = (0,0,0,0)
        _EyeR ("Right eye (u, v, rx, ry)", Vector) = (0,0,0,0)
        _T ("Time", Float) = 0
        _Blink ("Blink", Range(0,1)) = 0
        _Glitch ("Glitch", Range(0,1)) = 0
        _Breath ("Breath amplitude (uv)", Float) = 0.0065
        _Motion ("Motion amount", Range(0,1)) = 1
        _UvRect ("Visible uv rect (x, y, w, h)", Vector) = (0,0,1,1)
        _FadeLeft ("Fade left edge (0..1 of width)", Float) = 0
        _Fade ("Edge fade (left, right, bottom, top)", Vector) = (0,0,0,0)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "LeaderPortrait"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
                float2 uv    : TEXCOORD0;
                float4 wpos  : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _FxTex;
            fixed4 _Color;
            fixed4 _Accent;
            float4 _EyeL, _EyeR;
            float _T, _Blink, _Glitch, _Breath, _Motion, _FadeLeft;
            float4 _UvRect;
            float4 _Fade;
            static float2 s_dx, s_dy;   // градиенты uv — для выборок внутри ветвлений
            float4 _ClipRect;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.wpos = v.vertex;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            // Маска хранится как обычная (sRGB) картинка: в линейном пространстве возвращаем исходные значения
            float4 Fx(float2 uv)
            {
                float4 m = tex2D(_FxTex, uv);
                #ifndef UNITY_COLORSPACE_GAMMA
                m.rgb = LinearToGammaSpace(m.rgb);
                #endif
                return m;
            }

            float3 Blur(float2 uv) { return tex2Dlod(_MainTex, float4(uv, 0, 4.0)).rgb; }
            float3 Sharp(float2 uv) { return tex2Dgrad(_MainTex, uv, s_dx, s_dy).rgb; }

            // Веко опускается сверху вниз; возвращает новый цвет пикселя
            float3 Eyelid(float3 col, float2 uv, float4 eye, float blink)
            {
                if (blink < 0.02 || eye.z <= 0) return col;
                float2 d = uv - eye.xy;
                if (abs(d.x) > eye.z * 1.1 || abs(d.y) > eye.w * 1.6) return col;

                float px = _MainTex_TexelSize.y;                 // высота пикселя в uv
                float nx = d.x / eye.z;
                float k = sqrt(saturate(1.0 - nx * nx));         // форма глаза (миндаль)
                float top = eye.y + eye.w * k * 1.05;            // верхний край (v растёт вверх)
                float bot = eye.y - eye.w * k * 0.95;
                float down = top - uv.y;                         // сколько ниже верхнего края
                float lidD = (top - bot) * blink + px * 2.0;     // где сейчас край века

                float hor = smoothstep(0.0, 0.35, k);
                float cover = smoothstep(-1.5 * px, 0.5 * px, down) * (1.0 - smoothstep(lidD - px, lidD + px, down)) * hor;

                // Тон кожи века — размытая кожа над и под глазом, с объёмом к краю
                float3 skin = 0.5 * Blur(float2(uv.x, eye.y + eye.w * 1.9)) + 0.5 * Blur(float2(uv.x, eye.y - eye.w * 1.9));
                float depth = saturate(down / max(lidD, 1e-5));
                float3 lid = skin * (0.86 - 0.22 * depth * depth);
                // Немного фактуры кожи сверху, чтобы веко не было «заплаткой»
                float2 tuv = float2(uv.x, uv.y + (top - bot) + eye.w * 0.3);
                lid += (Sharp(tuv) - Blur(tuv)) * 0.5;

                col = lerp(col, lid, cover);
                // Линия ресниц по краю века
                float lash = exp(-pow((down - lidD) / (1.3 * px), 2.0)) * hor * saturate(blink * 2.5);
                return col * (1.0 - 0.55 * lash);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float t = _T;
                float2 uv = IN.uv;
                s_dx = ddx(IN.uv);
                s_dy = ddy(IN.uv);
                float m = _Motion;

                // 1. Медленный наезд и дрейф
                float zoom = 1.0 + m * (0.018 + 0.008 * sin(t * 0.09));
                float2 c = float2(0.5, 0.62);
                uv = c + (uv - c) / zoom + m * float2(sin(t * 0.050), cos(t * 0.041)) * 0.0022;

                // 2. Сбой сигнала: короткий горизонтальный сдвиг полосы
                if (_Glitch > 0.001)
                {
                    float band = step(0.82, frac(sin(floor(uv.y * 38.0 + floor(t * 24.0)) * 43758.5453)));
                    uv.x += band * _Glitch * 0.012;
                }

                float4 fx = Fx(uv);
                float fig = fx.g;

                // 3. Параллакс: фон плывёт сильнее фигуры
                uv += (1.0 - fig) * m * float2(sin(t * 0.13), sin(t * 0.11) * 0.6) * 0.0035;

                // 4. Дыхание (~4.6 с на вдох-выдох): грудь и плечи поднимаются
                float br = sin(t * 6.2831853 / 4.6);
                uv.y -= br * _Breath * m * fx.b;

                fixed4 col = tex2D(_MainTex, uv);

                // 5. Моргание
                col.rgb = Eyelid(col.rgb, uv, _EyeL, _Blink);
                col.rgb = Eyelid(col.rgb, uv, _EyeR, _Blink);

                // 6. Огни интерьера и контровой свет «живут»
                float lights = fx.r;
                float flicker = 0.08 * sin(t * 1.7) + 0.05 * sin(t * 5.3 + uv.y * 9.0) + 0.04 * sin(t * 13.1 + uv.x * 21.0);
                col.rgb += col.rgb * lights * flicker * 2.0 * m;
                col.rgb += _Accent.rgb * lights * 0.05 * (0.5 + 0.5 * sin(t * 0.8 + uv.y * 24.0)) * m;

                // 7. Еле заметная полоса развёртки канала связи
                float sweep = frac(uv.y * 0.5 - t * 0.035);
                float s = exp(-pow((sweep - 0.5) * 34.0, 2.0));
                col.rgb += _Accent.rgb * s * 0.03 * m;

                // Сбой: лёгкое смещение каналов
                if (_Glitch > 0.001)
                    col.r = lerp(col.r, Sharp(uv + float2(0.004 * _Glitch, 0)).r, _Glitch);

                col *= IN.color;

                // Мягкий левый край — когда портрет лежит поверх другой картинки
                float4 fade = max(_Fade, float4(_FadeLeft, 0, 0, 0));
                if (dot(fade, 1.0) > 0.001)
                {
                    float lx = (IN.uv.x - _UvRect.x) / max(_UvRect.z, 1e-5);
                    float ly = (IN.uv.y - _UvRect.y) / max(_UvRect.w, 1e-5);
                    if (fade.x > 0.001) col.a *= smoothstep(0.0, fade.x, lx);
                    if (fade.y > 0.001) col.a *= smoothstep(0.0, fade.y, 1.0 - lx);
                    if (fade.z > 0.001) col.a *= smoothstep(0.0, fade.z, ly);
                    if (fade.w > 0.001) col.a *= smoothstep(0.0, fade.w, 1.0 - ly);
                }

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.wpos.xy, _ClipRect);
                #endif
                return col;
            }
            ENDCG
        }
    }
}
