using UnityEngine;
using Shared;
using Shared.Rigs;
using Pose = Shared.Rigs.Pose;

namespace Proxies
{
    /// <summary>
    /// The Titan's brain: spots the player, chases through the streets, swipes and stomps when close, and reacts to blade
    /// hits on its zones (nape = kill, both hamstrings = kneel, anything else = stagger). Moves its host transform.
    /// </summary>
    public class TitanBrain : MonoBehaviour
    {
        public enum State { Idle, Chase, Attack, Stagger, Kneel, Dead }
        public State Current { get; private set; } = State.Idle;
        public float height = 15f, walkSpeed = 6.5f, sprintSpeed = 11f, sightRange = 120f, attackRange = 16f, turnRate = 70f;
        public float swipeDamage = 24f, stompDamage = 32f;
        public float windUp = 1.05f, attackLength = 2.1f, attackCooldown = 2.6f;
        public static bool SoftLock = false;
        public float gateHold = 6f, firstAttackGrace = 8f; float chaseStart = -1f;   // a readable telegraph, then a real opening
        public bool HamL, HamR;
        public float HP = 100f, HPMax = 100f;
        IPoser poser; Transform player; Component playerCtrl; float t, cooldown, kneelTimer, roarTimer;
        public TitanFx Fx { get; private set; }
        Vector3 spawnPos;
        public static TitanBrain Attach(GameObject host, float height)
        {
            var b = host.GetComponent<TitanBrain>() ?? host.AddComponent<TitanBrain>();
            b.height = height; b.walkSpeed = height * 0.45f; b.sprintSpeed = height * 0.65f; b.attackRange = height * 0.62f;   // ~9 m for the 15 m boss: he has to actually reach you
            b.spawnPos = host.transform.position;
            // a kinematic body: without one, every collider on him is a "static" collider that moves each frame and PhysX re-bakes it
            var rb = host.GetComponent<Rigidbody>() ?? host.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            b.Fx = TitanFx.Attach(host, height);
            return b;
        }
        IPoser Poser => poser ??= Ctx.Get<IPoser>("bossPoser");
        Transform Player { get { if (player == null) { var p = Ctx.Get<Component>("player"); if (p != null) { player = p.transform; playerCtrl = p; } } return player; } }

        void Update()
        {
            if (Application.isBatchMode && Bootstrap.Arg("-piece") != null && Bootstrap.Arg("-piece") != "titanai") return; // keep captures deterministic
            float dt = Time.deltaTime; t += dt; cooldown -= dt;
            var pl = Player; if (pl == null || Poser == null) return;
            Vector3 toP = pl.position - transform.position; float distFlat = new Vector2(toP.x, toP.z).magnitude;
            // soft camera lock: once he is close and alive the chase camera keeps him in frame (the rig blends, the mouse still steers)
            bool wantLock = SoftLock && Current != State.Idle && Current != State.Dead && distFlat < 80f;   // off: it dragged the view toward him while running (user)
            if (wantLock != locked)
            {
                locked = wantLock; if (locked) Ctx.Set("cameraLockTarget", LockPoint()); else Ctx.Remove("cameraLockTarget");
                if (locked && !hinted) { hinted = true; HudEvents.Add(transform.position + Vector3.up * height * 0.3f, "GET BEHIND HIM  ·  HAMSTRINGS FIRST", new Color(1f, 0.85f, 0.4f), 0.9f, 3.5f); }
            }
            switch (Current)
            {
                case State.Idle:
                    Set(Pose.Idle);
                    if (Ctx.Get<bool>("titleHold")) break;                                   // the title is up: he waits at the gate
                    if (distFlat < sightRange || (Ctx.Has("introUntil") && Time.unscaledTime > Ctx.Get<float>("introUntil") + gateHold)) { Current = State.Chase; roarTimer = 1.2f; chaseStart = Time.time; Roar(); }
                    break;
                case State.Chase:
                    if (Ridden)
                    {
                        // she is on his neck: he runs blind, swerving through the streets, and never swings
                        wanderT -= dt; if (wanderT <= 0f) { wanderT = Random.Range(1.2f, 2.6f); wanderSign = Random.value < 0.5f ? -1f : 1f; }
                        transform.Rotate(0f, wanderSign * 28f * dt, 0f, Space.World);
                        Set(Pose.Sprint);
                        var rf = transform.forward; rf.y = 0f; rf.Normalize();
                        transform.position += Steer(rf, sprintSpeed * 0.8f * dt) * sprintSpeed * 0.8f * dt;
                        Plow(rf, dt);   // running blind with her on his neck: he goes through the houses, not into them
                        break;
                    }
                    Face(toP, dt);
                    if (distFlat < attackRange && cooldown <= 0f && InFront(toP, 0.1f) && Time.time > chaseStart + firstAttackGrace)
                    {
                        Current = State.Attack; t = 0f; attackKind = pl.position.y > height * 0.35f ? Pose.Swipe : (Random.value < 0.5f ? Pose.Stomp : Pose.Swipe); Set(attackKind); hitDone = false;
                        // the telegraph: a warning at the spot he is about to hit, a grunt, a shiver through the camera
                        Vector3 warn = transform.position + transform.forward * height * 0.38f + Vector3.up * (attackKind == Pose.Stomp ? height * 0.1f : height * 0.45f);
                        HudEvents.Add(warn, attackKind == Pose.Stomp ? "STOMP" : "SWIPE", new Color(1f, 0.3f, 0.2f), 1.5f, windUp + 0.2f);   // the warning lives as long as the wind-up
                        if (Harness.Active) Debug.Log("[TitanAttack] " + attackKind + " dist=" + distFlat.ToString("0.0") + " t=" + Time.time.ToString("0.00"));
                        Sfx.Play("titan_step", transform.position + Vector3.up * height * 0.8f, 0.25f, 0.9f, 260f);
                        if (attackKind == Pose.Swipe) Invoke(nameof(SwipeWhoosh), windUp - 0.25f);
                        Fx?.Step(distFlat * 0.5f);
                    }
                    else
                    {
                        bool sprint = distFlat > 70f;   // walks the last stretch so you see him coming
                        Set(sprint ? Pose.Sprint : Pose.Run);
                        float sp = sprint ? sprintSpeed : walkSpeed;
                        var f = transform.forward; f.y = 0f; f.Normalize();
                        // progress watchdog: a block of houses between him and her had him zigzagging in place (each probe frame
                        // picked a side, the turn toward her undid it). No progress for 4 s -> he bulldozes straight through for 3 s.
                        progressT += dt;
                        if (progressT > 4f) { if (distFlat > bestDist - 3f && distFlat > attackRange) { bulldozeT = 3f; Roar(); } bestDist = distFlat; progressT = 0f; }
                        bestDist = Mathf.Min(bestDist, distFlat);
                        if (bulldozeT > 0f)
                        {
                            bulldozeT -= dt; rubbleT -= dt;
                            if (distFlat > attackRange * 0.55f)
                            {
                                var np = transform.position + f * sp * dt;
                                if (Ctx.Has("town.bounds")) { var tb = Ctx.Get<Bounds>("town.bounds"); np.x = Mathf.Clamp(np.x, tb.min.x + 4f, tb.max.x - 4f); np.z = Mathf.Clamp(np.z, tb.min.z + 4f, tb.max.z - 4f); }   // never through the boundary
                                transform.position = np;
                            }
                            if (rubbleT <= 0f)
                            {
                                rubbleT = 0.35f;
                                // He does not clip through the block any more: whatever is in front of him comes down.
                                var shoulder = transform.position + f * height * 0.3f;
                                if (Crush == null || !Crush.CrushNear(shoulder, height * 0.55f, f)) Fx?.Stomp(shoulder, toP);
                            }
                        }
                        else if (distFlat > attackRange * 0.55f) { transform.position += Steer(f, sp * dt) * sp * dt; Plow(f, dt); }   // close enough that a stomp can actually land on a grounded player
                    }
                    break;
                case State.Attack:
                    Face(toP, dt * 0.35f);   // committed: he cannot track you through the swing
                    if (!hitDone && t > windUp)
                    {
                        hitDone = true; Sfx.Play("titan_step", transform.position + transform.forward * height * 0.4f, attackKind == Pose.Stomp ? 0.35f : 0.6f, 1f, 260f);
                        Vector3 hitCenter = transform.position + transform.forward * height * 0.38f + Vector3.up * (attackKind == Pose.Stomp ? height * 0.06f : height * 0.45f);
                        float r = attackKind == Pose.Stomp ? height * 0.24f : height * 0.3f;   // stomp reaches ~3.6 m around the foot, swipe ~4.5 m around the hand
                        if (attackKind == Pose.Stomp)
                        {
                            var foot = new Vector3(hitCenter.x, transform.position.y, hitCenter.z);
                            Fx?.Stomp(foot, toP);
                            Crush?.CrushNear(foot, height * 0.3f, transform.forward);
                        }
                        else Fx?.Swipe(hitCenter);
                        bool inArc = InFront(toP, 0.25f);   // behind or beside him you are safe: that is where the nape is
                        if (inArc && Vector3.Distance(pl.position, hitCenter) < r) (playerCtrl as ODMHit)?.Hit(this, attackKind == Pose.Stomp ? stompDamage : swipeDamage);
                        else playerCtrl.SendMessage("TakeHitIfInside", new object[] { hitCenter, r, attackKind == Pose.Stomp ? stompDamage : swipeDamage }, SendMessageOptions.DontRequireReceiver);
                    }
                    if (t > attackLength) { Current = State.Chase; cooldown = attackCooldown; }
                    break;
                case State.Stagger:
                    if (t > 1.1f) { Current = State.Chase; cooldown = 0.6f; }
                    break;
                case State.Kneel:
                    kneelTimer -= dt;
                    if (kneelTimer <= 0f) { HamL = HamR = false; Current = State.Chase; walkSpeed *= 1.15f; sprintSpeed *= 1.15f; cooldown = 0.5f; Fx?.NapePlume(false); Roar(); }
                    break;
                case State.Dead:
                    // He stays down where he fell; the ending screen takes over.
                    if (!endShown && t > 2.5f) { endShown = true; var ttl = Ctx.Get<string>("gameOverTitle"); Ctx.Set("gameOver", string.IsNullOrEmpty(ttl) ? "TITAN SLAIN" : ttl); }
                    break;
            }
        }
        Pose attackKind; bool hitDone; bool endShown; bool locked, hinted; Transform lockPoint;
        Transform LockPoint()
        {
            if (lockPoint == null) { var go = new GameObject("TitanLockPoint"); go.transform.SetParent(transform, false); go.transform.localPosition = Vector3.up * height * 0.55f; lockPoint = go.transform; }
            return lockPoint;
        }
        void SwipeWhoosh() { if (Current == State.Attack) Sfx.PlayClip(Synth.Whoosh(), transform.position + transform.forward * height * 0.35f + Vector3.up * height * 0.45f, 0.55f, 1f, 220f); }
        void Roar() { Sfx.PlayClip(Synth.Roar(), transform.position + Vector3.up * height * 0.9f, 1f, 1f, 400f); Fx?.Step(0f); Ctx.Set("roarAt", Time.unscaledTime); }
        bool InFront(Vector3 toP, float minDot) { var f = transform.forward; f.y = 0f; toP.y = 0f; if (toP.sqrMagnitude < 0.01f) return true; return Vector3.Dot(f.normalized, toP.normalized) > minDot; }
        static readonly float[] probeAngles = { 0f, -35f, 35f, -70f, 70f, -110f, 110f };
        /// <summary>Obstacle avoidance: a fat sphere cast at chest height; the first clear direction wins, else stay put.</summary>
        Vector3 steerDir; float steerHold;
        Vector3 Steer(Vector3 want, float step)
        {
            float r = height * 0.16f; Vector3 origin = transform.position + Vector3.up * height * 0.45f;
            float look = Mathf.Max(step * 8f, height * 0.5f);
            int mask = ~(1 << gameObject.layer);
            float Free(Vector3 d)
            {
                if (!Physics.SphereCast(origin, r, d, out var h, look, mask, QueryTriggerInteraction.Ignore)) return look;
                if (h.collider.transform.root == transform.root || h.collider.GetComponentInParent<Rigidbody>() != null) return look;
                return h.distance;
            }
            bool Clear(Vector3 d) => Free(d) >= look;
            // hysteresis: keep the last chosen direction while it is still clear, so he does not zigzag between probes every frame
            Vector3 chosen = Vector3.zero;
            if (steerHold > 0f && steerDir.sqrMagnitude > 0.5f && Vector3.Dot(steerDir, want) > 0.3f && Clear(steerDir)) chosen = steerDir;
            else
            {
                float bestFree = 0f; Vector3 best = Vector3.zero;
                for (int i = 0; i < probeAngles.Length; i++)
                {
                    var d = Quaternion.AngleAxis(probeAngles[i], Vector3.up) * want;
                    float f = Free(d);
                    if (f >= look) { chosen = d; steerHold = 0.6f; break; }
                    if (f > bestFree) { bestFree = f; best = d; }
                }
                // boxed in (an alley, a corner between houses): take the longest opening instead of standing still,
                // and after a while just shoulder through: a 15 m Titan is not stopped by a cottage
                if (chosen.sqrMagnitude < 0.5f)
                {
                    stuckT += Time.deltaTime + steerHoldSpent; steerHoldSpent = 0f;
                    if (stuckT > 4f) { chosen = want; steerHold = 2.5f; steerHoldSpent = 2.5f; Fx?.Step(0f); }     // shoulder through
                    else if (bestFree > height * 0.15f) { chosen = best; steerHold = 1.2f; steerHoldSpent = 1.2f; }  // commit to the widest gap, no dithering
                }
                else stuckT = 0f;
            }
            steerHold -= Time.deltaTime;
            if (chosen.sqrMagnitude < 0.5f) return Vector3.zero;
            steerDir = steerDir.sqrMagnitude < 0.5f ? chosen : Vector3.Slerp(steerDir, chosen, 1f - Mathf.Exp(-5f * Time.deltaTime));
            return steerDir.normalized;
        }
        void Face(Vector3 to, float dt)
        {
            to.y = 0f; if (to.sqrMagnitude < 0.01f) return;
            var want = Quaternion.LookRotation(to.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnRate * dt);
        }
        void Set(Pose p) { if (Poser != null && Poser.Current != p) Poser.SetPose(p); }
        float stepTimer;
        void LateUpdate()
        {
            if (Current != State.Chase) return;
            stepTimer -= Time.deltaTime;
            if (stepTimer <= 0f) { stepTimer = Poser != null && Poser.Current == Pose.Sprint ? 0.42f : 0.62f; Sfx.Play("titan_step", transform.position, 0.45f, 1f, 220f); var pl = Player; if (pl != null) Fx?.Step(Vector3.Distance(pl.position, transform.position)); }
        }

        /// <summary>A blade hit on one of the named zones.</summary>
        /// <summary>Below 25% HP only the nape kills: ODM onto his upper half and slash. Everything else bounces off.</summary>
        public bool NapePhase => Current != State.Dead && HP <= HPMax * napePhaseAt;
        public float napePhaseAt = 0.25f;
        /// <summary>Mikasa is on the back of his neck: he runs and thrashes, cannot attack, and each stab takes a fifth of the last quarter.</summary>
        public bool Ridden;
        public int StabsToKill = 5;
        float wanderSign = 1f, wanderT, stuckT, steerHoldSpent, progressT, bestDist = 1e9f, bulldozeT, rubbleT, plowT;

        /// <summary>
        /// Steer only avoids what it can see a way around; boxed into an alley it takes the least-bad opening and
        /// walks straight into a wall, and the player then watches a 15 m Titan slide through a house. He levels it
        /// instead: anything his torso reaches comes down. Cheap - one scan a quarter second, only while he moves.
        /// </summary>
        void Plow(Vector3 dir, float dt)
        {
            plowT -= dt;
            if (plowT > 0f || Crush == null) return;
            plowT = 0.08f;   // at a sprint he covers 3 m in a quarter second: a slow scan let him get a body deep into a house first
            // his own footprint and one stride ahead, and up to two houses per scan (a block can put three against him at once)
            int down = 0;
            if (Crush.CrushNear(transform.position + dir * (height * 0.22f), height * 0.2f, dir)) down++;
            if (Crush.CrushNear(transform.position, height * 0.16f, dir)) down++;
            if (down > 0 && Harness.Active) Debug.Log("[Plow] " + down + " down at " + transform.position.ToString("0.0") + " total=" + Crush.Crushed);
        }

        /// <summary>The town's crusher, looked up once (Town does not exist in EditMode tests or with -noTown).</summary>
        ICrush crush; bool crushLooked;
        ICrush Crush
        {
            get
            {
                if (!crushLooked) { crush = Ctx.Get<ICrush>("town.destruction"); crushLooked = crush != null; }
                return crush;
            }
        }
        public Vector3 NapeWorld() => Fx != null ? Fx.NapePos() : transform.position + Vector3.up * height * 0.85f;

        /// <summary>How far off the nape surface her feet sit, and how far up the neck she kneels.</summary>
        public float seatOut = 0.18f, seatUp = 0.35f;

        /// <summary>
        /// Where a rider's feet actually go. NapeWorld() is the CENTRE of the nape zone, which is inside his
        /// neck: seating her there left her floating a nape-radius above the surface, gliding rather than
        /// riding. This walks back out to the zone's rear face along his forward axis and sits her on it.
        /// </summary>
        Transform rideBone; Vector3 rideOffset; Quaternion rideBoneRot0;

        /// <summary>
        /// Called once when she mounts: pins the seat to his animated neck bone. The Zone_ colliders hang off the
        /// proxy skeleton, which stops being posed as soon as the Meshy model dresses him, so a zone-only seat is
        /// really root-relative and does not follow anything his animation does.
        /// </summary>
        public void PinRide()
        {
            rideBone = null;
            var m = Ctx.Get<Characters.CharacterModel>("bossModel");
            var an = m != null ? m.animator : null;
            if (an == null || !an.isHuman) return;
            var neck = an.GetBoneTransform(HumanBodyBones.Neck) ?? an.GetBoneTransform(HumanBodyBones.Head);
            if (neck == null) return;
            SeatFromZone(out var pos, out _);
            rideBone = neck; rideOffset = pos - neck.position; rideBoneRot0 = neck.rotation;
        }

        public void RideSeat(out Vector3 pos, out Quaternion rot)
        {
            if (rideBone != null)
            {
                // rigid-follow the neck: no scale in the maths, so his 15 m model cannot double-apply it
                pos = rideBone.position + (rideBone.rotation * Quaternion.Inverse(rideBoneRot0)) * rideOffset;
                rot = transform.rotation;
                return;
            }
            SeatFromZone(out pos, out rot);
        }

        void SeatFromZone(out Vector3 pos, out Quaternion rot)
        {
            Vector3 c = NapeWorld();
            var z = Fx != null ? Fx.NapeCollider() : null;
            float depth = height * 0.06f;
            float lift = 0f;
            if (z != null)
            {
                // the zone's bounds are axis-aligned: project their extents onto his facing to get the rear face
                var e = z.bounds.extents;
                var f = transform.forward;
                depth = Mathf.Abs(e.x * f.x) + Mathf.Abs(e.y * f.y) + Mathf.Abs(e.z * f.z);
                lift = -e.y * 0.35f;   // down the back of the neck a little, not perched on top of the zone
            }
            pos = c - transform.forward * (depth + seatOut) + Vector3.up * (lift + seatUp);
            rot = transform.rotation;
        }
        /// <summary>She just landed on his neck: a roar, a stagger, a head shake, and the camera feels it.</summary>
        public void Mounted()
        {
            if (Current == State.Dead) return;
            Roar(); Fx?.CameraPunch(0.5f);
            var m = Ctx.Get<Characters.CharacterModel>("bossModel"); if (m != null) m.ShakeHead();
            if (Current != State.Kneel && !Ridden) { Current = State.Stagger; t = 0.2f; Set(Pose.Stagger); }
        }
        /// <summary>One stab from the rider. Returns true when this was the killing one (the caller plays the final plunge, then NapeKill).</summary>
        public bool Stab(int n)
        {
            if (Current == State.Dead) return false;
            float step = HPMax * napePhaseAt / StabsToKill;
            HP = Mathf.Max(n >= StabsToKill ? 0f : 1f, HP - step);
            Vector3 at = NapeWorld();
            Fx?.HitBurst(at, 0.8f);
            Fx?.CameraPunch(n >= StabsToKill ? 0.9f : 0.6f);
            (Poser as Characters.CharacterModel)?.ShakeHead(0.6f);
            HudEvents.Add(at, n >= StabsToKill ? "NAPE" : (n + " / " + StabsToKill), new Color(1f, 0.85f, 0.3f), n >= StabsToKill ? 1.8f : 1.3f);
            HitStop.Do(n >= StabsToKill ? 0.14f : 0.06f);
            Sfx.PlayClip(Synth.Squelch(), at, n >= StabsToKill ? 0.8f : Random.Range(0.95f, 1.1f), 1f, 200f);
            Sfx.Play("titan_hit", at, 0.5f, 0.9f, 200f);
            if (n % 2 == 1) Roar();
            // No stagger clip while she is on his neck. Meshy's `hit` take has the travel baked into the hips
            // (merge_clips only strips that for the INPLACE locomotion clips), so every stab slid his whole body
            // sideways while her seat stayed put, and she was left hanging over where he used to be. The stab still
            // reads: spray, head shake, camera punch, hit-stop, HUD dot, roar on odd stabs.
            if (Current != State.Kneel && Current != State.Stagger && !Ridden) { Current = State.Stagger; t = 0.5f; Set(Pose.Stagger); }
            if (Harness.Active) Debug.Log("[TitanStab] n=" + n + " hp=" + HP + " t=" + Time.time.ToString("0.00"));
            return n >= StabsToKill;
        }
        bool napeAnnounced;
        /// <summary>The nape phase kill: an airborne slash on the upper half of his body. Freezes into the nape cutscene
        /// (Hud plays StreamingAssets/nape.mp4), then <see cref="FinishNapeKill"/> drops him.</summary>
        public void NapeKill(Vector3 from)
        {
            if (Current == State.Dead || Ctx.Get<bool>("napeCutscene")) return;
            Vector3 at = Fx != null ? Fx.NapePos() : ZonePos("Zone_Nape", from);
            Fx?.HitBurst(at, 1f); Fx?.NapePlume(true);
            HudEvents.Add(at, "NAPE", new Color(1f, 0.85f, 0.3f), 1.8f);
            HitStop.Do(0.16f);
            Sfx.Play("titan_hit", at, 0.9f, 1f, 200f);
            if (Harness.Active) Debug.Log("[TitanHit] NAPE KILL hp=" + HP + " t=" + Time.time.ToString("0.00"));
            Ctx.Set("napeCutscene", true); Ctx.Set("napeCutsceneAt", Time.unscaledTime);
        }
        /// <summary>Called by the Hud when the cutscene ends: he dies for real and the ending card follows.</summary>
        public void FinishNapeKill()
        {
            if (Current == State.Dead) return;
            HP = 0f; Current = State.Dead; t = 2.0f; Set(Pose.Stagger); Invoke(nameof(DeathPose), 0.2f);
            Ctx.Set("bossDead", true); Ctx.Set("gameOverTitle", "YOU WON");
            Fx?.Death();
            var rig = Ctx.Get<Component>("cameraRig"); if (rig != null && !Application.isBatchMode) rig.SendMessage("KillCam", Fx != null ? Fx.NapePos() : transform.position + Vector3.up * height * 0.9f, SendMessageOptions.DontRequireReceiver);
        }
        public float Hit(string zone, Vector3 from)
        {
            if (Current == State.Dead) return 0f;
            float dmg = zone == "cannon" ? 40f : zone == "Zone_Nape" ? (Current == State.Kneel ? 100f : 40f) : zone.StartsWith("Zone_Hamstring") ? 18f : zone.StartsWith("Zone_") ? 12f : 6f;
            if (NapePhase)
            {
                // nothing but the nape cut lands now: the blade skids off, he shrugs it, the banner tells you where to go
                Vector3 skid = ZonePos(zone, from);
                Fx?.HitBurst(skid, 0.15f);
                HudEvents.Add(skid, "NAPE ONLY", new Color(0.75f, 0.75f, 0.8f), 0.9f);
                Sfx.Play("titan_hit", skid, 0.3f, 1.6f, 200f);
                if (Harness.Active) Debug.Log("[TitanHit] zone=" + zone + " blocked (nape phase) hp=" + HP);
                return 0f;
            }
            // never drop below the nape threshold from ordinary hits: the last quarter is the nape's
            dmg = Mathf.Min(dmg, Mathf.Max(0f, HP - HPMax * napePhaseAt));
            HP = Mathf.Max(0f, HP - dmg); Sfx.Play("titan_hit", transform.position + Vector3.up * height * 0.5f, 0.5f, 1f, 200f);
            if (NapePhase && !napeAnnounced)
            {
                napeAnnounced = true; Roar(); Ctx.Set("napePhaseAt", Time.unscaledTime);
                HudEvents.Add(transform.position + Vector3.up * height * 1.05f, "GO FOR THE NAPE", new Color(1f, 0.8f, 0.2f), 2.0f, 3f);
                if (Current != State.Kneel) { Current = State.Stagger; t = 0f; Set(Pose.Stagger); }
                return dmg;
            }
            if (Harness.Active) Debug.Log("[TitanHit] zone=" + zone + " dmg=" + dmg + " hp=" + HP + " state=" + Current + " t=" + Time.time.ToString("0.00"));
            // presentation: steam and a red spray at the cut, a number, a beat of hit-stop on the heavy ones
            Vector3 at = ZonePos(zone, from);
            bool nape = zone == "Zone_Nape", ham = zone.StartsWith("Zone_Hamstring");
            Fx?.HitBurst(at, nape ? 1f : zone == "cannon" ? 0.8f : ham ? 0.55f : 0.3f);
            HudEvents.Add(at, dmg.ToString("0"), nape ? new Color(1f, 0.85f, 0.3f) : ham ? new Color(1f, 0.55f, 0.35f) : Color.white, nape ? 1.5f : ham ? 1.15f : 1f);
            if (nape || zone == "cannon") HitStop.Do(nape && HP <= 0f ? 0.16f : 0.07f); else if (ham) HitStop.Do(0.04f);
            if (HP <= 0f)
            {
                Current = State.Dead; t = 0f; Set(Pose.Stagger); Invoke(nameof(DeathPose), 0.4f);
                Ctx.Set("bossDead", true);
                Fx?.Death();
                var rig = Ctx.Get<Component>("cameraRig"); if (rig != null && !Application.isBatchMode) rig.SendMessage("KillCam", Fx != null ? Fx.NapePos() : at, SendMessageOptions.DontRequireReceiver);
                return dmg;
            }
            if (zone == "Zone_HamstringL") HamL = true;
            if (zone == "Zone_HamstringR") HamR = true;
            if (HamL && HamR && Current != State.Kneel) { Current = State.Kneel; kneelTimer = 4f; t = 0f; Set(Pose.Kneel); Fx?.NapePlume(true); HudEvents.Add(Fx != null ? Fx.NapePos() : at, "NAPE OPEN", new Color(1f, 0.8f, 0.2f), 1.3f); return dmg; }
            if (Current != State.Kneel && zone != "body") { Current = State.Stagger; if (zone == "cannon") t = -0.6f; else t = 0f; Set(Pose.Stagger); }
            return dmg;
        }
        Vector3 ZonePos(string zone, Vector3 fallback)
        {
            if (zone == "cannon" || zone == "body") return fallback;
            var z = FindDeep(transform, zone); if (z == null) return fallback;
            var c = z.GetComponent<Collider>(); return c != null ? c.bounds.center : z.position;
        }
        static Transform FindDeep(Transform t, string name) { if (t.name == name) return t; for (int i = 0; i < t.childCount; i++) { var r = FindDeep(t.GetChild(i), name); if (r != null) return r; } return null; }
        void DeathPose() { var m = Ctx.Get<Characters.CharacterModel>("bossModel"); if (m != null) m.PlayClip("death"); }
    }

    /// <summary>Implemented by the player controller so the brain can hit it without an assembly reference.</summary>
    public interface ODMHit { void Hit(TitanBrain from, float damage); }
}
