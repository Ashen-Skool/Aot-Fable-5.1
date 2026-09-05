using Shared;
using Shared.Cam;
using UnityEngine;

namespace AotCamera
{
    /// <summary>
    /// Installs the CameraRig on the bootstrap camera at startup and registers it as Ctx "cameraRig".
    /// During a capture the rig only drives the camera for poses marked "camera": "game" (the
    /// CaptureRunner places the others). The DemoTarget flies the demo path only for the camera
    /// piece's own capture or in normal play without a registered "cameraTarget".
    /// </summary>
    public static class CameraBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { Reboot.Register(40, () => Install()); Install(); }

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
            rig.driveCamera = true;
            rig.logEvery = Bootstrap.ArgInt("-camlog", 0);
            var orbit = Ctx.Get<OrbitCamera>("orbit");
            if (orbit != null) orbit.enabled = false;
            Ctx.Set(CameraRig.CtxName, rig);
            if (!capturingOther && !RunningTests() && Ctx.Get<ICameraTarget>(ICameraTarget.CtxName) == null && Object.FindFirstObjectByType<DemoTarget>() == null)
            {
                var demo = DemoTarget.Create();
                rig.fallbackTarget = demo;
                Ctx.Set("cameraDemo", demo);
            }
            return rig;
        }
    }
}
