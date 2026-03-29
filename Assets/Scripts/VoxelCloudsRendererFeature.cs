using Helpers;
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class VoxelCloudsRendererFeature : ScriptableRendererFeature
{
    [SerializeField] ComputeShader m_SimulationShader;

	[SerializeField] SimulationPassSettings m_SimulationPassSettings;

    SimulationPass m_SimulationPass;

	[Serializable]
	public struct Ellipsoid
	{
		public Vector4 Position;
		public Vector4 Scale;
	}

	public override void Create()
    {
        m_SimulationPass = new SimulationPass(m_SimulationPassSettings);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
		if (m_SimulationShader == null)
		{
			Debug.LogWarning("Simulation compute shader hasn't been assigned. Skipping SimulationPass.");
		}
		else
		{
			m_SimulationPass.Setup(m_SimulationShader);
			renderer.EnqueuePass(m_SimulationPass);
		}
    }

	protected override void Dispose(bool disposing)
	{
		m_SimulationPass.Cleanup();
		base.Dispose(disposing);
	}

	[Serializable]
    public class SimulationPassSettings
    {
		[Tooltip("World space size of the voxel space in meters.")]
		public Vector3Int WorldExtents = new Vector3Int(20, 10, 20);

		[Tooltip("Use this to offset the voxel space. (0, 0, 0) centers the grid around origin.")]
		public Vector3 WorldOffset = Vector3.zero;

		[Min(0.2f), Tooltip("Voxel are cubes with side lengths of VoxelSize meters.")]
		public float VoxelSize = 1.0f;

		[Min(0), Tooltip("How many units will each voxel move after a simulation step.")]
		public int CloudSpeed = 1;

		[Tooltip("Container for ellipsoids. These are used to define higher probability for cloud presence.")]
		public Ellipsoid[] Ellipsoids;

		[Range(0.0f, 2.0f), Tooltip("Number of second between each simulation step.")]
		public float TimeBetweenUpdates = 0.5f;

		[Header("Rendering")]

		[Tooltip("Mesh used for visualizing voxel. Use the default cube mesh here.")]
		public Mesh VoxelMesh;

		[Tooltip("Material used for rendering VoxelMeshes.")]
		public Material VoxelMaterial;

		[Serializable]
		public enum SimulationVis { Humidity, Clouds, Activation }

		[Tooltip("Toggle which simulation variable to preview.")]
		public SimulationVis Visualization = SimulationVis.Clouds;
	}

    class SimulationPass : ScriptableRenderPass
    {
        #region PassFields

        const string m_PassName = "SimulationPass";

        ComputeShader m_Shader;
        int m_KernelSimulation;
        int m_KernelReset;
        int m_KernelPositions;

		RTHandle m_TextureCurrent;
		RTHandle m_TextureNext;

		GraphicsBuffer m_PositionsBuffer;
		GraphicsBuffer m_EllipsoidsBuffer;

		readonly SimulationPassSettings m_Settings;

		float m_TimeSinceLastUpdate = 0.0f;
		int m_CloudOffset = 0;

		#endregion

		#region Properties

		public int NumVoxelsX => (int)(m_Settings.WorldExtents.x / m_Settings.VoxelSize);
		public int NumVoxelsY => (int)(m_Settings.WorldExtents.y / m_Settings.VoxelSize);
		public int NumVoxelsZ => (int)(m_Settings.WorldExtents.z / m_Settings.VoxelSize);
		public int Volume => NumVoxelsX * NumVoxelsY * NumVoxelsZ;

		public Vector3 VoxelGridResolution => new Vector3(NumVoxelsX, NumVoxelsY, NumVoxelsZ);
		public Vector3Int VoxelGridResolutionInt => new Vector3Int(NumVoxelsX, NumVoxelsY, NumVoxelsZ);
		public Vector3 VoxelGridOrigin => -(m_Settings.WorldExtents / 2) + m_Settings.WorldOffset;

		#endregion

		public SimulationPass(SimulationPassSettings inSettings)
        {
            m_Settings = inSettings;
            renderPassEvent = RenderPassEvent.BeforeRendering;
        }

		public void Setup(ComputeShader inShader)
		{
			m_Shader = inShader;

            // Find kernels
			m_KernelSimulation = m_Shader.FindKernel("SimulationCS");
            m_KernelReset = m_Shader.FindKernel("ResetCS");
            m_KernelPositions = m_Shader.FindKernel("PositionsCS");

			// Re-create automatons if needed
			GraphicsHelper.CreateAutomaton(ref m_TextureCurrent, VoxelGridResolutionInt, "_Automaton1");
			GraphicsHelper.CreateAutomaton(ref m_TextureNext, VoxelGridResolutionInt, "_Automaton2");

			if (m_PositionsBuffer == null || m_PositionsBuffer.count != Volume)
			{
				m_PositionsBuffer?.Release();
				m_PositionsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Volume, GraphicsHelper.GetStride<Vector3>());
			}

			int numEllipsoids = m_Settings.Ellipsoids.Length;
			if (m_EllipsoidsBuffer == null || m_EllipsoidsBuffer.count != numEllipsoids)
			{
				m_EllipsoidsBuffer?.Release();
				m_EllipsoidsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numEllipsoids, GraphicsHelper.GetStride<Ellipsoid>());
			}
			m_EllipsoidsBuffer.SetData(m_Settings.Ellipsoids);
		}

		private class PassData
        {
			public ComputeShader Shader;    // Reference to the compute shader
			public int Kernel;              // Kernel index
			public TextureHandle AutomatonFrom;  // Input texture
			public TextureHandle AutomatonTo;    // Output texture
			public BufferHandle Positions;
			public BufferHandle Ellipsoids;
		}

		private void SwapAutomatons(ref TextureHandle outCurrent, ref TextureHandle outNext)
		{
			RTHandle rtTtemp = m_TextureCurrent;
			m_TextureCurrent = m_TextureNext;
			m_TextureNext = rtTtemp;

			TextureHandle handle = outCurrent;
			outCurrent = outNext;
			outNext = handle;
		}

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
			TextureHandle current = graph.ImportTexture(m_TextureCurrent);
			TextureHandle next = graph.ImportTexture(m_TextureNext);
			BufferHandle positionsBuffer = graph.ImportBuffer(m_PositionsBuffer);
			BufferHandle ellipsoidsBuffer = graph.ImportBuffer(m_EllipsoidsBuffer);

			m_TimeSinceLastUpdate += Time.deltaTime;
			if (m_TimeSinceLastUpdate >= m_Settings.TimeBetweenUpdates)
			{
				// Reset timer
				m_TimeSinceLastUpdate = 0.0f;

				m_CloudOffset += m_Settings.CloudSpeed;

				// Run the simulation step
				using (IComputeRenderGraphBuilder builder = graph.AddComputePass("Simulation Pass", out PassData data))
				{
					data.Shader = m_Shader;
					data.Kernel = m_KernelSimulation;
					data.AutomatonFrom = current;
					data.AutomatonTo = next;
					data.Positions = positionsBuffer;
					data.Ellipsoids = ellipsoidsBuffer;

					builder.UseTexture(data.AutomatonFrom, AccessFlags.Read);
					builder.UseTexture(data.AutomatonTo, AccessFlags.Write);
					builder.UseBuffer(data.Positions, AccessFlags.Write);
					builder.UseBuffer(data.Ellipsoids, AccessFlags.Read);

					builder.SetRenderFunc((PassData inD, ComputeGraphContext inCtx) =>
					{
						inCtx.cmd.SetComputeTextureParam(inD.Shader, inD.Kernel, "_AutomatonFrom", inD.AutomatonFrom);
						inCtx.cmd.SetComputeTextureParam(inD.Shader, inD.Kernel, "_AutomatonTo", inD.AutomatonTo);
						inCtx.cmd.SetComputeBufferParam(inD.Shader, inD.Kernel, "_Positions", inD.Positions);

						inCtx.cmd.SetComputeBufferParam(inD.Shader, inD.Kernel, "_Ellipsoids", inD.Ellipsoids);
						m_Shader.SetInt("_NumEllipsoids", m_EllipsoidsBuffer.count);

						inCtx.cmd.SetComputeFloatParam(inD.Shader, "_VoxelSize", m_Settings.VoxelSize);
						inCtx.cmd.SetComputeIntParam(inD.Shader, "_Volume", Volume);
						inCtx.cmd.SetComputeVectorParam(inD.Shader, "_VoxelGridResolution", VoxelGridResolution);
						inCtx.cmd.SetComputeVectorParam(inD.Shader, "_VoxelGridOrigin", VoxelGridOrigin);

						inCtx.cmd.SetComputeIntParam(inD.Shader, "_CloudSpeed", m_Settings.CloudSpeed);
						inCtx.cmd.SetComputeIntParam(inD.Shader, "_CloudOffset", m_CloudOffset);
						inCtx.cmd.SetComputeIntParam(inD.Shader, "_Visualization", (int)m_Settings.Visualization);
						inCtx.cmd.SetComputeIntParam(inD.Shader, "_Seed", UnityEngine.Random.Range(300, 1000));

						GraphicsHelper.Dispatch(inCtx, inD.Shader, inD.Kernel, NumVoxelsX, NumVoxelsY, NumVoxelsZ);
					});
				}
				SwapAutomatons(ref current, ref next);
			}

			using (IComputeRenderGraphBuilder builder = graph.AddComputePass("Positions Pass", out PassData data))
			{
				data.Shader = m_Shader;
				data.Kernel = m_KernelPositions;
				data.AutomatonFrom = current;
				data.AutomatonTo = next;
				data.Positions = positionsBuffer;
				data.Ellipsoids = ellipsoidsBuffer;

				builder.UseTexture(data.AutomatonFrom, AccessFlags.Read);
				builder.UseTexture(data.AutomatonTo, AccessFlags.Write);
				builder.UseBuffer(data.Positions, AccessFlags.Write);
				builder.UseBuffer(data.Ellipsoids, AccessFlags.Read);

				builder.SetRenderFunc((PassData inD, ComputeGraphContext inCtx) =>
				{
					inCtx.cmd.SetComputeTextureParam(inD.Shader, inD.Kernel, "_AutomatonFrom", inD.AutomatonFrom);
					inCtx.cmd.SetComputeTextureParam(inD.Shader, inD.Kernel, "_AutomatonTo", inD.AutomatonTo);
					inCtx.cmd.SetComputeBufferParam(inD.Shader, inD.Kernel, "_Positions", inD.Positions);

					inCtx.cmd.SetComputeBufferParam(inD.Shader, inD.Kernel, "_Ellipsoids", inD.Ellipsoids);
					m_Shader.SetInt("_NumEllipsoids", m_EllipsoidsBuffer.count);

					inCtx.cmd.SetComputeFloatParam(inD.Shader, "_VoxelSize", m_Settings.VoxelSize);
					inCtx.cmd.SetComputeIntParam(inD.Shader, "_Volume", Volume);
					inCtx.cmd.SetComputeVectorParam(inD.Shader, "_VoxelGridResolution", VoxelGridResolution);
					inCtx.cmd.SetComputeVectorParam(inD.Shader, "_VoxelGridOrigin", VoxelGridOrigin);

					inCtx.cmd.SetComputeIntParam(inD.Shader, "_CloudSpeed", m_Settings.CloudSpeed);
					inCtx.cmd.SetComputeIntParam(inD.Shader, "_CloudOffset", m_CloudOffset);
					inCtx.cmd.SetComputeIntParam(inD.Shader, "_Visualization", (int)m_Settings.Visualization);
					inCtx.cmd.SetComputeIntParam(inD.Shader, "_Seed", UnityEngine.Random.Range(300, 1000));

					GraphicsHelper.Dispatch(inCtx, inD.Shader, inD.Kernel, NumVoxelsX, NumVoxelsY, NumVoxelsZ);
				});
			}

			using (IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass("Voxel Raster Pass", out PassData passData))
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                // This sets the render target of the pass to the active color texture. Change it to your own render target as needed.
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
			
				builder.SetRenderFunc((PassData inD, RasterGraphContext inCtx) =>
				{
					MaterialPropertyBlock block = new MaterialPropertyBlock();
					block.SetFloat("_VoxelSize", m_Settings.VoxelSize);
					block.SetBuffer("_Positions", m_EllipsoidsBuffer);
					inCtx.cmd.DrawMeshInstancedProcedural(m_Settings.VoxelMesh, 0, m_Settings.VoxelMaterial, -1, m_PositionsBuffer.count, block);
				});
			}
        }

		public void Cleanup()
		{
			m_TextureCurrent?.Release();
			m_TextureCurrent = null;

			m_TextureNext?.Release();
			m_TextureNext = null;

			m_PositionsBuffer?.Release();
			m_PositionsBuffer = null;

			m_EllipsoidsBuffer?.Release();
			m_EllipsoidsBuffer = null;
		}
	}
}
