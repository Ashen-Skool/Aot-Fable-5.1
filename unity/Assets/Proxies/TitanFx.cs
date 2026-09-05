using Shared;
using UnityEngine;

namespace Proxies
{
    /// <summary>
    /// The Titan's presentation: steam from every cut and a plume off the nape when he is opened up, a dust ring plus
    /// rubble under a stomp, camera trauma from his steps and impacts, and a steam bath on death. Attached by TitanBrain.
    /// </summary>
    public class TitanFx : MonoBehaviour
    {
        public float height = 15f;
        ParticleSystem steam, dust, rubble, sparks;
        Transform nape; float plume; float plumeWant; AudioSource steamSrc; AudioLowPassFilter steamLp;
        static Texture2D soft; static Material puffMat;

        public static TitanFx Attach(GameObject host, float height)
        {
            var fx = host.GetComponent<TitanFx>() ?? host.AddComponent<TitanFx>();
            fx.height = height;
            return fx;
        }

        static Material PuffMat()
        {
            if (puffMat != null) return puffMat;
            if (soft == null)
            {
                const int n = 64; soft = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                    float a = Mathf.Clamp01(1f - d); a = a * a * (3f - 2f * a);
                    soft.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
                soft.Apply();
            }
            var b = Resources.Load<Material>("Materials/Particles");
            puffMat = b != null ? new Material(b) : Mats.Unlit(Color.white);
            puffMat.mainTexture = soft; if (puffMat.HasProperty("_BaseMap")) puffMat.SetTexture("_BaseMap", soft);
            return puffMat;
        }

        ParticleSystem Sys(string name, Material mat)
        {
            var go = new GameObject(name); go.transform.SetParent(null, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.playOnAwake = false; main.loop = false; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 600;
            var em = ps.emission; em.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return ps;
        }

        static Gradient Fade(float a0, float a1, float aEnd)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(a1, 0.3f), new GradientAlphaKey(aEnd, 1f) });
            return g;
        }

        void Awake()
        {
            var mat = PuffMat();
            steam = Sys("TitanSteam", mat);
            { var m = steam.main; m.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.6f); m.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6f); m.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.4f); m.startColor = new Color(0.92f, 0.9f, 0.88f, 0.55f); m.gravityModifier = -0.12f; m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
              var c = steam.colorOverLifetime; c.enabled = true; c.color = Fade(0.55f, 0.4f, 0f);
              var s = steam.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 2.6f)));
              var n = steam.noise; n.enabled = true; n.strength = 1.2f; n.frequency = 0.3f;
              var sh = steam.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.5f; }
            dust = Sys("TitanDust", mat);
            { var m = dust.main; m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f); m.startSpeed = new ParticleSystem.MinMaxCurve(6f, 16f); m.startSize = new ParticleSystem.MinMaxCurve(2f, 4.5f); m.startColor = new Color(0.6f, 0.52f, 0.42f, 0.7f); m.gravityModifier = 0.15f; m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
              var c = dust.colorOverLifetime; c.enabled = true; c.color = Fade(0.7f, 0.5f, 0f);
              var s = dust.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 2.2f)));
              var sh = dust.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 1.2f; }
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); var cubeMesh = cube.GetComponent<MeshFilter>().sharedMesh; Destroy(cube);
            rubble = Sys("TitanRubble", Mats.Lit(new Color(0.42f, 0.38f, 0.33f), 0.05f));
            { var m = rubble.main; m.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3f); m.startSpeed = new ParticleSystem.MinMaxCurve(7f, 15f); m.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.55f); m.gravityModifier = 1.1f; m.startRotation3D = true; m.startRotationX = new ParticleSystem.MinMaxCurve(0f, 6.28f); m.startRotationY = new ParticleSystem.MinMaxCurve(0f, 6.28f); m.startRotationZ = new ParticleSystem.MinMaxCurve(0f, 6.28f);
              var rot = rubble.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true; rot.x = new ParticleSystem.MinMaxCurve(-4f, 4f); rot.y = new ParticleSystem.MinMaxCurve(-4f, 4f); rot.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
              var col = rubble.collision; col.enabled = true; col.type = ParticleSystemCollisionType.World; col.bounce = 0.2f; col.dampen = 0.4f; col.lifetimeLoss = 0.1f;
              var r = rubble.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = cubeMesh; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
              var sh = rubble.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 1f; }
            sparks = Sys("TitanBlood", Mats.Unlit(new Color(1.8f, 0.35f, 0.25f)));
            { var m = sparks.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.7f); m.startSpeed = new ParticleSystem.MinMaxCurve(6f, 14f); m.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f); m.gravityModifier = 0.8f;
              var c = sparks.colorOverLifetime; c.enabled = true; c.color = Fade(1f, 0.9f, 0f);
              var r = sparks.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f; r.lengthScale = 1.5f;
              var sh = sparks.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.3f; }
            steamSrc = NoiseLoop.Source(gameObject, NoiseLoop.White(), 1f, 140f, out steamLp); if (steamLp != null) steamLp.cutoffFrequency = 1400f;
        }

        Transform Nape()
        {
            if (nape == null) { var z = FindDeep(transform, "Zone_Nape"); nape = z; }
            return nape;
        }
        public Vector3 NapePos() { var n = Nape(); if (n == null) return transform.position + Vector3.up * height * 0.85f; var c = n.GetComponent<Collider>(); return c != null ? c.bounds.center : n.position; }
        static Transform FindDeep(Transform t, string name) { if (t.name == name) return t; for (int i = 0; i < t.childCount; i++) { var r = FindDeep(t.GetChild(i), name); if (r != null) return r; } return null; }

        static void Shake(float trauma) { var rig = Ctx.Get<Component>("cameraRig"); if (rig != null) rig.SendMessage("Shake", trauma, SendMessageOptions.DontRequireReceiver); }

        /// <summary>A blade or shell hit at a world point. strength 1 = a nape cut.</summary>
        public void HitBurst(Vector3 pos, float strength)
        {
            if (Application.isBatchMode) return;
            var ep = new ParticleSystem.EmitParams { position = pos, applyShapeToPosition = true };
            steam.Emit(ep, Mathf.RoundToInt(10 + 26 * strength));
            sparks.Emit(ep, Mathf.RoundToInt(8 + 18 * strength));
            Shake(0.25f + 0.45f * strength);
            plume = Mathf.Max(plume, 0.6f * strength);
        }

        /// <summary>Continuous steam off the nape (kneel, death).</summary>
        public void NapePlume(bool on, float amount = 1f) { plumeWant = on ? amount : 0f; }

        public void Stomp(Vector3 foot, Vector3 toPlayer)
        {
            if (Application.isBatchMode) return;
            var ep = new ParticleSystem.EmitParams { position = foot, applyShapeToPosition = true };
            dust.Emit(ep, 46); rubble.Emit(ep, 22);
            float dist = toPlayer.magnitude; Shake(Mathf.Clamp01(1.4f - dist / 40f) * 0.9f);
        }

        public void Swipe(Vector3 hand)
        {
            if (Application.isBatchMode) return;
            var ep = new ParticleSystem.EmitParams { position = hand, applyShapeToPosition = true };
            dust.Emit(ep, 10);
        }

        public void Step(float distToPlayer) { Shake(Mathf.Clamp01(1f - distToPlayer / 60f) * 0.22f); }

        public void Death()
        {
            if (Application.isBatchMode) return;
            var c = transform.position + Vector3.up * height * 0.5f;
            for (int i = 0; i < 12; i++)
            {
                var ep = new ParticleSystem.EmitParams { position = c + Random.insideUnitSphere * height * 0.35f, applyShapeToPosition = true };
                steam.Emit(ep, 8);
            }
            plumeWant = 2.5f; Shake(0.8f);
        }

        void Update()
        {
            if (Application.isBatchMode) return;
            plume = Mathf.MoveTowards(plume, plumeWant, Time.deltaTime * 0.8f);
            if (plume > 0.02f && Time.frameCount % 3 == 0)
            {
                var ep = new ParticleSystem.EmitParams { position = NapePos(), applyShapeToPosition = true };
                steam.Emit(ep, Mathf.CeilToInt(plume * 1.5f));
            }
            if (steamSrc != null) { steamSrc.transform.position = transform.position; steamSrc.volume = Mathf.Clamp01(plume * 0.35f); }
        }

        void OnDestroy() { foreach (var p in new[] { steam, dust, rubble, sparks }) if (p != null) Destroy(p.gameObject); }
    }
}
