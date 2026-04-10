using Helpers;
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using static VoxelSpace;

public class VoxelCloudsRendererFeature : ScriptableRendererFeature
{
    [SerializeField] ComputeShader m_VoxelCloudsShader;
    [SerializeField] ComputeShader m_SimulationShader;
	[SerializeField] VoxelSpace m_VoxelSpace;

	[SerializeField] VoxelCloudsPassSettings m_VoxelCloudsPassSettings;
	[SerializeField] SimulationPassSettings m_SimulationPassSettings;

	VoxelCloudsPass m_VoxelCloudsPass;
    SimulationPass m_SimulationPass;

	public RTHandle GetSmoothDensity() => m_SimulationPass?.SmoothDensity;

	// Unity calls this method on the following events:
	//   - When the Renderer Feature loads the first time.
	//   - When you enable or disable the Renderer Feature.
	//   - When you change a property in the inspector of the Renderer Feature.
	// (Create() is not called when Renderer Feature overrides the OnValidate() method which is called instead)
	public override void Create()
    {
		m_VoxelCloudsPass = new VoxelCloudsPass(m_VoxelSpace, m_VoxelCloudsPassSettings);
        m_SimulationPass = new SimulationPass(m_SimulationShader, m_VoxelSpace, m_SimulationPassSettings);

		Debug.Log("Created VoxelCloudsRendererFeature.");
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
		if (m_SimulationShader == null)
		{
			Debug.LogWarning("Simulation compute shader hasn't been assigned. Skipping SimulationPass.");
		}
		else
		{
			m_SimulationPass.Setup();
			renderer.EnqueuePass(m_SimulationPass);
		}

		if (m_VoxelCloudsShader == null)
		{
			Debug.LogWarning("Voxel clouds compute shader hasn't been assigned. Skipping VoxelCloudsPass.");
		}
		else
		{
			m_VoxelCloudsPass.Setup(m_VoxelCloudsShader, GetSmoothDensity());
			renderer.EnqueuePass(m_VoxelCloudsPass);
		}
	}

	protected override void Dispose(bool disposing)
	{
		m_SimulationPass.Cleanup();
		base.Dispose(disposing);
	}

	[Serializable]
	public class VoxelCloudsPassSettings
	{
		[Range(8, 256), Tooltip("The maximum number of steps the raymarcher will take")]
		public int NumSteps = 128;

		[Range(2.0f, 4.0f)]
		public float LargeStepSizeMultiplier = 3.0f;

		[Tooltip("Offsets the starting sample position during the ray march")]
		public bool UseJitter = true;

		[Range(0.0f, 1.0f)]
		public float GlobalDensity = 0.021f;

		[Range(0.5f, 2.5f)]
		public float MetaballRadius = 1.25f;


		[Header("Phase")]

		// TODO: What is this tooltip??
		[Range(-0.99f, 0.99f), Tooltip("Directional scattering bias. Values >1 make the light scatter forward and values <1 backward")]
		public float Eccentricity = 0.65f;

		[Range(0.0f, 4.0f)]
		public float SilverIntensity = 0.95f;

		[Range(0.0f, 2.0f)]
		public float SilverSpread = 1.0f;

		[Space(2.0f)]

		[Range(0.0f, 2.0f)]
		public float Brightness = 1.0f;


		[Header("Debug")]

		[Tooltip("Enabling this setting will show pixels that stopped the ray march early due to low transmittance")]
		public bool ShowEarlyExit = false;

		public bool ShowTextures = false;

		[Range(0.0f, 1.0f)]
		public float Slice = 0.0f;
	}

	[Serializable]
    public class SimulationPassSettings
    {
		[Tooltip("Number of units the clouds will move after each simulation step on the XZ plane")]
		public Vector2Int CloudDirection = Vector2Int.zero;

		[Range(0.0f, 2.0f), Tooltip("Number of second between each simulation step.")]
		public float TimeBetweenUpdates = 0.5f;
	}

	class VoxelCloudsPass : ScriptableRenderPass
	{
		#region PassFields

		const string m_PassName = "VoxelCloudsPass";

		ComputeShader m_Shader;
		int m_Kernel;

		RTHandle m_SmoothDensity;

		readonly VoxelSpace m_VoxelSpace;
		readonly VoxelCloudsPassSettings m_Settings;

		#endregion

		public VoxelCloudsPass(VoxelSpace inVoxelSpace, VoxelCloudsPassSettings inSettings)
		{
			m_VoxelSpace = inVoxelSpace;
			m_Settings = inSettings;
			renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
		}

		public void Setup(ComputeShader inShader, RTHandle inSmoothDensity)
		{
			m_Shader = inShader;
			m_Kernel = m_Shader.FindKernel("CSMain");

			m_SmoothDensity = inSmoothDensity;

			requiresIntermediateTexture = true;
		}

		private class PassData
		{
			public Vector2 ViewportDimensions;
			public Vector2 ViewportDimensionsInv;
			public Vector3 CameraPosition;
			public Matrix4x4 ProjInv;
			public Matrix4x4 ViewInv;

			public Vector3 SunDirection;
			public Color SunColor;

			// Textures
			public TextureHandle SmoothDensity;
			public TextureHandle SceneTexture;
			public TextureHandle DepthTexture;
			public TextureHandle Output;
		}

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
		{
			// Make use of frameData to access resources and camera data through the dedicated containers.
			UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
			UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
			Light sun = RenderSettings.sun;

			if (resourceData.isActiveTargetBackBuffer)
			{
				Debug.LogWarning($"Skipping render pass. CloudsPass requires an intermediate ColorTexture, we can't use the " +
					$"BackBuffer as a texture input.");
				return;
			}

			TextureHandle source = resourceData.activeColorTexture;

			TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
			destinationDesc.name = $"CameraColor-{m_PassName}";
			destinationDesc.clearBuffer = true;
			destinationDesc.enableRandomWrite = true;
			TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

			using (IComputeRenderGraphBuilder builder = renderGraph.AddComputePass(m_PassName, out PassData data))
			{
				data.ViewportDimensions = new Vector2(destinationDesc.width, destinationDesc.height);
				data.ViewportDimensionsInv = new Vector2(1.0f / destinationDesc.width, 1.0f / destinationDesc.height);
				data.CameraPosition = cameraData.worldSpaceCameraPos;
				data.ProjInv = cameraData.GetProjectionMatrix().inverse;
				data.ViewInv = cameraData.GetViewMatrix().inverse;

				data.SunDirection = sun.transform.forward;
				data.SunColor = sun.color;

				data.Output = destination;
				data.SceneTexture = source;
				data.DepthTexture = resourceData.activeDepthTexture;
				data.SmoothDensity = renderGraph.ImportTexture(m_SmoothDensity);

				builder.UseTexture(destination, AccessFlags.Write);
				builder.UseTexture(source, AccessFlags.Read);
				builder.UseTexture(data.DepthTexture, AccessFlags.Read);
				builder.UseTexture(data.SmoothDensity, AccessFlags.Read);

				builder.SetRenderFunc((PassData inD, ComputeGraphContext inCtx) =>
				{
					inCtx.cmd.SetComputeVectorParam(m_Shader, "ViewportDimensions", inD.ViewportDimensions);
					inCtx.cmd.SetComputeVectorParam(m_Shader, "ViewportDimensionsInv", inD.ViewportDimensionsInv);
					inCtx.cmd.SetComputeVectorParam(m_Shader, "CameraPosition", inD.CameraPosition);
					inCtx.cmd.SetComputeMatrixParam(m_Shader, "ProjInv", inD.ProjInv);
					inCtx.cmd.SetComputeMatrixParam(m_Shader, "ViewInv", inD.ViewInv);
					inCtx.cmd.SetComputeVectorParam(m_Shader, "SunDirection", inD.SunDirection);
					inCtx.cmd.SetComputeVectorParam(m_Shader, "SunColor", inD.SunColor);
					inCtx.cmd.SetComputeFloatParam(m_Shader, "Time", Time.time);

					inCtx.cmd.SetComputeIntParam(m_Shader, "NumSteps", m_Settings.NumSteps);
					inCtx.cmd.SetComputeFloatParam(m_Shader, "LargeStepSizeMultiplier", m_Settings.LargeStepSizeMultiplier);
					inCtx.cmd.SetComputeIntParam(m_Shader, "UseJitter", m_Settings.UseJitter ? 1 : 0);
					
					inCtx.cmd.SetComputeFloatParam(m_Shader, "GlobalDensity", m_Settings.GlobalDensity);
					
					inCtx.cmd.SetComputeFloatParam(m_Shader, "Eccentricity", m_Settings.Eccentricity);
					inCtx.cmd.SetComputeFloatParam(m_Shader, "SilverIntensity", m_Settings.SilverIntensity);
					inCtx.cmd.SetComputeFloatParam(m_Shader, "SilverSpread", m_Settings.SilverSpread);
					
					inCtx.cmd.SetComputeFloatParam(m_Shader, "Brightness", m_Settings.Brightness);

					inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "SmoothDensity", inD.SmoothDensity);
					inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "SceneTexture", inD.SceneTexture);
					inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "DepthTexture", inD.DepthTexture);
					inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "Output", inD.Output);

					inCtx.cmd.SetComputeIntParam(m_Shader, "ShowEarlyExit", m_Settings.ShowEarlyExit ? 1 : 0);
					inCtx.cmd.SetComputeIntParam(m_Shader, "ShowTextures", m_Settings.ShowTextures ? 1 : 0);
					inCtx.cmd.SetComputeFloatParam(m_Shader, "Slice", m_Settings.Slice);

					inCtx.cmd.SetComputeFloatParam(m_Shader, "_MetaballRadius", m_Settings.MetaballRadius * m_VoxelSpace.VoxelSize);

					inCtx.cmd.SetComputeFloatParam(m_Shader, "_VoxelSize", m_VoxelSpace.VoxelSize);
					inCtx.cmd.SetComputeVectorParam(m_Shader, "_VoxelGridResolution", m_VoxelSpace.VoxelGridResolution);
					inCtx.cmd.SetComputeVectorParam(m_Shader, "_VoxelGridOrigin", m_VoxelSpace.VoxelGridOrigin);

					GraphicsHelper.Dispatch(inCtx, m_Shader, m_Kernel, (int)inD.ViewportDimensions.x, (int)inD.ViewportDimensions.y);
				});
			}

			// Swap camera color buffer with the cloud texture
			resourceData.cameraColor = destination;
		}
	}

	class SimulationPass : ScriptableRenderPass
    {
        #region PassFields

        const string m_PassName = "SimulationPass";

        ComputeShader m_Shader;
        int m_KernelSimulation;
        int m_KernelSmoothDensity;

		RTHandle m_TextureCurrent;
		RTHandle m_TextureNext;

		RTHandle m_SmoothDensity;

		GraphicsBuffer m_EllipsoidsBuffer;

		readonly VoxelSpace m_VoxelSpace;
		readonly SimulationPassSettings m_Settings;

		Vector3Int m_ByteSpace;

		float m_TimeSinceLastUpdate = 0.0f;
		Vector2Int m_CloudOffset = Vector2Int.zero;

		#endregion

		public RTHandle SmoothDensity => m_SmoothDensity;

		public SimulationPass(ComputeShader inShader, VoxelSpace inVoxelSpace, SimulationPassSettings inSettings)
        {
			m_VoxelSpace = inVoxelSpace;
            m_Settings = inSettings;
            renderPassEvent = RenderPassEvent.BeforeRendering;

			m_Shader = inShader;

			// Find kernels
			m_KernelSimulation = m_Shader.FindKernel("SimulationCS");
			m_KernelSmoothDensity = m_Shader.FindKernel("SmoothDensityCS");

			m_ByteSpace = m_VoxelSpace.VoxelGridResolutionInt;
			m_ByteSpace.y /= 8;

			// Create automatons and textures
			GraphicsHelper.CreateAutomaton(ref m_TextureCurrent, m_ByteSpace, "_Automaton1");
			GraphicsHelper.CreateAutomaton(ref m_TextureNext, m_ByteSpace, "_Automaton2");

			var desc = new RenderTextureDescriptor(m_VoxelSpace.NumVoxelsX, m_VoxelSpace.NumVoxelsY, GraphicsFormat.R16_UNorm, 0);
			desc.volumeDepth = m_VoxelSpace.NumVoxelsZ;
			desc.useMipMap = false;
			GraphicsHelper.CreateWriteable3D(ref m_SmoothDensity, desc, "_SmoothDensity");
		}

		public void Setup()
		{
			if (m_EllipsoidsBuffer == null || m_EllipsoidsBuffer.count != m_VoxelSpace.NumEllipsoids)
			{
				m_EllipsoidsBuffer?.Release();
				m_EllipsoidsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_VoxelSpace.NumEllipsoids, GraphicsHelper.GetStride<Ellipsoid>());
			}
			m_EllipsoidsBuffer.SetData(m_VoxelSpace.Ellipsoids);
		}

		private class PassData
        {
			public ComputeShader Shader;         // Reference to the compute shader
			public int Kernel;                   // Kernel index
			public TextureHandle AutomatonFrom;  // Input texture
			public TextureHandle AutomatonTo;    // Output texture
			public BufferHandle EllipsoidsBuffer;
			public TextureHandle SmoothDensity;
		}

		private void SwapAutomatons()
		{
			RTHandle rtTtemp = m_TextureCurrent;
			m_TextureCurrent = m_TextureNext;
			m_TextureNext = rtTtemp;
		}

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
			TextureHandle current = graph.ImportTexture(m_TextureCurrent);
			TextureHandle next = graph.ImportTexture(m_TextureNext);
			BufferHandle ellipsoidsBuffer = graph.ImportBuffer(m_EllipsoidsBuffer);
			TextureHandle smoothDensity = graph.ImportTexture(m_SmoothDensity);

			m_TimeSinceLastUpdate += Time.deltaTime;
			if (m_TimeSinceLastUpdate >= m_Settings.TimeBetweenUpdates)
			{
				// Reset timer
				m_TimeSinceLastUpdate = 0.0f;

				m_CloudOffset += m_Settings.CloudDirection;

				// Run the simulation step
				using (IComputeRenderGraphBuilder builder = graph.AddComputePass("Simulation Pass", out PassData data))
				{
					data.Shader = m_Shader;
					data.Kernel = m_KernelSimulation;
					data.AutomatonFrom = current;
					data.AutomatonTo = next;
					data.EllipsoidsBuffer = ellipsoidsBuffer;
				
					builder.UseTexture(data.AutomatonFrom, AccessFlags.Read);
					builder.UseTexture(data.AutomatonTo, AccessFlags.Write);
					builder.UseBuffer(data.EllipsoidsBuffer, AccessFlags.Read);
				
					builder.SetRenderFunc((PassData inD, ComputeGraphContext inCtx) =>
					{
						inCtx.cmd.SetComputeTextureParam(inD.Shader, inD.Kernel, "_AutomatonFrom", inD.AutomatonFrom);
						inCtx.cmd.SetComputeTextureParam(inD.Shader, inD.Kernel, "_AutomatonTo", inD.AutomatonTo);
				
						inCtx.cmd.SetComputeBufferParam(inD.Shader, inD.Kernel, "_Ellipsoids", inD.EllipsoidsBuffer);
						m_Shader.SetInt("_NumEllipsoids", m_EllipsoidsBuffer.count);
				
						inCtx.cmd.SetComputeFloatParam(inD.Shader, "_VoxelSize", m_VoxelSpace.VoxelSize);
						inCtx.cmd.SetComputeVectorParam(inD.Shader, "_VoxelGridResolution", m_VoxelSpace.VoxelGridResolution);
						inCtx.cmd.SetComputeVectorParam(inD.Shader, "_VoxelGridOrigin", m_VoxelSpace.VoxelGridOrigin);
				
						inCtx.cmd.SetComputeIntParam(inD.Shader, "_Volume", m_VoxelSpace.Volume);

						Vector2 cloudDirection = new Vector2(m_Settings.CloudDirection.x, m_Settings.CloudDirection.y);
						inCtx.cmd.SetComputeVectorParam(inD.Shader, "_CloudDirection", cloudDirection);
						Vector2 cloudOffset  = new Vector2(m_CloudOffset.x, m_CloudOffset.y);
						inCtx.cmd.SetComputeVectorParam(inD.Shader, "_CloudOffset", cloudOffset);

						inCtx.cmd.SetComputeIntParam(inD.Shader, "_Seed", UnityEngine.Random.Range(300, 1000));
				
						GraphicsHelper.Dispatch(inCtx, inD.Shader, inD.Kernel, m_ByteSpace);
					});
				}
				SwapAutomatons();

				// Smooth out the discrete density
				using (IComputeRenderGraphBuilder builder = graph.AddComputePass("Smooth Density Pass", out PassData data))
				{
					data.Shader = m_Shader;
					data.Kernel = m_KernelSmoothDensity;
					data.AutomatonFrom = next;
					data.AutomatonTo = current;
					data.SmoothDensity = smoothDensity;
				
					builder.UseTexture(data.AutomatonFrom, AccessFlags.Read);
					builder.UseTexture(data.AutomatonTo, AccessFlags.Read);
					builder.UseTexture(data.SmoothDensity, AccessFlags.Write);
				
					builder.SetRenderFunc((PassData inD, ComputeGraphContext inCtx) =>
					{
						inCtx.cmd.SetComputeTextureParam(inD.Shader, inD.Kernel, "_AutomatonFrom", inD.AutomatonFrom);
						inCtx.cmd.SetComputeTextureParam(inD.Shader, inD.Kernel, "_AutomatonTo", inD.AutomatonTo);
						inCtx.cmd.SetComputeTextureParam(inD.Shader, inD.Kernel, "_SmoothDensity", inD.SmoothDensity);
				
						inCtx.cmd.SetComputeFloatParam(inD.Shader, "_VoxelSize", m_VoxelSpace.VoxelSize);
						inCtx.cmd.SetComputeVectorParam(inD.Shader, "_VoxelGridResolution", m_VoxelSpace.VoxelGridResolution);
						inCtx.cmd.SetComputeVectorParam(inD.Shader, "_VoxelGridOrigin", m_VoxelSpace.VoxelGridOrigin);
				
						GraphicsHelper.Dispatch(inCtx, inD.Shader, inD.Kernel, m_VoxelSpace.VoxelGridResolutionInt);
					});
				}
			}
        }

		public void Cleanup()
		{
			m_TextureCurrent?.Release();
			m_TextureCurrent = null;

			m_TextureNext?.Release();
			m_TextureNext = null;

			m_EllipsoidsBuffer?.Release();
			m_EllipsoidsBuffer = null;
		}
	}
}
