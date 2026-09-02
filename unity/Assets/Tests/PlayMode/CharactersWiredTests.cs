using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Shared;
using Shared.Rigs;
using Shared.Cam;
using Characters;
using Pose = Shared.Rigs.Pose;

/// Code-level proof that the real Mikasa and Titan are wired into a play session exactly as the build runs them.
public class CharactersWiredTests
{
    [UnityTest]
    public IEnumerator MikasaAndTitanAreWiredForPlay()
    {
        Bootstrap.Ensure();
        for (int i = 0; i < 5; i++) yield return null;

        // 1. Real models dressed over the proxy hosts, registered as the posers gameplay talks to.
        var mik = Ctx.Get<CharacterModel>("mikasaModel"); var boss = Ctx.Get<CharacterModel>("bossModel");
        Assert.IsNotNull(mik, "Mikasa model dressed (Resources/Characters/Mikasa.fbx present)");
        Assert.IsNotNull(boss, "Titan model dressed (Resources/Characters/Titan.fbx present)");
        Assert.AreSame(mik, Ctx.Get<IPoser>("mikasaPoser"), "mikasaPoser is the real model");
        Assert.AreSame(boss, Ctx.Get<IPoser>("bossPoser"), "bossPoser is the real model");

        // 2. Humanoid rigs with clips, skinned meshes visible and textured, proxy geometry hidden.
        foreach (var (m, name, height) in new[] { (mik, "Mikasa", 1.70f), (boss, "Titan", 15f) })
        {
            Assert.IsTrue(m.animator.isHuman, name + " avatar is Humanoid");
            var smr = m.GetComponentsInChildren<SkinnedMeshRenderer>();
            Assert.Greater(smr.Length, 0, name + " has skinned mesh renderers");
            Assert.IsTrue(smr.All(r => r.enabled && r.sharedMaterial != null && r.sharedMaterial.mainTexture != null), name + " skinned meshes visible and textured");
            var host = m.transform.parent.gameObject;
            var geo = host.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name.StartsWith("Geo_")).ToArray();
            Assert.IsTrue(geo.All(r => !r.enabled), name + " proxy primitives hidden (" + geo.Count(r => r.enabled) + " still visible)");
            var b = smr[0].bounds; foreach (var r in smr) b.Encapsulate(r.bounds);
            Assert.AreEqual(height, b.size.y, height * 0.12f, name + " stands " + height + " m tall in world (was " + b.size.y.ToString("0.00") + ")");
            Assert.AreSame(Ctx.Get<GameObject>(name == "Mikasa" ? "mikasa" : "boss"), host, name + " host is the Ctx character object");
            // poses drive different clips
            m.SetPose(Pose.Idle); m.Tick(0.1f); var idle = m.Current;
            m.SetPose(name == "Mikasa" ? Pose.Run : Pose.Stomp); m.Tick(0.1f);
            Assert.AreNotEqual(idle, m.Current, name + " pose switches");
        }

        // 3. Player controller on Mikasa, camera follows the player (not the demo dummy).
        var player = Ctx.Get<Component>("player");
        Assert.IsNotNull(player, "ODM player registered");
        Assert.AreSame(mik.transform.parent.gameObject, player.gameObject, "ODM controller is on Mikasa's host");
        Assert.AreEqual("OdmController", player.GetType().Name);
        var camT = Ctx.Get<ICameraTarget>(ICameraTarget.CtxName);
        Assert.IsNotNull(camT, "camera target registered");
        Assert.AreEqual("OdmCameraTarget", camT.GetType().Name, "camera target is the player adapter");
        Assert.AreSame(player.transform, camT.Root, "camera target root is the player");
        var rig = Ctx.Get<Component>("cameraRig") ?? Camera.main.GetComponents<Component>().FirstOrDefault(c => c.GetType().Name == "CameraRig");
        Assert.IsNotNull(rig, "camera rig installed on the main camera");
        yield return null;
        // 4. Mikasa's poser follows flight state: drop her and she must go to Fly.
        var rb = player.GetComponent<Rigidbody>(); Assert.IsNotNull(rb, "player rigidbody");
        player.transform.position += Vector3.up * 30f; rb.linearVelocity = Vector3.zero;
        for (int i = 0; i < 6; i++) yield return new WaitForFixedUpdate();
        yield return null;
        Assert.AreEqual(Pose.Fly, mik.Current, "airborne Mikasa plays the Fly pose");
    }
}
