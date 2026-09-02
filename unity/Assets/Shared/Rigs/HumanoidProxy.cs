using UnityEngine;

namespace Shared.Rigs
{
    public enum BoneId
    {
        Hips, Spine, Chest, Neck, Head,
        LeftUpperArm, LeftLowerArm, LeftHand,
        RightUpperArm, RightLowerArm, RightHand,
        LeftUpperLeg, LeftLowerLeg, LeftFoot,
        RightUpperLeg, RightLowerLeg, RightFoot,
    }

    /// <summary>
    /// A Unity-Humanoid-compatible skeleton built from primitives, scaled by height.
    /// Bone transforms are empty joints (bind pose = identity rotation, character faces +Z,
    /// limbs hang along -Y); each joint carries a "Geo_*" primitive child for the visual.
    /// The final rigs carry the same bone names, so anything that finds a bone by name,
    /// parents a socket to it, or drives it through IPoser keeps working after the swap.
    /// </summary>
    public class HumanoidProxy : MonoBehaviour, IPoser
    {
        public const int BoneCount = 17;
        public static readonly string[] BoneNames =
        {
            "Hips", "Spine", "Chest", "Neck", "Head",
            "LeftUpperArm", "LeftLowerArm", "LeftHand",
            "RightUpperArm", "RightLowerArm", "RightHand",
            "LeftUpperLeg", "LeftLowerLeg", "LeftFoot",
            "RightUpperLeg", "RightLowerLeg", "RightFoot",
        };

        public float height = 1.7f;
        public Proportions props;
        public Palette palette;
        public bool autoTick = true;

        readonly Transform[] bones = new Transform[BoneCount];
        readonly Vector3[] bindPos = new Vector3[BoneCount];
        ProceduralPoser poser;

        public Transform Bone(BoneId id) => bones[(int)id];
        public Transform Bone(string name)
        {
            for (int i = 0; i < BoneCount; i++) if (BoneNames[i] == name) return bones[i];
            return null;
        }
        public Vector3 BindLocalPosition(BoneId id) => bindPos[(int)id];
        public ProceduralPoser Poser => poser;

        // ---- IPoser (forwarded) ----
        public Pose Current => poser.Current;
        public float Phase { get => poser.Phase; set => poser.Phase = value; }
        public float Speed { get => poser.Speed; set => poser.Speed = value; }
        public bool Paused { get => poser.Paused; set => poser.Paused = value; }
        public void SetPose(Pose pose) => poser.SetPose(pose);
        public void Snap(Pose pose, float phase) => poser.Snap(pose, phase);
        public void Tick(float dt) => poser.Tick(dt);

        void Update()
        {
            if (autoTick && poser != null) poser.Tick(Time.deltaTime);
        }

        /// <summary>Build the skeleton and visuals under root. Works in edit mode and play mode.</summary>
        public static HumanoidProxy Build(GameObject root, float height, Proportions props, Palette palette)
        {
            var hp = root.GetComponent<HumanoidProxy>() ?? root.AddComponent<HumanoidProxy>();
            hp.height = height;
            hp.props = props ?? Proportions.Human();
            hp.palette = palette ?? Palette.Solid(new Color(0.7f, 0.7f, 0.7f));
            hp.props.tempo = Mathf.Sqrt(1.7f / Mathf.Max(0.1f, height));
            hp.Construct();
            hp.poser = new ProceduralPoser(hp);
            hp.poser.Snap(Pose.Idle, 0f);
            return hp;
        }

        void Construct()
        {
            float H = height;
            var p = props;
            var pal = palette;

            var hips = Joint(BoneId.Hips, transform, new Vector3(0, p.hipsY * H, 0));
            var spine = Joint(BoneId.Spine, hips, new Vector3(0, p.spineRise * H, 0));
            var chest = Joint(BoneId.Chest, spine, new Vector3(0, p.spine * H, 0));
            var neck = Joint(BoneId.Neck, chest, new Vector3(0, p.chest * H, 0));
            var head = Joint(BoneId.Head, neck, new Vector3(0, p.neck * H, 0));

            float shoulderY = p.chest * H - p.upperArmR * H;
            var lua = Joint(BoneId.LeftUpperArm, chest, new Vector3(-p.shoulderHalf * H, shoulderY, 0));
            var lla = Joint(BoneId.LeftLowerArm, lua, new Vector3(0, -p.upperArm * H, 0));
            var lh = Joint(BoneId.LeftHand, lla, new Vector3(0, -p.lowerArm * H, 0));
            var rua = Joint(BoneId.RightUpperArm, chest, new Vector3(p.shoulderHalf * H, shoulderY, 0));
            var rla = Joint(BoneId.RightLowerArm, rua, new Vector3(0, -p.upperArm * H, 0));
            var rh = Joint(BoneId.RightHand, rla, new Vector3(0, -p.lowerArm * H, 0));

            float legTop = -0.01f * H;
            var lul = Joint(BoneId.LeftUpperLeg, hips, new Vector3(-p.hipHalf * H, legTop, 0));
            var lll = Joint(BoneId.LeftLowerLeg, lul, new Vector3(0, -p.upperLeg * H, 0));
            var lf = Joint(BoneId.LeftFoot, lll, new Vector3(0, -p.lowerLeg * H, 0));
            var rul = Joint(BoneId.RightUpperLeg, hips, new Vector3(p.hipHalf * H, legTop, 0));
            var rll = Joint(BoneId.RightLowerLeg, rul, new Vector3(0, -p.upperLeg * H, 0));
            var rf = Joint(BoneId.RightFoot, rll, new Vector3(0, -p.lowerLeg * H, 0));

            // ---- visuals ----
            // pelvis: a rounded block
            Geo(hips, PrimitiveType.Capsule, "Geo_Pelvis", new Vector3(0, 0.01f * H, 0),
                new Vector3(p.pelvisW * H, 0.06f * H, p.torsoD * H * 0.95f), pal.Pelvis);
            Geo(spine, PrimitiveType.Cube, "Geo_Spine", new Vector3(0, p.spine * H * 0.5f, 0),
                new Vector3(p.torsoW * H * 0.85f, p.spine * H + 0.02f * H, p.torsoD * H * 0.9f), pal.Torso);
            if (p.belly > 0f)
                Geo(spine, PrimitiveType.Sphere, "Geo_Belly", new Vector3(0, p.spine * H * 0.55f, p.torsoD * H * 0.15f),
                    new Vector3(p.torsoW * H * 1.05f, p.belly * H * 2f, p.torsoD * H * 1.35f), pal.Torso);
            Geo(chest, PrimitiveType.Cube, "Geo_Chest", new Vector3(0, p.chest * H * 0.5f, 0),
                new Vector3(p.torsoW * H, p.chest * H, p.torsoD * H), pal.Torso);
            // shoulder caps
            Geo(chest, PrimitiveType.Sphere, "Geo_ShoulderL", new Vector3(-p.shoulderHalf * H, shoulderY, 0),
                Vector3.one * p.upperArmR * H * 2.6f, pal.Arms);
            Geo(chest, PrimitiveType.Sphere, "Geo_ShoulderR", new Vector3(p.shoulderHalf * H, shoulderY, 0),
                Vector3.one * p.upperArmR * H * 2.6f, pal.Arms);
            Limb(neck, "Geo_Neck", p.neck * H + p.neckR * H, p.neckR * H, pal.skin);
            float headH = (1f - (p.hipsY + p.spineRise + p.spine + p.chest + p.neck)) * H;
            Geo(head, PrimitiveType.Sphere, "Geo_Head", new Vector3(0, headH * 0.5f, 0),
                new Vector3(p.headR * H * 1.8f, headH, p.headR * H * 1.9f), pal.Head);

            Limb(lua, "Geo_UpperArm", p.upperArm * H, p.upperArmR * H, pal.Arms);
            Limb(rua, "Geo_UpperArm", p.upperArm * H, p.upperArmR * H, pal.Arms);
            Limb(lla, "Geo_LowerArm", p.lowerArm * H, p.lowerArmR * H, pal.Arms);
            Limb(rla, "Geo_LowerArm", p.lowerArm * H, p.lowerArmR * H, pal.Arms);
            Geo(lh, PrimitiveType.Capsule, "Geo_Hand", new Vector3(0, -p.hand * H * 0.45f, 0),
                new Vector3(p.lowerArmR * H * 2.2f, p.hand * H * 0.5f, p.lowerArmR * H * 1.2f), pal.Hands);
            Geo(rh, PrimitiveType.Capsule, "Geo_Hand", new Vector3(0, -p.hand * H * 0.45f, 0),
                new Vector3(p.lowerArmR * H * 2.2f, p.hand * H * 0.5f, p.lowerArmR * H * 1.2f), pal.Hands);

            Limb(lul, "Geo_UpperLeg", p.upperLeg * H, p.thighR * H, pal.Legs);
            Limb(rul, "Geo_UpperLeg", p.upperLeg * H, p.thighR * H, pal.Legs);
            Limb(lll, "Geo_LowerLeg", p.lowerLeg * H + p.ankle * H * 0.5f, p.shinR * H, pal.Legs);
            Limb(rll, "Geo_LowerLeg", p.lowerLeg * H + p.ankle * H * 0.5f, p.shinR * H, pal.Legs);
            Geo(lf, PrimitiveType.Cube, "Geo_Foot", new Vector3(0, -p.ankle * H * 0.5f, p.footLen * H * 0.25f),
                new Vector3(p.footW * H, p.ankle * H, p.footLen * H), pal.Feet);
            Geo(rf, PrimitiveType.Cube, "Geo_Foot", new Vector3(0, -p.ankle * H * 0.5f, p.footLen * H * 0.25f),
                new Vector3(p.footW * H, p.ankle * H, p.footLen * H), pal.Feet);
        }

        Transform Joint(BoneId id, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(BoneNames[(int)id]);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localRotation = Quaternion.identity;
            bones[(int)id] = t;
            bindPos[(int)id] = localPos;
            return t;
        }

        /// <summary>A capsule hanging along -Y from the joint: length len, radius r.</summary>
        public static GameObject Limb(Transform joint, string name, float len, float r, Material mat)
        {
            return Geo(joint, PrimitiveType.Capsule, name, new Vector3(0, -len * 0.5f, 0),
                new Vector3(r * 2f, len * 0.5f, r * 2f), mat);
        }

        /// <summary>A primitive child without its collider (colliders are zones, added on purpose).</summary>
        public static GameObject Geo(Transform parent, PrimitiveType type, string name, Vector3 localPos, Vector3 localScale, Material mat)
        {
            var g = GameObject.CreatePrimitive(type);
            g.name = name;
            var c = g.GetComponent<Collider>();
            if (c != null) Kill(c);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.transform.localRotation = Quaternion.identity;
            g.transform.localScale = localScale;
            if (mat != null) g.GetComponent<Renderer>().sharedMaterial = mat;
            return g;
        }

        public static void Kill(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }

        /// <summary>Find a descendant by exact name (depth-first). Null if absent.</summary>
        public static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindDeep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
