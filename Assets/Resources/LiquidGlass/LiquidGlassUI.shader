// Astropolity — UI shader («тактический» стиль интерфейса).
//
// Один проход рисует элемент интерфейса целиком — плоско, без стекла, размытия и бликов:
//   • окна — непрозрачное тёмное тело, тонкая кромка и неоновая линия по верхнему краю, мягкая тень;
//   • вложенные пластины — ровная заливка чуть светлее фона, тонкая кромка;
//   • кнопки — капсулы: тёмное полупрозрачное тело и тонкая кромка, при наведении кромка светлеет;
//   • линии, точки, полосы прогресса — ровный цвет с лёгким ореолом;
//   • скрим под модальными окнами — ровное затемнение.
//
// Параметры приходят через UV-каналы вершин (см. LiquidGlassEffect.cs),
// поэтому все элементы делят один материал и батчатся.
//   uv0 = (local.x, local.y, halfW, halfH)
//   uv1 = радиусы углов (TL, TR, BR, BL)
//   uv2 = цвет кромки (linear)
//   uv3 = 4 × (3 байта) упакованных параметра
Shader "Astropolity/UI/LiquidGlass"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
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
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "TacticalUI"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_lg
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float4 uv0    : TEXCOORD0;
                float4 uv1    : TEXCOORD1;
                float4 uv2    : TEXCOORD2;
                float4 uv3    : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos       : SV_POSITION;
                float4 color     : COLOR;
                float4 local     : TEXCOORD0; // xy — позиция от центра, zw — half size
                float4 radii     : TEXCOORD1; // TL TR BR BL
                float4 rim       : TEXCOORD2; // цвет обводки
                float4 p0        : TEXCOORD3; // fill, glow, shadow, specular
                float4 p1        : TEXCOORD4; // refraction, hover, glowRadius, shadowRadius
                float4 p2        : TEXCOORD5; // mode, borderWidth, flags, intensity
                float4 screenPos : TEXCOORD6;
                float4 mask      : TEXCOORD7;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float4 _ClipRect;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;

            // Глобальные параметры — выставляет LiquidGlassSystem
            float _LGGlowStrength;   // общий регулятор ореола
            float _LGBrightness;     // подъём яркости тёмных панелей (0 — исходный тёмный вид, 1 — максимум)

            // 3 байта в одном float (точно представимо до 2^24)
            float3 Unpack3(float v)
            {
                v = floor(v + 0.5);
                float a = floor(v / 65536.0);
                v -= a * 65536.0;
                float b = floor(v / 256.0);
                float c = v - b * 256.0;
                return float3(a, b, c) / 255.0;
            }

            v2f vert(appdata_lg v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float4 clip = UnityObjectToClipPos(v.vertex);
                o.pos = clip;
                o.color = v.color * _Color;
                o.local = v.uv0;
                o.radii = v.uv1;
                o.rim = v.uv2;

                float3 a = Unpack3(v.uv3.x);          // fill, glow, shadow
                float3 c = Unpack3(v.uv3.y);          // specular, refraction, hover
                float3 m = Unpack3(v.uv3.z) * 255.0;  // mode, borderWidth*32, flags
                float3 r = Unpack3(v.uv3.w) * 255.0;  // glowRadius, shadowRadius, intensity*255

                o.p0 = float4(a.x, a.y, a.z, c.x);
                o.p1 = float4(c.y, c.z, r.x, r.y);
                o.p2 = float4(m.x, m.y / 32.0, m.z, r.z / 255.0);

                o.screenPos = ComputeScreenPos(clip);

                float2 pixelSize = clip.w;
                pixelSize /= abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                o.mask = float4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw,
                                0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));
                return o;
            }

            // Скруглённый прямоугольник (Inigo Quilez). r = (TR, BR, TL, BL)
            float SdRoundBox(float2 p, float2 b, float4 r)
            {
                r.xy = (p.x > 0.0) ? r.xy : r.zw;
                r.x  = (p.y > 0.0) ? r.x  : r.y;
                float2 q = abs(p) - b + r.x;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r.x;
            }

            float BitSet(float flags, float bit)
            {
                return fmod(floor(flags / bit), 2.0);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 p  = IN.local.xy;
                float2 b  = max(IN.local.zw, 0.5);
                float minB = min(b.x, b.y);
                float4 rr = min(IN.radii, minB);                     // TL TR BR BL
                float4 r4 = float4(rr.y, rr.z, rr.x, rr.w);          // → TR BR TL BL

                float mode     = fmod(floor(IN.p2.x + 0.5), 4.0);    // режим — младшие 2 бита (старшие — устаревший блик)
                float fill     = IN.p0.x;
                float glow     = IN.p0.y;
                float shadow   = IN.p0.z;
                float hover    = IN.p1.y;
                float glowR    = max(IN.p1.z, 1.0);
                float shadowR  = max(IN.p1.w, 1.0);
                float bw       = IN.p2.y;
                float flags    = IN.p2.z;
                float intensity= IN.p2.w;
                float3 tint    = IN.color.rgb;
                float opacity  = IN.color.a;
                float4 rim     = IN.rim;

                bool fHairline   = BitSet(flags, 1.0) > 0.5;
                bool fBorderOnly = BitSet(flags, 2.0) > 0.5;
                bool fButton     = BitSet(flags, 8.0) > 0.5;

                float unitPx = max(length(float2(ddx(p.x), ddy(p.x))), 1e-4);
                float pxPerUnit = 1.0 / unitPx;

                float d = SdRoundBox(p, b, r4);
                float cov = saturate(0.5 - d * pxPerUnit);
                float insidePx = max(-d, 0.0) * pxPerUnit;
                float tv = (p.y / b.y) * 0.5 + 0.5;                  // 0 — низ, 1 — верх
                float topPx = (b.y - p.y) * pxPerUnit;               // расстояние от верхнего края, px

                float3 bodyCol = tint;
                float  bodyA   = 1.0;

                if (mode < 0.5)
                {
                    // ================= ОКНО =================
                    // Непрозрачное: вершинная альфа окон (обычно 0.85–0.95) больше не даёт «стекла»
                    opacity = saturate(opacity * 1.2);
                    bodyCol = tint * (0.90 + 0.14 * tv);
                    bodyA = 1.0;
                }
                else if (mode < 1.5)
                {
                    // ================= ПЛАСТИНА / КНОПКА =================
                    if (fButton)
                    {
                        bodyCol = tint * (0.86 + 0.24 * tv);
                        bodyCol += rim.rgb * pow(saturate(tv), 10.0) * 0.035;
                    }
                    else
                        bodyCol = tint + float3(0.0015, 0.003, 0.008);   // чуть светлее окна, но в тёмно-синий, не в серый
                    bodyA = fill;
                }
                else if (mode < 2.5)
                {
                    // ================= ЛИНИИ, ТОЧКИ, ПОЛОСЫ =================
                    bodyCol = tint * (1.0 + hover * 0.25) * (0.9 + 0.2 * intensity);
                    bodyA = 1.0;
                    if (fHairline)
                    {
                        float along = (b.x >= b.y) ? abs(p.x) / b.x : abs(p.y) / b.y;
                        float fade = 1.0 - smoothstep(0.35, 1.0, along);
                        bodyA *= fade;
                        rim.a *= fade;
                    }
                }
                else
                {
                    // ================= СКРИМ =================
                    bodyCol = tint * 0.55 + float3(0.004, 0.007, 0.010);
                    bodyA = 0.80;
                }

                if (fBorderOnly) bodyA = 0.0;

                if (mode < 1.5)
                {
                    // Яркость интерфейса (настройка): светлеют только тёмные окна и пластины — в глубокий тёмно-синий.
                    // Цвета здесь линейные: прибавка 0.02 к каждому каналу — это уже заметный серо-бирюзовый налёт,
                    // поэтому подмешиваем только синеву (≈ #0E1C33 поверх чёрного при полной яркости)
                    float bodyLum = dot(bodyCol, float3(0.2126, 0.7152, 0.0722));
                    float darkW = 1.0 - smoothstep(0.10, 0.35, bodyLum);
                    // Единый тёмно-синий тон: тёмные бирюзовые и серо-голубые подложки (их цвета разбросаны по окнам)
                    // уводим в глубокий синий той же силы. Тёплые, красные и зелёные (статусные) тела не трогаем.
                    float cmx  = max(max(bodyCol.g, bodyCol.b), 1e-5);
                    float cool = saturate((min(bodyCol.g, bodyCol.b) - bodyCol.r) / cmx * 2.5)
                               * saturate((bodyCol.b - 0.7 * bodyCol.g) / max(0.3 * bodyCol.g, 1e-5));
                    bodyCol = lerp(bodyCol, float3(0.20, 0.42, 1.0) * cmx, cool * darkW * 0.85);
                    bodyCol = lerp(bodyCol, bodyCol * 1.25 + float3(0.0016, 0.0034, 0.0105), saturate(_LGBrightness) * darkW);
                    bodyCol += hover * float3(0.006, 0.012, 0.030);
                }

                // --- Тень ---
                float2 so = float2(0.0, -shadowR * 0.32);
                float sdS = SdRoundBox(p - so, b, r4);
                float shA = shadow * 0.6 * (1.0 - smoothstep(-shadowR * 0.35, shadowR, sdS));

                // --- Ореол (только у линий и выделенных элементов, сдержанный) ---
                float outside = max(d, 0.0);
                float glowA = glow * _LGGlowStrength * (1.0 + hover) * exp(-outside / glowR) * (1.0 - cov) * rim.a;

                // --- Кромка ---
                float strokeD = abs(d + bw * 0.5) - bw * 0.5;
                float stroke = (bw > 0.001) ? saturate(0.5 - strokeD * pxPerUnit) : 0.0;
                float3 strokeCol = rim.rgb * (0.9 + 0.4 * hover);
                float strokeA = saturate(stroke * rim.a * (0.85 + 0.4 * hover));
                if (fButton) strokeA *= 0.62 + 0.38 * hover;
                if (mode < 0.5) strokeA *= 0.6;                      // у окон кромка тише — акцент даёт верхняя линия

                // --- Композиция (premultiplied alpha) ---
                float4 acc = float4(0, 0, 0, shA);
                float4 g4 = float4(rim.rgb * glowA, glowA * 0.6);
                acc = g4 + acc * (1.0 - g4.a);
                float bA = bodyA * cov;
                acc = float4(bodyCol * bA, bA) + acc * (1.0 - bA);
                acc = float4(strokeCol * strokeA, strokeA) + acc * (1.0 - strokeA);

                // Окна: неоновая линия по верхнему краю (2 px) и лёгкий отсвет под ней
                if (mode < 0.5 && b.x * pxPerUnit > 40.0)
                {
                    float3 lc = lerp(rim.rgb, float3(1, 1, 1), 0.15);
                    float la = (1.0 - smoothstep(1.6, 2.6, topPx)) * cov * saturate(rim.a * 2.4);
                    acc = float4(lc * la, la) + acc * (1.0 - la);
                    acc.rgb += rim.rgb * exp(-topPx / 18.0) * 0.045 * cov * saturate(rim.a * 2.0);
                }

                // Дизеринг — без ступенек на плавных градиентах тёмных панелей
                float dz = frac(sin(dot(IN.pos.xy, float2(12.9898, 78.233))) * 43758.5453);
                acc.rgb += (dz - 0.5) / 255.0 * acc.a;

                acc *= opacity;

                #ifdef UNITY_UI_CLIP_RECT
                half2 mClip = saturate((_ClipRect.zw - _ClipRect.xy - abs(IN.mask.xy)) * IN.mask.zw);
                acc *= mClip.x * mClip.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(acc.a - 0.001);
                #endif

                return acc;
            }
            ENDCG
        }
    }
}
