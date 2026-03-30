using Helpers;
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class VoxelCloudsRendererFeature : ScriptableRendererFeature
{
    [SerializeField] ComputeShader m_VoxelCloudsShader;
    [SerializeField] ComputeShader m_SimulationShader;

	[SerializeField] CommonVoxelCloudsSettings m_CommonSettings;
	[SerializeField] VoxelCloudsPassSettings m_VoxelCloudsPassSettings;
	[SerializeField] SimulationPassSettings m_SimulationPassSettings;

	VoxelCloudsPass m_VoxelCloudsPass;
    SimulationPass m_SimulationPass;

	[Serializable]
	public struct Ellipsoid
	{
		public Vector4 Position;
		public Vector4 Scale;
	}

	public RTHandle GetCloudAutomaton() => m_SimulationPass?.CloudAutomaton;

	// Unity calls this method on the following events:
	//   - When the Renderer Feature loads the first time.
	//   - When you enable or disable the Renderer Feature.
	//   - When you change a property in the inspector of the Renderer Feature.
	// (Create() is not called when Renderer Feature overrides the OnValidate() method which is called instead)
	public override void Create()
    {
		m_VoxelCloudsPass = new VoxelCloudsPass(m_CommonSettings, m_VoxelCloudsPassSettings);
        m_SimulationPass = new SimulationPass(m_CommonSettings, m_SimulationPassSettings);

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
			m_SimulationPass.Setup(m_SimulationShader);
			renderer.EnqueuePass(m_SimulationPass);
		}

		if (m_VoxelCloudsShader == null)
		{
			Debug.LogWarning("Voxel clouds compute shader hasn't been assigned. Skipping VoxelCloudsPass.");
		}
		else
		{
			m_VoxelCloudsPass.Setup(m_VoxelCloudsShader, GetCloudAutomaton());
			renderer.EnqueuePass(m_VoxelCloudsPass);
		}
	}

	protected override void Dispose(bool disposing)
	{
		m_SimulationPass.Cleanup();
		base.Dispose(disposing);
	}

	[Serializable]
	public class CommonVoxelCloudsSettings
	{
		[Tooltip("World space size of the voxel space in meters.")]
		public Vector3Int WorldExtents = new Vector3Int(20, 10, 20);

		[Tooltip("Use this to offset the voxel space. (0, 0, 0) centers the grid around origin.")]
		public Vector3 WorldOffset = Vector3.zero;

		[Min(0.2f), Tooltip("Voxel are cubes with side lengths of VoxelSize meters.")]
		public float VoxelSize = 1.0f;

		[Serializable]
		public enum SimulationVis { Humidity, Clouds, Activation, All }

		[Tooltip("Toggle which simulation variable to preview.")]
		public SimulationVis Visualization = SimulationVis.Clouds;

		public int NumVoxelsX => (int)(WorldExtents.x / VoxelSize);
		public int NumVoxelsY => (int)(WorldExtents.y / VoxelSize);
		public int NumVoxelsZ => (int)(WorldExtents.z / VoxelSize);
		public int Volume => NumVoxelsX * NumVoxelsY * NumVoxelsZ;

		public Vector3 VoxelGridResolution => new Vector3(NumVoxelsX, NumVoxelsY, NumVoxelsZ);
		public Vector3Int VoxelGridResolutionInt => new Vector3Int(NumVoxelsX, NumVoxelsY, NumVoxelsZ);
		public Vector3 VoxelGridOrigin => -(WorldExtents / 2) + WorldOffset;

		public void SetCommonParams(ComputeGraphContext inCtx, ComputeShader inShader)
		{
			inCtx.cmd.SetComputeIntParam(inShader, "_Visualization", (int)Visualization);

			inCtx.cmd.SetComputeFloatParam(inShader, "_VoxelSize", VoxelSize);
			inCtx.cmd.SetComputeVectorParam(inShader, "_VoxelGridResolution", VoxelGridResolution);
			inCtx.cmd.SetComputeVectorParam(inShader, "_VoxelGridOrigin", VoxelGridOrigin);
		}
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
		[Min(0), Tooltip("How many units will each voxel move after a simulation step.")]
		public int CloudSpeed = 1;

		[Tooltip("Container for ellipsoids. These are used to define higher probability for cloud presence.")]
		public Ellipsoid[] Ellipsoids;

		[Range(0.0f, 2.0f), Tooltip("Number of second between each simulation step.")]
		public float TimeBetweenUpdates = 0.5f;
	}

	class VoxelCloudsPass : ScriptableRenderPass
	{
		#region PassFields

		const string m_PassName = "VoxelCloudsPass";

		ComputeShader m_Shader;
		int m_Kernel;

		RTHandle m_CloudAutomaton;

		readonly CommonVoxelCloudsSettings m_Common;
		readonly VoxelCloudsPassSettings m_Settings;

		#endregion

		#region Properties

		#endregion

		public VoxelCloudsPass(CommonVoxelCloudsSettings inCommon, VoxelCloudsPassSettings inSettings)
		{
			m_Common = inCommon;
			m_Settings = inSettings;
			renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
		}

		public void Setup(ComputeShader inShader, RTHandle inCloudAutomaton)
		{
			m_Shader = inShader;
			m_Kernel = m_Shader.FindKernel("CSMain");

			m_CloudAutomaton = inCloudAutomaton;

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
			public TextureHandle CloudAutomaton;
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
				data.CloudAutomaton = renderGraph.ImportTexture(m_CloudAutomaton);

				builder.UseTexture(destination, AccessFlags.Write);
				builder.UseTexture(source, AccessFlags.Read);
				builder.UseTexture(data.DepthTexture, AccessFlags.Read);
				builder.UseTexture(data.CloudAutomaton, AccessFlags.Read);

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

					inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "CloudAutomaton", inD.CloudAutomaton);
					inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "SceneTexture", inD.SceneTexture);
					inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "DepthTexture", inD.DepthTexture);
					inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "Output", inD.Output);

					inCtx.cmd.SetComputeIntParam(m_Shader, "ShowEarlyExit", m_Settings.ShowEarlyExit ? 1 : 0);
					inCtx.cmd.SetComputeIntParam(m_Shader, "ShowTextures", m_Settings.ShowTextures ? 1 : 0);
					inCtx.cmd.SetComputeFloatParam(m_Shader, "Slice", m_Settings.Slice);

					m_Common.SetCommonParams(inCtx, m_Shader);

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
        int m_KernelReset;

		RTHandle m_TextureCurrent;
		RTHandle m_TextureNext;

		GraphicsBuffer m_EllipsoidsBuffer;

		readonly CommonVoxelCloudsSettings m_Common;
		readonly SimulationPassSettings m_Settings;

		float m_TimeSinceLastUpdate = 0.0f;
		int m_CloudOffset = 0;

		#endregion

		public RTHandle CloudAutomaton => m_TextureCurrent;

		public SimulationPass(CommonVoxelCloudsSettings inCommon, SimulationPassSettings inSettings)
        {
			m_Common = inCommon;
            m_Settings = inSettings;
            renderPassEvent = RenderPassEvent.BeforeRendering;
        }

		public void Setup(ComputeShader inShader)
		{
			m_Shader = inShader;

            // Find kernels
			m_KernelSimulation = m_Shader.FindKernel("SimulationCS");
            m_KernelReset = m_Shader.FindKernel("ResetCS");

			// Re-create automatons if needed
			GraphicsHelper.CreateAutomaton(ref m_TextureCurrent, m_Common.VoxelGridResolutionInt, "_Automaton1");
			GraphicsHelper.CreateAutomaton(ref m_TextureNext, m_Common.VoxelGridResolutionInt, "_Automaton2");

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
			public ComputeShader Shader;         // Reference to the compute shader
			public int Kernel;                   // Kernel index
			public TextureHandle AutomatonFrom;  // Input texture
			public TextureHandle AutomatonTo;    // Output texture
			public BufferHandle Ellipsoids;
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
					data.Ellipsoids = ellipsoidsBuffer;

					builder.UseTexture(data.AutomatonFrom, AccessFlags.Read);
					builder.UseTexture(data.AutomatonTo, AccessFlags.Write);
					builder.UseBuffer(data.Ellipsoids, AccessFlags.Read);

					builder.SetRenderFunc((PassData inD, ComputeGraphContext inCtx) =>
					{
						inCtx.cmd.SetComputeTextureParam(inD.Shader, inD.Kernel, "_AutomatonFrom", inD.AutomatonFrom);
						inCtx.cmd.SetComputeTextureParam(inD.Shader, inD.Kernel, "_AutomatonTo", inD.AutomatonTo);

						inCtx.cmd.SetComputeBufferParam(inD.Shader, inD.Kernel, "_Ellipsoids", inD.Ellipsoids);
						m_Shader.SetInt("_NumEllipsoids", m_EllipsoidsBuffer.count);

						m_Common.SetCommonParams(inCtx, inD.Shader);

						inCtx.cmd.SetComputeIntParam(inD.Shader, "_Volume", m_Common.Volume);
						inCtx.cmd.SetComputeIntParam(inD.Shader, "_CloudSpeed", m_Settings.CloudSpeed);
						inCtx.cmd.SetComputeIntParam(inD.Shader, "_CloudOffset", m_CloudOffset);
						inCtx.cmd.SetComputeIntParam(inD.Shader, "_Seed", UnityEngine.Random.Range(300, 1000));

						GraphicsHelper.Dispatch(inCtx, inD.Shader, inD.Kernel, m_Common.VoxelGridResolutionInt);
					});
				}
				SwapAutomatons();
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
