using System;
using UnityEngine;

namespace Shared.Cam
{
    /// <summary>State flags a camera target reports. Rising edges of Hit and Grounded drive camera shake.</summary>
    [Flags]
    public enum CameraTargetState
    {
        None = 0,
        Grounded = 1 << 0,
        Flying = 1 << 1,
        Boosting = 1 << 2,   // gas boost: the camera kicks its FOV 70 -> 95 while set
        Hit = 1 << 3,
        Hooked = 1 << 4,     // cables attached: the rig leaves the heading to the rope, otherwise free flight is absolute mouse control        // took an impact this frame (set for at least one frame)
        Perched = 1 << 6,    // clinging to a wall face: the camera swings out in front of her, the wall is behind her
        Riding = 1 << 5,     // on the Titan's nape: the rig pulls back and up so the nape reads instead of sitting inside his back
    }

    /// <summary>
    /// What the chase camera follows. The ODM controller (or any other mover) implements
    /// this and registers it with <c>Ctx.Set(ICameraTarget.CtxName, this)</c>; the camera
    /// rig (Ctx "cameraRig", piece 3) picks it up on the next frame and drops its own
    /// DemoTarget. Values are world space, read every LateUpdate; keep the getters cheap.
    /// </summary>
    public interface ICameraTarget
    {
        /// <summary>Ctx key: "cameraTarget".</summary>
        const string CtxName = "cameraTarget";

        /// <summary>Centre of the character, roughly hip height.</summary>
        Vector3 Position { get; }
        /// <summary>World velocity in m/s. The camera pulls back and adds speed lines with its magnitude.</summary>
        Vector3 Velocity { get; }
        /// <summary>Facing direction (unit). Used when velocity is too small to define a heading.</summary>
        Vector3 Forward { get; }
        CameraTargetState State { get; }
        /// <summary>Root transform of the character's colliders so camera collision can ignore them. May be null.</summary>
        Transform Root { get; }
    }
}
