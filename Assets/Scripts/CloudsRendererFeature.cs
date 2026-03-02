using Helpers;
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class CloudsRendererFeature : ScriptableRendererFeature
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
		[Range(0, 128), Tooltip("Number of samles the ray marcher will take.")]
		public int SampleCount = 32;

		public float Absorption = 0.1f;
		public float Scattering = 0.1f;

		[Range(0.0f, 1.0f)]
		public float DensityScale = 1.0f;

		[Range(-1.0f, 1.0f)]
		public float Eccentricity = 0.1f;

		public bool UseJitter = true;

		[Range(0.0f, 1.0f)]
		public float CloudType = 0.5f;

		[Range (0.1f, 100.0f)]
		public float CoverageRepeat = 6.0f;

		[Range(0.0f, 100.0f)]
		public float NoiseScale = 0.1f;

		[Header("Noise Parameters")]

		public TextureChannel ActiveChannel = TextureChannel.R;

		[Range(0.0f, 1.0f)]
		public float Slice = 0.0f;

		[Range(0.0f, 7.0f)] // The max is 7, because that is the max mip level for 128 texture
		public float Mip = 0.0f;

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

		public bool DebugTextures = false;
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

	public RTHandle GetCloudNoiseTexture() => m_NoisePass?.CloudNoise;
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
			m_NoisePass?.CloudNoise == null || 
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
			m_CloudsPass.Setup(m_CloudsShader, GetCloudNoiseTexture(), GetCloudMapTexture());
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
		RTHandle m_CloudNoise;
		RTHandle m_CloudMap;
		readonly CloudsRenderPassSettings m_Settings;

		public CloudsPass(CloudsRenderPassSettings settings)
		{
			m_Settings = settings;
		}

		public void Setup(ComputeShader inShader, RTHandle inCloudNoise, RTHandle inCloudMap)
		{
			m_Shader = inShader;
			m_Kernel = m_Shader.FindKernel("CSMain");
			m_CloudNoise = inCloudNoise;
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
			public TextureHandle CloudNoise;
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
			inCtx.cmd.SetComputeIntParam(m_Shader, "SampleCount", m_Settings.SampleCount);
			inCtx.cmd.SetComputeFloatParam(m_Shader, "Time", Time.time);

			inCtx.cmd.SetComputeFloatParam(m_Shader, "Absorption", m_Settings.Absorption);
			inCtx.cmd.SetComputeFloatParam(m_Shader, "Scattering", m_Settings.Scattering);
			inCtx.cmd.SetComputeFloatParam(m_Shader, "DensityScale", m_Settings.DensityScale);
			inCtx.cmd.SetComputeFloatParam(m_Shader, "Eccentricity", m_Settings.Eccentricity);
			inCtx.cmd.SetComputeIntParam(m_Shader, "UseJitter", m_Settings.UseJitter ? 1 : 0);
			inCtx.cmd.SetComputeFloatParam(m_Shader, "CloudType", m_Settings.CloudType);
			inCtx.cmd.SetComputeFloatParam(m_Shader, "CoverageRepeat", m_Settings.CoverageRepeat);
			inCtx.cmd.SetComputeFloatParam(m_Shader, "NoiseScale", m_Settings.NoiseScale);

			inCtx.cmd.SetComputeVectorParam(m_Shader, "SunDirection", inData.SunDirection);
			inCtx.cmd.SetComputeVectorParam(m_Shader, "SunColor", inData.SunColor);

			inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "Result", inData.Output);
			inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "SceneTexture", inData.SceneTexture);
			inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "CloudNoise", inData.CloudNoise);
			inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "CloudMap", inData.CloudMap);

			inCtx.cmd.SetComputeVectorParam(m_Shader, "ActiveChannel", m_Settings.ChannelMask);
			inCtx.cmd.SetComputeFloatParam(m_Shader, "Slice", m_Settings.Slice);
			inCtx.cmd.SetComputeFloatParam(m_Shader, "Mip", m_Settings.Mip);
			inCtx.cmd.SetComputeIntParam(m_Shader, "DebugTextures", m_Settings.DebugTextures ? 1 : 0);

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
			// TOOD: Test that this depth texture is correct
			// data.DepthTexture = resourceData.activeDepthTexture;
			data.CloudNoise = renderGraph.ImportTexture(m_CloudNoise);
			data.CloudMap = renderGraph.ImportTexture(m_CloudMap);

			builder.UseTexture(destination, AccessFlags.Write);
			builder.UseTexture(source, AccessFlags.Read);
			builder.UseTexture(data.CloudNoise, AccessFlags.Read);
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

		int m_KernelNoise;
		int m_KernelMap;

		RTHandle m_HandleNoise;
		RTHandle m_HandleMap;

		int m_ResolutionNoise = 128;
		int m_ResolutionMap = 512;

		public RTHandle CloudNoise => m_HandleNoise;
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

			m_KernelNoise = m_Shader.FindKernel("CloudNoiseCS");
			m_KernelMap = m_Shader.FindKernel("CloudMapCS");

			CreateNoiseTexture();
			CreateMapTexture();
		}

		private void CreateNoiseTexture()
		{
			if (m_HandleNoise == null ||
				m_HandleNoise.rt.width != m_ResolutionNoise ||
				m_HandleNoise.rt.height != m_ResolutionNoise ||
				m_HandleNoise.rt.volumeDepth != m_ResolutionNoise)
			{
				m_HandleNoise?.Release();

				var desc = new RenderTextureDescriptor(m_ResolutionNoise, m_ResolutionNoise, GraphicsFormat.R16G16B16A16_SFloat, 0)
				{
					volumeDepth = m_ResolutionNoise,
					dimension = TextureDimension.Tex3D,
					enableRandomWrite = true,
					msaaSamples = 1,
					sRGB = false,
					useMipMap = true,
					autoGenerateMips = false,
				};

				m_HandleNoise = RTHandles.Alloc(desc, name: "_CloudNoise3D");
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
			TextureHandle noiseHandle = renderGraph.ImportTexture(m_HandleNoise);
			TextureHandle mapHandle = renderGraph.ImportTexture(m_HandleMap);

			// Add compute passes
			RecordNoisePass(renderGraph, noiseHandle);
			RecordMapPass(renderGraph, mapHandle);

			// Generate mips for 3D noise texture
			m_HandleNoise.rt.GenerateMips();
		}

		private void RecordNoisePass(RenderGraph inRenderGraph, TextureHandle inNoiseHandle)
		{
			using (IComputeRenderGraphBuilder builder = inRenderGraph.AddComputePass("Cloud Noise Pass", out PassData data))
			{
				data.Shader = m_Shader;
				data.Kernel = m_KernelNoise;
				data.Output = inNoiseHandle;
				data.ResolutionInv = 1.0f / m_ResolutionNoise;

				builder.UseTexture(inNoiseHandle, AccessFlags.Write);

				builder.SetRenderFunc((PassData inD, ComputeGraphContext inCtx) =>
				{
					inCtx.cmd.SetComputeTextureParam(inD.Shader, inD.Kernel, "OutputNoise", inD.Output);
					inCtx.cmd.SetComputeFloatParam(inD.Shader, "ResolutionInv", inD.ResolutionInv);

					GraphicsHelper.DispatchXYZ(inCtx, inD.Shader, inD.Kernel, m_ResolutionNoise);
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
			m_HandleNoise?.Release();
			m_HandleNoise = null;

			m_HandleMap?.Release();
			m_HandleMap = null;
		}
	}
}
