using System.IO;
using UnityEditor;
using UnityEngine;

namespace Town.Editor
{
    /// <summary>
    /// One-shot editor setup for the Town piece (safe to re-run):
    ///   Unity -batchmode -quit -executeMethod Town.Editor.TownSetup.Run
    /// - defines the HookTarget physics layer,
    /// - marks the imported *normal.jpg textures as normal maps,
    /// - creates Resources/Town/Materials/LitNormal.mat (URP Lit + _NORMALMAP) and Sky.mat
    ///   (Skybox/Panoramic + the late-afternoon HDRI) so those shader variants ship in builds.
    /// </summary>
    public static class TownSetup
    {
        const string MatDir = "Assets/Town/Resources/Town/Materials";
        const string TexDir = "Assets/Town/Imported/Resources/Town/Textures";
        const string Hdri = "Assets/Town/Imported/Resources/Town/Sky/qwantani.hdr";

        public static void Run()
        {
            Layer();
            Textures();
            Materials();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("TOWN_SETUP_OK");
            System.Console.WriteLine("TOWN_SETUP_OK");
        }

        static void Layer()
        {
            if (LayerMask.NameToLayer(Layers.HookTargetName) >= 0) return;
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) { Debug.LogError("[TownSetup] TagManager not found"); return; }
            var so = new SerializedObject(assets[0]);
            var layers = so.FindProperty("layers");
            int slot = -1;
            for (int i = Layers.HookTargetFallback; i < layers.arraySize; i++)
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) { slot = i; break; }
            if (slot < 0) { Debug.LogError("[TownSetup] no free layer"); return; }
            layers.GetArrayElementAtIndex(slot).stringValue = Layers.HookTargetName;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[TownSetup] layer " + Layers.HookTargetName + " = " + slot);
        }

        static void Textures()
        {
            if (!Directory.Exists(TexDir)) return;
            foreach (var path in Directory.GetFiles(TexDir, "*.jpg", SearchOption.AllDirectories))
            {
                var p = path.Replace('\\', '/');
                var imp = AssetImporter.GetAtPath(p) as TextureImporter;
                if (imp == null) continue;
                bool normal = p.EndsWith("normal.jpg");
                bool changed = false;
                var wantType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (imp.textureType != wantType) { imp.textureType = wantType; changed = true; }
                if (imp.wrapMode != TextureWrapMode.Repeat) { imp.wrapMode = TextureWrapMode.Repeat; changed = true; }
                if (imp.anisoLevel < 8) { imp.anisoLevel = 8; changed = true; }
                if (imp.mipmapEnabled == false) { imp.mipmapEnabled = true; changed = true; }
                if (changed) { imp.SaveAndReimport(); Debug.Log("[TownSetup] reimported " + p + (normal ? " as NormalMap" : "")); }
            }
            var hdr = AssetImporter.GetAtPath(Hdri) as TextureImporter;
            if (hdr != null && (hdr.wrapMode != TextureWrapMode.Clamp || hdr.maxTextureSize < 2048))
            {
                hdr.wrapMode = TextureWrapMode.Clamp;
                hdr.maxTextureSize = 2048;
                hdr.SaveAndReimport();
            }
        }

        static void Materials()
        {
            Directory.CreateDirectory(MatDir);
            var litPath = MatDir + "/LitNormal.mat";
            var lit = AssetDatabase.LoadAssetAtPath<Material>(litPath);
            if (lit == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) { Debug.LogError("[TownSetup] URP Lit shader missing"); return; }
                lit = new Material(sh);
                AssetDatabase.CreateAsset(lit, litPath);
            }
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/Bricks076A/normal.jpg");
            var col = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/Bricks076A/color.jpg");
            if (col != null) lit.SetTexture("_BaseMap", col);
            if (nrm != null) lit.SetTexture("_BumpMap", nrm);
            lit.EnableKeyword("_NORMALMAP");
            lit.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(lit);

            var skyPath = MatDir + "/Sky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null)
            {
                var sh = Shader.Find("Skybox/Panoramic");
                if (sh == null) { Debug.LogError("[TownSetup] Skybox/Panoramic shader missing"); return; }
                sky = new Material(sh);
                AssetDatabase.CreateAsset(sky, skyPath);
            }
            var hdri = AssetDatabase.LoadAssetAtPath<Texture2D>(Hdri);
            if (hdri == null) Debug.LogError("[TownSetup] HDRI missing at " + Hdri);
            else sky.SetTexture("_MainTex", hdri);
            sky.SetFloat("_Mapping", 1f);
            sky.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
            sky.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
            sky.SetFloat("_ImageType", 0f);
            sky.SetFloat("_Exposure", 1.0f);
            sky.SetFloat("_Rotation", 0f);
            EditorUtility.SetDirty(sky);
            Debug.Log("[TownSetup] materials ok: " + litPath + ", " + skyPath);
        }
    }

    /// <summary>Fresh clones: make sure the Town normal maps import as normal maps before any material sees them.</summary>
    public class TownTextureImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Town/Imported/")) return;
            var imp = (TextureImporter)assetImporter;
            if (assetPath.EndsWith("normal.jpg")) imp.textureType = TextureImporterType.NormalMap;
            if (assetPath.EndsWith(".jpg")) { imp.wrapMode = TextureWrapMode.Repeat; imp.anisoLevel = 8; }
        }
    }
}
