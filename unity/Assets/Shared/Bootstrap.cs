using System;
using UnityEngine;

namespace Shared
{
    /// <summary>
    /// Builds the world from code at startup. The saved scene only holds a camera.
    /// Everything created here is a placeholder that later pieces replace; they find
    /// it through Ctx (names below) and may Destroy the "placeholder" root.
    ///
    /// Ctx names registered: seed (int), bootstrap, camera (Camera), orbit (OrbitCamera),
    /// light (Light), ground, mikasa, titan, placeholder (GameObject), townCenter (Vector3).
    /// </summary>
    public class Bootstrap : MonoBehaviour
    {
        public const int DefaultSeed = 42;
        public static Bootstrap Instance { get; private set; }
        public static bool Built => Instance != null;

        public int seed = DefaultSeed;
        public GameObject mikasa, titan, ground, placeholder;
        public Light sun;
        public Camera cam;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot() => Ensure();

        /// <summary>Idempotent: builds the scene once, returns the live instance.</summary>
        public static Bootstrap Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("Bootstrap");
            return go.AddComponent<Bootstrap>();
        }

        public static int ArgInt(string flag, int fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag && int.TryParse(args[i + 1], out var v)) return v;
            return fallback;
        }

        public static string Arg(string flag, string fallback = null)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag) return args[i + 1];
            return fallback;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            seed = ArgInt("-seed", DefaultSeed);
            UnityEngine.Random.InitState(seed);
            Time.fixedDeltaTime = 1f / 60f;
            Ctx.Set("seed", seed);
            Ctx.Set("bootstrap", this);
            Ctx.Set("townCenter", Vector3.zero);
            BuildCamera();
            BuildLighting();
            BuildGround();
            BuildPlaceholders();
            BuildMikasa();
            BuildTitan();
            Debug.Log("[Bootstrap] built, seed=" + seed);
        }

        void BuildCamera()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 2000f;
            cam.fieldOfView = 60f;
            cam.allowHDR = true;
            var orbit = cam.GetComponent<OrbitCamera>() ?? cam.gameObject.AddComponent<OrbitCamera>();
            Ctx.Set("camera", cam);
            Ctx.Set("orbit", orbit);
        }

        void BuildLighting()
        {
            var go = new GameObject("Sun");
            sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.93f, 0.82f);
            sun.intensity = 2.0f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.9f;
            go.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.sun = sun;

            var sky = Mats.Sky();
            if (sky.HasProperty("_SkyTint")) sky.SetColor("_SkyTint", new Color(0.55f, 0.7f, 0.95f));
            if (sky.HasProperty("_GroundColor")) sky.SetColor("_GroundColor", new Color(0.35f, 0.33f, 0.3f));
            if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", 1.1f);
            if (sky.HasProperty("_AtmosphereThickness")) sky.SetFloat("_AtmosphereThickness", 0.9f);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.5f, 0.6f, 0.8f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.45f, 0.45f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.18f, 0.16f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.66f, 0.72f, 0.82f);
            RenderSettings.fogDensity = 0.0025f;
            Ctx.Set("light", sun);
        }

        void BuildGround()
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(40f, 1f, 40f); // plane is 10 m -> 400 m
            var mat = Mats.Lit(Color.white, 0.05f);
            mat.mainTexture = Mats.GridTexture();
            mat.SetTexture("_BaseMap", mat.mainTexture);
            mat.SetTextureScale("_BaseMap", new Vector2(40f, 40f));
            ground.GetComponent<Renderer>().sharedMaterial = mat;
            Ctx.Set("ground", ground);
        }

        /// <summary>A stub street so the capture poses point at something before Town exists.</summary>
        void BuildPlaceholders()
        {
            placeholder = new GameObject("Placeholder");
            var stone = Mats.Lit(new Color(0.62f, 0.58f, 0.52f), 0.1f);
            var roof = Mats.Lit(new Color(0.55f, 0.25f, 0.18f), 0.15f);
            var rng = new System.Random(seed);
            for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < 8; i++)
            {
                float w = 6f + (float)rng.NextDouble() * 3f;
                float d = 7f + (float)rng.NextDouble() * 3f;
                float h = 8f + (float)rng.NextDouble() * 6f;
                float z = -40f + i * 11f;
                float x = side * (7f + w * 0.5f);
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = "Block_" + (side < 0 ? "L" : "R") + i;
                b.transform.SetParent(placeholder.transform);
                b.transform.position = new Vector3(x, h * 0.5f, z);
                b.transform.localScale = new Vector3(w, h, d);
                b.GetComponent<Renderer>().sharedMaterial = stone;
                var r = GameObject.CreatePrimitive(PrimitiveType.Cube);
                r.name = "Roof";
                r.transform.SetParent(b.transform, false);
                r.transform.localPosition = new Vector3(0, 0.5f + 0.06f, 0);
                r.transform.localScale = new Vector3(1.1f, 0.12f, 1.1f);
                r.GetComponent<Renderer>().sharedMaterial = roof;
            }
            Ctx.Set("placeholder", placeholder);
        }

        void BuildMikasa()
        {
            mikasa = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            mikasa.name = "Mikasa";
            mikasa.transform.position = new Vector3(0f, 1f, -20f);
            mikasa.GetComponent<Renderer>().sharedMaterial = Mats.Lit(new Color(0.15f, 0.15f, 0.18f), 0.3f);
            var scarf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            scarf.name = "Scarf";
            scarf.transform.SetParent(mikasa.transform, false);
            scarf.transform.localPosition = new Vector3(0, 0.55f, 0);
            scarf.transform.localScale = new Vector3(1.15f, 0.18f, 1.15f);
            scarf.GetComponent<Renderer>().sharedMaterial = Mats.Lit(new Color(0.75f, 0.1f, 0.08f), 0.2f);
            var orbit = Ctx.Get<OrbitCamera>("orbit");
            if (orbit != null) orbit.target = mikasa.transform;
            Ctx.Set("mikasa", mikasa);
        }

        void BuildTitan()
        {
            titan = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            titan.name = "Titan";
            // capsule primitive is 2 m tall: scale y 7.5 -> 15 m, x/z 5 -> 5 m wide
            titan.transform.localScale = new Vector3(5f, 7.5f, 5f);
            titan.transform.position = new Vector3(0f, 7.5f, 60f);
            titan.transform.rotation = Quaternion.Euler(0, 180f, 0);
            titan.GetComponent<Renderer>().sharedMaterial = Mats.Lit(new Color(0.78f, 0.6f, 0.5f), 0.25f);
            Ctx.Set("titan", titan);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
