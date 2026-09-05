using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Town
{
    /// <summary>
    /// The things that make the district read as inhabited: smoke from the chimneys, dust hanging in the
    /// low sun, and two flocks of birds turning over the roofs. All cheap, all deterministic for the seed.
    /// </summary>
    public static class TownLife
    {
        static Texture2D soft;
        static Material particleMat;

        public static void Build(TownInfo info, Transform parent)
        {
            if (info == null || parent == null) return;
            var root = new GameObject("Life").transform; root.SetParent(parent, false);
            var rng = new System.Random(Ctx.Has("seed") ? Ctx.Get<int>("seed") : 42);
            Smoke(info, root, rng);
            Dust(root);
            Birds(root, new Vector3(info.square.center.x, 46f, info.square.center.y), 26f, 7, 0.9f);
            Birds(root, new Vector3(info.square.center.x - 70f, 58f, info.wallZ - 40f), 34f, 5, -0.7f);
        }

        static Material ParticleMat()
        {
            if (particleMat != null) return particleMat;
            if (soft == null)
            {
                const int n = 64; soft = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                    float a = Mathf.Clamp01(1f - d); a = a * a * (3f - 2f * a);
                    soft.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
                soft.Apply();
            }
            var baseMat = Resources.Load<Material>("Materials/Particles");
            particleMat = baseMat != null ? new Material(baseMat) : Mats.Unlit(Color.white);
            particleMat.name = "lifeParticles";
            particleMat.mainTexture = soft;
            if (particleMat.HasProperty("_BaseMap")) particleMat.SetTexture("_BaseMap", soft);
            if (particleMat.HasProperty("_BaseColor")) particleMat.SetColor("_BaseColor", Color.white);
            return particleMat;
        }

        static ParticleSystem NewSystem(Transform parent, string name, Vector3 pos)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = ParticleMat(); r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return ps;
        }

        /// <summary>Every third chimney (up to 90) trails a thin, wind-bent plume.</summary>
        static void Smoke(TownInfo info, Transform root, System.Random rng)
        {
            int made = 0;
            var wind = Quaternion.Euler(0f, TownRuntime.SunAzimuth + 100f, 0f) * Vector3.forward;
            for (int i = 0; i < info.chimneys.Count && made < 90; i += 3)
            {
                if (rng.NextDouble() < 0.25) continue;
                var ps = NewSystem(root, "Smoke", info.chimneys[i]);
                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 11f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startColor = new Color(0.4f, 0.39f, 0.38f, 1f);
                main.gravityModifier = -0.012f;
                main.maxParticles = 60;
                var em = ps.emission; em.rateOverTime = 2.6f;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 8f; sh.radius = 0.12f;
                var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(wind.x * 0.9f, wind.x * 1.6f);
                vel.y = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);   // all three axes must share a curve mode
                vel.z = new ParticleSystem.MinMaxCurve(wind.z * 0.9f, wind.z * 1.6f);
                var sol = ps.sizeOverLifetime; sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, Grow());
                var col = ps.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.16f, 0.12f), new GradientAlphaKey(0.08f, 0.6f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
                var noise = ps.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 0.18f; noise.scrollSpeed = 0.15f;
                ps.Simulate(12f, true, true); ps.Play();
                made++;
            }
        }

        static AnimationCurve Grow() => new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.5f, 1.4f), new Keyframe(1f, 2.6f));

        /// <summary>Dust and pollen hanging in the light around the camera. Follows the camera, world-simulated so it hangs still.</summary>
        static void Dust(Transform root)
        {
            var cam = Ctx.Get<Camera>("camera");
            if (cam == null) return;
            var ps = NewSystem(root, "Dust", cam.transform.position);
            ps.gameObject.AddComponent<FollowCamera>();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSpeed = 0.05f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.045f);
            main.startColor = new Color(1f, 0.92f, 0.75f, 0.38f);
            main.gravityModifier = 0.002f;
            main.maxParticles = 700;
            var em = ps.emission; em.rateOverTime = 45f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(22f, 10f, 22f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.22f; noise.frequency = 0.5f; noise.scrollSpeed = 0.2f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            ps.Simulate(8f, true, true); ps.Play();
        }

        static void Birds(Transform root, Vector3 centre, float radius, int count, float dir)
        {
            var flock = new GameObject("Birds").transform; flock.SetParent(root, false);
            var mat = Mats.Lit(new Color(0.06f, 0.05f, 0.05f), 0.1f);
            for (int i = 0; i < count; i++)
            {
                var b = new GameObject("Bird").transform; b.SetParent(flock, false);
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(body.GetComponent<Collider>());
                body.transform.SetParent(b, false); body.transform.localScale = new Vector3(0.22f, 0.05f, 0.5f);
                body.GetComponent<Renderer>().sharedMaterial = mat;
                for (int w = -1; w <= 1; w += 2)
                {
                    var wing = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(wing.GetComponent<Collider>());
                    wing.name = w < 0 ? "L" : "R"; wing.transform.SetParent(b, false);
                    wing.transform.localPosition = new Vector3(w * 0.42f, 0f, 0f); wing.transform.localScale = new Vector3(0.7f, 0.03f, 0.28f);
                    wing.GetComponent<Renderer>().sharedMaterial = mat;
                }
                var f = b.gameObject.AddComponent<BirdFlight>();
                f.centre = centre; f.radius = radius * (0.8f + 0.4f * (i / (float)count)); f.phase = i * (Mathf.PI * 2f / count) + i * 0.37f;
                f.speed = dir * (0.28f + 0.05f * (i % 3)); f.bob = 2.5f + i * 0.4f;
            }
        }

        class FollowCamera : MonoBehaviour
        {
            Camera cam;
            void LateUpdate()
            {
                if (cam == null) cam = Ctx.Get<Camera>("camera");
                if (cam != null) transform.position = cam.transform.position + cam.transform.forward * 12f;
            }
        }

        class BirdFlight : MonoBehaviour
        {
            public Vector3 centre; public float radius = 25f, phase, speed = 0.3f, bob = 3f;
            Transform l, r; float flap;
            void Start() { l = transform.Find("L"); r = transform.Find("R"); flap = phase * 3f; }
            void Update()
            {
                float t = Time.time * speed + phase;
                var p = centre + new Vector3(Mathf.Cos(t) * radius, Mathf.Sin(t * 1.7f + phase) * bob, Mathf.Sin(t) * radius * 0.8f);
                var v = p - transform.position;
                if (v.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(v.normalized, Vector3.up);
                transform.position = p;
                flap += Time.deltaTime * 9f;
                float a = Mathf.Sin(flap) * 38f;
                if (l != null) l.localRotation = Quaternion.Euler(0f, 0f, a);
                if (r != null) r.localRotation = Quaternion.Euler(0f, 0f, -a);
            }
        }
    }
}
