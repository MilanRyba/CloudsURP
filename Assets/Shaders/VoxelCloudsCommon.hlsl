#pragma once

//==============================
// Voxel Layout
//   X = Humidity
//   Y = Clouds
//   Z = Activation
//==============================

struct CloudVoxelData
{
    bool Humidity;
    bool Clouds;
    bool Activation;
};

void UnpackVoxel(uint4 inVoxels, int inBitIdx, out CloudVoxelData outVoxelData)
{
    outVoxelData.Humidity   = (inVoxels.x & (1 << inBitIdx)) != 0;
    outVoxelData.Clouds     = (inVoxels.y & (1 << inBitIdx)) != 0;
    outVoxelData.Activation = (inVoxels.z & (1 << inBitIdx)) != 0;
}

void PackVoxel(CloudVoxelData inData, int inBitIdx, inout uint4 ioVoxels)
{
    ioVoxels.x |= inData.Humidity   ? 1 << inBitIdx : 0;
    ioVoxels.y |= inData.Clouds     ? 1 << inBitIdx : 0;
    ioVoxels.z |= inData.Activation ? 1 << inBitIdx : 0;
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
