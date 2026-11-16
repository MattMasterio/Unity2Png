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
	/// Runs texture's processing in Unity's Job System (multithread tasks).
	/// Clears RGB channels (sets to pure black transparent) for fully transparent pixels
	/// to prevent alpha bleeding and colored fringes when bilinear filtering is applied.
	/// </summary>
	public class FX_ClearAlpha
	{
		private struct FX_Job : IJobParallelFor
		{
			// By default containers are assumed to be read & write
			public NativeArray<Color> RawData;

			// The code actually running on the job
			public void Execute(int i) // i = pixel index in texture's raw data
			{
				Color C = RawData[i];

				if (C.a <= 0.001f)
				{
					RawData[i] = new Color(0f, 0f, 0f, 0f);
				}
			}
		}

		/// <summary>
		/// Cleans RGB values of transparent pixels directly on 'texture'.
		/// </summary>
		public void Process(Texture2D texture, bool debug)
		{
			if (texture == null)
				return;

			var RawData = texture.GetRawTextureData<Color>();

			// Create the job data
			var job = new FX_Job()
			{
				RawData = RawData
			};

			// Schedule a parallel-for job. First parameter is how many for-each iterations to perform.
			// The second parameter is the batch size,
			// essentially the no-overhead innerloop that just invokes Execute(i) in a loop.
			// When there is a lot of work in each iteration then a value of 1 can be sensible.
			// When there is very little work values of 32 or 64 can make sense.
			JobHandle jobHandle = job.Schedule(RawData.Length, 64);

			// Ensure the job has completed.
			jobHandle.Complete();

			// Native arrays must be disposed manually.
			RawData.Dispose();
		}
	}
}