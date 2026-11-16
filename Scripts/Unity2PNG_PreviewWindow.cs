#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Unity2Png
{
	public class Unity2PNG_PreviewWindow : EditorWindow
	{
		private Unity2PNG _targetComponent;
		private Unity2PNG_Player _player = new Unity2PNG_Player();
		private bool _wasPlayingInEditor = false;

		public static void OpenWindow(Unity2PNG targetComponent)
		{
			Unity2PNG_PreviewWindow window = GetWindow<Unity2PNG_PreviewWindow>("Unity2PNG Preview");
			window.minSize = new Vector2(360f, 360f);
			window.SetTarget(targetComponent);
			window.Show();
		}

		public void SetTarget(Unity2PNG targetComponent)
		{
			_targetComponent = targetComponent;
			_player.LoadFrames(_targetComponent, true);
		}

		private static Unity2PNG FindUnity2PNGInstance()
		{
#if UNITY_2023_1_OR_NEWER
			return FindAnyObjectByType<Unity2PNG>(FindObjectsInactive.Include);
#else
			return FindObjectOfType<Unity2PNG>(true);
#endif
		}

		private Unity2PNG ResolveTargetComponent()
		{
			if (Selection.activeGameObject != null)
				return Selection.activeGameObject.GetComponent<Unity2PNG>();

			return null;
		}

		private void OnEnable()
		{
			EditorApplication.update += OnEditorUpdate;
			Selection.selectionChanged += OnSelectionChanged;

			_targetComponent = ResolveTargetComponent();
			if (_targetComponent != null)
				_player.LoadFrames(_targetComponent, false);
		}

		private void OnDisable()
		{
			EditorApplication.update -= OnEditorUpdate;
			Selection.selectionChanged -= OnSelectionChanged;
			_player.Dispose();
		}

		private void OnSelectionChanged()
		{
			Unity2PNG selectedComp = ResolveTargetComponent();

			if (selectedComp == null)
			{
				if (_targetComponent != null || _player.frames.Count > 0)
				{
					_targetComponent = null;
					_player.LoadFrames(null, false);
					Repaint();
				}
				return;
			}

			if (selectedComp != _targetComponent)
			{
				bool sameSource = _targetComponent != null &&
								  _targetComponent.effectiveFolderPath == selectedComp.effectiveFolderPath &&
								  _targetComponent.fileName == selectedComp.fileName;

				_targetComponent = selectedComp;

				if (!sameSource)
					_player.LoadFrames(_targetComponent, true);

				Repaint();
			}
		}

		private void OnEditorUpdate()
		{
			// Catch delayed selection restoration after Domain Reload when entering Play Mode
			if (_targetComponent == null && Selection.activeGameObject != null)
			{
				Unity2PNG selected = ResolveTargetComponent();
				if (selected != null)
				{
					_targetComponent = selected;
					_player.LoadFrames(_targetComponent, false);
					Repaint();
				}
			}

			if (_wasPlayingInEditor && !EditorApplication.isPlaying)
			{
				_wasPlayingInEditor = false;
				_targetComponent = ResolveTargetComponent();
				_player.LoadFrames(_targetComponent, true);
				Repaint();
			}
			else if (EditorApplication.isPlaying)
			{
				_wasPlayingInEditor = true;
			}

			if (_player.UpdatePlayback())
				Repaint();
		}

		private void OnGUI()
		{
			if (_targetComponent == null)
			{
				GUILayout.FlexibleSpace();
				GUILayout.BeginHorizontal();
				{
					GUILayout.FlexibleSpace();
					GUILayout.Label("Select a GameObject with a Unity2PNG component to preview.", EditorStyles.centeredGreyMiniLabel);
					GUILayout.FlexibleSpace();
				}
				GUILayout.EndHorizontal();
				GUILayout.FlexibleSpace();
				return;
			}

			GUILayout.BeginHorizontal(EditorStyles.toolbar);
			{
				GUILayout.Label("Target: " + _targetComponent.gameObject.name, EditorStyles.miniLabel);
				GUILayout.FlexibleSpace();
			}
			GUILayout.EndHorizontal();

			_player.DrawGUI(_targetComponent, true, Repaint);
		}
	}
}
#endif