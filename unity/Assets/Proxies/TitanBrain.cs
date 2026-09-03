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
        public float swipeDamage = 35f, stompDamage = 45f;
        public bool HamL, HamR;
        IPoser poser; Transform player; Component playerCtrl; float t, cooldown, kneelTimer, roarTimer;
        Vector3 spawnPos;
        public static TitanBrain Attach(GameObject host, float height)
        {
            var b = host.GetComponent<TitanBrain>() ?? host.AddComponent<TitanBrain>();
            b.height = height; b.walkSpeed = height * 0.45f; b.sprintSpeed = height * 0.8f; b.attackRange = height * 1.1f;
            b.spawnPos = host.transform.position;
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
            switch (Current)
            {
                case State.Idle:
                    Set(Pose.Idle);
                    if (distFlat < sightRange) { Current = State.Chase; roarTimer = 1.2f; }
                    break;
                case State.Chase:
                    Face(toP, dt);
                    if (distFlat < attackRange && cooldown <= 0f) { Current = State.Attack; t = 0f; attackKind = pl.position.y > height * 0.35f ? Pose.Swipe : (Random.value < 0.5f ? Pose.Stomp : Pose.Swipe); Set(attackKind); hitDone = false; }
                    else
                    {
                        bool sprint = distFlat > attackRange * 3f;
                        Set(sprint ? Pose.Sprint : Pose.Run);
                        float sp = sprint ? sprintSpeed : walkSpeed;
                        var f = transform.forward; f.y = 0f; f.Normalize();
                        if (distFlat > attackRange * 0.8f) transform.position += Steer(f, sp * dt) * sp * dt;
                    }
                    break;
                case State.Attack:
                    Face(toP, dt * 0.5f);
                    if (!hitDone && t > 0.55f)
                    {
                        hitDone = true;
                        Vector3 hitCenter = transform.position + transform.forward * height * 0.45f + Vector3.up * (attackKind == Pose.Stomp ? height * 0.08f : height * 0.5f);
                        float r = attackKind == Pose.Stomp ? height * 0.42f : height * 0.5f;
                        if (Vector3.Distance(pl.position, hitCenter) < r) (playerCtrl as ODMHit)?.Hit(this, attackKind == Pose.Stomp ? stompDamage : swipeDamage);
                        else playerCtrl.SendMessage("TakeHitIfInside", new object[] { hitCenter, r, attackKind == Pose.Stomp ? stompDamage : swipeDamage }, SendMessageOptions.DontRequireReceiver);
                    }
                    if (t > 1.6f) { Current = State.Chase; cooldown = 1.2f; }
                    break;
                case State.Stagger:
                    if (t > 1.1f) { Current = State.Chase; cooldown = 0.6f; }
                    break;
                case State.Kneel:
                    kneelTimer -= dt;
                    if (kneelTimer <= 0f) { HamL = HamR = false; Current = State.Chase; walkSpeed *= 1.15f; sprintSpeed *= 1.15f; cooldown = 0.5f; }
                    break;
                case State.Dead:
                    if (t > 6f && transform.position.y > -height * 1.2f) transform.position += Vector3.down * dt * 2f; // sink away
                    break;
            }
        }
        Pose attackKind; bool hitDone;
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

        /// <summary>A blade hit on one of the named zones.</summary>
        public void Hit(string zone, Vector3 from)
        {
            if (Current == State.Dead) return;
            if (zone == "Zone_Nape" && (Current == State.Kneel || Current == State.Stagger || true))
            {
                Current = State.Dead; t = 0f; Set(Pose.Stagger); Invoke(nameof(DeathPose), 0.4f);
                Ctx.Set("bossDead", true); return;
            }
            if (zone == "Zone_HamstringL") HamL = true;
            if (zone == "Zone_HamstringR") HamR = true;
            if (HamL && HamR && Current != State.Kneel) { Current = State.Kneel; kneelTimer = 4f; t = 0f; Set(Pose.Kneel); return; }
            if (Current != State.Kneel) { Current = State.Stagger; t = 0f; Set(Pose.Stagger); }
        }
        void DeathPose() { var m = Ctx.Get<Characters.CharacterModel>("bossModel"); if (m != null) m.PlayClip("death"); }
    }

    /// <summary>Implemented by the player controller so the brain can hit it without an assembly reference.</summary>
    public interface ODMHit { void Hit(TitanBrain from, float damage); }
}
