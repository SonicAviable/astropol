#include "Assets/ParallelCascades/ProceduralShaders/HLSL/RaySphereIntersection.hlsl"

void corona_float(
    float4 originalColor,
    float4 coronaColor,
    float linear01Depth,
    float starRadius,
    float coronaRadius,
    float3 cameraPosition,
    float3 viewDirection,
    float farClipPlane,
    float3 starPosition,
    float densityFalloff,
    float glowIntensity,
    out float4 outColor)
{
    float sceneOpaqueDistance = linear01Depth * farClipPlane;
    
    float distanceToStar = raySphere(starPosition,starRadius, cameraPosition, viewDirection);
    float distanceToSurface = min(sceneOpaqueDistance,distanceToStar);
    
    float2 coronaIntersect = raySphere(starPosition,coronaRadius, cameraPosition, viewDirection);
    float distanceToCorona = coronaIntersect.x;
    
    float distanceThroughCorona = min(coronaIntersect.y - coronaIntersect.x, distanceToSurface - distanceToCorona);

    if(distanceThroughCorona > 0 && distanceToCorona <= sceneOpaqueDistance)
    {
        float distanceToCenter = length(cameraPosition - starPosition);
        bool isInsideCorona = distanceToCenter < coronaRadius;
    
        float maxPossibleDistance = coronaRadius * 2.0;
        if (isInsideCorona)
        {
            maxPossibleDistance = coronaRadius + length(cameraPosition - starPosition);
        }
        
        float corona = distanceThroughCorona  / maxPossibleDistance;
        float coronaWithFalloff = saturate(pow(corona, densityFalloff));
        float originalColorTransmittance = exp(-coronaWithFalloff);
        originalColor = originalColor * originalColorTransmittance + coronaWithFalloff * glowIntensity * coronaColor;
    }

    outColor = originalColor;
}