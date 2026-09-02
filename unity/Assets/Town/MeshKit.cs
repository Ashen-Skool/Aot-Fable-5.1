using System.Collections.Generic;
using UnityEngine;

namespace Town
{
    /// <summary>
    /// Append-only mesh builder. Faces are wound clockwise as seen from outside (Unity front face).
    /// UVs are planar in metres (dot of the local position with the face axes), so a material's
    /// tiling of 1/tileSize gives a texture repeat every tileSize metres, continuous across boxes.
    /// All geometry is transformed by <see cref="xf"/> as it is appended.
    /// </summary>
    public class MeshKit
    {
        readonly List<Vector3> v = new List<Vector3>(8192);
        readonly List<Vector3> n = new List<Vector3>(8192);
        readonly List<Vector2> uv = new List<Vector2>(8192);
        readonly List<int> t = new List<int>(16384);
        public Matrix4x4 xf = Matrix4x4.identity;

        public int VertexCount => v.Count;
        public bool Empty => v.Count == 0;

        void Add(Vector3 p, Vector3 nrm, Vector3 uAxis, Vector3 vAxis)
        {
            v.Add(xf.MultiplyPoint3x4(p));
            n.Add(xf.MultiplyVector(nrm).normalized);
            uv.Add(new Vector2(Vector3.Dot(p, uAxis), Vector3.Dot(p, vAxis)));
        }

        /// <summary>a,b,c,d clockwise seen from the front. a->b is "up" (v), a->d is "right" (u).</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var nrm = Vector3.Cross(b - a, c - a).normalized;
            var uAxis = (d - a).normalized;
            var vAxis = (b - a).normalized;
            int i = v.Count;
            Add(a, nrm, uAxis, vAxis); Add(b, nrm, uAxis, vAxis); Add(c, nrm, uAxis, vAxis); Add(d, nrm, uAxis, vAxis);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        /// <summary>Double-sided quad (cloth, leaves).</summary>
        public void Quad2(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Quad(a, b, c, d);
            Quad(d, c, b, a);
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            var nrm = Vector3.Cross(b - a, c - a).normalized;
            var uAxis = (c - a).normalized;
            var vAxis = Vector3.Cross(nrm, uAxis).normalized;
            int i = v.Count;
            Add(a, nrm, uAxis, vAxis); Add(b, nrm, uAxis, vAxis); Add(c, nrm, uAxis, vAxis);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        public void Box(Vector3 min, Vector3 max)
        {
            float x0 = min.x, y0 = min.y, z0 = min.z, x1 = max.x, y1 = max.y, z1 = max.z;
            Quad(new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0)); // -z
            Quad(new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), new Vector3(x0, y0, z1)); // +z
            Quad(new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1)); // +x
            Quad(new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), new Vector3(x0, y0, z0)); // -x
            Quad(new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0)); // +y
            Quad(new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1)); // -y
        }

        public void BoxC(Vector3 center, Vector3 size) => Box(center - size * 0.5f, center + size * 0.5f);

        /// <summary>Box rotated by q around its centre.</summary>
        public void BoxRot(Vector3 center, Vector3 size, Quaternion q)
        {
            var saved = xf;
            xf = xf * Matrix4x4.TRS(center, q, Vector3.one);
            Box(-size * 0.5f, size * 0.5f);
            xf = saved;
        }

        /// <summary>Cylinder along local +Y from baseCenter, optionally rotated (rot applied about baseCenter).</summary>
        public void Cylinder(Vector3 baseCenter, float r, float h, int sides, Quaternion rot, bool caps = true, float r2 = -1f)
        {
            if (r2 < 0f) r2 = r;
            var saved = xf;
            xf = xf * Matrix4x4.TRS(baseCenter, rot, Vector3.one);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                var p0 = new Vector3(Mathf.Cos(a0) * r, 0, Mathf.Sin(a0) * r);
                var p1 = new Vector3(Mathf.Cos(a1) * r, 0, Mathf.Sin(a1) * r);
                var q0 = new Vector3(Mathf.Cos(a0) * r2, h, Mathf.Sin(a0) * r2);
                var q1 = new Vector3(Mathf.Cos(a1) * r2, h, Mathf.Sin(a1) * r2);
                Quad(p0, q0, q1, p1);
                if (caps)
                {
                    Tri(new Vector3(0, h, 0), q1, q0);
                    Tri(Vector3.zero, p0, p1);
                }
            }
            xf = saved;
        }

        public void Cylinder(Vector3 baseCenter, float r, float h, int sides = 12, bool caps = true)
            => Cylinder(baseCenter, r, h, sides, Quaternion.identity, caps);

        /// <summary>Gable prism. Eaves rectangle [min.x,max.x]x[min.z,max.z] at y=eaveY, ridge along X (ridgeAlongX) or Z.</summary>
        public void Gable(Vector3 min, Vector3 max, float eaveY, float ridgeY, bool ridgeAlongX, bool bottom = true)
        {
            float x0 = min.x, x1 = max.x, z0 = min.z, z1 = max.z;
            if (ridgeAlongX)
            {
                float zm = (z0 + z1) * 0.5f;
                var e0 = new Vector3(x0, eaveY, z0); var e1 = new Vector3(x1, eaveY, z0);
                var e2 = new Vector3(x1, eaveY, z1); var e3 = new Vector3(x0, eaveY, z1);
                var r0 = new Vector3(x0, ridgeY, zm); var r1 = new Vector3(x1, ridgeY, zm);
                Quad(e0, r0, r1, e1);          // front slope (-z)
                Quad(e2, r1, r0, e3);          // back slope (+z)
                Tri(e3, r0, e0);               // -x gable
                Tri(e1, r1, e2);               // +x gable
                if (bottom) Quad(e3, e0, e1, e2);
            }
            else
            {
                float xm = (x0 + x1) * 0.5f;
                var e0 = new Vector3(x0, eaveY, z0); var e1 = new Vector3(x1, eaveY, z0);
                var e2 = new Vector3(x1, eaveY, z1); var e3 = new Vector3(x0, eaveY, z1);
                var r0 = new Vector3(xm, ridgeY, z0); var r1 = new Vector3(xm, ridgeY, z1);
                Quad(e3, r1, r0, e0);          // -x slope
                Quad(e1, r0, r1, e2);          // +x slope
                Tri(e0, r0, e1);               // -z gable
                Tri(e2, r1, e3);               // +z gable
                if (bottom) Quad(e3, e0, e1, e2);
            }
        }

        public Mesh Build(string name = "mesh")
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        public void Clear() { v.Clear(); n.Clear(); uv.Clear(); t.Clear(); xf = Matrix4x4.identity; }
    }
}
