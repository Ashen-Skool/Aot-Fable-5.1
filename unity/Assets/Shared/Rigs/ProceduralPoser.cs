using UnityEngine;

namespace Shared.Rigs
{
    /// <summary>
    /// Procedural IPoser for HumanoidProxy. Every pose is a function of phase (seconds in
    /// human tempo; titans run the same curves slower through Proportions.tempo) that yields
    /// a local rotation per bone plus a hips offset. Tick blends the live pose toward the
    /// target so pose switches never pop. No per-frame allocation: all arrays are fixed.
    ///
    /// Conventions (bind pose = identity, character faces +Z, limbs hang along -Y):
    ///   Limb(fwd, out): positive fwd swings the limb forward (+Z), positive out swings it away
    ///   from the body midline. Knee(bend) folds the shin backward, Elbow(bend) folds the forearm
    ///   forward. Torso(pitch, yaw, roll): positive pitch leans forward.
    /// </summary>
    public class ProceduralPoser : IPoser
    {
        const int N = HumanoidProxy.BoneCount;
        const float BlendRate = 14f;

        readonly HumanoidProxy rig;
        readonly Transform[] bone = new Transform[N];
        readonly Quaternion[] target = new Quaternion[N];
        readonly Quaternion[] live = new Quaternion[N];
        readonly Vector3 hipsBind;
        Vector3 hipsTarget, hipsLive;

        Pose current = Pose.Idle;
        float phase;
        float speed = 1f;
        bool paused;

        public Pose Current => current;
        public float Phase { get => phase; set => phase = value; }
        public float Speed { get => speed; set => speed = value; }
        public bool Paused { get => paused; set => paused = value; }

        public ProceduralPoser(HumanoidProxy rig)
        {
            this.rig = rig;
            for (int i = 0; i < N; i++) { bone[i] = rig.Bone((BoneId)i); target[i] = live[i] = Quaternion.identity; }
            hipsBind = rig.BindLocalPosition(BoneId.Hips);
            hipsTarget = hipsLive = hipsBind;
        }

        public void SetPose(Pose pose)
        {
            if (pose == current) return;
            current = pose;
            phase = 0f;
        }

        public void Snap(Pose pose, float ph)
        {
            current = pose;
            phase = ph;
            Compute();
            for (int i = 0; i < N; i++) live[i] = target[i];
            hipsLive = hipsTarget;
            Apply();
        }

        public void Tick(float dt)
        {
            if (!paused) phase += dt * speed * rig.props.tempo;
            Compute();
            float k = 1f - Mathf.Exp(-dt * BlendRate);
            for (int i = 0; i < N; i++) live[i] = Quaternion.Slerp(live[i], target[i], k);
            hipsLive = Vector3.Lerp(hipsLive, hipsTarget, k);
            Apply();
        }

        void Apply()
        {
            for (int i = 0; i < N; i++) bone[i].localRotation = live[i];
            bone[(int)BoneId.Hips].localPosition = hipsLive;
        }

        // ---------------- pose curves ----------------

        void Compute()
        {
            for (int i = 0; i < N; i++) target[i] = Quaternion.identity;
            hipsTarget = hipsBind;
            float u = phase;
            if (rig.props.titan)
            {
                switch (current)
                {
                    case Pose.Land: TitanLand(u); return;
                    case Pose.Stagger: TitanStagger(u); return;
                    case Pose.Kneel: TitanKneel(u); return;
                    case Pose.Swipe: TitanSwipe(u); return;
                    case Pose.Grab: TitanGrab(u); return;
                    case Pose.Stomp: TitanStomp(u); return;
                }
            }
            switch (current)
            {
                case Pose.Idle: Idle(u); break;
                case Pose.Run: Run(u, 1f); break;
                case Pose.Sprint: Run(u, 1.55f); break;
                case Pose.Fly: Fly(u); break;
                case Pose.Slash: Slash(u); break;
                case Pose.Land: Land(u); break;
                case Pose.Stagger: Stagger(u); break;
                case Pose.Kneel: Kneel(u); break;
                case Pose.Swipe: Swipe(u); break;
                case Pose.Grab: Grab(u); break;
                case Pose.Stomp: Stomp(u); break;
            }
        }

        void Idle(float u)
        {
            float b = Mathf.Sin(u * Mathf.PI * 2f / 3.6f);
            Torso(BoneId.Spine, 1f, 0, 0);
            Torso(BoneId.Chest, 2f + 2f * b, 0, 0);
            Torso(BoneId.Head, -1.5f * b, 4f * Mathf.Sin(u * 0.5f), 0);
            Hips(0, 0.004f * b, 0, 0, 0, 0);
            Limb(BoneId.LeftUpperArm, 3f, 7f + 1.5f * b); Elbow(BoneId.LeftLowerArm, 10f);
            Limb(BoneId.RightUpperArm, 3f, 7f + 1.5f * b); Elbow(BoneId.RightLowerArm, 10f);
            Limb(BoneId.LeftUpperLeg, 0, 3f); Knee(BoneId.LeftLowerLeg, 2f);
            Limb(BoneId.RightUpperLeg, 0, 3f); Knee(BoneId.RightLowerLeg, 2f);
        }

        void Run(float u, float amp)
        {
            float hz = amp > 1.2f ? 3.0f : 2.6f;
            float w = u * Mathf.PI * 2f * hz;
            float s = Mathf.Sin(w), c = Mathf.Cos(w);
            float lean = amp > 1.2f ? 24f : 11f;
            float stride = 38f * amp;
            Limb(BoneId.LeftUpperLeg, stride * s, 2f);
            Limb(BoneId.RightUpperLeg, -stride * s, 2f);
            Knee(BoneId.LeftLowerLeg, 12f + 55f * amp * Mathf.Max(0, c));
            Knee(BoneId.RightLowerLeg, 12f + 55f * amp * Mathf.Max(0, -c));
            Foot(BoneId.LeftFoot, 12f * c);
            Foot(BoneId.RightFoot, -12f * c);
            float arm = 36f * amp;
            Limb(BoneId.LeftUpperArm, -arm * s - 5f, 9f); Elbow(BoneId.LeftLowerArm, amp > 1.2f ? 100f : 78f);
            Limb(BoneId.RightUpperArm, arm * s - 5f, 9f); Elbow(BoneId.RightLowerArm, amp > 1.2f ? 100f : 78f);
            float bounce = 0.018f * amp * Mathf.Max(0, -Mathf.Cos(2f * w));
            Hips(lean * 0.35f, -0.03f * amp + bounce, 0, 0, -7f * s, 2f * c);
            Torso(BoneId.Spine, lean * 0.3f, 4f * s, 0);
            Torso(BoneId.Chest, lean * 0.35f, 9f * s, -2f * c);
            Torso(BoneId.Head, -lean * 0.55f, -3f * s, 0);
        }

        void Fly(float u)
        {
            float wob = Mathf.Sin(u * 2.2f);
            Hips(74f, 0.10f, 0, 0, 0, 3f * wob);
            Torso(BoneId.Spine, -4f, 0, 0);
            Torso(BoneId.Chest, -12f, 0, 0);
            Torso(BoneId.Neck, -20f, 0, 0);
            Torso(BoneId.Head, -38f, 3f * wob, 0);
            Limb(BoneId.LeftUpperArm, -48f, 14f); Elbow(BoneId.LeftLowerArm, 12f);
            Limb(BoneId.RightUpperArm, -48f, 14f); Elbow(BoneId.RightLowerArm, 12f);
            Limb(BoneId.LeftUpperLeg, -12f, 2f + 2f * wob); Knee(BoneId.LeftLowerLeg, 14f); Foot(BoneId.LeftFoot, 30f);
            Limb(BoneId.RightUpperLeg, -14f, 2f - 2f * wob); Knee(BoneId.RightLowerLeg, 12f); Foot(BoneId.RightFoot, 30f);
        }

        void Slash(float u)
        {
            float p = Mathf.Repeat(u, 0.9f);
            float s = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.20f, 0.45f, p)); // 0 wound up, 1 followed through
            // twin blades: both arms wound up high on the right, swept down and across to the left
            Limb(BoneId.RightUpperArm, Mathf.Lerp(115f, 25f, s), Mathf.Lerp(65f, -45f, s), Mathf.Lerp(20f, -30f, s));
            Elbow(BoneId.RightLowerArm, Mathf.Lerp(35f, 6f, s));
            Limb(BoneId.LeftUpperArm, Mathf.Lerp(100f, 20f, s), Mathf.Lerp(-30f, 55f, s), Mathf.Lerp(-10f, 20f, s));
            Elbow(BoneId.LeftLowerArm, Mathf.Lerp(40f, 10f, s));
            float twist = Mathf.Lerp(-32f, 38f, s);
            Hips(8f, -0.06f, 0.02f, 0, twist * 0.4f, 0);
            Torso(BoneId.Spine, 8f, twist * 0.3f, 0);
            Torso(BoneId.Chest, 14f, twist * 0.5f, Mathf.Lerp(-6f, 8f, s));
            Torso(BoneId.Head, -12f, -twist * 0.6f, 0);
            Limb(BoneId.RightUpperLeg, 34f, 4f); Knee(BoneId.RightLowerLeg, 32f);
            Limb(BoneId.LeftUpperLeg, -26f, 6f); Knee(BoneId.LeftLowerLeg, 12f); Foot(BoneId.LeftFoot, 20f);
        }

        void Land(float u)
        {
            float settle = Mathf.Exp(-u * 3f); // deepest at impact, easing up a little
            float crouch = 0.75f + 0.25f * settle;
            Hips(0, -0.24f * crouch, -0.02f, 0, 0, 0);
            Limb(BoneId.LeftUpperLeg, 72f * crouch, 14f); Knee(BoneId.LeftLowerLeg, 100f * crouch);
            Limb(BoneId.RightUpperLeg, 72f * crouch, 14f); Knee(BoneId.RightLowerLeg, 100f * crouch);
            Foot(BoneId.LeftFoot, -28f * crouch); Foot(BoneId.RightFoot, -28f * crouch);
            Torso(BoneId.Spine, 12f * crouch, 0, 0);
            Torso(BoneId.Chest, 26f * crouch, 0, 0);
            Torso(BoneId.Head, -28f * crouch, 0, 0);
            Limb(BoneId.LeftUpperArm, 22f, 48f); Elbow(BoneId.LeftLowerArm, 25f);
            Limb(BoneId.RightUpperArm, 22f, 48f); Elbow(BoneId.RightLowerArm, 25f);
        }

        void Stagger(float u)
        {
            float wob = Mathf.Sin(u * 7f) * Mathf.Exp(-u * 0.6f);
            Hips(-18f, -0.03f, -0.06f, 0, 0, 6f * wob);
            Torso(BoneId.Spine, -8f, 4f * wob, 0);
            Torso(BoneId.Chest, -14f, 6f * wob, -4f * wob);
            Torso(BoneId.Head, -24f, 0, 5f * wob);
            Limb(BoneId.LeftUpperArm, 45f, 75f); Elbow(BoneId.LeftLowerArm, 40f);
            Limb(BoneId.RightUpperArm, 38f, 82f); Elbow(BoneId.RightLowerArm, 50f);
            Limb(BoneId.LeftUpperLeg, 32f, 10f); Knee(BoneId.LeftLowerLeg, 6f); Foot(BoneId.LeftFoot, -20f);
            Limb(BoneId.RightUpperLeg, -22f, 12f); Knee(BoneId.RightLowerLeg, 40f); Foot(BoneId.RightFoot, 25f);
        }

        void Kneel(float u)
        {
            float breath = Mathf.Sin(u * 4f);
            float drop = rig.props.upperLeg - 0.03f;
            Hips(6f, -drop, 0, 0, 0, 0);
            // right knee on the ground: thigh vertical, shin folded back along the ground
            Limb(BoneId.RightUpperLeg, -4f, 6f); Knee(BoneId.RightLowerLeg, 96f); Foot(BoneId.RightFoot, 30f);
            // left leg planted in front: thigh horizontal, shin vertical
            Limb(BoneId.LeftUpperLeg, 88f, 10f); Knee(BoneId.LeftLowerLeg, 92f); Foot(BoneId.LeftFoot, -6f);
            Torso(BoneId.Spine, 10f, 0, 0);
            Torso(BoneId.Chest, 14f + 3f * breath, 0, 0);
            Torso(BoneId.Head, 14f, 0, 0);
            Limb(BoneId.RightUpperArm, 58f, 14f); Elbow(BoneId.RightLowerArm, 4f);
            Limb(BoneId.LeftUpperArm, 28f, 6f); Elbow(BoneId.LeftLowerArm, 72f);
        }

        void Swipe(float u)
        {
            float p = Mathf.Repeat(u, 1.4f);
            float s = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.65f, p));
            float back = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.1f, 1.4f, p));
            s *= back;
            // right arm at shoulder height sweeping from behind to across the body
            Limb(BoneId.RightUpperArm, Mathf.Lerp(-45f, 110f, s), Mathf.Lerp(88f, 60f, s), Mathf.Lerp(0f, -20f, s));
            Elbow(BoneId.RightLowerArm, Mathf.Lerp(28f, 8f, s));
            Limb(BoneId.LeftUpperArm, Mathf.Lerp(20f, -35f, s), 34f); Elbow(BoneId.LeftLowerArm, 45f);
            float twist = Mathf.Lerp(-38f, 48f, s);
            Hips(6f, -0.04f, 0, 0, twist * 0.35f, 0);
            Torso(BoneId.Spine, 6f, twist * 0.3f, 0);
            Torso(BoneId.Chest, 12f, twist * 0.45f, 0);
            Torso(BoneId.Head, -6f, -twist * 0.5f, 0);
            Limb(BoneId.RightUpperLeg, 16f, 20f); Knee(BoneId.RightLowerLeg, 18f);
            Limb(BoneId.LeftUpperLeg, -14f, 20f); Knee(BoneId.LeftLowerLeg, 14f);
        }

        void Grab(float u)
        {
            float reach = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.5f));
            float fwd = Mathf.Lerp(55f, 88f, reach);
            Limb(BoneId.LeftUpperArm, fwd, 14f, 10f); Elbow(BoneId.LeftLowerArm, 14f); Hand(BoneId.LeftHand, 45f);
            Limb(BoneId.RightUpperArm, fwd, 14f, -10f); Elbow(BoneId.RightLowerArm, 14f); Hand(BoneId.RightHand, 45f);
            Hips(10f, -0.05f, 0.02f, 0, 0, 0);
            Torso(BoneId.Spine, 10f, 0, 0);
            Torso(BoneId.Chest, 18f * reach + 6f, 0, 0);
            Torso(BoneId.Head, 10f, 0, 0);
            Limb(BoneId.RightUpperLeg, 28f, 4f); Knee(BoneId.RightLowerLeg, 24f);
            Limb(BoneId.LeftUpperLeg, -22f, 6f); Knee(BoneId.LeftLowerLeg, 12f); Foot(BoneId.LeftFoot, 22f);
        }

        void Stomp(float u)
        {
            float p = Mathf.Repeat(u, 1.6f);
            float lift = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.7f, p));
            float slam = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 0.85f, p));
            float raise = lift * (1f - slam);
            float thigh = Mathf.Lerp(0f, 95f, raise);
            float knee = Mathf.Lerp(4f, 100f, raise);
            Limb(BoneId.RightUpperLeg, thigh, 8f); Knee(BoneId.RightLowerLeg, knee); Foot(BoneId.RightFoot, -12f * raise);
            Limb(BoneId.LeftUpperLeg, -4f, 6f); Knee(BoneId.LeftLowerLeg, 6f);
            float lean = Mathf.Lerp(-6f, 6f, raise) + 22f * slam;
            Hips(lean * 0.4f, -0.02f - 0.05f * slam, 0, 0, 0, -6f * raise);
            Torso(BoneId.Spine, lean * 0.3f, 0, 0);
            Torso(BoneId.Chest, lean * 0.4f, 0, 4f * raise);
            Torso(BoneId.Head, 18f, 0, 0);
            Limb(BoneId.LeftUpperArm, -20f, 62f); Elbow(BoneId.LeftLowerArm, 30f);
            Limb(BoneId.RightUpperArm, -26f, 58f); Elbow(BoneId.RightLowerArm, 36f);
        }

        // ---------------- titan variants: one exaggerated silhouette per state ----------------

        /// <summary>Both legs bent deep, arms out wide and level for balance, chest forward, eyes up.</summary>
        void TitanLand(float u)
        {
            float settle = 0.85f + 0.15f * Mathf.Exp(-u * 3f);
            Hips(0, -0.33f * settle, -0.02f, 0, 0, 0);
            Limb(BoneId.LeftUpperLeg, 85f * settle, 22f); Knee(BoneId.LeftLowerLeg, 112f * settle); Foot(BoneId.LeftFoot, -28f * settle);
            Limb(BoneId.RightUpperLeg, 85f * settle, 22f); Knee(BoneId.RightLowerLeg, 112f * settle); Foot(BoneId.RightFoot, -28f * settle);
            Torso(BoneId.Spine, 10f, 0, 0);
            Torso(BoneId.Chest, 18f, 0, 0);
            Torso(BoneId.Head, -24f, 0, 0);
            Limb(BoneId.LeftUpperArm, 6f, 88f); Elbow(BoneId.LeftLowerArm, 8f);
            Limb(BoneId.RightUpperArm, 6f, 88f); Elbow(BoneId.RightLowerArm, 8f);
        }

        /// <summary>Leaning far back off balance, arms thrown up overhead, one leg out front to catch itself.</summary>
        void TitanStagger(float u)
        {
            float wob = Mathf.Sin(u * 6f) * Mathf.Exp(-u * 0.5f);
            Hips(-32f, -0.05f, -0.08f, 0, 0, 5f * wob);
            Torso(BoneId.Spine, -12f, 3f * wob, 0);
            Torso(BoneId.Chest, -16f, 4f * wob, -3f * wob);
            Torso(BoneId.Head, -22f, 0, 4f * wob);
            Limb(BoneId.LeftUpperArm, 125f + 6f * wob, 55f); Elbow(BoneId.LeftLowerArm, 18f);
            Limb(BoneId.RightUpperArm, 118f - 6f * wob, 62f); Elbow(BoneId.RightLowerArm, 24f);
            Limb(BoneId.LeftUpperLeg, 42f, 10f); Knee(BoneId.LeftLowerLeg, 4f); Foot(BoneId.LeftFoot, -30f);
            Limb(BoneId.RightUpperLeg, -12f, 14f); Knee(BoneId.RightLowerLeg, 32f); Foot(BoneId.RightFoot, 20f);
        }

        /// <summary>One knee on the ground, back arched forward over it, head hanging: the nape is up and open.</summary>
        void TitanKneel(float u)
        {
            float breath = Mathf.Sin(u * 3.5f);
            float drop = rig.props.upperLeg - 0.02f;
            Hips(24f, -drop, 0.02f, 0, 0, 0);
            // right knee on the ground: thigh vertical in world (cancels the hips pitch), shin folded back flat
            Limb(BoneId.RightUpperLeg, 20f, 8f); Knee(BoneId.RightLowerLeg, 110f); Foot(BoneId.RightFoot, 30f);
            // left leg planted in front, shin vertical
            Limb(BoneId.LeftUpperLeg, 110f, 14f); Knee(BoneId.LeftLowerLeg, 88f); Foot(BoneId.LeftFoot, -10f);
            // back arched over the front knee, head hanging: the nape is the highest point of the spine
            Torso(BoneId.Spine, 22f, 0, 0);
            Torso(BoneId.Chest, 30f + 3f * breath, 0, 0);
            Torso(BoneId.Neck, 26f, 0, 0);
            Torso(BoneId.Head, 44f, 0, 0);
            Limb(BoneId.RightUpperArm, 78f, 14f); Elbow(BoneId.RightLowerArm, 2f);
            Limb(BoneId.LeftUpperArm, 72f, 16f); Elbow(BoneId.LeftLowerArm, 4f);
        }

        /// <summary>Arm held level and swept from far behind to across the body while the torso twists after it.</summary>
        void TitanSwipe(float u)
        {
            float p = Mathf.Repeat(u, 1.4f);
            float s = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.40f, p));
            float back = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.1f, 1.4f, p));
            s *= back;
            // out 90 = level with the shoulder; twist yaws the level arm: +behind, -across the front
            Limb(BoneId.RightUpperArm, 0f, 96f, Mathf.Lerp(70f, -105f, s));
            Elbow(BoneId.RightLowerArm, Mathf.Lerp(30f, 2f, s));
            Limb(BoneId.LeftUpperArm, -50f, 34f); Elbow(BoneId.LeftLowerArm, 30f);
            float twist = Mathf.Lerp(45f, -65f, s);
            Hips(8f, -0.05f, 0, 0, twist * 0.35f, 0);
            Torso(BoneId.Spine, 6f, twist * 0.35f, 0);
            Torso(BoneId.Chest, 10f, twist * 0.5f, Mathf.Lerp(6f, -12f, s));
            Torso(BoneId.Head, -4f, -twist * 0.3f, 0);
            Limb(BoneId.LeftUpperLeg, 28f, 24f); Knee(BoneId.LeftLowerLeg, 22f);
            Limb(BoneId.RightUpperLeg, -22f, 24f); Knee(BoneId.RightLowerLeg, 10f); Foot(BoneId.RightFoot, 15f);
        }

        /// <summary>One arm fully extended forward and low, the other swung back, torso leaning into the reach.</summary>
        void TitanGrab(float u)
        {
            float reach = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.35f));
            Limb(BoneId.RightUpperArm, Mathf.Lerp(30f, 58f, reach), 4f); Elbow(BoneId.RightLowerArm, Mathf.Lerp(30f, 0f, reach)); Hand(BoneId.RightHand, -25f);
            Limb(BoneId.LeftUpperArm, -60f, 22f); Elbow(BoneId.LeftLowerArm, 35f);
            Hips(20f, -0.10f, 0.04f, 0, -12f, 0);
            Torso(BoneId.Spine, 14f, -6f, 0);
            Torso(BoneId.Chest, 24f * reach + 4f, -8f, 6f);
            Torso(BoneId.Head, -14f, 10f, 0);
            Limb(BoneId.LeftUpperLeg, 48f, 8f); Knee(BoneId.LeftLowerLeg, 48f);
            Limb(BoneId.RightUpperLeg, -32f, 8f); Knee(BoneId.RightLowerLeg, 6f); Foot(BoneId.RightFoot, 28f);
        }

        /// <summary>Knee hauled up to the chest, torso upright, arms wide; then the slam.</summary>
        void TitanStomp(float u)
        {
            float p = Mathf.Repeat(u, 1.6f);
            float lift = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.35f, p));
            float slam = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.9f, 1.05f, p));
            float raise = lift * (1f - slam);
            Limb(BoneId.RightUpperLeg, Mathf.Lerp(0f, 105f, raise), 24f * raise + 6f); Knee(BoneId.RightLowerLeg, Mathf.Lerp(4f, 115f, raise)); Foot(BoneId.RightFoot, -20f * raise);
            Limb(BoneId.LeftUpperLeg, -3f, 6f); Knee(BoneId.LeftLowerLeg, 4f);
            float lean = Mathf.Lerp(-8f, -4f, raise) + 26f * slam;
            Hips(lean * 0.4f, -0.02f - 0.06f * slam, 0, -0.02f * raise, 0, -8f * raise);
            Torso(BoneId.Spine, lean * 0.3f, 0, 0);
            Torso(BoneId.Chest, lean * 0.3f, 0, 5f * raise);
            Torso(BoneId.Head, 6f + 18f * slam, 0, 0);
            Limb(BoneId.LeftUpperArm, Mathf.Lerp(-10f, -55f, raise), 30f); Elbow(BoneId.LeftLowerArm, 20f);
            Limb(BoneId.RightUpperArm, Mathf.Lerp(-14f, -60f, raise), 26f); Elbow(BoneId.RightLowerArm, 24f);
        }

        // ---------------- helpers ----------------

        static float SideSign(BoneId id)
        {
            switch (id)
            {
                case BoneId.LeftUpperArm: case BoneId.LeftLowerArm: case BoneId.LeftHand:
                case BoneId.LeftUpperLeg: case BoneId.LeftLowerLeg: case BoneId.LeftFoot:
                    return -1f;
                default: return 1f;
            }
        }

        void Limb(BoneId id, float fwd, float outward, float twist = 0f)
        {
            target[(int)id] = Quaternion.Euler(-fwd, twist * SideSign(id), outward * SideSign(id));
        }

        void Knee(BoneId id, float bend) => target[(int)id] = Quaternion.Euler(bend, 0, 0);
        void Elbow(BoneId id, float bend) => target[(int)id] = Quaternion.Euler(-bend, 0, 0);
        void Hand(BoneId id, float curl) => target[(int)id] = Quaternion.Euler(-curl, 0, 0);
        void Foot(BoneId id, float pitch) => target[(int)id] = Quaternion.Euler(pitch, 0, 0);
        void Torso(BoneId id, float pitch, float yaw, float roll) => target[(int)id] = Quaternion.Euler(pitch, yaw, roll);

        void Hips(float pitch, float dy, float dz, float dx, float yaw, float roll)
        {
            target[(int)BoneId.Hips] = Quaternion.Euler(pitch, yaw, roll);
            float H = rig.height;
            hipsTarget = hipsBind + new Vector3(dx * H, dy * H, dz * H);
        }
    }
}
