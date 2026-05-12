#pragma once

#include "Common.hlsl"

#define METRIC_EUCLIDEAN 0
#define METRIC_MANHATTAN 1
#define METRIC_CHEBYSHEV 2
#define METRIC_SQUARED 3

float ManhattanLength(float2 inV)
{
    return abs(inV.x) + abs(inV.y);
}

float ManhattanLength(float3 inV)
{
    return abs(inV.x) + abs(inV.y) + abs(inV.z);
}

float ChebyshevLength(float2 inV)
{
    return max(abs(inV.x), abs(inV.y));
}

float ChebyshevLength(float3 inV)
{
    return max(max(abs(inV.x), abs(inV.y)), abs(inV.z));
}

float WorleyNoiseMetric(float3 inV, int inMetric)
{
    if (inMetric == METRIC_EUCLIDEAN)
        return length(inV);
    else if (inMetric == METRIC_MANHATTAN)
        return ManhattanLength(inV);
    else if (inMetric == METRIC_CHEBYSHEV)
        return ChebyshevLength(inV);
    else if (inMetric == METRIC_SQUARED)
        return dot(inV, inV);

    return -1.0;
}

// Calculate Worley noise by determining the distance to the nearest feature point.
// The space is divided into cells and each cell contains one feature point.
//
// Returns a value in range [0, 1].
//
// inPosition - The sample position.
// inFrequency - Number of cells in each axis. To preserve tiling, the frequency needs to be a whole number.
// inOffset - Vector used to offset the sample position.
// inMetric - How should the distance to feature points be calculated. See WorleyNoiseMetric() function for possible options.
float WorleyNoise(float3 inPosition, uint3 inFrequency, float3 inOffset, int inMetric)
{
    // Scale and offset the position
    inPosition = inPosition * inFrequency + inOffset;
    
    // Integer coordinates
    float3 iPos = floor(inPosition);
    
    float d = 2.0;
    for (int ix = -1; ix <= 1; ix++)
    {
        for (int iy = -1; iy <= 1; iy++)
        {
            for (int iz = -1; iz <= 1; iz++)
            {
                // Integer coordinates of the current cell
                float3 currentCell = iPos + float3(ix, iy, iz);

                currentCell = inPosition - currentCell - hash33(mod(currentCell, inFrequency));

                d = min(d, WorleyNoiseMetric(currentCell, inMetric));
            }
        }
    }
    
    return saturate(d);
}

// Shorthand for calling WorleyNoise() using the euclidean metric for calculating distances.
float WorleyNoise(float3 inPosition, uint3 inFrequency, float3 inOffset)
{
    return WorleyNoise(inPosition, inFrequency, inOffset, METRIC_EUCLIDEAN);
}

float Cubed(float inV)
{
    return inV * inV * inV * inV;
}

float3 WorleyNoise2D(float2 inPosition, uint inFrequency, int inMetric)
{
    // Scale and offset the position
    inPosition = inPosition * inFrequency - float2(167798.0, 154416.0);
    
    // Integer coordinates
    float2 iPos = floor(inPosition);
    
    float f1 = 10.0;
    float f2 = 20.0;
    for (int ix = -1; ix <= 1; ix++)
    {
        for (int iy = -1; iy <= 1; iy++)
        {
            // Integer coordinates of the current cell
            float2 currentCell = iPos + float2(ix, iy);

            float3 cellPoint = hash33(float3(mod(currentCell, inFrequency), 0.0));
            // float3 cellPoint = hash33(float3(currentCell, 0.0));
            currentCell = inPosition - currentCell - cellPoint.xz;

            float currentDistance = length(currentCell);
            // float currentDistance = dot(currentCell, currentCell);
            
            if (currentDistance < f1)
            {
                f2 = f1;
                f1 = currentDistance;
            }
            else if (currentDistance < f2)
            {
                f2 = currentDistance;
            }
        }
    }
    
    float3 worley = 1.0 - saturate(f1);
    
    // float thickness = 0.02;
    // float2 grid = abs(frac(inPosition) * 2.0 - 1.0);
    // if (smoothstep(thickness, thickness, 1. - max(grid.x, grid.y)) != 1.0)
    //     worley = 1.0;
    // if (f1 < 0.06)
    //     worley.gb = 0.0;
    
    return worley;
}
