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

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.name = "UrpPipeline";
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 4;
            pipeline.shadowDistance = 250f;
            pipeline.shadowCascadeCount = 4;
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
