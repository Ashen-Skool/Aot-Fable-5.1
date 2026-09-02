using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Town
{
    /// <summary>
    /// Material palette for the town. Textured materials clone Resources/Town/Materials/LitNormal
    /// (URP Lit with the _NORMALMAP keyword baked into the asset so the variant ships in builds).
    /// Everything is cached so the whole district uses ~20 materials.
    /// </summary>
    public class TownMaterials
    {
        public static readonly string[] StoneSets = { "Bricks076A", "Bricks089", "Bricks100" };
        public static readonly float[] StoneTile = { 1.9f, 1.7f, 2.1f };
        public static readonly Color[] StoneTint = { new Color(0.86f, 0.82f, 0.74f), new Color(0.78f, 0.76f, 0.72f), new Color(0.9f, 0.86f, 0.76f) };
        public static readonly string[] RoofSets = { "RoofingTiles012A", "RoofingTiles013A" };
        public static readonly Color[] RoofTint = { new Color(0.48f, 0.3f, 0.23f), new Color(0.4f, 0.31f, 0.27f), new Color(0.45f, 0.37f, 0.3f), new Color(0.34f, 0.29f, 0.27f) };
        public static readonly Color[] PlasterTint = { new Color(0.93f, 0.88f, 0.78f), new Color(0.88f, 0.8f, 0.66f), new Color(0.9f, 0.84f, 0.74f), new Color(0.84f, 0.78f, 0.7f), new Color(0.92f, 0.86f, 0.7f) };
        public static readonly Color[] ShutterTint = { new Color(0.32f, 0.22f, 0.14f), new Color(0.25f, 0.35f, 0.28f), new Color(0.4f, 0.28f, 0.18f), new Color(0.3f, 0.3f, 0.36f) };
        public static readonly Color[] ClothTint = { new Color(0.86f, 0.82f, 0.72f), new Color(0.55f, 0.6f, 0.68f), new Color(0.6f, 0.4f, 0.3f), new Color(0.9f, 0.9f, 0.86f), new Color(0.45f, 0.5f, 0.4f), new Color(0.7f, 0.3f, 0.25f) };

        readonly Dictionary<string, Material> cache = new Dictionary<string, Material>(64);
        Material litNormalBase;

        Material LitNormalBase()
        {
            if (litNormalBase != null) return litNormalBase;
            litNormalBase = Resources.Load<Material>("Town/Materials/LitNormal");
            if (litNormalBase == null)
            {
                Debug.LogWarning("[Town] Resources/Town/Materials/LitNormal missing (run Town.Editor.TownSetup.Run); normal maps may be stripped in builds");
                litNormalBase = Mats.Lit(Color.white);
                litNormalBase.EnableKeyword("_NORMALMAP");
            }
            return litNormalBase;
        }

        static Texture2D Tex(string set, string map) => Resources.Load<Texture2D>("Town/Textures/" + set + "/" + map);

        public Material Textured(string key, string set, float tile, Color tint, float smooth = 0.15f, float bumpScale = 1f)
        {
            if (cache.TryGetValue(key, out var m)) return m;
            m = new Material(LitNormalBase()) { name = key };
            var col = Tex(set, "color");
            var nrm = Tex(set, "normal");
            if (col != null) { m.SetTexture("_BaseMap", col); m.mainTexture = col; }
            if (nrm != null) { m.SetTexture("_BumpMap", nrm); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_BumpScale", bumpScale);
            var scale = new Vector2(1f / tile, 1f / tile);
            m.SetTextureScale("_BaseMap", scale);
            m.SetTextureScale("_BumpMap", scale);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0f);
            cache[key] = m;
            return m;
        }

        public Material Plain(string key, Color c, float smooth = 0.2f, float metal = 0f)
        {
            if (cache.TryGetValue(key, out var m)) return m;
            m = Mats.Lit(c, smooth, metal);
            m.name = key;
            cache[key] = m;
            return m;
        }

        public static readonly float[] Shade = { 0.82f, 1.0f, 1.14f };
        public static readonly Color FogColor = new Color(0.86f, 0.75f, 0.6f);

        public Material Stone(int set, int shade = 1)
        {
            set = Mathf.Abs(set) % StoneSets.Length; shade = Mathf.Abs(shade) % Shade.Length;
            return Textured("stone" + set + "s" + shade, StoneSets[set], StoneTile[set], StoneTint[set] * Shade[shade], 0.1f, 1f);
        }
        public Material StoneDark => Textured("stonePlinth", "Bricks089", 1.5f, new Color(0.42f, 0.4f, 0.37f), 0.12f, 1.2f);
        public Material Plaster(int tint, int shade = 1)
        {
            tint = Mathf.Abs(tint) % PlasterTint.Length; shade = Mathf.Abs(shade) % Shade.Length;
            return Textured("plaster" + tint + "s" + shade, "Plaster007", 3.5f, PlasterTint[tint] * Shade[shade], 0.08f, 0.8f);
        }
        /// <summary>Wall block material per height band; lower bands are hazed toward the fog colour so the base reads far and huge.</summary>
        public Material WallBlock(int band)
        {
            band = Mathf.Clamp(band, 0, 3);
            float haze = new[] { 0.34f, 0.22f, 0.1f, 0f }[band];
            var tint = Color.Lerp(new Color(0.74f, 0.72f, 0.66f), FogColor, haze);
            return Textured("wallBlock" + band, "Rock030", 4.6f, tint, 0.06f, 1.1f);
        }
        public Material Mortar => Plain("mortar", new Color(0.26f, 0.25f, 0.23f), 0.05f);
        public Material WallStain => Textured("wallStain", "Rock030", 3.1f, new Color(0.3f, 0.29f, 0.26f), 0.04f, 0.6f);
        public Material Roof(int set, int tint)
        {
            set = Mathf.Abs(set) % RoofSets.Length; tint = Mathf.Abs(tint) % RoofTint.Length;
            return Textured("roof" + set + "_" + tint, RoofSets[set], 1.6f, RoofTint[tint], 0.1f, 1f);
        }
        public Material Timber => Textured("timber", "Planks039", 2.0f, new Color(0.42f, 0.3f, 0.2f), 0.15f, 0.7f);
        public Material TimberDark => Textured("timberDark", "Planks039", 2.0f, new Color(0.22f, 0.15f, 0.1f), 0.2f, 0.7f);
        public Material TimberPale => Textured("timberPale", "Planks039", 2.0f, new Color(0.62f, 0.5f, 0.36f), 0.15f, 0.7f);
        public Material Shutter(int tint) { tint = Mathf.Abs(tint) % ShutterTint.Length; return Textured("shutter" + tint, "Planks039", 1.2f, ShutterTint[tint], 0.2f, 0.6f); }
        public Material WallStone => Textured("wallStone", "Bricks100", 9f, new Color(0.8f, 0.78f, 0.72f), 0.08f, 1f);
        public Material WallStoneDark => Textured("wallStoneDark", "Bricks089", 7f, new Color(0.7f, 0.68f, 0.64f), 0.08f, 1f);
        public Material Paving(float tile) => Textured("paving", "PavingStones131", tile, new Color(0.72f, 0.68f, 0.62f), 0.12f, 1f);
        public Material Ground => Textured("ground", "PavingStones131", 3.2f, new Color(0.72f, 0.68f, 0.62f), 0.12f, 1f);
        public Material Glass => Plain("glass", new Color(0.1f, 0.13f, 0.18f), 0.9f, 0.3f);
        public Material Iron => Plain("iron", new Color(0.12f, 0.11f, 0.11f), 0.45f, 0.6f);
        public Material Water => Plain("water", new Color(0.25f, 0.38f, 0.42f), 0.95f, 0.1f);
        public Material Cloth(int i) { i = Mathf.Abs(i) % ClothTint.Length; return Plain("cloth" + i, ClothTint[i], 0.05f); }
        public Material Sack => Plain("sack", new Color(0.6f, 0.5f, 0.36f), 0.05f);
        public Material Straw => Plain("straw", new Color(0.78f, 0.66f, 0.36f), 0.05f);
        public Material Dark => Plain("dark", new Color(0.05f, 0.04f, 0.04f), 0.1f);
    }
}
