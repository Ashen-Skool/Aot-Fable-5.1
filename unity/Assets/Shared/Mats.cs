using UnityEngine;

namespace Shared
{
    /// <summary>
    /// Runtime material factory. Base materials live in Assets/Shared/Resources/Materials
    /// (created by Setup.ProjectSetup) so their shaders are guaranteed to ship in builds.
    /// Everything else is cloned from them at runtime.
    /// </summary>
    public static class Mats
    {
        static Material Base(string name)
        {
            var m = Resources.Load<Material>("Materials/" + name);
            if (m == null)
            {
                // Fallback for a project where setup has not run yet (editor only, really).
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                m = new Material(sh);
            }
            return m;
        }

        public static Material Lit(Color c, float smooth = 0.2f, float metal = 0f)
        {
            var m = new Material(Base("Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            return m;
        }

        public static Material Unlit(Color c)
        {
            var m = new Material(Base("Unlit"));
            m.SetColor("_BaseColor", c);
            return m;
        }

        public static Material Sky() => new Material(Base("Sky"));

        /// <summary>Procedural grid texture, dark base with bright lines.</summary>
        public static Texture2D GridTexture(int size = 256, int cells = 8, Color? line = null, Color? fill = null)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            var l = line ?? new Color(0.55f, 0.58f, 0.6f);
            var f = fill ?? new Color(0.22f, 0.23f, 0.24f);
            int step = size / cells;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool onLine = (x % step) < 2 || (y % step) < 2;
                px[y * size + x] = onLine ? l : f;
            }
            tex.SetPixels(px);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 8;
            tex.Apply(true, false);
            return tex;
        }
    }
}
