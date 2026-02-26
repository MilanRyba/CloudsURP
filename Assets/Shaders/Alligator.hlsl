#pragma once

#include "Common.hlsl"

/*
 * Copyright (c) 2026
 *      Side Effects Software Inc.  All rights reserved.
 *
 * Redistribution and use of Houdini Development Kit samples in source and
 * binary forms, with or without modification, are permitted provided that the
 * following conditions are met:
 * 1. Redistributions of source code must retain the above copyright notice,
 *    this list of conditions and the following disclaimer.
 * 2. The name of Side Effects Software may not be used to endorse or
 *    promote products derived from this software without specific prior
 *    written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY SIDE EFFECTS SOFTWARE `AS IS' AND ANY EXPRESS
 * OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
 * OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.  IN
 * NO EVENT SHALL SIDE EFFECTS SOFTWARE BE LIABLE FOR ANY DIRECT, INDIRECT,
 * INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
 * LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA,
 * OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
 * LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
 * NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE,
 * EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 *
 *----------------------------------------------------------------------------
 */

// Smoothstep between 0 and 1. If inX is outside this range, the value is clamped.
float Smoothstep01(float inX)
{
    inX = saturate(inX);
    return inX * inX * (3.0 - 2.0 * inX);
}

// Alligator Noise, originally from Side Effects Software Inc.
// See: https://www.sidefx.com/docs/hdk/alligator_2alligator_8_c-example.html
//
// Returns a value in range [0, 1]. (I think)
float Alligator(float3 inPosition, uint3 inFrequency, float3 inOffset, int3 inSeed)
{
    // Scale and offset the position
    inPosition = inPosition * inFrequency + inOffset;
    
    float3 iPos = floor(inPosition); // Integer coordinates
    float3 fPos = inPosition - iPos; // Fractional coordinates
    
    float densest = -1.0;
    float secondDensest = -2.0;
    
    for (int ix = -1; ix <= 1; ix++)
    {
        for (int iy = -1; iy <= 1; iy++)
        {
            for (int iz = -1; iz <= 1; iz++)
            {
                // Offset to the neighbor cell
                float3 neighborOffset = float3(ix, iy, iz);
                
                // Integer coordinates of the current cell
                float3 currentCell = iPos + neighborOffset;
                
                // Mod the coordinates for tiling
                currentCell = mod(currentCell, float3(inFrequency));
                
                // Modify the result with some Offset
                currentCell += float3(inSeed);
                
                // Compute the center point for the given noise cell
                float3 center = hash33(currentCell) + neighborOffset;
                
                float dist = distance(fPos, center);
                
                // Scale distance by noise associated with the point.
                float density = hash13(currentCell) * Smoothstep01(1.0 - dist);
                    
                if (density > densest)
                {
                    secondDensest = densest;
                    densest = density;
                }
                else if (density > secondDensest)
                {
                    secondDensest = density;
                }
            }
        }
    }
    
    // Subtract two largest density values for the result
    return densest - secondDensest;
}
