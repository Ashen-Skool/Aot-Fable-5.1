using UnityEngine;
using Shared;
using Shared.Rigs;
using Pose = Shared.Rigs.Pose;

namespace Proxies
{
    /// <summary>
    /// Titan stand-in: a HumanoidProxy with titan proportions plus the zone colliders that
    /// Combat and AI talk to. Zones are trigger colliders parented to the bone they belong
    /// to, so they follow every pose. 7 m = the street titans, 15 m = the Abnormal boss.
    /// </summary>
    public class TitanProxy : MonoBehaviour
    {
        public const float SmallHeight = 7f, BossHeight = 15f;
        public static readonly string[] ZoneNames =
            { "Zone_Nape", "Zone_HamstringL", "Zone_HamstringR", "Zone_ArmL", "Zone_ArmR", "Zone_Eyes" };

        public float height;
        public bool isBoss;
        public HumanoidProxy rig;
        public CapsuleCollider body;
        public Collider[] zones = new Collider[6];

        public IPoser Poser => rig;
        public Collider Zone(string name)
        {
            for (int i = 0; i < ZoneNames.Length; i++) if (ZoneNames[i] == name) return zones[i];
            return null;
        }

        public static TitanProxy Build(string name, float height, Vector3 pos, float yaw = 0f)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var t = go.AddComponent<TitanProxy>();
            t.height = height;
            t.isBoss = height > 10f;
            var props = t.isBoss ? Proportions.Boss() : Proportions.Titan();
            var skin = t.isBoss ? new Color(0.70f, 0.52f, 0.44f) : new Color(0.82f, 0.64f, 0.54f);
            var pal = new Palette
            {
                skin = Mats.Lit(skin, 0.18f),
                hair = Mats.Lit(t.isBoss ? new Color(0.10f, 0.08f, 0.07f) : new Color(0.28f, 0.18f, 0.12f), 0.3f),
            };
            t.rig = HumanoidProxy.Build(go, height, props, pal);
            t.Dress(pal);
            t.AddZones();
            float H = height;
            t.body = go.AddComponent<CapsuleCollider>();
            t.body.center = new Vector3(0, H * 0.5f, 0);
            t.body.height = H;
            t.body.radius = H * props.torsoW * 0.6f;
            return t;
        }

        void Dress(Palette pal)
        {
            float H = height;
            var p = rig.props;
            var head = rig.Bone(BoneId.Head);
            float headH = (1f - (p.hipsY + p.spineRise + p.spine + p.chest + p.neck)) * H;
            // hair cap
            HumanoidProxy.Geo(head, PrimitiveType.Sphere, "Geo_Hair", new Vector3(0, headH * 0.62f, -p.headR * H * 0.18f),
                new Vector3(p.headR * H * 1.9f, headH * 0.85f, p.headR * H * 1.95f), pal.hair);
            // eyes: two dark spheres, wide and unblinking
            var eye = Mats.Lit(new Color(0.08f, 0.08f, 0.09f), 0.7f);
            float ex = p.headR * H * 0.42f, ey = headH * 0.55f, ez = p.headR * H * 0.86f;
            float er = isBoss ? H * 0.014f : H * 0.02f;
            HumanoidProxy.Geo(head, PrimitiveType.Sphere, "Geo_EyeL", new Vector3(-ex, ey, ez), Vector3.one * er * 2f, eye);
            HumanoidProxy.Geo(head, PrimitiveType.Sphere, "Geo_EyeR", new Vector3(ex, ey, ez), Vector3.one * er * 2f, eye);
            // mouth: a wide dark slab (the titan grin)
            HumanoidProxy.Geo(head, PrimitiveType.Cube, "Geo_Mouth", new Vector3(0, headH * 0.22f, p.headR * H * 0.9f),
                new Vector3(p.headR * H * (isBoss ? 1.3f : 1.1f), H * 0.012f, H * 0.01f), eye);
            // the nape: a visible darker patch on the back of the neck, 1 m x 10 cm at 15 m scale
            var neck = rig.Bone(BoneId.Neck);
            var nape = Mats.Lit(new Color(0.55f, 0.22f, 0.18f), 0.15f);
            HumanoidProxy.Geo(neck, PrimitiveType.Cube, "Geo_NapeMark", new Vector3(0, p.neck * H * 0.3f, -p.neckR * H * 0.95f),
                new Vector3(H * 0.05f, H * 0.065f, H * 0.008f), nape);
            if (isBoss)
            {
                // the Abnormal: exposed muscle bands on the chest, no skin over the jaw
                var muscle = Mats.Lit(new Color(0.55f, 0.28f, 0.24f), 0.3f);
                var chest = rig.Bone(BoneId.Chest);
                HumanoidProxy.Geo(chest, PrimitiveType.Cube, "Geo_Pecs", new Vector3(0, p.chest * H * 0.62f, p.torsoD * H * 0.5f),
                    new Vector3(p.torsoW * H * 0.9f, p.chest * H * 0.35f, H * 0.012f), muscle);
                HumanoidProxy.Geo(head, PrimitiveType.Cube, "Geo_Jaw", new Vector3(0, headH * 0.16f, p.headR * H * 0.55f),
                    new Vector3(p.headR * H * 1.5f, headH * 0.22f, p.headR * H * 0.9f), muscle);
            }
        }

        void AddZones()
        {
            float H = height;
            var p = rig.props;
            // nape: back of the neck, spanning neck + top of the chest
            var nape = ZoneBox(0, rig.Bone(BoneId.Neck), new Vector3(0, p.neck * H * 0.3f, -p.neckR * H * 0.9f),
                new Vector3(H * 0.06f, H * 0.07f, H * 0.03f));
            // hamstrings: back of each thigh just above the knee
            ZoneBox(1, rig.Bone(BoneId.LeftUpperLeg), new Vector3(0, -p.upperLeg * H * 0.72f, -p.thighR * H * 0.8f),
                new Vector3(p.thighR * H * 1.6f, p.upperLeg * H * 0.4f, p.thighR * H * 0.8f));
            ZoneBox(2, rig.Bone(BoneId.RightUpperLeg), new Vector3(0, -p.upperLeg * H * 0.72f, -p.thighR * H * 0.8f),
                new Vector3(p.thighR * H * 1.6f, p.upperLeg * H * 0.4f, p.thighR * H * 0.8f));
            // arms: whole upper arm
            ZoneCapsule(3, rig.Bone(BoneId.LeftUpperArm), p.upperArm * H, p.upperArmR * H * 1.3f);
            ZoneCapsule(4, rig.Bone(BoneId.RightUpperArm), p.upperArm * H, p.upperArmR * H * 1.3f);
            // eyes: a band across the face
            float headH = (1f - (p.hipsY + p.spineRise + p.spine + p.chest + p.neck)) * H;
            ZoneBox(5, rig.Bone(BoneId.Head), new Vector3(0, headH * 0.55f, p.headR * H * 0.75f),
                new Vector3(p.headR * H * 1.5f, H * 0.04f, H * 0.03f));
        }

        Collider ZoneBox(int i, Transform parent, Vector3 center, Vector3 size)
        {
            var go = new GameObject(ZoneNames[i]);
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<BoxCollider>();
            c.isTrigger = true;
            c.center = center;
            c.size = size;
            zones[i] = c;
            return c;
        }

        Collider ZoneCapsule(int i, Transform parent, float len, float r)
        {
            var go = new GameObject(ZoneNames[i]);
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<CapsuleCollider>();
            c.isTrigger = true;
            c.direction = 1;
            c.center = new Vector3(0, -len * 0.5f, 0);
            c.height = len;
            c.radius = r;
            zones[i] = c;
            return c;
        }
    }
}
