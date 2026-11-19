// Общий шум для планет и звёзд Astropol: 3D simplex (Ashima / Gustavson, MIT), fbm, ridge, повороты.
#ifndef ASTRO_NOISE_INCLUDED
#define ASTRO_NOISE_INCLUDED

float3 an_mod289(float3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
float4 an_mod289(float4 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
float4 an_permute(float4 x) { return an_mod289(((x * 34.0) + 1.0) * x); }
float4 an_tis(float4 r) { return 1.79284291400159 - 0.85373472095314 * r; }

float snoise(float3 v)
{
    const float2 C = float2(1.0 / 6.0, 1.0 / 3.0);
    const float4 D = float4(0.0, 0.5, 1.0, 2.0);
    float3 i = floor(v + dot(v, C.yyy));
    float3 x0 = v - i + dot(i, C.xxx);
    float3 g = step(x0.yzx, x0.xyz);
    float3 l = 1.0 - g;
    float3 i1 = min(g.xyz, l.zxy);
    float3 i2 = max(g.xyz, l.zxy);
    float3 x1 = x0 - i1 + C.xxx;
    float3 x2 = x0 - i2 + C.yyy;
    float3 x3 = x0 - D.yyy;
    i = an_mod289(i);
    float4 p = an_permute(an_permute(an_permute(i.z + float4(0.0, i1.z, i2.z, 1.0)) + i.y + float4(0.0, i1.y, i2.y, 1.0)) + i.x + float4(0.0, i1.x, i2.x, 1.0));
    float3 ns = 0.142857142857 * D.wyz - D.xzx;
    float4 j = p - 49.0 * floor(p * ns.z * ns.z);
    float4 x_ = floor(j * ns.z);
    float4 y_ = floor(j - 7.0 * x_);
    float4 x = x_ * ns.x + ns.yyyy;
    float4 y = y_ * ns.x + ns.yyyy;
    float4 h = 1.0 - abs(x) - abs(y);
    float4 b0 = float4(x.xy, y.xy);
    float4 b1 = float4(x.zw, y.zw);
    float4 s0 = floor(b0) * 2.0 + 1.0;
    float4 s1 = floor(b1) * 2.0 + 1.0;
    float4 sh = -step(h, 0.0);
    float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
    float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
    float3 p0 = float3(a0.xy, h.x), p1 = float3(a0.zw, h.y), p2 = float3(a1.xy, h.z), p3 = float3(a1.zw, h.w);
    float4 nr = an_tis(float4(dot(p0, p0), dot(p1, p1), dot(p2, p2), dot(p3, p3)));
    p0 *= nr.x; p1 *= nr.y; p2 *= nr.z; p3 *= nr.w;
    float4 m = max(0.6 - float4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.0);
    m = m * m;
    return 42.0 * dot(m * m, float4(dot(p0, x0), dot(p1, x1), dot(p2, x2), dot(p3, x3)));
}

float fbm(float3 p, int o)
{
    float a = 0.5, s = 0.0;
    [unroll] for (int k = 0; k < 7; k++) { if (k >= o) break; s += a * snoise(p); p = p * 2.03 + float3(1.7, 9.2, 3.1); a *= 0.5; }
    return s;
}

float ridge(float3 p, int o)
{
    float a = 0.5, s = 0.0;
    [unroll] for (int k = 0; k < 6; k++) { if (k >= o) break; float n = 1.0 - abs(snoise(p)); s += a * n * n; p = p * 2.05 + float3(4.1, 1.3, 7.7); a *= 0.5; }
    return s;
}

float3 RotY(float3 v, float a) { float c = cos(a), s = sin(a); return float3(c * v.x + s * v.z, v.y, -s * v.x + c * v.z); }

#endif
