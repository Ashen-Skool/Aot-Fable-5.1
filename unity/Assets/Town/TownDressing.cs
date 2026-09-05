using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Town
{
    /// <summary>
    /// The second layer of the district, added after the houses and wall exist: hills and a treeline beyond the fog,
    /// gutters, puddles, dirt and hay in the streets, hanging shop signs, weathervanes and pigeons on the roofs,
    /// lamps that glow, torches and a portcullis at the gate. Deterministic for the seed.
    /// </summary>
    public static class TownDressing
    {
        public static void Build(TownLayout L, TownInfo info, Transform parent, TownMaterials mats)
        {
            var root = new GameObject("Dressing").transform; root.SetParent(parent, false);
            var rng = new System.Random(L.seed * 17 + 3);
            var g = new Group();
            Outskirts(L, info, root, mats, rng);
            Streets(L, info, g, mats, rng);
            Signs(L, g, mats, rng);
            Roofs(info, g, mats, rng);
            Gate(L, root, g, mats);
            Lamps(L, root, g, mats);
            g.Emit(root, "DressingMesh", 0);
        }

        /// <summary>One MeshKit per material, emitted as one renderer each.</summary>
        class Group
        {
            readonly Dictionary<Material, MeshKit> kits = new Dictionary<Material, MeshKit>();
            public MeshKit Get(Material m) { if (!kits.TryGetValue(m, out var k)) { k = new MeshKit(); kits[m] = k; } return k; }
            public void Emit(Transform parent, string name, int layer)
            {
                foreach (var kv in kits)
                {
                    var go = new GameObject(name + "_" + kv.Key.name); go.transform.SetParent(parent, false); go.layer = layer;
                    go.AddComponent<MeshFilter>().sharedMesh = kv.Value.Build(name);
                    var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = kv.Key;
                }
            }
        }

        static float R(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        // ---------------------------------------------------------------- outskirts
        static void Outskirts(TownLayout L, TownInfo info, Transform root, TownMaterials mats, System.Random rng)
        {
            var b = info.bounds;
            // a wide grass floor under everything, a step below the town's paving so it never z-fights
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.name = "Outskirts_Floor"; floor.transform.SetParent(root, false);
            floor.transform.position = new Vector3(b.center.x, -0.08f, b.center.z); floor.transform.localScale = new Vector3(160f, 1f, 160f);
            var floorMat = mats.Textured("grassFloor", "Ground103", 9f, new Color(0.2f, 0.24f, 0.14f), 0.05f, 0.8f);
            var floorScale = new Vector2(1600f / 9f, 1600f / 9f);   // the plane's UVs span 0..1 over 1600 m: tile every 9 m
            floorMat.SetTextureScale("_BaseMap", floorScale); floorMat.SetTextureScale("_BumpMap", floorScale);
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;
            Object.Destroy(floor.GetComponent<Collider>());
            // ring of hills: a radial mesh from just outside the boundary out to the horizon, rising with noise and distance
            const int rings = 14, segs = 72;
            float r0 = Mathf.Max(b.extents.x, b.extents.z) + 24f, r1 = 460f;
            var verts = new List<Vector3>((rings + 1) * (segs + 1)); var uvs = new List<Vector2>(); var tris = new List<int>();
            float nx = (float)rng.NextDouble() * 100f, nz = (float)rng.NextDouble() * 100f;
            for (int i = 0; i <= rings; i++)
            {
                float t = i / (float)rings; float r = Mathf.Lerp(r0, r1, t);
                for (int j = 0; j <= segs; j++)
                {
                    float a = j / (float)segs * Mathf.PI * 2f;
                    float x = b.center.x + Mathf.Cos(a) * r, z = b.center.z + Mathf.Sin(a) * r;
                    float n = Mathf.PerlinNoise(nx + x * 0.004f, nz + z * 0.004f) * 0.7f + Mathf.PerlinNoise(nx + x * 0.015f, nz + z * 0.015f) * 0.3f;
                    float rise = i == 0 ? -0.1f : Mathf.Lerp(0f, 150f, Mathf.Pow(t, 0.85f)) * (0.3f + 1.1f * n) + (i == 1 ? 0f : 3f * n);
                    if (z > L.wallZ1 - 40f && i < 4) rise = Mathf.Min(rise, 2f);   // flat outside the gate
                    verts.Add(new Vector3(x, rise, z)); uvs.Add(new Vector2(x / 14f, z / 14f));   // 1 UV unit per texture tile
                }
            }
            for (int i = 0; i < rings; i++) for (int j = 0; j < segs; j++)
            {
                int a = i * (segs + 1) + j, c = a + segs + 1;
                tris.Add(a); tris.Add(c); tris.Add(a + 1); tris.Add(a + 1); tris.Add(c); tris.Add(c + 1);
            }
            var mesh = new Mesh { name = "Hills", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var hills = new GameObject("Hills"); hills.transform.SetParent(root, false);
            hills.AddComponent<MeshFilter>().sharedMesh = mesh;
            hills.AddComponent<MeshRenderer>().sharedMaterial = mats.Textured("hills", "Ground103", 1f, new Color(0.15f, 0.2f, 0.13f), 0.04f, 0.6f);
            // treeline: dark cones on the first hill rings, merged into one mesh
            var kTree = new MeshKit(); var kTrunk = new MeshKit();
            int placed = 0;
            for (int n = 0; n < 900 && placed < 420; n++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f; float r = r0 + 4f + (float)rng.NextDouble() * 160f;
                float x = b.center.x + Mathf.Cos(a) * r, z = b.center.z + Mathf.Sin(a) * r;
                if (z > L.wallZ0 - 8f) continue;
                float y = SampleHills(mesh, b.center, r0, r1, rings, segs, x, z);
                float h = R(rng, 7f, 13f), w = h * R(rng, 0.28f, 0.4f);
                kTree.Cylinder(new Vector3(x, y + h * 0.15f, z), w, h * 0.85f, 6, Quaternion.identity, true, 0.05f);
                kTree.Cylinder(new Vector3(x, y + h * 0.45f, z), w * 0.75f, h * 0.55f, 6, Quaternion.identity, true, 0.05f);
                kTrunk.Cylinder(new Vector3(x, y - 0.5f, z), w * 0.12f, h * 0.25f, 5);
                placed++;
            }
            var tgo = new GameObject("Treeline"); tgo.transform.SetParent(root, false);
            tgo.AddComponent<MeshFilter>().sharedMesh = kTree.Build("Treeline"); tgo.AddComponent<MeshRenderer>().sharedMaterial = mats.Plain("treeDark", new Color(0.06f, 0.11f, 0.08f), 0.02f);
            var trgo = new GameObject("Trunks"); trgo.transform.SetParent(root, false);
            trgo.AddComponent<MeshFilter>().sharedMesh = kTrunk.Build("Trunks"); trgo.AddComponent<MeshRenderer>().sharedMaterial = mats.Plain("trunk", new Color(0.22f, 0.16f, 0.11f), 0.05f);
        }

        static float SampleHills(Mesh m, Vector3 c, float r0, float r1, int rings, int segs, float x, float z)
        {
            float r = Vector2.Distance(new Vector2(x, z), new Vector2(c.x, c.z));
            float t = Mathf.Clamp01((r - r0) / (r1 - r0));
            int i = Mathf.Clamp(Mathf.RoundToInt(t * rings), 0, rings);
            float a = Mathf.Atan2(z - c.z, x - c.x); if (a < 0f) a += Mathf.PI * 2f;
            int j = Mathf.Clamp(Mathf.RoundToInt(a / (Mathf.PI * 2f) * segs), 0, segs);
            return m.vertices[i * (segs + 1) + j].y;
        }

        // ---------------------------------------------------------------- streets
        static void Streets(TownLayout L, TownInfo info, Group g, TownMaterials mats, System.Random rng)
        {
            var kGut = g.Get(mats.Textured("gutter", "PavingStones131", 1.6f, new Color(0.3f, 0.3f, 0.29f), 0.12f, 1f));   // a shade darker than the paving, matte
            var kPud = g.Get(mats.Plain("puddle", new Color(0.07f, 0.08f, 0.09f), 0.42f, 0f));   // a dark wet patch; a mirror just reads as a white disc from above
            var kDirt = g.Get(mats.Textured("dirtPatch", "Ground103", 2.2f, new Color(0.4f, 0.33f, 0.24f), 0.04f, 0.5f));
            var kHay = g.Get(mats.Straw);
            var kMoss = g.Get(mats.Textured("moss", "Ground103", 1.6f, new Color(0.34f, 0.42f, 0.22f), 0.05f, 0.6f));
            float zMin = TownLayout.RowMin[0], zMax = TownLayout.RowMax[TownLayout.RowMax.Length - 1];
            float xMax = TownLayout.ColMin[3] + TownLayout.ColWidth;
            const float gy = 0.02f;
            void Strip(Vector3 a, Vector3 b, float w, MeshKit k) { var d = (b - a).normalized; var s = Vector3.Cross(Vector3.up, d) * (w * 0.5f); k.Quad(a - s, b - s, b + s, a + s); }
            // main street gutter, broken by the square
            Strip(new Vector3(0, gy, zMin - 6f), new Vector3(0, gy, L.square.yMin), 0.4f, kGut);
            Strip(new Vector3(0, gy, L.square.yMax), new Vector3(0, gy, L.wallZ0 - 2f), 0.4f, kGut);
            // cross streets between the rows, side streets between the columns
            for (int r = 0; r < TownLayout.RowMin.Length - 1; r++)
            {
                float z = (TownLayout.RowMax[r] + TownLayout.RowMin[r + 1]) * 0.5f;
                Strip(new Vector3(-xMax, gy, z), new Vector3(xMax, gy, z), 0.35f, kGut);
            }
            for (int c = 0; c < TownLayout.ColMin.Length - 1; c++)
            {
                float x = (TownLayout.ColMin[c] + TownLayout.ColWidth + TownLayout.ColMin[c + 1]) * 0.5f;
                Strip(new Vector3(x, gy, zMin), new Vector3(x, gy, zMax), 0.35f, kGut);
                Strip(new Vector3(-x, gy, zMin), new Vector3(-x, gy, zMax), 0.35f, kGut);
            }
            // puddles, dirt, hay along the streets (never inside the square, never on the main axis where she runs)
            var streetX = new List<float> { 0f };
            for (int c = 0; c < TownLayout.ColMin.Length - 1; c++) { float x = (TownLayout.ColMin[c] + TownLayout.ColWidth + TownLayout.ColMin[c + 1]) * 0.5f; streetX.Add(x); streetX.Add(-x); }
            for (int i = 0; i < 70; i++)
            {
                bool cross = rng.NextDouble() < 0.4;
                float x, z;
                if (cross) { int r = rng.Next(TownLayout.RowMin.Length - 1); z = (TownLayout.RowMax[r] + TownLayout.RowMin[r + 1]) * 0.5f + R(rng, -2.5f, 2.5f); x = R(rng, -xMax, xMax); }
                else { x = streetX[rng.Next(streetX.Count)] + R(rng, -2.4f, 2.4f); z = R(rng, zMin, zMax); }
                if (L.square.Contains(new Vector2(x, z))) continue;
                double kind = rng.NextDouble();
                if (kind < 0.35) kPud.Cylinder(new Vector3(x, gy + 0.005f, z), R(rng, 0.7f, 1.8f), 0.01f, 12, Quaternion.Euler(0, R(rng, 0, 360), 0), true, R(rng, 0.5f, 1.4f));
                else if (kind < 0.8) kDirt.Cylinder(new Vector3(x, gy, z), R(rng, 1.2f, 3f), 0.012f, 10, Quaternion.Euler(0, R(rng, 0, 360), 0), true, R(rng, 0.8f, 2.4f));
                else if (kind < 0.9) kHay.Cylinder(new Vector3(x, 0f, z), R(rng, 0.7f, 1.1f), R(rng, 0.35f, 0.6f), 9, Quaternion.identity, true, 0.35f);
                else kMoss.Cylinder(new Vector3(x, gy, z), R(rng, 0.8f, 1.6f), 0.012f, 8, Quaternion.Euler(0, R(rng, 0, 360), 0), true, 0.6f);
            }
            // a moss and dirt band along the foot of the wall's inner face
            float X = L.wallHalfLength, z0 = L.wallZ0;
            kMoss.Quad(new Vector3(-X, 0.02f, z0 - 0.42f), new Vector3(-X, 2.6f, z0 - 0.42f), new Vector3(X, 2.6f, z0 - 0.42f), new Vector3(X, 0.02f, z0 - 0.42f));
            kDirt.Cylinder(new Vector3(-X, 0.03f, z0 - 1.5f), 0f, 0f, 3); // keeps the kit non-empty on odd seeds
            for (float x = -X + 3f; x < X; x += R(rng, 4f, 9f)) { if (Mathf.Abs(x) < L.gateHalfWidth + 4f) continue; kDirt.Cylinder(new Vector3(x, 0.025f, z0 - 1.6f), R(rng, 1.2f, 2.6f), 0.01f, 8, Quaternion.identity, true, R(rng, 0.8f, 2f)); }
        }

        // ---------------------------------------------------------------- shop signs
        static void Signs(TownLayout L, Group g, TownMaterials mats, System.Random rng)
        {
            var kIron = g.Get(mats.Iron);
            var boards = new[] { mats.Cloth(2), mats.Cloth(4), mats.TimberDark, mats.Cloth(5), mats.Shutter(1) };
            foreach (var h in L.houses)
            {
                if (rng.NextDouble() > 0.22) continue;
                var front = h.Front; var right = Vector3.Cross(Vector3.up, front);
                float depth = h.facing == Facing.PosZ || h.facing == Facing.NegZ ? h.d : h.w;
                var basePos = h.pos + front * (depth * 0.5f + 0.05f) + right * (h.w * R(rng, -0.3f, 0.3f)) + Vector3.up * (h.baseH + h.storeyH + 0.55f);
                var q = Quaternion.LookRotation(front, Vector3.up);
                // bracket: a bar out from the wall with a drop, the board hanging under it
                kIron.BoxRot(basePos + front * 0.45f, new Vector3(0.05f, 0.05f, 0.9f), q);
                kIron.BoxRot(basePos + front * 0.85f + Vector3.down * 0.18f, new Vector3(0.05f, 0.36f, 0.05f), q);
                var kb = g.Get(boards[rng.Next(boards.Length)]);
                kb.BoxRot(basePos + front * 0.85f + Vector3.down * 0.65f, new Vector3(0.06f, 0.6f, 0.8f), q);
                kIron.BoxRot(basePos + front * 0.85f + Vector3.down * 0.65f, new Vector3(0.09f, 0.66f, 0.06f), q);
            }
        }

        // ---------------------------------------------------------------- roofs
        static void Roofs(TownInfo info, Group g, TownMaterials mats, System.Random rng)
        {
            var kIron = g.Get(mats.Iron);
            var kBird = g.Get(mats.Plain("pigeon", new Color(0.5f, 0.5f, 0.55f), 0.1f));
            foreach (var c in info.chimneys)
            {
                if (rng.NextDouble() > 0.14) continue;
                var p = c + new Vector3(0.62f, -0.4f, 0.62f);   // beside the pot
                float yaw = R(rng, 0f, 360f); var q = Quaternion.Euler(0f, yaw, 0f);
                kIron.Cylinder(p, 0.025f, 1.5f, 6);
                kIron.BoxRot(p + Vector3.up * 1.45f, new Vector3(0.9f, 0.03f, 0.03f), q);                       // arrow shaft
                kIron.BoxRot(p + Vector3.up * 1.45f + q * Vector3.right * 0.45f, new Vector3(0.22f, 0.22f, 0.02f), q * Quaternion.Euler(0, 0, 45f)); // head
                kIron.BoxRot(p + Vector3.up * 1.45f - q * Vector3.right * 0.4f, new Vector3(0.14f, 0.3f, 0.02f), q); // tail
                kIron.BoxRot(p + Vector3.up * 1.1f, new Vector3(0.5f, 0.02f, 0.02f), Quaternion.identity);         // N-S
                kIron.BoxRot(p + Vector3.up * 1.1f, new Vector3(0.02f, 0.02f, 0.5f), Quaternion.identity);         // E-W
            }
            int roofs = 0;
            foreach (var r in info.rooftops)
            {
                if (roofs++ % 5 != 0 || rng.NextDouble() > 0.6) continue;
                int n = rng.Next(1, 4);
                for (int i = 0; i < n; i++)
                {
                    var p = r + new Vector3(R(rng, -1.5f, 1.5f), 0.02f, R(rng, -1.5f, 1.5f));
                    var q = Quaternion.Euler(0f, R(rng, 0f, 360f), 0f);
                    kBird.BoxRot(p + Vector3.up * 0.11f, new Vector3(0.16f, 0.16f, 0.3f), q);
                    kBird.BoxRot(p + Vector3.up * 0.24f + q * Vector3.forward * 0.16f, new Vector3(0.09f, 0.09f, 0.1f), q);
                }
            }
        }

        // ---------------------------------------------------------------- gate
        static void Gate(TownLayout L, Transform root, Group g, TownMaterials mats)
        {
            var kIron = g.Get(mats.Iron);
            float gw = L.gateHalfWidth, gtop = L.gateRectHeight + L.gateArchRadius, z = L.wallZ0 + 1.4f;
            // portcullis just inside the arch, raised a third so the gate reads as open for the gate crew
            float lift = gtop * 0.34f;
            for (float x = -gw + 0.45f; x < gw; x += 0.9f) kIron.Box(new Vector3(x - 0.07f, lift, z - 0.07f), new Vector3(x + 0.07f, gtop + 0.4f, z + 0.07f));
            for (float y = lift + 0.4f; y < gtop; y += 1.8f) kIron.Box(new Vector3(-gw, y - 0.06f, z - 0.09f), new Vector3(gw, y + 0.06f, z + 0.09f));
            // torches either side of the gate frame
            foreach (float side in new[] { -1f, 1f })
            {
                var p = new Vector3(side * (gw + 1.2f), 7.5f, L.wallZ0 - 0.9f);
                kIron.BoxRot(p, new Vector3(0.08f, 0.08f, 0.6f), Quaternion.Euler(-35f, 0f, 0f));
                kIron.Cylinder(p + new Vector3(0f, 0.15f, -0.35f), 0.12f, 0.35f, 8);
                Torch(root, p + new Vector3(0f, 0.55f, -0.35f));
            }
        }

        static Material flameMat;
        public static void Torch(Transform root, Vector3 at)
        {
            var go = new GameObject("Torch"); go.transform.SetParent(root, false); go.transform.position = at;
            var light = go.AddComponent<Light>(); light.type = LightType.Point; light.range = 11f; light.intensity = 3.2f; light.color = new Color(1f, 0.62f, 0.3f); light.shadows = LightShadows.None;
            go.AddComponent<Flicker>().baseIntensity = light.intensity;
            if (Application.isBatchMode) return;
            if (flameMat == null)
            {
                var b = Resources.Load<Material>("Materials/Particles");
                flameMat = b != null ? new Material(b) : Mats.Unlit(Color.white);
                var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) { float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(16f, 16f)) / 16f; float a = Mathf.Clamp01(1f - d); tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a)); }
                tex.Apply(); flameMat.mainTexture = tex; if (flameMat.HasProperty("_BaseMap")) flameMat.SetTexture("_BaseMap", tex);
            }
            var ps = go.AddComponent<ParticleSystem>();
            var m = ps.main; m.simulationSpace = ParticleSystemSimulationSpace.World; m.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f); m.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.6f); m.startColor = new Color(1f, 0.55f, 0.15f, 0.9f); m.maxParticles = 40;
            var em = ps.emission; em.rateOverTime = 26f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12f; sh.radius = 0.08f;
            var col = ps.colorOverLifetime; col.enabled = true; var gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.4f), 0f), new GradientColorKey(new Color(1f, 0.35f, 0.08f), 0.5f), new GradientColorKey(new Color(0.3f, 0.1f, 0.05f), 1f) },
                       new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f)));
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = flameMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        }

        class Flicker : MonoBehaviour
        {
            public float baseIntensity = 3f; Light l; float seed;
            void Start() { l = GetComponent<Light>(); seed = Random.value * 100f; }
            void Update() { if (l != null) l.intensity = baseIntensity * (0.8f + 0.35f * Mathf.PerlinNoise(seed, Time.time * 9f)); }
        }

        // ---------------------------------------------------------------- lamps
        static void Lamps(TownLayout L, Transform root, Group g, TownMaterials mats)
        {
            var kGlow = g.Get(mats.GlassLit);
            foreach (var p in L.props)
            {
                if (p.kind != PropKind.Lamp) continue;
                var at = p.pos + Vector3.up * 3.68f;
                kGlow.BoxC(at, new Vector3(0.33f, 0.42f, 0.33f));
                var go = new GameObject("LampLight"); go.transform.SetParent(root, false); go.transform.position = at;
                var light = go.AddComponent<Light>(); light.type = LightType.Point; light.range = 10f; light.intensity = 2.4f; light.color = new Color(1f, 0.72f, 0.4f); light.shadows = LightShadows.None;
            }
        }
    }
}
