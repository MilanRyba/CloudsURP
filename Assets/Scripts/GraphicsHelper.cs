using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Helpers
{
	public static class GraphicsHelper
	{
		#region Compute

		public static void DispatchOld(ComputeShader inShader,
			int inNumInvocationsX = 1, int inNumInvocationsY = 1, int inNumInvocationsZ = 1, int inKernel = 0)
		{
			Vector3Int threadGroupSizes = GetThreadGroupSizes(inShader, inKernel);
			int numGroupsX = Mathf.CeilToInt(inNumInvocationsX / (float)threadGroupSizes.x);
			int numGroupsY = Mathf.CeilToInt(inNumInvocationsY / (float)threadGroupSizes.y);
			int numGroupsZ = Mathf.CeilToInt(inNumInvocationsZ / (float)threadGroupSizes.z);
			inShader.Dispatch(inKernel, numGroupsX, numGroupsY, numGroupsZ);
		}

		// Get the size of compute thread groups as stated in the shader.
		public static Vector3Int GetThreadGroupSizes(ComputeShader inShader, int inKernel = 0)
		{
			uint x, y, z;
			inShader.GetKernelThreadGroupSizes(inKernel, out x, out y, out z);
			return new Vector3Int((int)x, (int)y, (int)z);
		}

		// Return the number of thread groups to dispatch based on the desired number of invocations.
		public static Vector3Int GetNumThreadGroups(ComputeShader inShader, int inKernel,
			int inNumInvocationsX, int inNumInvocationsY, int inNumInvocationsZ)
		{
			Vector3Int threadGroupSizes = GetThreadGroupSizes(inShader, inKernel);
			Vector3Int numGroups = new Vector3Int();
			numGroups.x = Mathf.CeilToInt(inNumInvocationsX / (float)threadGroupSizes.x);
			numGroups.y = Mathf.CeilToInt(inNumInvocationsY / (float)threadGroupSizes.y);
			numGroups.z = Mathf.CeilToInt(inNumInvocationsZ / (float)threadGroupSizes.z);
			return numGroups;
		}

		// Record a commnand in a ComputeGraphContext which dispatches a compute shader.
		public static void Dispatch(ComputeGraphContext inCtx, ComputeShader inShader, int inKernel,
			int inNumInvocationsX = 1, int inNumInvocationsY = 1, int inNumInvocationsZ = 1)
		{
			Vector3Int threadGroupSizes = GetThreadGroupSizes(inShader, inKernel);
			Vector3Int numGroups = GetNumThreadGroups(inShader, inKernel, inNumInvocationsX, inNumInvocationsY, inNumInvocationsZ);
			inCtx.cmd.DispatchCompute(inShader, inKernel, numGroups.x, numGroups.y, numGroups.z);
		}

		// Record a commnand in a ComputeGraphContext which dispatches a compute shader.
		// The number of invocations is the same in X and Y dimensions, Z is 1.
		public static void DispatchXY(ComputeGraphContext inCtx, ComputeShader inShader, int inKernel, int inNumInvocationsXY)
		{
			Dispatch(inCtx, inShader, inKernel, inNumInvocationsXY, inNumInvocationsXY, 1);
		}

		// Record a commnand in a ComputeGraphContext which dispatches a compute shader.
		// The number of invocations is the same in all dimensions
		public static void DispatchXYZ(ComputeGraphContext inCtx, ComputeShader inShader, int inKernel, int inNumInvocationsXYZ)
		{
			Dispatch(inCtx, inShader, inKernel, inNumInvocationsXYZ, inNumInvocationsXYZ, inNumInvocationsXYZ);
		}

		#endregion

		#region Buffers

		public static int GetStride<T>()
		{
			return System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));
		}

		public static void CreateStructuredBuffer<T>(ref ComputeBuffer outBuffer, int inCount)
		{
			int stride = GetStride<T>();
			bool createNewBuffer = outBuffer == null || !outBuffer.IsValid() || outBuffer.count != inCount || outBuffer.stride != stride;
			if (createNewBuffer)
			{
				Release(outBuffer);
				outBuffer = new ComputeBuffer(inCount, stride, ComputeBufferType.Structured);
			}
		}

		public static void CreateStructuredBuffer<T>(ref ComputeBuffer outBuffer, T[] inData)
		{
			CreateStructuredBuffer<T>(ref outBuffer, inData.Length);
			outBuffer.SetData(inData);
		}

		public static void Release(ComputeBuffer inBuffer)
		{
			inBuffer?.Release();
		}

		#endregion

		#region Textures

		//
		// NOTE: These functions work for my use case in CloudsRendererFeature.CloudResourcesPass
		//

		// Get 2D texture for use as a noise texture (e.g. width = height, enableRandomWrite = true)
		public static void CreateNoise2D(ref RTHandle outHandle, int inResolution, GraphicsFormat inFormat, string inName)
		{
			if (outHandle == null ||
				outHandle.rt.width != inResolution ||
				outHandle.rt.height != inResolution)
			{
				Release(outHandle);

				var desc = new RenderTextureDescriptor(inResolution, inResolution, inFormat, 0)
				{
					dimension = TextureDimension.Tex2D,
					enableRandomWrite = true,
					msaaSamples = 1,
					sRGB = false,
					useMipMap = false,
				};

				outHandle = RTHandles.Alloc(desc, name: inName);
			}
		}

		// Get 3D texture for use as a noise texture
		// (e.g. width = height = volumeDepth, enableRandomWrite = true, useMipMap)
		public static void CreateNoise3D(ref RTHandle outHandle, int inResolution, GraphicsFormat inFormat, string inName)
		{
			if (outHandle == null ||
				outHandle.rt.width != inResolution ||
				outHandle.rt.height != inResolution ||
				outHandle.rt.volumeDepth != inResolution)
			{
				Release(outHandle);

				var desc = new RenderTextureDescriptor(inResolution, inResolution, inFormat, 0)
				{
					volumeDepth = inResolution,
					dimension = TextureDimension.Tex3D,
					enableRandomWrite = true,
					msaaSamples = 1,
					sRGB = false,
					useMipMap = true,
					autoGenerateMips = false,
				};

				outHandle = RTHandles.Alloc(desc, name: inName);
			}
		}

		public static void CreateAutomaton(ref RTHandle outHandle, Vector3Int inDimensions, string inName)
		{
			if (outHandle == null ||
				outHandle.rt.width != inDimensions.x ||
				outHandle.rt.height != inDimensions.y ||
				outHandle.rt.volumeDepth != inDimensions.z)
			{
				Release(outHandle);

				var desc = new RenderTextureDescriptor(inDimensions.x, inDimensions.y, GraphicsFormat.R8_UInt, 0)
				{
					volumeDepth = inDimensions.z,
					dimension = TextureDimension.Tex3D,
					enableRandomWrite = true,
					msaaSamples = 1,
					sRGB = false,
					useMipMap = false,
					autoGenerateMips = false,
				};

				outHandle = RTHandles.Alloc(desc, name: inName);
			}
		}

		public static void Release(RTHandle inTexture)
		{
			inTexture?.Release();
		}

		#endregion
	}
}
