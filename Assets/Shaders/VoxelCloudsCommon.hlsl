#pragma once

//==============================
// Voxel Byte Layout - 0000 0HCA
//   H = Humidity
//   C = Clouds
//   A = Activation
//==============================

struct CloudVoxelData
{
    bool Humidity;
    bool Clouds;
    bool Activation;
};

CloudVoxelData UnpackVoxel(uint inVoxel)
{
    CloudVoxelData voxelData;
    voxelData.Humidity = (inVoxel & (1 << 2)) != 0;
    voxelData.Clouds = (inVoxel & (1 << 1)) != 0;
    voxelData.Activation = (inVoxel & (1 << 0)) != 0;
    return voxelData;
}

uint PackVoxel(CloudVoxelData inData)
{
    uint voxel = 0;
    voxel |= inData.Humidity ? 1 << 2 : 0;
    voxel |= inData.Clouds ? 1 << 1 : 0;
    voxel |= inData.Activation ? 1 << 0 : 0;
    return voxel;
}

uint _Visualization; // 0 = Humidity, 1 = Clouds, 2 = Activation, 3 = All

float4 ColorFromVoxelVisualization(CloudVoxelData inData)
{
    bool assertion = false;
    if (_Visualization == 0)
        assertion = inData.Humidity;
    else if (_Visualization == 1)
        assertion = inData.Clouds;
    else if (_Visualization == 2)
        assertion = inData.Activation;
    
    if (assertion)
        return 1.0;
    else if (_Visualization == 3)
    {
        float4 color = 1.0;
        color.r = inData.Humidity ? 1.0 : 0.0;
        color.g = inData.Clouds ? 1.0 : 0.0;
        color.b = inData.Activation ? 1.0 : 0.0;
        return color;
    }
    else
        return 0.0;
}

// +----------------------+     +-------------------+     +-------+
// | World Space Position | <=> | Voxel Coordinates | => | Index |
// +----------------------+     +-------------------+     +-------+

// !! If you want to use the functions below, you must set these uniforms(?) from Unity !!

float _VoxelSize;          // Each voxel is a cube with side lengths of _VoxelSize meters
int3 _VoxelGridResolution; // Number of voxels in each dimension
float3 _VoxelGridOrigin;   // World space position of voxel at coordinates (0, 0, 0)

bool OutOfBoundsVoxelCoords(int3 inVoxelCoords)
{
    return any(inVoxelCoords < 0.0 || inVoxelCoords >= _VoxelGridResolution);
}

// Get voxel index from voxel coordinates. Returns -1 if coords are out of bounds.
int IdxFromVoxelCoords(int3 inVoxelCoords)
{
    if (OutOfBoundsVoxelCoords(inVoxelCoords))
        return -1;
    
    return inVoxelCoords.x + inVoxelCoords.y * _VoxelGridResolution.x + inVoxelCoords.z * _VoxelGridResolution.y * _VoxelGridResolution.x;
}

// Get voxel coordinates from world space position.
int3 VoxelCoordsFromWorldPosition(float3 inPosition)
{
    float3 localPos = inPosition - _VoxelGridOrigin;
    return int3(floor(localPos.x / _VoxelSize), floor(localPos.y / _VoxelSize), floor(localPos.z / _VoxelSize));
}

// Get world space position of voxel center
float3 WorldPositionFromVoxelCoords(int3 inVoxelCoords)
{
    // Scale voxel coordinates
    float3 position = inVoxelCoords * _VoxelSize;

	// Apply offset so that the position is in voxel center
    float offset = 0.5f * _VoxelSize;
    position += offset;

    // Translate the position relative to the grid origin
    position += _VoxelGridOrigin;

    return position;
}
