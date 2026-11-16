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
	public class FX_TransparentBackground_RGBTest
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
			public NativeArray<Color> tR_RawData;
			[ReadOnly]
			public NativeArray<Color> tG_RawData;
			[ReadOnly]
			public NativeArray<Color> tB_RawData;

			// By default containers are assumed to be read & write
			public NativeArray<Color> RESULT_RawData;

			// The code actually running on the job
			public void Execute(int i) // i = pixel index in texture's raw data
			{
				/* dobrze dziala pod hdr prawie perfect :) ale zostawia alpha obwodki przy np pink / blue flame
				Color A = A_RawData[i];     // black (with post-process)
				Color B = B_RawData[i];     // white (with post-process)
				Color tR = tR_RawData[i];   // red   (no post-process)
				Color tG = tG_RawData[i];   // green (no post-process)
				Color tB = tB_RawData[i];   // blue  (no post-process)

				Color C = new Color();

				// 1. Exact object alpha from orthogonal R, G, B backgrounds (no post-process).
				// Preserves opacity for dark/grey pixels that would otherwise look semi-transparent on white.
				float transR = Mathf.Clamp01(tR.r - 0.5f * (tR.g + tR.b));
				float transG = Mathf.Clamp01(tG.g - 0.5f * (tG.r + tG.b));
				float transB = Mathf.Clamp01(tB.b - 0.5f * (tB.r + tB.g));
				float rgbAlpha = Mathf.Clamp01(1f - ((transR + transG + transB) / 3f));

				// 2. Exact post-process / HDR bloom alpha derived from black vs white background captures.
				// Captures the full soft gradient of bloom, glow, and tone-mapped post-effects.
				float postAlpha = Mathf.Clamp01(1f - Mathf.Min(B.r - A.r, Mathf.Min(B.g - A.g, B.b - A.b)));

				// 3. Combine both without attenuation seams: object keeps full rgbAlpha, outer glow keeps soft postAlpha
				C.a = Mathf.Max(rgbAlpha, postAlpha);

				if (C.a <= ALPHA_LIMIT)
				{
					C = Color.clear;
				}
				else
				{
					// Un-premultiply without clamping HDR values so glow intensity and color saturation remain intact
					C.r = A.r / C.a;
					C.g = A.g / C.a;
					C.b = A.b / C.a;
				}

				RESULT_RawData[i] = C;
				*/

				Color A = A_RawData[i];     // black (with post-process)
				Color B = B_RawData[i];     // white (with post-process)
				Color tR = tR_RawData[i];   // red   (no post-process)
				Color tG = tG_RawData[i];   // green (no post-process)
				Color tB = tB_RawData[i];   // blue  (no post-process)

				Color C = new Color();

				// 1. Exact background transmittance (1 - alpha) from orthogonal R, G, B backgrounds.
				// Using Max across the three channel differences avoids hue bias on saturated VFX (like pure blue/red flames),
				// because any channel where the object is dark yields the exact uncorrupted background transmittance.
				float transR = Mathf.Clamp01(tR.r - tG.r);
				float transG = Mathf.Clamp01(tG.g - tB.g);
				float transB = Mathf.Clamp01(tB.b - tR.b);
				float maxTransmittance = Mathf.Max(transR, Mathf.Max(transG, transB));
				float rgbAlpha = Mathf.Clamp01(1f - maxTransmittance);

				// 2. Post-process / HDR bloom alpha derived from black vs white background captures.
				float postAlpha = Mathf.Clamp01(1f - Mathf.Min(B.r - A.r, Mathf.Min(B.g - A.g, B.b - A.b)));

				// 3. Luminance-weighted blend to prevent dark fringes on soft flame edges fading into black/bloom.
				// Keeps full rgbAlpha for dark opaque cores, while letting glowing edges transition smoothly into postAlpha.
				float glowLuminance = Mathf.Clamp01(Mathf.Max(A.r, Mathf.Max(A.g, A.b)));
				float effectiveRgbAlpha = Mathf.Lerp(rgbAlpha, postAlpha, glowLuminance * (1f - rgbAlpha));

				C.a = Mathf.Max(effectiveRgbAlpha, postAlpha);

				if (C.a <= ALPHA_LIMIT)
				{
					C = Color.clear;
				}
				else
				{
					// Un-premultiply without clamping HDR values so glow intensity and color saturation remain intact
					C.r = A.r / C.a;
					C.g = A.g / C.a;
					C.b = A.b / C.a;
				}

				RESULT_RawData[i] = C;
			}
		}

		/// <summary>
		/// Returns a new Texture2D with transparent background created from a 'texture_black' and 'texture_white'<para></para>
		/// (where 'texture_black' has a black backgound and 'texture_white' has a white background).<para></para>
		/// Additionaly tests textures with RGB backgrounds.
		/// </summary>
		public Texture2D Process(Texture2D texture_black, Texture2D texture_white, Texture2D texture_red, Texture2D texture_green, Texture2D texture_blue)
		{
			var A_RawData = texture_black.GetRawTextureData<Color>();
			var B_RawData = texture_white.GetRawTextureData<Color>();
			var tR_RawData = texture_red.GetRawTextureData<Color>();
			var tG_RawData = texture_green.GetRawTextureData<Color>();
			var tB_RawData = texture_blue.GetRawTextureData<Color>();
			var RESULT_texture = new Texture2D(texture_black.width, texture_black.height, texture_black.format, false);
			var RESULT_RawData = RESULT_texture.GetRawTextureData<Color>();

			// OnCreateNewData the job data
			var job = new FX_Job()
			{
				A_RawData = A_RawData,
				B_RawData = B_RawData,
				tR_RawData = tR_RawData,
				tG_RawData = tG_RawData,
				tB_RawData = tB_RawData,
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
			tR_RawData.Dispose();
			tG_RawData.Dispose();
			tB_RawData.Dispose();
			RESULT_RawData.Dispose();

			return RESULT_texture;
		}
	}
}