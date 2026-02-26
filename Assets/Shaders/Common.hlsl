#pragma once

// Constants
static const float PI = 3.1415;

//
// Hashing functions
//

#define FPRIME 1317666547U
#define VPRIME uint3(3480082861U, 2420690917U, 2149110343U)
#define SMALLESTFLOAT (1.0 / float(0xffffffffU))
float hash13(float3 p)
{
    uint3 q = uint3(int3(p)) * VPRIME;
    uint n = (q.x & q.y ^ q.z) * FPRIME;
    return float(n) * SMALLESTFLOAT;
}

// 3 in 3 out hash
#define UI0 1597334673U
#define UI1 3812015801U
#define UI3 uint3(UI0, UI1, 2798796415U)
#define UIF (1.0 / float(0xffffffffU))
float3 hash33(float3 p)
{
    uint3 q = uint3(int3(p)) * UI3;
    q = (q.x ^ q.y ^ q.z) * UI3;
    return float3(q) * UIF;
}

//
// Inverse Lerp function
//

#define InverseLerpFuncionDef(DATA_TYPE) \
    DATA_TYPE InverseLerp(DATA_TYPE inValue, DATA_TYPE inMin, DATA_TYPE inMax) \
    { \
        return (inValue - inMin) / (inMax - inMin); \
    }

InverseLerpFuncionDef(float)
InverseLerpFuncionDef(float2)
InverseLerpFuncionDef(float3)
InverseLerpFuncionDef(float4)

// Maps a value from one range to another
float Remap(float originalValue, float originalMin, float originalMax, float newMin, float newMax)
{
    return newMin + (((originalValue - originalMin) / (originalMax - originalMin)) * (newMax - newMin));
}

//
// Modulo function
//

#define ModFuncionDef(DATA_TYPE) \
    DATA_TYPE mod(DATA_TYPE inX, DATA_TYPE inY) \
    { \
        return inX - inY * floor(inX / inY); \
    }

ModFuncionDef(float)
ModFuncionDef(float2)
ModFuncionDef(float3)
ModFuncionDef(float4)

// Returns [0, 1] UV coordinates.
// inTexel - Integer pixel coordinate.
// inTexelSize - (1 / number of pixels) in each dimension.
float2 TexelToUV(uint2 inTexel, float2 inTexelSize)
{
    return ((float2) inTexel + 0.5f) * inTexelSize;
}

// Returns [0, 1] UV coordinates.
// inTexel - Integer pixel coordinate.
// inTexelSize - (1 / number of pixels) in each dimension.
float3 TexelToUV(uint3 inTexel, float3 inTexelSize)
{
    return ((float3) inTexel + 0.5f) * inTexelSize;
}

uint Flatten3D(uint3 coord, uint2 dimensionsXY)
{
    return coord.x + coord.y * dimensionsXY.x + coord.z * dimensionsXY.x * dimensionsXY.y;
}

float4 MaskChannels(float4 inValue, float4 inChannelMask)
{
    if (inChannelMask.r == 1)
        return float4(inValue.r, inValue.r, inValue.r, 1.0);
    else if (inChannelMask.g == 1)
        return float4(inValue.g, inValue.g, inValue.g, 1.0);
    else if (inChannelMask.b == 1)
        return float4(inValue.b, inValue.b, inValue.b, 1.0);
    else if (inChannelMask.a == 1)
        return float4(inValue.a, inValue.a, inValue.a, 1.0);
    
    return inValue;
}

struct Ray
{
    float3 mOrigin;
    float3 mDirection;
};

Ray GetCameraRay(float2 inUV, float3 inCameraPosition, float4x4 inProjInv, float4x4 inViewInv)
{
    Ray ray;
    ray.mOrigin = inCameraPosition;
    ray.mDirection = mul(inProjInv, float4(inUV * 2.0 - 1.0, 0.0f, 1.0f)).xyz;
    ray.mDirection = mul(inViewInv, float4(ray.mDirection, 0.0f)).xyz;
    ray.mDirection = normalize(ray.mDirection);
    return ray;
}

//
// Collisions
//

struct RayCastResult
{
    // Distance to the near intersection. If mInside is true, then mT0 is 0.0.
    float mT0;
    
    // Distance to the far intersection. Use this if mInside is true.
    float mT1;
    
    // Is the ray origin inside the object.
    bool mInside;
};

// Test for ray-box intersection. Returns true if there is at least one intersection.
// If ray origin is inside the sphere, mInside is set to true and mT0 will be 0.
// Properpties of outResult are not set if there was no hit.
bool RayBoxIntersection(float3 inBoundsMin, float3 inBoundsMax, Ray inRay, out RayCastResult outResult)
{
    float3 invRaydir = 1.0 / inRay.mDirection;

    float3 t0 = (inBoundsMin - inRay.mOrigin) * invRaydir;
    float3 t1 = (inBoundsMax - inRay.mOrigin) * invRaydir;
    float3 tmin = min(t0, t1);
    float3 tmax = max(t0, t1);
                
    outResult.mT0 = max(max(tmin.x, tmin.y), tmin.z);
    outResult.mT1 = min(tmax.x, min(tmax.y, tmax.z));
    outResult.mInside = false;
    
    if (outResult.mT0 > outResult.mT1)
        return false;
    
    if (outResult.mT0 < 0.0)
    {
        // T0 and T1 are both negative -> box is behind ray origin
        if (outResult.mT1 < 0.0)
            return false;
        
        outResult.mInside = true;
        outResult.mT0 = 0.0;
    }
    
    return true;
}

// Test for ray-sphere intersection. Returns true if there is at least one intersection.
// If ray origin is inside the sphere, mInside is set to true and mT0 will be 0.
// Properpties of outResult are not set if there was no hit.
bool RaySphereIntersection(float3 inCenter, float inRadius, Ray ray, out RayCastResult outResult)
{
    float3 of = ray.mOrigin - inCenter;
    const float a = 1.0;
    float b = 2.0 * dot(of, ray.mDirection);
    float c = dot(of, of) - inRadius * inRadius;
    float discriminant = b * b - 4.0 * a * c;
    
    if (discriminant < 0.0)
        return false;
    
    discriminant = sqrt(discriminant);
    outResult.mT0 = (-b - discriminant) / (2.0 * a);
    outResult.mT1 = (-b + discriminant) / (2.0 * a);
    outResult.mInside = false;
    
    if (outResult.mT0 < 0.0)
    {
        // T0 and T1 are both negative -> sphere is behind ray origin
        if (outResult.mT1 < 0.0)
            return false;
        
        outResult.mInside = true;
        outResult.mT0 = 0.0;
    }
    
    return true;
}

// float2 RaySphereIntersection(float3 inCenter, float inRadius, Ray ray)
// {
//     float3 of = ray.mOrigin - inCenter;
//     const float a = 1.0;
//     float b = 2.0 * dot(of, ray.mDirection);
//     float c = dot(of, of) - inRadius * inRadius;
//     float discriminant = b * b - 4.0 * a * c;
// 
//     if (discriminant > 0)
//     {
//         discriminant = sqrt(discriminant);
//         float dstToSphereNear = max(0.0, (-b - discriminant) / (2.0 * a));
//         float dstToSphereFar = (-b + discriminant) / (2.0 * a);
// 
//         if (dstToSphereFar >= 0.0)
//         {
//             return float2(dstToSphereNear, dstToSphereFar - dstToSphereNear);
//         }
//     }
//     return float2(0.0, 0.0);
// }

//
// Phase functions
//

float IsotropicPhase()
{
    return 1.0 / (4.0 * PI);
}

float HenyeyGreenstein(float inCosAngle, float inEccentricity)
{
    float eccentricity2 = inEccentricity * inEccentricity;
    return ((1.0 - eccentricity2) / pow((1.0 + eccentricity2 - 2.0 * inEccentricity * inCosAngle), 3.0 / 2.0)) / 4.0 * PI;
}

// 2-lobe phase function from 'Physically Based Sky, Atmosphere and Cloud Rendering in Frostbite'
// Allows users to better balance forward and backward scattering
// Default: inForwardScatter = 0.8, inBackwardScatter = -0.5, inWeight = 0.5
float DualLobePhase(float inCosAngle, float inForwardScatter, float inBackwardScatter, float inWeight)
{
    return lerp(HenyeyGreenstein(inCosAngle, inForwardScatter), HenyeyGreenstein(inCosAngle, inBackwardScatter), inWeight);
}

// Phase function presented in 'Nubis: Authoring Real-Time Volumetric Cloudscapes with the Decima Engine'
// inSilverIntensity controls the intensity of the second phase function and inSilverSpread controls its spread away from the sun
float HorizonPhase(float inCosAngle, float inEccentricity, float inSilverIntensity, float inSilverSpread)
{
    return max(HenyeyGreenstein(inCosAngle, inEccentricity), inSilverIntensity * HenyeyGreenstein(inCosAngle, 0.99 - inSilverSpread));
}

//
// Noise
//

float InterleavedGradientNoise(float2 uv)
{
    const float3 magic = float3(0.06711056f, 0.00583715f, 52.9829189f);
    return frac(magic.z * frac(dot(uv, magic.xy)));
}