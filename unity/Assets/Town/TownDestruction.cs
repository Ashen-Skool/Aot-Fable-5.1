using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Town
{
    /// <summary>
    /// Houses are batched per (spatial cell, material), so a house is not a GameObject you can hide:
    /// it is a contiguous vertex span inside a handful of shared meshes. Crushing one writes those
    /// spans down into a broken pile over <see cref="Duration"/> seconds, leaves a stub of the ground
    /// floor standing, drops the colliders to the pile, and throws dust and debris. The ridge and the
    /// upper storeys fall furthest; the footing barely moves.
    ///
    /// Original normals are kept through the collapse on purpose: the pile shuffles vertices past each
    /// other, so recalculating would flip half the quads to black.
    ///
    /// Built by TownBuilder, registered in Ctx as "town.destruction".
    /// </summary>
    public class TownDestruction : MonoBehaviour, ICrush
    {
        public const float Duration = 1.05f;
        public const float PileHeight = 2.4f;
        /// <summary>Below this the wall is a stub that stays: a crushed house is rubble around a broken ground floor, not a pancake.</summary>
        public const float StubHeight = 1.7f;

        /// <summary>One house's geometry as the builder hands it over, before meshes are resolved to indices.</summary>
        public class Parts
        {
            public HouseSpec spec;
            public GameObject colliders;
            public readonly List<Mesh> meshes = new List<Mesh>(16);
            public readonly List<int> starts = new List<int>(16);
            public readonly List<int> counts = new List<int>(16);
        }

        struct Span { public int mesh, start, count; }

        class House
        {
            public HouseSpec spec;
            public GameObject colliders;
            public Span[] spans;
            public Vector3 pos;
            public float top, reach;
            public int verts;
            public Vector3[] target;   // where every span vertex ends up, filled once when it goes down
            public bool down;
        }

        readonly List<Mesh> meshes = new List<Mesh>(64);
        Vector3[][] rest;    // the mesh as it was built
        Vector3[][] work;    // the mesh as it is now
        bool[] dirty;
        House[] houses;
        readonly List<int> falling = new List<int>(8);
        readonly List<float> fallT = new List<float>(8);
        ParticleSystem dust, debris;

        public int Crushed { get; private set; }
        public int Standing { get; private set; }
        public int Count => houses == null ? 0 : houses.Length;
        /// <summary>Houses keep the order TownLayout produced them in, so index i is L.houses[i].</summary>
        public bool Down(int i) => houses != null && i >= 0 && i < houses.Length && houses[i].down;

        // ---------------------------------------------------------------- build

        public void Init(List<Parts> parts)
        {
            var index = new Dictionary<Mesh, int>(64);
            houses = new House[parts.Count];
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                var spans = new Span[p.meshes.Count];
                int verts = 0;
                for (int s = 0; s < spans.Length; s++)
                {
                    var m = p.meshes[s];
                    if (!index.TryGetValue(m, out int mi)) { mi = meshes.Count; index[m] = mi; meshes.Add(m); m.MarkDynamic(); }
                    spans[s] = new Span { mesh = mi, start = p.starts[s], count = p.counts[s] };
                    verts += p.counts[s];
                }
                var h = p.spec;
                houses[i] = new House
                {
                    spec = h, colliders = p.colliders, spans = spans, verts = verts,
                    pos = h.pos, top = h.RidgeY, reach = Mathf.Max(h.w, h.d) * 0.5f,
                };
            }
            rest = new Vector3[meshes.Count][];
            work = new Vector3[meshes.Count][];
            dirty = new bool[meshes.Count];
            Standing = houses.Length;
        }

        /// <summary>The mesh vertex arrays are only paged in when a house that lives in that mesh first goes down.</summary>
        void Page(int i)
        {
            if (rest[i] != null) return;
            rest[i] = meshes[i].vertices;
            work[i] = (Vector3[])rest[i].Clone();
        }

        // ---------------------------------------------------------------- crushing

        public bool CrushNear(Vector3 p, float radius, Vector3 dir)
        {
            if (houses == null) return false;
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < houses.Length; i++)
            {
                var h = houses[i];
                if (h.down) continue;
                float dx = h.pos.x - p.x, dz = h.pos.z - p.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz) - h.reach;
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0 || bestD > radius) return false;
            Fell(best, dir);
            return true;
        }

        /// <summary>Bring down one house by index. False when it is already down.</summary>
        public bool Crush(int i, Vector3 dir)
        {
            if (houses == null || i < 0 || i >= houses.Length || houses[i].down) return false;
            Fell(i, dir);
            return true;
        }

        void Fell(int i, Vector3 dir)
        {
            var h = houses[i];
            h.down = true; Crushed++; Standing--;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;

            h.target = new Vector3[h.verts];
            int w = 0;
            foreach (var s in h.spans)
            {
                Page(s.mesh);
                var r = rest[s.mesh];
                int end = s.start + s.count;
                for (int v = s.start; v < end; v++) h.target[w++] = Rubble(r[v], h, dir);
            }
            falling.Add(i); fallT.Add(0f);
            Drop(h);
            Burst(h, dir);
        }

        /// <summary>Where one vertex of a standing house ends up in the pile.</summary>
        static Vector3 Rubble(Vector3 v, House h, Vector3 dir)
        {
            Vector3 b = h.pos;
            float y = v.y - b.y;
            float f = Mathf.Clamp01(y / Mathf.Max(1f, h.top));            // 0 at the footing, 1 at the ridge
            uint hs = Hash(v);
            float j0 = F(hs, 0), j1 = F(hs, 8), j2 = F(hs, 16), j3 = F(hs, 24);

            Vector3 outward = new Vector3(v.x - b.x, 0f, v.z - b.z);
            float rad = outward.magnitude;
            outward = rad > 0.01f ? outward / rad : dir;

            // heap it: highest where the house stood, tailing off at the edges, so it reads as a mound of
            // masonry instead of the roof planes laid flat on the street
            float mound = 1f - 0.55f * Mathf.Clamp01(rad / Mathf.Max(2f, h.reach));
            var t = v;
            t.y = b.y + PileHeight * mound * (0.2f + 0.95f * j1) * (1f - 0.3f * f);
            float thrown = f * (1.1f + 2.1f * j0);
            t += outward * (thrown * 0.5f) + dir * (thrown * 0.8f);
            t.x += (j2 - 0.5f) * 1.5f * f;
            t.z += (j3 - 0.5f) * 1.5f * f;

            // the ground floor is a stub, not a pancake: the closer a vertex was to the footing, the less it moves
            float keep = Mathf.Clamp01(1f - y / StubHeight);
            return keep > 0f ? Vector3.Lerp(t, v, keep * 0.88f) : t;
        }

        static uint Hash(Vector3 v)
        {
            unchecked
            {
                uint x = (uint)Mathf.RoundToInt(v.x * 13.7f), y = (uint)Mathf.RoundToInt(v.y * 11.3f), z = (uint)Mathf.RoundToInt(v.z * 9.1f);
                uint h = x * 73856093u ^ y * 19349663u ^ z * 83492791u;
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                return h;
            }
        }

        static float F(uint h, int shift) => ((h >> shift) & 255u) / 255f;

        /// <summary>Colliders follow the geometry down: the roof is gone, the body is a pile you can stand on.</summary>
        static void Drop(House h)
        {
            if (h.colliders == null) return;
            var box = h.colliders.GetComponent<BoxCollider>();
            if (box != null)
            {
                box.center = new Vector3(0f, PileHeight * 0.5f, 0f);
                box.size = new Vector3(h.spec.w + 1.6f, PileHeight, h.spec.d + 1.6f);
            }
            var mc = h.colliders.GetComponent<MeshCollider>();
            if (mc != null) mc.enabled = false;   // no roof left to walk on, and nothing to hook at ridge height
        }

        // ---------------------------------------------------------------- the fall

        void Update() => Step(Time.deltaTime);

        /// <summary>Advances every collapse in flight. Called from Update; the tests drive it directly.</summary>
        public void Step(float dt)
        {
            if (falling.Count == 0) return;
            for (int f = falling.Count - 1; f >= 0; f--)
            {
                var h = houses[falling[f]];
                float t = fallT[f] + dt; fallT[f] = t;
                float k = Mathf.Clamp01(t / Duration);
                k = k * k * (3f - 2f * k);   // it leans, then goes
                int w = 0;
                foreach (var s in h.spans)
                {
                    var r = rest[s.mesh]; var o = work[s.mesh];
                    int end = s.start + s.count;
                    for (int v = s.start; v < end; v++, w++)
                    {
                        var a = r[v]; var b = h.target[w];
                        o[v] = new Vector3(a.x + (b.x - a.x) * k, a.y + (b.y - a.y) * k, a.z + (b.z - a.z) * k);
                    }
                    dirty[s.mesh] = true;
                }
                if (t >= Duration)
                {
                    falling.RemoveAt(f); fallT.RemoveAt(f);
                    h.target = null;
                    foreach (var s in h.spans) meshes[s.mesh].RecalculateBounds();
                    Settle(h);
                }
            }
            for (int i = 0; i < dirty.Length; i++)
                if (dirty[i]) { meshes[i].SetVertices(work[i]); dirty[i] = false; }
        }

        void Settle(House h)
        {
            if (dust == null) return;
            var c = h.pos + Vector3.up * 0.6f;
            var ep = new ParticleSystem.EmitParams { position = c, applyShapeToPosition = true };
            dust.Emit(ep, 14);
        }

        // ---------------------------------------------------------------- dust and debris

        void Awake()
        {
            if (Application.isBatchMode) return;
            var soft = Resources.Load<Texture2D>("Particles/soft");
            var baseMat = Resources.Load<Material>("Materials/Particles");
            var puff = baseMat != null ? new Material(baseMat) : Mats.Unlit(Color.white);
            if (soft != null) { puff.mainTexture = soft; if (puff.HasProperty("_BaseMap")) puff.SetTexture("_BaseMap", soft); }

            dust = Sys("HouseDust", puff);
            {
                var m = dust.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.6f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(3f, 11f);
                m.startSize = new ParticleSystem.MinMaxCurve(3f, 7f);
                m.startColor = new Color(0.64f, 0.57f, 0.46f, 0.75f);
                m.gravityModifier = -0.04f;   // a collapse throws dust up and outward before it settles
                m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
                var col = dust.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0.55f, 0.35f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                var sz = dust.sizeOverLifetime; sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 2.4f)));
                var n = dust.noise; n.enabled = true; n.strength = 1.6f; n.frequency = 0.25f;
                var sh = dust.shape; sh.shapeType = ParticleSystemShapeType.Box;
            }

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var cubeMesh = cube.GetComponent<MeshFilter>().sharedMesh;
            Destroy(cube);
            debris = Sys("HouseDebris", Mats.Lit(new Color(0.34f, 0.29f, 0.23f), 0.03f));   // warm stone: the cool sky fill made pale cubes read as ice
            {
                var m = debris.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(4f, 13f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.75f);
                m.gravityModifier = 1.2f;
                m.startRotation3D = true;
                m.startRotationX = new ParticleSystem.MinMaxCurve(0f, 6.28f);
                m.startRotationY = new ParticleSystem.MinMaxCurve(0f, 6.28f);
                m.startRotationZ = new ParticleSystem.MinMaxCurve(0f, 6.28f);
                var rot = debris.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;
                rot.x = new ParticleSystem.MinMaxCurve(-5f, 5f); rot.y = new ParticleSystem.MinMaxCurve(-5f, 5f); rot.z = new ParticleSystem.MinMaxCurve(-5f, 5f);
                var col = debris.collision; col.enabled = true; col.type = ParticleSystemCollisionType.World;
                col.bounce = 0.15f; col.dampen = 0.55f; col.lifetimeLoss = 0.05f;
                var r = debris.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = cubeMesh;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                var sh = debris.shape; sh.shapeType = ParticleSystemShapeType.Box;
            }
        }

        ParticleSystem Sys(string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false; main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1200;
            var em = ps.emission; em.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        void Burst(House h, Vector3 dir)
        {
            Sfx.Play("titan_step", h.pos + Vector3.up * 2f, 0.45f, 1f, 220f);
            Shake(h.pos);
            if (dust == null || debris == null) return;
            var ds = dust.shape; ds.scale = new Vector3(h.spec.w, 1f, h.spec.d);
            var bs = debris.shape; bs.scale = new Vector3(h.spec.w, 1f, h.spec.d);
            // the dust goes up the whole height of what used to be there, thickest at the street
            for (int i = 0; i < 5; i++)
            {
                float y = 0.8f + h.top * (i / 5f);
                var ep = new ParticleSystem.EmitParams
                {
                    position = h.pos + Vector3.up * y + dir * (i * 0.6f),
                    applyShapeToPosition = true,
                };
                dust.Emit(ep, i == 0 ? 34 : 18);
            }
            var dp = new ParticleSystem.EmitParams { position = h.pos + Vector3.up * (h.top * 0.45f), applyShapeToPosition = true };
            debris.Emit(dp, 40);
        }

        static void Shake(Vector3 at)
        {
            var rig = Ctx.Get<Component>("cameraRig");
            if (rig == null) return;
            var cam = Ctx.Get<Camera>("camera");
            float d = cam != null ? Vector3.Distance(cam.transform.position, at) : 0f;
            float amount = Mathf.Clamp01(1f - d / 70f) * 0.7f;
            if (amount > 0.01f) rig.SendMessage("Shake", amount, SendMessageOptions.DontRequireReceiver);
        }
    }
}
