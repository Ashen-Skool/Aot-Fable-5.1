using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Capture
{
    /// <summary>
    /// Batch entry: Unity -batchmode -executeMethod Capture.Entry.Run -piece X [-poses a,b]
    /// Opens Assets/Scenes/Main.unity and enters play mode; Shared.Capture.CaptureRunner
    /// picks the same command-line args up at runtime, captures and exits the editor.
    /// </summary>
    public static class Entry
    {
        public static void Run()
        {
            var piece = Shared.Bootstrap.Arg("-piece");
            if (string.IsNullOrEmpty(piece))
            {
                Console.WriteLine("CAPTURE_FAIL missing -piece");
                EditorApplication.Exit(1);
                return;
            }
            Console.WriteLine("[Capture.Entry] piece=" + piece + " poses=" + (Shared.Bootstrap.Arg("-poses") ?? "all"));
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }
    }
}
