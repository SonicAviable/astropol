// Astropolity — ядро галактики (плоский квад в центре). Свет + поглощение: Blend One OneMinusSrcAlpha,
// альфа — пыль, она затеняет то, что под ядром (диск, звёздную пыль), цвет — свечение.
//   • ослепительное компактное ядро, вытянутый балдж и перемычка (бар) кремово-жёлтых старых звёзд
//   • внутренние рукава продолжают рукава звёзд карты (та же спираль, что у генератора: _Arms, _Twist)
//   • тёмные пылевые полосы по внутренней кромке рукавов и вдоль перемычки, тонкие волокна над балджем
//   • розовые области звездообразования и голубые молодые скопления в рукавах, мерцающие искры
//   • клочья медленно текут вдоль рукавов, ядро едва заметно дышит
// Координаты: rr — расстояние от центра в радиусах галактики (_Scale — радиус квада в них).
// _T, _Intensity, _Opacity, форму (_Arms, _Twist, _Bar, _Elliptical) выставляет GalaxyView.
Shader "Astropolity/GalaxyCore"
{
    Properties
    {
        _T ("Time", Float) = 0
        _Intensity ("Intensity", Float) = 1
        _Opacity ("Dust opacity", Float) = 1
        _Hot ("Nucleus", Color) = (1, 0.97, 0.9, 1)
        _Warm ("Bulge", Color) = (1, 0.86, 0.64, 1)
        _Outer ("Young disk", Color) = (0.72, 0.8, 1, 1)
        _HII ("Star forming", Color) = (1, 0.38, 0.55, 1)
        _Arms ("Arm count", Float) = 2
        _Twist ("Arm twist", Float) = 3.3
        _Bar ("Bar strength", Range(0, 1)) = 1
        _Elliptical ("Elliptical", Range(0, 1)) = 0
        _Scale ("Quad radius in galaxy radii", Float) = 0.6
    }
    SubShader
    {
        Tags { "Queue"="Transparent-30" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            float _T, _Intensity, _Opacity, _Arms, _Twist, _Bar, _Elliptical, _Scale;
            float4 _Hot, _Warm, _Outer, _HII;

            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }
            float Fbm(float2 p, int oct)
            {
                float v = 0.0, a = 0.5;
                for (int k = 0; k < 5; k++)
                {
                    if (k >= oct) break;
                    v += a * Noise(p); p = p * 2.03 + 7.7; a *= 0.5;
                }
                return v;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * 2.0;           // -1..1, 1 = край квада
                float edgeR = length(p);
                if (edgeR > 1.0) return 0;
                float2 q = p * _Scale;
                float rr = edgeR * _Scale;
                float ang = atan2(q.y, q.x);
                float fade = 1.0 - smoothstep(0.8, 1.0, edgeR);

                // Клочья текут вдоль рукавов: шумы — во вращающихся координатах (оборот ≈ 13 мин), рукава стоят
                float sp = _T * 0.008;
                float2 qn = float2(q.x * cos(sp) - q.y * sin(sp), q.x * sin(sp) + q.y * cos(sp));
                float n  = Fbm(qn * 14.0 + 3.0, 5);
                float n2 = Fbm(qn * 40.0 + 11.0, 4);
                float nb = Fbm(qn * 60.0 + 29.0, 3);
                float nf = Fbm(q * 90.0 + 5.0, 3);

                // ---- рукава: та же логарифмическая спираль, что у звёзд (угол = t·twist, t = (r − 0,14)/0,91)
                float t = (rr - 0.14) / 0.91;
                float ph = _Arms * (ang - t * _Twist + (n - 0.5) * 0.5);
                float hasArms = step(0.5, _Arms);
                float arm = pow(0.5 + 0.5 * cos(ph), 3.0) * hasArms;
                float armI = arm * exp(-rr / 0.22) * smoothstep(0.05, 0.16, rr) * (0.3 + 0.9 * n);
                float lane = pow(0.5 + 0.5 * cos(ph + 0.8), 4.0) * hasArms;   // пыль — по внутренней кромке

                // ---- перемычка (вдоль оси X: из её концов выходят рукава) и балдж
                float bx = q.x, by = q.y / lerp(1.0, 0.65, _Elliptical);
                float bar = exp(-(bx * bx / (2.0 * 0.085 * 0.085) + by * by / (2.0 * 0.028 * 0.028))) * _Bar;
                // пылевые полосы перемычки — по передним кромкам, на противоположных сторонах
                float bd = by - 0.02 * tanh(bx / 0.015) - bx * 0.12;
                float barLane = exp(-bd * bd / (2.0 * 0.007 * 0.007)) * exp(-bx * bx / (2.0 * 0.1 * 0.1))
                              * smoothstep(0.008, 0.03, abs(bx)) * _Bar;
                float rb = sqrt(bx * bx + by * by * 1.5625);
                float bulgeSize = lerp(0.045, 0.12, _Elliptical);
                float pulse = 1.0 + 0.05 * sin(_T * 0.6);
                float bulge = exp(-pow(rb / bulgeSize, 0.7)) * 1.8;
                float nuc = (exp(-rr * rr / (2.0 * 0.004 * 0.004)) * 4.0 + exp(-rr / 0.01)) * pulse;
                float glow = exp(-rr / lerp(0.08, 0.15, _Elliptical)) * 0.3 + exp(-rr / 0.22) * 0.07;

                // ---- звездообразование и молодые скопления
                float knots = smoothstep(0.62, 0.8, n2) * arm * smoothstep(0.07, 0.15, rr) * exp(-rr / 0.3);
                float young = smoothstep(0.55, 0.75, nb) * arm * smoothstep(0.06, 0.14, rr) * exp(-rr / 0.28);
                float2 cq = qn * 260.0;
                float2 fr = frac(cq) - 0.5;
                float h = Hash(floor(cq));
                float tw = 0.5 + 0.5 * sin(_T * (1.2 + h * 2.5) + h * 40.0);
                float spark = step(0.93, h) * exp(-dot(fr, fr) * 60.0) * tw * (arm * 0.8 + 0.2) * exp(-rr / 0.25)
                            * smoothstep(0.04, 0.1, rr);

                // ---- поглощение пылью
                float A = lane * (0.35 + 0.65 * smoothstep(0.3, 0.7, n)) * smoothstep(0.025, 0.08, rr)
                        * (1.0 - smoothstep(0.3, 0.5, rr));
                A += smoothstep(0.55, 0.8, nf) * smoothstep(0.015, 0.05, rr) * exp(-rr / 0.12) * 0.35;
                A = max(A, barLane * 0.75);
                A = saturate(A) * 0.92 * fade;

                float3 disk = lerp(_Warm.rgb, _Outer.rgb, smoothstep(0.06, 0.3, rr));
                float3 E = _Hot.rgb * nuc
                         + _Warm.rgb * (bulge + bar * 0.9)
                         + disk * armI * 0.6
                         + _HII.rgb * knots * 0.9
                         + _Outer.rgb * young * 0.5;
                E = E * (1.0 - A) + _Warm.rgb * glow + _Outer.rgb * spark * 0.8;
                E *= fade;
                float3 col = (1.0 - exp(-E * 1.1)) * _Intensity;   // мягкий тонмаппинг — ядро не выгорает в плоское пятно
                return float4(col, A * _Opacity);
            }
            ENDCG
        }
    }
}
