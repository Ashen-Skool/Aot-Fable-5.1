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

    GameObject go;
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
    }

    IEnumerator WaitRealtime(float seconds)
    {
        float until = Time.unscaledTime + seconds;
        while (Time.unscaledTime < until) yield return null;
    }

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
}
