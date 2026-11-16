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
	/// Seamless loop transition using Quintic SmootherStep and Premultiplied Alpha
	/// crossfade for dark, translucent, and bright VFX objects.
	/// </summary>
	public class FX_Transition_Seamless
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
			public float blendFactor;

			// By default containers are assumed to be read & write
			public NativeArray<Color> RESULT_RawData;

			// The code actually running on the job
			public void Execute(int i) // i = pixel index in texture's raw data
			{
				Color A = A_RawData[i]; // Frame from Buffer A (fading IN: 0 -> 1)
				Color B = B_RawData[i]; // Frame from Buffer B (fading OUT: 1 -> 0)

				float weightA = blendFactor;
				float weightB = 1f - blendFactor;

				// Premultiplied alpha crossfade (exact energy conservation: weightA + weightB = 1.0)
				float premultA = A.a * weightA;
				float premultB = B.a * weightB;

				Color C = new Color(0f, 0f, 0f, 0f);
				C.a = Mathf.Clamp01(premultA + premultB);

				if (C.a > 0.0001f)
				{
					float invAlpha = 1f / C.a;
					C.r = (A.r * premultA + B.r * premultB) * invAlpha;
					C.g = (A.g * premultA + B.g * premultB) * invAlpha;
					C.b = (A.b * premultA + B.b * premultB) * invAlpha;
				}

				RESULT_RawData[i] = C;
			}
		}

		/// <summary>
		/// Returns a new Texture2D created from 'textureB' and 'textureA'.
		/// </summary>
		public Texture2D Process(Texture2D textureA, Texture2D textureB, int transitionFrame, int transitionFramesCount, bool debug)
		{
			var A_RawData = textureA.GetRawTextureData<Color>();
			var B_RawData = textureB.GetRawTextureData<Color>();
			var RESULT_texture = new Texture2D(textureA.width, textureA.height, textureA.format, false);
			var RESULT_RawData = RESULT_texture.GetRawTextureData<Color>();

			// Normalized progress (0 to 1)
			float t = Mathf.Clamp01((float)(transitionFrame + 1) / (float)(transitionFramesCount + 1));

			// Quintic SmootherStep (6t^5 - 15t^4 + 10t^3) for zero velocity and acceleration at endpoints
			float blendFactor = t * t * t * (t * (t * 6f - 15f) + 10f);

#if UNITY_EDITOR
			if (debug)
				Debug.Log("Transition frame = " + (transitionFrame + 1).ToString("D2") + " / " + transitionFramesCount.ToString("D2") + ", t = " + t.ToString("F2") + ", blend = " + blendFactor.ToString("F2"));
#endif
			// OnCreateNewData the job data
			var job = new FX_Job()
			{
				blendFactor = blendFactor,
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
