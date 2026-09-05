using System.Collections;
using UnityEngine;

namespace Shared
{
    /// <summary>A few frames of near-freeze on a heavy hit. Skipped while something else (kill cam, title) already owns the clock.</summary>
    public class HitStop : MonoBehaviour
    {
        static HitStop inst; float until; bool active; float saved = 1f;
        public static void Do(float seconds, float scale = 0.04f)
        {
            if (Application.isBatchMode) return;
            if (inst == null) { var go = new GameObject("HitStop"); DontDestroyOnLoad(go); inst = go.AddComponent<HitStop>(); }
            if (!inst.active && Time.timeScale < 0.5f) return;      // slow-mo or paused by someone else
            if (!inst.active) { inst.saved = Time.timeScale; inst.active = true; }
            Time.timeScale = scale;
            inst.until = Mathf.Max(inst.until, Time.unscaledTime + seconds);
        }
        void Update()
        {
            if (!active || Time.unscaledTime < until) return;
            active = false;
            if (Time.timeScale < 0.5f) Time.timeScale = saved;
        }
    }
}
