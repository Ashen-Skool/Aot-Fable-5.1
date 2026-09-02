using Shared;
using Shared.Cam;
using UnityEngine;

namespace AotCamera
{
    /// <summary>
    /// Installs the CameraRig on the bootstrap camera at startup and registers it as Ctx "cameraRig".
    /// During a capture of another piece (-piece X, X != camera) the rig stays passive so that
    /// piece's poses hold. Without a registered "cameraTarget" a DemoTarget flies the demo path.
    /// </summary>
    public static class CameraBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => Install();

        static bool RunningTests()
        {
            foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-runTests") return true;
            return false;
        }

        public static CameraRig Install()
        {
            Bootstrap.Ensure();
            var cam = Ctx.Get<Camera>("camera") ?? Camera.main;
            if (cam == null) return null;
            var rig = cam.GetComponent<CameraRig>() ?? cam.gameObject.AddComponent<CameraRig>();
            var piece = Bootstrap.Arg("-piece");
            bool capturingOther = !string.IsNullOrEmpty(piece) && piece != "camera";
            rig.driveCamera = !capturingOther;
            rig.logEvery = Bootstrap.ArgInt("-camlog", 0);
            var orbit = Ctx.Get<OrbitCamera>("orbit");
            if (orbit != null && rig.driveCamera) orbit.enabled = false;
            Ctx.Set(CameraRig.CtxName, rig);
            if (rig.driveCamera && !RunningTests() && Ctx.Get<ICameraTarget>(ICameraTarget.CtxName) == null && Object.FindFirstObjectByType<DemoTarget>() == null)
            {
                var demo = DemoTarget.Create();
                rig.fallbackTarget = demo;
                Ctx.Set("cameraDemo", demo);
            }
            return rig;
        }
    }
}
