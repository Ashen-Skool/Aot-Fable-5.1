using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shared
{
    /// <summary>
    /// "Play again": the world is built by one-shot RuntimeInitializeOnLoad methods, so a plain scene reload lands on an
    /// empty scene. Each boot registers its entry point here (with a priority) and Reboot.Now() reloads the scene, clears
    /// Ctx, and runs them again in order.
    /// </summary>
    public static class Reboot
    {
        static readonly List<(int prio, Action run)> steps = new List<(int, Action)>();
        static bool pending;
        public static bool Restarting => pending;
        public static void Register(int priority, Action run) { steps.RemoveAll(s => s.run == run); steps.Add((priority, run)); }

        public static void Now()
        {
            if (pending) return;
            pending = true;
            Time.timeScale = 1f; Time.fixedDeltaTime = 1f / 60f;
            SceneManager.sceneLoaded += OnLoaded;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        static void OnLoaded(Scene s, LoadSceneMode m)
        {
            SceneManager.sceneLoaded -= OnLoaded;
            Ctx.Clear();
            HudEvents.Pops.Clear();
            steps.Sort((a, b) => a.prio.CompareTo(b.prio));
            foreach (var st in steps.ToArray())
            {
                try { st.run(); } catch (Exception e) { Debug.LogException(e); }
            }
            pending = false;
            Debug.Log("[Reboot] world rebuilt (" + steps.Count + " steps)");
        }
    }
}
