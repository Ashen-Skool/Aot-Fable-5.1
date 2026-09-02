using Shared;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Town
{
    /// <summary>
    /// Entry point: after Bootstrap, generates the district for the run seed, removes the stub
    /// street, re-skins the ground, and sets the golden late-afternoon atmosphere (HDRI sky,
    /// matched sun, fog, ambient). Idempotent.
    /// </summary>
    public static class TownRuntime
    {
        // Sun position measured in qwantani_late_afternoon_puresky_2k.hdr (brightest texel):
        // azimuth atan2(x,z) in degrees at _Rotation = 0, elevation above the horizon.
        public const float HdriSunAzimuth = 126.1f;
        public const float HdriSunElevation = 19.1f;
        // Where we want the sun: south-west, so south-facing fronts, the east side of the main
        // street and the inner face of the wall catch the light.
        public const float SunAzimuth = -135f;

        public static TownInfo Info { get; private set; }
        public static GameObject Root { get; private set; }
        public static TownMaterials Materials { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBuild() => Ensure();

        public static TownInfo Ensure()
        {
            if (Root != null) return Info;
            var boot = Bootstrap.Ensure();
            int seed = Ctx.Has("seed") ? Ctx.Get<int>("seed") : boot.seed;
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            var layout = TownLayout.Build(seed);
            Root = new GameObject("Town");
            Materials = new TownMaterials();
            Info = TownBuilder.Build(layout, Root.transform, Materials);

            var placeholder = Ctx.Get<GameObject>("placeholder");
            if (placeholder != null) Object.Destroy(placeholder);
            Ctx.Set("townCenter", new Vector3(Info.square.center.x, 0f, Info.square.center.y));

            // the stub titan capsule reads as a pale floating blob at the end of the street; hide it until the Titan piece replaces it
            var stubTitan = Ctx.Get<GameObject>("titan");
            if (stubTitan != null && stubTitan.GetComponent<Renderer>() != null && stubTitan.GetComponent<MeshFilter>() != null)
                foreach (var r in stubTitan.GetComponentsInChildren<Renderer>()) r.enabled = false;
            Ground();
            Atmosphere(Ctx.Get<Light>("light"));
            Debug.Log("[Town] built seed=" + seed + " houses=" + Info.houseCount + " rooftops=" + Info.rooftops.Count + " in " + t0.ElapsedMilliseconds + " ms");
            return Info;
        }

        static void Ground()
        {
            var ground = Ctx.Get<GameObject>("ground");
            if (ground == null) return;
            var r = ground.GetComponent<Renderer>();
            if (r == null) return;
            // Bootstrap's plane is 400 m with 0..1 UVs; tile the cobbles every 3.2 m.
            var m = Materials.Textured("groundPlane", "PavingStones131", 3.2f / 400f, new Color(0.5f, 0.46f, 0.4f), 0.08f, 1f);
            r.sharedMaterial = m;
        }

        public static Vector3 SunDirection()
        {
            float az = SunAzimuth * Mathf.Deg2Rad, el = HdriSunElevation * Mathf.Deg2Rad;
            var toSun = new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
            return -toSun;
        }

        static void Atmosphere(Light sun)
        {
            var skyBase = Resources.Load<Material>("Town/Materials/Sky");
            if (skyBase != null)
            {
                var sky = new Material(skyBase);
                sky.SetFloat("_Rotation", Mathf.Repeat(HdriSunAzimuth - SunAzimuth, 360f)); // panoramic: feature at az0 shows at az0 - rotation
                sky.SetFloat("_Exposure", 1.0f);
                sky.SetColor("_Tint", new Color(0.5f, 0.48f, 0.46f));
                RenderSettings.skybox = sky;
            }
            else
            {
                Debug.LogWarning("[Town] Resources/Town/Materials/Sky missing (run Town.Editor.TownSetup.Run); keeping the procedural sky");
                var sky = RenderSettings.skybox;
                if (sky != null && sky.HasProperty("_SkyTint")) sky.SetColor("_SkyTint", new Color(0.7f, 0.6f, 0.5f));
            }

            if (sun != null)
            {
                sun.transform.rotation = Quaternion.LookRotation(SunDirection(), Vector3.up);
                sun.color = new Color(1f, 0.8f, 0.6f);
                sun.intensity = 2.9f;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.88f;
                sun.shadowBias = 0.02f;
                sun.shadowNormalBias = 0.25f;
                RenderSettings.sun = sun;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.5f, 0.68f) * 1.35f;
            RenderSettings.ambientEquatorColor = new Color(0.82f, 0.66f, 0.5f) * 0.95f;
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.3f, 0.24f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.86f, 0.75f, 0.6f);
            RenderSettings.fogDensity = 0.0022f;
            DynamicGI.UpdateEnvironment();
            Grade();
        }

        /// <summary>Global volume: ACES tonemapping so the HDR sky and sunlit walls roll off instead of clipping to white.</summary>
        static void Grade()
        {
            if (Root == null) return;
            var go = new GameObject("TownAtmosphere");
            go.transform.SetParent(Root.transform, false);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 0f;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            var tone = prof.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var adj = prof.Add<ColorAdjustments>(true);
            adj.postExposure.Override(0.4f);
            adj.saturation.Override(6f);
            adj.contrast.Override(8f);
            vol.sharedProfile = prof;
            var cam = Ctx.Get<Camera>("camera");
            if (cam != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null) data.renderPostProcessing = true;
            }
        }
    }
}
