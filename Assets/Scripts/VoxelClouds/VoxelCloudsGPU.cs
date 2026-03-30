using Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public class VoxelCloudsGPU : MonoBehaviour
{
	[Header("Simulation")]

	[SerializeField]
	ComputeShader m_Shader;

	[SerializeField]
	Vector3Int m_WorldExtents = new Vector3Int(20, 10, 20);

	[SerializeField, Min(0.2f)]
	float m_VoxelSize = 1.0f;

	[SerializeField, Min(0)]
	int m_CloudSpeed = 1;

	[SerializeField, Range(0.0f, 1.0f)]
	float m_ProbabilityExtiction = 0.5f;

	[SerializeField]
	Transform[] m_Ellipsoids;

	[SerializeField, Range(0.0f, 2.0f)]
	float m_TimeBetweenUpdates = 0.5f;

	[Header("Rendering")]

	[SerializeField]
	Material m_Material;

	[SerializeField]
	Mesh m_Mesh;

	[SerializeField]
	Mesh m_EllipsoidMesh;

	enum Visualization { Humidity, Clouds, Activation }

	[SerializeField]
	Visualization m_Visualization = Visualization.Clouds;


	float m_TimeSinceLastUpdate = 0.0f;
	int m_CloudOffset = 0;

	int NumVoxelsX => (int)(m_WorldExtents.x / m_VoxelSize);
	int NumVoxelsY => (int)(m_WorldExtents.y / m_VoxelSize);
	int NumVoxelsZ => (int)(m_WorldExtents.z / m_VoxelSize);
	int Volume => NumVoxelsX * NumVoxelsY * NumVoxelsZ;

	Vector3 VoxelGridOrigin => -(m_WorldExtents / 2) + transform.position;

	private ComputeBuffer m_PositionsBuffer;
	private ComputeBuffer m_EllipsoidsBuffer;

	private RenderTexture m_TextureCurrent;
	private RenderTexture m_TextureNext;

	public struct Ellipsoid
	{
		public Vector4 Position;
		public Vector4 Scale;
	}

	private Ellipsoid[] m_EllipsoidsData;

	private void OnEnable()
	{
		CreateResources();
		ResetVoxels();
	}

	private void OnDisable()
	{
		ReleaseResources();
	}

	private void CreateResources()
	{
		var desc = new RenderTextureDescriptor(NumVoxelsX, NumVoxelsY);
		desc.volumeDepth = NumVoxelsZ;
		desc.graphicsFormat = GraphicsFormat.R8_UInt;
		desc.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
		desc.enableRandomWrite = true;
		m_TextureCurrent = new RenderTexture(desc);
		m_TextureNext = new RenderTexture(desc);
		m_TextureCurrent.Create();
		m_TextureNext.Create();

		m_PositionsBuffer = new ComputeBuffer(Volume, GraphicsHelper.GetStride<Vector3>());

		m_EllipsoidsBuffer = new ComputeBuffer(m_Ellipsoids.Length, GraphicsHelper.GetStride<Ellipsoid>());
		m_EllipsoidsData = new Ellipsoid[m_Ellipsoids.Length];
	}

	private void ReleaseResources()
	{
		m_TextureCurrent.Release();
		m_TextureCurrent = null;

		m_TextureNext.Release();
		m_TextureNext = null;

		m_PositionsBuffer.Release();
		m_PositionsBuffer = null;

		m_EllipsoidsBuffer.Release();
		m_EllipsoidsBuffer = null;

		m_EllipsoidsData = null;
	}

	private void ResetVoxels()
	{
		DispatchShader("ResetCS");
		SwapBuffers();
		m_CloudOffset = 0;
	}

	private void SwapBuffers()
	{
		RenderTexture temp = m_TextureCurrent;
		m_TextureCurrent = m_TextureNext;
		m_TextureNext = temp;
	}

	private void Update()
	{
		// if (Input.GetKeyDown(KeyCode.J))
		// {
		// 	ResetVoxels();
		// }

		m_TimeSinceLastUpdate += Time.deltaTime;
		if (m_TimeSinceLastUpdate >= m_TimeBetweenUpdates)
		// if (Input.GetKeyDown(KeyCode.K))
		{
			// Reset timer
			m_TimeSinceLastUpdate = 0.0f;

			m_CloudOffset += m_CloudSpeed;

			// Run the simulation step
			DispatchShader("SimulationCS");

			// Swap the automatons
			SwapBuffers();
		}

		RenderVoxels();
	}

	private void RenderVoxels()
	{
		// Recalculate new positions
		DispatchShader("PositionsCS");

		m_Material.SetBuffer("_Positions", m_PositionsBuffer);
		m_Material.SetFloat("_VoxelSize", m_VoxelSize);

		RenderParams renderParams = new RenderParams(m_Material);
		renderParams.worldBounds = new Bounds(Vector3.zero, m_WorldExtents / 2);

		Graphics.RenderMeshPrimitives(renderParams, m_Mesh, 0, m_PositionsBuffer.count);
	}

	private void DispatchShader(string inKernelName)
	{
		int kernel = m_Shader.FindKernel(inKernelName);

		m_Shader.SetFloat("_VoxelSize", m_VoxelSize);
		m_Shader.SetInt("_Volume", Volume);
		m_Shader.SetVector("_VoxelGridResolution", new Vector3(NumVoxelsX, NumVoxelsY, NumVoxelsZ));
		m_Shader.SetVector("_VoxelGridOrigin", VoxelGridOrigin);

		m_Shader.SetInt("_CloudSpeed", m_CloudSpeed);
		m_Shader.SetInt("_CloudOffset", m_CloudOffset);
		m_Shader.SetInt("_Visualization", (int)m_Visualization);
		m_Shader.SetInt("_Seed", Random.Range(300, 1000));

		m_Shader.SetTexture(kernel, "_AutomatonFrom", m_TextureCurrent);
		m_Shader.SetTexture(kernel, "_AutomatonTo", m_TextureNext);
		m_Shader.SetBuffer(kernel, "_Positions", m_PositionsBuffer);

		for (int i = 0; i <  m_Ellipsoids.Length; i++)
		{
			m_EllipsoidsData[i].Position = m_Ellipsoids[i].position;
			m_EllipsoidsData[i].Scale = m_Ellipsoids[i].localScale;
		}
		m_EllipsoidsBuffer.SetData(m_EllipsoidsData);
		m_Shader.SetBuffer(kernel, "_Ellipsoids", m_EllipsoidsBuffer);
		m_Shader.SetInt("_NumEllipsoids", m_Ellipsoids.Length);

		GraphicsHelper.DispatchOld(m_Shader, NumVoxelsX, NumVoxelsY, NumVoxelsZ, kernel);
	}

	private void OnDrawGizmos()
	{
		Vector3 origin = transform.position;

		Gizmos.color = Color.yellow;
		Gizmos.DrawWireCube(origin, m_WorldExtents);

		Gizmos.color = Color.red;
		Gizmos.DrawRay(origin, Vector3.right * (m_WorldExtents.x / 2));

		Gizmos.color = Color.green;
		Gizmos.DrawRay(origin, Vector3.up * (m_WorldExtents.y / 2));

		Gizmos.color = Color.blue;
		Gizmos.DrawRay(origin, Vector3.forward * (m_WorldExtents.z / 2));

		Gizmos.color = Color.green;
		foreach (var e in m_Ellipsoids)
			Gizmos.DrawWireMesh(m_EllipsoidMesh, e.position, Quaternion.identity, e.localScale);
	}
}
