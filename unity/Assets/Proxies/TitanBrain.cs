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
        public float windUp = 1.05f, attackLength = 2.1f, attackCooldown = 2.6f;   // a readable telegraph, then a real opening
        public bool HamL, HamR;
        public float HP = 100f, HPMax = 100f;
        IPoser poser; Transform player; Component playerCtrl; float t, cooldown, kneelTimer, roarTimer;
        public TitanFx Fx { get; private set; }
        Vector3 spawnPos;
        public static TitanBrain Attach(GameObject host, float height)
        {
            var b = host.GetComponent<TitanBrain>() ?? host.AddComponent<TitanBrain>();
            b.height = height; b.walkSpeed = height * 0.45f; b.sprintSpeed = height * 0.8f; b.attackRange = height * 0.62f;   // ~9 m for the 15 m boss: he has to actually reach you
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
            bool wantLock = Current != State.Idle && Current != State.Dead && distFlat < 80f;
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
                    if (distFlat < sightRange || (Ctx.Has("introUntil") && Time.unscaledTime > Ctx.Get<float>("introUntil"))) { Current = State.Chase; roarTimer = 1.2f; Roar(); }
                    break;
                case State.Chase:
                    Face(toP, dt);
                    if (distFlat < attackRange && cooldown <= 0f && InFront(toP, 0.1f))
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
                        bool sprint = distFlat > attackRange * 3f;
                        Set(sprint ? Pose.Sprint : Pose.Run);
                        float sp = sprint ? sprintSpeed : walkSpeed;
                        var f = transform.forward; f.y = 0f; f.Normalize();
                        if (distFlat > attackRange * 0.55f) transform.position += Steer(f, sp * dt) * sp * dt;   // close enough that a stomp can actually land on a grounded player
                    }
                    break;
                case State.Attack:
                    Face(toP, dt * 0.35f);   // committed: he cannot track you through the swing
                    if (!hitDone && t > windUp)
                    {
                        hitDone = true; Sfx.Play("titan_step", transform.position + transform.forward * height * 0.4f, attackKind == Pose.Stomp ? 0.35f : 0.6f, 1f, 260f);
                        Vector3 hitCenter = transform.position + transform.forward * height * 0.38f + Vector3.up * (attackKind == Pose.Stomp ? height * 0.06f : height * 0.45f);
                        float r = attackKind == Pose.Stomp ? height * 0.24f : height * 0.3f;   // stomp reaches ~3.6 m around the foot, swipe ~4.5 m around the hand
                        if (attackKind == Pose.Stomp) Fx?.Stomp(new Vector3(hitCenter.x, transform.position.y, hitCenter.z), toP); else Fx?.Swipe(hitCenter);
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
                    if (!endShown && t > 2.5f) { endShown = true; Ctx.Set("gameOver", "TITAN SLAIN"); }
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
        Vector3 Steer(Vector3 want, float step)
        {
            float r = height * 0.16f; Vector3 origin = transform.position + Vector3.up * height * 0.45f;
            float look = Mathf.Max(step * 8f, height * 0.5f);
            int mask = ~(1 << gameObject.layer);
            for (int i = 0; i < probeAngles.Length; i++)
            {
                var d = Quaternion.AngleAxis(probeAngles[i], Vector3.up) * want;
                if (!Physics.SphereCast(origin, r, d, out var h, look, mask, QueryTriggerInteraction.Ignore) || h.collider.transform.root == transform.root || h.collider.GetComponentInParent<Rigidbody>() != null)
                    return d;
            }
            return Vector3.zero;
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
        public float Hit(string zone, Vector3 from)
        {
            if (Current == State.Dead) return 0f;
            float dmg = zone == "cannon" ? 40f : zone == "Zone_Nape" ? (Current == State.Kneel ? 100f : 40f) : zone.StartsWith("Zone_Hamstring") ? 18f : zone.StartsWith("Zone_") ? 12f : 6f;
            HP = Mathf.Max(0f, HP - dmg); Sfx.Play("titan_hit", transform.position + Vector3.up * height * 0.5f, 0.5f, 1f, 200f);
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
