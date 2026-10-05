// Astropolity — Liquid Glass UI shader.
//
// Один проход рисует весь "стеклянный" элемент целиком:
//   • мягкая тень под панелью
//   • внешнее неоновое свечение
//   • тело (стекло / пластина / эмиссия / скрим)
//   • преломление по краям + хроматическая аберрация (режим GLASS)
//   • зеркальные блики, зависящие от направления света (_LGLightDir)
//   • тонкая обводка
//
// Все параметры приходят через UV-каналы вершин (см. LiquidGlassEffect.cs),
// поэтому все стеклянные элементы делят один материал и батчатся.
//   uv0 = (local.x, local.y, halfW, halfH)
//   uv1 = радиусы углов (TL, TR, BR, BL)
//   uv2 = цвет обводки (linear)
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
            Name "LiquidGlass"

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
            sampler2D _LGBackdropTex;
            float _LGBackdropReady;
            float _LGBackdropFlip;
            float4 _LGLightDir;   // xy — направление НА источник света (y вверх)
            float4 _LGPointer;    // xy — курсор в screen UV
            float _LGVibrancy;
            float _LGGlowStrength;   // общий регулятор неонового свечения

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

            // Скруглённый прямоугольник с разными радиусами (Inigo Quilez).
            // r = (TR, BR, TL, BL)
            float SdRoundBox(float2 p, float2 b, float4 r)
            {
                r.xy = (p.x > 0.0) ? r.xy : r.zw;
                r.x  = (p.y > 0.0) ? r.x  : r.y;
                float2 q = abs(p) - b + r.x;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r.x;
            }

            float3 SampleBackdrop(float2 uv)
            {
                uv = saturate(uv);
                if (_LGBackdropFlip > 0.5) uv.y = 1.0 - uv.y;
                return tex2Dlod(_LGBackdropTex, float4(uv, 0, 0)).rgb;
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

                float mode     = IN.p2.x;
                float fill     = IN.p0.x;
                float glow     = IN.p0.y;
                float shadow   = IN.p0.z;
                float specular = IN.p0.w;
                float refr     = IN.p1.x;
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
                bool fNoSpecular = BitSet(flags, 4.0) > 0.5;

                // --- Метрики пикселя ---
                float unitPx = max(length(float2(ddx(p.x), ddy(p.x))), 1e-4);  // юнитов на пиксель
                float pxPerUnit = 1.0 / unitPx;
                float2 suv = IN.screenPos.xy / max(IN.screenPos.w, 1e-5);
                float2 uvPerPx = float2(abs(ddx(suv.x)) + abs(ddy(suv.x)),
                                        abs(ddx(suv.y)) + abs(ddy(suv.y)));
                uvPerPx = max(uvPerPx, 1e-6);

                float d = SdRoundBox(p, b, r4);
                float cov = saturate(0.5 - d * pxPerUnit);

                // --- Нормаль края (по сглаженному SDF) ---
                float e = unitPx;
                float4 rs = max(r4, minB * 0.45);
                float2 n = float2(SdRoundBox(p + float2(e, 0), b, rs) - SdRoundBox(p - float2(e, 0), b, rs),
                                  SdRoundBox(p + float2(0, e), b, rs) - SdRoundBox(p - float2(0, e), b, rs));
                n /= max(length(n), 1e-5);

                float band   = clamp(minB * 0.6, 3.0, 26.0);
                float depth  = saturate(-d / band);
                float edge   = 1.0 - depth;
                float2 L     = normalize(_LGLightDir.xy + 1e-5);
                float facing = dot(n, L);
                float yN     = p.y / b.y;
                float insidePx = max(-d, 0.0) * pxPerUnit;

                float specMul = fNoSpecular ? 0.0 : specular;

                // Свечение курсора (на hover)
                float2 dPtr = (suv - _LGPointer.xy) / uvPerPx;
                float ptrGlow = exp(-dot(dPtr, dPtr) / (2.0 * 120.0 * 120.0)) * hover;

                float3 bodyCol = tint;
                float  bodyA   = 1.0;

                if (mode < 0.5)
                {
                    // ================= GLASS =================
                    float bend = edge * edge * edge;
                    float2 dispUV = -n * bend * band * pxPerUnit * 0.55 * refr * uvPerPx;
                    float2 caUV   = n * bend * refr * 2.2 * uvPerPx;

                    float3 bg;
                    bg.r = SampleBackdrop(suv + dispUV + caUV).r;
                    bg.g = SampleBackdrop(suv + dispUV).g;
                    bg.b = SampleBackdrop(suv + dispUV - caUV).b;

                    float lum = dot(bg, float3(0.2126, 0.7152, 0.0722));
                    bg = lerp(lum.xxx, bg, 1.0 + 0.45 * _LGVibrancy) * 1.06 + 0.006;

                    if (_LGBackdropReady < 0.5)
                        bg = tint * 0.55 + 0.012;

                    bodyCol = lerp(bg, tint, fill);
                    bodyCol *= 1.0 + 0.08 * yN * specMul;                                       // свет сверху
                    bodyCol += lerp(float3(1,1,1), rim.rgb, 0.5) * pow(edge, 6.0) * 0.14 * specMul; // френель
                    bodyA = (_LGBackdropReady < 0.5) ? max(0.9, fill) : 1.0;
                }
                else if (mode < 1.5)
                {
                    // ================= PLATTER (вложенная пластина) =================
                    bodyCol = tint + 0.025 + 0.045 * saturate(yN * 0.5 + 0.5) * specMul;
                    bodyCol += lerp(float3(1,1,1), rim.rgb, 0.5) * pow(edge, 5.0) * 0.08 * specMul;
                    bodyA = fill;
                }
                else if (mode < 2.5)
                {
                    // ================= EMISSIVE (линии, точки, полосы) =================
                    bodyCol = tint * (1.0 + hover * 0.35) * (0.85 + 0.3 * intensity);
                    bodyCol += 0.22 * specMul * smoothstep(0.1, 1.0, yN);
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
                    // ================= SCRIM (размытый фон под модалками) =================
                    float3 bg = SampleBackdrop(suv);
                    float lum = dot(bg, float3(0.2126, 0.7152, 0.0722));
                    bg = lerp(lum.xxx, bg, 0.8);
                    bodyCol = lerp(bg * 0.65, tint, fill);
                    bodyA = (_LGBackdropReady < 0.5) ? 0.82 : 1.0;
                }

                if (fBorderOnly) bodyA = 0.0;

                if (mode < 1.5)
                {
                    // Мягкая подсветка края, обращённого к свету
                    float soft = saturate(1.0 - insidePx / 16.0) * pow(saturate(facing), 2.0) * 0.16 * specMul;
                    bodyCol += soft;
                    // Hover: внутренний неон + блик под курсором
                    bodyCol += rim.rgb * exp(-insidePx / max(glowR * 1.2, 1.0)) * hover * glow * 0.3;
                    bodyCol += lerp(float3(1,1,1), rim.rgb, 0.3) * ptrGlow * 0.10;
                    bodyCol += hover * 0.035;
                }

                // --- Тень ---
                float2 so = float2(0.0, -shadowR * 0.32);
                float sdS = SdRoundBox(p - so, b, r4);
                float shA = shadow * 0.55 * (1.0 - smoothstep(-shadowR * 0.35, shadowR, sdS));

                // --- Внешнее свечение ---
                float outside = max(d, 0.0);
                float glowA = glow * _LGGlowStrength * (1.0 + hover * 0.9) * exp(-outside / glowR) * (1.0 - cov) * rim.a;
                float3 glowC = rim.rgb;

                // --- Обводка ---
                float strokeD = abs(d + bw * 0.5) - bw * 0.5;
                float stroke = (bw > 0.001) ? saturate(0.5 - strokeD * pxPerUnit) : 0.0;
                float f01 = saturate(facing * 0.5 + 0.5);
                float3 strokeCol = lerp(rim.rgb, float3(1,1,1), pow(saturate(facing), 3.0) * 0.55 * specMul)
                                   * (0.8 + 0.35 * hover);
                float strokeA = saturate(stroke * rim.a * (0.65 + 0.45 * f01 + 0.35 * hover));

                // --- Блик по кромке (двигается со светом) ---
                float glintMask = saturate(1.0 - insidePx / 2.6) * cov;
                float sp = pow(saturate(facing), 5.0) + 0.45 * pow(saturate(-facing), 7.0);
                float glintA = (mode < 1.5) ? glintMask * sp * specMul * (0.55 + 0.5 * hover) : 0.0;

                // --- Композиция (premultiplied alpha) ---
                float4 acc = float4(0, 0, 0, shA);
                float4 g4 = float4(glowC * glowA, glowA * 0.6);
                acc = g4 + acc * (1.0 - g4.a);
                float bA = bodyA * cov;
                acc = float4(bodyCol * bA, bA) + acc * (1.0 - bA);
                acc = float4(strokeCol * strokeA, strokeA) + acc * (1.0 - strokeA);
                acc.rgb += glintA;
                acc.a = saturate(acc.a + glintA * 0.4);

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
