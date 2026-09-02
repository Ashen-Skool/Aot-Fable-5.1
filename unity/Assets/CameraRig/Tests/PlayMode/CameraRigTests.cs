using System.Collections;
using AotCamera;
using NUnit.Framework;
using Shared.Cam;
using UnityEngine;
using UnityEngine.TestTools;

public class CameraRigTests
{
    class FakeTarget : ICameraTarget
    {
        public Vector3 Position { get; set; } = new Vector3(0, 1, 0);
        public Vector3 Velocity { get; set; } = new Vector3(0, 0, 30f);
        public Vector3 Forward => Vector3.forward;
        public CameraTargetState State { get; set; } = CameraTargetState.Flying;
        public Transform Root => null;
    }

    GameObject go, lockGo;
    CameraRig rig;
    FakeTarget target;

    [SetUp]
    public void SetUp()
    {
        go = new GameObject("TestCam");
        go.AddComponent<Camera>();
        rig = go.AddComponent<CameraRig>();
        target = new FakeTarget();
        rig.explicitTarget = target;
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 1f / 60f;
        if (go != null) Object.Destroy(go);
        if (lockGo != null) Object.Destroy(lockGo);
    }

    IEnumerator WaitRealtime(float seconds)
    {
        float until = Time.unscaledTime + seconds;
        while (Time.unscaledTime < until) yield return null;
    }

    /// <summary>Advance the fake target along its velocity for real seconds, like a moving character.</summary>
    IEnumerator Fly(float seconds)
    {
        float until = Time.unscaledTime + seconds;
        while (Time.unscaledTime < until) { target.Position += target.Velocity * Time.deltaTime; yield return null; }
    }

    Vector3 Viewport(Vector3 world) => rig.Cam.WorldToViewportPoint(world);

    [UnityTest]
    public IEnumerator FovRisesDuringBoost()
    {
        yield return null; yield return null;
        yield return WaitRealtime(0.5f);
        float before = rig.Fov;
        Assert.That(before, Is.EqualTo(rig.baseFov).Within(2f), "idle fov is the base fov");
        target.State = CameraTargetState.Flying | CameraTargetState.Boosting;
        yield return WaitRealtime(1.0f);
        Assert.Greater(rig.Fov, before + 15f, "fov kicks up while boosting");
        Assert.That(rig.Fov, Is.EqualTo(rig.boostFov).Within(3f), "fov reaches the boost fov");
        Assert.IsTrue(rig.Lines.Visible, "speed lines show at speed while boosting");
        target.State = CameraTargetState.Flying;
        yield return WaitRealtime(1.5f);
        Assert.That(rig.Fov, Is.EqualTo(rig.baseFov).Within(3f), "fov settles back after boost");
    }

    [UnityTest]
    public IEnumerator ChaseSitsOverTheRightShoulderWithMikasaLowerLeft()
    {
        target.Velocity = new Vector3(0, 0, 30f);
        yield return null; yield return null;
        yield return Fly(0.8f);
        var pivot = target.Position + Vector3.up * rig.pivotHeight;
        var cam = rig.transform.position;
        Assert.Greater(cam.x, 0.3f, "camera is offset to her right");
        Assert.Less(cam.z, target.Position.z - 2.4f, "camera is 3-4 m back");
        Assert.Greater(cam.z, target.Position.z - 4.6f, "camera is 3-4 m back");
        Assert.That(cam.y - pivot.y, Is.InRange(0.2f, 1.6f), "camera is roughly at shoulder height, not high above");
        var vp = Viewport(pivot);
        Assert.Greater(vp.z, 0f, "she is in front of the camera");
        Assert.That(vp.x, Is.InRange(0.2f, 0.47f), "she sits in the left third: " + vp);
        Assert.That(vp.y, Is.InRange(0.18f, 0.47f), "she sits in the lower third: " + vp);
        var horizon = Viewport(cam + new Vector3(0, 0, 1000f));
        Assert.Greater(horizon.y, 0.5f, "the horizon is in the upper half of the frame: " + horizon);
    }

    [UnityTest]
    public IEnumerator TargetLockFramesBothWithLeadRoom()
    {
        lockGo = new GameObject("LockTitan");
        lockGo.transform.position = new Vector3(4f, 8f, 40f);
        rig.lockTarget = lockGo.transform;
        target.Velocity = new Vector3(0, 0, 30f);
        yield return null; yield return null;
        yield return WaitRealtime(1.0f);
        Assert.AreSame(lockGo.transform, rig.Lock);
        var her = Viewport(target.Position + Vector3.up * rig.pivotHeight);
        var it = Viewport(lockGo.transform.position);
        Assert.Greater(her.z, 0f); Assert.Greater(it.z, 0f);
        Assert.That(her.x, Is.InRange(0.05f, 0.5f), "she is on the left: " + her);
        Assert.That(her.y, Is.InRange(0.05f, 0.5f), "she is low: " + her);
        Assert.That(it.x, Is.InRange(0.4f, 0.95f), "the target is framed to the right: " + it);
        Assert.That(it.y, Is.InRange(0.35f, 0.95f), "the target is framed high: " + it);
        Assert.Greater(it.x, her.x + 0.1f, "lead room between her and the target");
    }

    [UnityTest]
    public IEnumerator KillCamOrbitsAndFramesTheNapeLarge()
    {
        rig.killCamDuration = 1.0f;
        target.Position = new Vector3(2f, 15f, 63f);
        target.Velocity = Vector3.zero;
        var nape = new Vector3(0, 13.5f, 61f);
        yield return null;
        rig.KillCam(nape);
        yield return null; yield return null;
        Assert.AreEqual(CameraMode.KillCam, rig.Mode);
        float yawA = rig.KillCamYaw;
        var napeA = Viewport(nape); var herA = Viewport(target.Position);
        Assert.That(napeA.x, Is.InRange(0.3f, 0.7f), "nape centred early: " + napeA);
        Assert.That(herA.x, Is.InRange(0f, 1f), "she is in frame early: " + herA);
        Assert.Less(herA.z, napeA.z, "she is in the foreground, closer than the nape");
        yield return WaitRealtime(0.7f);
        float yawB = rig.KillCamYaw;
        var napeB = Viewport(nape); var herB = Viewport(target.Position);
        Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(yawA, yawB)), 30f, "the orbit visibly sweeps: " + yawA + " -> " + yawB);
        Assert.That(napeB.x, Is.InRange(0.3f, 0.7f), "nape still centred late: " + napeB);
        Assert.That(herB.x, Is.InRange(0f, 1f), "she is still in frame late: " + herB);
        Assert.Less(Vector3.Distance(rig.transform.position, nape), rig.killCamRadiusStart + 0.1f, "orbit pushes in");
    }

    [UnityTest]
    public IEnumerator KillCamSlowsTimeThenRestoresIt()
    {
        rig.killCamDuration = 0.6f;
        yield return null;
        rig.KillCam(new Vector3(0, 12, 20));
        yield return null;
        Assert.AreEqual(CameraMode.KillCam, rig.Mode);
        Assert.That(Time.timeScale, Is.EqualTo(rig.killCamTimeScale).Within(1e-4f), "slow motion during kill cam");
        yield return WaitRealtime(1.2f);
        Assert.AreEqual(CameraMode.Chase, rig.Mode, "back to chase");
        Assert.That(Time.timeScale, Is.EqualTo(1f).Within(1e-4f), "time scale restored");
        Assert.That(Time.fixedDeltaTime, Is.EqualTo(1f / 60f).Within(1e-4f), "fixed step restored");
    }

    [UnityTest]
    public IEnumerator DiveEndsLowBehindHerTiltedDown()
    {
        target.Position = new Vector3(0, 20f, -50f);
        target.Velocity = new Vector3(0, -18f, 10f);
        yield return null;
        rig.CinematicDive(new Vector3(4f, 56f, -82f), new Vector3(0, 20f, 20f), 0.5f);
        yield return null;
        Assert.AreEqual(CameraMode.Dive, rig.Mode);
        // sample the pose just before the dive hands over to chase
        float until = Time.unscaledTime + 0.42f;
        while (Time.unscaledTime < until && rig.Mode == CameraMode.Dive) yield return null;
        Assert.AreEqual(CameraMode.Dive, rig.Mode, "still diving at 0.42 s");
        var cam = rig.transform.position;
        Assert.Less(cam.y - target.Position.y, 9f, "camera has come down close behind her");
        Assert.Less(cam.z, target.Position.z, "camera is behind her");
        float pitchDown = -Mathf.Asin(Mathf.Clamp(rig.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        Assert.Greater(pitchDown, 12f, "camera is tilted forward, looking down the street: pitch down " + pitchDown);
        yield return WaitRealtime(0.3f);
        Assert.AreEqual(CameraMode.Chase, rig.Mode, "hands over to chase");
    }

    [UnityTest]
    public IEnumerator DutchTiltsIntoTurns()
    {
        target.Velocity = new Vector3(0, 0, 30f);
        yield return Fly(0.6f);
        Assert.That(Mathf.Abs(rig.Roll), Is.LessThan(2f), "no roll flying straight");
        // sweep the heading through a 90 degree turn over ~0.8 s
        float until = Time.unscaledTime + 0.8f; float a = 0f;
        float maxRoll = 0f;
        while (Time.unscaledTime < until)
        {
            a += 110f * Time.deltaTime;
            target.Velocity = Quaternion.Euler(0, a, 0) * new Vector3(0, 0, 30f);
            target.Position += target.Velocity * Time.deltaTime;
            maxRoll = Mathf.Max(maxRoll, Mathf.Abs(rig.Roll));
            yield return null;
        }
        Assert.Greater(maxRoll, 5f, "the camera banks into the turn");
        Assert.LessOrEqual(maxRoll, rig.dutchDeg + 0.5f, "dutch is capped");
    }

    [UnityTest]
    public IEnumerator MotionBlurFollowsSpeed()
    {
        target.Velocity = new Vector3(0, 0, 45f);
        yield return Fly(0.3f);
        Assert.Greater(rig.BlurIntensity, 0.5f, "fast flight blurs");
        Assert.IsTrue(rig.Lines.Visible, "speed lines only at high speed");
        target.Velocity = new Vector3(0, 0, 10f);
        yield return Fly(0.3f);
        Assert.Less(rig.BlurIntensity, 0.25f, "slow flight barely blurs");
        Assert.IsFalse(rig.Lines.Visible, "no speed lines below 30 m/s");
    }

    [UnityTest]
    public IEnumerator HitShakesTheCamera()
    {
        yield return null; yield return null;
        Assert.That(rig.Trauma, Is.EqualTo(0f).Within(1e-3f));
        target.State = CameraTargetState.Flying | CameraTargetState.Hit;
        yield return null;
        Assert.Greater(rig.Trauma, 0.5f, "impact adds trauma");
        target.State = CameraTargetState.Flying;
        yield return WaitRealtime(1.5f);
        Assert.That(rig.Trauma, Is.EqualTo(0f).Within(1e-3f), "trauma decays");
    }

    [Test]
    public void FrameAtPutsThePointWhereAsked()
    {
        var camPos = new Vector3(1f, 2f, -3f);
        var point = new Vector3(0f, 1.35f, 0f);
        var screen = new Vector2(0.36f, 0.34f);
        var rot = CameraRig.FrameAt(camPos, point, screen, 70f, 16f / 9f);
        var c = new GameObject("FrameCam").AddComponent<Camera>();
        c.fieldOfView = 70f; c.aspect = 16f / 9f;
        c.transform.SetPositionAndRotation(camPos, rot);
        var vp = c.WorldToViewportPoint(point);
        Assert.That(vp.x, Is.EqualTo(screen.x).Within(0.01f));
        Assert.That(vp.y, Is.EqualTo(screen.y).Within(0.01f));
        Assert.That(rot.eulerAngles.z, Is.EqualTo(0f).Within(0.01f).Or.EqualTo(360f).Within(0.01f), "no roll");
        Object.DestroyImmediate(c.gameObject);
    }
}
