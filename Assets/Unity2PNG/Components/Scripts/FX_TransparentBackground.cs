/*
    Author: Matthew Mazan (Masterio)
    Permitted: Free to personal or commercial use.
    Forbidden: Re-Sell/Re-Share this package.
    Compatibility: Unity 2021.3.0+, URP
*/
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Unity2Png
{
	/// <summary>
	/// Runs texture's processing in Unity's Job System (multithread tasks).
	/// </summary>
	public class FX_TransparentBackground
	{
		private const float ALPHA_LIMIT = 1f / 255f;

		private struct FX_Job : IJobParallelFor
		{
			// Jobs declare all data that will be accessed in the job
			// By declaring it as read only, multiple jobs are allowed to access the data in parallel
			[ReadOnly]
			public NativeArray<Color> A_RawData;
			[ReadOnly]
			public NativeArray<Color> B_RawData;
			[ReadOnly]
			public bool alphaSmooth;
			[ReadOnly]
			public float alphaSmoothLimit;
			[ReadOnly]
			public float alphaSmoothDamp;

			// By default containers are assumed to be read & write
			public NativeArray<Color> RESULT_RawData;

			// The code actually running on the job
			public void Execute(int i) // i = pixel index in texture's raw data
			{
				// Color32 as Color
				Color A = A_RawData[i];	// black
				Color B = B_RawData[i];	// white

				// result color
				Color C = new Color();

				// retrieve opaque texture color (takes inverted min color as alpha)
				C.a = 1f - Mathf.Min(B.r - A.r, B.g - A.g, B.b - A.b);

				// set color
				if (C.a <= ALPHA_LIMIT)
				{
					C = Color.clear;
				}
				else
				{
					C.r = A.r / C.a;
					C.g = A.g / C.a;
					C.b = A.b / C.a;

					// alpha smooth
					if (alphaSmooth && C.a <= alphaSmoothLimit)
					{
						// further distance from 'alphaSmoothLimit' gives more damp less distance gives almost nothing
						C.a = Mathf.Clamp01(C.a - (alphaSmoothLimit - C.a) * alphaSmoothDamp); // lower 'alphaSmoothDamp' values gives lesser effect
					}
				}

				// result
				RESULT_RawData[i] = C;
			}
		}

		/// <summary>
		/// Returns a new Texture2D with transparent background created from a 'texture_black' and 'texture_white'<para></para>
		/// (where 'texture_black' has a black backgound and 'texture_white' has a white background).
		/// </summary>
		public Texture2D Process(Texture2D texture_black, Texture2D texture_white, bool alphaSmooth, float alphaSmoothLimit, float alphaSmoothDamp)
		{
			var A_RawData = texture_black.GetRawTextureData<Color>();
			var B_RawData = texture_white.GetRawTextureData<Color>();
			var RESULT_texture = new Texture2D(texture_black.width, texture_black.height, texture_black.format, false);
			var RESULT_RawData = RESULT_texture.GetRawTextureData<Color>();

			// Initialize the job data
			var job = new FX_Job()
			{
				A_RawData = A_RawData,
				B_RawData = B_RawData,
				alphaSmooth = alphaSmooth,
				alphaSmoothLimit = alphaSmoothLimit,
				alphaSmoothDamp = alphaSmoothDamp,
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
			B_RawData.Dispose();
			RESULT_RawData.Dispose();

			return RESULT_texture;
		}
	}
}