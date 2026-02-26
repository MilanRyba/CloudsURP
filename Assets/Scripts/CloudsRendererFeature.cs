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

		public Color BackgroundColor = new Color(0.572f, 0.772f, 0.921f);
		public bool UseBackgroundColor = false;

		public float Absorption = 0.1f;
		public float Scattering = 0.1f;
		public float Density = 1.0f;

		[Range(-1.0f, 1.0f)]
		public float Eccentricity = 0.1f;

		public bool UseJitter = true;

		public Vector3 SphereCenter = Vector3.zero;
		public float SphereRadius = 1.0f;

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
		[Header("Worley Noise Parameters")]

		[Range(1, 16)]
		public int WorleyFrequency = 4;

		[Header("Perlin Noise Parameters")]

		[Range(1, 16)]
		public int PerlinFrequency = 5;

		[Range(2, 4)]
		public int PerlinLacunarity = 2;

		[Range(0.0f, 1.0f)]
		public float PerlinPersistence = 0.5f;

		[Range(1, 16)]
		public int PerlinOctaves = 5;

		[Header("Alligator Noise Parameters")]

		[Min(1)]
		public int AlligatorSeed = 1;
	}

	[SerializeField] ComputeShader m_CloudsShader;
	[SerializeField] ComputeShader m_NoiseShader;
	[SerializeField] CloudsRenderPassSettings m_CloudsSettings;
	[SerializeField] NoiseRenderPassSettings m_NoiseSettings;
	CloudsPass m_CloudsPass;
	NoisePass m_NoisePass;
	bool m_RegenerateNoise = true;

	public RTHandle GetCloudNoiseTexture() => m_NoisePass?.Noise;

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

		if (m_RegenerateNoise || m_NoisePass.Noise == null)
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
			m_CloudsPass.Setup(m_CloudsShader, GetCloudNoiseTexture());
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
		RTHandle m_CloudsNoise;
		readonly CloudsRenderPassSettings m_Settings;

		public CloudsPass(CloudsRenderPassSettings settings)
		{
			m_Settings = settings;
		}

		public void Setup(ComputeShader inShader, RTHandle inCloudsNoise)
		{
			m_Shader = inShader;
			m_Kernel = m_Shader.FindKernel("CSMain");
			m_CloudsNoise = inCloudsNoise;

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
			public TextureHandle CloudsNoise;

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

			inCtx.cmd.SetComputeVectorParam(m_Shader, "BackgroundColor", m_Settings.BackgroundColor);
			inCtx.cmd.SetComputeIntParam(m_Shader, "UseBackgroundColor", m_Settings.UseBackgroundColor ? 1 : 0);

			inCtx.cmd.SetComputeFloatParam(m_Shader, "Absorption", m_Settings.Absorption);
			inCtx.cmd.SetComputeFloatParam(m_Shader, "Scattering", m_Settings.Scattering);
			inCtx.cmd.SetComputeFloatParam(m_Shader, "Density", m_Settings.Density);
			inCtx.cmd.SetComputeFloatParam(m_Shader, "Eccentricity", m_Settings.Eccentricity);
			inCtx.cmd.SetComputeIntParam(m_Shader, "UseJitter", m_Settings.UseJitter ? 1 : 0);

			inCtx.cmd.SetComputeVectorParam(m_Shader, "SphereCenter", m_Settings.SphereCenter);
			inCtx.cmd.SetComputeFloatParam(m_Shader, "SphereRadius", m_Settings.SphereRadius);

			inCtx.cmd.SetComputeVectorParam(m_Shader, "SunDirection", inData.SunDirection);
			inCtx.cmd.SetComputeVectorParam(m_Shader, "SunColor", inData.SunColor);

			inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "Result", inData.Output);
			inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "SceneTexture", inData.SceneTexture);
			inCtx.cmd.SetComputeTextureParam(m_Shader, m_Kernel, "CloudsNoise", inData.CloudsNoise);

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
			data.CloudsNoise = renderGraph.ImportTexture(m_CloudsNoise);

			builder.UseTexture(destination, AccessFlags.Write);
			builder.UseTexture(source, AccessFlags.Read);
			builder.UseTexture(data.CloudsNoise, AccessFlags.Read);
			builder.SetRenderFunc((PassData inData, ComputeGraphContext inContext) => ExecutePass(inData, inContext));

			// Swap camera color buffer with the cloud texture
			resourceData.cameraColor = destination;
		}
	}

	class NoisePass : ScriptableRenderPass
	{
		#region PassFields

		ComputeShader m_Shader;
		int m_Kernel;

		RTHandle m_NoiseHandle;

		int m_Resolution = 128;

		public RTHandle Noise => m_NoiseHandle;

		NoiseRenderPassSettings m_Settings;

		#endregion

		public NoisePass(NoiseRenderPassSettings inSettings)
		{
			m_Settings = inSettings;
			renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
		}

		public void Setup(ComputeShader inShader)
		{
			m_Shader = inShader;
			m_Kernel = m_Shader.FindKernel("CSMain");

			if (m_NoiseHandle == null ||
				m_NoiseHandle.rt.width != m_Resolution ||
				m_NoiseHandle.rt.height != m_Resolution ||
				m_NoiseHandle.rt.volumeDepth != m_Resolution)
			{
				m_NoiseHandle?.Release();

				var desc = new RenderTextureDescriptor(m_Resolution, m_Resolution, GraphicsFormat.R16G16B16A16_SFloat, 0)
				{
					volumeDepth = m_Resolution,
					dimension = TextureDimension.Tex3D,
					enableRandomWrite = true,
					msaaSamples = 1,
					sRGB = false,
					useMipMap = true,
					autoGenerateMips = false,
				};

				m_NoiseHandle = RTHandles.Alloc(desc, name: "_CloudNoiseRT");
			}
		}

		class PassData
		{
			public ComputeShader Shader;    // Reference to the compute shader
			public int Kernel;              // Kernel index
			public TextureHandle Output;    // Output noise texture
			public float ResolutionInv;     // Reciprocal of the texture resolution
		}

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
		{
			TextureHandle textureHandle = renderGraph.ImportTexture(m_NoiseHandle);

			using IComputeRenderGraphBuilder builder = renderGraph.AddComputePass("Cloud Noise Pass", out PassData data);
			data.Shader = m_Shader;
			data.Kernel = m_Kernel;
			data.Output = textureHandle;
			data.ResolutionInv = 1.0f / m_Resolution;

			builder.UseTexture(textureHandle, AccessFlags.Write);

			builder.SetRenderFunc((PassData inD, ComputeGraphContext inCtx) =>
			{
				inCtx.cmd.SetComputeTextureParam(inD.Shader, inD.Kernel, "Output", inD.Output);
				inCtx.cmd.SetComputeFloatParam(inD.Shader, "ResolutionInv", inD.ResolutionInv);

				GraphicsHelper.DispatchXYZ(inCtx, inD.Shader, inD.Kernel, m_Resolution);
			});

			m_NoiseHandle.rt.GenerateMips();
		}

		public void Cleanup()
		{
			m_NoiseHandle?.Release();
			m_NoiseHandle = null;
		}
	}
}
