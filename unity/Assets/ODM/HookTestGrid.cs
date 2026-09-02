using UnityEngine;
using Shared;

namespace ODM
{
    /// <summary>
    /// Test geometry for flight: a grid of tall stone towers with flat roofs on the
    /// HookTarget layer, either side of the placeholder street. Deterministic from the seed.
    /// Town replaces it later; this exists so the critic can see flight between buildings.
    /// </summary>
    public class HookTestGrid : MonoBehaviour
    {
        public static readonly float[] Columns = { -70f, -46f, -22f, 22f, 46f, 70f };
        public const int Rows = 9;
        public const float RowStart = -66f, RowStep = 22f;
        public const float Footprint = 12f;

        public float[,] heights;   // [column, row]
        public Transform[,] towers;

        public static HookTestGrid Build(int seed)
        {
            var existing = Ctx.Get<HookTestGrid>("odmGrid");
            if (existing != null) return existing;
            var go = new GameObject("HookTestGrid");
            var g = go.AddComponent<HookTestGrid>();
            g.Generate(seed);
            Ctx.Set("odmGrid", g);
            return g;
        }

        public void Generate(int seed)
        {
            var rng = new System.Random(seed * 7919 + 13);
            int layer = OdmLayers.Hook;
            heights = new float[Columns.Length, Rows];
            towers = new Transform[Columns.Length, Rows];
            var stone = Mats.Lit(new Color(0.66f, 0.62f, 0.55f), 0.08f);
            var stoneDark = Mats.Lit(new Color(0.52f, 0.49f, 0.44f), 0.08f);
            var roof = Mats.Lit(new Color(0.5f, 0.22f, 0.16f), 0.12f);
            var lip = Mats.Lit(new Color(0.42f, 0.4f, 0.37f), 0.05f);
            for (int c = 0; c < Columns.Length; c++)
            for (int r = 0; r < Rows; r++)
            {
                float h = 20f + (float)rng.NextDouble() * 14f;
                float fx = Footprint + (float)rng.NextDouble() * 3f;
                float fz = Footprint + (float)rng.NextDouble() * 3f;
                float x = Columns[c], z = RowStart + r * RowStep;
                heights[c, r] = h;
                var t = GameObject.CreatePrimitive(PrimitiveType.Cube);
                t.name = "Tower_" + c + "_" + r;
                t.layer = layer;
                t.transform.SetParent(transform);
                t.transform.position = new Vector3(x, h * 0.5f, z);
                t.transform.localScale = new Vector3(fx, h, fz);
                t.GetComponent<Renderer>().sharedMaterial = (c + r) % 2 == 0 ? stone : stoneDark;
                towers[c, r] = t.transform;

                // roof slab + a parapet lip so the edge reads from the air
                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = "Roof";
                slab.layer = layer;
                slab.transform.SetParent(transform);
                slab.transform.position = new Vector3(x, h + 0.15f, z);
                slab.transform.localScale = new Vector3(fx, 0.3f, fz);   // flush: no overhang to bonk on
                slab.GetComponent<Renderer>().sharedMaterial = roof;
                for (int side = 0; side < 4; side++)
                {
                    var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    p.name = "Parapet";
                    p.layer = layer;
                    p.transform.SetParent(transform);
                    bool alongX = side < 2;
                    float sgn = side % 2 == 0 ? 1f : -1f;
                    p.transform.position = alongX
                        ? new Vector3(x, h + 0.6f, z + sgn * (fz * 0.5f + 0.15f))
                        : new Vector3(x + sgn * (fx * 0.5f + 0.15f), h + 0.6f, z);
                    p.transform.localScale = alongX ? new Vector3(fx + 0.6f, 0.6f, 0.3f) : new Vector3(0.3f, 0.6f, fz + 0.6f);
                    p.GetComponent<Renderer>().sharedMaterial = lip;
                    Destroy(p.GetComponent<Collider>());   // decor: never a tripping hazard
                }
                // a chimney so rooftops are not featureless
                var ch = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ch.name = "Chimney";
                ch.layer = layer;
                ch.transform.SetParent(transform);
                ch.transform.position = new Vector3(x + fx * 0.3f, h + 1.2f, z - fz * 0.25f);
                ch.transform.localScale = new Vector3(1.2f, 2.4f, 1.2f);
                ch.GetComponent<Renderer>().sharedMaterial = lip;
                Destroy(ch.GetComponent<Collider>());
            }
        }

        public float Height(int col, int row) => heights[col, row];
        public Vector3 Center(int col, int row) => new Vector3(Columns[col], 0, RowStart + row * RowStep);
        /// <summary>Roof-centre point (just above the slab).</summary>
        public Vector3 RoofTop(int col, int row) { var c = Center(col, row); return new Vector3(c.x, heights[col, row] + 0.3f, c.z); }
        /// <summary>A point on a tower wall, dy below the roof, on the side facing the street (toward x=0).</summary>
        public Vector3 WallNearTop(int col, int row, float dy)
        {
            var c = Center(col, row);
            float half = towers[col, row].localScale.x * 0.5f;
            float x = c.x - Mathf.Sign(c.x) * half;
            return new Vector3(x, heights[col, row] - dy, c.z);
        }
    }
}
