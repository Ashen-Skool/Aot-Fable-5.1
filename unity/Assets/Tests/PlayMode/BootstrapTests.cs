using System.Collections;
using NUnit.Framework;
using Shared;
using UnityEngine;
using UnityEngine.TestTools;

public class BootstrapTests
{
    [UnityTest]
    public IEnumerator BootstrapSpawnsBossAndMikasa()
    {
        var b = Bootstrap.Ensure();
        yield return null;
        Assert.IsNotNull(b, "Bootstrap instance");
        Assert.IsNotNull(GameObject.Find("Boss"), "Boss object exists");
        Assert.IsNotNull(GameObject.Find("Mikasa"), "Mikasa object exists");
        Assert.IsNotNull(Ctx.Get<GameObject>("mikasa"), "mikasa registered in Ctx");
        Assert.IsNotNull(Ctx.Get<Camera>("camera"), "camera registered in Ctx");
        Assert.AreEqual(Bootstrap.DefaultSeed, Ctx.Get<int>("seed"));
        // The 7 m Titan is built only for a proxies capture (-piece proxies / -lineup): v2 is one 15 m Titan,
        // and in a normal run he used to stand in the market street as bare capsules.
        Assert.IsNull(Ctx.Get<GameObject>("titan"), "no 7 m proxy in a normal run");
        Assert.IsNull(GameObject.Find("Titan"), "and nothing of him left in the scene");
        var boss = Ctx.Get<GameObject>("boss");
        Assert.IsNotNull(boss, "boss registered in Ctx");
        Assert.That(boss.GetComponent<Collider>().bounds.size.y, Is.EqualTo(15f).Within(0.5f), "boss is ~15 m tall");
    }
}
