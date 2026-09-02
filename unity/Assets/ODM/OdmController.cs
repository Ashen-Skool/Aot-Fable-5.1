using UnityEngine;
using Shared;
using Shared.Rigs;
using Pose = Shared.Rigs.Pose;

namespace ODM
{
    public enum HookState { None, Attached }
    public enum FlightState { Ground, Hook, Boost, Swing, Reel, Land }

    /// <summary>
    /// Rigidbody ODM controller. Ground: WASD relative to the look yaw. Air: momentum, light
    /// steering. RMB press fires both hooks at the aim point (raycast against HookTarget +
    /// Titan, 60 m); while held the cables act as a spring-damped tether with a constant
    /// winch pull, Space boosts (gas) mostly where you look and partly along the cable,
    /// Shift reels the cable in and mantles you onto the ledge behind the anchor.
    /// Release = free swing with momentum. Registered in Ctx as "player".
    /// Input comes from Input (live) or a FlightScript (deterministic replay).
    ///
    /// Body: drives the Mikasa proxy's IPoser (Run/Idle/Fly/Land) plus a per-state override
    /// layer on the bones (hook reach, boost streamline, swing roll + trailing leg, reel tuck),
    /// rolls the root into the arc, and draws gas plumes (TrailRenderer) from both hip
    /// sockets while boosting, ghost copies of the body behind it at speed, and a dust burst
    /// on touchdown.
    /// </summary>
    public class OdmController : MonoBehaviour
    {
        // ---- tuning (feel) ----
        public float runSpeed = 8f, runAccel = 45f, groundFriction = 30f;
        public float gravity = 16f;
        public float airSteer = 7f;
        public float airDrag = 0.003f;   // quadratic: a = airDrag * v^2
        public float maxSpeed = 55f;
        public float hookRange = 60f;
        public float ropeSpringK = 60f, ropeSpringDamp = 10f, ropeMaxForce = 90f;
        public float winchPull = 9f;       // constant pull toward the anchor while attached (m/s^2)
        public float hookSnap = 6f;        // velocity kick toward the anchor at hook time (m/s)
        public float hookLaunch = 7f;      // vertical launch when hooking from the ground (m/s)
        public float boostAccel = 38f;     // m/s^2 while Space is held
        public float boostForwardMix = 0.5f;   // boost = mostly where you look, partly along the cable
        public float reelSpeed = 16f;      // m/s of rope shortening
        public float reelAccel = 28f;
        public float reelDetach = 2.2f;    // m from anchor at which a reel pops you off
        public float popUp = 16f, popForward = 6f;  // the ledge pop when there is no roof to mantle onto
        public float reelTangentialDamp = 2.5f, reelMaxSpeed = 30f;
        public float gasMax = 100f, gasDrain = 22f, gasRefill = 18f, hopGas = 6f, hopSpeed = 9f;
        public float streamlineSpeed = 28f; // speed at which the root is fully along its velocity
        public float swingRoll = 38f;      // max roll into the arc (deg)
        public float landTime = 0.45f;     // seconds of the Land crouch
        public float hookReachTime = 0.45f;// seconds of the arms-out hook pose after firing
        public int ghostCount = 3;

        // ---- state (read via Ctx "player") ----
        public float Speed { get; private set; }
        public float Gas { get; private set; }
        public float GasMax => gasMax;
        public HookState Hook { get; private set; }
        public FlightState State { get; private set; }
        public bool Boosting { get; private set; }
        public bool Reeling { get; private set; }
        public bool Grounded { get; private set; }
        public int GroundLayer { get; private set; } = -1;
        public float GroundHeight { get; private set; }
        public Vector3 Velocity => rb != null ? rb.linearVelocity : Vector3.zero;
        public Vector3 Anchor { get; private set; }
        public Vector3 AnchorL { get; private set; }
        public Vector3 AnchorR { get; private set; }
        public float RopeLength { get; private set; }
        public Vector3 AimDir { get; private set; } = Vector3.forward;
        public Vector3 LookDir { get; private set; } = Vector3.forward;
        public float MaxSpeedSeen { get; private set; }
        public float AirTime { get; private set; }
        public float LandTime { get; private set; } = -1f;   // Time.fixedTime of the last landing
        public Vector3 LandSpot { get; private set; }
        public FlightScript Script => script;
        public bool Scripted => script != null && script.Playing;
        public bool Mantling => mantleT > 0f;
        public bool Crouching => crouchT > 0f;
        public OdmInput Input => input;
        public Rigidbody Body => rb;
        /// <summary>Height of the transform pivot above the feet (0 for the proxy, 1 for a primitive capsule).</summary>
        public float PivotAboveFeet { get; private set; }
        public Transform socketL, socketR;
        public bool verbose;

        Rigidbody rb;
        CapsuleCollider capsule;
        LineRenderer cableL, cableR;
        Transform hookHeadL, hookHeadR;
        FlightScript script, recorder;
        OdmInput input, liveInput;
        bool prevHook, prevBoost;
        RaycastHit hit;
        Camera cam;
        float logAccum, hookRetry, hookAge;
        bool wallContact; Vector3 wallNormal; Vector3 hookNormal = Vector3.forward;
        float mantleT, mantleDur = 0.32f; Vector3 mantleFrom, mantleTo, mantleFacing; int mantleGroundLayer;
        float crouchT, preLandSpeed;
        float rollLive;

        // visuals
        HumanoidProxy rig;
        IPoser poser;
        readonly Quaternion[] ovrTarget = new Quaternion[HumanoidProxy.BoneCount];
        readonly bool[] ovrMask = new bool[HumanoidProxy.BoneCount];
        readonly Quaternion[] ovrLive = new Quaternion[HumanoidProxy.BoneCount];
        readonly bool[] ovrLiveValid = new bool[HumanoidProxy.BoneCount];
        Renderer[] ghosts; Transform[] ghostTf; Material[] ghostMats; readonly float ghostBaseAlpha = 0.45f;
        Renderer[] bodyRenderers; Transform[] bodyTfs;
        TrailRenderer plumeL, plumeR;
        ParticleSystem dust, gasPuff;

        // ---------- setup ----------
        public static OdmController Attach(GameObject go)
        {
            var c = go.GetComponent<OdmController>();
            if (c != null) return c;
            return go.AddComponent<OdmController>();
        }

        void Awake()
        {
            if (!TryGetComponent(out rb)) rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.useGravity = false;
            rb.linearDamping = 0f;
            rb.angularDamping = 0f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            if (!TryGetComponent(out capsule)) { capsule = gameObject.AddComponent<CapsuleCollider>(); capsule.height = 2f; capsule.radius = 0.5f; }
            var pm = new PhysicsMaterial("odm") { dynamicFriction = 0f, staticFriction = 0f, frictionCombine = PhysicsMaterialCombine.Minimum, bounciness = 0f, bounceCombine = PhysicsMaterialCombine.Minimum };
            capsule.material = pm;
            PivotAboveFeet = -(capsule.center.y - capsule.height * 0.5f);
            Gas = gasMax;
            rig = GetComponent<HumanoidProxy>();
            poser = rig != null ? rig : Ctx.Get<IPoser>("mikasaPoser");
            socketL = FindSocket("Socket_HookL", new Vector3(-0.28f, PivotAboveFeet * 0.85f, 0.12f));
            socketR = FindSocket("Socket_HookR", new Vector3(0.28f, PivotAboveFeet * 0.85f, 0.12f));
            cableL = MakeCable("Cable_L");
            cableR = MakeCable("Cable_R");
            hookHeadL = MakeHookHead("HookHead_L");
            hookHeadR = MakeHookHead("HookHead_R");
            SetCablesVisible(false);
            BuildFx();
            Ctx.Set("player", this);
        }

        Transform FindSocket(string name, Vector3 fallbackLocal)
        {
            var t = FindDeep(transform, name);
            if (t != null) return t;
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = fallbackLocal;
            return go.transform;
        }

        static Transform FindDeep(Transform root, string name)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i);
                if (c.name == name) return c;
                var r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }

        LineRenderer MakeCable(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.startWidth = 0.05f; lr.endWidth = 0.03f;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = Mats.Unlit(new Color(0.08f, 0.08f, 0.09f));
            return lr;
        }

        Transform MakeHookHead(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * 0.2f;
            go.GetComponent<Renderer>().sharedMaterial = Mats.Lit(new Color(0.75f, 0.72f, 0.65f), 0.6f, 0.9f);
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        void SetCablesVisible(bool on)
        {
            cableL.enabled = on; cableR.enabled = on;
            hookHeadL.gameObject.SetActive(on); hookHeadR.gameObject.SetActive(on);
        }

        // ---------- materials / fx ----------
        static Material Transparent(Material m, Color c, bool additive = false)
        {
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.SrcAlpha));
            m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        /// <summary>Radial soft-edged white puff with alpha falloff.</summary>
        static Texture2D SoftPuffTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(px);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>Soft horizontal band for the plume ribbon: bright core, feathered edges.</summary>
        static Texture2D RibbonTexture(int size)
        {
            var tex = new Texture2D(4, size, TextureFormat.RGBA32, false);
            var px = new Color[4 * size];
            for (int y = 0; y < size; y++)
            {
                float v = Mathf.Abs((y + 0.5f) / size - 0.5f) * 2f;   // 0 centre .. 1 edge
                float a = Mathf.Clamp01(1f - v);
                a = a * a * (3f - 2f * a);
                for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(px);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply(false, false);
            return tex;
        }

        void BuildFx()
        {
            // body parts to ghost: every mesh under the root that is not ours
            var filters = GetComponentsInChildren<MeshFilter>(true);
            int n = 0;
            for (int i = 0; i < filters.Length; i++) if (IsBodyPart(filters[i].transform)) n++;
            bodyRenderers = new Renderer[n]; bodyTfs = new Transform[n];
            int k = 0;
            for (int i = 0; i < filters.Length; i++)
                if (IsBodyPart(filters[i].transform)) { bodyTfs[k] = filters[i].transform; bodyRenderers[k] = filters[i].GetComponent<Renderer>(); k++; }

            ghosts = new Renderer[ghostCount * n]; ghostTf = new Transform[ghostCount * n]; ghostMats = new Material[ghostCount];
            var root = new GameObject("SpeedGhosts").transform;
            for (int g = 0; g < ghostCount; g++)
            {
                float a = ghostBaseAlpha * (1f - g / (float)ghostCount);
                ghostMats[g] = Transparent(Mats.Lit(Color.white, 0f), new Color(0.35f, 0.4f, 0.5f, a));
                for (int b = 0; b < n; b++)
                {
                    var go = new GameObject("Ghost_" + g + "_" + bodyTfs[b].name);
                    go.transform.SetParent(root, false);
                    go.AddComponent<MeshFilter>().sharedMesh = bodyTfs[b].GetComponent<MeshFilter>().sharedMesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = ghostMats[g];
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.enabled = false;
                    ghosts[g * n + b] = r; ghostTf[g * n + b] = go.transform;
                }
            }

            // gas plumes: additive soft ribbons trailing 0.6 s behind each hip socket while boosting
            var ribbonMat = Transparent(Mats.Unlit(Color.white), new Color(0.55f, 0.68f, 0.85f, 1f), true);
            var ribbon = RibbonTexture(64);
            ribbonMat.mainTexture = ribbon;
            if (ribbonMat.HasProperty("_BaseMap")) ribbonMat.SetTexture("_BaseMap", ribbon);
            plumeL = MakePlume(socketL, ribbonMat);
            plumeR = MakePlume(socketR, ribbonMat);

            // touchdown dust
            var puffMat = Transparent(Mats.Unlit(Color.white), Color.white);
            var puff = SoftPuffTexture(64);
            puffMat.mainTexture = puff;
            if (puffMat.HasProperty("_BaseMap")) puffMat.SetTexture("_BaseMap", puff);
            var dgo = new GameObject("LandingDust");
            dgo.transform.SetParent(transform, false);
            dust = dgo.AddComponent<ParticleSystem>();
            var main = dust.main;
            main.playOnAwake = false; main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.6f);
            main.startColor = new Color(0.72f, 0.64f, 0.52f, 0.85f);
            main.gravityModifier = 0.2f;
            main.maxParticles = 128;
            var em = dust.emission; em.enabled = false;
            var sh = dust.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 0.5f;
            var col = dust.colorOverLifetime; col.enabled = true;
            var g2 = new Gradient();
            g2.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.5f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = g2;
            var sol = dust.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.8f)));
            var pr = dust.GetComponent<ParticleSystemRenderer>();
            // URP Unlit ignores particle vertex colour: tint the dust through its own material
            var dustMat = Transparent(Mats.Unlit(Color.white), new Color(0.70f, 0.62f, 0.50f, 0.75f));
            dustMat.mainTexture = puff;
            if (dustMat.HasProperty("_BaseMap")) dustMat.SetTexture("_BaseMap", puff);
            pr.sharedMaterial = dustMat;
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // gas exhaust puffs at the nozzles while boosting (the plume is the ribbon, these are the sputter)
            var ggo = new GameObject("GasPuff");
            ggo.transform.SetParent(transform, false);
            gasPuff = ggo.AddComponent<ParticleSystem>();
            var gm = gasPuff.main;
            gm.playOnAwake = false; gm.loop = false;
            gm.simulationSpace = ParticleSystemSimulationSpace.World;
            gm.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
            gm.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
            gm.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
            gm.startColor = new Color(0.95f, 0.97f, 1f, 0.5f);
            gm.maxParticles = 256;
            var gem = gasPuff.emission; gem.enabled = false;
            var gsh = gasPuff.shape; gsh.enabled = true; gsh.shapeType = ParticleSystemShapeType.Sphere; gsh.radius = 0.15f;
            var gcol = gasPuff.colorOverLifetime; gcol.enabled = true; gcol.color = g2;
            var gsol = gasPuff.sizeOverLifetime; gsol.enabled = true;
            gsol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.5f)));
            var gpr = gasPuff.GetComponent<ParticleSystemRenderer>();
            gpr.sharedMaterial = puffMat;
            gpr.renderMode = ParticleSystemRenderMode.Billboard;
            gpr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static TrailRenderer MakePlume(Transform socket, Material mat)
        {
            var go = new GameObject("GasPlume");
            go.transform.SetParent(socket, false);
            go.transform.localPosition = new Vector3(0, -0.05f, -0.08f);
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = 0.6f;
            tr.minVertexDistance = 0.08f;
            tr.widthMultiplier = 1f;
            tr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.38f), new Keyframe(0.35f, 0.26f), new Keyframe(1f, 0.0f));
            tr.numCapVertices = 4;
            tr.numCornerVertices = 4;
            tr.alignment = LineAlignment.View;
            tr.textureMode = LineTextureMode.Stretch;
            tr.sharedMaterial = mat;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.autodestruct = false;
            tr.emitting = false;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.95f, 0.98f, 1f), 0f), new GradientColorKey(new Color(0.55f, 0.7f, 0.95f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.45f, 0.4f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = grad;
            return tr;
        }

        bool IsBodyPart(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
            {
                var nm = p.name;
                if (nm.StartsWith("Cable_") || nm.StartsWith("HookHead_") || nm == "GasPlume" || nm == "LandingDust" || nm == "GasPuff") return false;
                if (p == transform) return true;
            }
            return false;
        }

        // ---------- scripting ----------
        /// <summary>Put the player somewhere at rest (scripts start from a known spot). Position = feet.</summary>
        public void Teleport(Vector3 feet, Vector3 facing)
        {
            if (Hook == HookState.Attached) Detach();
            mantleT = 0f; crouchT = 0f; rb.isKinematic = false;
            var position = feet + Vector3.up * PivotAboveFeet;
            rb.position = position;
            transform.position = position;
            rb.linearVelocity = Vector3.zero;
            var f = new Vector3(facing.x, 0, facing.z);
            if (f.sqrMagnitude > 1e-4f) { var q = Quaternion.LookRotation(f.normalized, Vector3.up); rb.rotation = q; transform.rotation = q; }
            Gas = gasMax;
            MaxSpeedSeen = 0f; AirTime = 0f;
            if (plumeL != null) { plumeL.Clear(); plumeR.Clear(); }
        }

        public void Play(FlightScript s) { script = s; script.Play(); input = default; }
        public void StopScript() { if (script != null) script.Stop(); script = null; }
        public FlightScript StartRecording() { recorder = new FlightScript { name = "recorded" }; recorder.Record(); return recorder; }
        public FlightScript StopRecording() { var r = recorder; if (r != null) r.StopRecording(); recorder = null; return r; }

        // ---------- input ----------
        void Update()
        {
            if (cam == null) { cam = Ctx.Get<Camera>("camera"); if (cam == null) cam = Camera.main; }
            if (Scripted) return;
            liveInput.moveX = UnityEngine.Input.GetAxisRaw("Horizontal");
            liveInput.moveY = UnityEngine.Input.GetAxisRaw("Vertical");
            liveInput.hook = UnityEngine.Input.GetMouseButton(1);
            liveInput.boost = UnityEngine.Input.GetKey(KeyCode.Space);
            liveInput.reel = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
            liveInput.hasAim = false; liveInput.hasLook = false;
        }

        void LateUpdate()
        {
            ApplyPoseLayer(Time.deltaTime);
            UpdateCables();
            UpdateGhosts();
        }

        void OnCollisionStay(Collision c)
        {
            if (c.contactCount == 0) return;
            var n = c.GetContact(0).normal;
            if (n.y < 0.5f) { wallContact = true; wallNormal = n; }
        }

        // ---------- simulation ----------
        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (script != null)
            {
                if (!script.Step(dt, ref input)) { script = null; input = default; }
            }
            else input = liveInput;
            if (recorder != null) recorder.RecordStep(dt, input);
            if (crouchT > 0f) crouchT -= dt;
            if (hookAge < 10f) hookAge += dt;

            if (mantleT > 0f)
            {
                // scripted ledge mantle: an arc from the anchor to the roof, then a landing
                mantleT -= dt;
                float f = 1f - Mathf.Clamp01(mantleT / mantleDur);
                Vector3 mp = Vector3.Lerp(mantleFrom, mantleTo, f) + Vector3.up * (1.6f * 4f * f * (1f - f));
                rb.MovePosition(mp);
                rb.MoveRotation(Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(mantleFacing, Vector3.up), 1f - Mathf.Exp(-12f * dt)));
                State = FlightState.Reel;
                if (mantleT <= 0f)
                {
                    rb.isKinematic = false;
                    rb.position = mantleTo;
                    rb.linearVelocity = mantleFacing * 2.5f;
                    Grounded = true; GroundLayer = mantleGroundLayer;
                    OnLanded(mantleTo, 8f);
                    Speed = 2.5f;
                }
                return;
            }

            Vector3 pos = rb.position;
            Vector3 eye = pos + Vector3.up * (PivotAboveFeet + capsule.center.y + 0.5f);
            // aim (hooks) and look (boost/body): script world points, else the camera's forward
            if (input.hasAim) AimDir = (input.aimPoint - eye).normalized;
            else if (cam != null) AimDir = cam.transform.forward;
            LookDir = input.hasLook ? (input.lookPoint - eye).normalized : AimDir;
            Vector3 aimFlat = new Vector3(LookDir.x, 0, LookDir.z);
            if (aimFlat.sqrMagnitude < 1e-4f) aimFlat = transform.forward;
            aimFlat.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, aimFlat);

            Vector3 v = rb.linearVelocity;

            // ground probe from the feet
            bool wasGrounded = Grounded;
            Grounded = false; GroundLayer = -1;
            float r = capsule.radius * 0.9f;
            Vector3 probeOrigin = pos + Vector3.up * (PivotAboveFeet + r + 0.12f);
            if (v.y < 3f && Physics.SphereCast(probeOrigin, r, Vector3.down, out hit, 0.32f, OdmLayers.GroundMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.normal.y > 0.6f) { Grounded = true; GroundLayer = hit.collider.gameObject.layer; GroundHeight = hit.point.y; }
            }

            // hooks: fire on press (a miss keeps searching briefly), drop on release
            bool hookPressed = input.hook && !prevHook;
            if (hookPressed) hookRetry = 0.25f;
            if (input.hook && Hook == HookState.None && hookRetry > 0f)
            {
                hookRetry -= dt;
                if (TryHook(eye, AimDir, right, v)) { hookRetry = 0f; v = rb.linearVelocity; }
            }
            if (!input.hook && Hook == HookState.Attached) Detach();
            prevHook = input.hook;

            Boosting = false; Reeling = false;
            float rollTarget = 0f;
            if (Hook == HookState.Attached)
            {
                Grounded = Grounded && v.y < 0.5f && !input.boost && !input.reel;
                Vector3 toA = Anchor - pos;
                float d = toA.magnitude;
                Vector3 dir = d > 1e-3f ? toA / d : Vector3.up;
                // pressed against a wall: the cables pull you along it (wall run), not into it
                if (wallContact && Vector3.Dot(dir, wallNormal) < -0.2f)
                {
                    Vector3 along = dir - wallNormal * Vector3.Dot(dir, wallNormal);
                    if (along.sqrMagnitude > 1e-4f) dir = along.normalized;
                }
                float radial = Vector3.Dot(v, dir); // >0 moving toward the anchor

                // spring-damped tether: only resists stretching beyond the rope length
                if (d > RopeLength)
                {
                    float stretch = d - RopeLength;
                    float f = Mathf.Min(stretch * ropeSpringK - Mathf.Min(radial, 0f) * ropeSpringDamp, ropeMaxForce);
                    v += dir * (f * dt);
                }
                // the winch never fully stops: constant pull keeps the arc tight and fast
                v += dir * (winchPull * dt);

                if (input.reel)
                {
                    Reeling = true;
                    RopeLength = Mathf.Max(1.5f, Mathf.Max(RopeLength - reelSpeed * dt, d - 3f));
                    v += dir * (reelAccel * dt);
                    // a controlled winch: bleed the sideways swing, cap the closing speed
                    Vector3 tangential = v - dir * radial;
                    if (radial > 1f) v -= tangential * (1f - Mathf.Exp(-reelTangentialDamp * dt));
                    float closing = Vector3.Dot(v, dir);
                    if (closing > reelMaxSpeed) v -= dir * (closing - reelMaxSpeed);
                    if (d < reelDetach || (d < 6f && pos.y > Anchor.y + 0.3f))
                    {
                        // reached the anchor: mantle onto the ledge behind it when there is one,
                        // otherwise pop up with what is left of the momentum
                        Detach();
                        Vector3 inward = new Vector3(-hookNormal.x, 0, -hookNormal.z);
                        if (inward.sqrMagnitude < 1e-3f) inward = new Vector3(toA.x, 0, toA.z);
                        inward = inward.sqrMagnitude > 1e-4f ? inward.normalized : transform.forward;
                        Vector3 probe = Anchor + inward * 1.7f + Vector3.up * 6f;
                        if (Physics.Raycast(probe, Vector3.down, out hit, 9f, OdmLayers.GroundMask, QueryTriggerInteraction.Ignore)
                            && hit.normal.y > 0.7f && hit.point.y > Anchor.y - 1.5f && hit.point.y < Anchor.y + 4f)
                        {
                            mantleFrom = pos; mantleTo = hit.point + Vector3.up * (PivotAboveFeet + 0.02f); mantleFacing = inward;
                            mantleGroundLayer = hit.collider.gameObject.layer;
                            mantleT = mantleDur;
                            rb.linearVelocity = Vector3.zero;
                            rb.isKinematic = true;
                            Speed = 0f; Reeling = false;
                            State = FlightState.Reel;
                            return;
                        }
                        float rise = Mathf.Clamp(Anchor.y + 3.5f - pos.y, 1f, 12f);
                        float up = Mathf.Clamp(Mathf.Sqrt(2f * gravity * rise), 6f, popUp);
                        v = v * 0.15f + Vector3.up * up + inward * popForward;
                    }
                }
                if (input.boost && Gas > 0f)
                {
                    Boosting = true;
                    Vector3 bd = (dir * (1f - boostForwardMix) + LookDir * boostForwardMix).normalized;
                    v += bd * (boostAccel * dt);
                    Gas = Mathf.Max(0f, Gas - gasDrain * dt);
                }
                // rope shortens by itself as you swing past the anchor (no slack winch)
                if (d < RopeLength) RopeLength = Mathf.Max(1.5f, Mathf.Lerp(RopeLength, d, 1f - Mathf.Exp(-6f * dt)));
                // light air steering while hooked
                v += (right * input.moveX + aimFlat * input.moveY * 0.5f) * (airSteer * dt);
                // bank into the arc: roll toward the side the cable pulls from
                if (v.sqrMagnitude > 16f)
                {
                    Vector3 vd = v.normalized;
                    Vector3 side = Vector3.Cross(Vector3.up, vd).normalized;
                    rollTarget = Mathf.Clamp(Vector3.Dot(dir, side), -1f, 1f) * swingRoll;
                }
            }
            else if (Grounded)
            {
                Vector3 wish = (right * input.moveX + aimFlat * input.moveY);
                if (wish.sqrMagnitude > 1f) wish.Normalize();
                Vector3 hv = new Vector3(v.x, 0, v.z);
                Vector3 target = wish * runSpeed;
                float accel = wish.sqrMagnitude > 0.01f ? runAccel : groundFriction;
                hv = Vector3.MoveTowards(hv, target, accel * dt);
                v.x = hv.x; v.z = hv.z;
                if (v.y < 0f) v.y = -1f; // stick to the ground
                if (input.boost && !prevBoost && Gas >= hopGas)
                {
                    v.y = hopSpeed; Gas -= hopGas; Grounded = false;
                }
                Gas = Mathf.Min(gasMax, Gas + gasRefill * dt);
            }
            else
            {
                // free air: momentum, light steering, small drag
                v += (right * input.moveX + aimFlat * input.moveY) * (airSteer * dt);
                if (input.boost && Gas > 0f)
                {
                    Boosting = true;
                    v += LookDir * (boostAccel * 0.6f * dt);
                    Gas = Mathf.Max(0f, Gas - gasDrain * dt);
                }
                // free arc: a lazy roll into the turn (from the lateral steer)
                rollTarget = -input.moveX * swingRoll * 0.5f;
            }
            prevBoost = input.boost;

            if (!Grounded) v.y -= gravity * dt;
            // drag and cap
            float sp = v.magnitude;
            if (!Grounded && sp > 1f) v -= v * Mathf.Min(0.5f, airDrag * sp * dt);
            sp = v.magnitude;
            if (sp > maxSpeed) v *= Mathf.Lerp(1f, maxSpeed / sp, 1f - Mathf.Exp(-6f * dt));
            if (sp > maxSpeed * 1.15f) v *= maxSpeed * 1.15f / sp;
            rb.linearVelocity = v;

            wallContact = false;
            Speed = v.magnitude;
            if (Speed > MaxSpeedSeen) MaxSpeedSeen = Speed;
            if (Grounded) { if (!wasGrounded && AirTime > 0.15f) OnLanded(pos, preLandSpeed); AirTime = 0f; }
            else AirTime += dt;
            preLandSpeed = Speed;

            // flight state (drives the pose layer)
            if (crouchT > 0f && Grounded) State = FlightState.Land;
            else if (Grounded) State = FlightState.Ground;
            else if (Hook == HookState.Attached && Reeling) State = FlightState.Reel;
            else if (Boosting) State = FlightState.Boost;
            else if (Hook == HookState.Attached && hookAge < hookReachTime) State = FlightState.Hook;
            else State = FlightState.Swing;

            // root orientation: upright facing the look on the ground; in the air the root's
            // forward follows the velocity (the Fly pose lays the body along it), banked into the arc
            Quaternion targetRot;
            float rotRate = 10f;
            if (Grounded || Speed < 3f) { targetRot = Quaternion.LookRotation(aimFlat, Vector3.up); rotRate = 14f; rollTarget = 0f; }
            else
            {
                Vector3 vd = v / Mathf.Max(Speed, 1e-3f);
                Vector3 vflat = new Vector3(vd.x, 0, vd.z);
                if (vflat.sqrMagnitude < 1e-4f) vflat = aimFlat;
                vflat.Normalize();
                Quaternion upright = Quaternion.LookRotation(vflat, Vector3.up);
                Quaternion along = Quaternion.LookRotation(vd, Vector3.up);
                float k = Mathf.Clamp01((Speed - 6f) / (streamlineSpeed - 6f));
                targetRot = Quaternion.Slerp(upright, along, k);
                rotRate = 8f;
            }
            rollLive = Mathf.Lerp(rollLive, rollTarget, 1f - Mathf.Exp(-5f * dt));
            targetRot = targetRot * Quaternion.Euler(0, 0, rollLive);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, 1f - Mathf.Exp(-rotRate * dt)));

            // gas fx
            bool plume = Boosting && Gas > 0f;
            if (plumeL != null && plumeL.emitting != plume) { plumeL.emitting = plume; plumeR.emitting = plume; }
            if (plume && gasPuff != null)
            {
                gasPuff.transform.position = (socketL.position + socketR.position) * 0.5f - v.normalized * 0.4f;
                gasPuff.Emit(1);
            }

            if (verbose)
            {
                logAccum += dt;
                if (logAccum >= 0.1f - 1e-4f)
                {
                    logAccum = 0f;
                    Debug.Log("[ODM] t=" + Time.fixedTime.ToString("0.00") + " pos=" + pos.ToString("0.0") + " v=" + Speed.ToString("0.0")
                              + " hook=" + Hook + " state=" + State + " gas=" + Gas.ToString("0") + " grounded=" + Grounded
                              + " key=" + (script != null ? script.CurrentLabel : "live"));
                }
            }
        }

        bool TryHook(Vector3 eye, Vector3 dir, Vector3 right, Vector3 v)
        {
            if (!Physics.Raycast(eye, dir, out hit, hookRange, OdmLayers.HookMask, QueryTriggerInteraction.Ignore)) return false;
            Hook = HookState.Attached;
            hookAge = 0f;
            Anchor = hit.point;
            hookNormal = hit.normal;
            // two hooks land a little apart so the cables read as a pair
            Vector3 spread = Vector3.Cross(hit.normal, Vector3.up);
            if (spread.sqrMagnitude < 1e-3f) spread = right;
            spread.Normalize();
            AnchorL = Anchor - spread * 0.45f;
            AnchorR = Anchor + spread * 0.45f;
            RopeLength = Vector3.Distance(rb.position, Anchor);
            // snappy: an immediate tug toward the anchor; from the ground the cables launch you
            Vector3 toA = (Anchor - rb.position).normalized;
            v += toA * hookSnap;
            if (Grounded) { v.y = Mathf.Max(v.y, hookLaunch); Grounded = false; }
            rb.linearVelocity = v;
            SetCablesVisible(true);
            hookHeadL.position = AnchorL; hookHeadR.position = AnchorR;
            return true;
        }

        void Detach()
        {
            Hook = HookState.None;
            SetCablesVisible(false);
        }

        void UpdateCables()
        {
            if (Hook != HookState.Attached) return;
            cableL.SetPosition(0, socketL.position); cableL.SetPosition(1, AnchorL);
            cableR.SetPosition(0, socketR.position); cableR.SetPosition(1, AnchorR);
        }

        void OnLanded(Vector3 pos, float impactSpeed)
        {
            LandTime = Time.fixedTime; LandSpot = pos;
            crouchT = landTime;
            if (dust != null)
            {
                dust.transform.position = pos - Vector3.up * (PivotAboveFeet - 0.05f);
                dust.Emit(Mathf.Clamp(28 + (int)(impactSpeed * 2f), 28, 64));
            }
        }

        // ---------- body: IPoser base + per-state override layer ----------
        void ApplyPoseLayer(float dt)
        {
            if (poser == null) return;
            // base pose
            Pose basePose;
            switch (State)
            {
                case FlightState.Land: basePose = Pose.Land; break;
                case FlightState.Ground: basePose = Speed > 1.5f ? Pose.Run : Pose.Idle; break;
                default: basePose = Pose.Fly; break;
            }
            if (poser.Current != basePose) poser.SetPose(basePose);
            if (rig == null) return;

            // override layer: the maneuver silhouette on top of the base pose
            for (int i = 0; i < HumanoidProxy.BoneCount; i++) ovrMask[i] = false;
            switch (State)
            {
                case FlightState.Hook:
                {
                    // both arms flung toward the anchors, legs trailing, chest lifted
                    Vector3 toA = (Anchor - transform.position).normalized;
                    Vector3 local = transform.InverseTransformDirection(toA);
                    float fwd = Mathf.Clamp(Mathf.Atan2(local.z, -local.y) * Mathf.Rad2Deg, 60f, 175f);
                    Arm(BoneId.LeftUpperArm, fwd, 30f); Ovr(BoneId.LeftLowerArm, Quaternion.Euler(-6f, 0, 0));
                    Arm(BoneId.RightUpperArm, fwd, 30f); Ovr(BoneId.RightLowerArm, Quaternion.Euler(-6f, 0, 0));
                    Ovr(BoneId.Chest, Quaternion.Euler(-18f, 0, 0));
                    Ovr(BoneId.Head, Quaternion.Euler(-40f, 0, 0));
                    Leg(BoneId.LeftUpperLeg, -30f, 6f); Ovr(BoneId.LeftLowerLeg, Quaternion.Euler(45f, 0, 0));
                    Leg(BoneId.RightUpperLeg, -22f, 6f); Ovr(BoneId.RightLowerLeg, Quaternion.Euler(30f, 0, 0));
                    break;
                }
                case FlightState.Boost:
                {
                    // streamlined: arms swept back along the body, legs together and straight, head up
                    Arm(BoneId.LeftUpperArm, -75f, 6f); Ovr(BoneId.LeftLowerArm, Quaternion.Euler(-4f, 0, 0));
                    Arm(BoneId.RightUpperArm, -75f, 6f); Ovr(BoneId.RightLowerArm, Quaternion.Euler(-4f, 0, 0));
                    Leg(BoneId.LeftUpperLeg, -6f, 0f); Ovr(BoneId.LeftLowerLeg, Quaternion.Euler(4f, 0, 0)); Ovr(BoneId.LeftFoot, Quaternion.Euler(40f, 0, 0));
                    Leg(BoneId.RightUpperLeg, -6f, 0f); Ovr(BoneId.RightLowerLeg, Quaternion.Euler(4f, 0, 0)); Ovr(BoneId.RightFoot, Quaternion.Euler(40f, 0, 0));
                    Ovr(BoneId.Chest, Quaternion.Euler(-10f, 0, 0));
                    Ovr(BoneId.Head, Quaternion.Euler(-45f, 0, 0));
                    break;
                }
                case FlightState.Swing:
                {
                    // arms out like wings, one leg trailing behind, the other tucked; body rolled with the root
                    float s = Mathf.Sign(rollLive == 0f ? 1f : rollLive);
                    Arm(BoneId.LeftUpperArm, -10f, 80f); Ovr(BoneId.LeftLowerArm, Quaternion.Euler(-20f, 0, 0));
                    Arm(BoneId.RightUpperArm, -10f, 80f); Ovr(BoneId.RightLowerArm, Quaternion.Euler(-20f, 0, 0));
                    if (s > 0) { Leg(BoneId.RightUpperLeg, -38f, 12f); Ovr(BoneId.RightLowerLeg, Quaternion.Euler(20f, 0, 0)); Leg(BoneId.LeftUpperLeg, 30f, 8f); Ovr(BoneId.LeftLowerLeg, Quaternion.Euler(80f, 0, 0)); }
                    else { Leg(BoneId.LeftUpperLeg, -38f, 12f); Ovr(BoneId.LeftLowerLeg, Quaternion.Euler(20f, 0, 0)); Leg(BoneId.RightUpperLeg, 30f, 8f); Ovr(BoneId.RightLowerLeg, Quaternion.Euler(80f, 0, 0)); }
                    Ovr(BoneId.Chest, Quaternion.Euler(-6f, 0, 8f * s));
                    Ovr(BoneId.Head, Quaternion.Euler(-30f, 0, 0));
                    break;
                }
                case FlightState.Reel:
                {
                    // knees tucked to the chest, arms pulling the cables in (elbows bent hard), head down
                    Leg(BoneId.LeftUpperLeg, 95f, 12f); Ovr(BoneId.LeftLowerLeg, Quaternion.Euler(115f, 0, 0));
                    Leg(BoneId.RightUpperLeg, 95f, 12f); Ovr(BoneId.RightLowerLeg, Quaternion.Euler(115f, 0, 0));
                    Arm(BoneId.LeftUpperArm, 110f, 18f); Ovr(BoneId.LeftLowerArm, Quaternion.Euler(-105f, 0, 0));
                    Arm(BoneId.RightUpperArm, 110f, 18f); Ovr(BoneId.RightLowerArm, Quaternion.Euler(-105f, 0, 0));
                    Ovr(BoneId.Chest, Quaternion.Euler(18f, 0, 0));
                    Ovr(BoneId.Head, Quaternion.Euler(10f, 0, 0));
                    break;
                }
            }
            float k = 1f - Mathf.Exp(-12f * dt);
            for (int i = 0; i < HumanoidProxy.BoneCount; i++)
            {
                var b = rig.Bone((BoneId)i);
                if (b == null) continue;
                Quaternion want = ovrMask[i] ? ovrTarget[i] : b.localRotation;
                if (!ovrLiveValid[i]) { ovrLive[i] = want; ovrLiveValid[i] = true; }
                ovrLive[i] = Quaternion.Slerp(ovrLive[i], want, k);
                b.localRotation = ovrLive[i];
            }
        }

        static float SideSign(BoneId id)
        {
            switch (id)
            {
                case BoneId.LeftUpperArm: case BoneId.LeftLowerArm: case BoneId.LeftHand:
                case BoneId.LeftUpperLeg: case BoneId.LeftLowerLeg: case BoneId.LeftFoot: return -1f;
                default: return 1f;
            }
        }

        void Ovr(BoneId id, Quaternion q) { ovrTarget[(int)id] = q; ovrMask[(int)id] = true; }
        // same conventions as ProceduralPoser: +fwd swings the limb forward, +out away from the midline
        void Arm(BoneId id, float fwd, float outward) => Ovr(id, Quaternion.Euler(-fwd, 0, outward * SideSign(id)));
        void Leg(BoneId id, float fwd, float outward) => Ovr(id, Quaternion.Euler(-fwd, 0, outward * SideSign(id)));

        void UpdateGhosts()
        {
            Vector3 v = rb.linearVelocity;
            float sp = v.magnitude;
            bool show = !Grounded && !Mantling && sp > 12f;
            if (show)
            {
                float kk = Mathf.Clamp01((sp - 12f) / 30f);
                Vector3 back = -v / sp;
                int n = bodyTfs.Length;
                float spacing = 0.02f * sp + 0.25f;
                for (int g = 0; g < ghostCount; g++)
                {
                    var c = ghostMats[g].GetColor("_BaseColor");
                    c.a = ghostBaseAlpha * (1f - g / (float)ghostCount) * kk;
                    ghostMats[g].SetColor("_BaseColor", c);
                    Vector3 off = back * (spacing * (g + 1));
                    for (int b = 0; b < n; b++)
                    {
                        var src = bodyTfs[b];
                        var gt = ghostTf[g * n + b];
                        gt.SetPositionAndRotation(src.position + off, src.rotation);
                        gt.localScale = src.lossyScale;
                        ghosts[g * n + b].enabled = bodyRenderers[b].enabled;
                    }
                }
            }
            else
            {
                for (int i = 0; i < ghosts.Length; i++) if (ghosts[i].enabled) ghosts[i].enabled = false;
            }
        }

        void OnDestroy()
        {
            if (ghostTf != null && ghostTf.Length > 0 && ghostTf[0] != null) Destroy(ghostTf[0].parent.gameObject);
            if (ReferenceEquals(Ctx.Get<OdmController>("player"), this)) Ctx.Remove("player");
        }
    }
}
