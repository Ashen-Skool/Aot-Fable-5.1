using UnityEngine;
using Shared;

namespace ODM
{
    public enum HookState { None, Attached }

    /// <summary>
    /// Rigidbody ODM controller. Ground: WASD relative to the aim yaw. Air: momentum, light
    /// steering. RMB press fires both hooks at the aim point (raycast against HookTarget +
    /// Titan, 60 m); while held the cables act as a spring-damped tether with a constant
    /// winch pull, Space boosts along the cable (drains gas), Shift reels the cable in and
    /// pops you onto the anchor when you reach it. Release = free swing with momentum.
    /// Registered in Ctx as "player". Input comes from Input (live) or a FlightScript.
    /// </summary>
    public class OdmController : MonoBehaviour, Proxies.ODMHit
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
        public float boostAccel = 38f;     // m/s^2 along the cable while Space is held
        public float boostForwardMix = 0.5f;   // boost = mostly where you look, partly along the cable
        public float reelSpeed = 16f;      // m/s of rope shortening
        public float reelAccel = 28f;
        public float reelDetach = 2.2f;    // m from anchor at which a reel pops you off
        public float popUp = 16f, popForward = 6f;  // the ledge mantle after a reel (popUp = max vertical)
        public float reelTangentialDamp = 2.5f, reelMaxSpeed = 30f;
        public float gasMax = 100f, gasDrain = 22f, gasRefill = 18f, hopGas = 6f, hopSpeed = 9f;
        public float streamlineSpeed = 28f; // speed at which the body is fully along its velocity
        public float crouchTime = 0.3f;
        public float trailSeconds = 0.14f;  // smear length = speed * trailSeconds
        public int ghostCount = 3;

        // ---- state (read via Ctx "player") ----
        public float Speed { get; private set; }
        public float Gas { get; private set; }
        public float GasMax => gasMax;
        public HookState Hook { get; private set; }
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
        public Transform socketL, socketR;

        Rigidbody rb;
        CapsuleCollider capsule;
        LineRenderer cableL, cableR;
        Transform hookHeadL, hookHeadR;
        FlightScript script;
        FlightScript recorder;
        OdmInput input, liveInput;
        bool prevHook, prevBoost;
        RaycastHit hit;
        Camera cam;
        public bool verbose;
        float logAccum, hookRetry;
        bool wallContact; Vector3 wallNormal;
        Vector3 hookNormal = Vector3.forward;
        float mantleT, mantleDur = 0.32f; Vector3 mantleFrom, mantleTo, mantleFacing;
        float crouchT, preLandSpeed; int mantleGroundLayer;
        LineRenderer trail; readonly Vector3[] trailPts = new Vector3[8];
        Renderer[] ghosts; Transform[] ghostTf; Material[] ghostMats; float ghostBaseAlpha = 0.5f;
        Renderer[] bodyRenderers; Transform[] bodyTfs;
        ParticleSystem dust, gasPuff, anchorDust, anchorSparks; Material puffMat;
        Vector3 baseScale;
        float hookTime = -10f; readonly Vector3[] cablePts = new Vector3[14];
        Transform[] bladeRoots; TrailRenderer[] bladeTrails; float bladeSearch;
        AudioSource wind, hiss; AudioLowPassFilter windLp, hissLp;
        public bool AimHasHit { get; private set; }
        public Vector3 AimHitPoint { get; private set; }
        public float AimHitDist { get; private set; }
        public float HitFlash => hitFlash;
        public float StaggerTimer => staggerTimer;

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
            socketL = FindSocket("Socket_HookL", new Vector3(-0.28f, -0.15f, 0.12f));
            socketR = FindSocket("Socket_HookR", new Vector3(0.28f, -0.15f, 0.12f));
            cableL = MakeCable("Cable_L");
            cableR = MakeCable("Cable_R");
            hookHeadL = MakeHookHead("HookHead_L");
            hookHeadR = MakeHookHead("HookHead_R");
            SetCablesVisible(false);
            baseScale = transform.localScale;
            BuildSpeedFx();
            BuildAnchorFx();
            wind = NoiseLoop.Source(gameObject, NoiseLoop.Brown(), 0f, 50f, out windLp);
            hiss = NoiseLoop.Source(gameObject, NoiseLoop.White(), 0f, 50f, out hissLp); if (hissLp != null) hissLp.cutoffFrequency = 6000f;
            Ctx.Set("player", this);
        }

        // ---------- speed / landing visuals ----------
        static Material Transparent(Material m, Color c)
        {
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        void BuildSpeedFx()
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
            root.SetParent(null, false);
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

            // speed smear: a tapered translucent streak behind the body along -velocity
            var tgo = new GameObject("SpeedTrail");
            tgo.transform.SetParent(null, false);
            trail = tgo.AddComponent<LineRenderer>();
            trail.positionCount = trailPts.Length;
            trail.useWorldSpace = true;
            trail.numCapVertices = 4;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = Transparent(Mats.Unlit(Color.white), new Color(0.85f, 0.9f, 1f, 0.55f));
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.9f, 0.93f, 1f), 0f), new GradientColorKey(new Color(0.6f, 0.7f, 0.9f), 1f) },
                new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0.25f, 0.4f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = grad;
            var wc = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.5f, 0.55f), new Keyframe(1f, 0f));
            trail.widthCurve = wc;
            trail.widthMultiplier = 0.9f;
            trail.enabled = false;

            // touchdown dust
            var dgo = new GameObject("LandingDust");
            dgo.transform.SetParent(transform, false);
            dust = dgo.AddComponent<ParticleSystem>();
            var main = dust.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.0f);
            main.startColor = new Color(0.62f, 0.56f, 0.48f, 0.7f);
            main.gravityModifier = 0.25f;
            main.maxParticles = 128;
            var em = dust.emission; em.enabled = false;
            var sh = dust.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 0.4f;
            var col = dust.colorOverLifetime; col.enabled = true;
            var g2 = new Gradient();
            g2.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g2;
            var sol = dust.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.6f)));
            var pr = dust.GetComponent<ParticleSystemRenderer>();
            var dm = Transparent(Mats.Unlit(Color.white), Color.white);
            var puff = SoftPuffTexture(64);
            dm.mainTexture = puff;
            if (dm.HasProperty("_BaseMap")) dm.SetTexture("_BaseMap", puff);
            pr.sharedMaterial = dm;
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            puffMat = dm;

            // gas exhaust: white puffs streaming back from the hips while boosting
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
            var gsh = gasPuff.shape; gsh.enabled = true; gsh.shapeType = ParticleSystemShapeType.Sphere; gsh.radius = 0.2f;
            var gcol = gasPuff.colorOverLifetime; gcol.enabled = true; gcol.color = g2;
            var gsol = gasPuff.sizeOverLifetime; gsol.enabled = true;
            gsol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.5f)));
            var gpr = gasPuff.GetComponent<ParticleSystemRenderer>();
            gpr.sharedMaterial = puffMat;
            gpr.renderMode = ParticleSystemRenderMode.Billboard;
            gpr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Radial soft-edged white puff with alpha falloff, for dust billboards.</summary>
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

        void BuildAnchorFx()
        {
            anchorDust = new GameObject("AnchorDust").AddComponent<ParticleSystem>();
            { var m = anchorDust.main; m.playOnAwake = false; m.loop = false; m.simulationSpace = ParticleSystemSimulationSpace.World; m.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f); m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f); m.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.6f); m.startColor = new Color(0.7f, 0.65f, 0.58f, 0.6f); m.maxParticles = 128;
              var em = anchorDust.emission; em.enabled = false; var sh = anchorDust.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.15f;
              var col = anchorDust.colorOverLifetime; col.enabled = true; var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0f, 1f) }); col.color = g;
              var r = anchorDust.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = puffMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            anchorSparks = new GameObject("AnchorSparks").AddComponent<ParticleSystem>();
            { var m = anchorSparks.main; m.playOnAwake = false; m.loop = false; m.simulationSpace = ParticleSystemSimulationSpace.World; m.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.4f); m.startSpeed = new ParticleSystem.MinMaxCurve(4f, 11f); m.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f); m.gravityModifier = 0.6f; m.maxParticles = 128;
              var em = anchorSparks.emission; em.enabled = false; var sh = anchorSparks.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.1f;
              var r = anchorSparks.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = Mats.Unlit(new Color(2.5f, 1.9f, 1.1f)); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f; r.lengthScale = 1.5f; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
        }

        /// <summary>Trail renderers on the blade tips, found once the rig has dressed her. Emit only during a slash.</summary>
        void EnsureBladeTrails()
        {
            if (bladeTrails != null || Time.time < bladeSearch) return;
            bladeSearch = Time.time + 0.5f;
            var l = FindDeep(transform, "Blade_L"); var r = FindDeep(transform, "Blade_R");
            if (l == null || r == null) return;
            bladeRoots = new[] { l, r }; bladeTrails = new TrailRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                float ext = 0.6f;
                foreach (var rd in bladeRoots[i].GetComponentsInChildren<Renderer>())
                {
                    var b = rd.bounds; var c = b.center; var e = b.extents;
                    for (int k = 0; k < 8; k++)
                    {
                        var corner = c + new Vector3((k & 1) == 0 ? -e.x : e.x, (k & 2) == 0 ? -e.y : e.y, (k & 4) == 0 ? -e.z : e.z);
                        ext = Mathf.Max(ext, Vector3.Dot(corner - bladeRoots[i].position, bladeRoots[i].forward));
                    }
                }
                var go = new GameObject("BladeTrail"); go.transform.SetParent(bladeRoots[i], false); go.transform.localPosition = new Vector3(0f, 0f, ext * 0.95f);
                var t = go.AddComponent<TrailRenderer>();
                t.time = 0.16f; t.startWidth = 0.28f; t.endWidth = 0.02f; t.minVertexDistance = 0.02f; t.numCapVertices = 3; t.alignment = LineAlignment.View;
                t.sharedMaterial = Transparent(Mats.Unlit(Color.white), new Color(1.7f, 1.9f, 2.4f, 0.85f));
                var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(new Color(1f, 1f, 1f), 0f), new GradientColorKey(new Color(0.6f, 0.8f, 1f), 1f) }, new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
                t.colorGradient = g; t.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; t.receiveShadows = false; t.emitting = false;
                bladeTrails[i] = t;
            }
        }

        bool IsBodyPart(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
            {
                var nm = p.name;
                if (nm.StartsWith("Cable_") || nm.StartsWith("HookHead_") || nm == "SpeedTrail" || nm == "LandingDust" || nm == "BladeTrail") return false;
                if (p == transform) return true;
            }
            return false;
        }

        void UpdateSpeedFx()
        {
            Vector3 v = rb.linearVelocity;
            float sp = v.magnitude;
            bool show = !Grounded && !Mantling && sp > 12f;
            float k = Mathf.Clamp01((sp - 12f) / 30f);
            Vector3 back = sp > 1e-3f ? -v / sp : -transform.forward;
            Vector3 pos = transform.position;
            if (show)
            {
                float len = sp * trailSeconds;
                for (int i = 0; i < trailPts.Length; i++)
                {
                    float f = i / (float)(trailPts.Length - 1);
                    trailPts[i] = pos + back * (0.4f + len * f);
                }
                trail.SetPositions(trailPts);
                trail.widthMultiplier = 0.5f + 0.7f * k;
                trail.enabled = true;
                int n = bodyTfs.Length;
                float spacing = 0.02f * sp + 0.25f;
                for (int g = 0; g < ghostCount; g++)
                {
                    var c = ghostMats[g].GetColor("_BaseColor");
                    c.a = ghostBaseAlpha * (1f - g / (float)ghostCount) * k;
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
                if (trail.enabled) trail.enabled = false;
                for (int i = 0; i < ghosts.Length; i++) if (ghosts[i].enabled) ghosts[i].enabled = false;
            }
            // crouch: squash on touchdown, recover
            float cr = crouchT > 0f ? Mathf.Sin(Mathf.Clamp01(crouchT / crouchTime) * Mathf.PI) : 0f;
            transform.localScale = new Vector3(baseScale.x * (1f + 0.18f * cr), baseScale.y * (1f - 0.32f * cr), baseScale.z * (1f + 0.18f * cr));
        }

        void OnLanded(Vector3 pos, float impactSpeed)
        {
            landPoseTimer = 0.35f; Shared.Sfx.Play("land", pos, 0.9f, Mathf.Clamp01(impactSpeed / 20f));
            LandTime = Time.fixedTime; LandSpot = pos;
            crouchT = crouchTime;
            if (dust != null)
            {
                dust.transform.position = pos + Vector3.down * 0.95f;
                dust.Emit(Mathf.Clamp(12 + (int)(impactSpeed * 1.5f), 12, 48));
            }
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
            lr.positionCount = cablePts.Length;
            lr.useWorldSpace = true;
            lr.startWidth = 0.075f; lr.endWidth = 0.05f;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = Mats.Unlit(new Color(0.92f, 0.94f, 1.0f)); // bright steel so the cable reads against stone
            return lr;
        }

        Transform MakeHookHead(string name)
        {
            // a grapple: a short shaft with two barbed prongs, oriented into the surface at hook time
            var go = new GameObject(name);
            var steel = Mats.Lit(new Color(0.55f, 0.55f, 0.58f), 0.65f, 0.9f);
            void Part(string n, Vector3 pos, Vector3 euler, Vector3 scale, PrimitiveType t)
            {
                var pgo = GameObject.CreatePrimitive(t); pgo.name = n; Destroy(pgo.GetComponent<Collider>());
                pgo.transform.SetParent(go.transform, false); pgo.transform.localPosition = pos; pgo.transform.localRotation = Quaternion.Euler(euler); pgo.transform.localScale = scale;
                var r = pgo.GetComponent<Renderer>(); r.sharedMaterial = steel; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Part("shaft", new Vector3(0f, 0f, -0.22f), new Vector3(90f, 0f, 0f), new Vector3(0.07f, 0.22f, 0.07f), PrimitiveType.Cylinder);
            Part("prongL", new Vector3(-0.09f, 0f, 0.02f), new Vector3(0f, -28f, 0f), new Vector3(0.05f, 0.05f, 0.34f), PrimitiveType.Cube);
            Part("prongR", new Vector3(0.09f, 0f, 0.02f), new Vector3(0f, 28f, 0f), new Vector3(0.05f, 0.05f, 0.34f), PrimitiveType.Cube);
            Part("prongU", new Vector3(0f, 0.09f, 0.02f), new Vector3(-28f, 0f, 0f), new Vector3(0.05f, 0.05f, 0.34f), PrimitiveType.Cube);
            return go.transform;
        }

        void SetCablesVisible(bool on)
        {
            cableL.enabled = on; cableR.enabled = on;
            hookHeadL.gameObject.SetActive(on); hookHeadR.gameObject.SetActive(on);
        }

        /// <summary>Put the player somewhere at rest (scripts start from a known spot).</summary>
        public void Teleport(Vector3 position, Vector3 facing)
        {
            if (Hook == HookState.Attached) Detach();
            mantleT = 0f; crouchT = 0f; rb.isKinematic = false;
            rb.position = position;
            transform.position = position;
            rb.linearVelocity = Vector3.zero;
            var f = new Vector3(facing.x, 0, facing.z);
            if (f.sqrMagnitude > 1e-4f) { var q = Quaternion.LookRotation(f.normalized, Vector3.up); rb.rotation = q; transform.rotation = q; }
            Gas = gasMax;
            MaxSpeedSeen = 0f; AirTime = 0f;
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
            GameInput.UpdateCursor();
            var mv = GameInput.Move;
            liveInput.moveX = mv.x;
            liveInput.moveY = mv.y;
            // Space toggles the hooks: press = fire at the crosshair (a virtual anchor if nothing is there) and get pulled
            // in; press again = release and fall. Shift = gas burst. The pull is automatic while hooked.
            if (UnityEngine.Input.GetKeyDown(KeyCode.Space))
            {
                if (Hook == HookState.Attached) hookLatched = false;
                else { hookLatched = true; wantVirtual = true; prevHook = false; hookRetry = 0.3f; } // fresh press every time, never a dead latch
            }
            if (hookLatched && Hook == HookState.None && hookRetry <= 0f) hookLatched = false; // self-heal: a latch with no hook is meaningless
            if (Hook == HookState.Attached && Vector3.Distance(rb.position, Anchor) < 3.5f) hookLatched = false; // arrived
            liveInput.hook = hookLatched;
            liveInput.reel = hookLatched && Hook == HookState.Attached;
            liveInput.boost = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
            liveInput.hasAim = false;
            if (UnityEngine.Input.GetMouseButtonDown(0) && slashTimer <= 0.15f)
            {
                slashAirborne = !Grounded; slashHitTimer = 0.25f; Shared.Sfx.Play("slash", rb.position, Random.Range(1.5f, 1.9f), 0.7f);
                var model = Ctx.Get<Characters.CharacterModel>("mikasaModel");
                slashTimer = model != null ? Mathf.Min(1.6f, model.Attack(slashAirborne)) : (Grounded ? 1.1f : 0.8f);
                slashPoseSet = model != null; // the model already plays the clip; UpdatePose must not restart it
            }
            if (Health <= 0f) { deathTimer -= Time.deltaTime; if (deathTimer <= 0f) Respawn(); }
            if (rb.position.y < -25f) Respawn(); // fell off the world
            // what the crosshair is over (HUD marker); the wind and gas beds
            if (cam != null)
            {
                AimHasHit = Physics.Raycast(rb.position + Vector3.up * 0.6f, cam.transform.forward, out var ah, hookRange, OdmLayers.HookMask, QueryTriggerInteraction.Ignore);
                if (AimHasHit) { AimHitPoint = ah.point; AimHitDist = ah.distance; }
            }
            if (wind != null && hiss != null)
            {
                float k = Mathf.Clamp01((Speed - 6f) / 42f);
                wind.volume = Mathf.Lerp(wind.volume, (Grounded ? 0.15f : 1f) * Mathf.Pow(k, 1.4f) * 0.55f, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
                if (windLp != null) windLp.cutoffFrequency = Mathf.Lerp(300f, 2600f, k);
                wind.pitch = 0.8f + 0.5f * k;
                hiss.volume = Mathf.Lerp(hiss.volume, Boosting ? 0.32f : 0f, 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
            }
            if (UnityEngine.Input.GetKeyDown(KeyCode.LeftBracket)) Characters.CharacterModel.FistRollDeg -= 15f;
            if (UnityEngine.Input.GetKeyDown(KeyCode.RightBracket)) Characters.CharacterModel.FistRollDeg += 15f;
        }
        float slashTimer; bool slashAirborne; bool slashPoseSet; bool hookLatched, wantVirtual; float slashHitTimer;
        public float Health { get; private set; } = 100f; public float HealthMax = 100f; float deathTimer; float hitFlash;

        void LateUpdate()
        {
            UpdateCables(); UpdateSpeedFx(); SlashHitCheck(Time.deltaTime); UpdatePose();
            EnsureBladeTrails();
            if (bladeTrails != null) { bool on = slashTimer > 0.05f; for (int i = 0; i < 2; i++) if (bladeTrails[i] != null && bladeTrails[i].emitting != on) { bladeTrails[i].emitting = on; if (on) bladeTrails[i].Clear(); } }
        }

        // ---------- combat: blade hits on titan zones, taking hits ----------
        static readonly Collider[] overlap = new Collider[32];
        void SlashHitCheck(float dt)
        {
            if (slashHitTimer <= 0f) return;
            slashHitTimer -= dt; if (slashHitTimer > 0f) return;
            int n = Physics.OverlapSphereNonAlloc(rb.position + Vector3.up * 0.9f + AimDir * 1.6f, 3.2f, overlap, ~0, QueryTriggerInteraction.Collide);
            Proxies.TitanBrain bodyHit = null;
            for (int i = 0; i < n; i++)
            {
                var c = overlap[i]; if (c == null) continue;
                var brain = c.GetComponentInParent<Proxies.TitanBrain>(); if (brain == null) continue;
                if (c.name.StartsWith("Zone_")) { brain.Hit(c.name, rb.position); Shared.Sfx.Play("titan_hit", rb.position, 0.7f, 1f); return; }
                bodyHit = brain;
            }
            if (bodyHit != null) { bodyHit.Hit("body", rb.position); Shared.Sfx.Play("titan_hit", rb.position, 0.9f, 0.7f); }
        }
        public void Hit(Proxies.TitanBrain from, float damage) => TakeHit(from.transform.position, damage);
        float hitGrace;   // seconds of invulnerability after a hit so one swing never chains into another
        public void TakeHit(Vector3 from, float damage)
        {
            if (Health <= 0f || Time.time < hitGrace) return;
            hitGrace = Time.time + 1.6f;
            Health = Mathf.Max(0f, Health - damage); hitFlash = 0.35f; Shared.Sfx.Play("player_hit", rb.position, 0.8f, 1f);
            Vector3 away = (rb.position - from); away.y = 0f; away = away.sqrMagnitude > 0.01f ? away.normalized : -AimDir;
            rb.linearVelocity = away * 13f + Vector3.up * 7f; Grounded = false;
            if (Hook == HookState.Attached) { hookLatched = false; Detach(); }
            landPoseTimer = 0f; slashTimer = 0f; staggerTimer = 0.5f;
            Shared.Music.Duck(0.5f);
            var camT = GetComponent<OdmCameraTarget>(); if (camT != null) camT.Hit();
            if (Health <= 0f) deathTimer = 2.5f;
        }
        float staggerTimer;
        void Respawn()
        {
            Health = HealthMax;
            var sp = Ctx.Has("town.spawn") ? Ctx.Get<Vector3>("town.spawn") : Vector3.zero;
            rb.position = sp + Vector3.up * 1.2f; rb.linearVelocity = Vector3.zero; Gas = gasMax; hookLatched = false; Detach();
        }

        // ---------- HUD (ODM/Hud.cs) ----------
        public static bool TitleDone { get; set; }
        void OnGUI()
        {
            if (Scripted || Application.isBatchMode) return;
            Hud.Draw(this, cam);
        }

        float landPoseTimer;
        /// <summary>Drive whichever IPoser is registered for Mikasa (proxy or the real rig) from flight state.</summary>
        void UpdatePose()
        {
            var poser = Ctx.Get<Shared.Rigs.IPoser>("mikasaPoser");
            if (poser == null) return;
            landPoseTimer -= Time.deltaTime; slashTimer -= Time.deltaTime; staggerTimer -= Time.deltaTime; hitFlash -= Time.deltaTime;
            Shared.Rigs.Pose want;
            if (staggerTimer > 0f) want = Shared.Rigs.Pose.Stagger;
            else if (slashTimer > 0f) want = Shared.Rigs.Pose.Slash; // Swipe = aerial blade spin on Mikasa
            else if (landPoseTimer > 0f) want = Shared.Rigs.Pose.Land;
            else if (!Grounded) want = Hook != HookState.None ? Shared.Rigs.Pose.Swing : Shared.Rigs.Pose.Fly;
            else if (Speed > 7f) want = Shared.Rigs.Pose.Sprint;
            else if (Speed > 0.6f) want = Shared.Rigs.Pose.Run;
            else want = Shared.Rigs.Pose.Idle;
            if (want == Shared.Rigs.Pose.Slash && slashPoseSet) return; // random attack clip already running on the model
            if (poser.Current != want) poser.SetPose(want);
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
            if (mantleT > 0f)
            {
                // scripted ledge mantle: an arc from the anchor to the roof, then a landing
                mantleT -= dt;
                float f = 1f - Mathf.Clamp01(mantleT / mantleDur);
                Vector3 mp = Vector3.Lerp(mantleFrom, mantleTo, f) + Vector3.up * (1.6f * 4f * f * (1f - f));
                rb.MovePosition(mp);
                rb.MoveRotation(Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(mantleFacing, Vector3.up), 1f - Mathf.Exp(-12f * dt)));
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
            // Probe from the collider centre, not the transform origin: rigs with the root at the
            // feet would start the sphere inside the ground and never register a hit.
            var probeOrigin = capsule.bounds.center;
            float probeDist = Mathf.Max(0.75f, capsule.bounds.extents.y - capsule.radius * 0.9f + 0.35f);
            if (v.y < 3f && Physics.SphereCast(probeOrigin, capsule.radius * 0.9f, Vector3.down, out hit, probeDist, OdmLayers.GroundMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.normal.y > 0.6f) { Grounded = true; GroundLayer = hit.collider.gameObject.layer; GroundHeight = hit.point.y; }
            }

            // hooks: fire on press, drop on release
            bool hookPressed = input.hook && !prevHook;
            if (hookPressed) hookRetry = 0.25f;               // a miss keeps searching briefly while RMB is held
            if (input.hook && Hook == HookState.None && hookRetry > 0f)
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
                            mantleFrom = pos; mantleTo = hit.point + Vector3.up * 1.02f; mantleFacing = inward;
                            mantleGroundLayer = hit.collider.gameObject.layer;
                            mantleT = mantleDur;
                            rb.linearVelocity = Vector3.zero;
                            rb.isKinematic = true;
                            Speed = 0f; Reeling = false;
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
                    if (gasPuff != null) { gasPuff.transform.position = (socketL.position + socketR.position) * 0.5f; gasPuff.Emit(14); }
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

            // body orientation: upright facing the look on the ground; in the air the body lies
            // along its velocity (head first, streamlined), blended in with speed
            Quaternion targetRot;
            float rotRate = 10f;
            if (Grounded || Speed < 3f) { targetRot = Quaternion.LookRotation(aimFlat, Vector3.up); rotRate = 14f; }
            else
            {
                Vector3 vd = v / Mathf.Max(Speed, 1e-3f);
                Vector3 vflat = new Vector3(vd.x, 0, vd.z);
                if (vflat.sqrMagnitude < 1e-4f) vflat = aimFlat;
                vflat.Normalize();
                Quaternion upright = Quaternion.LookRotation(vflat, Vector3.up);
                // capsule Y axis -> velocity, belly toward the ground
                Vector3 side = Vector3.Cross(Vector3.up, vd);
                if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(vflat, vd);
                Vector3 belly = Vector3.Cross(vd, side).normalized;   // roughly down-facing
                Quaternion along = Quaternion.LookRotation(-belly, vd);
                float k = Mathf.Clamp01((Speed - 6f) / (streamlineSpeed - 6f));
                targetRot = Quaternion.Slerp(upright, along, k);
                rotRate = 8f;
            }
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, 1f - Mathf.Exp(-rotRate * dt)));

            if (Boosting && gasPuff != null)
            {
                // two jets from the hip tanks, thrown back along the travel direction
                Vector3 back = -(v.sqrMagnitude > 1f ? v.normalized : LookDir);
                for (int j = 0; j < 2; j++)
                {
                    var ep = new ParticleSystem.EmitParams { position = (j == 0 ? socketL.position : socketR.position) + back * 0.35f, velocity = back * 9f + Random.insideUnitSphere * 1.5f, startSize = Random.Range(0.22f, 0.4f) };
                    gasPuff.Emit(ep, 2);
                }
            }

            if (verbose)
            {
                logAccum += dt;
                if (logAccum >= 0.1f - 1e-4f)
                {
                    logAccum = 0f;
                    Debug.Log("[ODM] t=" + Time.fixedTime.ToString("0.00") + " pos=" + pos.ToString("0.0") + " v=" + Speed.ToString("0.0")
                              + " hook=" + Hook + " boost=" + Boosting + " reel=" + Reeling + " gas=" + Gas.ToString("0") + " grounded=" + Grounded
                              + " key=" + (script != null ? script.CurrentLabel : "live"));
                }
            }
        }

        bool TryHook(Vector3 eye, Vector3 dir, Vector3 right, Vector3 v)
        {
            bool real = Physics.Raycast(eye, dir, out hit, hookRange, OdmLayers.HookMask, QueryTriggerInteraction.Ignore);
            if (!real && !wantVirtual) return false;
            Hook = HookState.Attached;
            Shared.Sfx.Play("hook_fire", rb.position, 1.4f, 0.8f); Shared.Sfx.Play("hook_attach", real ? hit.point : rb.position, 0.9f, 0.9f, 90f);
            Anchor = real ? hit.point : eye + dir * Mathf.Min(hookRange, 45f);   // nothing there: a virtual anchor in the sky still pulls you
            hookNormal = real ? hit.normal : -dir;
            wantVirtual = false;
            // two hooks land a little apart so the cables read as a pair
            Vector3 spread = Vector3.Cross(hookNormal, Vector3.up);
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
            var into = Quaternion.LookRotation(-hookNormal, Vector3.up);
            hookHeadL.rotation = into; hookHeadR.rotation = into;
            hookTime = Time.time;
            if (real && !Application.isBatchMode)
            {
                anchorDust.Emit(new ParticleSystem.EmitParams { position = Anchor, applyShapeToPosition = true }, 10);
                anchorSparks.Emit(new ParticleSystem.EmitParams { position = Anchor, applyShapeToPosition = true }, 12);
            }
            var rig = Ctx.Get<Component>("cameraRig"); if (rig != null && !Application.isBatchMode) rig.SendMessage("Shake", real ? 0.2f : 0.12f, SendMessageOptions.DontRequireReceiver);
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
            float age = Time.time - hookTime;
            float ext = Mathf.Clamp01(age / 0.07f);                            // the cable shoots out over a few frames
            float d = Vector3.Distance(rb.position, Anchor);
            float slack = Mathf.Max(0f, RopeLength - d);
            float sag = Mathf.Min(2.2f, slack * 0.45f) + 0.12f;                // slack cable hangs; a taut one is nearly straight
            float whip = 0.55f * Mathf.Sin(age * 34f) * Mathf.Exp(-age * 5.5f); // the snap when the grapple bites
            Cable(cableL, socketL.position, AnchorL, ext, sag, whip, 1f);
            Cable(cableR, socketR.position, AnchorR, ext, sag, whip, -1f);
            hookHeadL.position = AnchorL; hookHeadR.position = AnchorR;
        }

        void Cable(LineRenderer lr, Vector3 a, Vector3 b, float ext, float sag, float whip, float side)
        {
            Vector3 end = Vector3.Lerp(a, b, ext);
            Vector3 dir = end - a; float len = dir.magnitude; if (len < 1e-3f) dir = Vector3.forward; else dir /= len;
            Vector3 lateral = Vector3.Cross(dir, Vector3.up); if (lateral.sqrMagnitude < 1e-4f) lateral = Vector3.right;
            lateral.Normalize();
            int n = cablePts.Length;
            for (int i = 0; i < n; i++)
            {
                float f = i / (float)(n - 1);
                float bulge = 4f * f * (1f - f);
                cablePts[i] = Vector3.Lerp(a, end, f) + Vector3.down * (sag * bulge * ext) + lateral * (whip * side * Mathf.Sin(f * Mathf.PI * 2f) * ext);
            }
            lr.SetPositions(cablePts);
        }

        void OnDestroy()
        {
            if (trail != null) Destroy(trail.gameObject);
            if (ghostTf != null && ghostTf.Length > 0 && ghostTf[0] != null) Destroy(ghostTf[0].parent.gameObject);
            if (ReferenceEquals(Ctx.Get<OdmController>("player"), this)) Ctx.Remove("player");
        }
    }
}
