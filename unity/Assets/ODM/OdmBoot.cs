using UnityEngine;
using Shared;

namespace ODM
{
    /// <summary>
    /// Wires the ODM piece into the bootstrapped scene: puts the controller on Mikasa,
    /// moves the placeholder blocks / titan onto the hook layers, builds the tower grid
    /// (when no town exists, or when capturing this piece) and, for `-piece odm`, replays
    /// the demo FlightScript so the capture rig sees mid-flight frames.
    /// Ctx: "player" (OdmController), "odmGrid" (HookTestGrid).
    /// </summary>
    public static class OdmBoot
    {
        public static OdmController Player { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Auto()
        {
            string piece = Bootstrap.Arg("-piece");
            bool capturingOdm = piece == "odm";
            bool wantGrid = capturingOdm || Bootstrap.Arg("-odmGrid") != null || (piece == null && !Ctx.Has("town"));
            Ensure(wantGrid);
            if (capturingOdm || Bootstrap.Arg("-odmScript") != null) PlayDemo(true);
        }

        /// <summary>Idempotent. Attaches the controller to Mikasa and optionally builds the tower grid.</summary>
        static bool spawnPlaced;
        public static OdmController Ensure(bool buildGrid)
        {
            var boot = Bootstrap.Ensure();
            var mikasa = Ctx.Get<GameObject>("mikasa") ?? boot.mikasa;
            if (Player == null || Player.gameObject != mikasa) Player = OdmController.Attach(mikasa);
            Ctx.Set("player", Player);
            // Camera rig follows whatever is registered as the camera target; that is the player, not the demo dummy.
            var camTarget = Player.GetComponent<OdmCameraTarget>() ?? Player.gameObject.AddComponent<OdmCameraTarget>();
            Ctx.Set(Shared.Cam.ICameraTarget.CtxName, (Shared.Cam.ICameraTarget)camTarget);
            // The town now surrounds the origin with houses; spawn in the open square so the
            // player is never born inside a collider (grounded checks start inside otherwise).
            if (!spawnPlaced && (Ctx.Has("town.spawn") || Ctx.Has("townCenter")))
            {
                var c = Ctx.Has("town.spawn") ? Ctx.Get<Vector3>("town.spawn") : Ctx.Get<Vector3>("townCenter");
                Player.transform.position = new Vector3(c.x, 1.2f, c.z);
                var rb = Player.GetComponent<Rigidbody>(); if (rb != null) rb.linearVelocity = Vector3.zero;
                spawnPlaced = true;
            }
            var titan = Ctx.Get<GameObject>("titan");
            if (titan != null) SetLayerDeep(titan.transform, OdmLayers.Titan);
            var placeholder = Ctx.Get<GameObject>("placeholder");
            if (placeholder != null) SetLayerDeep(placeholder.transform, OdmLayers.Hook);
            if (buildGrid) HookTestGrid.Build(Ctx.Get<int>("seed"));
            return Player;
        }

        public static FlightScript PlayDemo(bool verbose)
        {
            var grid = HookTestGrid.Build(Ctx.Get<int>("seed"));
            // the demo flies the boulevard between the tower columns; the stub street is in the way
            var stub = Ctx.Get<GameObject>("placeholder");
            if (stub != null) { Object.Destroy(stub); Ctx.Remove("placeholder"); }
            var s = FlightScript.Demo(grid);
            Player.verbose = verbose;
            Player.Teleport(FlightScript.DemoStart(grid), Vector3.forward);
            if (verbose)
            {
                Debug.Log("[ODM] demo start=" + FlightScript.DemoStart(grid).ToString("0.0")
                          + " a1=" + FlightScript.DemoA1(grid).ToString("0.0") + " a2=" + FlightScript.DemoA2(grid).ToString("0.0")
                          + " a3=" + FlightScript.DemoA3(grid).ToString("0.0") + " landRoof=" + FlightScript.DemoLandRoof(grid).ToString("0.0"));
            }
            Player.Play(s);
            return s;
        }

        static void SetLayerDeep(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerDeep(t.GetChild(i), layer);
        }
    }
}
