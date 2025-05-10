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
	public class FX_TransitionCustom
	{
		private struct FX_Job : IJobParallelFor
		{
			// Jobs declare all data that will be accessed in the job
			// By declaring it as read only, multiple jobs are allowed to access the data in parallel
			[ReadOnly]
			public NativeArray<Color> A_RawData;
			[ReadOnly]
			public NativeArray<Color> B_RawData;
			[ReadOnly]
			public Vector2 alpha;
			[ReadOnly]
			public float transitionMultiplier;

			// By default containers are assumed to be read & write
			public NativeArray<Color> RESULT_RawData;

			// The code actually running on the job
			public void Execute(int i) // i = pixel index in texture's raw data
			{
				// Color32 as Color
				Color A = A_RawData[i];
				Color B = B_RawData[i];

				// result color
				Color C = new Color();

				// keep oryginal alphas
				float Aa = A.a;
				float Ba = B.a;

				// apply alpha
				A.a = A.a * alpha.x;
				B.a = B.a * alpha.y;

				// transition
				C.a = (1f - A.a) * B.a + A.a;
				C.r = ((1f - A.a) * B.a * B.r + A.a * A.r) / C.a;
				C.g = ((1f - A.a) * B.a * B.g + A.a * A.g) / C.a;
				C.b = ((1f - A.a) * B.a * B.b + A.a * A.b) / C.a;

				// correct final alpha
				if (Aa >= Mathf.Epsilon && Ba >= Mathf.Epsilon)
				{
					float mean = (Aa * Ba);

					if (C.a < mean)
						C.a = mean;
				}

				// result
				RESULT_RawData[i] = C;
			}
		}

		/// <summary>
		/// Returns a new Texture2D created from a 'textureB' and 'textureA'.
		/// </summary>
		public Texture2D Process(Texture2D textureA, Texture2D textureB, int transitionFrame, int transitionFramesCount, float transitionMultiplier, bool debug)
		{
			var A_RawData = textureA.GetRawTextureData<Color>();
			var B_RawData = textureB.GetRawTextureData<Color>();
			var RESULT_texture = new Texture2D(textureA.width, textureA.height, textureA.format, false);
			var RESULT_RawData = RESULT_texture.GetRawTextureData<Color>();

			// custom data
			float transition = Mathf.Clamp01((float)(transitionFrame + 1) / (float)(transitionFramesCount + 1));
			Vector2 alpha = new Vector2(transition, 1f - transition)/*.normalized*/;
			// alpha boost
			alpha = new Vector2(Mathf.Clamp01(alpha.x * transitionMultiplier), Mathf.Clamp01(alpha.y * transitionMultiplier));

			if (debug)
				Debug.LogError("Transition frame = " + (transitionFrame + 1).ToString("D2") + " / " + transitionFramesCount.ToString("D2") + ", transition = " + transition.ToString("F2") + ", transitionAlphaBoost = " + transitionMultiplier.ToString("F2") + ", alpha = " + alpha.ToString("F2"));

			// Initialize the job data
			var job = new FX_Job()
			{
				transitionMultiplier = transitionMultiplier,
				alpha = alpha,
				A_RawData = A_RawData,
				B_RawData = B_RawData,
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