using UnityEditor;
using UnityEngine;

namespace ODM.EditorTools
{
    /// <summary>
    /// Makes sure the HookTarget and Titan layers exist in ProjectSettings/TagManager.asset.
    /// Runs on editor load (batch mode included) so builds bake the names. Runtime code
    /// falls back to the fixed indices in OdmLayers if the names are missing.
    /// </summary>
    [InitializeOnLoad]
    public static class OdmLayerSetup
    {
        static OdmLayerSetup() { Ensure(); }

        public static void Ensure()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            var layers = so.FindProperty("layers");
            if (layers == null) return;
            bool changed = false;
            changed |= SetLayer(layers, OdmLayers.HookIndex, OdmLayers.HookName);
            changed |= SetLayer(layers, OdmLayers.TitanIndex, OdmLayers.TitanName);
            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
                Debug.Log("[ODM] layers ensured: " + OdmLayers.HookName + "=" + OdmLayers.HookIndex + " " + OdmLayers.TitanName + "=" + OdmLayers.TitanIndex);
            }
        }

        static bool SetLayer(SerializedProperty layers, int index, string name)
        {
            for (int i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == name) return false;
            var slot = layers.GetArrayElementAtIndex(index);
            if (!string.IsNullOrEmpty(slot.stringValue) && slot.stringValue != name)
            {
                Debug.LogWarning("[ODM] layer slot " + index + " is taken by '" + slot.stringValue + "', cannot add " + name);
                return false;
            }
            slot.stringValue = name;
            return true;
        }
    }
}
