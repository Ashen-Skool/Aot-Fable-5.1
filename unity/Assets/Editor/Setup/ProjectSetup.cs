using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Setup
{
    /// <summary>
    /// Project-wide settings + the single Main scene + base materials.
    /// Unity -batchmode -quit -executeMethod Setup.ProjectSetup.Run
    /// </summary>
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";
        const string MatDir = "Assets/Shared/Resources/Materials";

        public static void Run()
        {
            PlayerSettings.companyName = "Ashen";
            PlayerSettings.productName = "AOT";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.ashen.aot");
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;

            // Input handling "Both" (old Input Manager + new Input System). No public API: poke the serialized field.
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (ps != null && ps.Length > 0)
            {
                var so = new SerializedObject(ps[0]);
                var prop = so.FindProperty("activeInputHandler");
                if (prop != null) { prop.intValue = 2; so.ApplyModifiedPropertiesWithoutUndo(); }
                else Debug.LogWarning("[ProjectSetup] activeInputHandler property not found");
            }

            BaseMaterials();
            MainScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ProjectSetup] done");
        }

        static void BaseMaterials()
        {
            Directory.CreateDirectory(MatDir);
            Make("Lit", "Universal Render Pipeline/Lit");
            Make("Unlit", "Universal Render Pipeline/Unlit");
            Make("SimpleLit", "Universal Render Pipeline/Simple Lit");
            Make("Particles", "Universal Render Pipeline/Particles/Unlit");
            Make("Sky", "Skybox/Procedural");
        }

        static void Make(string name, string shader)
        {
            var path = MatDir + "/" + name + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
            var sh = Shader.Find(shader);
            if (sh == null) { Debug.LogError("[ProjectSetup] shader missing: " + shader); return; }
            AssetDatabase.CreateAsset(new Material(sh), path);
        }

        static void MainScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 2000f;
            cam.allowHDR = true;
            go.AddComponent<AudioListener>();
            go.transform.position = new Vector3(0, 2, -10);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }

    public static class All
    {
        public static void Run()
        {
            UrpSetup.Run();
            ProjectSetup.Run();
            Debug.Log("SETUP_OK");
            System.Console.WriteLine("SETUP_OK");
        }
    }
}
