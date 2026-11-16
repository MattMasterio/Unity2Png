/*
Author: Matthew Mazan (Masterio)
Permitted: Free to personal or commercial use.
Forbidden: Re-Sell/Re-Share this package.
Compatibility: Unity 2021.3.0+, URP
*/
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Unity2Png
{
	/// <summary>
	/// Applies quadratic Alpha Smooth in-place using Unity's Job System.
	/// Cleans faint outer glow edges without eating into translucent objects during loop transitions.
	/// </summary>
	public class FX_AlphaSmooth
	{
		private struct FX_Job : IJobParallelFor
		{
			[ReadOnly]
			public float alphaSmoothLimit;
			[ReadOnly]
			public float alphaSmoothDamp;

			public NativeArray<Color> RawData;

			public void Execute(int i)
			{
				Color C = RawData[i];

				if (C.a > 0f && C.a < alphaSmoothLimit)
				{
					// Normalized factor: 1.0 at alpha = 0 (outer edge), 0.0 at alpha = alphaSmoothLimit
					float edgeFactor = 1f - (C.a / alphaSmoothLimit);

					// Quadratic falloff: strongly damps faint outer glow noise while preserving translucent objects
					float dampFactor = edgeFactor * edgeFactor * alphaSmoothDamp;

					C.a = Mathf.Clamp01(C.a * (1f - dampFactor));

					if (C.a <= 0.0001f)
						C = Color.clear;

					RawData[i] = C;
				}
			}
		}

		public void Process(Texture2D texture, float alphaSmoothLimit, float alphaSmoothDamp)
		{
			var rawData = texture.GetRawTextureData<Color>();

			var job = new FX_Job()
			{
				alphaSmoothLimit = alphaSmoothLimit,
				alphaSmoothDamp = alphaSmoothDamp,
				RawData = rawData
			};

			JobHandle jobHandle = job.Schedule(rawData.Length, 64);
			jobHandle.Complete();

			rawData.Dispose();
		}
	}
}