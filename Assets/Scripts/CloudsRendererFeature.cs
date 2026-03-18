using Helpers;
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class CloudsRendererFeature : ScriptableRendererFeature
{
	[SerializeField] ComputeShader m_CloudsShader;
	[SerializeField] ComputeShader m_CloudResourcesShader;

	[SerializeField] CloudsPassSettings m_CloudsPassSettings;
    [SerializeField] CloudResourcesPassSettings m_CloudResourcesSettings;

    CloudsPass m_CloudsPass;
	CloudResourcesPass m_CloudResourcesPass;

	public RTHandle GetNoiseShape() => m_CloudResourcesPass?.CloudNoiseShape;
	public RTHandle GetNoiseDetail() => m_CloudResourcesPass?.CloudNoiseDetail;
	public RTHandle GetCloudMap() => m_CloudResourcesPass?.CloudMap;

	// Unity calls this method on the following events:
	//   - When the Renderer Feature loads the first time.
	//   - When you enable or disable the Renderer Feature.
	//   - When you change a property in the inspector of the Renderer Feature.
	// (Create() is not called when Renderer Feature overrides the OnValidate() method which is called instead)
	public override void Create()
    {
        m_CloudsPass = new CloudsPass(m_CloudsPassSettings);
		m_CloudResourcesPass = new CloudResourcesPass(m_CloudResourcesSettings);

		m_CloudResourcesSettings.RefreshResources = true;
		Debug.Log("Create() from CloudsRendererFeature");
    }

	// Called once per frame per camera, this method injects 'ScriptableRenderPass' instances into the renderer
	public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
		if (!SystemInfo.supportsComputeShaders)
		{
			Debug.LogWarning("System doesn't support compute shaders. Skipping CloudsPass and CloudResourcesPass.");
			return;
		}

		if (m_CloudResourcesSettings.RefreshResources ||
			m_CloudResourcesPass?.CloudNoiseShape == null ||
			m_CloudResourcesPass?.CloudNoiseDetail == null ||
			m_CloudResourcesPass?.CloudMap == null)
		{

			if (m_CloudResourcesShader == null)
			{
				Debug.LogWarning("Cloud resources compute shader hasn't been assigned. Skipping CloudResourcesPass.");
			}
			else
			{
				m_CloudResourcesPass.Setup(m_CloudResourcesShader);
				renderer.EnqueuePass(m_CloudResourcesPass);

				Debug.Log("Enqueing CloudResourcesPass");
				m_CloudResourcesSettings.RefreshResources = false;
			}
		}

		if (m_CloudsShader == null)
		{
			Debug.LogWarning("Clouds compute shader hasn't been assigned. Skipping CloudsPass.");
		}
		else
		{
			m_CloudsPass.Setup(m_CloudsShader, GetNoiseShape(), GetNoiseDetail(), GetCloudMap());
			renderer.EnqueuePass(m_CloudsPass);
		}
	}

	protected override void Dispose(bool disposing)
	{
		m_CloudResourcesPass.Cleanup();
		base.Dispose(disposing);
	}

	public enum TextureChannel { All, R, G, B, A }

	[Serializable]
    public class CloudsPassSettings
    {
		[Header("General")]

		[Range(1000.0f, 1000000.0f), Tooltip("Planet's radius in meters")]
		public float PlanetRadius = 60000.0f;

		[Range(100.0f, 10000.0f)]
		public float AtmosphereBottomHeight = 1500.0f;

		[Range(100.0f, 10000.0f)]
		public float AtmosphereTopHeight = 3000.0f;

		[Range(8, 256), Tooltip("The maximum number of steps the raymarcher will take")]
		public int NumSteps = 128;

		[Range(2.0f, 4.0f)]
		public float LargeStepSizeMultiplier = 3.0f;

		[Tooltip("Offsets the starting sample position during the ray march")]
		public bool UseJitter = true;



		[Header("Shape")]

		[Range(1, 10)]
		public int CoverageRepeat = 4;

		[Range(0.0f, 360.0f), Tooltip("Angle of the global wind direction")]
		public float WindAngle = 0.0f;

		[Range(0.0f, 100.0f), Tooltip("Speed of the clouds")]
		public float CloudSpeed = 1.0f;

		[Range(0.0f, 250.0f), Tooltip("Pushes the tops of the clouds along the wind direction by this many units")]
		public float CloudTopOffset = 100.0f;

		[Range(0.0f, 1.0f)]
		public float GlobalDensity = 0.021f;


		[Header("Clouds")]

		[Range(0.1f, 5.0f), Tooltip("Scale of the base cloud shape")]
		public float ShapeNoiseScale = 0.3f;

		[Range(0.1f, 5.0f), Tooltip("Scale of the cloud details")]
		public float DetailNoiseScale = 0.3f;

		[Range(0.0f, 1.0f)]
		public float DetailNoiseInfluence = 0.4f;

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

		public TextureChannel ActiveChannel = TextureChannel.R;

		public Vector4 ChannelMask
		{
			get
			{
				return new Vector4(
					(ActiveChannel == TextureChannel.R) ? 1 : 0,
					(ActiveChannel == TextureChannel.G) ? 1 : 0,
					(ActiveChannel == TextureChannel.B) ? 1 : 0,
					(ActiveChannel == TextureChannel.A) ? 1 : 0);
			}
		}
	}

	[Serializable]
	public class CloudResourcesPassSettings
	{
		public bool RefreshResources = false;
	}

	class CloudsPass : ScriptableRenderPass
    {
		#region PassFields

		const string m_PassName = "CloudsPass";

		ComputeShader m_Shader;
		int m_Kernel;

		RTHandle m_NoiseShape;
		RTHandle m_NoiseDetail;
		RTHandle m_CloudMap;

		readonly CloudsPassSettings m_Settings;

		#endregion

		public CloudsPass(CloudsPassSettings inSettings)
        {
            m_Settings = inSettings;

			renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        }

		public void Setup(ComputeShader inShader, RTHandle inNoiseShape, RTHandle inNoiseDetail, RTHandle inCloudMap)
		{
			m_Shader = inShader;
			m_Kernel = m_Shader.FindKernel("CSMain");

			m_NoiseShape = inNoiseShape;
			m_NoiseDetail = inNoiseDetail;
			m_CloudMap = inCloudMap;

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
			public TextureHandle NoiseShape;
			public TextureHandle NoiseDetail;
			public TextureHandle CloudMap;
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
				data.NoiseShape = renderGraph.ImportTexture(m_NoiseShape);
				data.NoiseDetail = renderGraph.ImportTexture(m_NoiseDetail);
				data.CloudMap = renderGraph.ImportTexture(m_CloudMap);

				builder.UseTexture(destination, AccessFlags.Write);
				builder.UseTexture(source, AccessFlags.Read);
				builder.UseTexture(data.DepthTexture, AccessFlags.Read);
				builder.UseTexture(data.NoiseShape, AccessFlags.Read);
				builder.UseTexture(data.NoiseDetail, AccessFlags.Read);
				builder.UseTexture(data.CloudMap, AccessFlags.Read);

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

					inCtx.cmd.SetComputeFloatParam(m_Shader, "PlanetRadius", m_Settings.PlanetRadius);
					inCtx.cmd.SetComputeFloatParam(m_Shader, "AtmosphereBottomHeight", m_Settings.AtmosphereBottomHeight);
					inCtx.cmd.SetComputeFloatParam(m_Shader, "AtmosphereTopHeight", m_Settings.AtmosphereTopHeight);

					inCtx.cmd.SetComputeIntParam(m_Shader, "NumSteps", m_Settings.NumSteps);
					inCtx.cmd.SetComputeFloatParam(m_Shader, "LargeStepSizeMultiplier", m_Settings.LargeStepSizeMultiplier);
					inCtx.cmd.SetComputeIntParam(m_Shader, "UseJitter", m_Settings.UseJitter ? 1 : 0);

					inCtx.cmd.SetComputeFloatParam(m_Shader, "GlobalDensity", m_Settings.GlobalDensity);
					inCtx.cmd.SetComputeFloatParam(m_Shader, "ShapeNoiseScale", m_Settings.ShapeNoiseScale);
					inCtx.cmd.SetComputeFloatParam(m_Shader, "DetailNoiseScale", m_Settings.DetailNoiseScale);
					inCtx.cmd.SetComputeFloatParam(m_Shader, "DetailNoiseInfluence", m_Settings.DetailNoiseInfluence);
					inCtx.cmd.SetComputeIntParam(m_Shader, "CoverageRepeat", m_Settings.CoverageRepeat);

					Vector3 windDirection = new Vector3(Mathf.Cos(m_Settings.WindAngle * Mathf.Deg2Rad), 0, -Mathf.Sin(m_Settings.WindAngle * Mathf.Deg2Rad));
					inCtx.cmd.SetComputeVectorParam(m_Shader, "WindDirection", windDirection);
					inCtx.cmd.SetComputeFloatParam(m_Shader, "CloudSpeed", m_Settings.CloudSpeed);
					inCtx.cmd.SetComputeFloatParam(m_Shader, "CloudTopOffset", m_Settings.CloudTopOffset);
					
					inCtx.cmd.SetComputeFloatParam(m_Shader, "Eccentricity", m_Settings.Eccentricity);
					inCtx.cmd.SetComputeFloatParam(m_Shader, "SilverIntensity", m_Settings.SilverIntensity);
					inCtx.cmd.SetComputeFloatParam(m_Shader, "SilverSpread", m_Settings.SilverSpread);

					inCtx.cmd.SetComputeFloatParam(m_Shader, "Brightness", m_Settings.Brightness);

					inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "ShapeTexture", inD.NoiseShape);
					inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "DetailTexture", inD.NoiseDetail);
					inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "CloudMap", inD.CloudMap);
					inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "SceneTexture", inD.SceneTexture);
					inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "DepthTexture", inD.DepthTexture);
					inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "Output", inD.Output);

					inCtx.cmd.SetComputeIntParam(m_Shader, "ShowEarlyExit", m_Settings.ShowEarlyExit ? 1 : 0);
					inCtx.cmd.SetComputeIntParam(m_Shader, "ShowTextures", m_Settings.ShowTextures ? 1 : 0);
					inCtx.cmd.SetComputeFloatParam(m_Shader, "Slice", m_Settings.Slice);
					inCtx.cmd.SetComputeVectorParam(m_Shader, "ChannelMask", m_Settings.ChannelMask);

					GraphicsHelper.Dispatch(inCtx, m_Shader, m_Kernel, (int)inD.ViewportDimensions.x, (int)inD.ViewportDimensions.y);
				});
			}

			// Swap camera color buffer with the cloud texture
			resourceData.cameraColor = destination;
		}
    }

	class CloudResourcesPass : ScriptableRenderPass
	{
		#region PassFields

		const string m_PassName = "CloudResourcesPass";
		readonly CloudResourcesPassSettings m_Settings;

		ComputeShader m_Shader;
		int m_KernelNoiseShape;
		int m_KernelNoiseDetail;
		int m_KernelCloudMap;

		RTHandle m_HandleNoiseShape;
		RTHandle m_HandleNoiseDetail;
		RTHandle m_HandleCloudMap;

		// Texture resolutions
		const int m_ResNoiseShape = 128;
		const int m_ResNoiseDetail = 32;
		const int m_ResCloudMap = 512;

		#endregion

		#region Properties

		public RTHandle CloudNoiseShape => m_HandleNoiseShape;
		public RTHandle CloudNoiseDetail => m_HandleNoiseDetail;
		public RTHandle CloudMap => m_HandleCloudMap;

		#endregion

		public CloudResourcesPass(CloudResourcesPassSettings inSettings)
		{
			m_Settings = inSettings;

			renderPassEvent = RenderPassEvent.BeforeRendering;
		}

		public void Setup(ComputeShader inShader)
		{
			m_Shader = inShader;

			// Find kernels
			m_KernelNoiseShape = m_Shader.FindKernel("CloudNoiseShapeCS");
			m_KernelNoiseDetail = m_Shader.FindKernel("CloudNoiseDetailCS");
			m_KernelCloudMap = m_Shader.FindKernel("CloudMapCS");

			// Re-create textures if needed
			GraphicsFormat format = GraphicsFormat.R16G16B16A16_SFloat;
			GraphicsHelper.CreateNoise3D(ref m_HandleNoiseShape, m_ResNoiseShape, format, "_CloudShapeNoise3D");
			GraphicsHelper.CreateNoise3D(ref m_HandleNoiseDetail, m_ResNoiseDetail, format, "_CloudDetailNoise3D");
			GraphicsHelper.CreateNoise2D(ref m_HandleCloudMap, m_ResCloudMap, format, "_CloudMap");
		}

		private class PassData
		{
			public ComputeShader Shader;    // Reference to the compute shader
			public int Kernel;              // Kernel index
			public TextureHandle Output;    // Output texture
			public float ResolutionInv;     // Reciprocal of the texture resolution
		}

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
		{
			// Import noise and map textures into the render graph
			TextureHandle shapeNoiseHandle = renderGraph.ImportTexture(m_HandleNoiseShape);
			TextureHandle detailNoiseHandle = renderGraph.ImportTexture(m_HandleNoiseDetail);
			TextureHandle cloudMapHandle = renderGraph.ImportTexture(m_HandleCloudMap);

			using (IComputeRenderGraphBuilder builder = renderGraph.AddComputePass("Cloud Shape Noise Pass", out PassData data))
			{
				data.Shader = m_Shader;
				data.Kernel = m_KernelNoiseShape;
				data.Output = shapeNoiseHandle;
				data.ResolutionInv = 1.0f / m_ResNoiseShape;

				builder.UseTexture(shapeNoiseHandle, AccessFlags.Write);

				builder.SetRenderFunc((PassData inD, ComputeGraphContext inCtx) =>
				{
					inCtx.cmd.SetComputeTextureParam(inD.Shader, inD.Kernel, "OutputNoise", inD.Output);
					inCtx.cmd.SetComputeFloatParam(inD.Shader, "ResolutionInv", inD.ResolutionInv);

					GraphicsHelper.DispatchXYZ(inCtx, inD.Shader, inD.Kernel, m_ResNoiseShape);
				});
			}

			using (IComputeRenderGraphBuilder builder = renderGraph.AddComputePass("Cloud Detail Noise Pass", out PassData data))
			{
				data.Shader = m_Shader;
				data.Kernel = m_KernelNoiseDetail;
				data.Output = detailNoiseHandle;
				data.ResolutionInv = 1.0f / m_ResNoiseDetail;

				builder.UseTexture(detailNoiseHandle, AccessFlags.Write);

				builder.SetRenderFunc((PassData inD, ComputeGraphContext inCtx) =>
				{
					inCtx.cmd.SetComputeTextureParam(inD.Shader, inD.Kernel, "OutputNoise", inD.Output);
					inCtx.cmd.SetComputeFloatParam(inD.Shader, "ResolutionInv", inD.ResolutionInv);

					GraphicsHelper.DispatchXYZ(inCtx, inD.Shader, inD.Kernel, m_ResNoiseDetail);
				});
			}

			using (IComputeRenderGraphBuilder builder = renderGraph.AddComputePass("Cloud Map Pass", out PassData data))
			{
				data.Shader = m_Shader;
				data.Kernel = m_KernelCloudMap;
				data.Output = cloudMapHandle;
				data.ResolutionInv = 1.0f / m_ResCloudMap;

				builder.UseTexture(cloudMapHandle, AccessFlags.Write);

				builder.SetRenderFunc((PassData inD, ComputeGraphContext inCtx) =>
				{
					inCtx.cmd.SetComputeTextureParam(inD.Shader, inD.Kernel, "OutputMap", inD.Output);
					inCtx.cmd.SetComputeFloatParam(inD.Shader, "ResolutionInv", inD.ResolutionInv);

					GraphicsHelper.DispatchXY(inCtx, inD.Shader, inD.Kernel, m_ResCloudMap);
				});
			}

			// Generate mips for 3D noise texture
			m_HandleNoiseShape.rt.GenerateMips();
			m_HandleNoiseDetail.rt.GenerateMips();
		}

		public void Cleanup()
		{
			m_HandleNoiseShape?.Release();
			m_HandleNoiseShape = null;

			m_HandleNoiseDetail?.Release();
			m_HandleNoiseDetail = null;

			m_HandleCloudMap?.Release();
			m_HandleCloudMap = null;
		}
	}
}
