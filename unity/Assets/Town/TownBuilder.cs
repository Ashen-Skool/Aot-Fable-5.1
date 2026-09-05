using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Town
{
    /// <summary>
    /// Turns a TownLayout into GameObjects: one combined mesh per (spatial cell, material) for the
    /// houses, one per material for the wall and the props, plus per-house colliders on the
    /// HookTarget layer (box body + convex roof mesh, so rooftops are walkable and hookable).
    /// Deterministic given the layout. Registers "town", "town.bounds", "town.spawn", "town.gate",
    /// "town.rooftops", "town.hookLayer", "town.root" in Ctx.
    /// </summary>
    public class TownBuilder
    {
        class Group
        {
            public readonly Dictionary<Material, MeshKit> kits = new Dictionary<Material, MeshKit>(24);
            public MeshKit Get(Material m, Matrix4x4 xf) => Get(m, xf, Vector2.zero);
            public MeshKit Get(Material m, Matrix4x4 xf, Vector2 uv)
            {
                if (!kits.TryGetValue(m, out var k)) { k = new MeshKit(); kits[m] = k; }
                k.xf = xf;
                k.uvOffset = uv;
                return k;
            }
        }

        readonly TownMaterials mats;
        readonly TownLayout L;
        readonly TownInfo info = new TownInfo();
        readonly Dictionary<long, Group> cells = new Dictionary<long, Group>(64);
        readonly Group wallGroup = new Group(), propGroup = new Group();
        readonly MeshKit colliderKit = new MeshKit();
        Transform housesRoot, propsRoot;
        const float CellSize = 56f;
        static readonly Vector3 Up = Vector3.up;

        TownBuilder(TownLayout layout, TownMaterials materials) { L = layout; mats = materials; }

        public static TownInfo Build(TownLayout layout, Transform parent, TownMaterials materials)
        {
            var b = new TownBuilder(layout, materials);
            return b.Run(parent);
        }

        TownInfo Run(Transform parent)
        {
            info.root = parent.gameObject;
            info.hookLayer = Layers.HookTarget;
            info.bounds = L.bounds;
            info.spawn = L.spawn;
            info.gate = L.gate;
            info.square = L.square;
            info.wallHeight = L.wallHeight;
            info.wallZ = L.wallZ0;
            housesRoot = new GameObject("Houses").transform; housesRoot.SetParent(parent, false);
            propsRoot = new GameObject("Props").transform; propsRoot.SetParent(parent, false);

            foreach (var h in L.houses) House(h);
            info.houseCount = L.houses.Count;
            Wall();
            Plaza();
            foreach (var p in L.props) Prop(p);

            foreach (var kv in cells) Emit(kv.Value, housesRoot, "Cell_" + kv.Key, info.hookLayer);
            Emit(wallGroup, parent, "WallMesh", info.hookLayer);
            Emit(propGroup, propsRoot, "PropMesh", 0);

            Ctx.Set("town", info);
            Ctx.Set("town.bounds", info.bounds);
            Ctx.Set("town.spawn", info.spawn);
            Ctx.Set("town.gate", info.gate);
            Ctx.Set("town.rooftops", info.rooftops.ToArray());
            Ctx.Set("town.hookLayer", info.hookLayer);
            Ctx.Set("town.root", info.root);
            return info;
        }

        Group Cell(Vector3 p)
        {
            int cx = Mathf.FloorToInt((p.x + 200f) / CellSize), cz = Mathf.FloorToInt((p.z + 200f) / CellSize);
            long key = ((long)cx << 20) | (uint)cz;
            if (!cells.TryGetValue(key, out var g)) { g = new Group(); cells[key] = g; }
            return g;
        }

        static void Emit(Group g, Transform parent, string name, int layer)
        {
            int i = 0;
            foreach (var kv in g.kits)
            {
                if (kv.Value.Empty) continue;
                var go = new GameObject(name + "_" + kv.Key.name + "_" + i++);
                go.transform.SetParent(parent, false);
                go.layer = layer;
                go.AddComponent<MeshFilter>().sharedMesh = kv.Value.Build(go.name);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = kv.Key;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                mr.receiveShadows = true;
            }
        }

        // ---------------------------------------------------------------- houses

        void House(HouseSpec h)
        {
            var xf = Matrix4x4.TRS(h.pos, Quaternion.Euler(0f, h.Yaw, 0f), Vector3.one);
            var g = Cell(h.pos);
            float hw = h.w * 0.5f, hd = h.d * 0.5f, top = h.WallTop, g0 = h.baseH + h.storeyH;
            float ov = h.Overhang, og = h.GableOverhang;
            var uv = h.uvOffset;
            var stone = mats.Stone(h.stoneSet, h.shade);
            var plaster = mats.Plaster(h.plasterTint, h.shade);
            var roof = mats.Roof(h.roofSet % TownMaterials.RoofSets.Length, h.roofSet / TownMaterials.RoofSets.Length);
            var kStone = g.Get(stone, xf, uv);
            var kPlaster = g.Get(plaster, xf, uv);
            var kRoof = g.Get(roof, xf, uv);
            var kTimber = g.Get(mats.TimberDark, xf, uv);
            var kPale = g.Get(mats.TimberPale, xf, uv);
            var kGlass = g.Get(mats.GlassFor(h), xf, uv);
            var kShut = g.Get(mats.Shutter(h.shutterTint), xf, uv);
            var kChim = g.Get(mats.Stone(0, h.shade), xf, uv);
            var kDark = g.Get(mats.Dark, xf, uv);
            var kPlinth = g.Get(mats.StoneDark, xf, uv);
            // darkened base course
            kPlinth.Box(new Vector3(-hw - 0.12f, 0f, -hd - 0.12f), new Vector3(hw + 0.12f, 0.75f, hd + 0.12f));

            // body
            if (h.wallStyle == 1)
            {
                kStone.Box(new Vector3(-hw, 0f, -hd), new Vector3(hw, top, hd));
            }
            else
            {
                kStone.Box(new Vector3(-hw - 0.06f, 0f, -hd - 0.06f), new Vector3(hw + 0.06f, g0, hd + 0.06f));
                kStone.Box(new Vector3(-hw - 0.14f, g0 - 0.16f, -hd - 0.14f), new Vector3(hw + 0.14f, g0, hd + 0.14f));
                kPlaster.Box(new Vector3(-hw, g0, -hd), new Vector3(hw, top, hd));
            }
            // cornice under the eaves
            kStone.Box(new Vector3(-hw - 0.16f, top - 0.32f, -hd - 0.16f), new Vector3(hw + 0.16f, top, hd + 0.16f));

            // roof
            Vector3 rmin, rmax;
            if (!h.gableFront) { rmin = new Vector3(-hw - og, 0, -hd - ov); rmax = new Vector3(hw + og, 0, hd + ov); }
            else { rmin = new Vector3(-hw - ov, 0, -hd - og); rmax = new Vector3(hw + ov, 0, hd + og); }
            float eave = top - 0.05f;
            kRoof.Gable(rmin, rmax, eave, h.RidgeY, !h.gableFront, true);
            // ridge cap
            if (!h.gableFront) kRoof.Box(new Vector3(rmin.x, h.RidgeY - 0.12f, -0.22f), new Vector3(rmax.x, h.RidgeY + 0.1f, 0.22f));
            else kRoof.Box(new Vector3(-0.22f, h.RidgeY - 0.12f, rmin.z), new Vector3(0.22f, h.RidgeY + 0.1f, rmax.z));
            // fascia board along the front eave
            if (!h.gableFront) kTimber.Box(new Vector3(rmin.x, eave - 0.3f, rmin.z - 0.02f), new Vector3(rmax.x, eave, rmin.z + 0.16f));
            else
            {
                // bargeboards on the street gable
                var q = Quaternion.LookRotation(Vector3.back, Vector3.up);
                float half = hw + ov, rise = h.RidgeY - eave;
                float len = Mathf.Sqrt(half * half + rise * rise);
                float ang = Mathf.Atan2(rise, half) * Mathf.Rad2Deg;
                kTimber.BoxRot(new Vector3(-half * 0.5f, eave + rise * 0.5f, rmin.z), new Vector3(len, 0.28f, 0.16f), Quaternion.Euler(0, 0, ang));
                kTimber.BoxRot(new Vector3(half * 0.5f, eave + rise * 0.5f, rmin.z), new Vector3(len, 0.28f, 0.16f), Quaternion.Euler(0, 0, -ang));
            }

            // openings: front, left, right
            Face(h, g, xf, Vector3.back, hd, h.w, true);
            Face(h, g, xf, Vector3.left, hw, h.d, false);
            Face(h, g, xf, Vector3.right, hw, h.d, false);

            // timber frame
            if (h.wallStyle == 2)
            {
                Frame(h, g.Get(mats.TimberDark, xf, uv), Vector3.back, hd, h.w);
                Frame(h, kTimber, Vector3.left, hw, h.d);
                Frame(h, kTimber, Vector3.right, hw, h.d);
            }

            // balcony on the first upper storey
            if (h.balcony)
            {
                float yb = h.baseH + h.storeyH;
                float bw = Mathf.Min(h.w * 0.6f, 4.5f);
                kTimber.Box(new Vector3(-bw * 0.5f, yb, -hd - 1.1f), new Vector3(bw * 0.5f, yb + 0.18f, -hd + 0.02f));
                int posts = 5;
                for (int i = 0; i < posts; i++)
                {
                    float px = -bw * 0.5f + 0.06f + i * (bw - 0.12f) / (posts - 1);
                    kPale.BoxC(new Vector3(px, yb + 0.68f, -hd - 1.04f), new Vector3(0.09f, 1.0f, 0.09f));
                }
                kPale.Box(new Vector3(-bw * 0.5f, yb + 1.12f, -hd - 1.1f), new Vector3(bw * 0.5f, yb + 1.2f, -hd - 0.98f));
                kPale.Box(new Vector3(-bw * 0.5f, yb + 1.12f, -hd - 1.1f), new Vector3(-bw * 0.5f + 0.1f, yb + 1.2f, -hd));
                kPale.Box(new Vector3(bw * 0.5f - 0.1f, yb + 1.12f, -hd - 1.1f), new Vector3(bw * 0.5f, yb + 1.2f, -hd));
                kTimber.BoxRot(new Vector3(-bw * 0.4f, yb - 0.45f, -hd - 0.5f), new Vector3(0.14f, 0.14f, 1.3f), Quaternion.Euler(-45f, 0, 0));
                kTimber.BoxRot(new Vector3(bw * 0.4f, yb - 0.45f, -hd - 0.5f), new Vector3(0.14f, 0.14f, 1.3f), Quaternion.Euler(-45f, 0, 0));
            }

            // chimneys
            for (int i = 0; i < h.chimneys; i++)
            {
                float along = i == 0 ? h.chimneyX0 : h.chimneyX1;
                float across = h.chimneyZ;
                float halfSpan = (h.gableFront ? hw : hd) + ov;
                float ys = top + h.Rise * (1f - Mathf.Abs(across) / halfSpan);
                Vector3 c = h.gableFront ? new Vector3(across, 0, along) : new Vector3(along, 0, across);
                kChim.Box(new Vector3(c.x - 0.45f, ys - 0.7f, c.z - 0.45f), new Vector3(c.x + 0.45f, h.RidgeY + 1.3f, c.z + 0.45f));
                kStone.Box(new Vector3(c.x - 0.6f, h.RidgeY + 1.3f, c.z - 0.6f), new Vector3(c.x + 0.6f, h.RidgeY + 1.52f, c.z + 0.6f));
                kDark.Cylinder(new Vector3(c.x, h.RidgeY + 1.5f, c.z), 0.2f, 0.55f, 8);
                info.chimneys.Add(xf.MultiplyPoint3x4(new Vector3(c.x, h.RidgeY + 2.05f, c.z)));
            }

            // dormer on the front slope
            if (h.dormer)
            {
                float t = 0.42f;
                float zs = -(hd + ov) * (1f - t);
                float ys = top + h.Rise * t;
                float dx = h.dormerX;
                kPlaster.Box(new Vector3(dx - 0.9f, ys - 1.3f, zs - 1.3f), new Vector3(dx + 0.9f, ys + 1.0f, zs + 0.9f));
                kRoof.Gable(new Vector3(dx - 1.1f, 0, zs - 1.5f), new Vector3(dx + 1.1f, 0, zs + 0.9f), ys + 0.95f, ys + 1.9f, false, true);
                Opening(g, xf, new Vector3(dx, ys - 0.55f, zs - 1.3f), Vector3.back, 0.8f, 1.0f, false, h, kPale, kGlass, kShut, kStone, kTimber);
            }

            // rooftop points along the ridge (world space)
            for (int i = 0; i < 3; i++)
            {
                float f = 0.2f + 0.3f * i;
                Vector3 p = !h.gableFront
                    ? new Vector3(Mathf.Lerp(rmin.x, rmax.x, f), h.RidgeY + 0.15f, 0f)
                    : new Vector3(0f, h.RidgeY + 0.15f, Mathf.Lerp(rmin.z, rmax.z, f));
                info.rooftops.Add(xf.MultiplyPoint3x4(p));
            }

            // colliders: body box + convex roof mesh, both hookable
            var go = new GameObject("House");
            go.transform.SetParent(housesRoot, false);
            go.transform.SetPositionAndRotation(h.pos, Quaternion.Euler(0f, h.Yaw, 0f));
            go.layer = info.hookLayer;
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, top * 0.5f, 0f);
            box.size = new Vector3(h.w, top, h.d);
            colliderKit.Clear();
            colliderKit.Gable(rmin, rmax, eave, h.RidgeY, !h.gableFront, true);
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = colliderKit.Build("roof");
            mc.convex = true;
        }

        /// <summary>Windows and the door on one face. outN is the outward normal in house-local space.</summary>
        void Face(HouseSpec h, Group g, Matrix4x4 xf, Vector3 outN, float halfDepth, float width, bool front)
        {
            var kPale = g.Get(mats.TimberPale, xf, h.uvOffset);
            var kGlass = g.Get(mats.GlassFor(h), xf, h.uvOffset);
            var kShut = g.Get(mats.Shutter(h.shutterTint), xf, h.uvOffset);
            var kStone = g.Get(mats.Stone(h.stoneSet, h.shade), xf, h.uvOffset);
            var kDoor = g.Get(mats.TimberDark, xf, h.uvOffset);
            var right = Vector3.Cross(outN, Vector3.up);
            int n = Mathf.Max(1, Mathf.RoundToInt(width / (front ? 2.6f : 3.2f)));
            for (int s = 0; s < h.storeys; s++)
            {
                float proud = (s == 0 && h.wallStyle != 1) ? 0.06f : 0f;
                float y0 = h.baseH + s * h.storeyH + (s == 0 ? 1.05f : 0.85f);
                for (int i = 0; i < n; i++)
                {
                    float a = -width * 0.5f + (i + 0.5f) * width / n;
                    var c = outN * (halfDepth + proud) + right * a;
                    if (front && s == 0 && i == h.doorSlot)
                    {
                        Opening(g, xf, c + Vector3.up * 0.02f, outN, 1.2f, 2.3f, true, h, kPale, kGlass, kShut, kStone, kDoor);
                        continue;
                    }
                    if (!front && s == 0 && (i % 2 == 1)) continue;
                    Opening(g, xf, c + Vector3.up * y0, outN, s == 0 ? 0.9f : 1.0f, s == 0 ? 1.25f : 1.4f, false, h, kPale, kGlass, kShut, kStone, kDoor);
                }
            }
        }

        static void Slab(MeshKit k, Vector3 c, Vector3 outN, float w, float hgt, float depth)
            => k.BoxRot(c, new Vector3(w, hgt, depth), Quaternion.LookRotation(outN, Vector3.up));

        /// <summary>One window (or door). c = bottom-centre of the opening on the wall surface.</summary>
        static void Opening(Group g, Matrix4x4 xf, Vector3 c, Vector3 outN, float ww, float wh, bool door, HouseSpec h,
            MeshKit kPale, MeshKit kGlass, MeshKit kShut, MeshKit kStone, MeshKit kDoor)
        {
            var right = Vector3.Cross(outN, Vector3.up);
            var mid = c + Vector3.up * (wh * 0.5f);
            if (door)
            {
                Slab(kDoor, mid + outN * 0.02f, outN, ww, wh, 0.1f);
                Slab(kStone, mid - right * (ww * 0.5f + 0.16f) + outN * 0.1f, outN, 0.32f, wh + 0.3f, 0.34f);
                Slab(kStone, mid + right * (ww * 0.5f + 0.16f) + outN * 0.1f, outN, 0.32f, wh + 0.3f, 0.34f);
                Slab(kStone, c + Vector3.up * (wh + 0.18f) + outN * 0.12f, outN, ww + 0.64f, 0.36f, 0.38f);
                Slab(kStone, c + Vector3.up * 0.08f + outN * 0.34f, outN, ww + 0.5f, 0.16f, 0.62f);
                // door planks + iron bands
                Slab(kDoor, mid + outN * 0.045f, outN, ww - 0.1f, wh - 0.1f, 0.03f);
                return;
            }
            // glass, nearly flush
            var a = c - right * (ww * 0.5f) + outN * 0.02f;
            kGlass.Quad(a, a + Vector3.up * wh, a + Vector3.up * wh + right * ww, a + right * ww);
            // stone reveal: jambs and lintel stand 0.22 m proud so the glass sits in a shadowed recess
            Slab(kStone, mid - right * (ww * 0.5f + 0.13f) + outN * 0.08f, outN, 0.26f, wh + 0.36f, 0.3f);
            Slab(kStone, mid + right * (ww * 0.5f + 0.13f) + outN * 0.08f, outN, 0.26f, wh + 0.36f, 0.3f);
            Slab(kStone, c + Vector3.up * (wh + 0.14f) + outN * 0.1f, outN, ww + 0.52f, 0.28f, 0.34f);
            // inner timber frame + mullion, flush with the glass
            Slab(kPale, mid - right * (ww * 0.5f) + outN * 0.03f, outN, 0.08f, wh + 0.08f, 0.06f);
            Slab(kPale, mid + right * (ww * 0.5f) + outN * 0.03f, outN, 0.08f, wh + 0.08f, 0.06f);
            Slab(kPale, c + Vector3.up * wh + outN * 0.03f, outN, ww + 0.08f, 0.08f, 0.06f);
            Slab(kPale, mid + outN * 0.03f, outN, 0.06f, wh, 0.06f);
            Slab(kPale, mid + outN * 0.03f, outN, ww, 0.06f, 0.06f);
            // deep sill
            Slab(kStone, c - Vector3.up * 0.07f + outN * 0.16f, outN, ww + 0.6f, 0.14f, 0.46f);
            if (h.shutters)
            {
                Slab(kShut, mid - right * (ww * 0.5f + 0.26f + 0.27f) + outN * 0.18f, outN, 0.52f, wh + 0.2f, 0.07f);
                Slab(kShut, mid + right * (ww * 0.5f + 0.26f + 0.27f) + outN * 0.18f, outN, 0.52f, wh + 0.2f, 0.07f);
            }
        }

        /// <summary>Half-timber beams on the upper storeys of one face.</summary>
        void Frame(HouseSpec h, MeshKit k, Vector3 outN, float halfDepth, float width)
        {
            var right = Vector3.Cross(outN, Vector3.up);
            float g0 = h.baseH + h.storeyH, top = h.WallTop;
            var surf = outN * (halfDepth + 0.06f);
            for (int s = 1; s < h.storeys; s++)
                Slab(k, surf + Vector3.up * (h.baseH + s * h.storeyH), outN, width + 0.14f, 0.26f, 0.24f);
            Slab(k, surf + Vector3.up * (top - 0.42f), outN, width + 0.14f, 0.22f, 0.24f);
            int n = Mathf.Max(1, Mathf.RoundToInt(width / 2.6f));
            float postH = top - 0.32f - g0;
            float yc = g0 + postH * 0.5f;
            Slab(k, surf + right * (-width * 0.5f + 0.12f) + Vector3.up * yc, outN, 0.24f, postH, 0.24f);
            Slab(k, surf + right * (width * 0.5f - 0.12f) + Vector3.up * yc, outN, 0.24f, postH, 0.24f);
            for (int i = 1; i < n; i++)
                Slab(k, surf + right * (-width * 0.5f + i * width / n) + Vector3.up * yc, outN, 0.22f, postH, 0.24f);
            // diagonal braces on the top storey, first and last bay
            if (h.storeys >= 2)
            {
                float yb = h.baseH + (h.storeys - 1) * h.storeyH + 0.12f, yt = top - 0.52f;
                float bay = width / n;
                Brace(k, surf, right, outN, -width * 0.5f + 0.2f, yb, -width * 0.5f + bay - 0.1f, yt);
                Brace(k, surf, right, outN, width * 0.5f - 0.2f, yb, width * 0.5f - bay + 0.1f, yt);
            }
        }

        static void Brace(MeshKit k, Vector3 surf, Vector3 right, Vector3 outN, float x0, float y0, float x1, float y1)
        {
            var p0 = surf + right * x0 + Vector3.up * y0;
            var p1 = surf + right * x1 + Vector3.up * y1;
            var dir = p1 - p0;
            k.BoxRot((p0 + p1) * 0.5f, new Vector3(0.2f, dir.magnitude, 0.22f), Quaternion.LookRotation(outN, dir.normalized));
        }

        // ---------------------------------------------------------------- wall + gate

        void Wall()
        {
            float z0 = L.wallZ0, z1 = L.wallZ1, H = L.wallHeight, X = L.wallHalfLength;
            float gw = L.gateHalfWidth, gh = L.gateRectHeight, gr = L.gateArchRadius, gtop = gh + gr;
            var wr = new System.Random(L.seed * 31 + 7);
            var k = wallGroup.Get(mats.WallStone, Matrix4x4.identity);
            var km = wallGroup.Get(mats.Mortar, Matrix4x4.identity);
            var kd = wallGroup.Get(mats.WallStoneDark, Matrix4x4.identity);
            var kw = wallGroup.Get(mats.TimberDark, Matrix4x4.identity);
            var ki = wallGroup.Get(mats.Iron, Matrix4x4.identity);
            var ks = wallGroup.Get(mats.WallStain, Matrix4x4.identity);

            // core: the mortar-dark mass the blocks stand out from
            km.Box(new Vector3(-X, 0, z0), new Vector3(-gw, H, z1));
            km.Box(new Vector3(gw, 0, z0), new Vector3(X, H, z1));
            km.Box(new Vector3(-gw, gtop, z0), new Vector3(gw, H, z1));
            k.Box(new Vector3(-X, H - 0.4f, z0), new Vector3(X, H, z1));                 // walkway
            k.Box(new Vector3(-X, 0, z1 - 0.3f), new Vector3(X, H, z1));                 // outer skin

            // stacked stone-block courses on the inner face: 1.6 m courses, 2.4-4.4 m blocks,
            // half-offset per course, each block a little different in depth and texture offset
            const float course = 1.6f;
            float faceZ = z0;
            int bandRows = Mathf.CeilToInt(H / course);
            for (int row = 0; row < bandRows; row++)
            {
                float y0 = row * course, y1 = Mathf.Min(H - 0.4f, y0 + course);
                if (y1 - y0 < 0.3f) break;
                int band = Mathf.Clamp((int)(y0 / 12.5f), 0, 3);
                float x = -X + (row % 2 == 0 ? 0f : -1.6f);
                while (x < X)
                {
                    var kb = wallGroup.Get(mats.WallBlock(band, wr.Next(3)), Matrix4x4.identity);   // three shades so the blocks read individually
                    float w = 2.4f + (float)wr.NextDouble() * 2.0f;
                    float xa = Mathf.Max(-X, x), xb = Mathf.Min(X, x + w);
                    x += w;
                    if (xb - xa < 0.4f) continue;
                    // leave the gate frame clear
                    if (xb > -gw - 2.4f && xa < gw + 2.4f && y0 < gtop + 4.2f) continue;
                    float depth = 0.16f + (float)wr.NextDouble() * 0.16f;
                    kb.uvOffset = new Vector2((float)wr.NextDouble() * 9f, (float)wr.NextDouble() * 9f);
                    kb.Box(new Vector3(xa + 0.05f, y0 + 0.05f, faceZ - depth), new Vector3(xb - 0.05f, y1 - 0.05f, faceZ + 0.05f));
                }
            }
            // string courses every 12.5 m (band boundaries), with a dark weather band beneath
            for (float y = 12.5f; y < H - 1f; y += 12.5f)
            {
                kd.Box(new Vector3(-X, y - 0.3f, z0 - 0.55f), new Vector3(X, y + 0.3f, z0));
                ks.Quad(new Vector3(-X, y - 1.4f, z0 - 0.36f), new Vector3(-X, y - 0.3f, z0 - 0.36f), new Vector3(X, y - 0.3f, z0 - 0.36f), new Vector3(X, y - 1.4f, z0 - 0.36f));
            }
            // drainage staining streaks running down from the parapet and the ledges
            for (int i = 0; i < 46; i++)
            {
                float sx = -X + 4f + (float)wr.NextDouble() * (2f * X - 8f);
                if (Mathf.Abs(sx) < gw + 4f) continue;
                float top = (wr.NextDouble() < 0.6) ? H - 0.5f : 12.5f * (1 + wr.Next(3));
                float len = 8f + (float)wr.NextDouble() * 22f;
                float wt = 0.5f + (float)wr.NextDouble() * 0.9f, wb = wt * 0.35f;
                float yb = Mathf.Max(0.5f, top - len);
                ks.uvOffset = new Vector2((float)wr.NextDouble() * 5f, 0f);
                ks.Quad(new Vector3(sx - wb, yb, z0 - 0.37f), new Vector3(sx - wt, top, z0 - 0.37f), new Vector3(sx + wt, top, z0 - 0.37f), new Vector3(sx + wb, yb, z0 - 0.37f));
            }
            // gate arch fill (stepped) and frame
            const int steps = 20;
            for (int i = 0; i < steps; i++)
            {
                float xa = -gw + i * (2f * gw / steps), xb = xa + 2f * gw / steps;
                float xm = (xa + xb) * 0.5f;
                float y = gh + Mathf.Sqrt(Mathf.Max(0f, gr * gr - xm * xm));
                kd.Box(new Vector3(xa, y, z0), new Vector3(xb, gtop + 0.02f, z1));
            }
            kd.Box(new Vector3(-gw - 2.4f, 0, z0 - 0.75f), new Vector3(-gw, gtop + 3f, z0));
            kd.Box(new Vector3(gw, 0, z0 - 0.75f), new Vector3(gw + 2.4f, gtop + 3f, z0));
            kd.Box(new Vector3(-gw - 2.4f, gtop + 2f, z0 - 0.75f), new Vector3(gw + 2.4f, gtop + 3.4f, z0));
            kd.Box(new Vector3(-gw - 3.2f, gtop + 3.4f, z0 - 1.05f), new Vector3(gw + 3.2f, gtop + 4.2f, z0));
            // parapets and crenellations
            k.Box(new Vector3(-X, H, z0 - 0.25f), new Vector3(X, H + 1.6f, z0 + 1.1f));
            k.Box(new Vector3(-X, H, z1 - 1.1f), new Vector3(X, H + 1.6f, z1 + 0.25f));
            for (float x = -X + 1f; x < X - 2f; x += 4.4f)
            {
                k.Box(new Vector3(x, H + 1.6f, z0 - 0.25f), new Vector3(x + 2.2f, H + 3.2f, z0 + 1.1f));
                k.Box(new Vector3(x, H + 1.6f, z1 - 1.1f), new Vector3(x + 2.2f, H + 3.2f, z1 + 0.25f));
            }
            // stepped buttresses every 30 m
            var kbut = wallGroup.Get(mats.WallBlock(1), Matrix4x4.identity);
            for (float x = -150f; x <= 150f; x += 30f)
            {
                if (Mathf.Abs(x) < 14f) continue;
                kbut.uvOffset = new Vector2(x * 0.37f, 0f);
                kbut.Box(new Vector3(x - 1.8f, 0, z0 - 3.2f), new Vector3(x + 1.8f, 16f, z0));
                kbut.Box(new Vector3(x - 1.5f, 0, z0 - 2.3f), new Vector3(x + 1.5f, 32f, z0));
                kbut.Box(new Vector3(x - 1.2f, 0, z0 - 1.4f), new Vector3(x + 1.2f, H - 4f, z0));
                kd.Box(new Vector3(x - 2.0f, 15.7f, z0 - 3.5f), new Vector3(x + 2.0f, 16.3f, z0));
                kd.Box(new Vector3(x - 1.7f, 31.7f, z0 - 2.6f), new Vector3(x + 1.7f, 32.3f, z0));
            }
            // doors (closed) with iron straps, and the dark tympanum above them
            float dz0 = z0 + 3.0f, dz1 = dz0 + 0.6f;
            kw.Box(new Vector3(-gw, 0, dz0), new Vector3(-0.06f, gh, dz1));
            kw.Box(new Vector3(0.06f, 0, dz0), new Vector3(gw, gh, dz1));
            kw.Box(new Vector3(-gw, gh - 0.02f, dz0 + 0.1f), new Vector3(gw, gtop, dz1 - 0.1f));
            for (int i = 0; i < 3; i++)
            {
                float y = 2f + i * 4f;
                ki.Box(new Vector3(-gw + 0.2f, y, dz0 - 0.05f), new Vector3(-0.15f, y + 0.4f, dz0));
                ki.Box(new Vector3(0.15f, y, dz0 - 0.05f), new Vector3(gw - 0.2f, y + 0.4f, dz0));
            }
            for (float x = -gw + 0.6f; x < gw; x += 0.9f)
                ki.Box(new Vector3(x - 0.08f, gh, dz0 + 0.2f), new Vector3(x + 0.08f, gtop - 0.1f, dz0 + 0.36f));

            var go = new GameObject("Wall");
            go.transform.SetParent(info.root.transform, false);
            go.layer = info.hookLayer;
            AddBox(go, new Vector3((-X - gw) * 0.5f, H * 0.5f, (z0 + z1) * 0.5f), new Vector3(X - gw, H, z1 - z0));
            AddBox(go, new Vector3((X + gw) * 0.5f, H * 0.5f, (z0 + z1) * 0.5f), new Vector3(X - gw, H, z1 - z0));
            AddBox(go, new Vector3(0, (gtop + H) * 0.5f, (z0 + z1) * 0.5f), new Vector3(2f * gw, H - gtop, z1 - z0));
            AddBox(go, new Vector3(0, gh * 0.5f, (dz0 + dz1) * 0.5f), new Vector3(2f * gw, gh, dz1 - dz0));
        }

        static void AddBox(GameObject go, Vector3 c, Vector3 s)
        {
            var b = go.AddComponent<BoxCollider>();
            b.center = c; b.size = s;
        }

        /// <summary>Raised flagstone plaza under the market square and a kerb ring.</summary>
        void Plaza()
        {
            var k = propGroup.Get(mats.Paving(2.6f), Matrix4x4.identity);
            var r = L.square;
            k.Box(new Vector3(r.xMin, -0.05f, r.yMin), new Vector3(r.xMax, 0.1f, r.yMax));
            var kb = propGroup.Get(mats.WallStoneDark, Matrix4x4.identity);
            kb.Box(new Vector3(-L.wallHalfLength, -0.05f, L.wallZ0 - 10f), new Vector3(L.wallHalfLength, 0.08f, L.wallZ0));
        }

        // ---------------------------------------------------------------- props

        void Prop(PropSpec p)
        {
            var xf = Matrix4x4.TRS(p.pos, Quaternion.Euler(0f, p.yaw, 0f), Vector3.one * p.scale);
            switch (p.kind)
            {
                case PropKind.Barrel: Barrel(xf); break;
                case PropKind.Crate: Crate(xf); break;
                case PropKind.Stall: Stall(xf, p); break;
                case PropKind.Cart: Cart(xf, p); break;
                case PropKind.Lamp: Lamp(xf); break;
                case PropKind.Fountain: Fountain(p); break;
                case PropKind.Well: Well(xf, p); break;
                case PropKind.Sacks: Sacks(xf); break;
                case PropKind.Clothesline: Clothesline(p); break;
            }
        }

        void Barrel(Matrix4x4 xf)
        {
            var k = propGroup.Get(mats.Timber, xf);
            k.Cylinder(Vector3.zero, 0.4f, 0.48f, 12, Quaternion.identity, true, 0.44f);
            k.Cylinder(new Vector3(0, 0.48f, 0), 0.44f, 0.47f, 12, Quaternion.identity, true, 0.4f);
            var ki = propGroup.Get(mats.Iron, xf);
            ki.Cylinder(new Vector3(0, 0.16f, 0), 0.45f, 0.07f, 12, false);
            ki.Cylinder(new Vector3(0, 0.74f, 0), 0.45f, 0.07f, 12, false);
        }

        void Crate(Matrix4x4 xf)
        {
            var k = propGroup.Get(mats.Timber, xf);
            k.BoxC(new Vector3(0, 0.45f, 0), new Vector3(0.9f, 0.9f, 0.9f));
            var kd = propGroup.Get(mats.TimberDark, xf);
            kd.BoxC(new Vector3(0, 0.45f, -0.46f), new Vector3(0.08f, 0.92f, 0.04f));
            kd.BoxC(new Vector3(0, 0.45f, 0.46f), new Vector3(0.08f, 0.92f, 0.04f));
            kd.BoxC(new Vector3(-0.46f, 0.45f, 0), new Vector3(0.04f, 0.92f, 0.08f));
            kd.BoxC(new Vector3(0.46f, 0.45f, 0), new Vector3(0.04f, 0.92f, 0.08f));
        }

        void Stall(Matrix4x4 xf, PropSpec p)
        {
            var k = propGroup.Get(mats.Timber, xf);
            var kd = propGroup.Get(mats.TimberDark, xf);
            foreach (var c in new[] { new Vector3(-1.4f, 0, -1.0f), new Vector3(1.4f, 0, -1.0f), new Vector3(-1.4f, 0, 1.0f), new Vector3(1.4f, 0, 1.0f) })
                kd.BoxC(c + new Vector3(0, 1.25f, 0), new Vector3(0.14f, 2.5f, 0.14f));
            k.Box(new Vector3(-1.5f, 0.0f, -1.0f), new Vector3(1.5f, 0.95f, -0.1f));
            k.Box(new Vector3(-1.5f, 0.92f, -1.05f), new Vector3(1.5f, 1.0f, 0.6f));
            var kc = propGroup.Get(mats.Cloth(p.variant), xf);
            kc.BoxRot(new Vector3(0, 2.62f, -0.05f), new Vector3(3.3f, 0.05f, 2.7f), Quaternion.Euler(-14f, 0, 0));
            // scalloped hem
            for (int i = -3; i <= 3; i++)
                kc.BoxRot(new Vector3(i * 0.47f, 2.25f, -1.3f), new Vector3(0.44f, 0.36f, 0.04f), Quaternion.identity);
            var ks = propGroup.Get(mats.Sack, xf);
            ks.BoxRot(new Vector3(-0.8f, 1.2f, 0.2f), new Vector3(0.6f, 0.4f, 0.5f), Quaternion.Euler(0, 15f, 0));
            ks.BoxRot(new Vector3(0.5f, 1.2f, 0.1f), new Vector3(0.7f, 0.4f, 0.5f), Quaternion.Euler(0, -20f, 0));
            var kb = propGroup.Get(mats.Timber, xf);
            kb.BoxC(new Vector3(0.9f, 0.4f, 0.3f), new Vector3(0.8f, 0.8f, 0.8f));
            Collider(p.pos, p.yaw, new Vector3(0, 1.3f, 0), new Vector3(3.2f, 2.6f, 2.4f));
        }

        void Cart(Matrix4x4 xf, PropSpec p)
        {
            var k = propGroup.Get(mats.Timber, xf);
            var kd = propGroup.Get(mats.TimberDark, xf);
            var ki = propGroup.Get(mats.Iron, xf);
            k.Box(new Vector3(-0.95f, 0.72f, -1.6f), new Vector3(0.95f, 0.9f, 1.6f));
            k.Box(new Vector3(-1.0f, 0.9f, -1.6f), new Vector3(-0.88f, 1.5f, 1.6f));
            k.Box(new Vector3(0.88f, 0.9f, -1.6f), new Vector3(1.0f, 1.5f, 1.6f));
            k.Box(new Vector3(-1.0f, 0.9f, 1.5f), new Vector3(1.0f, 1.5f, 1.62f));
            k.Box(new Vector3(-1.0f, 0.9f, -1.62f), new Vector3(1.0f, 1.3f, -1.5f));
            kd.Cylinder(new Vector3(-1.12f, 0.75f, 0.3f), 0.75f, 0.12f, 14, Quaternion.Euler(0, 0, -90f));
            kd.Cylinder(new Vector3(1.0f, 0.75f, 0.3f), 0.75f, 0.12f, 14, Quaternion.Euler(0, 0, -90f));
            ki.Cylinder(new Vector3(-1.2f, 0.75f, 0.3f), 0.06f, 2.4f, 8, Quaternion.Euler(0, 0, -90f));
            kd.BoxRot(new Vector3(-0.6f, 0.55f, -2.4f), new Vector3(0.1f, 0.1f, 2.0f), Quaternion.Euler(-12f, 0, 0));
            kd.BoxRot(new Vector3(0.6f, 0.55f, -2.4f), new Vector3(0.1f, 0.1f, 2.0f), Quaternion.Euler(-12f, 0, 0));
            var ks = propGroup.Get(mats.Sack, xf);
            ks.BoxRot(new Vector3(-0.3f, 1.15f, 0.4f), new Vector3(0.7f, 0.5f, 0.9f), Quaternion.Euler(0, 20f, 0));
            ks.BoxRot(new Vector3(0.35f, 1.15f, -0.5f), new Vector3(0.7f, 0.5f, 0.9f), Quaternion.Euler(0, -10f, 0));
            var kh = propGroup.Get(mats.Straw, xf);
            kh.BoxRot(new Vector3(0.1f, 1.55f, 0.1f), new Vector3(1.5f, 0.5f, 2.4f), Quaternion.Euler(0, 4f, 0));
            Collider(p.pos, p.yaw, new Vector3(0, 0.9f, 0), new Vector3(2.4f, 1.8f, 3.4f));
        }

        void Lamp(Matrix4x4 xf)
        {
            var ki = propGroup.Get(mats.Iron, xf);
            ki.Cylinder(Vector3.zero, 0.22f, 0.3f, 8);
            ki.Cylinder(Vector3.zero, 0.08f, 3.4f, 8);
            ki.BoxC(new Vector3(0, 3.95f, 0), new Vector3(0.52f, 0.1f, 0.52f));
            ki.BoxC(new Vector3(0, 3.42f, 0), new Vector3(0.44f, 0.06f, 0.44f));
            foreach (var c in new[] { new Vector3(-0.2f, 0, -0.2f), new Vector3(0.2f, 0, -0.2f), new Vector3(-0.2f, 0, 0.2f), new Vector3(0.2f, 0, 0.2f) })
                ki.BoxC(c + new Vector3(0, 3.68f, 0), new Vector3(0.04f, 0.5f, 0.04f));
            var kg = propGroup.Get(mats.Glass, xf);
            kg.BoxC(new Vector3(0, 3.68f, 0), new Vector3(0.36f, 0.46f, 0.36f));
        }

        void Fountain(PropSpec p)
        {
            var xf = Matrix4x4.TRS(p.pos, Quaternion.identity, Vector3.one);
            var ks = propGroup.Get(mats.WallStoneDark, xf);
            var kw = propGroup.Get(mats.Water, xf);
            float R = 5.2f;
            for (int i = 0; i < 8; i++)
            {
                float ang = i * 45f + 22.5f;
                var c = new Vector3(Mathf.Sin(ang * Mathf.Deg2Rad), 0, Mathf.Cos(ang * Mathf.Deg2Rad)) * R;
                ks.BoxRot(c + new Vector3(0, 0.5f, 0), new Vector3(2f * R * Mathf.Tan(22.5f * Mathf.Deg2Rad) + 0.3f, 1.0f, 0.6f), Quaternion.Euler(0, ang, 0));
                ks.BoxRot(c + new Vector3(0, 1.05f, 0), new Vector3(2f * R * Mathf.Tan(22.5f * Mathf.Deg2Rad) + 0.5f, 0.12f, 0.8f), Quaternion.Euler(0, ang, 0));
            }
            ks.Cylinder(Vector3.zero, R + 0.6f, 0.18f, 16);
            kw.Cylinder(new Vector3(0, 0.1f, 0), R - 0.25f, 0.72f, 24);
            ks.Cylinder(new Vector3(0, 0.1f, 0), 0.7f, 2.4f, 10);
            ks.Cylinder(new Vector3(0, 2.4f, 0), 1.9f, 0.16f, 12, Quaternion.identity, true, 1.75f);
            ks.Cylinder(new Vector3(0, 2.5f, 0), 1.6f, 0.3f, 12);
            kw.Cylinder(new Vector3(0, 2.7f, 0), 1.5f, 0.14f, 12);
            ks.Cylinder(new Vector3(0, 2.8f, 0), 0.28f, 1.4f, 8);
            ks.Cylinder(new Vector3(0, 4.2f, 0), 0.5f, 0.35f, 8);
            Collider(p.pos, 0f, new Vector3(0, 0.6f, 0), new Vector3(2f * R + 1f, 1.2f, 2f * R + 1f));
        }

        void Well(Matrix4x4 xf, PropSpec p)
        {
            var ks = propGroup.Get(mats.Stone(1), xf);
            var kt = propGroup.Get(mats.TimberDark, xf);
            var kr = propGroup.Get(mats.Roof(0, 1), xf);
            float R = 1.1f;
            for (int i = 0; i < 8; i++)
            {
                float ang = i * 45f + 22.5f;
                var c = new Vector3(Mathf.Sin(ang * Mathf.Deg2Rad), 0, Mathf.Cos(ang * Mathf.Deg2Rad)) * R;
                ks.BoxRot(c + new Vector3(0, 0.5f, 0), new Vector3(2f * R * Mathf.Tan(22.5f * Mathf.Deg2Rad) + 0.2f, 1.0f, 0.4f), Quaternion.Euler(0, ang, 0));
            }
            kt.BoxC(new Vector3(-1.05f, 1.3f, 0), new Vector3(0.16f, 2.6f, 0.16f));
            kt.BoxC(new Vector3(1.05f, 1.3f, 0), new Vector3(0.16f, 2.6f, 0.16f));
            kt.Cylinder(new Vector3(-1.1f, 2.0f, 0), 0.07f, 2.2f, 8, Quaternion.Euler(0, 0, -90f));
            kr.Gable(new Vector3(-1.5f, 0, -1.0f), new Vector3(1.5f, 0, 1.0f), 2.6f, 3.4f, true, true);
            Collider(p.pos, p.yaw, new Vector3(0, 0.6f, 0), new Vector3(2.6f, 1.2f, 2.6f));
        }

        void Sacks(Matrix4x4 xf)
        {
            var ks = propGroup.Get(mats.Sack, xf);
            ks.BoxRot(new Vector3(0, 0.3f, 0), new Vector3(0.8f, 0.6f, 1.1f), Quaternion.Euler(0, 10f, 0));
            ks.BoxRot(new Vector3(0.85f, 0.3f, 0.1f), new Vector3(0.8f, 0.6f, 1.1f), Quaternion.Euler(0, -8f, 0));
            ks.BoxRot(new Vector3(0.4f, 0.85f, 0.05f), new Vector3(0.8f, 0.55f, 1.05f), Quaternion.Euler(0, 25f, 0));
            var kh = propGroup.Get(mats.Straw, xf);
            kh.BoxRot(new Vector3(-1.2f, 0.4f, 0.4f), new Vector3(1.1f, 0.8f, 0.9f), Quaternion.Euler(0, 35f, 0));
        }

        void Clothesline(PropSpec p)
        {
            var kr = propGroup.Get(mats.Dark, Matrix4x4.identity);
            var dir = p.end - p.pos;
            float len = dir.magnitude;
            var d = dir / len;
            kr.Cylinder(p.pos, 0.025f, len, 5, Quaternion.FromToRotation(Vector3.up, d), false);
            int n = 4 + p.variant % 3;
            for (int i = 0; i < n; i++)
            {
                float t = (i + 0.5f) / n;
                float sag = 0.3f * Mathf.Sin(t * Mathf.PI);
                var c = Vector3.Lerp(p.pos, p.end, t) - Up * sag;
                float w = 0.5f + ((p.variant + i) % 3) * 0.12f, hgt = 0.7f + ((p.variant * 7 + i) % 4) * 0.15f;
                var kc = propGroup.Get(mats.Cloth((p.variant + i) % TownMaterials.ClothTint.Length), Matrix4x4.identity);
                var a = c - d * (w * 0.5f) - Up * hgt;
                kc.Quad2(a, a + Up * hgt, a + Up * hgt + d * w, a + d * w);
            }
        }

        void Collider(Vector3 pos, float yaw, Vector3 center, Vector3 size)
        {
            var go = new GameObject("PropCollider");
            go.transform.SetParent(propsRoot, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var b = go.AddComponent<BoxCollider>();
            b.center = center; b.size = size;
        }
    }
}
