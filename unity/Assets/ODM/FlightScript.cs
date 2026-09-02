using System;
using System.Collections.Generic;
using UnityEngine;

namespace ODM
{
    /// <summary>
    /// Deterministic input replay for the ODM controller. A script is a list of keyframes;
    /// each keyframe's input holds from its time until the next keyframe. The controller
    /// samples it once per FixedUpdate against its own fixed clock, so the same script on
    /// the same seed gives the same flight every time (capture rig, tests).
    ///
    /// Can also record live input (Record/Stop) and round-trip through JSON.
    /// </summary>
    [Serializable]
    public class FlightScript
    {
        [Serializable]
        public class Key
        {
            public float t;
            public OdmInput input;
            public string label;
        }

        public string name = "script";
        public List<Key> keys = new List<Key>(64);
        public float endTime;     // script is finished after this

        int cursor;
        bool playing;
        float clock;

        public bool Playing => playing;
        public bool Finished => !playing && clock >= endTime && keys.Count > 0;
        public float Clock => clock;
        public string CurrentLabel => cursor > 0 && cursor <= keys.Count ? keys[cursor - 1].label : "";

        public FlightScript Add(float t, float moveX, float moveY, bool hook, bool boost, bool reel, Vector3? aim = null, string label = null, Vector3? look = null)
        {
            keys.Add(new Key
            {
                t = t,
                label = label ?? "",
                input = new OdmInput
                {
                    moveX = moveX, moveY = moveY, hook = hook, boost = boost, reel = reel,
                    hasAim = aim.HasValue, aimPoint = aim ?? Vector3.zero,
                    hasLook = look.HasValue, lookPoint = look ?? Vector3.zero
                }
            });
            if (t > endTime) endTime = t;
            return this;
        }

        public FlightScript End(float t) { endTime = Mathf.Max(endTime, t); return this; }

        public void Play()
        {
            keys.Sort((a, b) => a.t.CompareTo(b.t));
            cursor = 0; clock = 0f; playing = keys.Count > 0;
        }

        public void Stop() { playing = false; }

        /// <summary>Advance by dt and write the active keyframe's input. Returns false when the script is over.</summary>
        public bool Step(float dt, ref OdmInput input)
        {
            if (!playing) return false;
            while (cursor < keys.Count && keys[cursor].t <= clock + 1e-5f) cursor++;
            if (cursor > 0) input = keys[cursor - 1].input;
            clock += dt;
            if (clock >= endTime && cursor >= keys.Count)
            {
                playing = false;
                input = default;
                return false;
            }
            return true;
        }

        // ---- recording ----
        OdmInput lastRecorded; bool recording; float recClock;
        public bool Recording => recording;

        public void Record() { keys.Clear(); endTime = 0; recClock = 0; recording = true; lastRecorded = new OdmInput { moveX = float.NaN }; }

        public void RecordStep(float dt, in OdmInput input)
        {
            if (!recording) return;
            if (!Same(input, lastRecorded))
            {
                keys.Add(new Key { t = recClock, input = input, label = "" });
                lastRecorded = input;
            }
            recClock += dt;
            endTime = recClock;
        }

        public void StopRecording() { recording = false; }

        static bool Same(in OdmInput a, in OdmInput b) =>
            a.moveX == b.moveX && a.moveY == b.moveY && a.hook == b.hook && a.boost == b.boost &&
            a.reel == b.reel && a.hasAim == b.hasAim && (a.aimPoint - b.aimPoint).sqrMagnitude < 1e-4f &&
            a.hasLook == b.hasLook && (a.lookPoint - b.lookPoint).sqrMagnitude < 1e-4f;

        public string ToJson() => JsonUtility.ToJson(this, true);
        public static FlightScript FromJson(string json) => JsonUtility.FromJson<FlightScript>(json);

        /// <summary>
        /// The demo flight the capture rig replays: run, hook a tower ahead-right, boost up,
        /// release into an arc, hook a tower across the street, boost through the gap, hook
        /// a third tower just under its roof, reel up and land on it. Aim points come from
        /// the HookTestGrid so the script follows the seeded geometry.
        /// </summary>
        public const int A1c = 3, A1r = 1, A2c = 2, A2r = 2, A3c = 3, A3r = 4;
        public static Vector3 DemoA1(HookTestGrid g) => g.WallNearTop(A1c, A1r, 2.5f);   // x=+22, z=-44
        public static Vector3 DemoA2(HookTestGrid g) => g.WallNearTop(A2c, A2r, 2.5f);   // x=-22, z=-22
        public static Vector3 DemoA3(HookTestGrid g) => g.WallNearTop(A3c, A3r, 0.8f);   // x=+22, z=22 (landing roof)
        public static Vector3 DemoLandRoof(HookTestGrid g) => g.RoofTop(A3c, A3r);

        public static FlightScript Demo(HookTestGrid g)
        {
            var s = new FlightScript { name = "demo" };
            var a1 = DemoA1(g); var a2 = DemoA2(g); var a3 = DemoA3(g);
            var ahead = new Vector3(0, 6, 0);
            var up1 = new Vector3(0, 34, -10);        // look: up the street and skyward, so boosts climb
            var up2 = new Vector3(0, 34, 40);
            var roof = DemoLandRoof(g);
            s.Add(0.00f, 0, 1, false, false, false, ahead, "run");
            s.Add(0.40f, 0, 1, true, false, false, a1, "hook1", up1);
            s.Add(0.50f, 0, 1, true, true, false, a1, "boost1", up1);
            s.Add(1.10f, 0, 1, false, false, false, a1, "release1", up1);
            s.Add(1.35f, 0, 1, true, false, false, a2, "hook2", up2);
            s.Add(1.45f, 0, 1, true, true, false, a2, "boost2", up2);
            s.Add(1.95f, 0, 1, false, false, false, a2, "release2", up2);
            s.Add(2.45f, 0, 1, true, false, false, a3, "hook3", roof);
            s.Add(2.55f, 0, 1, true, true, false, a3, "boost3", roof);
            s.Add(2.75f, 0, 0, true, false, true, a3, "reel3", roof);
            s.Add(6.50f, 0, 0, false, false, false, a3, "land", roof);
            s.End(8.5f);
            return s;
        }

        /// <summary>Where the demo starts: on the street south of the placeholder houses, facing +z.</summary>
        public static Vector3 DemoStart(HookTestGrid g) => new Vector3(0f, 1.0f, -76f);
    }
}
