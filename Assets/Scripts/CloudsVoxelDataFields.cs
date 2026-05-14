using System;
using UnityEngine;

[Serializable]
public class VoxelSpace
{
	[Tooltip("World space size of the voxel space in meters.")]
	public Vector3Int WorldExtents = new Vector3Int(2048, 256, 2048);

	[Tooltip("Use this to offset the voxel space. (0, 0, 0) centers the grid around origin.")]
	public Vector3 WorldOffset = new Vector3(0, 64, 0);

	[Min(0.2f), Tooltip("Voxels are cubes with side lengths of VoxelSize meters.")]
	public float VoxelSize = 4.0f;

	#region Properties

	public int NumVoxelsX => (int)(WorldExtents.x / VoxelSize);
	public int NumVoxelsY => (int)(WorldExtents.y / VoxelSize);
	public int NumVoxelsZ => (int)(WorldExtents.z / VoxelSize);
	public int Volume => NumVoxelsX * NumVoxelsY * NumVoxelsZ;

	public Vector3 VoxelGridResolution => new Vector3(NumVoxelsX, NumVoxelsY, NumVoxelsZ);
	public Vector3Int VoxelGridResolutionInt => new Vector3Int(NumVoxelsX, NumVoxelsY, NumVoxelsZ);
	public Vector3 VoxelGridOrigin => -(WorldExtents / 2) + WorldOffset;

	#endregion
}

[CreateAssetMenu(fileName = "CVDFs", menuName = "Scriptable Objects/CVDFs")]
public class CloudsVoxelDataFields : ScriptableObject
{
	public VoxelSpace Space;

	[Serializable]
	public struct Ellipsoid
	{
		public Vector4 Position;
		public Vector4 Scale;

		public Ellipsoid(Vector4 inPosition, Vector4 inScale)
		{
			Position = inPosition;
			Scale = inScale;
		}
	}

	public Ellipsoid[] Ellipsoids;

	public int NumEllipsoids => Ellipsoids.Length;
}
