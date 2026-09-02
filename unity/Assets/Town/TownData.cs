using System.Collections.Generic;
using UnityEngine;

namespace Town
{
    /// <summary>Physics layer every hookable surface (houses, roofs, wall) lives on.</summary>
    public static class Layers
    {
        public const string HookTargetName = "HookTarget";
        public const int HookTargetFallback = 8;
        static int cached = -2;
        public static int HookTarget
        {
            get
            {
                if (cached == -2)
                {
                    cached = LayerMask.NameToLayer(HookTargetName);
                    if (cached < 0)
                    {
                        Debug.LogWarning("[Town] layer '" + HookTargetName + "' is not defined in TagManager; using " + HookTargetFallback);
                        cached = HookTargetFallback;
                    }
                }
                return cached;
            }
        }
    }

    public enum Facing { PosX, NegX, PosZ, NegZ }

    /// <summary>Everything the mesh builder needs to know about one house. Pure data, produced by TownLayout.</summary>
    public class HouseSpec
    {
        public Vector3 pos;          // footprint centre, y = 0
        public float w, d;           // width along the street, depth into the block
        public Facing facing;        // which world direction the front faces
        public int storeys;
        public float storeyH = 2.9f, baseH = 0.3f;
        public bool gableFront;      // ridge perpendicular to the street
        public float pitch = 45f;    // roof pitch in degrees
        public int wallStyle;        // 0 stone base + plaster, 1 all stone, 2 plaster + timber frame
        public int plasterTint, stoneSet, roofSet, shutterTint;
        public int chimneys;
        public float chimneyX0, chimneyX1, chimneyZ;
        public bool dormer, balcony, shutters;
        public float dormerX;
        public int doorSlot;

        public float WallTop => baseH + storeys * storeyH;
        public float Yaw => facing == Facing.NegZ ? 0f : facing == Facing.PosZ ? 180f : facing == Facing.PosX ? -90f : 90f;
        public Vector3 Front => facing == Facing.NegZ ? Vector3.back : facing == Facing.PosZ ? Vector3.forward : facing == Facing.PosX ? Vector3.right : Vector3.left;
        public float Overhang => 0.6f;
        public float GableOverhang => 0.35f;
        public float Rise => (gableFront ? w * 0.5f : d * 0.5f) * Mathf.Tan(pitch * Mathf.Deg2Rad) + Overhang * Mathf.Tan(pitch * Mathf.Deg2Rad);
        public float RidgeY => WallTop + Rise;
    }

    public enum PropKind { Fountain, Stall, Barrel, Crate, Cart, Lamp, Well, Clothesline, Sacks }

    public class PropSpec
    {
        public PropKind kind;
        public Vector3 pos, end;
        public float yaw, height, scale = 1f;
        public int variant;
    }

    /// <summary>What the town registers in Ctx under "town".</summary>
    public class TownInfo
    {
        public Bounds bounds;
        public Vector3 spawn, gate;
        public List<Vector3> rooftops = new List<Vector3>(1024);
        public int hookLayer;
        public int houseCount;
        public Rect square;
        public float wallHeight, wallZ;
        public GameObject root;
    }
}
