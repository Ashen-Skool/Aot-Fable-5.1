using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Setup
{
    /// <summary>
    /// Creates the URP pipeline asset + renderer data from code, assigns them to
    /// Graphics and every Quality level, enables HDR and saves.
    /// Unity -batchmode -quit -executeMethod Setup.UrpSetup.Run
    /// </summary>
    public static class UrpSetup
    {
        const string Dir = "Assets/Settings";
        const string RendererPath = Dir + "/UrpRenderer.asset";
        const string PipelinePath = Dir + "/UrpPipeline.asset";

        /// <summary>Screen-space ambient occlusion as a renderer feature (sub-asset of the renderer data). Idempotent.</summary>
        static void Ssao(UniversalRendererData renderer)
        {
            foreach (var f in renderer.rendererFeatures) if (f is ScreenSpaceAmbientOcclusion) return;
            var ao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            ao.name = "SSAO";
            AssetDatabase.AddObjectToAsset(ao, renderer);
            var aso = new SerializedObject(ao);
            var st = aso.FindProperty("m_Settings");
            st.FindPropertyRelative("AOMethod").enumValueIndex = 0;          // blue noise
            st.FindPropertyRelative("Downsample").boolValue = false;
            st.FindPropertyRelative("AfterOpaque").boolValue = false;
            st.FindPropertyRelative("Source").enumValueIndex = 1;            // depth + normals
            st.FindPropertyRelative("NormalSamples").enumValueIndex = 2;     // high
            st.FindPropertyRelative("Intensity").floatValue = 1.6f;
            st.FindPropertyRelative("DirectLightingStrength").floatValue = 0.3f;
            st.FindPropertyRelative("Radius").floatValue = 0.6f;
            st.FindPropertyRelative("Samples").enumValueIndex = 1;           // medium
            st.FindPropertyRelative("BlurQuality").enumValueIndex = 0;       // high
            st.FindPropertyRelative("Falloff").floatValue = 120f;
            aso.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ao, out _, out long localId);
            var rso = new SerializedObject(renderer);
            var list = rso.FindProperty("m_RendererFeatures"); var map = rso.FindProperty("m_RendererFeatureMap");
            list.arraySize++; list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = ao;
            map.arraySize++; map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            rso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(renderer);
            Debug.Log("[UrpSetup] SSAO feature added (localId " + localId + ")");
        }

        public static UniversalRenderPipelineAsset Run()
        {
            if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.name = "UrpRenderer";
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }
            if (renderer.postProcessData == null)
                renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                    "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            renderer.renderingMode = RenderingMode.Forward;
            renderer.depthPrimingMode = DepthPrimingMode.Auto;
            EditorUtility.SetDirty(renderer);
            Ssao(renderer);

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.name = "UrpPipeline";
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 4;
            pipeline.shadowDistance = 260f;
            pipeline.shadowCascadeCount = 4;
            pipeline.cascade4Split = new Vector3(0.05f, 0.15f, 0.4f);
            pipeline.mainLightShadowmapResolution = 4096;
            { var pso = new SerializedObject(pipeline); pso.FindProperty("m_SoftShadowsSupported").boolValue = true; pso.ApplyModifiedPropertiesWithoutUndo(); }
            pipeline.shadowDepthBias = 1.0f;
            pipeline.shadowNormalBias = 0.6f;
            pipeline.supportsCameraDepthTexture = true;
            pipeline.supportsCameraOpaqueTexture = true;
            pipeline.colorGradingMode = ColorGradingMode.HighDynamicRange;
            pipeline.colorGradingLutSize = 32;
            EditorUtility.SetDirty(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int prev = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(prev, false);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[UrpSetup] pipeline assigned: " + PipelinePath + " HDR=" + pipeline.supportsHDR);
            return pipeline;
        }
    }
}
