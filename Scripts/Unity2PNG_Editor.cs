/*
    Author: Matthew Mazan (Masterio)
    Permitted: Free to personal or commercial use.
    Forbidden: Re-Sell/Re-Share this package.
    Compatibility: Unity 2021.3.0+, URP
*/
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Unity2Png
{
	[CustomEditor(typeof(Unity2PNG))]
	[CanEditMultipleObjects]
	public class Unity2PNG_Editor : Editor
	{
		private const string PREF_SHOW_PREVIEW = "Unity2PNG_ShowInspectorPreview";
		private const string PREF_SHOW_FILES_OUTPUT = "Unity2PNG_ShowFilesOutput";

		private bool _showPreview = true;
		private bool _showFilesOutput = false;

		private Unity2PNG_Player _player = new Unity2PNG_Player();
		private bool _wasPlayingInEditor = false;
		private bool _wasRecording = false;

		// SerializedProperties for Multi-Object Editing
		private SerializedProperty _renderTextureProp;
		private SerializedProperty _resolutionPresetProp;
		private SerializedProperty _frameRateProp;
		private SerializedProperty _durationProp;
		private SerializedProperty _captureBackgroundProp;
		private SerializedProperty _loopTransitionMethodProp;
		private SerializedProperty _loopTransitionFramesProp;
		private SerializedProperty _postProcessMethodProp;
		private SerializedProperty _rgbTestProp;
		private SerializedProperty _actorsProp;
		private SerializedProperty _postProcessRendererProp;
		private SerializedProperty _postProcessRendererFitProp;
		private SerializedProperty _maskMethodProp;
		private SerializedProperty _maskRendererProp;
		private SerializedProperty _alphaSmoothProp;
		private SerializedProperty _alphaSmoothLimitProp;
		private SerializedProperty _alphaSmoothDampProp;
		private SerializedProperty _convertLinearToGammaProp;
		private SerializedProperty _debugProp;
		private SerializedProperty _folderPathProp;
		private SerializedProperty _fileNameProp;
		private SerializedProperty _digitsProp;
		private SerializedProperty _overrideFolderProp;
		private SerializedProperty _showInExplorerProp;
		private SerializedProperty _captureAutoStartProp;
		private SerializedProperty _captureDelayProp;
		private SerializedProperty _appendObjectNameProp;


		private void OnEnable()
		{
			EditorApplication.update += OnEditorUpdate;
			_showPreview = EditorPrefs.GetBool(PREF_SHOW_PREVIEW, true);
			_showFilesOutput = EditorPrefs.GetBool(PREF_SHOW_FILES_OUTPUT, false);
			_renderTextureProp = serializedObject.FindProperty("renderTexture");
			_resolutionPresetProp = serializedObject.FindProperty("resolutionPreset");
			_frameRateProp = serializedObject.FindProperty("frameRate");
			_durationProp = serializedObject.FindProperty("duration");
			_captureBackgroundProp = serializedObject.FindProperty("captureBackground");
			_loopTransitionMethodProp = serializedObject.FindProperty("loopTransitionMethod");
			_loopTransitionFramesProp = serializedObject.FindProperty("loopTransitionFrames");
			_postProcessMethodProp = serializedObject.FindProperty("postProcessMethod");
			_rgbTestProp = serializedObject.FindProperty("rgbTest");
			_actorsProp = serializedObject.FindProperty("actors");
			_postProcessRendererProp = serializedObject.FindProperty("postProcessRenderer");
			_postProcessRendererFitProp = serializedObject.FindProperty("postProcessRenderer_FitFullScreen");
			_maskMethodProp = serializedObject.FindProperty("maskMethod");
			_maskRendererProp = serializedObject.FindProperty("maskRenderer");
			_alphaSmoothProp = serializedObject.FindProperty("alphaSmooth");
			_alphaSmoothLimitProp = serializedObject.FindProperty("alphaSmoothLimit");
			_alphaSmoothDampProp = serializedObject.FindProperty("alphaSmoothDamp");
			_convertLinearToGammaProp = serializedObject.FindProperty("convertLinearToGamma");
			_debugProp = serializedObject.FindProperty("debug");
			_folderPathProp = serializedObject.FindProperty("folderPath");
			_fileNameProp = serializedObject.FindProperty("fileName");
			_digitsProp = serializedObject.FindProperty("digits");
			_overrideFolderProp = serializedObject.FindProperty("overrideFolderContent");
			_showInExplorerProp = serializedObject.FindProperty("showInFileExplorer");
			_captureAutoStartProp = serializedObject.FindProperty("captureAutoStart");
			_captureDelayProp = serializedObject.FindProperty("captureDelay");
			_appendObjectNameProp = serializedObject.FindProperty("appendObjectNameToFolder");

			if (_showPreview && targets.Length == 1 && target != null)
				_player.LoadFrames((Unity2PNG)target, false);
		}

		private void OnDisable()
		{
			EditorApplication.update -= OnEditorUpdate;
			_player.Dispose();
		}

		private void OnEditorUpdate()
		{
			if (target == null)
				return;

			bool needsRepaint = false;

			if (_wasPlayingInEditor && !EditorApplication.isPlaying)
			{
				_wasPlayingInEditor = false;
				if (targets.Length == 1 && target != null)
				{
					_player.LoadFrames((Unity2PNG)target, true);
					needsRepaint = true;
				}
			}
			else if (EditorApplication.isPlaying)
			{
				_wasPlayingInEditor = true;
			}

			bool isRecordingNow = Unity2PNG.isAnyRecording;
			if (isRecordingNow != _wasRecording)
			{
				_wasRecording = isRecordingNow;
				needsRepaint = true;
			}

			if (_showPreview && targets.Length == 1 && _player.UpdatePlayback())
			{
				needsRepaint = true;
			}

			if (needsRepaint)
				Repaint();
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			if (Application.isPlaying)
				GUI.enabled = false;

			GUILayout.Space(10);

			Unity2PNG component = (Unity2PNG)target;
			bool isMultiEdit = targets.Length > 1;

			// RENDERER TEXTURE
			GUILayout.Label("RENDER TEXTURE", EditorStyles.boldLabel);
			GUILayout.BeginVertical("helpbox");
			{
				EditorGUI.BeginChangeCheck();
				EditorGUILayout.PropertyField(_renderTextureProp, new GUIContent(
					"Render Texture",
					"Target RenderTexture used by the camera to capture frames.\n\n" +
					"Required format: R32G32B32A32_SFloat (ARGBFloat)."
				));
				if (EditorGUI.EndChangeCheck())
				{
					serializedObject.ApplyModifiedProperties();
					foreach (Object obj in targets)
					{
						Unity2PNG c = obj as Unity2PNG;
						if (c != null && c.renderTexture != null)
						{
							Undo.RecordObject(c, "Sync RenderTexture Presets");
							c.SyncPresetsFromRenderTexture();
							EditorUtility.SetDirty(c);
						}
					}
					serializedObject.Update();
				}

				EditorGUILayout.PropertyField(_resolutionPresetProp, new GUIContent(
					"Resolution Override",
					"Overrides the RenderTexture dimensions during capture without modifying the asset on disk."
				));

				if (!isMultiEdit)
				{
					bool isInvalidFormat = component.renderTexture != null &&
										   component.renderTexture.graphicsFormat != UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32B32A32_SFloat;

					string resInfo = component.renderTexture != null
						? $"{component.effectiveWidth}x{component.effectiveHeight} ({component.renderTexture.graphicsFormat})"
						: "None (0x0)";

					if (isInvalidFormat)
					{
						EditorGUILayout.HelpBox(
							$"Unsupported RenderTexture format: {component.renderTexture.graphicsFormat}!\n" +
							"Unity2PNG requires 32-bit float (R32G32B32A32_SFloat / ARGBFloat) to prevent pixel glitches.",
							MessageType.Error
						);
					}
					else
					{
						EditorGUILayout.HelpBox(
							$"Texture: {resInfo}  |  Project Color Space: {PlayerSettings.colorSpace}\n" +
							"Ensure RenderTexture matches Project Settings > Player > Color Space.",
							component.renderTexture == null ? MessageType.Warning : MessageType.Info
						);
					}
				}
			}
			GUILayout.EndVertical();

			GUILayout.Space(10);

			// CAPTURE
			GUILayout.Label("CAPTURE", EditorStyles.boldLabel);

			// output details & estimated peak RAM usage
			if (!isMultiEdit)
			{
				DrawCaptureSummary(component);
			}

			GUILayout.BeginVertical();
			{
				GUILayout.BeginVertical("helpbox");
				{
					EditorGUILayout.IntSlider(_frameRateProp, 6, 60, new GUIContent("Frame Rate"));

					EditorGUI.BeginChangeCheck();
					float newDuration = EditorGUILayout.Slider(new GUIContent("Duration (s)"), _durationProp.floatValue, 0.1f, 10f);
					if (EditorGUI.EndChangeCheck())
					{
						_durationProp.floatValue = Mathf.Round(newDuration * 100f) / 100f;
					}
				}
				GUILayout.EndVertical();

				GUILayout.BeginVertical("helpbox");
				{
					EditorGUILayout.PropertyField(_loopTransitionMethodProp, new GUIContent("Loop"));

					if (!_loopTransitionMethodProp.hasMultipleDifferentValues &&
						(Unity2PNG.LoopMethod)_loopTransitionMethodProp.intValue != Unity2PNG.LoopMethod.Off)
					{
						int max_tf = component.calculateMaxTransitionFrames;
						if (max_tf % 2 > 0)
							max_tf++;

						EditorGUILayout.IntSlider(_loopTransitionFramesProp, 2, max_tf, new GUIContent("Transition Frames"));
					}
				}
				GUILayout.EndVertical();

				// POST PROCESS
				GUILayout.BeginVertical("helpbox");
				{
					EditorGUI.BeginChangeCheck();
					EditorGUILayout.PropertyField(_postProcessMethodProp, new GUIContent(
						"Post Process",
						"Selects when post-processing effects (such as Bloom or Color Grading) are applied:\n\n" +
						"• Off: Disables post-processing during capture.\n" +
						"• On Input Texture: Applies post-processing before the loop transition is generated.\n" +
						"• On Output Texture: Applies post-processing after the loop transition is generated (recommended for loops; requires Actors and Post Process Renderer)."
					));

					if (EditorGUI.EndChangeCheck() && !_postProcessMethodProp.hasMultipleDifferentValues)
					{
						Unity2PNG.PostProcessMethod newMethod = (Unity2PNG.PostProcessMethod)_postProcessMethodProp.enumValueIndex;
						if (newMethod != Unity2PNG.PostProcessMethod.OnOutputTexture)
						{
							foreach (Object obj in targets)
							{
								Unity2PNG c = obj as Unity2PNG;
								if (c != null && c.postProcessRenderer != null && c.postProcessRenderer.gameObject.activeSelf)
								{
									Undo.RecordObject(c.postProcessRenderer.gameObject, "Hide Post Process Renderer");
									c.postProcessRenderer.gameObject.SetActive(false);
									EditorUtility.SetDirty(c.postProcessRenderer.gameObject);
								}
							}
						}
					}

					if (!_postProcessMethodProp.hasMultipleDifferentValues &&
						(Unity2PNG.PostProcessMethod)_postProcessMethodProp.enumValueIndex == Unity2PNG.PostProcessMethod.OnOutputTexture)
					{
						EditorGUILayout.PropertyField(_actorsProp, new GUIContent(
							"Actors",
							"Root GameObject containing the visual VFX/scene objects to capture. Temporarily hidden during the post-process re-capture pass."
						));

						GUILayout.BeginHorizontal();
						{
							EditorGUILayout.PropertyField(_postProcessRendererProp, new GUIContent(
								"Post Process Renderer",
								"Quad or plane renderer used to project the blended frames back to the camera so post-processing (e.g., Bloom) can be applied on the output."
							));

							if (!isMultiEdit && component.postProcessRenderer != null)
							{
								GameObject pprObj = component.postProcessRenderer.gameObject;
								bool isPprActive = pprObj.activeSelf;

								Color prevBg = GUI.backgroundColor;
								GUI.backgroundColor = isPprActive ? new Color(0.35f, 0.9f, 0.45f) : prevBg;

								bool newPprActive = GUILayout.Toggle(isPprActive, new GUIContent(isPprActive ? "Visible" : "Hidden"), GUI.skin.button, GUILayout.Width(65f));
								GUI.backgroundColor = prevBg;

								if (newPprActive != isPprActive)
								{
									Undo.RecordObject(pprObj, "Toggle Post Process Renderer Visibility");
									pprObj.SetActive(newPprActive);
									EditorUtility.SetDirty(pprObj);
								}
							}
						}
						GUILayout.EndHorizontal();

						GUILayout.BeginHorizontal();
						{
							EditorGUI.showMixedValue = _postProcessRendererFitProp.hasMultipleDifferentValues;
							EditorGUI.BeginChangeCheck();
							bool fitVal = EditorGUILayout.ToggleLeft(new GUIContent(
									"PPR Fit To Screen On Start",
									"Automatically scales the Post Process Renderer to fill the entire camera view when recording starts."
								), _postProcessRendererFitProp.boolValue, GUILayout.Width(175f));
							if (EditorGUI.EndChangeCheck())
								_postProcessRendererFitProp.boolValue = fitVal;
							EditorGUI.showMixedValue = false;

							GUILayout.FlexibleSpace();
							if (GUILayout.Button(new GUIContent(
								"Fit To Screen",
								"Immediately scales the Post Process Renderer to match the camera's full screen bounds."
							)))
							{
								foreach (Object obj in targets)
								{
									Unity2PNG c = obj as Unity2PNG;
									if (c != null && c.postProcessRenderer != null)
									{
										Undo.RecordObject(c.postProcessRenderer.transform, "Fit Post Process Renderer To Screen");
										c.PostProcessRenderer_FitFullScreen();
										EditorUtility.SetDirty(c.postProcessRenderer.transform);
										EditorUtility.SetDirty(c.postProcessRenderer);
									}
								}
							}
						}
						GUILayout.EndHorizontal();
					}

					if (_postProcessMethodProp.hasMultipleDifferentValues ||
						(Unity2PNG.PostProcessMethod)_postProcessMethodProp.enumValueIndex != Unity2PNG.PostProcessMethod.Off)
					{
						GUILayout.Space(2);
						EditorGUI.showMixedValue = _rgbTestProp.hasMultipleDifferentValues;
						EditorGUI.BeginChangeCheck();
						bool rgbVal = EditorGUILayout.ToggleLeft(
							new GUIContent(
								"+RGB Alpha Test",
								"Additional RGB alpha check (takes ~2.5x longer to calculate).\n\n" +
								"Fixes artifacts where dark, black, or grey pixels (RGB < 1.0) are captured as semi-transparent instead of fully opaque.\n\n" +
								"Recommended when using Bloom or HDR effects."
							),
							_rgbTestProp.boolValue
						);
						if (EditorGUI.EndChangeCheck())
							_rgbTestProp.boolValue = rgbVal;
						EditorGUI.showMixedValue = false;
					}
				}
				GUILayout.EndVertical();

				// MASK
				GUILayout.BeginVertical("helpbox");
				{
					EditorGUI.BeginChangeCheck();

					EditorGUILayout.PropertyField(_maskMethodProp, new GUIContent(
						"Mask",
						"Selects how the alpha mask is applied:\n\n" +
						"• Off: Disables masking.\n" +
						"• On Input Texture: Masks each captured frame before loop blending and output post-processing.\n" +
						"• On Output Texture: Masks the final image after loop blending and post-processing."
					));

					if (EditorGUI.EndChangeCheck() && !_maskMethodProp.hasMultipleDifferentValues)
					{
						if ((Unity2PNG.MaskMethod)_maskMethodProp.enumValueIndex == Unity2PNG.MaskMethod.Off)
						{
							foreach (Object obj in targets)
							{
								Unity2PNG c = obj as Unity2PNG;
								if (c != null && c.maskRenderer != null && c.maskRenderer.gameObject.activeSelf)
								{
									Undo.RecordObject(c.maskRenderer.gameObject, "Hide Mask Renderer");
									c.maskRenderer.gameObject.SetActive(false);
									EditorUtility.SetDirty(c.maskRenderer.gameObject);
								}
							}
						}
					}

					if (!_maskMethodProp.hasMultipleDifferentValues &&
						(Unity2PNG.MaskMethod)_maskMethodProp.enumValueIndex != Unity2PNG.MaskMethod.Off)
					{
						GUILayout.BeginHorizontal();
						{
							EditorGUILayout.PropertyField(_maskRendererProp, new GUIContent(
								"Mask Renderer",
								"Renderer object whose black-and-white luminance defines the transparency mask (white = visible, black = transparent)."
							));

							if (!isMultiEdit && component.maskRenderer != null)
							{
								GameObject maskObj = component.maskRenderer.gameObject;
								bool isMaskActive = maskObj.activeSelf;

								Color prevBg = GUI.backgroundColor;
								GUI.backgroundColor = isMaskActive ? new Color(0.35f, 0.9f, 0.45f) : prevBg;

								bool newMaskActive = GUILayout.Toggle(isMaskActive, new GUIContent(isMaskActive ? "Visible" : "Hidden"), GUI.skin.button, GUILayout.Width(65f));
								GUI.backgroundColor = prevBg;

								if (newMaskActive != isMaskActive)
								{
									Undo.RecordObject(maskObj, "Toggle Mask Renderer Visibility");
									maskObj.SetActive(newMaskActive);
									EditorUtility.SetDirty(maskObj);
								}
							}
						}
						GUILayout.EndHorizontal();
					}
				}
				GUILayout.EndVertical();

				// BACKGROUND
				GUILayout.BeginVertical("helpbox");
				{
					EditorGUILayout.PropertyField(_captureBackgroundProp, new GUIContent("Background"));
				}
				GUILayout.EndVertical();

				// ALPHA SMOOTH
				GUILayout.BeginVertical("helpbox");
				{
					EditorGUI.showMixedValue = _alphaSmoothProp.hasMultipleDifferentValues;
					EditorGUI.BeginChangeCheck();
					bool alphaSmoothVal = EditorGUILayout.ToggleLeft(
						new GUIContent("Alpha Smooth",
							"Smooths out faint outer pixels (low-alpha glow/bloom edges) by dampening alpha values below the threshold.\n\n" +
							"Useful for cleaning up soft transparent fringes around glowing VFX."),
						_alphaSmoothProp.boolValue
					);
					if (EditorGUI.EndChangeCheck())
						_alphaSmoothProp.boolValue = alphaSmoothVal;
					EditorGUI.showMixedValue = false;

					if (!_alphaSmoothProp.hasMultipleDifferentValues && _alphaSmoothProp.boolValue)
					{
						EditorGUILayout.Slider(_alphaSmoothLimitProp, 0.1f, 0.9f, new GUIContent(
							"Alpha Smooth Start",
							"Pixels with an alpha value below this threshold (0.1 - 0.5) will be dampened. Default: 0.3"
						));

						EditorGUILayout.Slider(_alphaSmoothDampProp, 0.1f, 3f, new GUIContent(
							"Alpha Smooth Power",
							"Controls how aggressively faint pixels below the threshold are faded out. Higher values trim more outer glow. Default: 1.5"
						));

						EditorGUILayout.HelpBox(
							"Fades out faint outer pixels whose alpha falls below 'Alpha Smooth Start', trimming soft bloom/glow fringes cleanly.",
							MessageType.Info
						);
					}
				}
				GUILayout.EndVertical();

				// COLOR SPACE
				if (PlayerSettings.colorSpace == ColorSpace.Linear)
				{
					GUILayout.BeginVertical("helpbox");
					{
						EditorGUI.showMixedValue = _convertLinearToGammaProp.hasMultipleDifferentValues;
						EditorGUI.BeginChangeCheck();
						bool convertLinearToGammaVal = EditorGUILayout.ToggleLeft(
							new GUIContent("Convert Linear To Gamma",
								"Converts captured linear color values to sRGB (Gamma) before saving PNGs.\n\n" +
								"• Checked (Recommended): Output PNGs match the Game view and standard image viewers.\n" +
								"• Unchecked: Exports raw linear data (useful only for data textures like masks, flowmaps, or non-sRGB shader inputs)."),
							_convertLinearToGammaProp.boolValue
						);
						if (EditorGUI.EndChangeCheck())
							_convertLinearToGammaProp.boolValue = convertLinearToGammaVal;
						EditorGUI.showMixedValue = false;
					}
					GUILayout.EndVertical();
				}

				// DEBUG
				GUILayout.BeginVertical("helpbox");
				{
					EditorGUI.showMixedValue = _debugProp.hasMultipleDifferentValues;
					EditorGUI.BeginChangeCheck();
					bool debugVal = EditorGUILayout.ToggleLeft(
						new GUIContent("Debug", "Logs detailed capture, transition, and export progress messages to the Console."),
						_debugProp.boolValue
					);
					if (EditorGUI.EndChangeCheck())
						_debugProp.boolValue = debugVal;
					EditorGUI.showMixedValue = false;
				}
				GUILayout.EndVertical();

				// PIPELINE
				if (!isMultiEdit)
				{
					GUILayout.Space(10);
					GUILayout.BeginVertical("helpbox");
					{
						var steps = new List<string> { "Capture" };
						if (component.postProcessMethod == Unity2PNG.PostProcessMethod.OnInputTexture)
							steps.Add("Post-Process (Input)");
						if (component.maskMethod == Unity2PNG.MaskMethod.OnInputTexture)
							steps.Add("Mask (Input)");
						if (component.loopTransitionMethod != Unity2PNG.LoopMethod.Off)
							steps.Add("Loop");
						if (component.postProcessMethod == Unity2PNG.PostProcessMethod.OnOutputTexture)
							steps.Add("Post-Process (Output)");
						if (component.maskMethod == Unity2PNG.MaskMethod.OnOutputTexture)
							steps.Add("Mask (Output)");
						if (component.alphaSmooth)
							steps.Add("Alpha Smooth");
						if (component.convertLinearToGamma && PlayerSettings.colorSpace == ColorSpace.Linear)
							steps.Add("Linear To Gamma");
						GUILayout.Label("PIPELINE", EditorStyles.miniBoldLabel);
						GUILayout.Label(string.Join("  →  ", steps), EditorStyles.wordWrappedLabel);
					}
					GUILayout.EndVertical();
				}
			}
			GUILayout.EndVertical();

			// FILES
			GUILayout.Space(10);

			EditorGUI.BeginChangeCheck();
			bool newShowFilesOutput = EditorGUILayout.Foldout(_showFilesOutput, "FILES & OUTPUT", true, EditorStyles.foldoutHeader);
			if (EditorGUI.EndChangeCheck())
			{
				_showFilesOutput = newShowFilesOutput;
				EditorPrefs.SetBool(PREF_SHOW_FILES_OUTPUT, _showFilesOutput);
			}

			GUILayout.BeginVertical("helpbox");
			{
				if (!isMultiEdit)
				{
					string zeros = new string('0', Mathf.Max(1, component.digits));
					EditorGUILayout.HelpBox($"First file: {component.effectiveFolderPath}\\{component.fileName}{zeros}.png", MessageType.None);
				}

				if (_showFilesOutput)
				{
					if (!isMultiEdit)
						GUILayout.Space(4);

					// Base Folder Path + Open button (opens effectiveFolderPath)
					GUILayout.BeginHorizontal();
					{
						EditorGUILayout.PropertyField(_folderPathProp, new GUIContent("Folder Path"));
						if (!isMultiEdit && GUILayout.Button("Open", GUILayout.Width(55)))
							Unity2PNG.OpenFolder(component.effectiveFolderPath + "\\");
					}
					GUILayout.EndHorizontal();

					EditorGUILayout.PropertyField(_fileNameProp, new GUIContent("File Name"));
					EditorGUILayout.IntSlider(_digitsProp, 1, 3, new GUIContent("Digits"));

					GUILayout.Space(4);

					// Three folder options justified evenly across the full row width
					GUILayout.BeginHorizontal();
					{
						EditorGUI.showMixedValue = _appendObjectNameProp.hasMultipleDifferentValues;
						EditorGUI.BeginChangeCheck();
						bool appendVal = EditorGUILayout.ToggleLeft(
							new GUIContent("Append Object Name", "Appends this GameObject's name as a subfolder to Folder Path."),
							_appendObjectNameProp.boolValue,
							GUILayout.Width(148f)
						);
						if (EditorGUI.EndChangeCheck())
							_appendObjectNameProp.boolValue = appendVal;

						GUILayout.FlexibleSpace();

						EditorGUI.showMixedValue = _overrideFolderProp.hasMultipleDifferentValues;
						EditorGUI.BeginChangeCheck();
						bool ovVal = EditorGUILayout.ToggleLeft(
							new GUIContent("Override Folder", "Existing files inside the target folder will be deleted before capture."),
							_overrideFolderProp.boolValue,
							GUILayout.Width(114f)
						);
						if (EditorGUI.EndChangeCheck())
							_overrideFolderProp.boolValue = ovVal;

						GUILayout.FlexibleSpace();

						EditorGUI.showMixedValue = _showInExplorerProp.hasMultipleDifferentValues;
						EditorGUI.BeginChangeCheck();
						bool openVal = EditorGUILayout.ToggleLeft(
							new GUIContent("Open On End", "Opens the output folder in File Explorer when export finishes."),
							_showInExplorerProp.boolValue,
							GUILayout.Width(98f)
						);
						if (EditorGUI.EndChangeCheck())
							_showInExplorerProp.boolValue = openVal;

						EditorGUI.showMixedValue = false;
					}
					GUILayout.EndHorizontal();
				}
			}
			GUILayout.EndVertical();

			GUILayout.Space(10);

			// AUTO START
			GUILayout.BeginVertical("helpbox");
			{
				GUILayout.BeginHorizontal();
				{
					EditorGUI.showMixedValue = _captureAutoStartProp.hasMultipleDifferentValues;
					EditorGUI.BeginChangeCheck();
					bool autoVal = EditorGUILayout.ToggleLeft("Auto Start", _captureAutoStartProp.boolValue, GUILayout.Width(85f));
					if (EditorGUI.EndChangeCheck())
						_captureAutoStartProp.boolValue = autoVal;
					EditorGUI.showMixedValue = false;
					GUILayout.Space(8);
					bool prevGui = GUI.enabled;
					GUI.enabled = (_captureAutoStartProp.hasMultipleDifferentValues || _captureAutoStartProp.boolValue) && !Application.isPlaying;
					float prevLabelWidth = EditorGUIUtility.labelWidth;
					EditorGUIUtility.labelWidth = 90f;
					EditorGUILayout.Slider(_captureDelayProp, 0f, 10f, new GUIContent("Start Delay (s)"));
					EditorGUIUtility.labelWidth = prevLabelWidth;
					GUI.enabled = prevGui;
				}
				GUILayout.EndHorizontal();
			}
			GUILayout.EndVertical();
			serializedObject.ApplyModifiedProperties();
			// Enforce even frameRate and clamp/even transitionFrames across all edited objects
			foreach (Object obj in targets)
			{
				Unity2PNG c = obj as Unity2PNG;
				if (c != null)
				{
					bool changed = false;

					if (c.frameRate % 2 > 0)
					{
						c.frameRate++;
						changed = true;
					}

					int maxTf = c.calculateMaxTransitionFrames;
					if (maxTf % 2 > 0)
						maxTf++;

					if (c.loopTransitionFrames > maxTf)
					{
						c.loopTransitionFrames = maxTf;
						changed = true;
					}

					if (c.loopTransitionFrames % 2 > 0)
					{
						c.loopTransitionFrames++;
						changed = true;
					}

					if (changed)
						EditorUtility.SetDirty(c);
				}
			}

			// CONTROL
			GUILayout.Space(10);
			GUILayout.BeginVertical("helpbox");
			{
				GUILayout.Space(6);
				GUILayout.BeginHorizontal();
				{
					GUILayout.FlexibleSpace();
					GUILayout.Label("UNITY 2 PNG " + Unity2PNG.VERSION, EditorStyles.wordWrappedLabel);
					GUILayout.FlexibleSpace();
				}
				GUILayout.EndHorizontal();
				GUILayout.Space(8);

				// Check if selected object is a Prefab asset in Project window or not in an active scene
				bool isPrefabOrNotInScene = component != null &&
					(EditorUtility.IsPersistent(component) || !component.gameObject.scene.IsValid());

				if (isPrefabOrNotInScene)
				{
					EditorGUILayout.HelpBox("Place this Prefab in a Scene to start capturing.", MessageType.Warning);
					GUILayout.Space(4);
				}

				GUILayout.BeginHorizontal();
				{
					GUILayout.FlexibleSpace();
					bool prevGui = GUI.enabled;
					bool isBusy = Unity2PNG.isAnyRecording || (component != null && component.isRecording);
					GUI.enabled = !isMultiEdit && !isBusy && !isPrefabOrNotInScene;

					string startBtnLabel = isMultiEdit
						? "SELECT SINGLE TO START"
						: (isBusy ? "RECORDING..." : "START");

					if (GUILayout.Button(startBtnLabel, GUILayout.Width(200), GUILayout.Height(30)))
					{
						if (!Application.isPlaying)
						{
							SessionState.SetString("Unity2PNG_TargetID", Unity2PNG.GetRecorderId(component));
							EditorApplication.EnterPlaymode();
						}
						else
						{
							component.StartRecording();
						}
					}
					GUI.enabled = prevGui;
					GUILayout.FlexibleSpace();
				}
				GUILayout.EndHorizontal();
				GUILayout.Space(8);
				GUILayout.BeginHorizontal();
				{
					GUILayout.FlexibleSpace();
					Color prevColor = GUI.contentColor;
					GUI.contentColor = new Color(0.3f, 0.65f, 1f);
					if (GUILayout.Button("Video Tutorial", EditorStyles.label))
						Application.OpenURL(Unity2PNG.VIDEO_TUTORIAL_LINK);
					GUI.contentColor = prevColor;
					GUILayout.FlexibleSpace();
				}
				GUILayout.EndHorizontal();
				GUILayout.Space(6);
			}
			GUILayout.EndVertical();

			GUI.enabled = !Unity2PNG.isAnyRecording;

			// ANIMATION PREVIEW PLAYER
			GUILayout.Space(10);

			EditorGUI.BeginChangeCheck();
			bool newShowPreview = EditorGUILayout.Foldout(_showPreview, "PREVIEW", true, EditorStyles.foldoutHeader);
			if (EditorGUI.EndChangeCheck())
			{
				_showPreview = newShowPreview;
				EditorPrefs.SetBool(PREF_SHOW_PREVIEW, _showPreview);

				if (_showPreview && !isMultiEdit && component != null && _player.frames.Count == 0)
					_player.LoadFrames(component, false);
				else if (!_showPreview)
					_player.ClearFrames();

				Repaint();
			}

			if (_showPreview)
			{
				GUILayout.BeginVertical("helpbox");
				{
					if (isMultiEdit)
						EditorGUILayout.HelpBox("Preview is disabled while multiple Unity2PNG objects are selected.", MessageType.Info);
					else
						_player.DrawGUI(component, false, Repaint);
				}
				GUILayout.EndVertical();
			}

			GUI.enabled = true;
		}

		private void DrawCaptureSummary(Unity2PNG component)
		{
			GUILayout.BeginVertical("helpbox");
			{
				bool loopEnabled = component.loopTransitionMethod != Unity2PNG.LoopMethod.Off;

				int transitionFrames = Mathf.Clamp(component.loopTransitionFrames, 2, component.calculateMaxTransitionFrames);
				if (transitionFrames % 2 > 0)
					transitionFrames++;

				int outputFrames = component.calculatedOutputFrames;
				int capturedFrames = outputFrames + (loopEnabled ? transitionFrames : 0);

				string framesText = !loopEnabled
					? $"Output frames: {outputFrames}"
					: $"Output frames: {outputFrames}  (captured: {capturedFrames}, transition: {transitionFrames})";

				double freeRamGB = Unity2PNG.GetAvailableSystemRamGB();
				double totalRamGB = SystemInfo.systemMemorySize / 1024.0;

				string estRamLabel = "RAM Needed";
				string estRamText = $"{estRamLabel}: N/A";

				if (component.renderTexture != null)
				{
					double estGB = component.EstimatePeakRamGB();
					double estMB = estGB * 1024.0;

					string rawEst = estMB >= 1024.0
						? $"{estRamLabel}: ~{estGB:F2} GB"
						: $"{estRamLabel}: ~{estMB:F0} MB";

					bool criticalMemoryWarning = estGB > (freeRamGB * 0.85);
					bool highMemoryWarning = estGB > 3.0;

					if (criticalMemoryWarning)
						estRamText = $"<color=#FF4B4B><b>{rawEst}</b></color>";
					else if (highMemoryWarning)
						estRamText = $"<color=#FFD147><b>{rawEst}</b></color>";
					else
						estRamText = rawEst;
				}

				GUIStyle richMiniLabel = new GUIStyle(EditorStyles.miniLabel) { richText = true };
				GUILayout.Label(framesText, EditorStyles.wordWrappedLabel);
				GUILayout.Label($"Available System RAM: {freeRamGB:F2} GB  /  {totalRamGB:F1} GB   |   {estRamText}", richMiniLabel);
			}
			GUILayout.EndVertical();
		}
	}
}
#endif