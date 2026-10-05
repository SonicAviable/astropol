#include "Assets/ParallelCascades/ProceduralShaders/HLSL/RaySphereIntersection.hlsl"

void glow_float(
    float3 lightDirection,
    float4 originalColor,
    float4 glowColor,
    float linear01Depth,
    float objectRadius,
    float effectRadius,
    float3 cameraPosition,
    float3 viewDirection,
    float farClipPlane,
    float3 objectPosition,
    float densityFalloff,
    float glowIntensity,
    out float4 outColor)
{
    float sceneOpaqueDistance = linear01Depth * farClipPlane;
    
    float2 distanceToObject = raySphere(objectPosition,objectRadius, cameraPosition, viewDirection);
    float distanceToSurface = min(sceneOpaqueDistance,distanceToObject);
    
    float2 glowIntersect = raySphere(objectPosition,effectRadius, cameraPosition, viewDirection);
    float distanceToGlow = glowIntersect.x;
    
    float distanceThroughGlow = min(glowIntersect.y - glowIntersect.x, distanceToSurface - distanceToGlow);

    if(distanceThroughGlow > 0 && distanceToGlow <= sceneOpaqueDistance)
    {
        float distanceToCenter = length(cameraPosition - objectPosition);
        bool isInsideGlow = distanceToCenter < effectRadius;

        float maxPossibleDistance = effectRadius * 2.0;
        if (isInsideGlow)
        {
            maxPossibleDistance = effectRadius + length(cameraPosition - objectPosition);
        }
        
        float glow = distanceThroughGlow / maxPossibleDistance;

        float coronaWithFalloff = saturate(pow(glow, densityFalloff));
        float originalColorTransmittance = exp(-coronaWithFalloff);

        // Lighting influence
        float3 samplePosition = cameraPosition + viewDirection * (distanceToGlow + distanceThroughGlow * 0.5); // sample at midpoint(0.5) since we're only sampling once, so it's representative of the glow volume, rather than the edges
        float3 directionToCenter = normalize(objectPosition - samplePosition);
        
        float3 normal = -directionToCenter;
        
        float lightFactor = saturate(dot(normal, -lightDirection));
        float4 litColor = glowColor * lightFactor;

        originalColor = originalColor * originalColorTransmittance + coronaWithFalloff * glowIntensity * litColor;
    }
    outColor = originalColor;
}