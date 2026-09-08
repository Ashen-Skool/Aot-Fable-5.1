using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Build
{
    /// <summary>
    /// Per-platform WebGL import overrides for every texture that ships.
    ///
    /// The Meshy character and prop textures come in at 4096 and the town PBR sets at 2048, which is right for the
    /// standalone build and absurd for a web download: they were most of a 64 MB data file. WebGL2 here reports
    /// WEBGL_compressed_texture_s3tc, so DXT with crunch is safe. Standalone is untouched - this only writes the
    /// "WebGL" platform override into each importer, which the mac build ignores.
    ///
    ///   tools/unity.sh webgltex -quit -executeMethod Build.WebGLTextures.Run
    /// </summary>
    public static class WebGLTextures
    {
        const string Platform = "WebGL";

        /// <summary>Folder prefix -> max size on WebGL. Longest match wins.</summary>
        static readonly (string prefix, int max)[] Budget =
        {
            ("Assets/Resources/Props/FistTex", 256),          // a gloved fist, never more than a few hundred pixels on screen
            ("Assets/Resources/Props/BladeTex", 512),
            ("Assets/Resources/Props/CannonTex", 512),
            ("Assets/Resources/Characters", 1024),            // she and the Titan are what you actually look at
            ("Assets/Town/Imported/Resources/Town/Textures", 512),
            ("Assets/Resources", 512),
            ("Assets", 512),
        };

        static int MaxFor(string path)
        {
            foreach (var (prefix, max) in Budget) if (path.StartsWith(prefix)) return max;
            return 512;
        }

        [MenuItem("Build/Apply WebGL texture budget")]
        public static void Run()
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" });
            int changed = 0, skipped = 0;
            long before = 0;
            var seen = new HashSet<string>();
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                if (!seen.Add(path)) continue;
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) continue;
                // HDR skies cannot go through DXT; cap the size and leave the format alone
                bool hdr = path.EndsWith(".hdr") || path.EndsWith(".exr");
                var fi = new System.IO.FileInfo(path);
                if (fi.Exists) before += fi.Length;

                var ps = imp.GetPlatformTextureSettings(Platform);
                ps.name = Platform;
                ps.overridden = true;
                ps.maxTextureSize = hdr ? 1024 : MaxFor(path);
                // Format is left to Unity (DXT here). Crunch was tried and is NOT worth it: it shrinks the
                // download but the runtime transcode tanked the frame rate to 1 FPS in the browser.
                ps.format = TextureImporterFormat.Automatic;
                ps.textureCompression = hdr ? TextureImporterCompression.Uncompressed : TextureImporterCompression.Compressed;
                ps.compressionQuality = 50;
                imp.SetPlatformTextureSettings(ps);
                imp.SaveAndReimport();
                changed++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("WEBGLTEX_OK textures=" + changed + " skipped=" + skipped + " sourceBytes=" + before);
        }
    }
}
