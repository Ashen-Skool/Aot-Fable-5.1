using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

/// Import rule for character FBX under Assets/Resources/Characters: Humanoid, clips named after takes, loops for locomotion.
public class CharacterImporter : AssetPostprocessor
{
    static readonly HashSet<string> Loops = new HashSet<string> { "idle", "walking_glb_url", "running_glb_url", "sprint", "combatidle", "swordrun", "runfast", "ropehang" };

    void OnPreprocessModel()
    {
        if (assetPath.Contains("/Resources/Props/")) { var pi = (ModelImporter)assetImporter; pi.animationType = ModelImporterAnimationType.None; pi.importAnimation = false; pi.useFileScale = true; pi.isReadable = true; return; }
        if (!assetPath.Contains("/Resources/Characters/")) return;
        var imp = (ModelImporter)assetImporter;
        imp.animationType = ModelImporterAnimationType.Human;
        imp.importAnimation = true;
        imp.useFileScale = true;
        imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
        imp.isReadable = false;
        imp.optimizeGameObjects = false; // we need the bone transforms for sockets
    }

    void OnPreprocessTexture()
    {
        if (!assetPath.Contains("/Resources/Characters/") && !assetPath.Contains("/Resources/Props/")) return;
        var t = (TextureImporter)assetImporter;
        if (assetPath.EndsWith("normal.png")) t.textureType = TextureImporterType.NormalMap;
        t.sRGBTexture = assetPath.EndsWith("base_color.png");
        t.maxTextureSize = 2048; t.mipmapEnabled = true;
    }

    void OnPreprocessAnimation()
    {
        if (!assetPath.Contains("/Resources/Characters/")) return;
        var imp = (ModelImporter)assetImporter;
        var clips = imp.defaultClipAnimations;
        foreach (var c in clips)
        {
            c.name = c.takeName.Replace("Armature|", "");
            c.loopTime = Loops.Contains(c.name);
            c.loopPose = c.loopTime;
            c.lockRootHeightY = true; c.lockRootRotation = true; c.lockRootPositionXZ = true; // no root motion; the controller moves the body
            c.keepOriginalPositionY = true; c.keepOriginalOrientation = true; c.keepOriginalPositionXZ = true;
        }
        imp.clipAnimations = clips;
    }
}
