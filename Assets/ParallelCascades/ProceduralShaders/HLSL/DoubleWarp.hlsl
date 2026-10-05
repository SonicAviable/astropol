#include "FractalNoise.hlsl"

void doubleWarp_float(
float3 pos,
float3 offset,
int numLayers,
float persistence,
float lacunarity,
float scale,
out float noisePattern)
{
    
    float r;
    fbmSimplexNoise3D_float(pos, offset, 10, .75, 2, 1.8, r);

    float3 warpedPos = lerp(pos, r, 0.5);

    fbmSimplexNoise3D_float(warpedPos, offset, numLayers, persistence, lacunarity, scale, noisePattern);
}

void doubleWarp_filtered_float(
    float3 pos,
    float3 offset,
    int numLayers,
    float persistence,
    float lacunarity,
    float scale,
    float detailLevel,
    out float noisePattern)
{
    const int MaxSamples = 10;

    float3 uvw = pos;

    float3 ddx_uvw = uvw + ddx(uvw);
    float3 ddy_uvw = uvw + ddy(uvw);

    int sx = 1 + int(clamp(detailLevel * length(ddx_uvw - uvw), 0.0, float(MaxSamples - 1)));
    int sy = 1 + int(clamp(detailLevel * length(ddy_uvw - uvw), 0.0, float(MaxSamples - 1)));

    noisePattern = 0;
    for (int j = 0; j < sy; j++)
    {
        for (int i = 0; i < sx; i++)
        {
            float2 st = float2(float(i), float(j)) / float2(float(sx), float(sy));
            float3 samplePos = uvw + st.x * (ddx_uvw - uvw) + st.y * (ddy_uvw - uvw);
            float noiseSample;
            doubleWarp_float(samplePos, offset, numLayers,
                                    persistence, lacunarity, scale,
                                    noiseSample);
            noisePattern += noiseSample;
        }
    }

    noisePattern = noisePattern / float(sx * sy);
}