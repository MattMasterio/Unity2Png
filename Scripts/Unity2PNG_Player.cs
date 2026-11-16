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
using System.IO;
using System.Linq;

namespace Unity2Png
{
	public class Unity2PNG_Player
	{
		private const string PREF_FPS = "Unity2PNG_Preview_FPS";
		private const string PREF_BG = "Unity2PNG_Preview_BG";
		private const string PREF_AUTO_PLAY = "Unity2PNG_Preview_AutoPlay";
		private const string PREF_SHOW_ZERO_ALPHA = "Unity2PNG_Preview_ShowZeroAlpha";
		private const string PREF_ZERO_ALPHA_COLOR = "Unity2PNG_Preview_ZeroAlphaColor";

		private static bool _showZeroAlpha = false;
		private static Color _zeroAlphaColor = new Color(1f, 0f, 1f, 1f); // Default bright magenta
		private static int _zeroAlphaVersion = 0;
		private int _localZeroAlphaVersion = 0;

		private static bool _autoPlayOnLoad = true;

		// Shared static cache so Inspector and Pop-Out Window always display the exact same textures
		private static List<Texture2D> _sharedFrames = new List<Texture2D>();
		private static List<byte[]> _sharedRawBytes = new List<byte[]>();
		private static string _sharedLoadedFolder = "";
		private static string _sharedFileName = "";

		private string _loadedFolderPath = "";
		private Texture2D _checkerboardTex;
		private Texture2D[] _bgModeIcons;
		private Material _previewBlitMat;
		private static bool _isPlaying = true;
		private static int _frameIndex = 0;
		private static int _desiredFrameIndex = 0;
		private static double _lastFrameTime = 0;

		private static int _bgMode = 0;
		private static int _previewFps = 30;
		private static int _maxPreviewFpsSlider = 60;
		private float _zoom = 1f;
		private Vector2 _panOffset = Vector2.zero;
		private bool _requestFitWidthOnNextDraw = false;
		private bool _instanceViewLoaded = false;
		private static bool _prefsLoaded = false;

		private bool _isDragging = false;

		private int _lastRenderedFrame = -1;
		private bool _lastRenderedPlayingState = true;
		private int _lastRenderedFps = -1;
		private bool _lastRenderedAutoPlay = true;
		private int _lastRenderedBgMode = -1;

		public List<Texture2D> frames => _sharedFrames;

		private static string GetViewPrefPrefix(bool isPopOutWindow)
		{
			return isPopOutWindow ? "Unity2PNG_PreviewWin_" : "Unity2PNG_PreviewInsp_";
		}

		private void EnsureGlobalPrefsLoaded(Unity2PNG component)
		{
			if (_prefsLoaded)
				return;

			int defaultFps = component != null ? Mathf.Max(1, component.frameRate) : 30;
			_previewFps = Mathf.Max(1, EditorPrefs.GetInt(PREF_FPS, defaultFps));
			_maxPreviewFpsSlider = Mathf.Max(60, _previewFps);
			_autoPlayOnLoad = EditorPrefs.GetBool(PREF_AUTO_PLAY, true);
			_bgMode = EditorPrefs.GetInt(PREF_BG, 1);

			// Load zero-alpha debug settings
			_showZeroAlpha = EditorPrefs.GetBool(PREF_SHOW_ZERO_ALPHA, false);
			string hexColor = EditorPrefs.GetString(PREF_ZERO_ALPHA_COLOR, "#FF00FFFF");
			if (ColorUtility.TryParseHtmlString(hexColor, out Color parsedCol))
				_zeroAlphaColor = parsedCol;

			_prefsLoaded = true;
		}

		private void EnsureInstanceViewLoaded(bool isPopOutWindow)
		{
			if (_instanceViewLoaded)
				return;

			string prefix = GetViewPrefPrefix(isPopOutWindow);
			if (EditorPrefs.HasKey(prefix + "Zoom"))
			{
				_zoom = Mathf.Clamp(EditorPrefs.GetFloat(prefix + "Zoom", 1f), 0.25f, 16f);
				_panOffset = new Vector2(
					EditorPrefs.GetFloat(prefix + "PanX", 0f),
					EditorPrefs.GetFloat(prefix + "PanY", 0f)
				);
				_requestFitWidthOnNextDraw = false;
			}
			else
			{
				_requestFitWidthOnNextDraw = !isPopOutWindow;
			}

			_instanceViewLoaded = true;
		}


		private void SaveViewPrefs(bool isPopOutWindow)
		{
			string prefix = GetViewPrefPrefix(isPopOutWindow);
			EditorPrefs.SetFloat(prefix + "Zoom", _zoom);
			EditorPrefs.SetFloat(prefix + "PanX", _panOffset.x);
			EditorPrefs.SetFloat(prefix + "PanY", _panOffset.y);
		}

		private Material GetPreviewBlitMaterial()
		{
			if (_previewBlitMat == null)
			{
				Shader shader = Shader.Find("UI/Default");
				if (shader == null)
					shader = Shader.Find("Sprites/Default");

				if (shader != null)
				{
					_previewBlitMat = new Material(shader);
					_previewBlitMat.hideFlags = HideFlags.HideAndDontSave;
				}
			}
			return _previewBlitMat;
		}

		public bool UpdatePlayback()
		{
			if (_localZeroAlphaVersion != _zeroAlphaVersion)
			{
				_localZeroAlphaVersion = _zeroAlphaVersion;
				return true; // Force Repaint in other window
			}

			if (!Unity2PNG.isAnyRecording && _isPlaying && frames.Count > 1)
			{
				double frameDuration = 1.0 / Mathf.Max(1, _previewFps);
				if (EditorApplication.timeSinceStartup - _lastFrameTime >= frameDuration)
				{
					_lastFrameTime = EditorApplication.timeSinceStartup;
					_frameIndex = (_frameIndex + 1) % frames.Count;
					_desiredFrameIndex = _frameIndex;
				}
			}

			// Sync all UI controls (Frame, Play/Pause, FPS, Auto-Play, BG, Zoom, Pan) across Inspector and Pop-Out windows
			if (_lastRenderedFrame != _frameIndex ||
				_lastRenderedPlayingState != _isPlaying ||
				_lastRenderedFps != _previewFps ||
				_lastRenderedAutoPlay != _autoPlayOnLoad ||
				_lastRenderedBgMode != _bgMode)
			{
				_lastRenderedFrame = _frameIndex;
				_lastRenderedPlayingState = _isPlaying;
				_lastRenderedFps = _previewFps;
				_lastRenderedAutoPlay = _autoPlayOnLoad;
				_lastRenderedBgMode = _bgMode;
				return true;
			}

			return false;
		}

		public void LoadFrames(Unity2PNG component, bool autoPlay)
		{
			if (component == null)
			{
				_loadedFolderPath = "";
				ClearFrames();
				_sharedRawBytes.Clear();
				_sharedLoadedFolder = "";
				_sharedFileName = "";
				return;
			}

			string targetFolder = component.effectiveFolderPath;
			if (string.IsNullOrEmpty(targetFolder) || !Directory.Exists(targetFolder))
			{
				ClearFrames();
				_sharedRawBytes.Clear();
				_sharedLoadedFolder = "";
				_sharedFileName = "";
				return;
			}

			_loadedFolderPath = targetFolder;
			EnsureGlobalPrefsLoaded(component);

			// Reload from disk only if forced (autoPlay == true) or if folder/fileName changed
			bool sourceChanged = _sharedLoadedFolder != targetFolder || _sharedFileName != component.fileName;
			if (autoPlay || sourceChanged || _sharedRawBytes.Count == 0)
			{
				ClearFrames();
				_sharedRawBytes.Clear();
				_sharedLoadedFolder = targetFolder;
				_sharedFileName = component.fileName;

				string searchPattern = string.IsNullOrEmpty(component.fileName) ? "*.png" : component.fileName + "*.png";
				string[] files = Directory.GetFiles(targetFolder, searchPattern)
										  .OrderBy(f => f)
										  .ToArray();

				foreach (string file in files)
				{
					_sharedRawBytes.Add(File.ReadAllBytes(file));
				}

				RebuildTexturesFromRawBytes();
			}
			else if (_sharedFrames.Count == 0)
			{
				// Rebuild textures immediately from cached raw bytes if frames were cleared on foldout collapse
				RebuildTexturesFromRawBytes();
			}

			_localZeroAlphaVersion = _zeroAlphaVersion;

			if (frames.Count > 0)
			{
				if (autoPlay)
				{
					if (_autoPlayOnLoad)
					{
						_frameIndex = 0;
						_desiredFrameIndex = 0;
					}
					else
					{
						_frameIndex = Mathf.Clamp(_desiredFrameIndex, 0, frames.Count - 1);
					}

					_isPlaying = true;
					_lastFrameTime = EditorApplication.timeSinceStartup;
				}
				else
				{
					_frameIndex = Mathf.Clamp(_desiredFrameIndex, 0, frames.Count - 1);
					_lastFrameTime = EditorApplication.timeSinceStartup;
				}
			}
		}

		private void RebuildTexturesFromRawBytes()
		{
			ClearFrames();

			Color32 replacementColor = new Color32(
				(byte)Mathf.RoundToInt(_zeroAlphaColor.r * 255f),
				(byte)Mathf.RoundToInt(_zeroAlphaColor.g * 255f),
				(byte)Mathf.RoundToInt(_zeroAlphaColor.b * 255f),
				255
			);

			foreach (byte[] data in _sharedRawBytes)
			{
				bool isLinearProject = PlayerSettings.colorSpace == ColorSpace.Linear;
				Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, isLinearProject);
				if (tex.LoadImage(data))
				{
					if (_showZeroAlpha)
					{
						Color32[] pixels = tex.GetPixels32();
						for (int i = 0; i < pixels.Length; i++)
						{
							if (pixels[i].a == 0)
								pixels[i] = replacementColor;
						}
						tex.SetPixels32(pixels);
					}

					tex.filterMode = FilterMode.Point;
					tex.hideFlags = HideFlags.HideAndDontSave;
					tex.Apply(false, true);
					_sharedFrames.Add(tex);
				}
				else
				{
					Object.DestroyImmediate(tex);
				}
			}
		}

		public void ClearFrames()
		{
			bool hadFrames = _sharedFrames.Count > 0;
			for (int i = 0; i < _sharedFrames.Count; i++)
			{
				if (_sharedFrames[i] != null)
					Object.DestroyImmediate(_sharedFrames[i]);
			}
			_sharedFrames.Clear();

			if (hadFrames)
				System.GC.Collect();
		}

		public void Dispose()
		{
			if (_checkerboardTex != null)
			{
				Object.DestroyImmediate(_checkerboardTex);
				_checkerboardTex = null;
			}

			if (_bgModeIcons != null)
			{
				for (int i = 0; i < _bgModeIcons.Length; i++)
				{
					if (_bgModeIcons[i] != null)
						Object.DestroyImmediate(_bgModeIcons[i]);
				}
				_bgModeIcons = null;
			}

			if (_previewBlitMat != null)
			{
				Object.DestroyImmediate(_previewBlitMat);
				_previewBlitMat = null;
			}
		}

		private Texture2D GetCheckerboardTexture()
		{
			if (_checkerboardTex == null)
			{
				_checkerboardTex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
				_checkerboardTex.wrapMode = TextureWrapMode.Repeat;
				_checkerboardTex.filterMode = FilterMode.Point;
				_checkerboardTex.hideFlags = HideFlags.HideAndDontSave;

				Color c1 = new Color(0.28f, 0.28f, 0.28f, 1f);
				Color c2 = new Color(0.38f, 0.38f, 0.38f, 1f);

				for (int y = 0; y < 16; y++)
				{
					for (int x = 0; x < 16; x++)
					{
						bool isEven = ((x / 8) + (y / 8)) % 2 == 0;
						_checkerboardTex.SetPixel(x, y, isEven ? c1 : c2);
					}
				}
				_checkerboardTex.Apply();
			}
			return _checkerboardTex;
		}

		private GUIContent[] GetBgModeContents()
		{
			if (_bgModeIcons == null || _bgModeIcons.Length != 4 || _bgModeIcons[0] == null)
			{
				_bgModeIcons = new Texture2D[4];
				int size = 12;

				// 0: No Background icon (transparent interior with border and subtle diagonal slash)
				Texture2D noneIcon = new Texture2D(size, size, TextureFormat.RGBA32, false);
				noneIcon.filterMode = FilterMode.Point;
				noneIcon.hideFlags = HideFlags.HideAndDontSave;

				Color border = new Color(0.35f, 0.35f, 0.35f, 1f);
				Color slashColor = new Color(0.85f, 0.35f, 0.35f, 1f);
				Color clear = new Color(0f, 0f, 0f, 0f);

				for (int y = 0; y < size; y++)
				{
					for (int x = 0; x < size; x++)
					{
						if (x == 0 || y == 0 || x == size - 1 || y == size - 1)
						{
							noneIcon.SetPixel(x, y, border);
						}
						else if (x == y)
						{
							noneIcon.SetPixel(x, y, slashColor);
						}
						else
						{
							noneIcon.SetPixel(x, y, clear);
						}
					}
				}
				noneIcon.Apply();
				_bgModeIcons[0] = noneIcon;

				// 1: 2x2 Checkerboard icon
				Texture2D checkerIcon = new Texture2D(size, size, TextureFormat.RGBA32, false);
				checkerIcon.filterMode = FilterMode.Point;
				checkerIcon.hideFlags = HideFlags.HideAndDontSave;

				Color cDark = new Color(0.28f, 0.28f, 0.28f, 1f);
				Color cLight = new Color(0.65f, 0.65f, 0.65f, 1f);
				Color checkerBorder = new Color(0.15f, 0.15f, 0.15f, 1f);

				for (int y = 0; y < size; y++)
				{
					for (int x = 0; x < size; x++)
					{
						if (x == 0 || y == 0 || x == size - 1 || y == size - 1)
						{
							checkerIcon.SetPixel(x, y, checkerBorder);
						}
						else
						{
							bool isEven = ((x / 5) + (y / 5)) % 2 == 0;
							checkerIcon.SetPixel(x, y, isEven ? cDark : cLight);
						}
					}
				}
				checkerIcon.Apply();
				_bgModeIcons[1] = checkerIcon;

				// 2: Black swatch icon (with subtle border so it stays visible on dark theme)
				_bgModeIcons[2] = CreateSolidSwatchIcon(size, new Color(0.05f, 0.05f, 0.05f, 1f), new Color(0.35f, 0.35f, 0.35f, 1f));

				// 3: White swatch icon
				_bgModeIcons[3] = CreateSolidSwatchIcon(size, new Color(0.95f, 0.95f, 0.95f, 1f), new Color(0.25f, 0.25f, 0.25f, 1f));
			}

			return new GUIContent[]
			{
				new GUIContent(_bgModeIcons[0], "No Background (Editor Window)"),
				new GUIContent(_bgModeIcons[1], "Checkerboard Background"),
				new GUIContent(_bgModeIcons[2], "Black Background"),
				new GUIContent(_bgModeIcons[3], "White Background")
			};
		}

		private Texture2D CreateSolidSwatchIcon(int size, Color fill, Color border)
		{
			Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
			tex.filterMode = FilterMode.Point;
			tex.hideFlags = HideFlags.HideAndDontSave;

			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					bool isBorder = x == 0 || y == 0 || x == size - 1 || y == size - 1;
					tex.SetPixel(x, y, isBorder ? border : fill);
				}
			}
			tex.Apply();
			return tex;
		}

		public void DrawGUI(Unity2PNG component, bool isPopOutWindow, System.Action repaintCallback)
		{
			EnsureGlobalPrefsLoaded(component);
			EnsureInstanceViewLoaded(isPopOutWindow);

			if (_localZeroAlphaVersion != _zeroAlphaVersion)
			{
				_localZeroAlphaVersion = _zeroAlphaVersion;
				if (_sharedRawBytes.Count > 0)
					RebuildTexturesFromRawBytes();
			}

			// Top bar: Actions & Background mode
			GUILayout.BeginHorizontal(isPopOutWindow ? EditorStyles.toolbar : GUIStyle.none);
			{
				GUIStyle btnStyle = isPopOutWindow ? EditorStyles.toolbarButton : GUI.skin.button;

				if (GUILayout.Button("Reload", btnStyle, GUILayout.Width(60)))
				{
					LoadFrames(component, true);
				}

				if (GUILayout.Button("Reset", btnStyle, GUILayout.Width(55)))
				{
					_panOffset = Vector2.zero;
					if (!isPopOutWindow)
					{
						_requestFitWidthOnNextDraw = true;
					}
					else
					{
						_zoom = 1f;
						SaveViewPrefs(isPopOutWindow);
					}
				}

				if (!isPopOutWindow && component != null)
				{
					if (GUILayout.Button("Pop Out", btnStyle, GUILayout.Width(65)))
					{
						Unity2PNG_PreviewWindow.OpenWindow(component);
					}
				}

				// Debug button to highlight pixels with Alpha == 0
				Color prevBg = GUI.backgroundColor;
				if (_showZeroAlpha)
					GUI.backgroundColor = new Color(0.95f, 0.45f, 0.85f);

				EditorGUI.BeginChangeCheck();
				bool newShowZeroAlpha = GUILayout.Toggle(
					_showZeroAlpha,
					new GUIContent("Alpha=0", "Highlights fully transparent pixels (alpha == 0) with the selected color."),
					btnStyle,
					GUILayout.Width(62)
				);
				GUI.backgroundColor = prevBg;

				if (EditorGUI.EndChangeCheck())
				{
					_showZeroAlpha = newShowZeroAlpha;
					EditorPrefs.SetBool(PREF_SHOW_ZERO_ALPHA, _showZeroAlpha);
					_zeroAlphaVersion++;
					_localZeroAlphaVersion = _zeroAlphaVersion;
					RebuildTexturesFromRawBytes();
					repaintCallback?.Invoke();
				}

				if (_showZeroAlpha)
				{
					EditorGUI.BeginChangeCheck();
					Color newZeroCol = EditorGUILayout.ColorField(GUIContent.none, _zeroAlphaColor, false, false, false, GUILayout.Width(36));
					if (EditorGUI.EndChangeCheck())
					{
						_zeroAlphaColor = newZeroCol;
						EditorPrefs.SetString(PREF_ZERO_ALPHA_COLOR, "#" + ColorUtility.ToHtmlStringRGBA(_zeroAlphaColor));
						_zeroAlphaVersion++;
						_localZeroAlphaVersion = _zeroAlphaVersion;
						RebuildTexturesFromRawBytes();
						repaintCallback?.Invoke();
					}
				}

				GUILayout.FlexibleSpace();

				int newBgMode = GUILayout.Toolbar(_bgMode, GetBgModeContents(), btnStyle, GUILayout.Width(110));
				if (newBgMode != _bgMode)
				{
					_bgMode = newBgMode;
					EditorPrefs.SetInt(PREF_BG, _bgMode);
				}
			}
			GUILayout.EndHorizontal();

			GUILayout.Space(4);

			// Folder path bar + Info bar inside a clean sub-box
			GUILayout.BeginVertical(EditorStyles.helpBox);
			{
				if (!string.IsNullOrEmpty(_loadedFolderPath))
				{
					GUILayout.BeginHorizontal();
					{
						if (GUILayout.Button("Open Folder", GUILayout.Width(85), GUILayout.Height(18)))
						{
							Unity2PNG.OpenFolder(_loadedFolderPath + "\\");
						}

						GUILayout.Space(6);
						GUILayout.Label(_loadedFolderPath, EditorStyles.label, GUILayout.Height(18));
					}
					GUILayout.EndHorizontal();
				}

				if (frames != null && frames.Count > 0 && frames[0] != null)
				{
					GUILayout.Space(2);
					GUILayout.Label($"Resolution: {frames[0].width} x {frames[0].height}   |   Zoom: {Mathf.RoundToInt(_zoom * 100f)}%   (Scroll: Zoom, Drag: Pan)", EditorStyles.miniLabel);
				}
			}
			GUILayout.EndVertical();

			GUILayout.Space(6);

			// Viewport area
			if (frames != null && frames.Count > 0)
			{
				_frameIndex = Mathf.Clamp(_frameIndex, 0, frames.Count - 1);
				Texture2D currentTex = frames[_frameIndex];

				if (currentTex != null)
				{
					Rect availableRect = isPopOutWindow
						? GUILayoutUtility.GetRect(10f, 10f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true))
						: GUILayoutUtility.GetRect(240f, 240f, GUILayout.ExpandWidth(true));

					Event e = Event.current;
					if (availableRect.Contains(e.mousePosition))
					{
						if (e.type == EventType.ScrollWheel)
						{
							float zoomDelta = -e.delta.y * 0.05f;
							float oldZoom = _zoom;
							_zoom = Mathf.Clamp(_zoom * (1f + zoomDelta), 0.25f, 16f);
							_panOffset *= (_zoom / oldZoom);
							SaveViewPrefs(isPopOutWindow);

							e.Use();
							repaintCallback?.Invoke();
						}
						else if (e.type == EventType.MouseDown && (e.button == 0 || e.button == 2))
						{
							_isDragging = true;
							e.Use();
						}
					}

					if (_isDragging)
					{
						if (e.type == EventType.MouseDrag)
						{
							_panOffset += e.delta;
							e.Use();
							repaintCallback?.Invoke();
						}
						else if (e.type == EventType.MouseUp)
						{
							_isDragging = false;
							SaveViewPrefs(isPopOutWindow);
							e.Use();
						}
					}

					GUI.BeginGroup(availableRect);
					{
						float pad = 8f;
						float baseWidth = Mathf.Max(10f, availableRect.width - pad);
						float baseHeight = Mathf.Max(10f, availableRect.height - pad);

						float aspect = (float)currentTex.width / Mathf.Max(1, currentTex.height);
						float fitWidth = Mathf.Min(baseWidth, baseHeight * aspect);
						float fitHeight = fitWidth / aspect;

						// Fit texture width to the Inspector frame width when requested (default & Reset)
						if (_requestFitWidthOnNextDraw && !isPopOutWindow && Event.current.type == EventType.Repaint && fitWidth > 0f)
						{
							_zoom = Mathf.Clamp(baseWidth / fitWidth, 0.25f, 16f);
							_panOffset = Vector2.zero;
							_requestFitWidthOnNextDraw = false;
							SaveViewPrefs(isPopOutWindow);
						} 

						float drawWidth = fitWidth * _zoom;
						float drawHeight = fitHeight * _zoom;

						Rect centeredRect = new Rect(
							(availableRect.width - drawWidth) * 0.5f + _panOffset.x,
							(availableRect.height - drawHeight) * 0.5f + _panOffset.y,
							drawWidth,
							drawHeight
						);

						currentTex.filterMode = FilterMode.Point;

						if (_bgMode == 1)
						{
							float tileSize = 16f * Mathf.Clamp(_zoom, 0.5f, 4f);
							GUI.DrawTextureWithTexCoords(centeredRect, GetCheckerboardTexture(), new Rect(0, 0, drawWidth / tileSize, drawHeight / tileSize));
						}
						else if (_bgMode == 2)
						{
							EditorGUI.DrawRect(centeredRect, Color.black);
						}
						else if (_bgMode == 3)
						{
							EditorGUI.DrawRect(centeredRect, Color.white);
						}

						if (Event.current.type == EventType.Repaint)
						{
							Material mat = GetPreviewBlitMaterial();
							if (mat != null)
							{
								Rect viewportBounds = new Rect(0f, 0f, availableRect.width, availableRect.height);

								float xMin = Mathf.Max(centeredRect.xMin, viewportBounds.xMin);
								float yMin = Mathf.Max(centeredRect.yMin, viewportBounds.yMin);
								float xMax = Mathf.Min(centeredRect.xMax, viewportBounds.xMax);
								float yMax = Mathf.Min(centeredRect.yMax, viewportBounds.yMax);

								if (xMax > xMin && yMax > yMin)
								{
									Rect clippedRect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);

									float uMin = (xMin - centeredRect.xMin) / centeredRect.width;
									float uMax = (xMax - centeredRect.xMin) / centeredRect.width;
									// GUI Y goes top-to-bottom, Texture UV Y goes bottom-to-top
									float vMin = 1f - ((yMax - centeredRect.yMin) / centeredRect.height);
									float vMax = 1f - ((yMin - centeredRect.yMin) / centeredRect.height);

									Rect uvRect = Rect.MinMaxRect(uMin, vMin, uMax, vMax);
									Graphics.DrawTexture(clippedRect, currentTex, uvRect, 0, 0, 0, 0, Color.white, mat);
								}
							}
							else
								GUI.DrawTexture(centeredRect, currentTex, ScaleMode.ScaleToFit, true);
						}
					}
					GUI.EndGroup();
				}
				GUILayout.Space(4);

				// Controls: Play/Pause + Auto-Play + Step Back/Forward + Frame Scrubber
				GUILayout.BeginHorizontal();
				{
					if (GUILayout.Button(_isPlaying ? "Pause" : "Play", GUILayout.Width(52)))
					{
						_isPlaying = !_isPlaying;
						_lastFrameTime = EditorApplication.timeSinceStartup;
					}

					Color prevBg = GUI.backgroundColor;
					if (_autoPlayOnLoad)
						GUI.backgroundColor = new Color(0.35f, 0.65f, 1f);

					EditorGUI.BeginChangeCheck();
					bool newAutoPlay = GUILayout.Toggle(
						_autoPlayOnLoad,
						new GUIContent("Auto-Play", "Automatically start playing from frame 1 when switching objects or reloading."),
						GUI.skin.button,
						GUILayout.Width(68)
					);
					if (EditorGUI.EndChangeCheck())
					{
						_autoPlayOnLoad = newAutoPlay;
						EditorPrefs.SetBool(PREF_AUTO_PLAY, _autoPlayOnLoad);
					}
					GUI.backgroundColor = prevBg;

					// Step backward 1 frame (wraps around to last frame)
					if (GUILayout.Button(new GUIContent("<", "Previous frame"), GUILayout.Width(24)))
					{
						_isPlaying = false;
						_frameIndex = (_frameIndex - 1 + frames.Count) % frames.Count;
						_desiredFrameIndex = _frameIndex;
					}

					// Step forward 1 frame (wraps around to frame 1)
					if (GUILayout.Button(new GUIContent(">", "Next frame"), GUILayout.Width(24)))
					{
						_isPlaying = false;
						_frameIndex = (_frameIndex + 1) % frames.Count;
						_desiredFrameIndex = _frameIndex;
					}

					EditorGUI.BeginChangeCheck();
					int newFrame = EditorGUILayout.IntSlider(_frameIndex + 1, 1, frames.Count) - 1;
					if (EditorGUI.EndChangeCheck())
					{
						_isPlaying = false;
						_frameIndex = newFrame;
						_desiredFrameIndex = _frameIndex;
					}

					GUILayout.Label($"/ {frames.Count}", GUILayout.Width(38));
				}
				GUILayout.EndHorizontal();


				// Controls: Dynamic FPS Slider + Manual Input
				GUILayout.BeginHorizontal();
				{
					GUILayout.Label("FPS:", GUILayout.Width(35));
					int sliderFps = Mathf.RoundToInt(GUILayout.HorizontalSlider(_previewFps, 1f, _maxPreviewFpsSlider));
					int typedFps = EditorGUILayout.IntField(sliderFps, GUILayout.Width(45));
					typedFps = Mathf.Max(1, typedFps);
					if (typedFps != _previewFps)
					{
						_previewFps = typedFps;
						EditorPrefs.SetInt(PREF_FPS, _previewFps);

						if (_previewFps > _maxPreviewFpsSlider)
							_maxPreviewFpsSlider = _previewFps;
						else if (_previewFps <= 60 && _maxPreviewFpsSlider > 60)
							_maxPreviewFpsSlider = 60;
					}
				}
				GUILayout.EndHorizontal();
			}
			else
			{
				GUILayout.Label("No PNG frames found. Click START to capture.", EditorStyles.centeredGreyMiniLabel);
			}
		}
	}
}
#endif