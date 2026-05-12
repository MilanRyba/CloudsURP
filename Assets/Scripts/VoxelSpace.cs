using System;
using UnityEngine;

[CreateAssetMenu(fileName = "VoxelSpace", menuName = "Scriptable Objects/VoxelSpace")]
public class VoxelSpace : ScriptableObject
{
	[Tooltip("World space size of the voxel space in meters.")]
	public Vector3Int WorldExtents = new Vector3Int(2048, 256, 2048);

	[Tooltip("Use this to offset the voxel space. (0, 0, 0) centers the grid around origin.")]
	public Vector3 WorldOffset = new Vector3(0, 64, 0);

	[Min(0.2f), Tooltip("Voxels are cubes with side lengths of VoxelSize meters.")]
	public float VoxelSize = 4.0f;

	public int NumVoxelsX => (int)(WorldExtents.x / VoxelSize);
	public int NumVoxelsY => (int)(WorldExtents.y / VoxelSize);
	public int NumVoxelsZ => (int)(WorldExtents.z / VoxelSize);
	public int Volume => NumVoxelsX * NumVoxelsY * NumVoxelsZ;

	public Vector3 VoxelGridResolution => new Vector3(NumVoxelsX, NumVoxelsY, NumVoxelsZ);
	public Vector3Int VoxelGridResolutionInt => new Vector3Int(NumVoxelsX, NumVoxelsY, NumVoxelsZ);
	public Vector3 VoxelGridOrigin => -(WorldExtents / 2) + WorldOffset;

	[Space(10)]
	[Header("Ellipsoids")]

	[Range(1, 100)]
	public int NumEllipsoids = 33;

	[Range(1.0f, 512.0f)]
	public float ScaleMean = 218.0f;

	[Range(0.0f, 128.0f)]
	public float ScaleDeviation = 56.0f;

	[Min(1)]
	public int EllispoidSeed = 32;

	public Texture2D WeatherMap;

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

	Ellipsoid[] m_Ellipsoids;

	public Ellipsoid[] Ellipsoids
	{
		get
		{
			if (m_Ellipsoids == null)
				CreateEllipsoids();

			return m_Ellipsoids;
		}
	}

	private Vector3 GetRandomVector3(Vector3 inMinInclusive, Vector3 inMaxInclusive)
	{
		float x = UnityEngine.Random.Range(inMinInclusive.x, inMaxInclusive.x);
		float y = UnityEngine.Random.Range(inMinInclusive.y, inMaxInclusive.y);
		float z = UnityEngine.Random.Range(inMinInclusive.z, inMaxInclusive.z);
		return new Vector3(x, y, z);
	}

	float Remap(float originalValue, float originalMin, float originalMax, float newMin, float newMax)
	{
		return newMin + (((originalValue - originalMin) / (originalMax - originalMin)) * (newMax - newMin));
	}

	private Vector3 Remap(Vector3 a, Vector3 b, Vector3 t)
	{
		Vector3 value = new Vector3();
		value.x = Remap(t.x, 0.0f, 1.0f, a.x, b.x);
		value.y = Remap(t.y, 0.0f, 1.0f, a.y, b.y);
		value.z = Remap(t.z, 0.0f, 1.0f, a.z, b.z);
		return value;
	}

	private void CreateEllipsoids()
	{
		UnityEngine.Random.InitState(EllispoidSeed);
		Vector3 voxelBoundsMin = VoxelGridOrigin;
		Vector3 voxelBoundsMax = VoxelGridOrigin + VoxelGridResolution * VoxelSize;

		float scaleXZMin = ScaleMean - ScaleDeviation;
		float scaleXZMax = ScaleMean + ScaleDeviation;
		Vector3 scaleMin = new Vector3(scaleXZMin, scaleXZMin * 0.5f, scaleXZMin);
		Vector3 scaleMax = new Vector3(scaleXZMax, scaleXZMax * 0.5f, scaleXZMax);

		// Shrink the voxel space so that the ellipsoids don't poke out
		voxelBoundsMin += scaleMax / 2.5f;
		voxelBoundsMax -= scaleMax / 2.5f;

		m_Ellipsoids = new Ellipsoid[NumEllipsoids];
		for (int i = 0; i < NumEllipsoids; i++)
		{
			Vector2Int randomPos = Vector2Int.zero;
			
			Color weatherData = Color.blanchedAlmond;
			for (int n = 0; n < 10; n++)
			{
				randomPos.x = UnityEngine.Random.Range(0, WeatherMap.width);
				randomPos.y = UnityEngine.Random.Range(0, WeatherMap.height);
				weatherData = WeatherMap.GetPixel(randomPos.x, randomPos.y);

				if (weatherData.b >= 0.7f)
					break;
			}

			Vector3 ellipsoidPosition = Vector3.zero;
			ellipsoidPosition.x = randomPos.x * (1.0f / WeatherMap.width);
			ellipsoidPosition.y = weatherData.r;
			ellipsoidPosition.z = randomPos.y * (1.0f / WeatherMap.height);
			m_Ellipsoids[i] = new Ellipsoid(Remap(voxelBoundsMin, voxelBoundsMax, ellipsoidPosition), 
				Remap(scaleMin, scaleMax, Vector3.one * (weatherData.r + UnityEngine.Random.Range(-0.4f, 0.4f))));
		}
	}

	private void OnValidate()
	{
		CreateEllipsoids();
	}
}
