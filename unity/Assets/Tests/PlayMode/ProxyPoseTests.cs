using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Proxies;
using Shared;
using Shared.Rigs;
using Pose = Shared.Rigs.Pose;
using UnityEngine;
using UnityEngine.TestTools;

public class ProxyPoseTests
{
    static Quaternion[] Snapshot(HumanoidProxy r)
    {
        var q = new Quaternion[HumanoidProxy.BoneCount];
        for (int i = 0; i < q.Length; i++) q[i] = r.Bone((BoneId)i).localRotation;
        return q;
    }

    static float Distance(Quaternion[] a, Quaternion[] b)
    {
        float d = 0;
        for (int i = 0; i < a.Length; i++) d += Quaternion.Angle(a[i], b[i]);
        return d;
    }

    [UnityTest]
    public IEnumerator EveryPoseMovesTheHierarchyAndIsDistinct()
    {
        var m = MikasaProxy.Build("PoseTestMikasa", new Vector3(100, 0, 100));
        m.rig.autoTick = false;
        yield return null;
        var bind = new Quaternion[HumanoidProxy.BoneCount];
        for (int i = 0; i < bind.Length; i++) bind[i] = Quaternion.identity;
        var poses = (Pose[])Enum.GetValues(typeof(Pose));
        var snaps = new Dictionary<Pose, Quaternion[]>();
        var hips = new Dictionary<Pose, Vector3>();
        foreach (var p in poses)
        {
            m.rig.Snap(p, ProxyBootstrap.BestPhase(p));
            Assert.AreEqual(p, m.rig.Current);
            snaps[p] = Snapshot(m.rig);
            hips[p] = m.rig.Bone(BoneId.Hips).localPosition;
            Assert.Greater(Distance(snaps[p], bind), 10f, p + " moves bones away from the bind pose");
        }
        foreach (var a in poses)
            foreach (var b in poses)
                if (a < b) Assert.Greater(Distance(snaps[a], snaps[b]), 20f, a + " and " + b + " read differently");
        Assert.Less(hips[Pose.Kneel].y, hips[Pose.Idle].y - 0.3f, "kneel drops the hips");
        Assert.Less(hips[Pose.Land].y, hips[Pose.Idle].y - 0.2f, "land crouches");
        UnityEngine.Object.Destroy(m.gameObject);
    }

    [UnityTest]
    public IEnumerator TitanPosesAreStronglyDistinctAtTheirPeakFrames()
    {
        var t = TitanProxy.Build("PoseTestTitan", 7f, new Vector3(150, 0, 100));
        t.rig.autoTick = false;
        yield return null;
        var poses = new[] { Pose.Idle, Pose.Land, Pose.Stagger, Pose.Kneel, Pose.Swipe, Pose.Grab, Pose.Stomp };
        var snaps = new Dictionary<Pose, Quaternion[]>();
        foreach (var p in poses) { t.rig.Snap(p, ProxyBootstrap.BestPhase(p)); snaps[p] = Snapshot(t.rig); }
        foreach (var a in poses)
            foreach (var b in poses)
                if (a < b) Assert.Greater(Distance(snaps[a], snaps[b]), 120f, a + " vs " + b + " must read differently");
        t.rig.Snap(Pose.Stomp, ProxyBootstrap.BestPhase(Pose.Stomp));
        var knee = t.rig.Bone(BoneId.RightLowerLeg).position.y;
        Assert.Greater(knee, 7f * 0.5f, "stomp lifts the knee to hip height");
        t.rig.Snap(Pose.Kneel, ProxyBootstrap.BestPhase(Pose.Kneel));
        Assert.Less(t.rig.Bone(BoneId.RightLowerLeg).position.y, 7f * 0.1f, "kneel puts the knee on the ground");
        UnityEngine.Object.Destroy(t.gameObject);
    }

    [UnityTest]
    public IEnumerator CyclesAdvanceWithTickAndFreezeWhenPaused()
    {
        var m = MikasaProxy.Build("PoseTestMikasa2", new Vector3(100, 0, 110));
        m.rig.autoTick = false;
        yield return null;
        m.rig.Snap(Pose.Run, 0f);
        var a = Snapshot(m.rig);
        for (int i = 0; i < 12; i++) m.rig.Tick(1f / 60f);
        var b = Snapshot(m.rig);
        Assert.Greater(Distance(a, b), 5f, "run cycles the limbs over time");
        m.rig.Snap(Pose.Idle, 0f);
        var c = Snapshot(m.rig);
        for (int i = 0; i < 60; i++) m.rig.Tick(1f / 60f);
        Assert.Greater(Distance(c, Snapshot(m.rig)), 0.5f, "idle breathes");
        m.rig.Paused = true;
        var d = Snapshot(m.rig);
        for (int i = 0; i < 60; i++) m.rig.Tick(1f / 60f);
        Assert.Less(Distance(d, Snapshot(m.rig)), 0.01f, "paused pose is still after settling");
        UnityEngine.Object.Destroy(m.gameObject);
    }

    [UnityTest]
    public IEnumerator SetPoseBlendsInsteadOfPopping()
    {
        var m = MikasaProxy.Build("PoseTestMikasa3", new Vector3(100, 0, 120));
        m.rig.autoTick = false;
        yield return null;
        m.rig.Snap(Pose.Idle, 0f);
        var idle = Snapshot(m.rig);
        m.rig.SetPose(Pose.Slash);
        m.rig.Tick(1f / 60f);
        var mid = Snapshot(m.rig);
        for (int i = 0; i < 120; i++) m.rig.Tick(1f / 60f);
        var end = Snapshot(m.rig);
        Assert.Less(Distance(idle, mid), Distance(idle, end), "first frame is between the poses");
        UnityEngine.Object.Destroy(m.gameObject);
    }

    [UnityTest]
    public IEnumerator BootstrapSpawnsProxiesInsteadOfCapsules()
    {
        Bootstrap.Ensure();
        yield return null;
        var mk = Ctx.Get<MikasaProxy>("mikasaProxy");
        var b = Ctx.Get<TitanProxy>("bossProxy");
        Assert.IsNotNull(mk, "mikasaProxy registered");
        Assert.IsNotNull(b, "bossProxy registered");
        Assert.AreSame(mk.gameObject, Ctx.Get<GameObject>("mikasa"));
        Assert.AreSame(b.gameObject, Ctx.Get<GameObject>("boss"));
        Assert.IsNotNull(Ctx.Get<IPoser>("mikasaPoser"));
        Assert.IsNull(Ctx.Get<TitanProxy>("titanProxy"), "the 7 m proxy is built only for a proxies capture");
        Assert.That(b.height, Is.EqualTo(15f));
        // the 7 m proxy still builds correctly when the capture rig asks for one
        var small = TitanProxy.Build("SmallProbe", TitanProxy.SmallHeight, new Vector3(0f, -400f, 0f), 0f);
        Assert.That(small.height, Is.EqualTo(7f));
        UnityEngine.Object.Destroy(small.gameObject);
        Assert.IsNotNull(HumanoidProxy.FindDeep(b.transform, "Zone_Nape"));
        Assert.IsNotNull(HumanoidProxy.FindDeep(mk.transform, "Socket_Scarf"));
    }
}
