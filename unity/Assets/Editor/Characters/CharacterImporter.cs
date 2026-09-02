using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

/// Import rule for character FBX under Assets/Resources/Characters: Humanoid, clips named after takes, loops for locomotion.
public class CharacterImporter : AssetPostprocessor
{
    static readonly HashSet<string> Loops = new HashSet<string> { "idle", "walking_glb_url", "running_glb_url", "sprint" };

    void OnPreprocessModel()
    {
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
