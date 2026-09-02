using System.Collections;
using NUnit.Framework;
using ODM;
using Shared;
using UnityEngine;
using UnityEngine.TestTools;

public class OdmFlightTests
{
    [SetUp] public void Fast() { Time.timeScale = 5f; }
    [TearDown] public void Normal() { Time.timeScale = 1f; }

    [UnityTest]
    public IEnumerator ScriptedHookAndBoostReachesSpeedAndLandsOnRoof()
    {
        Bootstrap.Ensure();
        yield return null;
        var player = OdmBoot.Ensure(true);
        Assert.IsNotNull(player, "controller attached");
        Assert.AreSame(player, Ctx.Get<OdmController>("player"), "player registered in Ctx");
        var grid = Ctx.Get<HookTestGrid>("odmGrid");
        Assert.IsNotNull(grid, "grid built");
        yield return new WaitForFixedUpdate();

        var script = OdmBoot.PlayDemo(true);
        float gasStart = player.Gas;
        bool hooked = false, boosted = false;
        float minGas = gasStart;
        int steps = 0;
        while (script.Playing && steps < 60 * 12)
        {
            yield return new WaitForFixedUpdate();
            steps++;
            if (player.Hook == HookState.Attached) hooked = true;
            if (player.Boosting) boosted = true;
            if (player.Gas < minGas) minGas = player.Gas;
        }
        // let the landing settle
        for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();

        Assert.IsTrue(hooked, "a hook attached during the script");
        Assert.IsTrue(boosted, "gas boost fired during the script");
        Assert.Less(minGas, gasStart, "boost drained gas");
        Assert.Greater(player.MaxSpeedSeen, 25f, "peak speed > 25 m/s (was " + player.MaxSpeedSeen.ToString("0.0") + ")");
        Assert.IsTrue(player.Grounded, "player is grounded at the end (pos " + player.transform.position + ")");
        Assert.AreEqual(OdmLayers.Hook, player.GroundLayer, "standing on a HookTarget surface");
        Assert.Greater(player.transform.position.y, 15f, "landed on a rooftop, not the street (y=" + player.transform.position.y.ToString("0.0") + ")");
        Assert.IsNotNull(Ctx.Get<Shared.Rigs.IPoser>("mikasaPoser"), "controller drives the Mikasa proxy poser");
        Assert.Less(player.Speed, 2f, "came to rest after landing");
    }

    [UnityTest]
    public IEnumerator GasRefillsWhileGrounded()
    {
        Bootstrap.Ensure();
        yield return null;
        var player = OdmBoot.Ensure(true);
        yield return new WaitForFixedUpdate();
        var s = new FlightScript { name = "hop" };
        s.Add(0f, 0, 0, false, true, false, null, "hop");   // gas hop on the ground
        s.Add(0.2f, 0, 0, false, false, false, null, "idle");
        s.End(0.3f);
        player.Play(s);
        while (s.Playing) yield return new WaitForFixedUpdate();
        float afterHop = player.Gas;
        Assert.Less(afterHop, player.GasMax, "hop cost gas");
        for (int i = 0; i < 60 * 3; i++) yield return new WaitForFixedUpdate();
        Assert.Greater(player.Gas, afterHop, "gas refilled while grounded");
    }
}
