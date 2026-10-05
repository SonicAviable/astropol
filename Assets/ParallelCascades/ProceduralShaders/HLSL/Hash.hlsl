#ifndef HASH_INCLUDED
#define HASH_INCLUDED

#include "Packages/com.unity.render-pipelines.core//ShaderLibrary/Macros.hlsl"

float mod(float x, float y)
{
    return x - y * floor(x/y);
}

float Hash12(float2 p)
{
    float3 p3  = frac(float3(p.xyx) * 383.14);
    p3 += dot(p3, p3.yzx + 19.19);
    // Use sin to modulate the hash
    return frac(sin((p3.x + p3.y) * p3.z) * 43758.5453);
}

float Hash21 (float2 st)
{
    const float a = 12.9898;
    const float b = 78.233;
    const float c = 43758.5453;
    float dt= dot(st.xy ,float2(a,b));
    float sn= mod(dt,PI);
    return frac(sin(sn) * c);
}

float3 Hash33( float3 x )
{
    x = float3( dot(x,float3(127.1,311.7, 74.7)),
              dot(x,float3(269.5,183.3,246.1)),
              dot(x,float3(113.5,271.9,124.6)));

    return frac(sin(x) * 43758.5453123);
}

//https://byteblacksmith.com/improvements-to-the-canonical-one-liner-glsl-rand-for-opengl-es-2-0/
// Returns a value in [0,1]
float3 Hash33_highp( float3 x )
{
    // High precision constants
    const float3 k1 = float3(127.1, 311.7, 74.7);
    const float3 k2 = float3(269.5, 183.3, 246.1);
    const float3 k3 = float3(113.5, 271.9, 124.6);

    x = float3(dot(x, k1), dot(x, k2), dot(x, k3));
    x = float3(mod(x.x, TWO_PI), mod(x.y, TWO_PI), mod(x.z, TWO_PI));

    return frac(sin(x) * 43758.5453123);
}

float2 Hash22_highp( float2 x )
{
    // High precision constants
    const float3 k1 = float3(127.1, 311.7, 74.7);
    const float3 k2 = float3(269.5, 183.3, 246.1);

    x = float2(dot(x, k1), dot(x, k2));
    x = float2(mod(x.x, TWO_PI), mod(x.y, TWO_PI));

    return frac(sin(x) * 43758.5453123);
}


#endif