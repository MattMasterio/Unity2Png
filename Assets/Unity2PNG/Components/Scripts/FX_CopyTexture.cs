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
	/// </summary>
	public class FX_CopyTexture
	{
		private struct FX_Job : IJobParallelFor
		{
			// Jobs declare all data that will be accessed in the job
			// By declaring it as read only, multiple jobs are allowed to access the data in parallel
			[ReadOnly]
			public NativeArray<Color> A_RawData;

			// By default containers are assumed to be read & write
			public NativeArray<Color> RESULT_RawData;

			// The code actually running on the job
			public void Execute(int i) // i = pixel index in texture's raw data
			{
				RESULT_RawData[i] = A_RawData[i];
			}
		}

		/// <summary>
		/// Returns a new Texture2D copied from the 'texture'.
		/// </summary>
		public Texture2D Process(Texture2D texture)
		{
			var A_RawData = texture.GetRawTextureData<Color>();
			var RESULT_texture = new Texture2D(texture.width, texture.height, texture.format, false);
			var RESULT_RawData = RESULT_texture.GetRawTextureData<Color>();

			// Initialize the job data
			var job = new FX_Job()
			{
				A_RawData = A_RawData,
				RESULT_RawData = RESULT_RawData
			};

			// Schedule a parallel-for job. First parameter is how many for-each iterations to perform.
			// The second parameter is the batch size,
			// essentially the no-overhead innerloop that just invokes Execute(i) in a loop.
			// When there is a lot of work in each iteration then a value of 1 can be sensible.
			// When there is very little work values of 32 or 64 can make sense.
			JobHandle jobHandle = job.Schedule(RESULT_RawData.Length, 64);

			// Ensure the job has completed.
			// It is not recommended to Complete a job immediately,
			// since that reduces the chance of having other jobs run in parallel with this one.
			// You optimally want to schedule a job early in a frame and then wait for it later in the frame.
			jobHandle.Complete();

			// Job complete

			// Native arrays must be disposed manually.
			A_RawData.Dispose();
			RESULT_RawData.Dispose();

			return RESULT_texture;
		}
	}
}