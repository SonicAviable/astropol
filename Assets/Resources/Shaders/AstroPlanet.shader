// Планета Astropol: фото-карта (равнопромежуточная развёртка по направлению от центра, без UV меша)
// плюс процедурная вариативность. _Kind: 0 материковая (Земля), 1 газовый гигант, 2 вулканическая,
// 3 ледяная, 5 пустынная/безжизненная (Марс, Луна…), 6 процедурный мир (материки из шума, детали из карты).
// Цвета (_A…_CityCol) — векторы в sRGB, как в превью; карта читается и переводится в sRGB для порогов.
// Освещение — по направлению на звезду (_SunDir, мир; ставит PlanetSunLink).
Shader "Astropolity/Planet/Astro"
{
    Properties
    {
        _MainTex ("Surface / detail map", 2D) = "gray" {}
        _AuxTex ("Night lights", 2D) = "black" {}
        _CloudTex ("Clouds", 2D) = "black" {}
        _Kind ("Kind", Float) = 0
        _A ("A", Vector) = (0,0,0,0)
        _B ("B", Vector) = (0,0,0,0)
        _C ("C", Vector) = (0,0,0,0)
        _D ("D", Vector) = (0,0,0,0)
        _Atm ("Atmosphere", Vector) = (0.4,0.65,1,0)
        _Tint ("Tint", Vector) = (1,1,1,0)
        _CloudCol ("Cloud color", Vector) = (0.92,0.92,0.92,0)
        _CityCol ("City color", Vector) = (1,0.62,0.3,0)
        _Seed ("Seed", Float) = 0
        _Spin ("Spin (rad/s)", Float) = 0.02
        _Cloud ("Clouds", Float) = 0
        _City ("Cities", Float) = 0
        _Hue ("Hue shift", Float) = 0
        _Sat ("Saturation", Float) = 1
        _Off ("Longitude offset", Float) = 0
        _Flip ("Mirror", Float) = 1
        _Bump ("Relief", Float) = 0
        _Cap ("Ice / caps", Float) = 2
        _Sea ("Sea level", Float) = 0
        _Temp ("Temperature bias", Float) = 0
        _AtmK ("Atmosphere rim", Float) = 1
        _Exposure ("Exposure", Float) = 1.35
        _SunDir ("Sun direction (world)", Vector) = (0,0,1,0)
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

            sampler2D _MainTex, _AuxTex, _CloudTex;
            float4 _MainTex_TexelSize;
            float _Kind, _Seed, _Spin, _Cloud, _City, _Hue, _Sat, _Off, _Flip, _Bump, _Cap, _Sea, _Temp, _AtmK, _Exposure;
            float4 _A, _B, _C, _D, _Atm, _Tint, _CloudCol, _CityCol, _SunDir;

            struct v2f { float4 pos : SV_POSITION; float3 obj : TEXCOORD0; float3 wpos : TEXCOORD1; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.obj = v.vertex.xyz;
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float3 Lin(float3 c) { return pow(max(c, 0), 2.2); }
            float Lum(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }
            float3 HueShift(float3 c, float a) { const float3 k = 0.57735; float ca = cos(a); return c * ca + cross(k, c) * sin(a) + k * dot(k, c) * (1 - ca); }
            float3 Grade(float3 c) { c = HueShift(c, _Hue); return max(lerp(Lum(c).xxx, c, _Sat) * _Tint.rgb, 0); }
            float2 Sph(float3 p) { return float2(atan2(p.x, p.z) / 6.2831853 + 0.5, asin(clamp(p.y, -1, 1)) / 3.1415927 + 0.5); }

            // Производные без скачка на шве 180°: берём ту из двух развёрток долготы, где они меньше
            struct Grad { float2 dx; float2 dy; };
            Grad SeamGrad(float2 uv)
            {
                float ub = frac(uv.x + 0.5) - 0.5;
                float2 a = float2(ddx(uv.x), ddy(uv.x)), b = float2(ddx(ub), ddy(ub));
                float2 du = (abs(a.x) + abs(a.y) <= abs(b.x) + abs(b.y)) ? a : b;
                Grad g; g.dx = float2(du.x, ddx(uv.y)); g.dy = float2(du.y, ddy(uv.y)); return g;
            }
            // Чтение карты в sRGB (пороги и цвета подобраны в sRGB)
            float3 Tx(sampler2D s, float2 uv, Grad g)
            {
                float3 c = tex2Dgrad(s, uv, g.dx, g.dy).rgb;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                c = LinearToGammaSpace(c);
                #endif
                return c;
            }
            float3 BumpN(float2 uv, Grad g, float3 p, float k)
            {
                // Шаг — полтора текселя карты (2k…4k), сила рельефа приведена к шагу 1/1024
                float e = max(_MainTex_TexelSize.x * 1.5, 1e-4);
                k *= (1.0 / 1024.0) / e;
                float h0 = Lum(Tx(_MainTex, uv, g)), hu = Lum(Tx(_MainTex, uv + float2(e, 0), g)), hv = Lum(Tx(_MainTex, uv + float2(0, e), g));
                float3 east = normalize(float3(p.z, 0, -p.x) + float3(1e-5, 0, 0));
                float3 north = cross(p, east);
                return normalize(p - k * ((hu - h0) * _Flip * east + (hv - h0) * north));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;
                float3 pn = normalize(i.obj);
                float ang = t * _Spin;
                float3 p = RotY(pn, ang);
                float3 N = normalize(mul((float3x3)unity_ObjectToWorld, pn));
                float3 V = normalize(_WorldSpaceCameraPos - i.wpos);
                float3 L = normalize(_SunDir.xyz);
                float diff = dot(N, L);
                float lit = smoothstep(-0.05, 0.18, diff);
                float3 H = normalize(L + V);
                float nv = saturate(dot(N, V));

                float2 g0 = Sph(p);
                float2 uvT = float2(g0.x * _Flip + _Off, g0.y);
                Grad gr = SeamGrad(uvT);
                float3 surf = 0, emis = 0, nb = p;
                float atmK = 1, scat = 0;
                float night = 1 - smoothstep(-0.18, 0.04, diff);
                // Облака: своя скорость вращения; производные — вне ветвлений
                float3 pc = RotY(pn, ang * 1.15 + _Seed);
                float2 cuv = Sph(pc);
                cuv.x += 0.004 * snoise(pc * 4 + float3(0, 0, t * 0.03));
                float2 cuv6 = float2(cuv.x * _Flip + _Off * 0.37, cuv.y);
                Grad cgr = SeamGrad(cuv), cgr6 = SeamGrad(cuv6);

                if (_Kind < 0.5)
                {
                    float3 s = Tx(_MainTex, uvT, gr);
                    float l = Lum(s);
                    float water = smoothstep(0.02, 0.08, s.b - max(s.r, s.g) * 0.9) * smoothstep(0.5, 0.3, l);
                    float3 c = lerp(Grade(s), _A.rgb * (0.45 + 3.2 * l), water);
                    nb = BumpN(uvT, gr, p, _Bump * (1 - water));
                    float cl = smoothstep(0.08, 0.95, Tx(_CloudTex, cuv, cgr).r) * _Cloud;
                    surf = Lin(c) * (1 - cl * 0.35);
                    emis += float3(1, 0.88, 0.7) * pow(saturate(dot(N, H)), 45) * 0.5 * water * (1 - cl) * lit;
                    emis += Lin(Tx(_AuxTex, uvT, gr)) * float3(1, 0.75, 0.5) * night * (1 - cl) * _City * 1.3;
                    surf = lerp(surf, Lin(_CloudCol.rgb) * 0.85, cl);
                    scat = 1;
                }
                else if (_Kind < 1.5)
                {
                    float2 g = uvT;
                    g.x += t * 0.0025 * sin(g.y * 29 + _Seed) + 0.004 * snoise(float3(p.x * 3, p.y * 16, p.z * 3) + t * 0.04);
                    g.y += 0.0015 * snoise(float3(p.x * 5, p.y * 24, p.z * 5) - t * 0.03);
                    surf = Lin(Grade(Tx(_MainTex, g, gr))) * (0.6 + 0.4 * pow(nv, 0.5));
                    atmK = 0.45;
                }
                else if (_Kind < 2.5)
                {
                    float l = Lum(Tx(_MainTex, uvT, gr));
                    float3 base = lerp(_A.rgb, _B.rgb, smoothstep(0.15, 0.75, l));
                    float rg = ridge(p * 3 + _Seed, 4);
                    float cr = smoothstep(0.84, 0.97, rg) * smoothstep(0.55, 0.3, l);
                    float flow = smoothstep(0.34, 0.2, l + 0.12 * snoise(p * 6 + _Seed));
                    float pulse = 0.8 + 0.2 * sin(t * 1.3 + rg * 10);
                    surf = Lin(base) * (1 - max(cr, flow) * 0.85);
                    emis += Lin(lerp(_C.rgb, float3(1, 0.75, 0.35), cr)) * (cr + flow * 0.6) * pulse * 1.6;
                    nb = BumpN(uvT, gr, p, _Bump);
                    atmK = 0.35;
                }
                else if (_Kind < 3.5)
                {
                    float l = Lum(Tx(_MainTex, uvT, gr));
                    float3 c = lerp(_C.rgb, _A.rgb, smoothstep(0.08, 0.8, l));
                    c = lerp(c, _B.rgb, smoothstep(0.62, 0.9, ridge(p * 2.4 + _Seed, 4)) * 0.35);
                    surf = Lin(c);
                    nb = BumpN(uvT, gr, p, _Bump);
                    emis += pow(saturate(dot(N, H)), 25) * 0.1 * lit;
                    atmK = 0.2;
                }
                else if (_Kind > 5.5)
                {
                    float3 q = p * 1.6 + _Seed;
                    float3 w = fbm(q * 0.8 + 7.3, 3) * 0.5;
                    float h = fbm(q + w, 6);
                    float e = h - _Sea;
                    float land = smoothstep(0, 0.012, e);
                    float d = Lum(Tx(_MainTex, uvT, gr));
                    float dd = d - 0.45;
                    float lat = abs(p.y);
                    float moist = fbm(q * 1.1 + 19, 4) * 0.6 + 0.5 + dd * 0.5;
                    float temp = 1 - lat * 1.15 - max(e, 0) * 1.2 + _Temp + 0.1 * fbm(q * 3, 2);
                    float3 gc = lerp(_B.rgb, _C.rgb, smoothstep(0.38, 0.62, moist));
                    gc = lerp(gc, _D.rgb, smoothstep(0.16, 0.34, e + dd * 0.25));
                    gc = lerp(gc, Lum(gc) * float3(1.05, 1, 0.92) * 0.9, smoothstep(0.4, 0.2, temp));
                    gc *= 0.7 + 0.7 * d;
                    float ice = saturate(smoothstep(0.12, 0.02, temp) + smoothstep(0.44, 0.52, e)) * _Cap;
                    gc = lerp(gc, float3(0.88, 0.9, 0.93), ice);
                    float3 oc = lerp(_A.rgb * 0.55, _A.rgb, smoothstep(-0.35, 0, e));
                    oc = lerp(oc, _A.rgb * 1.7 + float3(0.02, 0.06, 0.06), smoothstep(-0.05, 0, e) * 0.5);
                    oc = lerp(oc, float3(0.8, 0.86, 0.92), smoothstep(0.06, -0.04, temp) * _Cap);
                    nb = BumpN(uvT, gr, p, _Bump * land);
                    float cl = smoothstep(0.08, 0.95, Tx(_CloudTex, cuv6, cgr6).r) * _Cloud;
                    surf = Lin(lerp(oc, gc, land)) * (1 - cl * 0.35);
                    emis += float3(1, 0.88, 0.7) * pow(saturate(dot(N, H)), 45) * 0.5 * (1 - land) * (1 - cl) * lit;
                    float ct = smoothstep(0.78, 0.96, snoise(q * 42) * 0.5 + 0.5) * land * (1 - ice) * smoothstep(0.22, 0, e);
                    emis += Lin(_CityCol.rgb) * ct * night * (1 - cl) * _City * 0.7;
                    surf = lerp(surf, Lin(_CloudCol.rgb) * 0.85, cl);
                    scat = 1;
                }
                else
                {
                    float3 c = Grade(Tx(_MainTex, uvT, gr));
                    float cap = smoothstep(_Cap, _Cap + 0.035, abs(p.y) + 0.03 * snoise(p * 9 + _Seed));
                    c = lerp(c, float3(0.9, 0.88, 0.86), cap);
                    surf = Lin(c);
                    nb = BumpN(uvT, gr, p, _Bump);
                    atmK = 0.5;
                }
                atmK *= _AtmK;

                float3 nW = normalize(mul((float3x3)unity_ObjectToWorld, RotY(nb, -ang)));
                float lt = saturate(dot(nW, L) * 0.95 + 0.05) * lit;
                float3 col = surf * lt * _Exposure + surf * 0.004 + emis;
                float3 atm = Lin(_Atm.rgb);
                float fres = pow(1 - nv, 4);
                col += atm * fres * smoothstep(-0.2, 0.5, diff) * 0.75 * atmK;
                col += float3(1, 0.4, 0.15) * exp(-abs(diff) * 28) * pow(1 - nv, 2) * 0.15 * scat;
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
