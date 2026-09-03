using UnityEngine;
using Shared.Cam;

namespace ODM
{
    /// <summary>Exposes the ODM player to the camera rig as its ICameraTarget.</summary>
    public class OdmCameraTarget : MonoBehaviour, ICameraTarget
    {
        OdmController c; Rigidbody rb; bool hitLatch;
        public Vector3 Position => transform.position; // feet; the rig adds its own pivot height
        public Vector3 Velocity => rb != null ? rb.linearVelocity : Vector3.zero;
        /// <summary>Heading for the rig: the flight direction at speed; otherwise the camera's own flat forward so the
        /// rig never follows the body (the body faces the camera, which would make the view spin on its own).</summary>
        public Vector3 Forward
        {
            get
            {
                if (Velocity.sqrMagnitude > 25f) return Velocity.normalized;
                var cam = Camera.main; if (cam == null) return transform.forward;
                var f = cam.transform.forward; f.y = 0f;
                return f.sqrMagnitude > 1e-4f ? f.normalized : transform.forward;
            }
        }
        public Transform Root => transform;
        public CameraTargetState State
        {
            get
            {
                var s = CameraTargetState.None;
                if (c == null) return s;
                s |= c.Grounded ? CameraTargetState.Grounded : CameraTargetState.Flying;
                if (c.Boosting) s |= CameraTargetState.Boosting;
                if (hitLatch) { s |= CameraTargetState.Hit; hitLatch = false; }
                return s;
            }
        }
        public void Hit() => hitLatch = true;
        void Awake() { c = GetComponent<OdmController>(); rb = GetComponent<Rigidbody>(); }
        void Start() { if (rb == null) rb = GetComponent<Rigidbody>(); }
    }
}
