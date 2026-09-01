using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Shared.Capture
{
    [Serializable]
    public class Pose
    {
        public string name;
        public float[] pos;
        public float[] lookAt;
        public float fov = 60f;
        public float timeSec = 0f;
        public Vector3 Pos => new Vector3(pos[0], pos[1], pos[2]);
        public Vector3 LookAt => new Vector3(lookAt[0], lookAt[1], lookAt[2]);
    }

    [Serializable]
    class PoseList { public Pose[] poses; }

    /// <summary>
    /// Deterministic capture rig. Started from the command line (-piece X [-poses a,b]
    /// [-shots dir] [-seed n]) in the editor (Capture.Entry.Run enters play mode) or in a
    /// player. Advances the simulation with a fixed 60 fps capture framerate, places the
    /// camera per pose, waits two frames, renders 1920x1080 into a RenderTexture and
    /// writes shots/&lt;piece&gt;/&lt;pose&gt;.png. Prints CAPTURE_OK n shots / CAPTURE_FAIL reason
    /// and exits with 0 / 1.
    /// </summary>
    public class CaptureRunner : MonoBehaviour
    {
        public const int Width = 1920, Height = 1080;
        public string piece = "harness";
        public string[] only;          // pose names filter, null = all
        public string shotsDir;        // absolute dir; default <repo>/shots
        public string posesPath;       // absolute path; default <repo>/tools/poses.json
        public int settleFrames = 3;
        public int warmupFrames = 10;   // first frames render before textures/skybox are uploaded

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            var piece = Bootstrap.Arg("-piece");
            if (string.IsNullOrEmpty(piece)) return;
            Begin(piece, Bootstrap.Arg("-poses"), Bootstrap.Arg("-shots"), Bootstrap.Arg("-posesFile"));
        }

        public static string RepoRoot
        {
            get
            {
                // editor: <repo>/unity/Assets -> <repo>; player: <repo>/builds/mac/AOT.app/Contents -> <repo>
                var p = Application.dataPath;
                for (int i = 0; i < 6; i++)
                {
                    var parent = Path.GetDirectoryName(p);
                    if (parent == null) break;
                    if (File.Exists(Path.Combine(parent, "tools", "poses.json"))) return parent;
                    p = parent;
                }
                return Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            }
        }

        public static CaptureRunner Begin(string piece, string poses = null, string shotsDir = null, string posesFile = null)
        {
            var go = new GameObject("CaptureRunner");
            var r = go.AddComponent<CaptureRunner>();
            r.piece = piece;
            r.only = string.IsNullOrEmpty(poses) || poses == "all" ? null : poses.Split(',');
            r.shotsDir = shotsDir ?? Path.Combine(RepoRoot, "shots");
            r.posesPath = posesFile ?? Path.Combine(RepoRoot, "tools", "poses.json");
            return r;
        }

        IEnumerator Start()
        {
            Bootstrap.Ensure();
            Time.captureFramerate = 60;
            Time.fixedDeltaTime = 1f / 60f;
            Application.targetFrameRate = -1;
            List<Pose> poses;
            try { poses = LoadPoses(); }
            catch (Exception e) { Fail("poses: " + e.Message); yield break; }
            if (poses.Count == 0) { Fail("no poses matched"); yield break; }
            Log("[Capture] start piece=" + piece + " poses=" + poses.Count + " frame=" + Time.frameCount);
            yield return null;
            Log("[Capture] ticking frame=" + Time.frameCount + " t=" + Time.time.ToString("0.000"));
            for (int i = 0; i < warmupFrames; i++) yield return null;

            var cam = Ctx.Get<Camera>("camera") ?? Camera.main;
            if (cam == null) { Fail("no camera"); yield break; }
            var orbit = cam.GetComponent<OrbitCamera>();
            if (orbit != null) orbit.enabled = false;

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            rt.Create();
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var outDir = Path.Combine(shotsDir, piece);
            Directory.CreateDirectory(outDir);

            float t0 = Time.time;
            int n = 0;
            foreach (var p in poses)
            {
                // advance simulation to the pose time (never rewind)
                while (Time.time - t0 < p.timeSec - 1e-4f) yield return null;
                cam.transform.position = p.Pos;
                cam.transform.LookAt(p.LookAt);
                cam.fieldOfView = p.fov;
                // NOTE: no WaitForEndOfFrame here: it never fires in -batchmode (no game view).
                for (int i = 0; i < settleFrames; i++) yield return null;
                try
                {
                    var prevTarget = cam.targetTexture;
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = prevTarget;
                    var prevActive = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prevActive;
                    var path = Path.Combine(outDir, p.name + ".png");
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    n++;
                    Log("[Capture] wrote " + path + " t=" + (Time.time - t0).ToString("0.00"));
                }
                catch (Exception e) { Fail("render " + p.name + ": " + e.Message); yield break; }
            }
            if (orbit != null) orbit.enabled = true;
            Ok(n);
        }

        List<Pose> LoadPoses()
        {
            var text = File.ReadAllText(posesPath).Trim();
            var list = JsonUtility.FromJson<PoseList>("{\"poses\":" + text + "}");
            var result = new List<Pose>();
            if (list?.poses == null) throw new Exception("could not parse " + posesPath);
            var wanted = only == null ? null : new HashSet<string>(only);
            foreach (var p in list.poses)
            {
                if (p.pos == null || p.pos.Length != 3 || p.lookAt == null || p.lookAt.Length != 3)
                    throw new Exception("pose '" + p.name + "' needs pos[3] and lookAt[3]");
                if (wanted == null || wanted.Contains(p.name)) result.Add(p);
            }
            if (wanted != null)
                foreach (var w in wanted)
                    if (!result.Exists(x => x.name == w)) throw new Exception("unknown pose '" + w + "'");
            result.Sort((a, b) => a.timeSec.CompareTo(b.timeSec));
            return result;
        }

        static void Log(string s) { Debug.Log(s); Console.WriteLine(s); }

        static void Ok(int n)
        {
            Log("CAPTURE_OK " + n + " shots");
            Exit(0);
        }

        static void Fail(string reason)
        {
            Log("CAPTURE_FAIL " + reason);
            Debug.LogError("CAPTURE_FAIL " + reason);
            Exit(1);
        }

        static void Exit(int code)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }
    }
}
