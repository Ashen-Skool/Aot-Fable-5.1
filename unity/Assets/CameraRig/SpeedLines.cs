using Shared;
using UnityEngine;

namespace AotCamera
{
    /// <summary>
    /// Screen-space radial speed lines: an additive quad glued to the camera's near plane
    /// with a handful of procedurally drawn radial-streak textures cycled a few times a
    /// second (anime flicker). Intensity 0 hides it. No shader assets: uses the shipped
    /// Unlit base material with its blend state overridden to One/One.
    /// </summary>
    public class SpeedLines : MonoBehaviour
    {
        public const int Size = 1024;
        public const int Frames = 4;
        public int streaks = 170;
        public float planeDistance = 0.5f;
        public int framesPerCycle = 3;

        Texture2D[] frames;
        Material mat;
        MeshRenderer mr;
        Transform quad;
        float intensity;
        float burst;
        Material vigMat;
        MeshRenderer vigMr;
        Transform vigQuad;
        float vignette, vignetteTarget;
        float rotJitter;
        int frameCursor;
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public float Intensity => intensity;
        public float Vignette => vignette;
        public bool Visible => mr != null && mr.enabled;

        public static SpeedLines Create(Camera cam, int seed)
        {
            var go = new GameObject("SpeedLines");
            go.transform.SetParent(cam.transform, false);
            var sl = go.AddComponent<SpeedLines>();
            sl.Build(seed);
            return sl;
        }

        void Build(int seed)
        {
            frames = new Texture2D[Frames];
            var px = new Color32[Size * Size];
            var rng = new System.Random(seed * 31 + 7);
            for (int f = 0; f < Frames; f++) frames[f] = DrawFrame(rng, px);

            var q = new GameObject("Quad");
            quad = q.transform;
            quad.SetParent(transform, false);
            quad.localPosition = new Vector3(0, 0, planeDistance);
            var mf = q.AddComponent<MeshFilter>();
            mf.sharedMesh = UnitQuad();
            mr = q.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            mat = Mats.Unlit(Color.white);
            mat.name = "SpeedLinesAdditive";
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 1f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            mat.renderQueue = 3100;
            mat.SetTexture(BaseMap, frames[0]);
            mat.mainTexture = frames[0];
            mr.sharedMaterial = mat;
            mr.enabled = false;
            BuildVignette();
        }

        /// <summary>Dark alpha-blended ring on the near plane: the slow-motion kill-cam look.</summary>
        void BuildVignette()
        {
            const int S = 256;
            var px = new Color32[S * S];
            float R = S * 0.5f;
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f - R) / R, dy = (y + 0.5f - R) / R;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.82f, r));
                px[y * S + x] = new Color32(0, 0, 0, (byte)(a * 255f));
            }
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { name = "Vignette", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px);
            tex.Apply(false, true);

            var q = new GameObject("Vignette");
            vigQuad = q.transform;
            vigQuad.SetParent(transform, false);
            vigQuad.localPosition = new Vector3(0, 0, planeDistance + 0.02f);
            q.AddComponent<MeshFilter>().sharedMesh = UnitQuad();
            vigMr = q.AddComponent<MeshRenderer>();
            vigMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            vigMr.receiveShadows = false;
            vigMr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            vigMr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            vigMr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            vigMat = Mats.Unlit(Color.black);
            vigMat.name = "VignetteAlpha";
            vigMat.SetOverrideTag("RenderType", "Transparent");
            vigMat.SetFloat("_Surface", 1f);
            vigMat.SetFloat("_Blend", 0f);
            vigMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            vigMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            vigMat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            vigMat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            vigMat.SetFloat("_ZWrite", 0f);
            vigMat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            vigMat.renderQueue = 3090;   // under the additive lines
            vigMat.SetTexture(BaseMap, tex);
            vigMat.mainTexture = tex;
            vigMr.sharedMaterial = vigMat;
            vigMr.enabled = false;
        }

        /// <summary>Target vignette strength 0..1; eased in Tick.</summary>
        public void SetVignette(float amount) => vignetteTarget = Mathf.Clamp01(amount);

        static Mesh UnitQuad()
        {
            var m = new Mesh { name = "SpeedLinesQuad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 100f);
            return m;
        }

        Texture2D DrawFrame(System.Random rng, Color32[] px)
        {
            int n = streaks;
            var center = new float[n]; var inner = new float[n]; var width = new float[n]; var gain = new float[n];
            float step = Mathf.PI * 2f / n;
            float R = Size * 0.5f;
            for (int k = 0; k < n; k++)
            {
                center[k] = (k + 0.5f) * step + ((float)rng.NextDouble() - 0.5f) * step * 0.9f;
                inner[k] = R * (0.26f + 0.55f * (float)rng.NextDouble());
                width[k] = 1.1f + 2.6f * (float)rng.NextDouble();
                gain[k] = 0.35f + 0.65f * (float)rng.NextDouble();
                if (rng.NextDouble() < 0.25) gain[k] *= 0.3f; // thin faint filler streaks
            }
            for (int y = 0; y < Size; y++)
            {
                float dy = y - R + 0.5f;
                for (int x = 0; x < Size; x++)
                {
                    float dx = x - R + 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float th = Mathf.Atan2(dy, dx); if (th < 0) th += Mathf.PI * 2f;
                    int k0 = (int)(th / step);
                    float v = 0f;
                    for (int j = -1; j <= 1; j++)
                    {
                        int k = (k0 + j + n) % n;
                        if (r < inner[k]) continue;
                        float d = th - center[k];
                        if (d > Mathf.PI) d -= Mathf.PI * 2f; else if (d < -Mathf.PI) d += Mathf.PI * 2f;
                        float arc = Mathf.Abs(d) * r;
                        float a = 1f - arc / width[k];
                        if (a <= 0f) continue;
                        float fadeIn = Mathf.Clamp01((r - inner[k]) / (R * 0.22f));
                        float val = a * a * fadeIn * gain[k];
                        if (val > v) v = val;
                    }
                    byte b = (byte)(Mathf.Clamp01(v) * 255f);
                    px[y * Size + x] = new Color32(b, b, b, 255);
                }
            }
            var tex = new Texture2D(Size, Size, TextureFormat.RGB24, true) { name = "SpeedLines", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>Instant bright flash of lines that decays on its own (kill cam, big hits).</summary>
        public void Burst(float amount) => burst = Mathf.Max(burst, amount);

        /// <summary>Called by the rig every LateUpdate. intensity 0..1 = steady lines from speed; udt = unscaled dt.</summary>
        public void Tick(Camera cam, float steady, float udt)
        {
            // fit the quads to the frustum at planeDistance, generous so aspect changes never expose an edge
            float halfH = planeDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float aspect = Mathf.Max(cam.aspect, 16f / 9f);
            float h = halfH * 2f * 1.45f;                       // margin: the quad is rotated a little and must never show an edge
            quad.localScale = new Vector3(h * aspect, h, 1f);
            quad.localPosition = new Vector3(0, 0, planeDistance);

            vignette = Mathf.MoveTowards(vignette, vignetteTarget, udt * (vignetteTarget > vignette ? 4f : 2.5f));
            bool showVig = vignette > 0.01f;
            if (vigMr.enabled != showVig) vigMr.enabled = showVig;
            if (showVig)
            {
                float vh = halfH * 2f * 1.3f;                   // same over-cover as the lines: batch mode reports an odd aspect
                vigQuad.localScale = new Vector3(vh * aspect, vh, 1f);
                vigQuad.localPosition = new Vector3(0, 0, planeDistance + 0.02f);
                vigMat.SetColor(BaseColor, new Color(0f, 0f, 0f, vignette));
            }

            burst = Mathf.Max(0f, burst - udt * 1.8f);
            intensity = Mathf.Clamp(steady + burst, 0f, 1.4f);
            if (intensity < 0.01f) { if (mr.enabled) mr.enabled = false; return; }
            if (!mr.enabled) mr.enabled = true;

            if (Time.frameCount % framesPerCycle == 0)
            {
                frameCursor = (frameCursor + 1) % Frames;
                mat.SetTexture(BaseMap, frames[frameCursor]);
                rotJitter = ((Time.frameCount * 37) % 11 - 5) * 0.6f;
            }
            quad.localRotation = Quaternion.Euler(0, 0, rotJitter);
            float g = Mathf.Min(1.2f, intensity);
            mat.SetColor(BaseColor, new Color(g, g, g, 1f));
        }
    }
}
