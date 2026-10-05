#include "Hash.hlsl"

#ifndef VORONOI_NOISE_2D_INCLUDED
#define VORONOI_NOISE_2D_INCLUDED

// returns float3(closest point distance, second closest point distance, cell id)
void voronoiNoise2d_float(float2 uv, float2 offset, float cellDensity, out float3 result)
{
    int cellDensityInt = (int)cellDensity;
    
    uv = uv * cellDensityInt + offset;
    float2 p = floor(uv);
    float2 f = frac(uv);

    float id = 0; 
    float2 res = float2(100.0,0);
    
    [unroll]
    for( int j=-1; j<=1; j++ )
    {
        [unroll]
        for( int i=-1; i<=1; i++ )
        {
            float2 b = float2(float(i),float(j));
            int2 gridCoord = (int2)floor(p + b);
            gridCoord = (gridCoord % cellDensityInt + cellDensityInt) % cellDensityInt; // wrap negative values to positive side
            
            float2 hash = Hash22_highp(gridCoord);

            float2 r = b - f +  hash;
            
            float d = dot(r,r);

            if( d < res.x )
            {
                id = dot( gridCoord, float3(1.0,57.0,113.0 ) );
                res = float2( d, res.x );			
            }
            else if( d < res.y )
            {
                res.y = d;
            }
        }
    }
    
    
    result = float3(sqrt(res.x),sqrt(res.y),abs(id));
}

#endif