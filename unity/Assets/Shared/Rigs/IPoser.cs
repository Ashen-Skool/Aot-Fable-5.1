namespace Shared.Rigs
{
    /// <summary>
    /// Every animated character exposes exactly these poses. Proxies implement them
    /// procedurally (ProceduralPoser); the final rigs implement them with Animator
    /// clips. Callers only ever call SetPose; they never reference a clip.
    /// </summary>
    public enum Pose
    {
        Idle, Run, Fly, Slash, Land, Stagger, Kneel, Swipe, Grab, Stomp, Sprint, Swing
    }

    public interface IPoser
    {
        /// <summary>The pose currently requested.</summary>
        Pose Current { get; }

        /// <summary>Seconds since the current pose was requested (cycle time, in character tempo units).</summary>
        float Phase { get; set; }

        /// <summary>Rate multiplier for the pose cycle. 1 = authored speed.</summary>
        float Speed { get; set; }

        /// <summary>When true the phase does not advance; the pose is frozen at Phase.</summary>
        bool Paused { get; set; }

        /// <summary>Request a pose. Blends from the previous pose; resets Phase to 0 when the pose changes.</summary>
        void SetPose(Pose pose);

        /// <summary>Request a pose at a given phase and apply it without blending (still frames, tests).</summary>
        void Snap(Pose pose, float phase);

        /// <summary>Advance the pose. Proxies call this from Update; tests call it directly.</summary>
        void Tick(float dt);
    }
}
