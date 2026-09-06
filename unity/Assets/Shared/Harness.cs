using UnityEngine;
using Unity.Profiling;

namespace Shared
{
    /// <summary>
    /// Command-line self-checks for a built player: -quitAfter N (seconds), -autoRestart N (calls Reboot.Now at N s, once),
    /// -autoStabs N (N LMB presses 0.6 s apart starting at -autoSlash: the whole nape kill), -autoPerch N (hook the nearest wall face),
    /// -fpslog (average frame rate to the log every 2 s). Lets a headless run prove things the editor tests cannot.
    /// </summary>
    public class Harness : MonoBehaviour
    {
        static bool restarted; static Harness inst;
        public static bool Active => inst != null;
        float quitAt = -1f, restartAt = -1f, autoStart = -1f, autoKill = -1f, autoFly = -1f, autoSlash = -1f, autoPause = -1f, autoRide = -1f, autoPerch = -1f; bool titanLog; Transform hips; bool fps; float acc; int n; float t0; bool counted, started, killed, flew, slashed, paused, rode, perched;
        float[] shotAt = new float[0]; int shotIdx; string shotDir;
        int autoStabs; int stabsSent, presses; float nextStabAt; public float stabGap = 0.6f;
        readonly FrameTiming[] timings = new FrameTiming[1]; double cpuMain, cpuRender, gpu; int tn;

        public static void Ensure()
        {
            if (inst != null) return;
            float q = Bootstrap.ArgInt("-quitAfter", -1), r = Bootstrap.ArgInt("-autoRestart", -1);
            bool f = Bootstrap.Arg("-fpslog", null) != null || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-fpslog") >= 0;
            float a = Bootstrap.ArgInt("-autoStart", -1); float k = Bootstrap.ArgInt("-autoKill", -1); float fl = Bootstrap.ArgInt("-autoFly", -1); float sl = Bootstrap.ArgInt("-autoSlash", -1); float pa = Bootstrap.ArgInt("-autoPause", -1); float ri = Bootstrap.ArgInt("-autoRide", -1); float pe = Bootstrap.ArgInt("-autoPerch", -1);
            int stabs = Bootstrap.ArgInt("-autoStabs", 0);
            string shots = Bootstrap.Arg("-screenshotAt"); string dir = Bootstrap.Arg("-shotDir", "shots/play");
            bool tl = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-titanLog") >= 0;
            if (q < 0f && r < 0f && !f && a < 0f && k < 0f && fl < 0f && sl < 0f && pa < 0f && pe < 0f && stabs <= 0 && !tl && shots == null) return;
            var go = new GameObject("Harness"); DontDestroyOnLoad(go);
            inst = go.AddComponent<Harness>(); inst.quitAt = q; inst.restartAt = restarted ? -1f : r; inst.fps = f; inst.t0 = Time.realtimeSinceStartup; inst.autoStart = a; inst.autoKill = k; inst.autoFly = fl; inst.autoSlash = sl; inst.autoPause = pa; inst.autoRide = ri; inst.autoStabs = stabs; inst.nextStabAt = sl; inst.autoPerch = pe; inst.shotDir = dir; inst.titanLog = tl;
            if (shots != null) { var parts = shots.Split(','); inst.shotAt = new float[parts.Length]; for (int i = 0; i < parts.Length; i++) float.TryParse(parts[i], out inst.shotAt[i]); System.IO.Directory.CreateDirectory(dir); }
            Debug.Log("[Harness] quitAfter=" + q + " autoRestart=" + r + " fpslog=" + f);
        }

        void Update()
        {
            float t = Time.realtimeSinceStartup - t0;
            if (autoStart >= 0f && !started && t >= autoStart) { started = true; Ctx.Set("autoStart", true); Debug.Log("[Harness] autoStart at t=" + t.ToString("0.0")); }
            if (titanLog && Time.frameCount % 30 == 0)
            {
                var pl = Ctx.Get<Component>("player"); var prb = pl != null ? pl.GetComponent<Rigidbody>() : null;
                if (prb != null) Debug.Log("[Player] t=" + Time.time.ToString("0.0") + " pos=" + prb.position.ToString("0.0") + " vel=" + prb.linearVelocity.ToString("0.0") + " kin=" + prb.isKinematic + " ts=" + Time.timeScale);
            }
            if (titanLog)
            {
                var boss = Ctx.Get<Component>("bossBrain");
                if (boss != null)
                {
                    if (hips == null) { var an = boss.GetComponentInChildren<Animator>(); if (an != null && an.isHuman) hips = an.GetBoneTransform(HumanBodyBones.Hips); }
                    var p = boss.transform.position; var f = boss.transform.forward; var hp = hips != null ? hips.position : p;
                    Debug.Log("[TL] " + t.ToString("0.000") + " " + p.x.ToString("0.000") + " " + p.z.ToString("0.000") + " " + f.x.ToString("0.000") + " " + f.z.ToString("0.000") + " " + hp.x.ToString("0.000") + " " + hp.y.ToString("0.000") + " " + hp.z.ToString("0.000") + " " + Time.deltaTime.ToString("0.0000"));
                }
            }
            // -autoStabs N: N LMB presses stabGap apart from the autoSlash time (five stabs = the whole nape kill), instead of the single press.
            // A press inside the ride's stab cooldown is swallowed (hit-stop stretches it in real time), so a press that did not
            // land is repeated instead of being lost.
            if (autoStabs > 0 && autoSlash >= 0f && t >= nextStabAt)
            {
                int landed = RideStabs();
                if (landed >= autoStabs || (landed < 0 && stabsSent >= autoStabs) || presses >= autoStabs * 5)
                { Debug.Log("[Harness] autoStabs done: landed=" + landed + " presses=" + presses); autoStabs = 0; }
                else
                {
                    bool retry = landed >= 0 && landed < stabsSent;
                    presses++; Ctx.Set("autoSlash", true);
                    if (!retry) stabsSent++;
                    nextStabAt = t + (retry ? 0.2f : stabGap);
                    Debug.Log("[Harness] autoStab " + (retry ? "retry " + stabsSent : stabsSent + "/" + autoStabs) + " (landed=" + landed + ") at t=" + t.ToString("0.0"));
                }
            }
            else if (autoStabs <= 0 && autoSlash >= 0f && !slashed && t >= autoSlash) { slashed = true; Ctx.Set("autoSlash", true); }
            if (autoPause >= 0f && !paused && t >= autoPause) { paused = true; Ctx.Set("autoPause", true); }
            if (autoRide >= 0f && !rode && t >= autoRide) { rode = true; Ctx.Set("autoRide", true); Debug.Log("[Harness] autoRide at t=" + t.ToString("0.0")); }
            if (autoPerch >= 0f && !perched && t >= autoPerch) { perched = true; Ctx.Set("autoPerch", true); Debug.Log("[Harness] autoPerch at t=" + t.ToString("0.0")); }
            if (autoFly >= 0f && !flew && t >= autoFly) { flew = true; Ctx.Set("autoFly", true); Debug.Log("[Harness] autoFly at t=" + t.ToString("0.0")); }
            if (autoKill >= 0f && !killed && t >= autoKill)
            {
                // open the nape from the harness: HP to 1, then a nape hit through reflection (the brain lives in another assembly)
                killed = true; var brain = Ctx.Get<Component>("bossBrain");
                if (brain != null)
                {
                    var ty = brain.GetType(); ty.GetField("HP")?.SetValue(brain, 1f);
                    ty.GetMethod("NapeKill")?.Invoke(brain, new object[] { brain.transform.position + Vector3.up * 12f });
                    Debug.Log("[Harness] autoKill: nape hit sent at t=" + t.ToString("0.0"));
                }
            }
            if (shotIdx < shotAt.Length && t >= shotAt[shotIdx])
            {
                var path = System.IO.Path.Combine(shotDir, "play_" + shotAt[shotIdx].ToString("0.0").Replace(".", "_") + "s.png");
                ScreenCapture.CaptureScreenshot(path); Debug.Log("[Harness] screenshot " + path); shotIdx++;
            }
            if (fps)
            {
                acc += Time.unscaledDeltaTime; n++;
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0) { cpuMain += timings[0].cpuMainThreadFrameTime; cpuRender += timings[0].cpuRenderThreadFrameTime; gpu += timings[0].gpuFrameTime; tn++; }
                if (acc >= 2f)
                {
                    var rig = Ctx.Get<Component>("cameraRig");
                    if (rig != null)
                    {
                        var ty = rig.GetType();
                        Debug.Log("[Cam] blur=" + ty.GetProperty("BlurIntensity")?.GetValue(rig) + " fov=" + ty.GetProperty("Fov")?.GetValue(rig) + " mode=" + ty.GetProperty("Mode")?.GetValue(rig) + " trauma=" + ty.GetProperty("Trauma")?.GetValue(rig) + " lines=" + (ty.GetProperty("Lines")?.GetValue(rig) as Component)?.GetType().GetProperty("Intensity")?.GetValue(ty.GetProperty("Lines")?.GetValue(rig)) + " pos=" + rig.transform.position.ToString("0.00"));
                    }
                    Debug.Log("[FPS] " + (n / acc).ToString("0.0") + " avg over " + acc.ToString("0.0") + " s at t=" + t.ToString("0")
                        + (tn > 0 ? "  main=" + (cpuMain / tn).ToString("0.0") + "ms render=" + (cpuRender / tn).ToString("0.0") + "ms gpu=" + (gpu / tn).ToString("0.0") + "ms" : ""));
                    acc = 0f; n = 0; cpuMain = cpuRender = gpu = 0; tn = 0;
                }
                if (!counted && t > 4f)
                {
                    counted = true; long verts = 0, tris = 0; int renderers = 0, casters = 0; var seen = new System.Collections.Generic.HashSet<Mesh>();
                    foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                    {
                        if (mf.sharedMesh == null) continue; var r = mf.GetComponent<Renderer>(); if (r == null || !r.enabled) continue;
                        renderers++; if (!seen.Add(mf.sharedMesh)) continue;   // static batching shares one combined mesh across many renderers
                        verts += mf.sharedMesh.vertexCount; tris += mf.sharedMesh.triangles.Length / 3;
                        if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) casters++;
                    }
                    foreach (var sk in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None)) { if (sk.sharedMesh != null) { verts += sk.sharedMesh.vertexCount; tris += sk.sharedMesh.triangles.Length / 3; renderers++; } }
                    Debug.Log("[Scene] renderers=" + renderers + " shadowCasters=" + casters + " verts=" + verts + " tris=" + tris + " vsync=" + QualitySettings.vSyncCount + " target=" + Application.targetFrameRate + " msaa=" + QualitySettings.antiAliasing);
                }
            }
            if (restartAt >= 0f && t >= restartAt && !restarted)
            {
                restarted = true;
                var town = Ctx.Get<object>("town"); var player = Ctx.Get<Component>("player"); var boss = Ctx.Get<Component>("bossBrain");
                Debug.Log("[Harness] before restart: town=" + (town != null) + " player=" + (player != null) + " boss=" + (boss != null));
                Reboot.Now();
                Invoke(nameof(AfterRestart), 3f);
            }
            if (quitAt >= 0f && t >= quitAt) { Debug.Log("[Harness] quitting at t=" + t.ToString("0")); Application.Quit(); }
        }

        /// <summary>Stabs the ride has actually registered (OdmController lives in another assembly). -1 when unknown.</summary>
        static int RideStabs()
        {
            var pl = Ctx.Get<Component>("player"); if (pl == null) return -1;
            var pr = pl.GetType().GetProperty("Stabs"); if (pr == null) return -1;
            return (int)pr.GetValue(pl);
        }

        void AfterRestart()
        {
            var town = Ctx.Get<object>("town"); var player = Ctx.Get<Component>("player"); var boss = Ctx.Get<Component>("bossBrain");
            int houses = 0; var root = GameObject.Find("Town"); if (root != null) houses = root.transform.childCount;
            Debug.Log("[Harness] after restart: town=" + (town != null) + " player=" + (player != null) + " boss=" + (boss != null) + " townChildren=" + houses + " timeScale=" + Time.timeScale + " -> " + ((town != null && player != null && boss != null) ? "RESTART_OK" : "RESTART_FAIL"));
        }
    }
}
