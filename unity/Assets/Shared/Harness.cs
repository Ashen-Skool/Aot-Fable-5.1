using UnityEngine;

namespace Shared
{
    /// <summary>
    /// Command-line self-checks for a built player: -quitAfter N (seconds), -autoRestart N (calls Reboot.Now at N s, once),
    /// -fpslog (average frame rate to the log every 2 s). Lets a headless run prove things the editor tests cannot.
    /// </summary>
    public class Harness : MonoBehaviour
    {
        static bool restarted; static Harness inst;
        float quitAt = -1f, restartAt = -1f; bool fps; float acc; int n; float t0;

        public static void Ensure()
        {
            if (inst != null) return;
            float q = Bootstrap.ArgInt("-quitAfter", -1), r = Bootstrap.ArgInt("-autoRestart", -1);
            bool f = Bootstrap.Arg("-fpslog", null) != null || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-fpslog") >= 0;
            if (q < 0f && r < 0f && !f) return;
            var go = new GameObject("Harness"); DontDestroyOnLoad(go);
            inst = go.AddComponent<Harness>(); inst.quitAt = q; inst.restartAt = restarted ? -1f : r; inst.fps = f; inst.t0 = Time.realtimeSinceStartup;
            Debug.Log("[Harness] quitAfter=" + q + " autoRestart=" + r + " fpslog=" + f);
        }

        void Update()
        {
            float t = Time.realtimeSinceStartup - t0;
            if (fps)
            {
                acc += Time.unscaledDeltaTime; n++;
                if (acc >= 2f) { Debug.Log("[FPS] " + (n / acc).ToString("0.0") + " avg over " + acc.ToString("0.0") + " s at t=" + t.ToString("0")); acc = 0f; n = 0; }
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

        void AfterRestart()
        {
            var town = Ctx.Get<object>("town"); var player = Ctx.Get<Component>("player"); var boss = Ctx.Get<Component>("bossBrain");
            int houses = 0; var root = GameObject.Find("Town"); if (root != null) houses = root.transform.childCount;
            Debug.Log("[Harness] after restart: town=" + (town != null) + " player=" + (player != null) + " boss=" + (boss != null) + " townChildren=" + houses + " timeScale=" + Time.timeScale + " -> " + ((town != null && player != null && boss != null) ? "RESTART_OK" : "RESTART_FAIL"));
        }
    }
}
