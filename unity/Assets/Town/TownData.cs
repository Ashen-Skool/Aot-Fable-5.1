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
        public int shade;            // 0 dark, 1 mid, 2 light: per-building tint variation
        public Vector2 uvOffset;     // per-building texture shift

        public float WallTop => baseH + storeys * storeyH;
        public float Yaw => facing == Facing.NegZ ? 0f : facing == Facing.PosZ ? 180f : facing == Facing.PosX ? -90f : 90f;
        public Vector3 Front => facing == Facing.NegZ ? Vector3.back : facing == Facing.PosZ ? Vector3.forward : facing == Facing.PosX ? Vector3.right : Vector3.left;
        public float Overhang => 0.6f;
        public float GableOverhang => 0.35f;
        public float Rise => (gableFront ? w * 0.5f : d * 0.5f) * Mathf.Tan(pitch * Mathf.Deg2Rad) + Overhang * Mathf.Tan(pitch * Mathf.Deg2Rad);
        public float RidgeY => WallTop + Rise;
        /// <summary>About a quarter of the houses have lamps lit behind the glass at this hour (deterministic per house).</summary>
        public bool LitWindows => ((int)(Mathf.Abs(uvOffset.x * 7.31f + uvOffset.y * 3.17f) * 1000f) % 4) == 0;
        /// <summary>Roof access hatch onto the tiles: about half the houses have one (deterministic per house).</summary>
        public bool Hatch => ((int)(Mathf.Abs(uvOffset.x * 5.13f + uvOffset.y * 9.7f) * 1000f) % 2) == 0;
        /// <summary>How far up the front slope the hatch sits, 0 at the eave, 1 at the ridge.</summary>
        public float HatchT => 0.4f + 0.02f * (((int)(Mathf.Abs(uvOffset.y * 39f) * 100f) % 11));
        /// <summary>Where along the ridge the hatch sits, as a fraction of the roof's half width.</summary>
        public float HatchOff => ((((int)(Mathf.Abs(uvOffset.x * 77f) * 100f) % 21)) / 20f - 0.5f) * 1.1f;
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
        public List<Vector3> chimneys = new List<Vector3>(512);   // chimney pot tops, world space (smoke)
        public int hookLayer;
        public int houseCount;
        public Rect square;
        public float wallHeight, wallZ;
        public GameObject root;
        /// <summary>The crusher that owns every house's vertex span; also in Ctx as "town.destruction".</summary>
        public TownDestruction destruction;
    }
}
