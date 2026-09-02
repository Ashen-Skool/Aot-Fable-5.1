using System.Collections.Generic;
using Shared;
using Shared.Cam;
using UnityEngine;

namespace AotCamera
{
    /// <summary>
    /// Stand-in for the ODM controller: a scripted, time-exact flight through the stub street
    /// (wall dive, street run, boost, roof landing with impact, kill cam, loop) so the camera can
    /// be captured at speed without the real character. Registers nothing in Ctx; the rig uses
    /// it only while no "cameraTarget" is registered, and it removes itself when one appears.
    /// </summary>
    public class DemoTarget : MonoBehaviour, ICameraTarget
    {
        struct Key
        {
            public float t; public Vector3 p; public CameraTargetState state;
            public Key(float t, Vector3 p, CameraTargetState s) { this.t = t; this.p = p; state = s; }
        }

        public const float LoopFrom = 3.2f;        // after the first pass, restart from the street entry
        public float DiveAt = 0.55f, DiveDuration = 2.8f, HitAt = 7.5f, KillAt = 8.8f;

        readonly List<Key> keys = new List<Key>(24);
        readonly List<Vector3> dense = new List<Vector3>(2048);   // spline samples
        readonly List<float> denseT = new List<float>(2048);      // time at each sample
        Transform body;
        GameObject root;
        Vector3 pos, vel, fwd = Vector3.forward;
        CameraTargetState state = CameraTargetState.Grounded;
        float t0 = -1f, loopOffset, hitUntil = -1f;
        bool diveDone, killDone;
        int cursor;
        Vector3 titanNape = new Vector3(0f, 13.5f, 61f);

        public Vector3 Position => pos;
        public Vector3 Velocity => vel;
        public Vector3 Forward => fwd;
        public CameraTargetState State => state;
        public Transform Root => root != null ? root.transform : null;
        public float Elapsed => t0 < 0 ? 0f : Time.time - t0;

        public static DemoTarget Create()
        {
            var go = new GameObject("CameraDemo");
            return go.AddComponent<DemoTarget>();
        }

        void Awake()
        {
            root = gameObject;
            var titan = Ctx.Get<GameObject>("titan");
            if (titan != null) titanNape = titan.transform.position + new Vector3(0f, 6f, 1f);

            BuildProps();
            BuildBody();
            // wall top -> dive -> street -> boost past the titan -> land on a roof (impact) -> jump to the nape (kill cam) -> loop
            var G = CameraTargetState.Grounded; var F = CameraTargetState.Flying; var B = F | CameraTargetState.Boosting;
            keys.Add(new Key(0.0f, new Vector3(0f, 50.9f, -72f), G));
            keys.Add(new Key(0.8f, new Vector3(0f, 50.9f, -71f), G));
            keys.Add(new Key(2.0f, new Vector3(0f, 28f, -62f), F));
            keys.Add(new Key(3.2f, new Vector3(0f, 5f, -45f), F));
            keys.Add(new Key(4.6f, new Vector3(2f, 6f, -8f), F));      // street run, through the pillars
            keys.Add(new Key(5.6f, new Vector3(-3f, 9f, 30f), B));     // boost down the street
            keys.Add(new Key(6.6f, new Vector3(-6f, 16f, 62f), B));    // past the titan's left shoulder
            keys.Add(new Key(7.0f, new Vector3(-15f, 19f, 82f), F));   // swing round
            keys.Add(new Key(7.5f, new Vector3(-14f, 14.6f, 75f), G)); // roof landing facing the titan = impact
            keys.Add(new Key(8.0f, new Vector3(-13.8f, 14.6f, 74.7f), G));
            keys.Add(new Key(8.8f, new Vector3(1f, 15f, 63f), F));     // lunge at the nape -> kill cam
            keys.Add(new Key(9.4f, new Vector3(1.2f, 15.2f, 63.5f), F));
            keys.Add(new Key(9.9f, new Vector3(-4f, 15f, 50f), F));
            keys.Add(new Key(11.1f, new Vector3(-9f, 11f, 20f), F));
            keys.Add(new Key(12.3f, new Vector3(0f, 6f, -10f), F));
            keys.Add(new Key(13.5f, new Vector3(2f, 5f, -45f), F));
            keys.Add(new Key(14.1f, new Vector3(0f, 5f, -60f), F));
            BakeSpline();
            pos = keys[0].p;
            body.position = pos;
        }

        void BuildProps()
        {
            var stone = Mats.Lit(new Color(0.5f, 0.49f, 0.47f), 0.08f);
            Box("Wall", new Vector3(0f, 25f, -75f), new Vector3(220f, 50f, 8f), stone);
            Box("Box_Landing", new Vector3(-14f, 7f, 75f), new Vector3(8f, 14f, 8f), stone);
            Box("Pillar_L", new Vector3(-4.5f, 4.5f, -5f), new Vector3(1.6f, 9f, 1.6f), stone);
            Box("Pillar_R", new Vector3(4.5f, 4.5f, -5f), new Vector3(1.6f, 9f, 1.6f), stone);
            Box("Box_Mid", new Vector3(-9f, 6f, 30f), new Vector3(6f, 12f, 6f), stone);
        }

        void Box(string name, Vector3 at, Vector3 size, Material m)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = name;
            b.transform.SetParent(transform, false);
            b.transform.position = at;
            b.transform.localScale = size;
            b.GetComponent<Renderer>().sharedMaterial = m;
        }

        void BuildBody()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "DemoMikasa";
            go.transform.SetParent(transform, false);
            go.transform.localScale = new Vector3(0.5f, 0.85f, 0.5f);
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = Mats.Lit(new Color(0.14f, 0.14f, 0.17f), 0.3f);
            var scarf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            scarf.name = "Scarf";
            scarf.transform.SetParent(go.transform, false);
            scarf.transform.localPosition = new Vector3(0, 0.62f, 0);
            scarf.transform.localScale = new Vector3(1.3f, 0.2f, 1.3f);
            Destroy(scarf.GetComponent<Collider>());
            scarf.GetComponent<Renderer>().sharedMaterial = Mats.Lit(new Color(0.75f, 0.1f, 0.08f), 0.2f);
            body = go.transform;
        }

        void BakeSpline()
        {
            const int perSeg = 24;
            for (int i = 0; i < keys.Count - 1; i++)
            {
                var p0 = keys[Mathf.Max(i - 1, 0)].p; var p1 = keys[i].p; var p2 = keys[i + 1].p; var p3 = keys[Mathf.Min(i + 2, keys.Count - 1)].p;
                // arc-length table for this segment so speed is constant inside it
                int m = perSeg;
                float total = 0f; int start = dense.Count;
                for (int s = 0; s <= m; s++)
                {
                    var p = CatmullRom(p0, p1, p2, p3, s / (float)m);
                    if (s > 0) total += Vector3.Distance(p, dense[dense.Count - 1]);
                    dense.Add(p); denseT.Add(total);
                }
                for (int s = start; s < dense.Count; s++)
                    denseT[s] = Mathf.Lerp(keys[i].t, keys[i + 1].t, total > 1e-4f ? denseT[s] / total : (s - start) / (float)m);
            }
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            float u2 = u * u, u3 = u2 * u;
            return 0.5f * ((2f * p1) + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
        }

        Vector3 Sample(float t)
        {
            if (t <= denseT[0]) return dense[0];
            int last = dense.Count - 1;
            if (t >= denseT[last]) return dense[last];
            if (cursor >= last || denseT[cursor] > t) cursor = 0;
            while (cursor < last - 1 && denseT[cursor + 1] < t) cursor++;
            float a = denseT[cursor], b = denseT[cursor + 1];
            float f = b > a ? (t - a) / (b - a) : 0f;
            return Vector3.LerpUnclamped(dense[cursor], dense[cursor + 1], f);
        }

        CameraTargetState StateAt(float t)
        {
            for (int i = keys.Count - 1; i >= 0; i--) if (t >= keys[i].t) return keys[i].state;
            return keys[0].state;
        }

        void Update()
        {
            if (Ctx.Get<ICameraTarget>(ICameraTarget.CtxName) != null) { Destroy(gameObject); return; }
            if (t0 < 0f) t0 = Time.time;
            float dt = Time.deltaTime;
            float t = Time.time - t0 - loopOffset;
            float end = keys[keys.Count - 1].t;
            if (t >= end) { loopOffset += end - LoopFrom; t = LoopFrom; }

            var next = Sample(t);
            vel = dt > 1e-5f ? (next - pos) / dt : vel;
            pos = next;
            if (vel.sqrMagnitude > 0.5f) fwd = Vector3.Slerp(fwd, vel.normalized, 1f - Mathf.Exp(-8f * dt));
            var s = StateAt(t);
            // the roof landing at 8.0 s is an impact: one frame of Hit
            if (!killDone && t >= HitAt && t < HitAt + 0.2f) { s |= CameraTargetState.Hit; hitUntil = t + 0.12f; }
            if (t < hitUntil) s |= CameraTargetState.Hit;
            state = s;

            body.position = pos;
            if (fwd.sqrMagnitude > 1e-4f)
            {
                var look = Quaternion.LookRotation(fwd, Vector3.up);
                var lean = (s & CameraTargetState.Flying) != 0 ? Quaternion.Euler(55f, 0, 0) : Quaternion.identity;
                body.rotation = look * lean;
            }

            var rig = Ctx.Get<CameraRig>(CameraRig.CtxName);
            if (rig == null) return;
            if (!diveDone && loopOffset == 0f && t >= DiveAt)
            {
                diveDone = true;
                rig.CinematicDive(new Vector3(4f, 56f, -82f), new Vector3(0f, 20f, 20f), DiveDuration);
            }
            if (!killDone && loopOffset == 0f && t >= KillAt)
            {
                killDone = true;
                rig.KillCam(titanNape);
            }
        }
    }
}
