using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Shared
{
    /// <summary>
    /// Command-line switches to bisect frame cost in a built player: -noSsao -noPost -msaa1 -shadow2k -noShadows -noMist
    /// -noSmoke -noDust -noLamps -noTrees. Town systems read Off("mist") etc.; pipeline switches are applied here.
    /// </summary>
    public static class PerfToggles
    {
        static HashSet<string> set;
        public static bool Off(string name)
        {
            if (set == null)
            {
                set = new HashSet<string>();
                foreach (var a in System.Environment.GetCommandLineArgs()) if (a.StartsWith("-no") || a == "-msaa1" || a == "-shadow2k") set.Add(a.ToLowerInvariant());
            }
            return set.Contains("-no" + name.ToLowerInvariant());
        }
        public static bool Has(string flag) { Off("x"); return set.Contains(flag.ToLowerInvariant()); }

        public static void ApplyPipeline(Camera cam)
        {
            var pipe = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipe == null) return;
            if (Has("-msaa1")) pipe.msaaSampleCount = 1;
            if (Has("-shadow2k")) { pipe.shadowCascadeCount = 2; pipe.mainLightShadowmapResolution = 2048; }
            if (Off("shadows")) { var sun = Ctx.Get<Light>("light"); if (sun != null) sun.shadows = LightShadows.None; }
            if (Off("post") && cam != null) cam.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            if (Off("ssao"))
            {
                // the renderer data list is internal: reach it by reflection and switch the SSAO feature off
                var f = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var list = f?.GetValue(pipe) as ScriptableRendererData[];
                if (list != null) foreach (var rd in list) if (rd != null) foreach (var feat in rd.rendererFeatures) if (feat != null && feat.GetType().Name.Contains("AmbientOcclusion")) feat.SetActive(false);
            }
            var flags = new List<string>(set); if (flags.Count > 0) Debug.Log("[PerfToggles] " + string.Join(" ", flags));
        }
    }
}
