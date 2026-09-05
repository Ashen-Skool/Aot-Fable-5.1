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
        public Vector3 Forward => Velocity.sqrMagnitude > 25f ? Velocity.normalized : transform.forward; // only used by the rig at speed
        public Transform Root => transform;
        public CameraTargetState State
        {
            get
            {
                var s = CameraTargetState.None;
                if (c == null) return s;
                s |= c.Grounded ? CameraTargetState.Grounded : CameraTargetState.Flying;
                if (c.Boosting) s |= CameraTargetState.Boosting;
                if (c.Hook != HookState.None) s |= CameraTargetState.Hooked;
                if (hitLatch) { s |= CameraTargetState.Hit; hitLatch = false; }
                return s;
            }
        }
        public void Hit() => hitLatch = true;
        void Awake() { c = GetComponent<OdmController>(); rb = GetComponent<Rigidbody>(); }
        void Start() { if (rb == null) rb = GetComponent<Rigidbody>(); }
    }
}
