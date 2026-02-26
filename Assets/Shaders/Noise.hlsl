#pragma once

#include "Common.hlsl"
#include "Worley.hlsl"
#include "Perlin.hlsl"
#include "Alligator.hlsl"

float WorleyFBMDecima(float3 inPosition, float inBaseFrequency)
{    
    float worley1 = WorleyNoise(inPosition, inBaseFrequency * 19.0, float3(0, 200, 0)) / 3.0;
    float worley2 = WorleyNoise(inPosition, inBaseFrequency * 10.0, float3(0, 0, 0));
    float worley3 = WorleyNoise(inPosition, inBaseFrequency * 25.0, float3(0, 400, 0)) / 6.0;
    float worley4 = WorleyNoise(inPosition, inBaseFrequency * 37.0, float3(0, 800, 0)) / 8.0;
    float worley5 = WorleyNoise(inPosition, inBaseFrequency * 5.0,  float3(0, 0, 0))   / 3.0;
    
    float average = (worley1 + worley2 + worley3 + worley4 + worley5) * 0.2;
    average = 1.0 - average;
    average = Remap(average, 0.65, 1.0, 0.0, 1.0);
    
    return average;
}

float3 WorleyDecima(float3 inPosition)
{
    float worleyFBM0 = WorleyFBMDecima(inPosition, 1.0);
    float worleyFBM1 = WorleyFBMDecima(inPosition, 1.7);
    float worleyFBM2 = WorleyFBMDecima(inPosition, 2.3);
    return float3(worleyFBM0, worleyFBM1, worleyFBM2);
}

float3 WorleyFrostbite(float3 inPosition)
{
    const float baseFrequency = 4.0;
    
    float worleyNoise1 = WorleyNoise(inPosition, baseFrequency * 2.0, 0.0);
    float worleyNoise2 = WorleyNoise(inPosition, baseFrequency * 4.0, 0.0);
    float worleyNoise3 = WorleyNoise(inPosition, baseFrequency * 8.0, 0.0);
    float worleyNoise4 = WorleyNoise(inPosition, baseFrequency * 16.0, 0.0);

	// Three frequency of Worley FBM noise
    float worleyFBM0 = worleyNoise1 * 0.625f + worleyNoise2 * 0.25f + worleyNoise3 * 0.125f;
    float worleyFBM1 = worleyNoise2 * 0.625f + worleyNoise3 * 0.25f + worleyNoise4 * 0.125f;
    float worleyFBM2 = worleyNoise3 * 0.75f + worleyNoise4 * 0.25f;
    
    worleyFBM0 = 1.0 - worleyFBM0;
    worleyFBM1 = 1.0 - worleyFBM1;
    worleyFBM2 = 1.0 - worleyFBM2;
    
    return float3(worleyFBM0, worleyFBM1, worleyFBM2);
}

float PerlinFBM(float3 inPosition, float inFrequency, float3 inOffset, float inOctaves)
{
    inPosition += inOffset;
    
    float octaveFrenquencyFactor = 2.0; // noise frequency factor between octave, forced to 2

	// Compute the sum for each octave
    float sum = 0.0f;
    float weightSum = 0.0f;
    float weight = 0.5f;
    for (int oct = 0; oct < inOctaves; oct++)
    {
        float4 p = float4(inPosition, 0.0) * inFrequency;
        // Apply [-1, 1] -> [0, 1] here due to the normalization at the end
        float val = glmPerlin4D(p, inFrequency);

        sum += val * weight;
        weightSum += weight;

        weight *= weight;
        inFrequency *= octaveFrenquencyFactor;
    }

    float noise = (sum / weightSum);
    return noise;
}

float PerlinFBM2(float3 inPosition, int inFrequency, int inOctaves)
{
    int lacunarity = 2;
    float persistence = 0.5;
    
    int frequency = inFrequency;
    float amplitude = 1.0;
    float amplitudeSum = 0.0;
    float sum = 0.0;
    
    for (int i = 0; i < inOctaves; i++)
    {
        sum += amplitude * PerlinNoise(inPosition, frequency);
        frequency *= lacunarity;
        amplitudeSum += amplitude;
        amplitude *= 0.5;
    }
    
    return (sum / amplitudeSum) * 0.5 + 0.5;
}

float PerlinWorley(float3 inPosition, float inFrequency, float3 inOffset, float inAmplitude, float inRoughness, float inOctaves)
{
    float perlin = PerlinFBM(inPosition, inFrequency, inOffset, inOctaves);
    perlin = Remap(perlin, -0.6, 0.6, 0, 1);
    
    float baseFrequency = 5.0;
    
    float worley1 = -0.1 * (1.0 - WorleyNoise(inPosition, baseFrequency * 10, 0.0));
    perlin = Remap(perlin, worley1, 1, 0, 1);
    
    float worley2 = -0.1 * (1.0 - WorleyNoise(inPosition, baseFrequency * 4, 0.0));
    perlin = Remap(perlin, worley2, 1, 0, 1);
    
    float worley3 = 0.2 * WorleyNoise(inPosition, baseFrequency * 2, 0.0);
    perlin = Remap(perlin, worley3, 1, 0, 1);
    
    float worley4 = 0.2 * WorleyNoise(inPosition, baseFrequency * 4, 0.0);
    perlin = Remap(perlin, worley4, 1, 0, 1);
    
    return Remap(perlin, -0.1, 1.1, 0, 1);
}

// Alligator noise with octaves
float AlligatorFBM(float3 inPosition, int inFrequency, float3 inOffset, int inOctaves)
{
    // Move these as parameters
    float lacunarity = 2.0;
    float persistence = 0.5;
    uint3 seed = 421;
    
    float amplitude = 1.0;
    float amplitudeSum = 0.0;
    float result = 0.0;
    
    // For each octave...
    for (int i = 0; i < inOctaves; i++)
    {
        // Sample noise and apply amplitude
        result += Alligator(inPosition, inFrequency, 0.0, seed) * amplitude;
        
        // Add up amplitude to normalize result later
        amplitudeSum += amplitude;
        
        // Increase frequency for the next octave
        inFrequency *= lacunarity;
        
        // Decrease amplitude for the next octave
        amplitude *= persistence;
        
        // Change seed/offset noise so it is unique for the next octave
        seed += inFrequency;
    }
    
    // Normalize the result to 0-1 range
    result /= amplitudeSum;

    return result;
}

float AlligatorDecima(float3 inPosition)
{
    float alligator1 = AlligatorFBM(inPosition, 4, float3(200.0, 200.0, 0.0), 5) * 1.2;
    alligator1 = Remap(alligator1, 0.01, 0.6, 0.0, 1.0);
    
    return alligator1;
}
