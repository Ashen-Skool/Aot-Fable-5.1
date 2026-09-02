using UnityEngine;
using Shared;
using Shared.Rigs;
using Pose = Shared.Rigs.Pose;

namespace Proxies
{
    /// <summary>
    /// Mikasa stand-in: a 1.7 m HumanoidProxy dressed in the Survey Corps colors, with the
    /// sockets that ODM, Combat and the scarf attach to. Sockets are empty transforms on the
    /// bone they belong to; the final rig carries the same names.
    /// </summary>
    public class MikasaProxy : MonoBehaviour
    {
        public const float Height = 1.70f;
        public static readonly string[] SocketNames =
            { "Socket_HookL", "Socket_HookR", "Socket_BladeL", "Socket_BladeR", "Socket_Scarf" };

        public HumanoidProxy rig;
        public CapsuleCollider body;
        public Transform[] sockets = new Transform[5];

        public IPoser Poser => rig;
        public Transform Socket(string name)
        {
            for (int i = 0; i < SocketNames.Length; i++) if (SocketNames[i] == name) return sockets[i];
            return null;
        }

        public static MikasaProxy Build(string name, Vector3 pos, float yaw = 0f)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var m = go.AddComponent<MikasaProxy>();
            var pal = new Palette
            {
                skin = Mats.Lit(new Color(0.92f, 0.78f, 0.68f), 0.2f),
                torso = Mats.Lit(new Color(0.60f, 0.42f, 0.24f), 0.25f),    // corps jacket
                pelvis = Mats.Lit(new Color(0.86f, 0.84f, 0.78f), 0.15f),   // white trousers
                arms = Mats.Lit(new Color(0.60f, 0.42f, 0.24f), 0.25f),
                feet = Mats.Lit(new Color(0.24f, 0.16f, 0.10f), 0.4f),      // boots
                hair = Mats.Lit(new Color(0.07f, 0.07f, 0.09f), 0.35f),
            };
            m.rig = HumanoidProxy.Build(go, Height, Proportions.Human(), pal);
            m.Dress(pal);
            m.AddSockets();
            m.body = go.AddComponent<CapsuleCollider>();
            m.body.center = new Vector3(0, Height * 0.5f, 0);
            m.body.height = Height;
            m.body.radius = 0.28f;
            return m;
        }

        void Dress(Palette pal)
        {
            float H = Height;
            var p = rig.props;
            var head = rig.Bone(BoneId.Head);
            float headH = (1f - (p.hipsY + p.spineRise + p.spine + p.chest + p.neck)) * H;
            // bob haircut: cap over the top and back, fringe at the front
            HumanoidProxy.Geo(head, PrimitiveType.Sphere, "Geo_Hair", new Vector3(0, headH * 0.6f, -p.headR * H * 0.15f),
                new Vector3(p.headR * H * 2.0f, headH * 1.0f, p.headR * H * 2.05f), pal.hair);
            HumanoidProxy.Geo(head, PrimitiveType.Cube, "Geo_Fringe", new Vector3(0, headH * 0.86f, p.headR * H * 0.55f),
                new Vector3(p.headR * H * 1.5f, headH * 0.14f, p.headR * H * 0.6f), pal.hair);
            // scarf: a red ring at the neck with a tail down the back
            var scarf = Mats.Lit(new Color(0.72f, 0.10f, 0.08f), 0.2f);
            var neck = rig.Bone(BoneId.Neck);
            HumanoidProxy.Geo(neck, PrimitiveType.Capsule, "Geo_Scarf", new Vector3(0, 0.005f * H, 0),
                new Vector3(p.neckR * H * 4.2f, p.neck * H * 0.6f, p.neckR * H * 4.2f), scarf);
            HumanoidProxy.Geo(neck, PrimitiveType.Cube, "Geo_ScarfTail", new Vector3(0, -0.06f * H, -p.torsoD * H * 0.55f),
                new Vector3(0.05f * H, 0.14f * H, 0.012f * H), scarf);
            // jacket over a shirt: a lighter shirt panel down the chest, harness straps
            var shirt = Mats.Lit(new Color(0.88f, 0.86f, 0.80f), 0.15f);
            var chest = rig.Bone(BoneId.Chest);
            HumanoidProxy.Geo(chest, PrimitiveType.Cube, "Geo_Shirt", new Vector3(0, p.chest * H * 0.5f, p.torsoD * H * 0.5f),
                new Vector3(p.torsoW * H * 0.35f, p.chest * H * 0.9f, 0.004f * H), shirt);
            // ODM gear: the gas cylinders and hook launchers on both hips
            var gear = Mats.Lit(new Color(0.34f, 0.34f, 0.36f), 0.5f, 0.6f);
            var hips = rig.Bone(BoneId.Hips);
            for (int s = -1; s <= 1; s += 2)
            {
                HumanoidProxy.Geo(hips, PrimitiveType.Cube, s < 0 ? "Geo_GearL" : "Geo_GearR",
                    new Vector3(s * (p.pelvisW * H * 0.5f + 0.035f * H), -0.01f * H, -0.01f * H),
                    new Vector3(0.06f * H, 0.10f * H, 0.14f * H), gear);
                var cyl = HumanoidProxy.Geo(hips, PrimitiveType.Cylinder, s < 0 ? "Geo_CylinderL" : "Geo_CylinderR",
                    new Vector3(s * (p.pelvisW * H * 0.5f + 0.035f * H), -0.06f * H, -0.02f * H),
                    new Vector3(0.05f * H, 0.11f * H, 0.05f * H), gear);
                cyl.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            }
            // blades: one metre of steel out of each hand, along the forearm line
            var steel = Mats.Lit(new Color(0.80f, 0.83f, 0.86f), 0.85f, 0.9f);
            var grip = Mats.Lit(new Color(0.12f, 0.12f, 0.13f), 0.4f);
            foreach (var id in new[] { BoneId.LeftHand, BoneId.RightHand })
            {
                var hand = rig.Bone(id);
                HumanoidProxy.Geo(hand, PrimitiveType.Cube, "Geo_Grip", new Vector3(0, -p.hand * H * 0.5f, 0.02f * H),
                    new Vector3(0.02f * H, p.hand * H * 1.2f, 0.02f * H), grip);
                HumanoidProxy.Geo(hand, PrimitiveType.Cube, "Geo_Blade", new Vector3(0, -p.hand * H * 0.8f - 0.30f, 0.02f * H),
                    new Vector3(0.006f * H, 0.60f, 0.022f * H), steel);
            }
        }

        void AddSockets()
        {
            float H = Height;
            var p = rig.props;
            sockets[0] = Socket(0, rig.Bone(BoneId.Hips), new Vector3(-(p.pelvisW * H * 0.5f + 0.035f * H), -0.02f * H, 0.05f * H));
            sockets[1] = Socket(1, rig.Bone(BoneId.Hips), new Vector3(p.pelvisW * H * 0.5f + 0.035f * H, -0.02f * H, 0.05f * H));
            sockets[2] = Socket(2, rig.Bone(BoneId.LeftHand), new Vector3(0, -p.hand * H * 0.6f, 0.02f * H));
            sockets[3] = Socket(3, rig.Bone(BoneId.RightHand), new Vector3(0, -p.hand * H * 0.6f, 0.02f * H));
            sockets[4] = Socket(4, rig.Bone(BoneId.Neck), new Vector3(0, 0, -p.neckR * H));
        }

        Transform Socket(int i, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(SocketNames[i]);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }
    }
}
