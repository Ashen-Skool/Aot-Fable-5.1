using UnityEngine;
using Shared;

namespace ODM
{
    public enum HookState { None, Attached }

    /// <summary>
    /// Rigidbody ODM controller. Ground: WASD relative to the aim yaw. Air: momentum, light
    /// steering. RMB press fires both hooks at the aim point (raycast against HookTarget +
    /// Titan, 60 m); while held the cables act as a spring-damped tether with a constant
    /// winch pull, Space boosts along the cable (drains gas), Shift reels the cable in and,
    /// when the anchor sits just under a roof edge, mantles you onto the roof. Release =
    /// free swing with momentum. Landing at speed is an impact + skid, not a slide.
    ///
    /// The collider stays upright (yaw only); the lean into flight is applied to a "Visual"
    /// child so the capsule never wedges under ledges. Two gas jets at the hip sockets emit
    /// while boosting. Registered in Ctx as "player". Input comes from Input (live) or a
    /// FlightScript (deterministic replay).
    /// </summary>
    public class OdmController : MonoBehaviour
    {
        // ---- tuning (feel) ----
        public float runSpeed = 8f, runAccel = 45f, groundFriction = 30f;
        public float landSkidFriction = 55f, landSkidTime = 0.5f, landImpactKeep = 0.55f;
        public float gravity = 16f;
        public float airSteer = 7f;
        public float airDrag = 0.003f;   // quadratic: a = airDrag * v^2
        public float maxSpeed = 55f;
        public float hookRange = 60f;
        public float ropeSpringK = 60f, ropeSpringDamp = 10f, ropeMaxForce = 90f;
        public float winchPull = 9f;       // constant pull toward the anchor while attached (m/s^2)
        public float hookSnap = 6f;        // velocity kick toward the anchor at hook time (m/s)
        public float hookLaunch = 7f;      // vertical launch when hooking from the ground (m/s)
        public float boostAccel = 38f;     // m/s^2 along the cable while Space is held
        public float boostForwardMix = 0.5f;   // boost = mostly where you look, partly along the cable
        public float reelSpeed = 16f;      // m/s of rope shortening
        public float reelAccel = 28f;
        public float reelDetach = 2.2f;    // m from anchor at which a reel pops you off
        public float reelTangentialDamp = 2.5f, reelMaxSpeed = 30f;
        public float mantleMaxRise = 8f, mantleClearance = 1.4f, mantleInward = 6f, mantleTimeout = 1.5f;
        public float popUp = 6f;           // detach kick when there is no ledge to mantle
        public float gasMax = 100f, gasDrain = 22f, gasRefill = 18f, hopGas = 6f, hopSpeed = 9f;
        public float bodyTilt = 55f, bodyRoll = 22f;   // visual lean (deg)

        // ---- state (read via Ctx "player") ----
        public float Speed { get; private set; }
        public float Gas { get; private set; }
        public float GasMax => gasMax;
        public HookState Hook { get; private set; }
        public bool Boosting { get; private set; }
        public bool Reeling { get; private set; }
        public bool Mantling { get; private set; }
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
        public float LandSpeed { get; private set; }
        public FlightScript Script => script;
        public bool Scripted => script != null && script.Playing;
        public OdmInput Input => input;

        public Rigidbody Body => rb;
        public Transform Visual => visual;
        public Transform socketL, socketR;

        Rigidbody rb;
        CapsuleCollider capsule;
        Transform visual;
        LineRenderer cableL, cableR;
        Transform hookHeadL, hookHeadR;
        ParticleSystem jetL, jetR;
        TrailRenderer trail;
        FlightScript script;
        FlightScript recorder;
        OdmInput input, liveInput;
        bool prevHook, prevBoost;
        RaycastHit hit, roofHit;
        Camera cam;
        public bool verbose;
        float logAccum, hookRetry, skid;
        bool wallContact; Vector3 wallNormal;
        // mantle: the roof just above the current anchor, if any
        bool mantleOK; float roofY; Vector3 inward;
        float mantleRoofY, mantleTimer; Vector3 mantleDir;

        void OnCollisionStay(Collision c)
        {
            if (c.contactCount == 0) return;
            var n = c.GetContact(0).normal;
            if (n.y < 0.5f) { wallContact = true; wallNormal = n; }
        }

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
            Gas = gasMax;
            visual = BuildVisual();
            socketL = FindSocket("Socket_HookL", new Vector3(-0.28f, -0.15f, 0.12f));
            socketR = FindSocket("Socket_HookR", new Vector3(0.28f, -0.15f, 0.12f));
            cableL = MakeCable("Cable_L");
            cableR = MakeCable("Cable_R");
            hookHeadL = MakeHookHead("HookHead_L");
            hookHeadR = MakeHookHead("HookHead_R");
            jetL = MakeJet("GasJet_L", socketL, 11u);
            jetR = MakeJet("GasJet_R", socketR, 23u);
            trail = MakeTrail("SpeedTrail");
            SetCablesVisible(false);
            Ctx.Set("player", this);
        }

        /// <summary>The leaning part. A rigged Mikasa provides "Visual"/"Rig"; the capsule placeholder gets one built here.</summary>
        Transform BuildVisual()
        {
            var existing = FindDeep(transform, "Visual") ?? FindDeep(transform, "Rig");
            if (existing != null) return existing;
            var vis = new GameObject("Visual").transform;
            vis.SetParent(transform, false);
            if (TryGetComponent<MeshRenderer>(out var mr) && TryGetComponent<MeshFilter>(out var mf))
            {
                var body = new GameObject("Body");
                body.transform.SetParent(vis, false);
                body.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                body.AddComponent<MeshRenderer>().sharedMaterial = mr.sharedMaterial;
                mr.enabled = false;
            }
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i);
                if (c != vis) c.SetParent(vis, true);
            }
            // the visual leans every frame; nothing under it may be part of the physics compound
            var cols = vis.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++) Destroy(cols[i]);
            return vis;
        }

        Transform FindSocket(string name, Vector3 fallbackLocal)
        {
            var t = FindDeep(transform, name);
            if (t != null) return t;
            var go = new GameObject(name);
            go.transform.SetParent(visual, false);
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
            lr.startWidth = 0.06f; lr.endWidth = 0.035f;
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
            go.transform.localScale = Vector3.one * 0.22f;
            go.GetComponent<Renderer>().sharedMaterial = Mats.Lit(new Color(0.75f, 0.72f, 0.65f), 0.6f, 0.9f);
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        /// <summary>White gas puffs out of a hip socket, backwards. Rate is driven per frame; seeded so captures repeat.</summary>
        ParticleSystem MakeJet(string name, Transform socket, uint seed)
        {
            var go = new GameObject(name);
            go.transform.SetParent(socket, false);
            go.transform.localRotation = Quaternion.Euler(12f, 180f, 0f);   // back and slightly down
            var ps = go.AddComponent<ParticleSystem>();
            ps.useAutoRandomSeed = false;
            ps.randomSeed = seed;
            var main = ps.main;
            main.loop = true; main.playOnAwake = true;
            main.startLifetime = 0.42f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(10f, 16f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
            main.startColor = Color.white;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            main.gravityModifier = -0.05f;
            var em = ps.emission; em.enabled = true; em.rateOverTime = 0f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 9f; sh.radius = 0.04f;
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.6f, 1.6f), new Keyframe(1f, 0f)));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = SoftParticleMat(new Color(0.93f, 0.94f, 0.96f, 0.85f));
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.Distance;
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.85f, 0.87f, 0.9f), 1f) },
                         new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.55f, 0.35f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            ps.Play();
            return ps;
        }

        static Texture2D softDot;
        /// <summary>Radial-falloff white dot, generated once; the gas puffs and the speed trail use it.</summary>
        static Texture2D SoftDot()
        {
            if (softDot != null) return softDot;
            const int n = 64;
            softDot = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);
                px[y * n + x] = new Color(1f, 1f, 1f, a);
            }
            softDot.SetPixels(px);
            softDot.Apply(true, false);
            return softDot;
        }

        /// <summary>
        /// Alpha-blended particle material from the shared Particles base. Blending in URP's
        /// Particles/Unlit is render state, not a keyword, so this survives build stripping.
        /// </summary>
        static Material SoftParticleMat(Color tint)
        {
            var baseMat = Resources.Load<Material>("Materials/Particles");
            var m = baseMat != null ? new Material(baseMat) : Mats.Unlit(tint);
            m.SetTexture("_BaseMap", SoftDot());
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        /// <summary>A short white ribbon behind the hips; only emits at flight speed so stills read the arc.</summary>
        TrailRenderer MakeTrail(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, -0.2f, -0.3f);
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = 0.45f;
            tr.minVertexDistance = 0.25f;
            tr.startWidth = 0.55f; tr.endWidth = 0.05f;
            tr.numCapVertices = 3;
            tr.alignment = LineAlignment.View;
            tr.textureMode = LineTextureMode.Stretch;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
            var mat = SoftParticleMat(new Color(1f, 1f, 1f, 0.6f));
            mat.SetTexture("_BaseMap", null);
            tr.sharedMaterial = mat;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0.25f, 0.4f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = g;
            tr.emitting = false;
            return tr;
        }

        void SetCablesVisible(bool on)
        {
            cableL.enabled = on; cableR.enabled = on;
            hookHeadL.gameObject.SetActive(on); hookHeadR.gameObject.SetActive(on);
        }

        void SetJets(bool on)
        {
            var el = jetL.emission; el.rateOverTime = on ? 220f : 0f;
            var er = jetR.emission; er.rateOverTime = on ? 220f : 0f;
        }

        /// <summary>Put the player somewhere at rest (scripts start from a known spot).</summary>
        public void Teleport(Vector3 position, Vector3 facing)
        {
            if (Hook == HookState.Attached) Detach();
            Mantling = false; skid = 0f;
            rb.position = position;
            transform.position = position;
            rb.linearVelocity = Vector3.zero;
            var f = new Vector3(facing.x, 0, facing.z);
            if (f.sqrMagnitude > 1e-4f) { var q = Quaternion.LookRotation(f.normalized, Vector3.up); rb.rotation = q; transform.rotation = q; }
            if (visual != null) visual.localRotation = Quaternion.identity;
            Gas = gasMax;
            MaxSpeedSeen = 0f; AirTime = 0f; LandTime = -1f;
            jetL.Clear(); jetR.Clear();
            trail.Clear(); trail.emitting = false;
        }

        // ---------- scripting ----------
        public void Play(FlightScript s)
        {
            script = s;
            script.Play();
            input = default;
        }

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
            liveInput.hasAim = false;
            liveInput.hasLook = false;
        }

        void LateUpdate()
        {
            UpdateCables();
            UpdateLean(Time.deltaTime);
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

            Vector3 pos = rb.position;
            Vector3 eye = pos + Vector3.up * 0.6f;
            // aim (hooks) and look (boost/body): script world points, else the camera's forward
            if (input.hasAim) AimDir = (input.aimPoint - eye).normalized;
            else if (cam != null) AimDir = cam.transform.forward;
            LookDir = input.hasLook ? (input.lookPoint - eye).normalized : AimDir;
            Vector3 aimFlat = new Vector3(LookDir.x, 0, LookDir.z);
            if (aimFlat.sqrMagnitude < 1e-4f) aimFlat = transform.forward;
            aimFlat.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, aimFlat);

            Vector3 v = rb.linearVelocity;

            // ground probe
            bool wasGrounded = Grounded;
            Grounded = false; GroundLayer = -1;
            if (v.y < 3f && Physics.SphereCast(pos, capsule.radius * 0.9f, Vector3.down, out hit, 0.75f, OdmLayers.GroundMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.normal.y > 0.6f) { Grounded = true; GroundLayer = hit.collider.gameObject.layer; GroundHeight = hit.point.y; }
            }
            if (Grounded && !wasGrounded)
            {
                // touchdown: the impact eats most of the horizontal speed, the rest is a short skid
                LandTime = Time.fixedTime; LandSpot = pos; LandSpeed = v.magnitude;
                Vector3 hv = new Vector3(v.x, 0, v.z);
                if (hv.magnitude > 10f) { hv *= landImpactKeep; v.x = hv.x; v.z = hv.z; }
                skid = landSkidTime;
                Mantling = false;
            }

            // hooks: fire on press, drop on release
            bool hookPressed = input.hook && !prevHook;
            if (hookPressed) hookRetry = 0.25f;               // a miss keeps searching briefly while RMB is held
            if (input.hook && Hook == HookState.None && hookRetry > 0f && !Mantling)
            {
                hookRetry -= dt;
                if (TryHook(eye, AimDir, right, v)) { hookRetry = 0f; v = rb.linearVelocity; }
            }
            if (!input.hook && Hook == HookState.Attached) Detach();
            prevHook = input.hook;

            Boosting = false; Reeling = false;
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
                    // never wind in faster than the body can follow (blocked by a wall = bounded tension)
                    RopeLength = Mathf.Max(1.5f, Mathf.Max(RopeLength - reelSpeed * dt, d - 3f));
                    v += dir * (reelAccel * dt);
                    // a controlled winch: bleed the sideways swing, cap the closing speed
                    Vector3 tangential = v - dir * radial;
                    if (radial > 1f) v -= tangential * (1f - Mathf.Exp(-reelTangentialDamp * dt));
                    float closing = Vector3.Dot(v, dir);
                    if (closing > reelMaxSpeed) v -= dir * (closing - reelMaxSpeed);
                    if (d < reelDetach || (d < 6f && pos.y > Anchor.y + 0.3f))
                    {
                        // reached the anchor: onto the roof if there is one just above, else a small pop
                        Detach();
                        v *= 0.15f;
                        if (mantleOK)
                        {
                            Mantling = true; mantleTimer = 0f; mantleRoofY = roofY; mantleDir = inward;
                            float into = Vector3.Dot(v, mantleDir);
                            if (into > 0f) v -= mantleDir * into;
                            float bottom = pos.y - capsule.height * 0.5f;
                            v.y = Mathf.Max(v.y, Mathf.Sqrt(2f * gravity * Mathf.Max(0.5f, mantleRoofY + mantleClearance - bottom)));
                        }
                        else v += Vector3.up * popUp;
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
            }
            else if (Grounded)
            {
                Vector3 wish = (right * input.moveX + aimFlat * input.moveY);
                if (wish.sqrMagnitude > 1f) wish.Normalize();
                Vector3 hv = new Vector3(v.x, 0, v.z);
                Vector3 target = wish * runSpeed;
                float accel = wish.sqrMagnitude > 0.01f ? runAccel : (skid > 0f ? landSkidFriction : groundFriction);
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
                if (input.boost && Gas > 0f && !Mantling)
                {
                    Boosting = true;
                    v += LookDir * (boostAccel * 0.6f * dt);
                    Gas = Mathf.Max(0f, Gas - gasDrain * dt);
                }
            }
            prevBoost = input.boost;
            if (skid > 0f) skid -= dt;

            // mantle: ride straight up the wall face until the feet clear the roof, then step in
            if (Mantling)
            {
                mantleTimer += dt;
                float bottom = pos.y - capsule.height * 0.5f;
                if (bottom < mantleRoofY + 0.15f && mantleTimer < mantleTimeout)
                {
                    float into = Vector3.Dot(v, mantleDir);
                    if (into > 0f) v -= mantleDir * into;
                    float need = Mathf.Sqrt(2f * gravity * Mathf.Max(0.3f, mantleRoofY + mantleClearance - bottom));
                    if (v.y < need) v.y = need;
                }
                else
                {
                    Mantling = false;
                    v += mantleDir * mantleInward;
                    v.y = Mathf.Min(v.y, 4f);
                }
            }

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
            if (Grounded) AirTime = 0f; else AirTime += dt;
            SetJets(Boosting);
            trail.emitting = !Grounded && Speed > 14f;

            // body yaw only: face the aim on the ground, face the velocity in the air (lean is visual)
            Vector3 face;
            if (Grounded || Speed < 4f) face = aimFlat;
            else { face = new Vector3(v.x, 0, v.z); if (face.sqrMagnitude < 1e-4f) face = aimFlat; face.Normalize(); }
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(face, Vector3.up), 1f - Mathf.Exp(-10f * dt)));

            if (verbose)
            {
                logAccum += dt;
                if (logAccum >= 0.1f - 1e-4f)
                {
                    logAccum = 0f;
                    Debug.Log("[ODM] t=" + Time.fixedTime.ToString("0.00") + " pos=" + pos.ToString("0.0") + " v=" + Speed.ToString("0.0")
                              + " hook=" + Hook + " boost=" + Boosting + " reel=" + Reeling + " mantle=" + Mantling + " gas=" + Gas.ToString("0") + " grounded=" + Grounded
                              + " key=" + (script != null ? script.CurrentLabel : "live"));
                }
            }
        }

        void UpdateLean(float dt)
        {
            if (visual == null) return;
            Quaternion target;
            if (Grounded || Speed < 4f) target = Quaternion.identity;
            else
            {
                Vector3 vd = rb.linearVelocity.normalized;
                float lean = Mathf.Clamp01((Speed - 6f) / 30f) * bodyTilt;
                float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(vd.y, -1f, 1f)) * Mathf.Rad2Deg, -lean, lean);
                float roll = -input.moveX * bodyRoll * Mathf.Clamp01((Speed - 6f) / 20f);
                target = Quaternion.Euler(pitch, 0f, roll);
            }
            visual.localRotation = Quaternion.Slerp(visual.localRotation, target, 1f - Mathf.Exp(-8f * dt));
        }

        bool TryHook(Vector3 eye, Vector3 dir, Vector3 right, Vector3 v)
        {
            if (!Physics.Raycast(eye, dir, out hit, hookRange, OdmLayers.HookMask, QueryTriggerInteraction.Ignore)) return false;
            Hook = HookState.Attached;
            Anchor = hit.point;
            // two hooks land a little apart so the cables read as a pair
            Vector3 spread = Vector3.Cross(hit.normal, Vector3.up);
            if (spread.sqrMagnitude < 1e-3f) spread = right;
            spread.Normalize();
            AnchorL = Anchor - spread * 0.45f;
            AnchorR = Anchor + spread * 0.45f;
            RopeLength = Vector3.Distance(rb.position, Anchor);
            ProbeRoof();
            // snappy: an immediate tug toward the anchor; from the ground the cables launch you
            Vector3 toA = (Anchor - rb.position).normalized;
            v += toA * hookSnap;
            if (Grounded) { v.y = Mathf.Max(v.y, hookLaunch); Grounded = false; }
            rb.linearVelocity = v;
            SetCablesVisible(true);
            hookHeadL.position = AnchorL; hookHeadR.position = AnchorR;
            return true;
        }

        /// <summary>Is there a roof just above this wall anchor? (Reeling in then mantles onto it.)</summary>
        void ProbeRoof()
        {
            mantleOK = false; roofY = float.NaN;
            if (hit.collider.gameObject.layer == OdmLayers.Titan) return;
            if (Mathf.Abs(hit.normal.y) > 0.5f) return;   // hooked a roof/floor, nothing to climb
            inward = -new Vector3(hit.normal.x, 0, hit.normal.z).normalized;
            float top = hit.collider.bounds.max.y;
            float above = top - hit.point.y;
            if (above < -0.5f || above > mantleMaxRise) return;
            Vector3 o = hit.point + inward * 1.2f + Vector3.up * (above + 3f);
            if (!Physics.Raycast(o, Vector3.down, out roofHit, above + 6f, OdmLayers.GroundMask, QueryTriggerInteraction.Ignore)) return;
            if (roofHit.normal.y < 0.7f) return;
            float rise = roofHit.point.y - hit.point.y;
            if (rise < -0.5f || rise > mantleMaxRise) return;
            roofY = roofHit.point.y;
            mantleOK = true;
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

        void OnDestroy()
        {
            if (ReferenceEquals(Ctx.Get<OdmController>("player"), this)) Ctx.Remove("player");
        }
    }
}
