#include "Packages/com.unity.render-pipelines.core//ShaderLibrary/Macros.hlsl"

#ifndef SPHERICAL_COORDINATE_UTILITIES_HLSL
#define SPHERICAL_COORDINATE_UTILITIES_HLSL

// Spherical coordinate conversion theory:
// https://www.scratchapixel.com/lessons/mathematics-physics-for-computer-graphics/geometry/spherical-coordinates-and-trigonometric-functions.html

// input gets normalized
// Output.x is azimuth in [-PI, PI], output.y is elevation in [0, PI]
void cart2sphe_float(float3 cartesianPosition, out float2 sphericalCoord)
{
    // fast normalization step - should be cheaper than normalize() which includes sqrt
    cartesianPosition *= rsqrt(dot(cartesianPosition, cartesianPosition));
    // atan2 computes the angle between the positive X axis and the point (x, y) in the 2D Cartesian plane 
    sphericalCoord = float2(atan2(cartesianPosition.z, cartesianPosition.x), acos(cartesianPosition.y));
}

// does not normalize input
void cart2sphe_raw_float(float3 cartesianPosition, out float2 sphericalCoord)
{
    sphericalCoord = float2(atan2(cartesianPosition.z, cartesianPosition.x), acos(cartesianPosition.y));
}

// input does not need to be normalized
// Normalized Output.x is azimuth in [0, 1], output.y is elevation in [0, 1]
void cart2sphe01_float(float3 cartesianPosition, out float2 sphericalCoord01)
{
    float2 sphericalCoord;
    cart2sphe_float(cartesianPosition, sphericalCoord);
    // sphericalCoord.x is azimuth in [-PI, PI], sphericalCoord.y is elevation in [0, PI]
    sphericalCoord01 = float2((sphericalCoord.x + PI) / (2.0 * PI), (sphericalCoord.y+PI) / -PI);
}

void cart2sphe01_raw_float(float3 cartesianPosition, out float2 sphericalCoord01)
{
    float2 sphericalCoord;
    cart2sphe_raw_float(cartesianPosition, sphericalCoord);
    // sphericalCoord.x is azimuth in [-PI, PI], sphericalCoord.y is elevation in [0, PI]
    sphericalCoord01 = float2((sphericalCoord.x + PI) / (2.0 * PI), sphericalCoord.y / PI);
}

// input period is 2 * PI for azimuth and PI for elevation
// outputs a unit sphere position with coordinates in the range [-1, 1]
void sphe2cart_float(float2 sphericalCoord, out float3 cartesianPosition)
{
    float2 s = sin(sphericalCoord);
    float2 c = cos(sphericalCoord);
    cartesianPosition = float3(c.x * s.y, c.y, s.x * s.y);
}
#endif
