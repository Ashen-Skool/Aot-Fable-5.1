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
        // Sun position measured in wasteland_clouds_puresky_2k.hdr (brightest texel, tools/sunpos.py):
        // azimuth atan2(x,z) in degrees at _Rotation = 0, elevation above the horizon.
        public const float HdriSunAzimuth = 125.9f;
        public const float HdriSunElevation = 7.6f;
        // The light sits a little higher than the sky's sun so the streets keep some direct light.
        public const float SunElevation = 16f;
        // Where we want the sun: south-west, so south-facing fronts, the east side of the main
        // street and the inner face of the wall catch the light.
        public const float SunAzimuth = -135f;

        public static TownInfo Info { get; private set; }
        public static GameObject Root { get; private set; }
        public static TownMaterials Materials { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBuild() { Reboot.Register(10, () => Ensure()); Ensure(); }

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
            TownDressing.Build(layout, Info, Root.transform, Materials);
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
            // Trim the world to the built-out district: the plane covers the town bounds (plus a street's margin), and
            // invisible walls keep the player inside. Beyond the wall there is nothing.
            var b = Info.bounds; float margin = 12f;
            var size = new Vector3(b.size.x + margin * 2f, 1f, b.size.z + margin * 2f);
            ground.transform.position = new Vector3(b.center.x, 0f, b.center.z);
            ground.transform.localScale = new Vector3(size.x / 10f, 1f, size.z / 10f);
            var m = Materials.Textured("groundPlane", "PavingStones131", 3.2f / Mathf.Max(size.x, size.z), new Color(0.5f, 0.46f, 0.4f), 0.08f, 1f);
            r.sharedMaterial = m;
            var fence = new GameObject("Boundary"); fence.transform.SetParent(Root.transform, false);
            void Fence(Vector3 c, Vector3 s) { var f = fence.AddComponent<BoxCollider>(); f.center = c; f.size = s; }
            float h = 140f, t = 2f; float x0 = b.min.x - margin, x1 = b.max.x + margin, z0 = b.min.z - margin, z1 = b.max.z + margin;
            Fence(new Vector3(x0, h * 0.5f, b.center.z), new Vector3(t, h, size.z)); Fence(new Vector3(x1, h * 0.5f, b.center.z), new Vector3(t, h, size.z));
            Fence(new Vector3(b.center.x, h * 0.5f, z0), new Vector3(size.x, h, t)); Fence(new Vector3(b.center.x, h * 0.5f, z1), new Vector3(size.x, h, t));
            Ctx.Set("town.wallTop", new Vector3(b.max.x * 0.45f, Info.wallHeight, Info.wallZ + 5f));
            Ctx.Set("town.stoneMat", Materials.WallStone); Ctx.Set("town.roofMat", Materials.WallStoneDark);
        }

        public static Vector3 SunDirection()
        {
            float az = SunAzimuth * Mathf.Deg2Rad, el = SunElevation * Mathf.Deg2Rad;
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
                sky.SetFloat("_Exposure", 0.5f);   // this HDRI is ~2.5x brighter than qwantani in the upper hemisphere (tools/skymean.py)
                sky.SetColor("_Tint", new Color(0.52f, 0.5f, 0.48f));
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
                sun.color = new Color(1f, 0.76f, 0.52f);
                sun.intensity = 3.0f;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.92f;
                sun.shadowBias = 0.02f;
                sun.shadowNormalBias = 0.3f;
                RenderSettings.sun = sun;
            }

            // Ambient from the sky itself (cool blue from above, warm bounce at the horizon), lifted a touch for the shadowed streets.
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.7f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = TownMaterials.FogColor;
            RenderSettings.fogDensity = 0.0023f;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            DynamicGI.UpdateEnvironment();
            Grade();
            TownLife.Build(Info, Root.transform);
            AmbientBed.Ensure(Ctx.Get<Camera>("camera"));
        }

        /// <summary>Global volume: ACES tonemapping, bloom for the sun and lit windows, warm highlights over cool shadows, a soft vignette.</summary>
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
            adj.saturation.Override(10f);
            adj.contrast.Override(10f);
            var wb = prof.Add<WhiteBalance>(true);
            wb.temperature.Override(8f);
            var smh = prof.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.92f, 0.96f, 1.08f, 0f));
            smh.highlights.Override(new Vector4(1.06f, 1.0f, 0.92f, 0f));
            var bloom = prof.Add<Bloom>(true);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.68f);
            bloom.tint.Override(new Color(1f, 0.9f, 0.75f));
            var vig = prof.Add<Vignette>(true);
            vig.intensity.Override(0.22f);
            vig.smoothness.Override(0.45f);
            var grain = prof.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.12f);
            var ca = prof.Add<ChromaticAberration>(true);
            ca.intensity.Override(0.02f);
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
