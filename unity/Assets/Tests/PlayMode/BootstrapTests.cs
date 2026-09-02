using System.Collections;
using NUnit.Framework;
using Shared;
using UnityEngine;
using UnityEngine.TestTools;

public class BootstrapTests
{
    [UnityTest]
    public IEnumerator BootstrapSpawnsTitanAndMikasa()
    {
        var b = Bootstrap.Ensure();
        yield return null;
        Assert.IsNotNull(b, "Bootstrap instance");
        Assert.IsNotNull(GameObject.Find("Titan"), "Titan object exists");
        Assert.IsNotNull(GameObject.Find("Mikasa"), "Mikasa object exists");
        Assert.IsNotNull(Ctx.Get<GameObject>("titan"), "titan registered in Ctx");
        Assert.IsNotNull(Ctx.Get<GameObject>("mikasa"), "mikasa registered in Ctx");
        Assert.IsNotNull(Ctx.Get<Camera>("camera"), "camera registered in Ctx");
        Assert.AreEqual(Bootstrap.DefaultSeed, Ctx.Get<int>("seed"));
        var titan = Ctx.Get<GameObject>("titan");
        var h = titan.GetComponent<Collider>().bounds.size.y;
        Assert.That(h, Is.EqualTo(7f).Within(0.5f), "titan is ~7 m tall");
        var boss = Ctx.Get<GameObject>("boss");
        Assert.IsNotNull(boss, "boss registered in Ctx");
        Assert.That(boss.GetComponent<Collider>().bounds.size.y, Is.EqualTo(15f).Within(0.5f), "boss is ~15 m tall");
    }
}
