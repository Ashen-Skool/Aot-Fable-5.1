using Shared;
using Shared.Cam;
using UnityEngine;

namespace AotCamera
{
    public enum CameraMode { Chase, KillCam, Dive }

    /// <summary>
    /// Piece 3: third-person chase camera for ODM speeds. Registered in Ctx as "cameraRig".
    /// Follows whatever ICameraTarget is registered as "cameraTarget" (or explicitTarget).
    /// Chase: spring-damped follow tuned for 40 m/s, heading lag, look-ahead, bank roll,
    /// sphere-cast collision. Boost -> FOV 70..95 kick. Speed -> radial speed lines.
    /// Hit/landing -> trauma shake. KillCam(point): slow-mo orbit for 3 s real time, then
    /// snaps back. CinematicDive(from, look, dur): opening shot from the wall into the street.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public const string CtxName = "cameraRig";

        [Header("Chase")]
        public float pivotHeight = 1.0f;
        public float distanceIdle = 2.8f;
        public float distanceFast = 3.8f;
        public float boostDistanceMul = 0.65f;      // pull in while the FOV kicks so she does not shrink
        public float heightIdle = 1.4f;
        public float heightFast = 1.55f;
        public float speedRef = 40f;
        public float posSmoothTime = 0.11f;
        public float headingSharpness = 6f;
        public float rotSharpness = 14f;
        public float lookAheadTime = 0.14f;
        public float maxHeadingPitchDeg = 55f;      // climbing: the camera may swing well below
        public float maxHeadingDiveDeg = 22f;       // diving: keep the horizon in frame
        public float bankDeg = 7f;
        public float collisionRadius = 0.35f;
        public float minCollisionDistance = 0.6f;
        public LayerMask collisionMask = ~0;

        [Header("FOV")]
        public float baseFov = 70f;
        public float boostFov = 95f;
        public float fovRiseTime = 0.16f;
        public float fovFallTime = 0.45f;

        [Header("Speed lines")]
        public float linesStartSpeed = 16f;
        public float linesFullSpeed = 40f;
        public float linesBoostBonus = 0.4f;

        [Header("Kill cam")]
        public float killCamDuration = 3f;
        public float killCamTimeScale = 0.2f;
        public float killCamRadius = 9.5f;
        public float killCamLines = 0.85f;
        public float killCamVignette = 0.8f;
        public float killCamSweepDeg = 150f;
        public float killCamFovStart = 52f;
        public float killCamFovEnd = 40f;

        [Header("Dive")]
        public float diveFov = 58f;

        /// <summary>Overrides the Ctx target (tests, cutscenes).</summary>
        public ICameraTarget explicitTarget;
        /// <summary>Used only while nothing is registered as "cameraTarget" (the demo flight).</summary>
        public ICameraTarget fallbackTarget;
        /// <summary>False while another piece's capture owns the camera: the rig then does nothing.</summary>
        public bool driveCamera = true;
        /// <summary>Log the rig state every N frames (0 = off). Set with -camlog N.</summary>
        public int logEvery = 0;

        public CameraMode Mode { get; private set; } = CameraMode.Chase;
        public ICameraTarget Target { get; private set; }
        public Camera Cam { get; private set; }
        public SpeedLines Lines { get; private set; }
        public float Speed { get; private set; }
        public float Trauma => shake.Trauma;
        public float Fov => Cam != null ? Cam.fieldOfView : 0f;
        public float KillCamProgress => Mode == CameraMode.KillCam ? Mathf.Clamp01(killT / killCamDuration) : 0f;

        readonly CameraShake shake = new CameraShake();
        readonly RaycastHit[] hits = new RaycastHit[16];
        Vector3 smoothedPos, posVel, headingDir = Vector3.forward, lastVelocity;
        Quaternion smoothedRot = Quaternion.identity;
        float fovVel, roll, rollVel, mouseYaw, mousePitch;
        bool snapNext = true;
        CameraTargetState prevState;
        // kill cam
        Vector3 killPoint; float killT, killYaw0, savedTimeScale = 1f, savedFixedDt = 1f / 60f;
        // dive
        Vector3 diveFrom, diveLook; float diveT, diveDur;

        void Awake()
        {
            Cam = GetComponent<Camera>();
            if (Cam == null) Cam = gameObject.AddComponent<Camera>();
            Lines = SpeedLines.Create(Cam, Ctx.Get<int>("seed"));
            smoothedPos = transform.position;
            smoothedRot = transform.rotation;
        }

        void OnDisable()
        {
            if (Mode == CameraMode.KillCam) EndKillCam();
        }

        // ---------------------------------------------------------------- public API

        public void Shake(float trauma) => shake.Add(trauma);

        /// <summary>Slow-motion orbit around a world point (the nape) for killCamDuration real seconds, then snap back to chase.</summary>
        public void KillCam(Vector3 point)
        {
            if (Mode != CameraMode.KillCam)
            {
                savedTimeScale = Time.timeScale > 0.5f ? Time.timeScale : 1f;
                savedFixedDt = Time.fixedDeltaTime;
                Time.timeScale = killCamTimeScale;
                Time.fixedDeltaTime = savedFixedDt * killCamTimeScale;
            }
            Mode = CameraMode.KillCam;
            killPoint = point;
            killT = 0f;
            var toCam = transform.position - point; toCam.y = 0f;
            if (toCam.sqrMagnitude < 0.01f) toCam = -headingDir; toCam.y = 0f;
            if (toCam.sqrMagnitude < 0.01f) toCam = Vector3.back;
            killYaw0 = Mathf.Atan2(toCam.x, toCam.z) * Mathf.Rad2Deg;
            Lines.Burst(1.1f);
            shake.Add(0.45f);
        }

        /// <summary>Opening shot: hold at <paramref name="from"/> looking at <paramref name="lookAt"/>, then dive down into the chase position over <paramref name="duration"/> seconds.</summary>
        public void CinematicDive(Vector3 from, Vector3 lookAt, float duration)
        {
            if (Mode == CameraMode.KillCam) EndKillCam();
            Mode = CameraMode.Dive;
            diveFrom = from; diveLook = lookAt; diveDur = Mathf.Max(0.1f, duration); diveT = 0f;
            transform.position = from;
            transform.rotation = Quaternion.LookRotation(lookAt - from, Vector3.up);
            Cam.fieldOfView = diveFov;
            fovVel = 0f;
        }

        /// <summary>Drop the springs and put the camera straight at its desired chase position next frame.</summary>
        public void SnapToTarget() => snapNext = true;

        // ---------------------------------------------------------------- update

        void ResolveTarget()
        {
            var t = explicitTarget ?? Ctx.Get<ICameraTarget>(ICameraTarget.CtxName);
            if (t is Object o && o == null) t = null;
            if (t == null) { t = fallbackTarget; if (t is Object f && f == null) t = null; }
            if (!ReferenceEquals(t, Target)) { Target = t; snapNext = true; }
        }

        void LateUpdate()
        {
            if (!driveCamera) { Lines.Tick(Cam, 0f, 0f); return; }
            ResolveTarget();
            // "real" dt for things that must ignore slow-mo. Not Time.unscaledDeltaTime: under
            // Time.captureFramerate that is wall-clock time per frame (~1 ms), not the captured step.
            float dt = Time.deltaTime;
            float udt = Time.timeScale > 1e-4f ? dt / Time.timeScale : Time.unscaledDeltaTime;
            if (Target == null)
            {
                if (Mode == CameraMode.KillCam) UpdateKillCam(udt); // still legal without a target
                Lines.SetVignette(Mode == CameraMode.KillCam ? killCamVignette : 0f);
                Lines.Tick(Cam, Mode == CameraMode.KillCam ? killCamLines : 0f, udt);
                return;
            }

            var st = Target.State;
            var v = Target.Velocity;
            Speed = v.magnitude;
            if ((st & CameraTargetState.Hit) != 0 && (prevState & CameraTargetState.Hit) == 0) shake.Add(0.8f);
            if ((st & CameraTargetState.Grounded) != 0 && (prevState & CameraTargetState.Grounded) == 0)
                shake.Add(Mathf.Clamp01(Mathf.Max(0f, -lastVelocity.y) / 30f) * 0.7f);
            prevState = st;
            lastVelocity = v;

            float fovTarget;
            switch (Mode)
            {
                case CameraMode.KillCam:
                    UpdateKillCam(udt);
                    fovTarget = Mathf.Lerp(killCamFovStart, killCamFovEnd, KillCamProgress);
                    break;
                case CameraMode.Dive:
                    UpdateDive(dt, v);
                    fovTarget = Mathf.Lerp(diveFov, baseFov, Mathf.Clamp01(diveT / diveDur));
                    break;
                default:
                    UpdateChase(dt, v, st);
                    fovTarget = (st & CameraTargetState.Boosting) != 0 ? boostFov : baseFov;
                    break;
            }

            // FOV kick: fast up, slower down; real time so slow-mo does not freeze it
            bool rising = fovTarget > Cam.fieldOfView;
            Cam.fieldOfView = Mode == CameraMode.KillCam && killT < udt * 2f
                ? fovTarget
                : Mathf.SmoothDamp(Cam.fieldOfView, fovTarget, ref fovVel, rising ? fovRiseTime : fovFallTime, Mathf.Infinity, udt);

            // shake on top of everything
            shake.Update(udt);
            transform.position += transform.rotation * shake.PosOffset;
            transform.rotation *= Quaternion.Euler(shake.RotOffset);

            // speed lines
            float steady = Mathf.InverseLerp(linesStartSpeed, linesFullSpeed, Speed);
            steady = Mathf.Pow(steady, 1.5f) * 0.9f;
            if ((st & CameraTargetState.Boosting) != 0 && Mode == CameraMode.Chase) steady += linesBoostBonus;
            if (Mode == CameraMode.KillCam) steady = killCamLines * (1f - 0.45f * KillCamProgress);   // slow-mo streaks hold through the orbit
            else if (Mode != CameraMode.Chase) steady *= 0.25f;
            Lines.SetVignette(Mode == CameraMode.KillCam ? killCamVignette : 0f);
            Lines.Tick(Cam, steady, udt);
            if (logEvery > 0 && Time.frameCount % logEvery == 0)
                Debug.Log("[CameraRig] f=" + Time.frameCount + " t=" + Time.time.ToString("0.00") + " mode=" + Mode + " speed=" + Speed.ToString("0.0") + " fov=" + Cam.fieldOfView.ToString("0.0") + " lines=" + Lines.Intensity.ToString("0.00") + " vis=" + Lines.Visible + " trauma=" + shake.Trauma.ToString("0.00") + " ts=" + Time.timeScale + " dt=" + dt.ToString("0.0000") + " udt=" + udt.ToString("0.0000"));
        }

        Vector3 ChaseDesired(Vector3 pivot, Vector3 v, float dt, out float speed01)
        {
            speed01 = Mathf.Clamp01(Speed / speedRef);
            var want = Target.Forward;
            if (Speed > 2f) want = Vector3.Slerp(want, v / Speed, Mathf.Clamp01((Speed - 2f) / 8f));
            if (want.sqrMagnitude < 1e-4f) want = headingDir;
            want.Normalize();
            if ((Target.State & CameraTargetState.Grounded) != 0) { want.y = 0f; if (want.sqrMagnitude < 1e-4f) want = headingDir; want.Normalize(); }
            want = ClampPitch(want, want.y < 0f ? maxHeadingDiveDeg : maxHeadingPitchDeg);
            headingDir = snapNext ? want : Vector3.Slerp(headingDir, want, 1f - Mathf.Exp(-headingSharpness * dt));
            headingDir.Normalize();

            var orbit = Quaternion.AngleAxis(mouseYaw, Vector3.up) * headingDir;
            var right = Vector3.Cross(Vector3.up, orbit).normalized;
            orbit = Quaternion.AngleAxis(-mousePitch, right) * orbit;

            float dist = Mathf.Lerp(distanceIdle, distanceFast, speed01);
            if ((Target.State & CameraTargetState.Boosting) != 0) dist *= boostDistanceMul;
            float height = Mathf.Lerp(heightIdle, heightFast, speed01);
            // when the heading already pitches down the orbit is above the target: drop the extra height so the view stays shallow
            float downFrac = Mathf.Clamp01(-orbit.y / Mathf.Sin(maxHeadingDiveDeg * Mathf.Deg2Rad));
            height *= 1f - 0.75f * downFrac;
            return pivot - orbit * dist + Vector3.up * height;
        }

        void UpdateChase(float dt, Vector3 v, CameraTargetState st)
        {
            ReadMouse(dt);
            var pivot = Target.Position + Vector3.up * pivotHeight;
            var desired = ChaseDesired(pivot, v, dt, out float speed01);

            // feed-forward cancels the steady-state lag of the spring at constant velocity
            float smooth = Mathf.Lerp(posSmoothTime * 1.5f, posSmoothTime * 0.6f, speed01);
            var lead = desired + v * (smooth * 0.8f);
            lead = ResolveCollision(pivot, lead);
            if (snapNext) { smoothedPos = lead; posVel = Vector3.zero; }
            else smoothedPos = Vector3.SmoothDamp(smoothedPos, lead, ref posVel, smooth, Mathf.Infinity, dt);
            smoothedPos = ResolveCollision(pivot, smoothedPos);

            // look-ahead follows the horizontal motion; falling should not drag the horizon out of frame
            var lead2 = v; lead2.y = (st & CameraTargetState.Grounded) != 0 ? 0f : Mathf.Max(lead2.y, 0f) * 0.5f;
            var lookAt = pivot + lead2 * lookAheadTime;
            var dir = lookAt - smoothedPos;
            if (dir.sqrMagnitude < 1e-4f) dir = headingDir;
            var rot = Quaternion.LookRotation(dir, Vector3.up);
            smoothedRot = snapNext ? rot : Quaternion.Slerp(smoothedRot, rot, 1f - Mathf.Exp(-rotSharpness * dt));

            float lateral = Vector3.Dot(v, smoothedRot * Vector3.right) / speedRef;
            float rollTarget = -lateral * bankDeg;
            roll = snapNext ? rollTarget : Mathf.SmoothDamp(roll, rollTarget, ref rollVel, 0.25f, Mathf.Infinity, dt);

            transform.position = smoothedPos;
            transform.rotation = smoothedRot * Quaternion.Euler(0, 0, roll);
            snapNext = false;
        }

        void UpdateKillCam(float udt)
        {
            killT += udt;
            float u = Mathf.Clamp01(killT / killCamDuration);
            float e = 1f - (1f - u) * (1f - u);                 // ease-out sweep
            float yaw = killYaw0 + killCamSweepDeg * e;
            float pitch = Mathf.Lerp(30f, 10f, e);
            float radius = killCamRadius * Mathf.Lerp(1.15f, 0.8f, e);
            var viewDir = Quaternion.Euler(pitch, yaw + 180f, 0f) * Vector3.forward;
            var pos = killPoint - viewDir * radius;
            pos = ResolveCollision(killPoint, pos);
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(killPoint - pos, Vector3.up);
            if (killT >= killCamDuration) EndKillCam();
        }

        void EndKillCam()
        {
            Time.timeScale = savedTimeScale;
            Time.fixedDeltaTime = savedFixedDt;
            Mode = CameraMode.Chase;
            snapNext = true; // snap back
        }

        void UpdateDive(float dt, Vector3 v)
        {
            diveT += dt;
            float u = Mathf.Clamp01(diveT / diveDur);
            float e = u * u * (3f - 2f * u);                    // hold on the wall, then the drop
            var pivot = Target.Position + Vector3.up * pivotHeight;
            var chaseEnd = ResolveCollision(pivot, ChaseDesired(pivot, v, dt, out _));
            var pos = Vector3.Lerp(diveFrom, chaseEnd, e);
            float el = Mathf.Clamp01(u * 1.6f); el = el * el * (3f - 2f * el); // the look finds Mikasa before the camera arrives
            var flat = headingDir; flat.y = 0f; if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward; flat.Normalize();
            // look past Mikasa into the street while dropping so the horizon stays in frame, settle on her at the end
            var look = Vector3.Lerp(diveLook, pivot + Vector3.up * 1f + flat * 14f * (1f - e), el);
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(look - pos, Vector3.up);
            if (u >= 1f)
            {
                Mode = CameraMode.Chase;
                smoothedPos = pos; posVel = Vector3.zero; smoothedRot = transform.rotation; roll = 0f;
                snapNext = false;
            }
        }

        Vector3 ResolveCollision(Vector3 from, Vector3 to)
        {
            var d = to - from;
            float len = d.magnitude;
            if (len < 1e-3f) return to;
            d /= len;
            int n = Physics.SphereCastNonAlloc(from, collisionRadius, d, hits, len, collisionMask, QueryTriggerInteraction.Ignore);
            float best = len;
            var root = Target != null ? Target.Root : null;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h.distance <= 0f) continue;
                if (root != null && h.transform.IsChildOf(root)) continue;
                if (h.distance < best) best = h.distance;
            }
            if (best >= len) return to;
            return from + d * Mathf.Max(best, minCollisionDistance);
        }

        void ReadMouse(float dt)
        {
            if (!Application.isFocused || Application.isBatchMode) return;
            mouseYaw += Input.GetAxis("Mouse X") * 2.5f;
            mousePitch = Mathf.Clamp(mousePitch + Input.GetAxis("Mouse Y") * 2.0f, -35f, 45f);
            // drift back behind the character while moving fast
            float recenter = Mathf.Clamp01((Speed - 8f) / 20f) * 3f * dt;
            mouseYaw = Mathf.Lerp(mouseYaw, 0f, recenter);
            mousePitch = Mathf.Lerp(mousePitch, 0f, recenter);
        }

        static Vector3 ClampPitch(Vector3 d, float maxDeg)
        {
            float maxY = Mathf.Sin(maxDeg * Mathf.Deg2Rad);
            if (Mathf.Abs(d.y) <= maxY) return d;
            float y = Mathf.Sign(d.y) * maxY;
            var xz = new Vector3(d.x, 0, d.z);
            if (xz.sqrMagnitude < 1e-6f) xz = Vector3.forward; else xz.Normalize();
            return xz * Mathf.Cos(maxDeg * Mathf.Deg2Rad) + Vector3.up * y;
        }
    }
}
