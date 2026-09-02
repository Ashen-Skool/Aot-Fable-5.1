using System.Collections.Generic;
using NUnit.Framework;
using Proxies;
using Shared.Rigs;
using Pose = Shared.Rigs.Pose;
using UnityEngine;

public class ProxyRigTests
{
    readonly List<GameObject> made = new List<GameObject>();
    [TearDown] public void Cleanup() { foreach (var g in made) if (g != null) Object.DestroyImmediate(g); made.Clear(); }

    MikasaProxy Mikasa() { var m = MikasaProxy.Build("TestMikasa", Vector3.zero); made.Add(m.gameObject); return m; }
    TitanProxy Titan(float h) { var t = TitanProxy.Build("TestTitan", h, Vector3.zero); made.Add(t.gameObject); return t; }

    static Bounds RenderBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) if (!(r is MeshRenderer mr && mr.GetComponent<TextMesh>() != null)) b.Encapsulate(r.bounds);
        return b;
    }

    [Test]
    public void HumanoidHasEveryBoneExactlyOnce()
    {
        var m = Mikasa();
        Assert.AreEqual(17, HumanoidProxy.BoneNames.Length);
        foreach (var name in HumanoidProxy.BoneNames)
        {
            var found = new List<Transform>();
            foreach (var t in m.GetComponentsInChildren<Transform>()) if (t.name == name) found.Add(t);
            Assert.AreEqual(1, found.Count, name + " appears once");
            Assert.AreSame(found[0], m.rig.Bone(name), name + " resolves through Bone(name)");
        }
    }

    [Test]
    public void HumanoidHierarchyIsMecanimShaped()
    {
        var m = Mikasa();
        var r = m.rig;
        Assert.AreEqual(m.transform, r.Bone(BoneId.Hips).parent);
        Assert.AreEqual(r.Bone(BoneId.Hips), r.Bone(BoneId.Spine).parent);
        Assert.AreEqual(r.Bone(BoneId.Spine), r.Bone(BoneId.Chest).parent);
        Assert.AreEqual(r.Bone(BoneId.Chest), r.Bone(BoneId.Neck).parent);
        Assert.AreEqual(r.Bone(BoneId.Neck), r.Bone(BoneId.Head).parent);
        Assert.AreEqual(r.Bone(BoneId.Chest), r.Bone(BoneId.LeftUpperArm).parent);
        Assert.AreEqual(r.Bone(BoneId.LeftUpperArm), r.Bone(BoneId.LeftLowerArm).parent);
        Assert.AreEqual(r.Bone(BoneId.LeftLowerArm), r.Bone(BoneId.LeftHand).parent);
        Assert.AreEqual(r.Bone(BoneId.Chest), r.Bone(BoneId.RightUpperArm).parent);
        Assert.AreEqual(r.Bone(BoneId.RightUpperArm), r.Bone(BoneId.RightLowerArm).parent);
        Assert.AreEqual(r.Bone(BoneId.RightLowerArm), r.Bone(BoneId.RightHand).parent);
        Assert.AreEqual(r.Bone(BoneId.Hips), r.Bone(BoneId.LeftUpperLeg).parent);
        Assert.AreEqual(r.Bone(BoneId.LeftUpperLeg), r.Bone(BoneId.LeftLowerLeg).parent);
        Assert.AreEqual(r.Bone(BoneId.LeftLowerLeg), r.Bone(BoneId.LeftFoot).parent);
        Assert.AreEqual(r.Bone(BoneId.Hips), r.Bone(BoneId.RightUpperLeg).parent);
        Assert.AreEqual(r.Bone(BoneId.RightUpperLeg), r.Bone(BoneId.RightLowerLeg).parent);
        Assert.AreEqual(r.Bone(BoneId.RightLowerLeg), r.Bone(BoneId.RightFoot).parent);
        Assert.Less(r.Bone(BoneId.LeftUpperArm).position.x, r.Bone(BoneId.RightUpperArm).position.x, "left is -X");
    }

    [TestCase(1.7f)] [TestCase(7f)] [TestCase(15f)]
    public void HeightMatchesRequest(float h)
    {
        GameObject go;
        if (h < 3f) go = Mikasa().gameObject; else go = Titan(h).gameObject;
        var b = RenderBounds(go);
        Assert.That(b.min.y, Is.EqualTo(0f).Within(h * 0.02f), "feet on the ground");
        Assert.That(b.max.y, Is.EqualTo(h).Within(h * 0.04f), "crown at height");
        var col = go.GetComponent<CapsuleCollider>();
        Assert.That(col.bounds.size.y, Is.EqualTo(h).Within(0.01f), "body collider height");
    }

    [Test]
    public void TitanZonesExistAndSitOnTheRightBones()
    {
        var t = Titan(7f);
        Assert.AreEqual(6, TitanProxy.ZoneNames.Length);
        foreach (var z in TitanProxy.ZoneNames)
        {
            var tr = HumanoidProxy.FindDeep(t.transform, z);
            Assert.IsNotNull(tr, z + " exists");
            var c = tr.GetComponent<Collider>();
            Assert.IsNotNull(c, z + " has a collider");
            Assert.IsTrue(c.isTrigger, z + " is a trigger");
            Assert.AreSame(c, t.Zone(z));
        }
        Assert.AreEqual("Neck", HumanoidProxy.FindDeep(t.transform, "Zone_Nape").parent.name);
        Assert.AreEqual("LeftUpperLeg", HumanoidProxy.FindDeep(t.transform, "Zone_HamstringL").parent.name);
        Assert.AreEqual("RightUpperLeg", HumanoidProxy.FindDeep(t.transform, "Zone_HamstringR").parent.name);
        Assert.AreEqual("LeftUpperArm", HumanoidProxy.FindDeep(t.transform, "Zone_ArmL").parent.name);
        Assert.AreEqual("RightUpperArm", HumanoidProxy.FindDeep(t.transform, "Zone_ArmR").parent.name);
        Assert.AreEqual("Head", HumanoidProxy.FindDeep(t.transform, "Zone_Eyes").parent.name);
        var nape = t.Zone("Zone_Nape").bounds;
        Assert.Greater(nape.center.y, 7f * 0.8f, "nape is high on the body");
        Assert.Less(nape.center.z, 0f, "nape is on the back (titan faces +Z)");
    }

    [Test]
    public void MikasaSocketsExistOnTheRightBones()
    {
        var m = Mikasa();
        foreach (var s in MikasaProxy.SocketNames)
        {
            var tr = HumanoidProxy.FindDeep(m.transform, s);
            Assert.IsNotNull(tr, s + " exists");
            Assert.AreSame(tr, m.Socket(s));
        }
        Assert.AreEqual("Hips", m.Socket("Socket_HookL").parent.name);
        Assert.AreEqual("Hips", m.Socket("Socket_HookR").parent.name);
        Assert.AreEqual("LeftHand", m.Socket("Socket_BladeL").parent.name);
        Assert.AreEqual("RightHand", m.Socket("Socket_BladeR").parent.name);
        Assert.AreEqual("Neck", m.Socket("Socket_Scarf").parent.name);
        Assert.Less(m.Socket("Socket_HookL").position.x, m.Socket("Socket_HookR").position.x);
    }

    [Test]
    public void OnlyZonesAndBodyCarryColliders()
    {
        var t = Titan(15f);
        foreach (var c in t.GetComponentsInChildren<Collider>())
            Assert.IsTrue(c.gameObject == t.gameObject || c.name.StartsWith("Zone_"), c.name + " should not have a collider");
    }
}
