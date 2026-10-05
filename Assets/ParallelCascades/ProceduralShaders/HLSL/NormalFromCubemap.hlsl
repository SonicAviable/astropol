// This is based on the implementation of the Normal From Texture Node in Shader Graph:
// https://docs.unity3d.com/Packages/com.unity.shadergraph@17.4/manual/Normal-From-Texture-Node.html
// But it works with cubemaps instead. It uses tangent and bitangent vectors for every sample point instead of flat UV
// offsets. This way it works for spherical meshes like planets.

// Dir should be Object Position when sampling cubemap textures for planets.
// Offset value of 0.2 looks good.
// LOD can be calculated automatically with the AutoCubemapLOD subgraph.
void NormalFromCubemap_float(UnityTextureCube Cubemap, UnitySamplerState Sampler, float3 Dir, float Offset, float Strength, float LOD, out float3 Out)
{
    Offset = pow(Offset, 3) * 0.1;
    
    float normalSample = Cubemap.SampleLevel(Sampler, Dir, LOD).r;
    
    // Create orthogonal tangent vectors on the sphere
    float3 up = float3(0, 1, 0);
    float3 tangent = normalize(cross(up, Dir));
    float3 bitangent = normalize(cross(Dir, tangent));
    
    float3 offsetU = normalize(Dir + tangent * Offset);
    float3 offsetV = normalize(Dir + bitangent * Offset);
    
    float uSample = Cubemap.SampleLevel(Sampler, offsetU, LOD).r;
    float vSample = Cubemap.SampleLevel(Sampler, offsetV, LOD).r;
    
    float3 va = tangent + Dir * (uSample - normalSample) * Strength;
    float3 vb = bitangent + Dir * (vSample - normalSample) * Strength;
    
    Out = normalize(cross(va, vb));
}