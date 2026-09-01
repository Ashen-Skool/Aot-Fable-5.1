using UnityEngine;

namespace Shared
{
    /// <summary>
    /// Placeholder third-person orbit camera. Mouse look, scroll to zoom.
    /// The capture rig disables this component while it drives the camera.
    /// </summary>
    public class OrbitCamera : MonoBehaviour
    {
        public Transform target;
        public float distance = 6f;
        public float yaw = 20f;
        public float pitch = 15f;
        public float sensitivity = 3f;
        public Vector3 targetOffset = new Vector3(0, 1.2f, 0);

        void LateUpdate()
        {
            if (target == null) return;
            if (Application.isFocused)
            {
                yaw += Input.GetAxis("Mouse X") * sensitivity;
                pitch -= Input.GetAxis("Mouse Y") * sensitivity;
                pitch = Mathf.Clamp(pitch, -30f, 80f);
                distance = Mathf.Clamp(distance - Input.GetAxis("Mouse ScrollWheel") * 3f, 2f, 30f);
            }
            var rot = Quaternion.Euler(pitch, yaw, 0);
            var focus = target.position + targetOffset;
            transform.position = focus - rot * Vector3.forward * distance;
            transform.rotation = rot;
        }
    }
}
