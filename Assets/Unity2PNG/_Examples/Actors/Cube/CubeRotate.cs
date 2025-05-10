using UnityEngine;

namespace Unity2PNG
{
	public class CubeRotate : MonoBehaviour
	{
		public Vector3 rotation;

		private Transform _transform;

		private void Start()
		{
			_transform = transform;
		}

		void Update()
		{
			if (_transform != null)
				_transform.Rotate(rotation * Time.deltaTime);
		}
	}
}