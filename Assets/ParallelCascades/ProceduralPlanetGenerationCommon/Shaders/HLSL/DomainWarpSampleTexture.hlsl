float fbm2D(float2 p, int octaves, float persistence, float lacunarity, float scale, UnityTexture2D noiseTexture, UnitySamplerState noiseSampler)
{
    float value = 0.0;
    float amplitude = 1.0;
    float frequency = scale;

    for (int i = 0; i < octaves; i++)
    {
        value += amplitude * noiseTexture.Sample(noiseSampler, frac(p*frequency)).r;
        frequency *= lacunarity;
        amplitude *= persistence;
    }
    return value;
}

void domainWarp2DOffsetOutputs_float(
    float2 p,
    int numLayers,
    float persistence,
    float lacunarity,
    float scale,
    float warpAmount,
    UnityTexture2D noiseTexture,
    UnitySamplerState textureSampler,
    out float noise,
    out float2 q,
    out float2 r)
{
    // Primary warp
    q = float2(
        fbm2D(p + float2(0.0, 0.0), numLayers, persistence, lacunarity, scale, noiseTexture, textureSampler),
        fbm2D(p + float2(5.2, 1.3), numLayers, persistence, lacunarity, scale, noiseTexture, textureSampler)
    );

    // Secondary warp
    r = float2(
        fbm2D(p + warpAmount * q + float2(1.7, 9.2), numLayers, persistence, lacunarity, scale, noiseTexture, textureSampler),
        fbm2D(p + warpAmount * q + float2(8.3, 2.8), numLayers, persistence, lacunarity, scale, noiseTexture, textureSampler)
    );

    noise = fbm2D(p + warpAmount * r, numLayers, persistence, lacunarity, scale, noiseTexture, textureSampler);
}
