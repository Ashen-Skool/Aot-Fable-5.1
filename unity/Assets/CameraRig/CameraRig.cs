using Shared;
using Shared.Cam;
using Shared.Capture;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AotCamera
{
    public enum CameraMode { Chase, KillCam, Dive }

    /// <summary>
    /// Piece 3: third-person chase camera for ODM speeds. Registered in Ctx as "cameraRig".
    /// Follows whatever ICameraTarget is registered as "cameraTarget" (or explicitTarget).
    /// Chase: over-the-right-shoulder at shoulder height, 3-4 m back, Mikasa framed in the
    /// lower-left third with the street ahead. Target lock (lockTarget / Ctx "cameraLockTarget"):
    /// the heading turns toward the target and the frame holds both Mikasa (lower left) and the
    /// target (upper right, lead room in the travel direction). Boost -> FOV 70..95 kick.
    /// Speed -> radial speed lines. Hit/landing -> trauma shake. KillCam(point): slow-mo orbit
    /// sweeping 90 degrees around the nape with Mikasa small in the foreground, then snaps back.
    /// CinematicDive(from, look, dur): opening shot from the wall, ending low behind Mikasa,
    /// tilted forward down the street.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public const string CtxName = "cameraRig";
        public const string LockCtxName = "cameraLockTarget";

        [Header("Chase")]
        public float pivotHeight = 1.35f;            // shoulder
        public float shoulderRight = 0f;                // centered on Mikasa (user call); lock mode widens it
        public float distanceIdle = 3.2f;
        public float distanceFast = 4.0f;
        public float boostDistanceMul = 0.82f;       // pull in a little while the FOV kicks so she does not shrink
        public float heightIdle = 1.3f;              // above the shoulder pivot
        public float heightFast = 1.6f;
        public Vector2 playerScreen = new Vector2(0.36f, 0.30f);   // where the pivot sits in the frame (0..1, from bottom-left)
        public float speedRef = 40f;
        public float posSmoothTime = 0.11f;
        public float headingSharpness = 6f;
        public float rotSharpness = 14f;
        public float lookAheadTime = 0.02f;
        public float maxHeadingPitchDeg = 45f;      // climbing: the camera may swing well below
        public float maxHeadingDiveDeg = 20f;       // diving: keep the horizon in frame
        public float bankDeg = 6f;                  // roll from lateral velocity
        public float dutchDeg = 12f;                // max roll into a turn (heading yaw rate)
        public float dutchYawRate = 110f;           // deg/s of heading change that gives the full dutch
        public float collisionRadius = 0.35f;
        public float minCollisionDistance = 0.6f;
        public LayerMask collisionMask = ~0;

        [Header("Target lock")]
        /// <summary>World transform the chase frame keeps in view (the Titan). Null = plain chase. Ctx "cameraLockTarget" is used when this is null.</summary>
        public Transform lockTarget;
        public Vector2 lockScreen = new Vector2(0.62f, 0.60f);
        public float lockBlend = 0.5f;               // 0 = frame only Mikasa, 1 = frame only the target
        public float lockHeadingBlend = 0.55f;       // heading pulled toward the target
        public float lockShoulderMul = 1.35f;        // (legacy) kept for tuning files
        public float lockShoulder = 1.15f;           // shoulder offset while locked so the target is not hidden behind her
        public Vector2 lockSafe = new Vector2(0.16f, 0.16f);   // the lock blend backs off before it pushes her out of the frame

        [Header("FOV")]
        public float baseFov = 70f;
        public float boostFov = 95f;
        public float fovRiseTime = 0.16f;
        public float fovFallTime = 0.45f;

        [Header("Speed lines")]
        public float linesStartSpeed = 30f;         // subtle, and only past 30 m/s: motion blur carries the speed below that
        public float linesFullSpeed = 46f;
        public float linesMax = 0.4f;
        public float linesBoostBonus = 0.12f;

        [Header("Motion blur")]
        public float blurFullSpeed = 45f;
        public float blurMax = 0.75f;               // URP MotionBlur intensity at blurFullSpeed
        public float blurKillCam = 0.2f;

        [Header("Kill cam")]
        public float killCamDuration = 3f;
        public float killCamTimeScale = 0.2f;
        public float killCamRadiusStart = 11f;
        public float killCamRadiusEnd = 8.5f;
        public float killCamLines = 0.3f;
        public float killCamVignette = 0.8f;
        public float killCamSweepDeg = 120f;         // yaw swept across Mikasa's side of the nape
        public float killCamPitchStart = 22f;
        public float killCamPitchEnd = 6f;
        public float killCamFovStart = 50f;
        public float killCamFovEnd = 42f;
        public Vector2 killCamScreen = new Vector2(0.5f, 0.64f);    // the nape high in frame, the body filling the lower two thirds
        public float killCamPlayerBlend = 0.22f;

        [Header("Dive")]
        public float diveFov = 62f;
        public float diveDistance = 3.8f;
        public float diveHeight = 2.6f;
        public float diveShoulder = 0.9f;
        public Vector2 diveScreen = new Vector2(0.38f, 0.30f);

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
        public Transform Lock { get; private set; }
        public Camera Cam { get; private set; }
        public SpeedLines Lines { get; private set; }
        public float Speed { get; private set; }
        public float Trauma => shake.Trauma;
        public float Roll => roll;
        public float BlurIntensity => blur != null ? blur.intensity.value : 0f;
        public float Fov => Cam != null ? Cam.fieldOfView : 0f;
        public float KillCamProgress => Mode == CameraMode.KillCam ? Mathf.Clamp01(killT / killCamDuration) : 0f;
        /// <summary>Current kill-cam orbit yaw in degrees (world), for tests and logs.</summary>
        public float KillCamYaw { get; private set; }

        readonly CameraShake shake = new CameraShake();
        readonly RaycastHit[] hits = new RaycastHit[16];
        Vector3 smoothedPos, posVel, headingDir = Vector3.forward, lastVelocity;
        Quaternion smoothedRot = Quaternion.identity;
        float fovVel, roll, rollVel, mouseYaw, mousePitch, prevHeadingYaw, yawRate;
        MotionBlur blur;
        Volume volume;
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
            BuildMotionBlur();
            smoothedPos = transform.position;
            smoothedRot = transform.rotation;
        }

        void OnDisable()
        {
            if (Mode == CameraMode.KillCam) EndKillCam();
        }

        /// <summary>Real URP motion blur through a runtime global Volume; intensity follows target speed every frame.</summary>
        void BuildMotionBlur()
        {
            var data = Cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "CameraRigVolume";
            blur = profile.Add<MotionBlur>(true);
            blur.mode.value = MotionBlurMode.CameraAndObjects;
            blur.quality.value = MotionBlurQuality.High;
            blur.intensity.value = 0f;
            blur.clamp.value = 0.08f;
            var go = new GameObject("CameraRigVolume");
            go.transform.SetParent(transform, false);
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;
        }

        void OnDestroy()
        {
            if (volume != null && volume.sharedProfile != null) Destroy(volume.sharedProfile);
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
            // orbit across Mikasa's side of the nape: she stays in the foreground the whole sweep
            var side = (Target != null ? Target.Position : transform.position) - point; side.y = 0f;
            if (side.sqrMagnitude < 0.01f) { side = -headingDir; side.y = 0f; }
            if (side.sqrMagnitude < 0.01f) side = Vector3.back;
            killYaw0 = Mathf.Atan2(side.x, side.z) * Mathf.Rad2Deg + killCamSweepDeg * 0.5f;
            Lines.Burst(1.1f);
            shake.Add(0.45f);
        }

        /// <summary>Opening shot: hold at <paramref name="from"/> looking at <paramref name="lookAt"/>, then dive down to a low, forward-tilted pose behind Mikasa over <paramref name="duration"/> seconds.</summary>
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

        /// <summary>Rotation that places <paramref name="point"/> at viewport position <paramref name="screen"/> (0..1 from bottom-left) for a camera at <paramref name="camPos"/>. Zero roll.</summary>
        public static Quaternion FrameAt(Vector3 camPos, Vector3 point, Vector2 screen, float fovDeg, float aspect)
        {
            var d = point - camPos;
            if (d.sqrMagnitude < 1e-6f) return Quaternion.identity;
            d.Normalize();
            float t = Mathf.Tan(fovDeg * 0.5f * Mathf.Deg2Rad);
            float ax = (screen.x - 0.5f) * 2f * t * aspect;
            float ay = (screen.y - 0.5f) * 2f * t;
            float yawOff = Mathf.Atan(ax) * Mathf.Rad2Deg;
            float pitchOff = Mathf.Atan(ay / Mathf.Sqrt(1f + ax * ax)) * Mathf.Rad2Deg;
            float yawD = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitchD = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
            return Quaternion.Euler(-(pitchD - pitchOff), yawD - yawOff, 0f);
        }

        /// <summary>Viewport position (0..1) of <paramref name="p"/> for a camera at <paramref name="camPos"/> with rotation <paramref name="rot"/>; (-1,-1) when behind.</summary>
        public static Vector2 Project(Quaternion rot, Vector3 camPos, Vector3 p, float fovDeg, float aspect)
        {
            var l = Quaternion.Inverse(rot) * (p - camPos);
            if (l.z < 0.01f) return new Vector2(-1f, -1f);
            float t = Mathf.Tan(fovDeg * 0.5f * Mathf.Deg2Rad);
            return new Vector2(0.5f + l.x / (l.z * 2f * t * aspect), 0.5f + l.y / (l.z * 2f * t));
        }

        float Aspect => Cam != null && Cam.aspect > 1.2f && Cam.aspect < 2.6f ? Cam.aspect : 16f / 9f;

        // ---------------------------------------------------------------- update

        void ResolveTarget()
        {
            var t = explicitTarget ?? Ctx.Get<ICameraTarget>(ICameraTarget.CtxName);
            if (t is Object o && o == null) t = null;
            if (t == null) { t = fallbackTarget; if (t is Object f && f == null) t = null; }
            if (!ReferenceEquals(t, Target)) { Target = t; snapNext = true; }
            var l = lockTarget != null ? lockTarget : Ctx.Get<Transform>(LockCtxName);
            if (l == null) l = null; // destroyed transform -> real null
            Lock = l;
        }

        void LateUpdate()
        {
            // passive while a capture places the camera itself (poses without "camera": "game"):
            // the rig keeps simulating (dive / kill cam timers, springs) but leaves the transform alone
            bool passive = !driveCamera || (CaptureRunner.Capturing && !CaptureRunner.LiveCamera);
            var keepPos = transform.position; var keepRot = transform.rotation; float keepFov = Cam.fieldOfView;
            Step();
            if (passive)
            {
                transform.SetPositionAndRotation(keepPos, keepRot);
                Cam.fieldOfView = keepFov;
                Lines.SetVignette(0f); Lines.Tick(Cam, 0f, 0f);
                if (blur != null) blur.intensity.value = 0f;
            }
        }

        void Step()
        {
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

            // motion blur from speed (real URP post effect); a little during the slow-mo orbit
            if (blur != null)
                blur.intensity.value = Mode == CameraMode.KillCam ? blurKillCam : Mathf.Clamp01(Speed / blurFullSpeed) * blurMax;

            // speed lines: subtle, only past linesStartSpeed
            float steady = Mathf.InverseLerp(linesStartSpeed, linesFullSpeed, Speed);
            steady = Mathf.Pow(steady, 1.5f) * linesMax;
            if ((st & CameraTargetState.Boosting) != 0 && Mode == CameraMode.Chase) steady += linesBoostBonus;
            if (Mode == CameraMode.KillCam) steady = killCamLines * (1f - 0.45f * KillCamProgress);   // slow-mo streaks hold through the orbit
            else if (Mode != CameraMode.Chase) steady *= 0.25f;
            Lines.SetVignette(Mode == CameraMode.KillCam ? killCamVignette : 0f);
            Lines.Tick(Cam, steady, udt);
            if (logEvery > 0 && Time.frameCount % logEvery == 0)
                Debug.Log("[CameraRig] f=" + Time.frameCount + " t=" + Time.time.ToString("0.00") + " mode=" + Mode + " lock=" + (Lock != null) + " speed=" + Speed.ToString("0.0") + " fov=" + Cam.fieldOfView.ToString("0.0") + " lines=" + Lines.Intensity.ToString("0.00") + " vis=" + Lines.Visible + " trauma=" + shake.Trauma.ToString("0.00") + " ts=" + Time.timeScale + " pos=" + transform.position.ToString("0.0") + " kyaw=" + KillCamYaw.ToString("0"));
        }

        Vector3 ChaseDesired(Vector3 pivot, Vector3 v, float dt, out float speed01, out Vector3 right)
        {
            speed01 = Mathf.Clamp01(Speed / speedRef);
            // Slow and unlocked: the heading is frozen and the mouse offset is absolute. Following the body here is a
            // feedback loop (the body faces the camera), and following the camera integrates the mouse offset every frame.
            var want = (Speed < 10f && Lock == null) ? headingDir : Target.Forward;
            if (Speed > 2f) want = Vector3.Slerp(want, v / Speed, Mathf.Clamp01((Speed - 2f) / 8f) * Mathf.Clamp01((Speed - 6f) / 6f));
            if (want.sqrMagnitude < 1e-4f) want = headingDir;
            want.Normalize();
            if ((Target.State & CameraTargetState.Grounded) != 0) { want.y = 0f; if (want.sqrMagnitude < 1e-4f) want = headingDir; want.Normalize(); }
            if (Lock != null)
            {
                // locked: the heading turns toward the target so it stays framed even at speed
                var toLock = Lock.position - pivot; toLock.y *= 0.35f;
                if (toLock.sqrMagnitude > 1f) want = Vector3.Slerp(want, toLock.normalized, lockHeadingBlend).normalized;
            }
            want = ClampPitch(want, want.y < 0f ? maxHeadingDiveDeg : maxHeadingPitchDeg);
            headingDir = snapNext ? want : Vector3.Slerp(headingDir, want, 1f - Mathf.Exp(-headingSharpness * dt));
            headingDir.Normalize();

            var orbit = Quaternion.AngleAxis(mouseYaw, Vector3.up) * headingDir;
            right = Vector3.Cross(Vector3.up, orbit).normalized;
            orbit = Quaternion.AngleAxis(-mousePitch, right) * orbit;

            float dist = Mathf.Lerp(distanceIdle, distanceFast, speed01);
            if ((Target.State & CameraTargetState.Boosting) != 0) dist *= boostDistanceMul;
            float height = Mathf.Lerp(heightIdle, heightFast, speed01);
            // when the heading already pitches down the orbit is above the target: drop the extra height so the view stays shallow
            float downFrac = Mathf.Clamp01(-orbit.y / Mathf.Sin(maxHeadingDiveDeg * Mathf.Deg2Rad));
            height *= 1f - 0.6f * downFrac;
            float shoulder = Lock != null ? lockShoulder : shoulderRight;   // centered in free flight, over-the-shoulder only when locked on a titan
            return pivot - orbit * dist + right * shoulder + Vector3.up * height;
        }

        void UpdateChase(float dt, Vector3 v, CameraTargetState st)
        {
            ReadMouse(dt);
            var pivot = Target.Position + Vector3.up * pivotHeight;
            var desired = ChaseDesired(pivot, v, dt, out float speed01, out _);

            // feed-forward cancels the steady-state lag of the spring at constant velocity
            float smooth = Mathf.Lerp(posSmoothTime * 1.5f, posSmoothTime * 0.6f, speed01);
            var lead = desired + v * (smooth * 0.8f);
            lead = ResolveCollision(pivot, lead);
            if (snapNext) { smoothedPos = lead; posVel = Vector3.zero; }
            else smoothedPos = Vector3.SmoothDamp(smoothedPos, lead, ref posVel, smooth, Mathf.Infinity, dt);
            smoothedPos = ResolveCollision(pivot, smoothedPos);

            // composition: Mikasa in the lower-left third; with a lock, the target in the upper right
            var lead2 = v; lead2.y = (st & CameraTargetState.Grounded) != 0 ? 0f : Mathf.Max(lead2.y, 0f) * 0.5f;
            var framePoint = pivot + lead2 * lookAheadTime;
            float fov = Cam.fieldOfView;
            var rot = FrameAt(smoothedPos, framePoint, playerScreen, fov, Aspect);
            if (Lock != null)
            {
                var rotA = rot;
                var rotB = FrameAt(smoothedPos, Lock.position, lockScreen, fov, Aspect);
                float w = lockBlend;
                for (int i = 0; i < 5; i++)
                {
                    rot = Quaternion.Slerp(rotA, rotB, w);
                    var her = Project(rot, smoothedPos, framePoint, fov, Aspect);
                    if (her.x >= lockSafe.x && her.y >= lockSafe.y) break;
                    w *= 0.6f;
                }
            }
            smoothedRot = snapNext ? rot : Quaternion.Slerp(smoothedRot, rot, 1f - Mathf.Exp(-rotSharpness * dt));

            // dutch tilt: bank into turns (heading yaw rate) plus a little from lateral drift, up to dutchDeg
            float headingYaw = Mathf.Atan2(headingDir.x, headingDir.z) * Mathf.Rad2Deg;
            float rate = snapNext || dt <= 1e-5f ? 0f : Mathf.DeltaAngle(prevHeadingYaw, headingYaw) / dt;
            prevHeadingYaw = headingYaw;
            yawRate = Mathf.Lerp(yawRate, rate, 1f - Mathf.Exp(-12f * dt));
            float lateral = Vector3.Dot(v, smoothedRot * Vector3.right) / speedRef;
            float rollTarget = -Mathf.Clamp(yawRate / dutchYawRate, -1f, 1f) * dutchDeg * Mathf.Clamp01(Speed / 12f) - lateral * bankDeg;
            rollTarget = Mathf.Clamp(rollTarget, -dutchDeg, dutchDeg);
            roll = snapNext ? rollTarget : Mathf.SmoothDamp(roll, rollTarget, ref rollVel, 0.18f, Mathf.Infinity, dt);

            transform.position = smoothedPos;
            transform.rotation = smoothedRot * Quaternion.Euler(0, 0, roll);
            snapNext = false;
        }

        void UpdateKillCam(float udt)
        {
            killT += udt;
            float u = Mathf.Clamp01(killT / killCamDuration);
            float e = 1f - (1f - u) * (1f - u);                 // ease-out sweep
            float yaw = killYaw0 - killCamSweepDeg * e;
            float pitch = Mathf.Lerp(killCamPitchStart, killCamPitchEnd, e);
            float radius = Mathf.Lerp(killCamRadiusStart, killCamRadiusEnd, e);
            KillCamYaw = yaw;
            var offset = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;   // from the nape out to the camera, pitch = elevation
            var pos = killPoint + offset * radius;
            pos = ResolveCollision(killPoint, pos);
            float fov = Mathf.Lerp(killCamFovStart, killCamFovEnd, u);
            var rot = FrameAt(pos, killPoint, killCamScreen, fov, Aspect);
            if (Target != null)
                rot = Quaternion.Slerp(rot, FrameAt(pos, Target.Position + Vector3.up * pivotHeight, new Vector2(0.5f, 0.35f), fov, Aspect), killCamPlayerBlend);
            transform.position = pos;
            transform.rotation = rot;
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
            // the dive pose: low behind her right shoulder, tilted forward, the street stretching ahead
            var flat = v; flat.y = 0f;
            if (flat.sqrMagnitude < 1f) { flat = Target.Forward; flat.y = 0f; }
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            flat.Normalize();
            headingDir = flat;
            var right = Vector3.Cross(Vector3.up, flat);
            var divePos = ResolveCollision(pivot, pivot - flat * diveDistance + right * diveShoulder + Vector3.up * diveHeight);
            // arc over the wall's edge before dropping: a straight line from the wall top would cut through the wall face
            var ctrl = Vector3.Lerp(diveFrom, divePos, 0.35f); ctrl.y = diveFrom.y + 2f;
            var pos = Vector3.Lerp(Vector3.Lerp(diveFrom, ctrl, e), Vector3.Lerp(ctrl, divePos, e), e);
            float el = Mathf.Clamp01(u * 1.6f); el = el * el * (3f - 2f * el); // the look finds Mikasa before the camera arrives
            var rotWall = Quaternion.LookRotation(diveLook - pos, Vector3.up);
            var rotDive = FrameAt(pos, pivot, diveScreen, Cam.fieldOfView, Aspect);
            transform.position = pos;
            transform.rotation = Quaternion.Slerp(rotWall, rotDive, el);
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
                if (Lock != null && h.transform.IsChildOf(Lock)) continue;
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
            // Recentre only at speed: on the ground and in slow flight the mouse is the authority, otherwise the
            // heading (which follows the player, who faces the camera) and the recentre chase each other and the view drifts.
            if (Speed > 10f) { mouseYaw = Mathf.Lerp(mouseYaw, 0f, recenter); mousePitch = Mathf.Lerp(mousePitch, 0f, recenter); }
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
