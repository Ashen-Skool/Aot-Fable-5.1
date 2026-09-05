using System;
using System.Collections.Generic;
using UnityEngine;

namespace Town
{
    /// <summary>
    /// Pure, seeded layout of the district: blocks, houses, props, wall, square. No Unity objects.
    /// World: X east, Z north (towards the wall), metres. Main street runs along Z at x=0 from the
    /// south edge to the gate. Deterministic for a seed (System.Random only).
    /// </summary>
    public class TownLayout
    {
        public int seed;
        public List<HouseSpec> houses = new List<HouseSpec>(400);
        public List<PropSpec> props = new List<PropSpec>(400);
        public List<Rect> blocks = new List<Rect>(64);
        public Rect square;
        public Bounds bounds;
        public Vector3 spawn, gate;
        public float wallZ0 = 118f, wallZ1 = 128f, wallHeight = 50f, wallHalfLength = 170f;
        public float gateHalfWidth = 5f, gateRectHeight = 12f, gateArchRadius = 5f;
        public float mainHalf = 4.5f;

        // column x ranges (mirrored to -x) and row z ranges
        public static readonly float[] ColMin = { 4.5f, 32.5f, 60.5f, 88.5f };
        public const float ColWidth = 22f;
        public static readonly float[] RowMin = { -86f, -44f, -4f, 36f, 76f };
        public static readonly float[] RowMax = { -50f, -10f, 30f, 70f, 108f };

        public static TownLayout Build(int seed)
        {
            var L = new TownLayout { seed = seed };
            var rng = new System.Random(seed);
            L.square = Rect.MinMaxRect(-32.5f, -10f, 32.5f, 30f);
            L.spawn = new Vector3(0f, 0f, -30f);
            L.gate = new Vector3(0f, 0f, L.wallZ0);

            for (int side = -1; side <= 1; side += 2)
            for (int c = 0; c < ColMin.Length; c++)
            for (int r = 0; r < RowMin.Length; r++)
            {
                float xa = ColMin[c], xb = ColMin[c] + ColWidth;
                float xmin = side > 0 ? xa : -xb, xmax = side > 0 ? xb : -xa;
                var block = Rect.MinMaxRect(xmin, RowMin[r], xmax, RowMax[r]);
                if (c == 0 && r == 2) continue;                 // market square
                L.blocks.Add(block);
                int endRow = 0;                                  // 0 none, 1 north end row, 2 south end row
                if (c == 0 && r == 1) endRow = 1;                // faces the square from the south
                else if (c == 0 && r == 3) endRow = 2;           // faces the square from the north
                else
                {
                    double p = rng.NextDouble();
                    endRow = p < 0.3 ? 1 : p < 0.6 ? 2 : 0;
                }
                L.FillBlock(block, endRow, rng);
            }

            L.Props(rng);

            float minX = -ColMin[3] - ColWidth, maxX = ColMin[3] + ColWidth;
            L.bounds = new Bounds();
            L.bounds.SetMinMax(new Vector3(minX, 0f, RowMin[0]), new Vector3(maxX, L.wallHeight, L.wallZ1));
            return L;
        }

        static float Range(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        void FillBlock(Rect b, int endRow, System.Random rng)
        {
            float endDepth = 9.5f;
            float zLo = b.yMin, zHi = b.yMax;
            if (endRow == 1) zHi -= endDepth;
            if (endRow == 2) zLo += endDepth;
            Row(new Vector3(b.xMin, 0, zLo), new Vector3(b.xMin, 0, zHi), Facing.NegX, b.width * 0.5f, rng);
            Row(new Vector3(b.xMax, 0, zLo), new Vector3(b.xMax, 0, zHi), Facing.PosX, b.width * 0.5f, rng);
            if (endRow == 1) Row(new Vector3(b.xMin, 0, b.yMax), new Vector3(b.xMax, 0, b.yMax), Facing.PosZ, endDepth, rng);
            if (endRow == 2) Row(new Vector3(b.xMin, 0, b.yMin), new Vector3(b.xMax, 0, b.yMin), Facing.NegZ, endDepth, rng);
        }

        /// <summary>Houses packed edge to edge along the street edge a->b, fronts on that edge, backs towards the block.</summary>
        void Row(Vector3 a, Vector3 b, Facing facing, float maxDepth, System.Random rng)
        {
            float len = Vector3.Distance(a, b);
            var dir = (b - a) / len;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 7.6f));
            var widths = new float[n];
            float sum = 0f;
            for (int i = 0; i < n; i++) { widths[i] = Range(rng, 0.75f, 1.3f); sum += widths[i]; }
            var inward = -Spec(facing);
            float cursor = 0f;
            for (int i = 0; i < n; i++)
            {
                float w = widths[i] / sum * len;
                float d = Mathf.Min(maxDepth, Range(rng, 8.5f, 11f));
                var centre = a + dir * (cursor + w * 0.5f) + inward * (d * 0.5f);
                cursor += w;
                houses.Add(MakeHouse(centre, w, d, facing, rng));
            }
        }

        static Vector3 Spec(Facing f) => f == Facing.NegZ ? Vector3.back : f == Facing.PosZ ? Vector3.forward : f == Facing.PosX ? Vector3.right : Vector3.left;

        HouseSpec MakeHouse(Vector3 centre, float w, float d, Facing facing, System.Random rng)
        {
            var h = new HouseSpec { pos = centre, w = w, d = d, facing = facing };
            double p = rng.NextDouble();
            h.storeys = p < 0.38 ? 2 : p < 0.9 ? 3 : 4;
            h.storeyH = Range(rng, 2.8f, 3.1f);
            h.baseH = Range(rng, 0.25f, 0.5f);
            h.gableFront = w < 8.5f && rng.NextDouble() < 0.3;
            h.pitch = Range(rng, 40f, 50f);
            p = rng.NextDouble();
            h.wallStyle = p < 0.45 ? 0 : p < 0.7 ? 1 : 2;
            h.plasterTint = rng.Next(TownMaterials.PlasterTint.Length);
            h.stoneSet = rng.Next(TownMaterials.StoneSets.Length);
            h.roofSet = rng.Next(TownMaterials.RoofSets.Length * TownMaterials.RoofTint.Length);
            h.shutterTint = rng.Next(TownMaterials.ShutterTint.Length);
            h.chimneys = rng.NextDouble() < 0.85 ? (w > 8f && rng.NextDouble() < 0.4 ? 2 : 1) : 0;
            float span = (h.gableFront ? d : w) * 0.5f - 1.0f;
            h.chimneyX0 = Range(rng, -span, span * 0.1f);
            h.chimneyX1 = Range(rng, span * 0.2f, span);
            h.chimneyZ = Range(rng, -0.8f, 0.8f);
            h.dormer = !h.gableFront && w > 7f && rng.NextDouble() < 0.45;
            h.dormerX = Range(rng, -w * 0.3f, w * 0.3f);
            if (h.chimneys > 0 && Mathf.Abs(h.dormerX - h.chimneyX0) < 1.6f) h.dormer = false;
            h.balcony = h.storeys >= 3 && rng.NextDouble() < 0.3;
            h.shutters = rng.NextDouble() < 0.7;
            int slots = Mathf.Max(1, Mathf.RoundToInt(w / 2.6f));
            h.doorSlot = rng.Next(slots);
            h.shade = rng.Next(3);
            h.uvOffset = new Vector2(Range(rng, 0f, 7f), Range(rng, 0f, 7f));
            return h;
        }

        void Props(System.Random rng)
        {
            // market square
            var fc = new Vector3(0f, 0f, 10f);
            props.Add(new PropSpec { kind = PropKind.Fountain, pos = fc });
            for (int i = 0; i < 8; i++)
            {
                float ang = i * 45f;
                if (i == 0 || i == 4) continue;                 // keep the main street axis open
                float rad = 16f;
                var pos = fc + new Vector3(Mathf.Sin(ang * Mathf.Deg2Rad) * rad, 0f, Mathf.Cos(ang * Mathf.Deg2Rad) * rad);
                props.Add(new PropSpec { kind = PropKind.Stall, pos = pos, yaw = ang + Range(rng, -8f, 8f), variant = rng.Next(6) });
                var c = pos + new Vector3(Mathf.Sin((ang + 90f) * Mathf.Deg2Rad), 0, Mathf.Cos((ang + 90f) * Mathf.Deg2Rad)) * 2.4f;
                props.Add(new PropSpec { kind = rng.NextDouble() < 0.5 ? PropKind.Barrel : PropKind.Crate, pos = c, yaw = Range(rng, 0f, 360f) });
            }
            // stalls along the square's long edges
            for (int i = 0; i < 4; i++)
            {
                float z = -2f + i * 8f;
                props.Add(new PropSpec { kind = PropKind.Stall, pos = new Vector3(-28.5f, 0, z), yaw = -90f, variant = rng.Next(6) });
                props.Add(new PropSpec { kind = PropKind.Stall, pos = new Vector3(28.5f, 0, z), yaw = 90f, variant = rng.Next(6) });
            }
            props.Add(new PropSpec { kind = PropKind.Well, pos = new Vector3(-20f, 0, 22f) });
            props.Add(new PropSpec { kind = PropKind.Cart, pos = new Vector3(19f, 0, -3f), yaw = 70f });
            props.Add(new PropSpec { kind = PropKind.Cart, pos = new Vector3(-9f, 0, 24f), yaw = -30f });
            props.Add(new PropSpec { kind = PropKind.Sacks, pos = new Vector3(24f, 0, 20f), yaw = 20f });

            // lamps along the main street and around the square
            for (float z = -80f; z < 108f; z += 24f)
            {
                float sx = ((int)((z + 80f) / 24f) % 2 == 0) ? -1f : 1f;
                if (Mathf.Abs(z - 10f) < 22f) continue;
                props.Add(new PropSpec { kind = PropKind.Lamp, pos = new Vector3(sx * (mainHalf - 0.6f), 0, z), yaw = sx > 0 ? 90f : -90f });
            }
            foreach (var p in new[] { new Vector3(-31f, 0, -8f), new Vector3(31f, 0, -8f), new Vector3(-31f, 0, 28f), new Vector3(31f, 0, 28f), new Vector3(-6f, 0, -8.5f), new Vector3(6f, 0, 28.5f) })
                props.Add(new PropSpec { kind = PropKind.Lamp, pos = p, yaw = 0f });

            // carts and clutter in the streets
            for (int i = 0; i < 6; i++)
            {
                float z = Range(rng, -80f, 100f);
                if (Mathf.Abs(z - 10f) < 24f) z += 50f;
                float x = (rng.NextDouble() < 0.5 ? -1f : 1f) * (mainHalf - 1.6f);
                props.Add(new PropSpec { kind = PropKind.Cart, pos = new Vector3(x, 0, Mathf.Min(z, 104f)), yaw = Range(rng, -15f, 15f) });
            }

            // barrels and crates against house fronts
            foreach (var h in houses)
            {
                if (rng.NextDouble() > 0.28) continue;
                var front = h.Front;
                var right = Vector3.Cross(Vector3.up, front);
                float along = Range(rng, -h.w * 0.4f, h.w * 0.4f);
                var basePos = h.pos + front * (h.d * 0.5f + 0.6f) + right * along;
                int count = 1 + rng.Next(3);
                for (int k = 0; k < count; k++)
                {
                    var pos = basePos + right * (k * 0.95f) + front * Range(rng, 0f, 0.3f);
                    props.Add(new PropSpec { kind = rng.NextDouble() < 0.55 ? PropKind.Barrel : PropKind.Crate, pos = pos, yaw = Range(rng, 0f, 360f), scale = Range(rng, 0.85f, 1.1f) });
                }
                if (count == 3 && rng.NextDouble() < 0.5)
                    props.Add(new PropSpec { kind = PropKind.Crate, pos = basePos + right * 0.95f + Vector3.up * 0.9f, yaw = Range(rng, 0f, 360f), scale = 0.85f });
            }

            // clotheslines across the narrow side streets (between the columns)
            float[] streetX = { 29.5f, 57.5f, 85.5f };
            for (int i = 0; i < 26; i++)
            {
                float sx = streetX[rng.Next(3)] * (rng.NextDouble() < 0.5 ? -1f : 1f);
                int row = rng.Next(RowMin.Length);
                float z = Range(rng, RowMin[row] + 4f, RowMax[row] - 4f);
                float hgt = Range(rng, 6.5f, 9f);
                props.Add(new PropSpec { kind = PropKind.Clothesline, pos = new Vector3(sx - 3f, hgt, z), end = new Vector3(sx + 3f, hgt, z), variant = rng.Next(100) });
            }
            // and a few over the E-W streets
            for (int i = 0; i < 8; i++)
            {
                int gap = rng.Next(RowMin.Length - 1);
                float z0 = RowMax[gap], z1 = RowMin[gap + 1];
                int col = rng.Next(4);
                float x = (ColMin[col] + Range(rng, 3f, ColWidth - 3f)) * (rng.NextDouble() < 0.5 ? -1f : 1f);
                if (Mathf.Abs(x) < 34f && z0 > -12f && z1 < 32f) continue;
                float hgt = Range(rng, 6.5f, 9f);
                props.Add(new PropSpec { kind = PropKind.Clothesline, pos = new Vector3(x, hgt, z0), end = new Vector3(x, hgt, z1), variant = rng.Next(100) });
            }
        }
    }
}
