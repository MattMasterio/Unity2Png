/*
    Author: Matthew Mazan (Masterio)
    Permitted: Free to personal or commercial use.
    Forbidden: Re-Sell/Re-Share this package.
    Compatibility: Unity 2021.3.0+, URP
*/

#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using System.Collections;
using System.IO;
using UnityEngine.Experimental.Rendering;
using System;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Unity2Png
{
    public class Unity2PNG : MonoBehaviour
    {
        public const string VERSION = "(v3.0)";
		public const string VIDEO_TUTORIAL_LINK = "https://youtu.be/j4Z2xsvX4vw";
		public const int MAX_TRANSITION_FRAMES = 60;

		#region PROPERTIES
		// render texture
		public RenderTexture renderTexture;
		public ResolutionPreset resolutionPreset = ResolutionPreset.CopyFromRenderTexture;

		public enum ResolutionPreset
		{
			CopyFromRenderTexture = 0,
			_256x256 = 256,
			_512x512 = 512,
			_1024x1024 = 1024,
			_2048x2048 = 2048
		}

		// input
		public int frameRate = 60;
		public float duration = 1f;
		public CaptureBackground captureBackground = CaptureBackground.Transparent;
		// transition/loop
		[Tooltip("Selects the seamless loop transition method:\n\n" +
				 "• Off: No loop transition.\n" +
				 "• Seamless: Universal seamless transition using Quintic SmootherStep and Premultiplied Alpha to prevent over-brightening and dark fringes.")]
		public LoopMethod loopTransitionMethod = LoopMethod.Off;
		public int loopTransitionFrames = 30;
		// post process
		[Tooltip("Selects when post-processing effects (such as Bloom or Color Grading) are applied:\n\n" +
						 "• Off: Disables post-processing during capture.\n" +
						 "• On Input Texture: Applies post-processing before the loop transition is generated (can cause a glow artifact during legacy transitions).\n" +
						 "• On Output Texture: Applies post-processing after the loop transition is generated (recommended for loops; requires Actors and Post Process Renderer).")]
		public PostProcessMethod postProcessMethod = PostProcessMethod.Off;
		[Tooltip("Additional RGB alpha check (takes ~2.5x longer to calculate).\n\n" +
				 "Fixes artifacts where dark, black, or grey pixels (RGB < 1.0) are captured as semi-transparent instead of fully opaque.\n\n" +
				 "Recommended when using Bloom or HDR effects.")]
		public bool rgbTest = true;
		[Tooltip("Root GameObject containing the visual VFX/scene objects to capture. Temporarily hidden during the post-process re-capture pass.")]
		public GameObject actors;
		[Tooltip("Quad or plane renderer used to display the blended loop frames back to the camera so post-processing (e.g., Bloom) can be applied on the output.")]
		public Renderer postProcessRenderer;
		public bool postProcessRenderer_FitFullScreen = true;
		// mask
		[Tooltip("Selects how the alpha mask is applied:\n\n" +
						 "• Off: Disables masking.\n" +
						 "• On Input Texture: Masks each captured frame before loop blending and output post-processing.\n" +
						 "• On Output Texture: Masks the final image after loop blending and post-processing.")]
		public MaskMethod maskMethod = MaskMethod.Off;

		[Tooltip("Renderer object whose black-and-white luminance defines the transparency mask (white = visible, black = transparent).")]
		public Renderer maskRenderer;
		// output
		public string folderPath = "C:\\Unity2PNG";
        public string fileName = "";
		public int digits = 3;
		public bool appendObjectNameToFolder = true;
		public bool overrideFolderContent = true;
        public bool showInFileExplorer = true;
        // player
        public bool captureAutoStart = false;               // otherwise START button press or mouse click on the game screen is required
        public float captureDelay = 3f;
		// alpha cut off adjustment
		[Tooltip("Smooths out faint outer pixels (low-alpha glow/bloom edges) by dampening alpha values below the threshold.\n\n" +
						 "Useful for cleaning up soft transparent fringes around glowing VFX.")]
		public bool alphaSmooth = false;

		[Tooltip("Alpha Threshold (Start): Pixels with an alpha value below this limit (0.1 to 0.5) will be smoothed out.\n\nDefault: 0.3")]
		[Range(0.1f, 0.9f)]
		public float alphaSmoothLimit = 0.3f;

		[Tooltip("Smoothing Strength (Power): Controls how strongly faint pixels below the threshold are faded out. Higher values remove more near-transparent glow.\n\nDefault: 1.5")]
		[Range(0.1f, 3f)]
		public float alphaSmoothDamp = 1.5f;
		// color space conversion
		[Tooltip("Converts captured linear color values to sRGB (Gamma) before saving PNGs.\n\n" +
				 "• Checked (Recommended): Output PNGs match the Game view and standard image viewers.\n" +
				 "• Unchecked: Exports raw linear data (useful only for data textures like masks, flowmaps, or non-sRGB shader inputs).")]
		public bool convertLinearToGamma = true;

		// debug frames
		public bool debug = false;

		// default
		private string _fullPath = "";                      // folder path
        private Camera _camera;                             // camera component
        private int _currentFrame = 0;                      // current frame
        private int _screenWidth;                           // screen width
        private int _screenHeight;                          // screen height
        private Texture2D _alphaTest_Black;                 // tmp texture
        private Texture2D _alphaTest_White;                 // tmp texture
        private Texture2D _alphaTest_Red;                   // tmp texture
        private Texture2D _alphaTest_Green;                 // tmp texture
        private Texture2D _alphaTest_Blue;                  // tmp texture
        private Texture2D _maskTexture;                     // tmp texture
        private int _defaultCaptureFramerate = 0;           // default

		// tmp
		private RenderTexture _tempRenderTexture;           // reusable temporary render texture

		// loop
		// loop - ONLY transition frames are kept in RAM!
		private int _totalFrames = 0;
		private int _transitionFrames = 0;
		private int _outputFrames = 0;
		private Texture2D[] _transitionBufferA;				// First _transitionFrames of capture (indices 0 .. _transitionFrames-1)
		private Texture2D[] _transitionBufferB;				// Last _transitionFrames of capture (indices _outputFrames .. _totalFrames-1)

        private CaptureStage _stage = CaptureStage.NotStarted;
        private static bool _started = false;               // used to stop other instances

		private DateTime _startTime;

		private static Unity2PNG _activeRecorder = null;

		public bool isRecording => _stage != CaptureStage.NotStarted && _stage != CaptureStage.Done;
		public static bool isAnyRecording => _started || (_activeRecorder != null && _activeRecorder.isRecording);

		public enum CaptureBackground
        {
            [Tooltip("Captures with transparent background.")]
            Transparent = 0,
            [Tooltip("Captures as is.")]
            Opaque = 1
        }

        public enum LoopMethod
        {
            Off = 0,
			[Tooltip("Seamless: Universal seamless transition using Quintic SmootherStep and Premultiplied Alpha to prevent over-brightening and dark fringes.")]
			Seamless = 1,
		}

		public enum PostProcessMethod
		{
			[Tooltip("Off: Disables post-processing during capture.")]
			Off = 0,
			[Tooltip("On Input Texture: Applies post-processing before the loop transition (can cause a glow artifact during transitions).")]
			OnInputTexture = 1,
			[Tooltip("On Output Texture: Applies post-processing after the loop transition is generated (recommended for loops; requires Post Process Renderer and Actors objects).")]
			OnOutputTexture = 2
		}

		public enum MaskMethod
        {
            Off = 0,                // no mask
            OnInputTexture = 1,
            OnOutputTexture = 2
        }

        private enum CaptureStage
        {
            NotStarted = 0,
            Prepare = 1,
            Recording = 2,
            Finalizing = 3,
            Done = 4
        }
		#endregion

		#region MAIN
		public int effectiveWidth
		{
			get
			{
				if (resolutionPreset != ResolutionPreset.CopyFromRenderTexture)
					return (int)resolutionPreset;
				return renderTexture != null ? renderTexture.width : 0;
			}
		}

		public int effectiveHeight
		{
			get
			{
				if (resolutionPreset != ResolutionPreset.CopyFromRenderTexture)
					return (int)resolutionPreset;
				return renderTexture != null ? renderTexture.height : 0;
			}
		}

		public void SyncPresetsFromRenderTexture()
		{
			if (renderTexture == null)
				return;

			if (renderTexture.width == renderTexture.height &&
				(renderTexture.width == 256 || renderTexture.width == 512 || renderTexture.width == 1024 || renderTexture.width == 2048))
			{
				resolutionPreset = (ResolutionPreset)renderTexture.width;
			}
			else
			{
				resolutionPreset = ResolutionPreset.CopyFromRenderTexture;
			}
		}

		public bool isLoop { get => loopTransitionMethod != LoopMethod.Off && _transitionFrames > 1; }
		
		public bool cameraPostProcessing { get => _camera.GetUniversalAdditionalCameraData().renderPostProcessing; set => _camera.GetUniversalAdditionalCameraData().renderPostProcessing = value; }
		public int calculatedOutputFrames
		{
			get
			{
				int frames = Mathf.Max(2, Mathf.RoundToInt(frameRate * duration));
				if (frames % 2 > 0)
					frames++;
				return frames;
			}
		}

		public int calculateMaxTransitionFrames
		{
			get
			{
				int halfTotal = Mathf.FloorToInt(calculatedOutputFrames / 2f);
				return Mathf.Clamp(halfTotal, 2, MAX_TRANSITION_FRAMES);
			}
		}

		public string effectiveFolderPath
		{
			get
			{
				string baseFolder = string.IsNullOrEmpty(folderPath)
					? "C:\\Unity2PNG"
					: folderPath.Trim().TrimEnd('\\', '/');

				if (!appendObjectNameToFolder)
					return baseFolder;

				// Replace only characters forbidden by Windows file system, keeping spaces and parentheses intact
				string safeObjName = string.Join("_", gameObject.name.Split(System.IO.Path.GetInvalidFileNameChars()))
										   .Trim().Trim('\\', '/');

				return baseFolder + "\\" + safeObjName;
			}
		}

		private void OnEnable()
		{
			_started = false; // must reset bcs unity editor can save this static value in some cases
			_activeRecorder = null;
		}

		public static string GetRecorderId(Unity2PNG obj)
		{
			if (obj == null)
				return "";
#if UNITY_7000_0_OR_NEWER
			return obj.GetEntityId().ToString();
#else
			return obj.GetInstanceID().ToString();
#endif
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void ActivateSelectedRecorderOnPlay()
		{
			_started = false;

			string targetID = SessionState.GetString("Unity2PNG_TargetID", "");
			if (string.IsNullOrEmpty(targetID))
				return;

			// Clear after reading so manual Play Mode clicks behave normally
			SessionState.EraseString("Unity2PNG_TargetID");

#if UNITY_2023_1_OR_NEWER
			Unity2PNG[] allRecorders = FindObjectsByType<Unity2PNG>(FindObjectsInactive.Include);
#else
            Unity2PNG[] allRecorders = FindObjectsOfType<Unity2PNG>(true);
#endif

			Unity2PNG targetRecorder = null;

			for (int i = 0; i < allRecorders.Length; i++)
			{
				if (GetRecorderId(allRecorders[i]) == targetID)
				{
					targetRecorder = allRecorders[i];
				}
				else
				{
					// Deactivate all other Unity2PNG objects so they don't interfere
					allRecorders[i].gameObject.SetActive(false);
				}
			}

			if (targetRecorder != null)
			{
				// Ensure parent hierarchy and the target GameObject itself are active
				Transform parent = targetRecorder.transform.parent;
				while (parent != null)
				{
					if (!parent.gameObject.activeSelf)
						parent.gameObject.SetActive(true);
					parent = parent.parent;
				}

				targetRecorder.gameObject.SetActive(true);
				targetRecorder.enabled = true;
			}
		}

		/// <summary>
		/// Starts recorder, you can call it any moment during gameplay.
		/// </summary>
		public void StartRecording()
        {
			// Ensure the Game view is focused and rendering so WaitForEndOfFrame triggers immediately
			System.Type gameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
			if (gameViewType != null)
			{
				EditorWindow.GetWindow(gameViewType, false, null, false);
			}

			if (_stage != CaptureStage.NotStarted)
                return;

			int w = effectiveWidth;
			int h = effectiveHeight;

			if (renderTexture == null)
			{
				Debug.LogError("[Unity2PNG] Render Texture is not assigned!");
				enabled = false;
				return;
			}

			if (renderTexture.graphicsFormat != GraphicsFormat.R32G32B32A32_SFloat)
			{
				Debug.LogError($"[Unity2PNG] Unsupported RenderTexture format: {renderTexture.graphicsFormat}. Required format: R32G32B32A32_SFloat (ARGBFloat).");
				enabled = false;
				return;
			}

			if (!SystemInfo.SupportsRenderTextureFormat(renderTexture.format))
			{
				Debug.LogError(renderTexture.format.ToString() + " is not supported in this system.");
				enabled = false;
				return;
			}

			double freeRamGB = GetAvailableSystemRamGB();
			double estPeakGB = EstimatePeakRamGB();
			const double minSafetyBufferGB = 1.0; // Always leave at least 1 GB free for Windows & Unity

			if (freeRamGB < (estPeakGB + minSafetyBufferGB))
			{
				Debug.LogError(
					$"[Unity2PNG] Capture blocked: Not enough free RAM! " +
					$"Estimated peak: ~{estPeakGB:F2} GB + {minSafetyBufferGB:F1} GB safety margin required, " +
					$"but only {freeRamGB:F2} GB is available."
				);
				enabled = false;
				EditorApplication.isPlaying = false;
				return;
			}
			
			_started = true;
			_activeRecorder = this;
			_stage = CaptureStage.Prepare;

			_startTime = DateTime.Now;

			_startFreeRamGB = GetAvailableSystemRamGB();
			_minFreeRamGB = _startFreeRamGB;

			_screenWidth = Screen.width;
            _screenHeight = Screen.height;

			CreateOutputFolder();

            _defaultCaptureFramerate = Time.captureFramerate;
            _camera = gameObject.GetComponentInChildren<Camera>(true);
            _camera.targetTexture = renderTexture;

            // correct start settings if needed 
            if (frameRate % 2 > 0)
                frameRate = Mathf.FloorToInt(frameRate + 1);

			Time.captureFramerate = frameRate;

			_transitionFrames = loopTransitionFrames;

            if (_transitionFrames < 2)
                _transitionFrames = 2;

            if (_transitionFrames > calculateMaxTransitionFrames)
                _transitionFrames = calculateMaxTransitionFrames;

            if (_transitionFrames % 2 > 0)
                _transitionFrames = Mathf.FloorToInt(_transitionFrames + 1);

			_totalFrames = calculatedOutputFrames + (isLoop ? _transitionFrames : 0);
			_outputFrames = _totalFrames - (isLoop ? _transitionFrames : 0);

			// Allocate ONLY small transition buffers in RAM (0 if loop is disabled!)
			if (isLoop)
			{
				_transitionBufferA = new Texture2D[_transitionFrames];
				_transitionBufferB = new Texture2D[_transitionFrames];
			}
			else
			{
				_transitionBufferA = null;
				_transitionBufferB = null;
			}

			_alphaTest_Black = new Texture2D(w, h, renderTexture.graphicsFormat, 0, TextureCreationFlags.None);
			_alphaTest_White = new Texture2D(w, h, renderTexture.graphicsFormat, 0, TextureCreationFlags.None);
			_alphaTest_Red = new Texture2D(w, h, renderTexture.graphicsFormat, 0, TextureCreationFlags.None);
			_alphaTest_Green = new Texture2D(w, h, renderTexture.graphicsFormat, 0, TextureCreationFlags.None);
			_alphaTest_Blue = new Texture2D(w, h, renderTexture.graphicsFormat, 0, TextureCreationFlags.None);

			// create a single reusable RenderTexture for the entire capture session
			_tempRenderTexture = CloneRenderTexture(renderTexture);

			// turn off post processing
			cameraPostProcessing = false;

            if (postProcessRenderer != null)
            {
                postProcessRenderer.gameObject.SetActive(false);

                if(postProcessRenderer_FitFullScreen)
                    PostProcessRenderer_FitFullScreen();
            }

            if (maskRenderer != null)
                maskRenderer.gameObject.SetActive(false);

            if (actors != null)
                actors.SetActive(true);

			// Start after one frame to ensure URP Render Graph and scene are fully initialized
			StartCoroutine(StartCaptureDelayed());
		}

		private IEnumerator StartCaptureDelayed()
		{
			// Wait one frame for Unity's Render Pipeline to fully initialize on first Play Mode entry
			yield return null;
			StartCoroutine(CaptureFrame());
		}

		private void RenderMaskTexture()
		{
			if (maskMethod != MaskMethod.Off && maskRenderer != null && _maskTexture == null)
			{
				if (actors != null)
					actors.SetActive(false);

				maskRenderer.gameObject.SetActive(true);

				// Allocate _maskTexture once and write pixels directly into it
				_maskTexture = new Texture2D(effectiveWidth, effectiveHeight, renderTexture.graphicsFormat, 0, TextureCreationFlags.None);

				CaptureTexture(_camera, Color.black, false, _maskTexture);

				maskRenderer.gameObject.SetActive(false);

				if (actors != null)
					actors.SetActive(true);
			}
		}

		private void LateUpdate()
		{
			if (!_started)
			{
				if (_stage == CaptureStage.NotStarted)
				{
					bool anyKeyPressed = false;

#if ENABLE_INPUT_SYSTEM
					// New Input System check (Keyboard and Mouse)
					anyKeyPressed = (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) ||
									(Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame ||
															   Mouse.current.rightButton.wasPressedThisFrame ||
															   Mouse.current.middleButton.wasPressedThisFrame));
#elif ENABLE_LEGACY_INPUT_MANAGER
					// Old Input Manager check
					anyKeyPressed = Input.anyKeyDown;
#endif

					// Start recording on any input OR when auto-start delay has passed
					if (anyKeyPressed || (captureAutoStart && captureDelay <= Time.timeSinceLevelLoad))
					{
						StartRecording();
					}
				}
			}
			else if (_stage == CaptureStage.NotStarted)
			{
				gameObject.SetActive(false);
			}
		}

		IEnumerator CaptureFrame()
        {
            yield return new WaitForEndOfFrame();

            if (_stage == CaptureStage.Prepare)
            {
                RenderMaskTexture();
                _stage = CaptureStage.Recording;

                StartCoroutine(CaptureFrame());
            }
            else if (_stage == CaptureStage.Recording)
            {
                if (_currentFrame < _totalFrames)
                {
					double currentFreeGB = GetAvailableSystemRamGB();
					if (currentFreeGB < 0.80)
					{
						AbortRecordingDueToMemory($"Available system RAM dropped to {currentFreeGB:F2} GB at frame {_currentFrame}/{_totalFrames}.");
						yield break;
					}

					ProcessCapturedFrame(_currentFrame);
					_currentFrame++;
                    StartCoroutine(CaptureFrame());
                }
                else
                {
                    if (actors != null)
                        actors.SetActive(false);

					Debug.Log("COMPUTING PLEASE WAIT ...");
                    _stage = CaptureStage.Finalizing;

					yield return new WaitForSecondsRealtime(0.5f);

					StartCoroutine(CaptureFrame());
				}
            }
            else if (_stage == CaptureStage.Finalizing)
            {
                StopCoroutine("CaptureFrame");
                OnFinalize();
            }
        }

        void OnGUI()
        {
            if (_stage == CaptureStage.Finalizing)
                GUI.Label(new Rect(_screenWidth / 2 - 40, _screenHeight / 2 - 10, 256, 20), "COMPUTING...");
        }

		private void OnFinalize()
		{
			Time.captureFramerate = _defaultCaptureFramerate;

			_started = false;
			_activeRecorder = null;
			_stage = CaptureStage.Done;

			enabled = false;

			// Process ONLY the transition frames (which were kept in RAM) and export them directly!
			if (isLoop)
			{
				ProcessAndExportTransitionFrames();
			}

			// Release all temporary & transition buffers
			ReleaseAllWorkingBuffers();

			// Final utilities
			UpdatePeakRam();
			System.GC.Collect();

			DateTime endTime = DateTime.Now;
			TimeSpan span = endTime.Subtract(_startTime);

			double totalRamGB = SystemInfo.systemMemorySize / 1024.0;
			double ramStartGB = totalRamGB - _startFreeRamGB;
			double ramPeakGB = totalRamGB - _minFreeRamGB;
			double ramDiffGB = Mathf.Max(0f, (float)(ramPeakGB - ramStartGB));

			string diffText = ramDiffGB >= 1.0
				? $"{ramDiffGB:F2} GB"
				: $"{ramDiffGB * 1024.0:F0} MB";

			Debug.Log($"RAM Start: {ramStartGB:F2} GB | RAM Peak: {ramPeakGB:F2} GB | RAM Used: {diffText} | RAM Left: {_minFreeRamGB:F2} GB");
			Debug.Log(
				"Complete in " + span.TotalSeconds.ToString("F1") + "s, " +
				_outputFrames + " video frames rendered" +
				(isLoop ? ", " + _transitionFrames + " transition frames used." : ".")
			);

			EditorApplication.isPlaying = false;

			if (showInFileExplorer)
				OpenFolder(effectiveFolderPath);
		}
		public static void OpenFolder(string path)
        {
            Application.OpenURL("file:\\" + path);
            //EditorUtility.RevealInFinder(foler); // this one opens a parent folder
        }

        // Fills current camera screen rect.
        public void PostProcessRenderer_FitFullScreen()
        {
            if (postProcessRenderer != null)
            {
                Camera cam = GetComponentInChildren<Camera>(false);

                if (cam != null)
                {
                    float pos = cam.nearClipPlane;
                    float h = Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * 0.5f) * pos * 2f * (1f / cam.nearClipPlane);
                    postProcessRenderer.transform.localScale = new Vector3(h * cam.aspect, h, 0f) * postProcessRenderer.transform.localPosition.z;
                }
                else
                    Debug.LogError("cam == null");
            }
            else
                Debug.LogError("postProcessRenderer == null");
        }
		#endregion

		#region CAPTURE
		private void CaptureTexture(Camera camera, Color backgroundColor, bool postProcess, Texture2D targetTexture)
		{
			if (_tempRenderTexture == null)
				_tempRenderTexture = CloneRenderTexture(renderTexture);

			camera.backgroundColor = backgroundColor;
			cameraPostProcessing = postProcess;

			RenderTexture.active = _tempRenderTexture;

			camera.targetTexture = _tempRenderTexture;
			camera.Render(); // also applies the post-process if any enabled

			// Reuse existing Texture2D buffer instead of allocating a new one
			targetTexture.ReadPixels(new Rect(0, 0, _tempRenderTexture.width, _tempRenderTexture.height), 0, 0);
			targetTexture.Apply();

			camera.targetTexture = null;
			RenderTexture.active = null;
		}

		private RenderTexture CloneRenderTexture(RenderTexture sourceRT)
		{
			int w = effectiveWidth;
			int h = effectiveHeight;

			RenderTexture rt = new RenderTexture(w, h, sourceRT.depth, sourceRT.graphicsFormat, sourceRT.mipmapCount);
			rt.format = sourceRT.format;
			rt.graphicsFormat = sourceRT.graphicsFormat;
			rt.depthStencilFormat = sourceRT.depthStencilFormat;
			rt.wrapMode = sourceRT.wrapMode;
			rt.useDynamicScale = sourceRT.useDynamicScale;
			rt.useMipMap = sourceRT.useMipMap;
			rt.filterMode = sourceRT.filterMode;
			rt.antiAliasing = sourceRT.antiAliasing;

			return rt;
		}

		private void CaptureScreen(bool render_post_effects)
		{
			if (captureBackground == CaptureBackground.Transparent)
			{
				if (rgbTest && postProcessMethod != PostProcessMethod.Off)
				{
					CaptureTexture(_camera, Color.red, false, _alphaTest_Red);
					CaptureTexture(_camera, Color.green, false, _alphaTest_Green);
					CaptureTexture(_camera, Color.blue, false, _alphaTest_Blue);
				}

				CaptureTexture(_camera, Color.white, render_post_effects, _alphaTest_White);
			}

			CaptureTexture(_camera, Color.black, render_post_effects, _alphaTest_Black);
		}

		private Texture2D CalculateTransparentScreen(bool apply_mask, bool post_effects)
		{
			CaptureScreen(post_effects);

			bool rgb_test = rgbTest && postProcessMethod != PostProcessMethod.Off;
			Texture2D texture = null;

			if (captureBackground == CaptureBackground.Opaque)
			{
				if (apply_mask)
				{
					FX_Mask fx_mask = new();
					texture = fx_mask.Process(_alphaTest_Black, _maskTexture);
				}
				else
				{
					FX_CopyTexture fx_copy = new();
					texture = fx_copy.Process(_alphaTest_Black);
				}
			}
			else
			{
				if (rgb_test)
				{
					FX_TransparentBackground_RGBTest fx_transparentBackground_RGB = new();
					texture = fx_transparentBackground_RGB.Process(_alphaTest_Black, _alphaTest_White, _alphaTest_Red, _alphaTest_Green, _alphaTest_Blue);
				}
				else
				{
					FX_TransparentBackground fx_transparentBackground = new();
					texture = fx_transparentBackground.Process(_alphaTest_Black, _alphaTest_White);
				}

				if (apply_mask)
				{
					FX_Mask fx_mask = new();
					Texture2D maskedTex = fx_mask.Process(texture, _maskTexture);
					if (maskedTex != texture && texture != null)
						Destroy(texture);
					texture = maskedTex;
				}
			}

			return texture;
		}
		private Texture2D ApplyMaskToTexture(Texture2D texture)
		{
			FX_Mask fx_mask = new();
			Texture2D masked = fx_mask.Process(texture, _maskTexture);
			if (masked != texture && texture != null)
				Destroy(texture);
			return masked;
		}

		private Texture2D ApplyOutputPostProcessSingleFrame(Texture2D sourceTex)
		{
			if (postProcessMethod != PostProcessMethod.OnOutputTexture || sourceTex == null)
				return sourceTex;

			// Force upload of CPU pixel changes to GPU VRAM before rendering the quad
			sourceTex.Apply(false, false);

			int prevCullingMask = _camera.cullingMask;
			int pprLayer = 31; // Use built-in unused layer 31 for isolated post-process pass
			int prevPprLayer = 0;

			if (postProcessRenderer != null)
			{
				prevPprLayer = postProcessRenderer.gameObject.layer;
				postProcessRenderer.gameObject.layer = pprLayer;
				_camera.cullingMask = 1 << pprLayer; // Camera renders ONLY the postProcessRenderer!

				postProcessRenderer.gameObject.SetActive(true);
				postProcessRenderer.material.mainTexture = sourceTex;
				if (postProcessRenderer.material.HasProperty("_BaseMap"))
					postProcessRenderer.material.SetTexture("_BaseMap", sourceTex);
			}

			bool applyOutputMask = (maskMethod == MaskMethod.OnOutputTexture) && (_maskTexture != null);
			Texture2D resultTex = CalculateTransparentScreen(applyOutputMask, true);

			if (postProcessRenderer != null)
			{
				postProcessRenderer.material.mainTexture = null;
				if (postProcessRenderer.material.HasProperty("_BaseMap"))
					postProcessRenderer.material.SetTexture("_BaseMap", null);
				postProcessRenderer.gameObject.SetActive(false);
				postProcessRenderer.gameObject.layer = prevPprLayer;
			}

			// Restore camera culling mask without ever touching actors.SetActive!
			_camera.cullingMask = prevCullingMask;
			cameraPostProcessing = false;

			UpdatePeakRam();
			Destroy(sourceTex);

			return resultTex;
		}

		private void FinalizeAndExportSingleFrame(Texture2D tex, int outputFileIndex)
		{
			if (tex == null)
			{
				Debug.LogError("Frame texture is null for output index " + outputFileIndex);
				return;
			}

			if (postProcessMethod == PostProcessMethod.OnOutputTexture)
			{
				tex = ApplyOutputPostProcessSingleFrame(tex);
			}
			else if (maskMethod == MaskMethod.OnOutputTexture && _maskTexture != null)
			{
				tex = ApplyMaskToTexture(tex);
			}

			if (alphaSmooth && tex != null)
			{
				FX_AlphaSmooth fx_alphaSmooth = new();
				fx_alphaSmooth.Process(tex, alphaSmoothLimit, alphaSmoothDamp);
			}

			FX_ClearAlpha fx_clearAlpha = new FX_ClearAlpha();
			fx_clearAlpha.Process(tex, debug);

			if (convertLinearToGamma && PlayerSettings.colorSpace == ColorSpace.Linear)
			{
				FX_LinearToGamma fx_linearToGamma = new();
				Texture2D oldTex = tex;
				tex = fx_linearToGamma.Process(oldTex);
				UpdatePeakRam();
				Destroy(oldTex);
			}

			SavePng(tex, outputFileIndex);
			UpdatePeakRam();
			Destroy(tex);
		}

		private void ProcessCapturedFrame(int frame)
		{
			bool apply_mask = maskMethod == MaskMethod.OnInputTexture && _maskTexture != null;
			bool post_effects = postProcessMethod == PostProcessMethod.OnInputTexture;

			Texture2D tex = CalculateTransparentScreen(apply_mask, post_effects);

			if (isLoop)
			{
				if (frame < _transitionFrames)
				{
					_transitionBufferA[frame] = tex;
					UpdatePeakRam();
					if (debug)
						Debug.Log($"Frame {frame} captured (stored in Transition Buffer A).");
					else
						Debug.Log($"Frame {frame} captured.");

					return;
				}

				if (frame >= _outputFrames)
				{
					int bIndex = frame - _outputFrames;
					_transitionBufferB[bIndex] = tex;
					UpdatePeakRam();
					if(debug)
						Debug.Log($"Frame {frame} captured (stored in Transition Buffer B).");
					else
						Debug.Log($"Frame {frame} captured.");
					return;
				}
			}

			int finalOutputIndex = GetNonTransitionOutputIndex(frame);
			FinalizeAndExportSingleFrame(tex, finalOutputIndex);

			if (debug)
				Debug.Log($"Frame {frame} captured, saved as #{finalOutputIndex} and freed from RAM.");
			else
				Debug.Log($"Frame {frame} captured.");
		}

		private int GetNonTransitionOutputIndex(int capturedFrame)
		{
			if (!isLoop)
				return capturedFrame;

			int halfTotal = _totalFrames / 2;

			if (capturedFrame >= _transitionFrames && capturedFrame < halfTotal)
			{
				int offsetBeforeA = halfTotal - _transitionFrames;
				return offsetBeforeA + capturedFrame;
			}

			if (capturedFrame >= halfTotal && capturedFrame < _outputFrames)
			{
				return capturedFrame - halfTotal;
			}

			return -1;
		}

		private void ProcessAndExportTransitionFrames()
		{
			if (!isLoop || _transitionBufferA == null || _transitionBufferB == null)
				return;

			int halfTotal = _totalFrames / 2;
			int transitionStartOutputIndex = halfTotal - _transitionFrames;

			if (debug)
				Debug.Log("[ Transition ] Transition frame's alpha(x, y) for frames (A, B):");

			for (int i = 0; i < _transitionFrames; i++)
			{
				Texture2D blendedTex = null;

				if (loopTransitionMethod == LoopMethod.Seamless)
				{
					FX_Transition_Seamless fx = new();
					blendedTex = fx.Process(_transitionBufferA[i], _transitionBufferB[i], i, _transitionFrames, debug);
				}
				// else ... more future transition types ?

				UpdatePeakRam();

				if (_transitionBufferA[i] != null)
				{
					Destroy(_transitionBufferA[i]);
					_transitionBufferA[i] = null;
				}

				if (_transitionBufferB[i] != null)
				{
					Destroy(_transitionBufferB[i]);
					_transitionBufferB[i] = null;
				}

				int outputIndex = transitionStartOutputIndex + i;
				FinalizeAndExportSingleFrame(blendedTex, outputIndex);
			}
		}

		private void ReleaseAllWorkingBuffers()
		{
			if (_maskTexture != null)
			{ Destroy(_maskTexture); _maskTexture = null; }

			if (_tempRenderTexture != null)
			{
				_tempRenderTexture.Release();
				Destroy(_tempRenderTexture);
				_tempRenderTexture = null;
			}

			if (_alphaTest_Black != null)
			{ Destroy(_alphaTest_Black); _alphaTest_Black = null; }
			if (_alphaTest_White != null)
			{ Destroy(_alphaTest_White); _alphaTest_White = null; }
			if (_alphaTest_Red != null)
			{ Destroy(_alphaTest_Red); _alphaTest_Red = null; }
			if (_alphaTest_Green != null)
			{ Destroy(_alphaTest_Green); _alphaTest_Green = null; }
			if (_alphaTest_Blue != null)
			{ Destroy(_alphaTest_Blue); _alphaTest_Blue = null; }

			if (_transitionBufferA != null)
			{
				for (int i = 0; i < _transitionBufferA.Length; i++)
				{
					if (_transitionBufferA[i] != null)
					{
						Destroy(_transitionBufferA[i]);
						_transitionBufferA[i] = null;
					}
				}
				_transitionBufferA = null;
			}

			if (_transitionBufferB != null)
			{
				for (int i = 0; i < _transitionBufferB.Length; i++)
				{
					if (_transitionBufferB[i] != null)
					{
						Destroy(_transitionBufferB[i]);
						_transitionBufferB[i] = null;
					}
				}
				_transitionBufferB = null;
			}
		}

		private void CreateOutputFolder()
		{
			string path = effectiveFolderPath;
			_fullPath = effectiveFolderPath;

			if (!overrideFolderContent)
			{
				int count = 1;
				while (Directory.Exists(_fullPath))
				{
					_fullPath = path + " " + count;
					count++;
				}
				Directory.CreateDirectory(_fullPath);
			}
			else
			{
				// Create folder if it doesn't exist
				if (!Directory.Exists(_fullPath))
				{
					Directory.CreateDirectory(_fullPath);
				}
				else
				{
					// Delete only existing files inside the folder instead of deleting the folder itself
					// This prevents Windows Explorer / Antivirus file-lock race conditions!
					foreach (string file in Directory.GetFiles(_fullPath))
					{
						File.Delete(file);
					}
				}
			}
		}

		public void SavePng(Texture2D texture, int frame)
        {
			string name = string.Format("{0}\\{1}{2:D0" + digits + "}.png", _fullPath, fileName, frame);

			if (texture != null)
            {
                var output = texture.EncodeToPNG();
                File.WriteAllBytes(name, output);
            }
            else
                Debug.LogError("output[" + frame + "] is null!");

            //Debug.Log("Frame " + frame + " exported to " + name);
        }

		private void OnValidate()
		{
			if (convertLinearToGamma && PlayerSettings.colorSpace != ColorSpace.Linear)
			{
				convertLinearToGamma = false;
			}
		}

		private void OnDestroy()
		{
			_started = false;
			Time.captureFramerate = _defaultCaptureFramerate;
			ReleaseAllWorkingBuffers();
		}
		#endregion

		#region MEASURMENTS
		public double EstimatePeakRamGB()
		{
			if (renderTexture == null)
				return 0.0;

			bool loopEnabled = loopTransitionMethod != LoopMethod.Off;
			int transitionFrames = Mathf.Clamp(loopTransitionFrames, 2, calculateMaxTransitionFrames);
			if (transitionFrames % 2 > 0)
				transitionFrames++;

			long pixelsPerFrame = (long)effectiveWidth * effectiveHeight;

			// R32G32B32A32_SFloat uses 16 bytes per pixel (4 floats x 4 bytes)
			long bytesPerPixel = 16L;
			long bytesPerFrame = pixelsPerFrame * bytesPerPixel;

			// Only transition frames (2 * transitionFrames) or 1 active frame are kept in RAM
			long activeFramesInRam = loopEnabled ? (transitionFrames * 2L) : 1L;
			long totalBuffers = activeFramesInRam;

			// Reusable working buffers (Black, White, optional RGB, Mask, TempRT)
			int reusableBuffers = captureBackground == CaptureBackground.Transparent ? 3 : 2;
			if (captureBackground == CaptureBackground.Transparent && rgbTest && postProcessMethod != PostProcessMethod.Off)
				reusableBuffers += 3;
			if (maskMethod != MaskMethod.Off)
				reusableBuffers += 1;

			totalBuffers += reusableBuffers;

			// Temporary single-frame processing buffers
			if (loopEnabled)
				totalBuffers += 1;
			if (postProcessMethod == PostProcessMethod.OnOutputTexture)
				totalBuffers += 1;
			if (convertLinearToGamma && PlayerSettings.colorSpace == ColorSpace.Linear)
				totalBuffers += 1;

			// 2.2x multiplier accounts for CPU texture copy + GPU driver copy + temporary EncodeToPNG buffer
			double estBytes = (bytesPerFrame * totalBuffers) * 2.2;
			return estBytes / (1024.0 * 1024.0 * 1024.0);
		}
		private void AbortRecordingDueToMemory(string reason)
		{
			_started = false;
			Debug.LogError($"[Unity2PNG] Capture aborted to prevent Out-Of-Memory crash! {reason}");
			StopAllCoroutines();
			Time.captureFramerate = _defaultCaptureFramerate;
			_stage = CaptureStage.Done;
			enabled = false;

			ReleaseAllWorkingBuffers();

			System.GC.Collect();
			EditorApplication.isPlaying = false;
		}

		private double _startFreeRamGB = 0;
		private double _minFreeRamGB = 0;

		[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
		private class MEMORYSTATUSEX
		{
			public uint dwLength;
			public uint dwMemoryLoad;
			public ulong ullTotalPhys;
			public ulong ullAvailPhys;
			public ulong ullTotalPageFile;
			public ulong ullAvailPageFile;
			public ulong ullTotalVirtual;
			public ulong ullAvailVirtual;
			public ulong ullAvailExtendedVirtual;

			public MEMORYSTATUSEX()
			{
				this.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MEMORYSTATUSEX));
			}
		}

		[System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
		[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
		private static extern bool GlobalMemoryStatusEx([System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] MEMORYSTATUSEX lpBuffer);

		public static double GetAvailableSystemRamGB()
		{
#if UNITY_EDITOR_WIN
			MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
			if (GlobalMemoryStatusEx(memStatus))
			{
				// Check BOTH physical RAM and Windows PageFile/Commit limit (prevents DX11/Native OOM crashes!)
				ulong realAvailableBytes = Math.Min(memStatus.ullAvailPhys, memStatus.ullAvailPageFile);
				return realAvailableBytes / (1024.0 * 1024.0 * 1024.0);
			}
#endif
			double totalGB = SystemInfo.systemMemorySize / 1024.0;
			double unityUsedGB = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024.0 * 1024.0 * 1024.0);
			return Mathf.Max(0f, (float)(totalGB - unityUsedGB));
		}

		private void UpdatePeakRam()
		{
			double currentFree = GetAvailableSystemRamGB();
			if (currentFree < _minFreeRamGB)
				_minFreeRamGB = currentFree;
		}
		#endregion
	}
}
#endif