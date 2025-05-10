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
using System.Threading.Tasks;
using UnityEngine.UI;
using System.Threading;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using System;

namespace Unity2Png
{
    public class Unity2PNG : MonoBehaviour
    {
        public const string VERSION = "(v2.0)";
		public const string VIDEO_TUTORIAL = "https://youtu.be/IMklxTDHUrc";

        #region PROPERTIES
        // render texture
        public RenderTexture renderTexture;
        // input
        public int frameRate = 30;
        public int duration = 1;
        public CaptureBackground captureBackground = CaptureBackground.Transparent;
        // transition/loop
        public LoopMethod loopTransitionMethod = LoopMethod.Off;
        public int loopTransitionFrames = 10;
        public float transitionMultiplier = 1.15f;
        // post process
        public PostProcessMethod postProcessMethod = PostProcessMethod.Off;
        public bool rgbTest = false;
        public GameObject actors;                                           // only for PostProcessMethod.OnOutputTexture 
        public Renderer postProcessRenderer;                                // only for PostProcessMethod.OnOutputTexture
        public bool postProcessRenderer_FitFullScreen = true;
        // mask
        public MaskMethod maskMethod = MaskMethod.Off;
        public Renderer maskRenderer;                                       // mask is applied on frame capture end (uses camera black backgound)
        // output
        public string folderPath = "C:\\Unity2PNG";
        public string fileName = "";
        public int digits = 3;
        public bool overrideFolderContent = true;
        public bool showInFileExplorer = true;
        // player
        public bool captureAutoStart = true;				// other wise button or mouse click required
        public float captureDelay = 1f;
        // alpha cut off adjustment
        public bool alphaSmooth = true;
        public float alphaSmoothLimit = 0.2f;				
        public float alphaSmoothDamp = 0.4f;
        // color psace conversion
        public bool convertLinearToGamma = true;

		// debug frames
		public bool debug = false;							// shows transition frame alpha ratios

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

        // loop
        private int _totalFrames = 0;
        private int _transitionFrames = 0;
        private int _outputFrames = 0;
        private Texture2D[] _array_a;                       // first half of input frames (transition_a inlcuded)
        private Texture2D[] _array_b;                       // second half of input frames (transition_b inlcuded)
		private Texture2D[] _final_array;

		// output frames
		private Texture2D[] _output;                        // frames ready to save as png

        private CaptureStage _stage = CaptureStage.NotStarted;
        private static bool _started = false;               // used to stop other instances
		private DateTime _startTime;

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
            [Tooltip("Transition (Standard): Linear transition. Frames A and B are multiplied (+alpha correction).")]
            Transition_Standard = 1,
            [Tooltip("Transition (Normalized): Normalized transition. Frames A and B are multiplied (+alpha correction).")]
            Transition_Standard_Normalized = 2,
            [Tooltip("Transition (Custom): Linear transition (multiplied by custom value). Frames A and B are multiplied (+alpha correction).")]
            Transition_Custom = 3,
			[Tooltip("Transition (Custom): Normalized transition (multiplied by custom value). Frames A and B are multiplied (+alpha correction).")]
			Transition_Custom_Normalized = 4,
			[Tooltip("Overlap: Draws new frame over old frame.")]
            Overlap = 5
        }

        public enum PostProcessMethod
        {
            Off = 0,                // no post effects
            [Tooltip("Bad for loops (causing glow effect in transition frames).")]
            OnInputTexture = 1,   // bad for transitions ( except advanced transition xD )
            [Tooltip("Good for loops.\n\nRequires an AdditionalRenderer and Actors objects.")]
            OnOutputTexture = 2     // good for transitions // apply post effect after transition is done
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
        public bool isLoop { get => loopTransitionMethod != LoopMethod.Off && _transitionFrames > 1; }
        public bool cameraPostProcessing { get => _camera.GetUniversalAdditionalCameraData().renderPostProcessing; set => _camera.GetUniversalAdditionalCameraData().renderPostProcessing = value; }
        public int calculateMaxTransitionFrames { get => Mathf.Max(2, Mathf.FloorToInt((frameRate * (float)duration) / 2f)); }

        /// <summary>
        /// Starts recorder.
        /// </summary>
        public void StartRecording()
        {
			if (_stage != CaptureStage.NotStarted)
                return;

            if (!SystemInfo.SupportsRenderTextureFormat(renderTexture.format))
            {
                Debug.LogError(renderTexture.format.ToString() + " is not supported in this system.");
				enabled = false;
                return;
            }

            _started = true;
			_stage = CaptureStage.Prepare;

			_startTime = DateTime.Now;

			_screenWidth = Screen.width;
            _screenHeight = Screen.height;

			CreateOutputFolder();

            _defaultCaptureFramerate = Time.captureFramerate;
            _camera = gameObject.GetComponentInChildren<Camera>(true);
            _camera.targetTexture = renderTexture;
            Time.captureFramerate = frameRate;

            // correct start settings if needed 
            if (frameRate % 2 > 0)
                frameRate = Mathf.FloorToInt(frameRate + 1);

            _transitionFrames = loopTransitionFrames;

            if (_transitionFrames < 2)
                _transitionFrames = 2;

            if (_transitionFrames > calculateMaxTransitionFrames)
                _transitionFrames = calculateMaxTransitionFrames;

            if (_transitionFrames % 2 > 0)
                _transitionFrames = Mathf.FloorToInt(_transitionFrames + 1);

            _totalFrames = frameRate * duration + (isLoop ? _transitionFrames : 0);
            _outputFrames = _totalFrames - _transitionFrames;

            // init output array
            _output = new Texture2D[_totalFrames];

            // init tmp textures (TextureFormat.RGB24 alpha not needed)
            _alphaTest_Black = new Texture2D(renderTexture.width, renderTexture.height, renderTexture.graphicsFormat, 0, TextureCreationFlags.None);
            _alphaTest_White = new Texture2D(renderTexture.width, renderTexture.height, renderTexture.graphicsFormat, 0, TextureCreationFlags.None);
            _alphaTest_Red = new Texture2D(renderTexture.width, renderTexture.height, renderTexture.graphicsFormat, 0, TextureCreationFlags.None);
            _alphaTest_Green = new Texture2D(renderTexture.width, renderTexture.height, renderTexture.graphicsFormat, 0, TextureCreationFlags.None);
            _alphaTest_Blue = new Texture2D(renderTexture.width, renderTexture.height, renderTexture.graphicsFormat, 0, TextureCreationFlags.None);

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

			// start
			StartCoroutine(CaptureFrame());
		}

        private void RenderMaskTexture()
        {
            if (maskMethod != MaskMethod.Off && maskRenderer != null && _maskTexture == null)
            {
                if (actors != null)
                    actors.SetActive(false);

                maskRenderer.gameObject.SetActive(true);

                _maskTexture = new Texture2D(renderTexture.width, renderTexture.height, renderTexture.graphicsFormat, 0, TextureCreationFlags.None);
                _maskTexture = CaptureTexture(_camera, Color.black, false);

				renderTexture.Release();

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
                    if (Input.anyKeyDown || captureAutoStart && captureDelay <= Time.timeSinceLevelLoad)
                    {
						StartRecording();
					}
                }
            }
            else if (_stage == CaptureStage.NotStarted)
                gameObject.SetActive(false);
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
                    AddOutputFrame(_currentFrame);
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

			_stage = CaptureStage.Done;
            enabled = false;

			// try loop
			Output_ConvertToLoop();
	
			// try apply post effects
			Output_ApplyPostEffects();

			// try apply mask
			Output_ApplyMask();

			// release mask texture
			if (_maskTexture != null)
				Destroy(_maskTexture);

			// release final array textures
			if (_final_array != null)
			{
				for (int i = 0; i < _final_array.Length; i++)
				{
					if(_final_array[i] != null)
						Destroy(_final_array[i]);
				}
			}

			// save files
			ExportOutputToFiles();

			// final utilities
			DateTime endTime = DateTime.Now;
			TimeSpan span = endTime.Subtract(_startTime);
			Debug.Log("Complete in " + span.Seconds.ToString() + "s, " + (isLoop ? _currentFrame - _transitionFrames : _currentFrame) + " video frames rendered" + (isLoop ? ", " + _transitionFrames + " transition frames used." : "."));

            // stop player
            EditorApplication.isPlaying = false;

            // open folder
            if (showInFileExplorer)
                OpenFolder(folderPath);
		}

		public static void OpenFolder(string path)
        {
            Application.OpenURL("file:\\" + path);
            //EditorUtility.RevealInFinder(foler); // this one opens a parent folder
        }

        // Fills current camera screen rect.
        private void PostProcessRenderer_FitFullScreen()
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
		private Texture2D CaptureTexture(Camera camera, Color backgroundColor, bool postProcess)
        {
            if (renderTexture == null)
                Debug.LogError("render tex is null");

            camera.backgroundColor = backgroundColor;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = postProcess;

			RenderTexture rt = CloneRenderTexture(renderTexture);
            RenderTexture.active = rt;
            Texture2D texture = new Texture2D(rt.width, rt.height, renderTexture.graphicsFormat, 0, TextureCreationFlags.None);

            camera.targetTexture = rt;
            camera.Render(); // also applies the post-process if any enabled

            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); // read from current RenderTexture
            texture.Apply();

            camera.targetTexture = null;
            RenderTexture.active = null;
            Destroy(rt);

            return texture;
        }

        private RenderTexture CloneRenderTexture(RenderTexture renderTexture)
        {
            RenderTexture rt = new RenderTexture(renderTexture.width, renderTexture.height, renderTexture.depth, renderTexture.graphicsFormat, renderTexture.mipmapCount);
            rt.format = renderTexture.format;
            rt.graphicsFormat = renderTexture.graphicsFormat;
            rt.depthStencilFormat = renderTexture.depthStencilFormat;
            rt.wrapMode = renderTexture.wrapMode;
            rt.useDynamicScale = renderTexture.useDynamicScale;
            rt.useMipMap = renderTexture.useMipMap;
            rt.filterMode = renderTexture.filterMode;
            rt.antiAliasing = renderTexture.antiAliasing;

            // Debug.LogError(rt.format.ToString() + ", " + rt.graphicsFormat.ToString() + ", " + rt.depthStencilFormat.ToString());

            return rt;
        }

		/// <summary>
		/// Saves game screen with diffrent background colors (black, white, red, green, blue).
		/// Transparency will be calculated from these backgrounds in later phase.
		/// </summary>
        private void CaptureScreen(bool render_post_effects, bool end = false)
        {
			//FX_SetAlphaTo1 fx_alpha1 = new(); // alpha was set to 0 bcs RGBA channel was converted to ARGB, now it is not needed bcs we not convert from Color32 <-> Color anymore (operating on Color only now)
 
			if (captureBackground == CaptureBackground.Transparent)
            {
                // rgb background test
                //  post process affects black and white backgrounds (example: bloom glows)
                //  so if object has dark color [ (r || g || b) < 1f ] it is recognized as transparent
                //  additional tests fix that problem
                if (rgbTest && postProcessMethod != PostProcessMethod.Off)
                {
                    _alphaTest_Red = /*fx_alpha1.Process*/(CaptureTexture(_camera, Color.red, false));
					//SavePng(_alphaTest_White, 100002);
					_alphaTest_Green = /*fx_alpha1.Process*/(CaptureTexture(_camera, Color.green, false));
					//SavePng(_alphaTest_White, 100003);
					_alphaTest_Blue = /*fx_alpha1.Process*/(CaptureTexture(_camera, Color.blue, false));
					//SavePng(_alphaTest_White, 100004);
				}

				_alphaTest_White = /*fx_alpha1.Process*/(CaptureTexture(_camera, Color.white, render_post_effects));
				//SavePng(_alphaTest_White, 100001);
			}

			_alphaTest_Black = /*fx_alpha1.Process*/(CaptureTexture(_camera, Color.black, render_post_effects));
			//SavePng(_alphaTest_Black, 100000);
		}
        
        private Texture2D CalculateTransparentScreen(bool apply_mask, bool post_effects, bool end)
        {
            // generates colored backgound
            CaptureScreen(post_effects, end); // captures taxtures with diffrent background colors and save it in: _alphaTestBlack, _alphaTestWhite, ... variables.

            // MANUAL MODE
            bool rgb_test = rgbTest && postProcessMethod != PostProcessMethod.Off;
            Texture2D texture = new Texture2D(renderTexture.width, renderTexture.height, renderTexture.graphicsFormat, 0, TextureCreationFlags.None);

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
				// black/white/rgb test
				if (rgb_test && postProcessMethod != PostProcessMethod.Off) // rgb is not needed if post-process is off
				{
					FX_TransparentBackground_RGBTest fx_transparentBackground_RGB = new();
					texture = fx_transparentBackground_RGB.Process(_alphaTest_Black, _alphaTest_White, alphaSmooth, alphaSmoothLimit, alphaSmoothDamp, _alphaTest_Red, _alphaTest_Green, _alphaTest_Blue);
				}
				// black/white test
				else
				{
					FX_TransparentBackground fx_transparentBackground = new();
					texture = fx_transparentBackground.Process(_alphaTest_Black, _alphaTest_White, alphaSmooth, alphaSmoothLimit, alphaSmoothDamp);
				}

				if (apply_mask)
				{
					FX_Mask fx_mask = new();
					texture = fx_mask.Process(texture, _maskTexture);
				}
            }

			// clear temporary textures
			if (_alphaTest_Black != null)
				Destroy(_alphaTest_Black);
			if (_alphaTest_White != null)
				Destroy(_alphaTest_White);
			if (_alphaTest_Red != null)
				Destroy(_alphaTest_Red);
			if (_alphaTest_Green != null)
				Destroy(_alphaTest_Green);
			if (_alphaTest_Blue != null)
				Destroy(_alphaTest_Blue);

			// texture with transparent background
			return texture;
        }

        private Texture2D ApplyMaskToTexture(Texture2D texture)
        {
			FX_Mask fx_mask = new();
			return fx_mask.Process(texture, _maskTexture);
		}

        private void AddOutputFrame(int frame)
        {
            // mask can be applied on input frame or on end by Output_ApplyMask()
            bool apply_mask = maskMethod == MaskMethod.OnInputTexture && _maskTexture != null;
            bool post_effects = postProcessMethod == PostProcessMethod.OnInputTexture;
            bool end = false;

			//if(debug && debug_frameLabel != null)
				//debug_frameLabel.text = "Frame: " + frame.ToString("D2");

			// get texture from captured screen
			_output[frame] = CalculateTransparentScreen(apply_mask, post_effects, end);

            Debug.Log("Frame " + frame.ToString() + " captured.");
        }

        private void CreateTranstitionArrays()
        {
            // 0. SETTINGS:                     frameRate = 30      loopTransitionFrames = 10
            //
            // 1. CAPTURED INPUT FRAMES:        |00|01|02|03|04|05|06|07|08|09|10|11|12|13|14|15|16|17|18|19|20|21|22|23|24|25|26|27|28|29|30|31|32|33|34|35|36|37|38|39|
            // 
            // 2. COPY TO BUFFER ARRAYS A & B:
            //
            //                     BUFFER_A:    |00|01|02|03|04|05|06|07|08|09|10|11|12|13|14|15|16|17|18|19|                             |------- transition b --------|
            //                     BUFFER_B:    |------- transition a --------|                             |20|21|22|23|24|25|26|27|28|29|30|31|32|33|34|35|36|37|38|39|
            // 
            // 3. EXPORT TO FILES / OUTPUT ARRAY (LOOPED OUTPUT):
            //
            //                   FINAL ARRAY:   |20|21|22|23|24|25|26|27|28|29|30|31|32|33|34|35|36|37|38|39|    
            //                                                                |00|01|02|03|04|05|06|07|08|09|10|11|12|13|14|15|16|17|18|19|
            //                                                                |------- overlay A on B ------|

            _array_a = new Texture2D[_totalFrames / 2]; // frames + transition A
            _array_b = new Texture2D[_totalFrames / 2]; // frames + transition B

            int array_a_size = _array_a.Length;
            int array_b_size = _array_b.Length + _array_a.Length;

            for (int frame = 0; frame < _output.Length; frame++)
            {
                // buffer frames
                if (frame < array_a_size)
                {
					_array_a[frame] = _output[frame];
                }
                else if (frame < array_b_size)
                {
					_array_b[frame - _array_a.Length] = _output[frame];
                }
                else
                    Debug.LogError("Frame " + frame + " skipped during transition process!");
            }
        }

		private void Output_ConvertToLoop()
		{
			if (!isLoop)
				return;

			// creates two arrays for transition
			CreateTranstitionArrays();

			_final_array = new Texture2D[_outputFrames];

			// current frame
			int frame = 0;

			// first array part, before transition frames (copy as is from array B)
			for (int i = 0; i < _array_b.Length - _transitionFrames; i++)
			{
				_final_array[frame] = _array_b[i];
				frame++;
			}

			// transition frames
			if (debug)
				Debug.LogError("Transition frame's alpha(x, y) for frames (A, B):");

			for (int i = 0; i < _transitionFrames; i++)
			{
				if (loopTransitionMethod == LoopMethod.Transition_Standard)
				{
					FX_TransitionStandard FX_Transition = new();
					_final_array[frame] = FX_Transition.Process(_array_a[i], _array_b[_array_b.Length - _transitionFrames + i], i, _transitionFrames, debug);
				}
				else if (loopTransitionMethod == LoopMethod.Transition_Standard_Normalized)
				{
					FX_TransitionStandardNormalized FX_Transition = new();
					_final_array[frame] = FX_Transition.Process(_array_a[i], _array_b[_array_b.Length - _transitionFrames + i], i, _transitionFrames, debug);
				}
				else if (loopTransitionMethod == LoopMethod.Transition_Custom)
				{
					FX_TransitionCustom FX_Transition = new();
					_final_array[frame] = FX_Transition.Process(_array_a[i], _array_b[_array_b.Length - _transitionFrames + i], i, _transitionFrames, transitionMultiplier, debug);
				}
				else if (loopTransitionMethod == LoopMethod.Transition_Custom_Normalized)
				{
					FX_TransitionCustomNormalized FX_Transition = new();
					_final_array[frame] = FX_Transition.Process(_array_a[i], _array_b[_array_b.Length - _transitionFrames + i], i, _transitionFrames, transitionMultiplier, debug);
				}
				else if (loopTransitionMethod == LoopMethod.Overlap)
				{
					FX_TransitionOverlap FX_Transition = new();
					_final_array[frame] = FX_Transition.Process(_array_a[i], _array_b[_array_b.Length - _transitionFrames + i], i, _transitionFrames, debug);
				}
				
				frame++;
			}
			
			// last array part, after transition frames (copy as is from array A)
			for (int i = _transitionFrames; i < _array_a.Length; i++)  // save non-transition frames
			{
				_final_array[frame] = _array_a[i];
				frame++;
			}

			// release old textures
			{
				for (int i = 0; i < _output.Length; i++)
				{
					Destroy(_output[i]);
				}

				for (int i = 0; i < _array_a.Length; i++)
				{
					Destroy(_array_a[i]);
				}

				for (int i = 0; i < _array_b.Length; i++)
				{
					Destroy(_array_b[i]);
				}
			}

			// override old output
			_output = _final_array;
		}

        private void Output_ApplyPostEffects()
        {
            // 1. activate post process on camera, activate texture renderer
            // 2. render output textures on camera and re-capture with post effects
            // 3. calculate transparent blackground from black/white frames and save to the file

            if (postProcessMethod == PostProcessMethod.OnOutputTexture)
            {
                if (actors != null)
                    actors.SetActive(false);

                if (postProcessRenderer != null)
                    postProcessRenderer.gameObject.SetActive(true);

                cameraPostProcessing = true;

                for (int i = 0; i < _output.Length; i++)
                {
                    if (_output[i] != null)
                    {
                        // activate plane
                        _output[i].Apply(); // this contains non-post-effect-texture
                        postProcessRenderer.material.mainTexture = _output[i];

						_output[i] = CalculateTransparentScreen((maskMethod == MaskMethod.OnOutputTexture) && _maskTexture != null, true, true); // capture and combine with post-effects
                    }
                    else
                        Debug.LogError("output[" + i + "] is null!");
                }
            }
        }

        private void Output_ApplyMask()
        {
            if (maskMethod == MaskMethod.OnOutputTexture && _maskTexture != null)
            {
                for (int i = 0; i < _output.Length; i++)
                {
                    if (_output[i] != null)
                    {
						_output[i] = ApplyMaskToTexture(_output[i]);
                    }
                    else
                        Debug.LogError("output[" + i + "] is null!");
                }
            }
        }

        private void ExportOutputToFiles()
        {
            if (_output != null)
            {
                for (int i = 0; i < _output.Length; i++)
                {
					if (convertLinearToGamma && PlayerSettings.colorSpace == ColorSpace.Linear)
					{
						FX_LinearToGamma fx_linearToGamma = new();
						_output[i] = fx_linearToGamma.Process(_output[i]);
					}

                    SavePng(_output[i], i);
                }
            }
        }

        private void CreateOutputFolder()
        {
            string path = folderPath;
            _fullPath = folderPath;

            if (!overrideFolderContent)
            {
                int count = 1;

                while (Directory.Exists(_fullPath))
                {
                    _fullPath = path + " " + count;
                    count++;
                }
            }
			else if(Directory.Exists(folderPath))
				Directory.Delete(_fullPath, true);

			System.IO.Directory.CreateDirectory(_fullPath);
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

            Debug.Log("Frame " + frame + " exported to " + name);
        }

		private void OnValidate()
		{
			if (convertLinearToGamma && PlayerSettings.colorSpace != ColorSpace.Linear)
			{
				convertLinearToGamma = false;
			}
		}
		#endregion

		#region EDITOR UI
		// EDITOR UI :::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::
		[CustomEditor(typeof(Unity2PNG))]
        [CanEditMultipleObjects]
        public class Unity2PNGEditor : Editor
        {
            public override void OnInspectorGUI()
            {
                //DrawDefaultInspector();

                //Game_GUI.enabled = false;
                //EditorGUILayout.ObjectField("Script:", MonoScript.FromMonoBehaviour((Unity2PNG)target), typeof(Unity2PNG), false);
                //Game_GUI.enabled = true;

                if (Application.isPlaying)
                    GUI.enabled = false;

                GUILayout.Space(10);

                Unity2PNG component = (Unity2PNG)target;

                // start change check
                EditorGUI.BeginChangeCheck();

                Undo.RecordObject(component, "Change Values");

                GUILayout.Label("REQUIRED COMPONENTS", EditorStyles.boldLabel);

                GUILayout.BeginVertical("helpbox");
                {
                    component.renderTexture = (RenderTexture)EditorGUILayout.ObjectField("Render Texture", component.renderTexture, typeof(RenderTexture), false, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    int x = 0;
                    int y = 0;
                    string colorF = "";

                    if (component.renderTexture != null)
                    {
                        x = component.renderTexture.width;
                        y = component.renderTexture.height;
                        colorF = component.renderTexture.graphicsFormat.ToString();
                    }

                    GUILayout.Label("Resolution: " + x + " x " + y + "\nColor Format: " + colorF, EditorStyles.wordWrappedLabel);
                    GUILayout.Label("Remember to choose a render texture with a proper color space.\nYour current color space in project is set to: " + PlayerSettings.colorSpace.ToString(), EditorStyles.wordWrappedLabel);
                    GUILayout.Label("You can change color space in 'ProjectSettings -> Player -> Other Settings -> Color Space'", EditorStyles.wordWrappedLabel);
                }
                GUILayout.EndVertical();

                GUILayout.Space(10);

                GUILayout.Label("CAPTURE", EditorStyles.boldLabel);

                // output details
                GUILayout.BeginVertical("helpbox");
                {
                    int transitionFrames = component.loopTransitionFrames;

                    if (transitionFrames < 2)
                        transitionFrames = 2;

                    if (transitionFrames > component.calculateMaxTransitionFrames)
                        transitionFrames = component.calculateMaxTransitionFrames;

                    if (transitionFrames % 2 > 0)
                        transitionFrames = Mathf.FloorToInt(transitionFrames + 1);

                    int totalFrames = component.frameRate * component.duration + (component.isLoop ? transitionFrames : 0);
                    //int outputFrames = totalFrames - transitionFrames;

                    if(component.loopTransitionMethod == LoopMethod.Off)
                        GUILayout.Label("Output frames: " + totalFrames, EditorStyles.wordWrappedLabel);
                    else
                        GUILayout.Label("Output frames: " + totalFrames + ", loop transition frames: " + transitionFrames, EditorStyles.wordWrappedLabel);
                }
                GUILayout.EndVertical();

                GUILayout.BeginVertical();
                {
                    GUILayout.BeginVertical("helpbox");
                    {
                        component.frameRate = EditorGUILayout.IntSlider("Frame Rate", component.frameRate, 6, 60);
                        component.duration = EditorGUILayout.IntSlider("Duration", component.duration, 1, 10);
                    }
                    GUILayout.EndVertical();

                    GUILayout.BeginVertical("helpbox");
                    {
                        component.captureBackground = (CaptureBackground)EditorGUILayout.EnumPopup("Background", component.captureBackground);
                    }
                    GUILayout.EndVertical();

                    GUILayout.BeginVertical("helpbox");
                    {
                        component.loopTransitionMethod = (LoopMethod)EditorGUILayout.EnumPopup("Loop", component.loopTransitionMethod);

                        if (component.loopTransitionMethod != LoopMethod.Off)
                        {
                            int max_tf = component.calculateMaxTransitionFrames;
                            if (max_tf % 2 > 0)
                                max_tf++;

                            component.loopTransitionFrames = EditorGUILayout.IntSlider("Transition Frames", component.loopTransitionFrames, 2, max_tf);

                            if (component.loopTransitionFrames % 2 > 0)
                                component.loopTransitionFrames++;

                            if (component.loopTransitionFrames > 0)
                            {
                                if (component.loopTransitionMethod == LoopMethod.Transition_Custom || component.loopTransitionMethod == LoopMethod.Transition_Custom_Normalized)
                                {
                                    component.transitionMultiplier = (EditorGUILayout.Slider(
                                    new GUIContent("Transition Alpha Boost", "Increase this value if transition alpha is too low in the middle frame."),
                                    component.transitionMultiplier, 0.5f, 1.5f));
                                }
                            }
                        }
                    }
                    GUILayout.EndVertical();

                    GUILayout.BeginVertical("helpbox");
                    {
                        component.postProcessMethod = (PostProcessMethod)EditorGUILayout.EnumPopup("Post Process", component.postProcessMethod);

                        if (component.postProcessMethod == PostProcessMethod.OnOutputTexture)
                        {
                            component.actors = (GameObject)EditorGUILayout.ObjectField("Actors", component.actors, typeof(GameObject), true);
                            component.postProcessRenderer = (Renderer)EditorGUILayout.ObjectField("Post Process Renderer", component.postProcessRenderer, typeof(Renderer), true);

                            GUILayout.BeginHorizontal();
                            {
                                component.postProcessRenderer_FitFullScreen = EditorGUILayout.Toggle(new GUIContent("PPR Fit To Screen On Start", "Automatically sets the 'PostProcessRenderer' object to be fully visible by the camera."), component.postProcessRenderer_FitFullScreen);

                                GUILayout.FlexibleSpace();

                                if (GUILayout.Button("Fit Post Process Renderer To Screen"))
                                {
                                    component.PostProcessRenderer_FitFullScreen();

                                    if (component.postProcessRenderer != null)
                                        EditorUtility.SetDirty(component.postProcessRenderer);

                                    EditorUtility.SetDirty(component);
                                    EditorUtility.SetDirty(component.gameObject);
                                }
                            }
                            GUILayout.EndHorizontal();
                        }

                        if (component.postProcessMethod != PostProcessMethod.Off)
                            component.rgbTest = EditorGUILayout.Toggle(new GUIContent("+RGB Alpha Test", "Additional alpha tests (takes 2.5x more time for calculations).\n\n" +
								"Check the Video Tutorial to understand this.\n\n" +
								"Fixes glitch where pixels with rgb values less than 1.0 are captured with alpha less than 1.0 but alpha should be 1.0, fixes alpha for objects: black, grey, dark color like (0.5, 0.0, 0.0), etc.\n\n" +
								"NOTE: This is designed to fix Bloom, HDR issues mostly."), component.rgbTest);

                        if (component.postProcessMethod == PostProcessMethod.OnInputTexture && component.loopTransitionMethod != LoopMethod.Off)
                            GUILayout.Label("LIMITATION: A 'glow effect' can appear in loop transition.", EditorStyles.wordWrappedLabel);
                        if (component.postProcessMethod == PostProcessMethod.OnOutputTexture || component.postProcessMethod == PostProcessMethod.OnInputTexture)
                            GUILayout.Label("LIMITATION: Post-Process (Bloom/Glow) can cause transparency problems.", EditorStyles.wordWrappedLabel);
                        if (component.postProcessMethod == PostProcessMethod.OnOutputTexture)
                            GUILayout.Label("LIMITATION: HDR looks bad in re-capture mode.", EditorStyles.wordWrappedLabel);
                    }
                    GUILayout.EndVertical();

                    GUILayout.BeginVertical("helpbox");
                    {
                        component.maskMethod = (MaskMethod)EditorGUILayout.EnumPopup("Mask", component.maskMethod);

                        if (component.maskMethod != MaskMethod.Off)
                        {
                            component.maskRenderer = (Renderer)EditorGUILayout.ObjectField("Mask Renderer", component.maskRenderer, typeof(Renderer), true);
                            //GUILayout.Label("\nNOTE: Set a mask to be visible at the camera all black pixels will be ignored in output.", EditorStyles.wordWrappedLabel);
                        }
                    }
                    GUILayout.EndVertical();

                    // alpha smooth
                    GUILayout.BeginVertical("helpbox");
                    {
                        component.alphaSmooth = EditorGUILayout.Toggle(new GUIContent("Alpha Smooth", "Smooths alpha in low alpha places (reduces outer alpha in glowing effects etc.)."), component.alphaSmooth);

                        if (component.alphaSmooth)
                        {
                            component.alphaSmoothLimit = EditorGUILayout.Slider(new GUIContent("Alpha Smooth Start", "When color's alpha (range: 0f, 1f) is below this value then smooth is applied.\n\nDefault (0.2)"), component.alphaSmoothLimit, 0.1f, 0.5f);
                            component.alphaSmoothDamp = EditorGUILayout.Slider(new GUIContent("Alpha Smooth Power", "Higher value removes more almost visible pixels (reduces alpha).\n\nDefault (0.4)"), component.alphaSmoothDamp, 0.1f, 1f);
                            //GUILayout.Label("\nNOTE: It will remove pixels with very low alpha.", EditorStyles.wordWrappedLabel);
                        }
                    }
                    GUILayout.EndVertical();

                    // color space conversion (only if linear is set in player settings)
                    bool guiEnabled = GUI.enabled;
                    if (PlayerSettings.colorSpace != ColorSpace.Linear)
                        GUI.enabled = false;

                    GUILayout.BeginVertical("helpbox");
                    {
                        component.convertLinearToGamma = EditorGUILayout.Toggle(new GUIContent("Convert Linear To Gamma", "Converts color space from Linear to Gamma if the 'ProjectSettings -> Player -> Other Settings -> Color Space' is 'Linear'."), component.convertLinearToGamma);
                    }
                    GUILayout.EndVertical();
                    GUI.enabled = guiEnabled;

					GUILayout.BeginVertical("helpbox");
					{
						//guiEnabled = GUI.enabled;
						component.debug = EditorGUILayout.Toggle(new GUIContent("Debug", "Shows debug messages."), component.debug);

						//if (!component.debug)
							//GUI.enabled = false;

						//component.debug_frameLabel = (Text)EditorGUILayout.ObjectField(new GUIContent("Debug Frame Text", "Renders current frame for debug purposes. Can be null if not used."), component.debug_frameLabel, typeof(Text), true);
						//GUI.enabled = guiEnabled;
					}
					GUILayout.EndVertical();

				

					// procedure
					GUILayout.Space(20);
                    GUILayout.BeginVertical("helpbox");
                    {
                        string procedure = "CAPTURE";

                        if (component.loopTransitionMethod != LoopMethod.Off)
                            procedure += " + LOOP";

                        if (component.postProcessMethod == PostProcessMethod.OnInputTexture)
                            procedure += " + POST PROCESS (INPUT)";

                        if (component.maskMethod == MaskMethod.OnOutputTexture && component.postProcessMethod != PostProcessMethod.OnOutputTexture)
                            procedure += " + MASK (OUTPUT)";

                        if (component.maskMethod == MaskMethod.OnInputTexture)
                        {
                            if (component.captureBackground == CaptureBackground.Opaque && component.postProcessMethod == PostProcessMethod.OnOutputTexture)
                                procedure += " + MASK (IGNORED)";
                            else
                                procedure += " + MASK (INPUT)";
                        }

                        if (component.postProcessMethod == PostProcessMethod.OnOutputTexture)
                            procedure += " + POST PROCESS (OUTPUT)";

                        if (component.maskMethod == MaskMethod.OnOutputTexture && component.postProcessMethod == PostProcessMethod.OnOutputTexture)
                            procedure += " + MASK (OUTPUT)";

                        GUILayout.Label(procedure, EditorStyles.wordWrappedLabel);
                    }
                    GUILayout.EndVertical();
                }
                GUILayout.EndVertical();

                GUILayout.Space(20);

                GUILayout.Label("FILES", EditorStyles.boldLabel);
                // output details
                GUILayout.BeginVertical("helpbox");
                {
                    string path_preview = component.folderPath + "\\" + component.fileName;

                    for (int i = 0; i < component.digits; i++)
                    {
                        path_preview += "0";
                    }

                    GUILayout.Label("First file: " + path_preview + ".png", EditorStyles.wordWrappedLabel);
                }
                GUILayout.EndVertical();

                GUILayout.BeginVertical("helpbox");
                {
                    GUILayout.BeginHorizontal();
                    {
                        component.folderPath = EditorGUILayout.TextField("Folder Path", component.folderPath);

                        if (GUILayout.Button("Open", GUILayout.Width(60)))
                        {
                            OpenFolder(component.folderPath + "\\");
                        }
                    }
                    GUILayout.EndHorizontal();

                    component.fileName = EditorGUILayout.TextField("File Name", component.fileName);
                    component.digits = EditorGUILayout.IntSlider("Digits", component.digits, 1, 3);
                    component.overrideFolderContent = EditorGUILayout.Toggle(new GUIContent("Override Folder", "All subfolders and files inside the folder will be deleted."), component.overrideFolderContent);
                    component.showInFileExplorer = EditorGUILayout.Toggle("Open Folder On End", component.showInFileExplorer);
                }
                GUILayout.EndVertical();

                GUILayout.BeginVertical("helpbox");
                {
                    component.captureAutoStart = EditorGUILayout.Toggle("Auto Start", component.captureAutoStart);
                    GUI.enabled = component.captureAutoStart && !Application.isPlaying;
                    component.captureDelay = EditorGUILayout.Slider("Start Capture After", component.captureDelay, 0f, 10f);
                    if(!GUI.enabled)
                        GUI.enabled = !Application.isPlaying;
                }
                GUILayout.EndVertical();

                // end change check
                if (EditorGUI.EndChangeCheck())
                {
                    if (component.frameRate % 2 > 0)
                        component.frameRate++;

                    // round to 0.01
                    float mod100 = component.transitionMultiplier % 0.01f;

                    if (mod100 > 0f)
                        component.transitionMultiplier = Mathf.Clamp(component.transitionMultiplier - mod100, 0f, 2f);

                    Undo.RecordObject(component, "Change Values");

                    EditorUtility.SetDirty(component);
                    EditorUtility.SetDirty(component.gameObject);
                }

                // CONTROL
                GUILayout.Space(10);

                GUILayout.BeginVertical("helpbox");
                {
                    GUILayout.Space(10);

                    GUILayout.BeginHorizontal();
                    {
                        GUILayout.FlexibleSpace();
                        GUILayout.Label("UNITY 2 PNG " + VERSION, EditorStyles.wordWrappedLabel);
                        GUILayout.FlexibleSpace();
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.Space(10);

                    GUILayout.BeginHorizontal();
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("START", GUILayout.Width(120)))
                        {
                            EditorApplication.EnterPlaymode();
                        }
                        GUILayout.FlexibleSpace();
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.Space(10);
                    GUILayout.BeginHorizontal();
                    {
                        GUILayout.FlexibleSpace();
                        GUILayout.Label("( press any key in play mode to start )", EditorStyles.wordWrappedLabel);
                        GUILayout.FlexibleSpace();
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.Space(10);
                }
                GUILayout.EndVertical();

                GUILayout.BeginVertical("helpbox");
                {
                    GUILayout.Space(10);

                    GUILayout.BeginHorizontal();
                    {
                        GUILayout.FlexibleSpace();
                
                        if (GUILayout.Button("Video Tutorial", GUILayout.Width(120)))
                        {
                            Application.OpenURL(VIDEO_TUTORIAL);
						}

                        GUILayout.FlexibleSpace();
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.Space(10);
                }
                GUILayout.EndVertical();

                GUI.enabled = true;
            }
        }
        // EDITOR UI :::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::
        #endregion
    }
}
#endif