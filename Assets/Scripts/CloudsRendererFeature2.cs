using Helpers;
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class CloudsRendererFeature2 : ScriptableRendererFeature
{
	public enum Metric
	{
		Euclidean = 0,
		Manhattan = 1,
		Chebyshev = 2
	}

	public enum TextureChannel { All, R, G, B, A }

	[Serializable]
	public class CloudsRenderPassSettings
	{
		[Header("Weather")]

		[Range(0.0f, 1.0f)]
		public float Coverage = 0.9f;

		[Range(0.0f, 1.0f), Tooltip("Type of the cloud to render. 0 -> stratus, 0.5 -> stratocumulus, 1 -> cumulus")]
		public float CloudType = 0.5f;

		[Range(0.0f, 360.0f), Tooltip("Angle of the global wind direction")]
		public float WindAngle = 0.0f;

		[Range(0.01f, 10.0f), Tooltip("Speed of the clouds")]
		public float CloudSpeed = 1.0f;

		[Range(0.0f, 250.0f), Tooltip("Pushes the tops of the clouds along the wind direction by this many units")]
		public float CloudTopOffset = 100.0f;

		[Range(0.0f, 1.0f)]
		public float GlobalDensity = 0.021f;

		[Range(0.0001f, 0.001f)]
		public float GlobalScale = 0.001f;


		[Header("Clouds")]

		[Range(0.1f, 5.0f), Tooltip("Scale of the base cloud shape")]
		public float ShapeNoiseScale = 0.3f;

		[Range(0.1f, 5.0f), Tooltip("Scale of the cloud details")]
		public float DetailNoiseScale = 0.3f;


		[Header("Phase")]

		[Range(-0.99f, 0.99f), Tooltip("Directional scattering bias. Values >1 make the light scatter forward and values <1 backward")]
		public float Eccentricity = 0.65f;

		[Range(0.0f, 5.0f)]
		public float Intensity = 0.95f;

		[Range(0.0f, 1.0f)]
		public float Spread = 1.0f;


		[Header("Rendering")]

		[Range(8, 256), Tooltip("The maximum number of steps the raymarcher will take")]
		public int NumSteps = 128;

		[Range(2.0f, 4.0f)]
		public float LargeStepSizeMultiplier = 3.0f;

		[Tooltip("Offsets the starting sample position during the ray march")]
		public bool UseJitter = true;

		[Range(1000.0f, 1000000.0f)]
		public float PlanetRadius = 60000.0f; // Earth's radius in meters

		public Vector2 AtmosphereHeightRange = new Vector2(200.0f, 900.0f);

		[Header("Debug")]

		[Tooltip("Show pixels that ended the ray marching loop early due to low transmittance")]
		public bool EarlyTerminatedPixels = false;

		public bool ShowTextureSlices = false;

		[Range(0.0f, 1.0f)]
		public float TextureSlice = 0.0f;

		[Header("Noise Parameters")]

		public TextureChannel ActiveChannel = TextureChannel.R;

		// [Range(0.0f, 7.0f)] // The max is 7, because that is the max mip level for 128 texture
		// public float Mip = 0.0f;

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
	class NoiseRenderPassSettings
	{
		[Header("Cloud Map Parameters")]

		[Range(0.0f, 1.0f)]
		public float Coverage = 1.0f;

		[Range(-4.0f, 2.0f)]
		public float NewMin = 0.0f;

		[Range(-2.0f, 4.0f)]
		public float NewMax = 1.0f;

		// [Header("Worley Noise Parameters")]
		// 
		// [Range(1, 16)]
		// public int WorleyFrequency = 4;
		// 
		// [Header("Perlin Noise Parameters")]
		// 
		// [Range(1, 16)]
		// public int PerlinFrequency = 5;
		// 
		// [Range(2, 4)]
		// public int PerlinLacunarity = 2;
		// 
		// [Range(0.0f, 1.0f)]
		// public float PerlinPersistence = 0.5f;
		// 
		// [Range(1, 16)]
		// public int PerlinOctaves = 5;
		// 
		// [Header("Alligator Noise Parameters")]
		// 
		// [Min(1)]
		// public int AlligatorSeed = 1;
	}

	[SerializeField] ComputeShader m_CloudsShader;
	[SerializeField] ComputeShader m_NoiseShader;
	[SerializeField] CloudsRenderPassSettings m_CloudsSettings;
	[SerializeField] NoiseRenderPassSettings m_NoiseSettings;
	CloudsPass m_CloudsPass;
	NoisePass m_NoisePass;
	bool m_RegenerateNoise = true;

	public RTHandle GetCloudShapeNoise() => m_NoisePass?.CloudShapeNoise;
	public RTHandle GetCloudDetailNoise() => m_NoisePass?.CloudDetailNoise;
	public RTHandle GetCloudMapTexture() => m_NoisePass?.CloudMap;

	// Called when the renderer feature is created by Unity
	public override void Create()
	{
		m_CloudsPass = new CloudsPass(m_CloudsSettings);
		m_NoisePass = new NoisePass(m_NoiseSettings);
	}

	// Called once per frame per camera, this method injects 'ScriptableRenderPass' instances into the renderer
	public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
	{
		if (!SystemInfo.supportsComputeShaders)
		{
			Debug.LogWarning("System doesn't support compute shaders. Skipping CloudsPass and NoisePass.");
			return;
		}

		if (m_RegenerateNoise || 
			m_NoisePass?.CloudShapeNoise == null || 
			m_NoisePass?.CloudDetailNoise == null || 
			m_NoisePass?.CloudMap == null)
		{
			m_RegenerateNoise = false;

			if (m_NoiseShader == null)
			{
				Debug.LogWarning("Noise compute shader hasn't been assigned. Skipping NoisePass.");
			}
			else
			{
				m_NoisePass.Setup(m_NoiseShader);
				renderer.EnqueuePass(m_NoisePass);
			}
		}

		if (m_CloudsShader == null)
		{
			Debug.LogWarning("Clouds compute shader hasn't been assigned. Skipping CloudsPass.");
		}
		else
		{
			m_CloudsPass.Setup(m_CloudsShader, GetCloudShapeNoise(), GetCloudDetailNoise(), GetCloudMapTexture());
			renderer.EnqueuePass(m_CloudsPass);
		}
	}

	protected override void Dispose(bool disposing)
	{
		m_NoisePass.Cleanup();
		base.Dispose(disposing);
	}

	private void OnValidate()
	{
		m_RegenerateNoise = true;
	}

	class CloudsPass : ScriptableRenderPass
	{
		const string m_PassName = "CloudsPass";

		ComputeShader m_Shader;
		int m_Kernel;
		RTHandle m_CloudShapeNoise;
		RTHandle m_CloudDetailNoise;
		RTHandle m_CloudMap;
		readonly CloudsRenderPassSettings m_Settings;

		public CloudsPass(CloudsRenderPassSettings settings)
		{
			m_Settings = settings;
		}

		public void Setup(ComputeShader inShader, RTHandle inCloudShapeNoise, RTHandle inCloudDetailNoise, RTHandle inCloudMap)
		{
			m_Shader = inShader;
			m_Kernel = m_Shader.FindKernel("CSMain");
			m_CloudShapeNoise = inCloudShapeNoise;
			m_CloudDetailNoise = inCloudDetailNoise;
			m_CloudMap = inCloudMap;

			requiresIntermediateTexture = true;
			renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
		}

		// This class stores the data needed by the RenderGraph pass.
		// It is passed as a parameter to the delegate function that executes the RenderGraph pass.
		private class PassData
		{
			// Textures
			public TextureHandle Output;
			public TextureHandle SceneTexture;
			public TextureHandle DepthTexture;
			public TextureHandle CloudShapeNoise;
			public TextureHandle CloudDetailNoise;
			public TextureHandle CloudMap;

			// Data
			public Vector2 ViewportDimensions;
			public Vector2 ViewportDimensionsInv;
			public Vector3 CameraPosition;
			public Matrix4x4 ProjInv;
			public Matrix4x4 ViewInv;

			public Vector3 SunDirection;
			public Color SunColor;
		}

		// This static method is passed as the RenderFunc delegate to the RenderGraph render pass.
		// It is used to execute draw commands.
		void ExecutePass(PassData inData, ComputeGraphContext inCtx)
		{
			inCtx.cmd.SetComputeVectorParam(m_Shader, "ViewportDimensions", inData.ViewportDimensions);
			inCtx.cmd.SetComputeVectorParam(m_Shader, "ViewportDimensionsInv", inData.ViewportDimensionsInv);
			inCtx.cmd.SetComputeVectorParam(m_Shader, "CameraPosition", inData.CameraPosition);
			inCtx.cmd.SetComputeMatrixParam(m_Shader, "ProjInv", inData.ProjInv);
			inCtx.cmd.SetComputeMatrixParam(m_Shader, "ViewInv", inData.ViewInv);

			inCtx.cmd.SetComputeFloatParam(m_Shader, "PlanetRadius", m_Settings.PlanetRadius);
			inCtx.cmd.SetComputeVectorParam(m_Shader, "AtmosphereHeightRange", m_Settings.AtmosphereHeightRange);

			inCtx.cmd.SetComputeVectorParam(m_Shader, "SunDirection", inData.SunDirection);
			inCtx.cmd.SetComputeVectorParam(m_Shader, "SunColor", inData.SunColor);

			inCtx.cmd.SetComputeIntParam(m_Shader, "NumSteps", m_Settings.NumSteps);
			inCtx.cmd.SetComputeFloatParam(m_Shader, "LargeStepSizeMultiplier", m_Settings.LargeStepSizeMultiplier);
			inCtx.cmd.SetComputeIntParam(m_Shader, "UseJitter", m_Settings.UseJitter ? 1 : 0);

			inCtx.cmd.SetComputeFloatParam(m_Shader, "GlobalDensity", m_Settings.GlobalDensity);
			inCtx.cmd.SetComputeFloatParam(m_Shader, "GlobalScale", m_Settings.GlobalScale);
			inCtx.cmd.SetComputeFloatParam(m_Shader, "ShapeNoiseScale", m_Settings.ShapeNoiseScale);
			inCtx.cmd.SetComputeFloatParam(m_Shader, "DetailNoiseScale", m_Settings.DetailNoiseScale);
			inCtx.cmd.SetComputeFloatParam(m_Shader, "Coverage", m_Settings.Coverage);
			inCtx.cmd.SetComputeFloatParam(m_Shader, "CloudType", m_Settings.CloudType);

			inCtx.cmd.SetComputeFloatParam(m_Shader, "Eccentricity", m_Settings.Eccentricity);
			inCtx.cmd.SetComputeFloatParam(m_Shader, "Intensity", m_Settings.Intensity);
			inCtx.cmd.SetComputeFloatParam(m_Shader, "Spread", m_Settings.Spread);

			inCtx.cmd.SetComputeFloatParam(m_Shader, "Time", Time.time);

			inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "Output", inData.Output);
			inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "SceneTexture", inData.SceneTexture);
			inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "DepthTexture", inData.DepthTexture);
			inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "ShapeTexture", inData.CloudShapeNoise);
			inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "DetailTexture", inData.CloudDetailNoise);
			// inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "CloudMap", inData.CloudMap);

			inCtx.cmd.SetComputeIntParam(m_Shader, "Debug", m_Settings.ShowTextureSlices ? 1 : 0);
			inCtx.cmd.SetComputeIntParam(m_Shader, "EarlyTerminatedPixels", m_Settings.EarlyTerminatedPixels ? 1 : 0);
			inCtx.cmd.SetComputeVectorParam(m_Shader, "ChannelMask", m_Settings.ChannelMask);
			inCtx.cmd.SetComputeFloatParam(m_Shader, "TextureSlice", m_Settings.TextureSlice);

			GraphicsHelper.Dispatch(inCtx, m_Shader, m_Kernel, (int)inData.ViewportDimensions.x, (int)inData.ViewportDimensions.y);
		}

		// RecordRenderGraph is where the RenderGraph handle can be accessed, through which render passes can be added to the graph.
		// FrameData is a context container through which URP resources can be accessed and managed.
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
			destinationDesc.clearBuffer = false;
			destinationDesc.enableRandomWrite = true;
			TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

			using IComputeRenderGraphBuilder builder = renderGraph.AddComputePass("CloudsPass", out PassData data);
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
			data.CloudShapeNoise = renderGraph.ImportTexture(m_CloudShapeNoise);
			data.CloudDetailNoise = renderGraph.ImportTexture(m_CloudDetailNoise);
			data.CloudMap = renderGraph.ImportTexture(m_CloudMap);

			builder.UseTexture(destination, AccessFlags.Write);
			builder.UseTexture(source, AccessFlags.Read);
			builder.UseTexture(data.DepthTexture, AccessFlags.Read);
			builder.UseTexture(data.CloudShapeNoise, AccessFlags.Read);
			builder.UseTexture(data.CloudDetailNoise, AccessFlags.Read);
			builder.UseTexture(data.CloudMap, AccessFlags.Read);
			builder.SetRenderFunc((PassData inData, ComputeGraphContext inContext) => ExecutePass(inData, inContext));

			// Swap camera color buffer with the cloud texture
			resourceData.cameraColor = destination;
		}
	}

	class NoisePass : ScriptableRenderPass
	{
		#region PassFields

		NoiseRenderPassSettings m_Settings;
		ComputeShader m_Shader;

		int m_KernelShapeNoise;
		int m_KernelDetailNoise;
		int m_KernelMap;

		RTHandle m_HandleShapeNoise;
		RTHandle m_HandleDetailNoise;
		RTHandle m_HandleMap;

		int m_ResolutionShapeNoise = 128;
		int m_ResolutionDetailNoise = 32;
		int m_ResolutionMap = 512;

		public RTHandle CloudShapeNoise => m_HandleShapeNoise;
		public RTHandle CloudDetailNoise => m_HandleDetailNoise;
		public RTHandle CloudMap => m_HandleMap;

		#endregion

		public NoisePass(NoiseRenderPassSettings inSettings)
		{
			m_Settings = inSettings;

			// If we need to create textures, do it before doing anything else
			renderPassEvent = RenderPassEvent.BeforeRendering;
		}

		public void Setup(ComputeShader inShader)
		{
			m_Shader = inShader;

			m_KernelShapeNoise = m_Shader.FindKernel("CloudShapeNoiseCS");
			m_KernelShapeNoise = m_Shader.FindKernel("CloudDetailNoiseCS");
			m_KernelMap = m_Shader.FindKernel("CloudMapCS");

			CreateShapeNoiseTexture();
			CreateDetailNoiseTexture();
			CreateMapTexture();
		}

		private void CreateShapeNoiseTexture()
		{
			if (m_HandleShapeNoise == null ||
				m_HandleShapeNoise.rt.width != m_ResolutionShapeNoise ||
				m_HandleShapeNoise.rt.height != m_ResolutionShapeNoise ||
				m_HandleShapeNoise.rt.volumeDepth != m_ResolutionShapeNoise)
			{
				m_HandleShapeNoise?.Release();

				var desc = new RenderTextureDescriptor(m_ResolutionShapeNoise, m_ResolutionShapeNoise, GraphicsFormat.R16G16B16A16_SFloat, 0)
				{
					volumeDepth = m_ResolutionShapeNoise,
					dimension = TextureDimension.Tex3D,
					enableRandomWrite = true,
					msaaSamples = 1,
					sRGB = false,
					useMipMap = true,
					autoGenerateMips = false,
				};

				m_HandleShapeNoise = RTHandles.Alloc(desc, name: "_CloudShapeNoise3D");
			}
		}

		private void CreateDetailNoiseTexture()
		{
			if (m_HandleDetailNoise == null ||
				m_HandleDetailNoise.rt.width != m_ResolutionDetailNoise ||
				m_HandleDetailNoise.rt.height != m_ResolutionDetailNoise ||
				m_HandleDetailNoise.rt.volumeDepth != m_ResolutionDetailNoise)
			{
				m_HandleDetailNoise?.Release();

				var desc = new RenderTextureDescriptor(m_ResolutionDetailNoise, m_ResolutionDetailNoise, GraphicsFormat.R16G16B16A16_SFloat, 0)
				{
					volumeDepth = m_ResolutionDetailNoise,
					dimension = TextureDimension.Tex3D,
					enableRandomWrite = true,
					msaaSamples = 1,
					sRGB = false,
					useMipMap = true,
					autoGenerateMips = false,
				};

				m_HandleDetailNoise = RTHandles.Alloc(desc, name: "_CloudDetailNoise3D");
			}
		}

		private void CreateMapTexture()
		{
			if (m_HandleMap == null ||
				m_HandleMap.rt.width != m_ResolutionMap ||
				m_HandleMap.rt.height != m_ResolutionMap)
			{
				m_HandleMap?.Release();

				var desc = new RenderTextureDescriptor(m_ResolutionMap, m_ResolutionMap, GraphicsFormat.R16G16B16A16_SFloat, 0)
				{
					dimension = TextureDimension.Tex2D,
					enableRandomWrite = true,
					msaaSamples = 1,
					sRGB = false,
					useMipMap = false,
				};

				m_HandleMap = RTHandles.Alloc(desc, name: "_CloudMap");
			}
		}

		class PassData
		{
			public ComputeShader Shader;    // Reference to the compute shader
			public int Kernel;              // Kernel index
			public TextureHandle Output;    // Output noise texture
			public float ResolutionInv;     // Reciprocal of the texture resolution
			public float Coverage;
			public float NewMin;
			public float NewMax;
		}

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
		{
			// Import noise and map textures into the render graph
			TextureHandle shapeNoiseHandle = renderGraph.ImportTexture(m_HandleShapeNoise);
			TextureHandle detailNoiseHandle = renderGraph.ImportTexture(m_HandleDetailNoise);
			TextureHandle mapHandle = renderGraph.ImportTexture(m_HandleMap);

			// Add compute passes
			RecordShapeNoisePass(renderGraph, shapeNoiseHandle);
			RecordDetailNoisePass(renderGraph, detailNoiseHandle);
			RecordMapPass(renderGraph, mapHandle);

			// Generate mips for 3D noise texture
			m_HandleShapeNoise.rt.GenerateMips();
			m_HandleDetailNoise.rt.GenerateMips();
		}

		private void RecordShapeNoisePass(RenderGraph inRenderGraph, TextureHandle inNoiseHandle)
		{
			using (IComputeRenderGraphBuilder builder = inRenderGraph.AddComputePass("Cloud Shape Noise Pass", out PassData data))
			{
				data.Shader = m_Shader;
				data.Kernel = m_KernelShapeNoise;
				data.Output = inNoiseHandle;
				data.ResolutionInv = 1.0f / m_ResolutionShapeNoise;

				builder.UseTexture(inNoiseHandle, AccessFlags.Write);

				builder.SetRenderFunc((PassData inD, ComputeGraphContext inCtx) =>
				{
					inCtx.cmd.SetComputeTextureParam(inD.Shader, inD.Kernel, "OutputNoise", inD.Output);
					inCtx.cmd.SetComputeFloatParam(inD.Shader, "ResolutionInv", inD.ResolutionInv);

					GraphicsHelper.DispatchXYZ(inCtx, inD.Shader, inD.Kernel, m_ResolutionShapeNoise);
				});
			}
		}

		private void RecordDetailNoisePass(RenderGraph inRenderGraph, TextureHandle inNoiseHandle)
		{
			using (IComputeRenderGraphBuilder builder = inRenderGraph.AddComputePass("Cloud Detail Noise Pass", out PassData data))
			{
				data.Shader = m_Shader;
				data.Kernel = m_KernelDetailNoise;
				data.Output = inNoiseHandle;
				data.ResolutionInv = 1.0f / m_ResolutionDetailNoise;

				builder.UseTexture(inNoiseHandle, AccessFlags.Write);

				builder.SetRenderFunc((PassData inD, ComputeGraphContext inCtx) =>
				{
					inCtx.cmd.SetComputeTextureParam(inD.Shader, inD.Kernel, "OutputNoise", inD.Output);
					inCtx.cmd.SetComputeFloatParam(inD.Shader, "ResolutionInv", inD.ResolutionInv);

					GraphicsHelper.DispatchXYZ(inCtx, inD.Shader, inD.Kernel, m_ResolutionDetailNoise);
				});
			}
		}

		private void RecordMapPass(RenderGraph inRenderGraph, TextureHandle inMapHandle)
		{
			using (IComputeRenderGraphBuilder builder = inRenderGraph.AddComputePass("Cloud Map Pass", out PassData data))
			{
				data.Shader = m_Shader;
				data.Kernel = m_KernelMap;
				data.Output = inMapHandle;
				data.ResolutionInv = 1.0f / m_ResolutionMap;

				data.Coverage = m_Settings.Coverage;
				data.NewMin = m_Settings.NewMin;
				data.NewMax = m_Settings.NewMax;

				builder.UseTexture(inMapHandle, AccessFlags.Write);

				builder.SetRenderFunc((PassData inD, ComputeGraphContext inCtx) =>
				{
					inCtx.cmd.SetComputeTextureParam(inD.Shader, inD.Kernel, "OutputMap", inD.Output);
					inCtx.cmd.SetComputeFloatParam(inD.Shader, "ResolutionInv", inD.ResolutionInv);

					inCtx.cmd.SetComputeFloatParam(inD.Shader, "Coverage", inD.Coverage);
					inCtx.cmd.SetComputeFloatParam(inD.Shader, "NewMin", inD.NewMin);
					inCtx.cmd.SetComputeFloatParam(inD.Shader, "NewMax", inD.NewMax);

					GraphicsHelper.DispatchXY(inCtx, inD.Shader, inD.Kernel, m_ResolutionMap);
				});
			}
		}

		public void Cleanup()
		{
			m_HandleShapeNoise?.Release();
			m_HandleShapeNoise = null;

			m_HandleMap?.Release();
			m_HandleMap = null;
		}
	}
}
