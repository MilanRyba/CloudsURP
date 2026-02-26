using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;

namespace Helpers
{
	public static class GraphicsHelper
	{
		#region #Compute

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

		#region #Buffers

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
	}
}
