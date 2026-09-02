using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Build
{
    /// <summary>
    /// Unity -batchmode -quit -executeMethod Build.Builder.Mac   -> builds/mac/AOT.app (arm64)
    /// Unity -batchmode -quit -executeMethod Build.Builder.WebGL -> builds/webgl/
    /// Prints BUILD_OK &lt;target&gt; &lt;seconds&gt;s &lt;MB&gt; or BUILD_FAIL &lt;target&gt; &lt;reason&gt;.
    /// </summary>
    public static class Builder
    {
        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string[] Scenes => EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

        public static void Mac()
        {
            UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.ARM64;
            UnityEditor.OSXStandalone.UserBuildSettings.createXcodeProject = false;
            Do("mac", BuildTarget.StandaloneOSX, Path.Combine(Repo, "builds", "mac", "AOT.app"));
        }

        public static void WebGL()
        {
            Do("webgl", BuildTarget.WebGL, Path.Combine(Repo, "builds", "webgl"));
        }

        static void Do(string name, BuildTarget target, string outPath)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (Scenes.Length == 0) throw new Exception("no scenes in Build Settings; run Setup.All.Run");
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                var opts = new BuildPlayerOptions
                {
                    scenes = Scenes,
                    locationPathName = outPath,
                    target = target,
                    options = BuildOptions.None,
                };
                var report = BuildPipeline.BuildPlayer(opts);
                var s = report.summary;
                double mb = s.totalSize / (1024.0 * 1024.0);
                if (s.result == BuildResult.Succeeded)
                {
                    Out($"BUILD_OK {name} {sw.Elapsed.TotalSeconds:0}s {mb:0.0}MB {outPath}");
                    EditorApplication.Exit(0);
                }
                else
                {
                    Out($"BUILD_FAIL {name} result={s.result} errors={s.totalErrors} after {sw.Elapsed.TotalSeconds:0}s");
                    EditorApplication.Exit(1);
                }
            }
            catch (Exception e)
            {
                Out($"BUILD_FAIL {name} {e.Message}");
                EditorApplication.Exit(1);
            }
        }

        static void Out(string s) { Debug.Log(s); Console.WriteLine(s); }
    }
}
