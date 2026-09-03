using UnityEngine;

namespace Shared
{
    /// <summary>Orient generated props from their own geometry instead of trusting exporter axes.</summary>
    public static class PropAlign
    {
        /// <summary>
        /// Rotate <paramref name="meshRoot"/> so its longest axis runs along +Z of its parent, with the end that has the
        /// smaller cross-section (tipSmaller) or larger one facing +Z; then scale the long axis to <paramref name="length"/>
        /// and put <paramref name="pivotFrac"/> along the axis (0 = back end, 1 = tip) at the parent origin.
        /// </summary>
        public static void Align(Transform meshRoot, bool tipSmaller, float length, float pivotFrac)
        {
            var mf = meshRoot.GetComponentInChildren<MeshFilter>(); if (mf == null || mf.sharedMesh == null) return;
            var mesh = mf.sharedMesh; var verts = mesh.vertices; var b = mesh.bounds; var e = b.size;
            int axis = e.x >= e.y && e.x >= e.z ? 0 : (e.y >= e.z ? 1 : 2);
            int[] others = axis == 0 ? new[] { 1, 2 } : axis == 1 ? new[] { 0, 2 } : new[] { 0, 1 };
            float lo = b.min[axis], hi = b.max[axis], span = hi - lo;
            float Cross(bool lowEnd)
            {
                float a0 = lowEnd ? lo : hi - span * 0.2f, a1 = lowEnd ? lo + span * 0.2f : hi;
                float min0 = float.MaxValue, max0 = float.MinValue, min1 = float.MaxValue, max1 = float.MinValue; int n = 0;
                foreach (var v in verts) { float a = v[axis]; if (a < a0 || a > a1) continue; n++; min0 = Mathf.Min(min0, v[others[0]]); max0 = Mathf.Max(max0, v[others[0]]); min1 = Mathf.Min(min1, v[others[1]]); max1 = Mathf.Max(max1, v[others[1]]); }
                return n == 0 ? 0f : (max0 - min0) + (max1 - min1);
            }
            bool lowIsSmaller = Cross(true) < Cross(false);
            float tipSign = (tipSmaller == lowIsSmaller) ? -1f : 1f;   // +1 = tip at the high end of the axis
            Vector3 tipLocal = Vector3.zero; tipLocal[axis] = tipSign;
            // rotate the mesh so tipLocal -> parent +Z (work in the mesh transform's local frame)
            var mt = mf.transform;
            Vector3 tipWorld = mt.TransformDirection(tipLocal).normalized;
            Vector3 targetWorld = meshRoot.parent != null ? meshRoot.parent.forward : Vector3.forward;
            meshRoot.rotation = Quaternion.FromToRotation(tipWorld, targetWorld) * meshRoot.rotation;
            // scale
            float worldSpan = (mt.TransformVector(tipLocal * span)).magnitude;
            float s = length / Mathf.Max(1e-4f, worldSpan);
            meshRoot.localScale *= s;
            // pivot: point at pivotFrac along the axis, centred on the other two axes, to the parent origin
            Vector3 pivotLocal = b.center; pivotLocal[axis] = tipSign > 0 ? lo + span * pivotFrac : hi - span * pivotFrac;
            Vector3 pivotWorld = mt.TransformPoint(pivotLocal);
            Vector3 originWorld = meshRoot.parent != null ? meshRoot.parent.position : Vector3.zero;
            meshRoot.position += originWorld - pivotWorld;
        }

        public static Material TexturedLit(string texFolder, float smooth, float metal)
        {
            var mat = Mats.Lit(Color.white, smooth, metal);
            var bc = Resources.Load<Texture2D>(texFolder + "/base_color"); if (bc != null) { mat.SetTexture("_BaseMap", bc); mat.mainTexture = bc; }
            var nr = Resources.Load<Texture2D>(texFolder + "/normal"); if (nr != null) { mat.EnableKeyword("_NORMALMAP"); mat.SetTexture("_BumpMap", nr); }
            return mat;
        }
    }
}
