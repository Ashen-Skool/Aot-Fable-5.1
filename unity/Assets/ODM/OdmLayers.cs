using UnityEngine;

namespace ODM
{
    /// <summary>Layer names/indices the hooks raycast against. Names are ensured by the editor script.</summary>
    public static class OdmLayers
    {
        public const string HookName = "HookTarget";
        public const string TitanName = "Titan";
        public const int HookIndex = 8;
        public const int TitanIndex = 9;

        public static int Hook
        {
            get { int l = LayerMask.NameToLayer(HookName); return l < 0 ? HookIndex : l; }
        }

        public static int Titan
        {
            get { int l = LayerMask.NameToLayer(TitanName); return l < 0 ? TitanIndex : l; }
        }

        /// <summary>Everything a hook may anchor to.</summary>
        public static int HookMask => (1 << Hook) | (1 << Titan);

        /// <summary>Everything the player can stand on (not titans).</summary>
        public static int GroundMask => (1 << 0) | (1 << Hook);
    }
}
