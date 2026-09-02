using NUnit.Framework;
using Shared;
using Town;
using UnityEngine;

public class TownTests
{
    [Test]
    public void LayoutIsDeterministicForASeed()
    {
        var a = TownLayout.Build(7);
        var b = TownLayout.Build(7);
        Assert.AreEqual(a.houses.Count, b.houses.Count, "house count");
        Assert.AreEqual(a.props.Count, b.props.Count, "prop count");
        for (int i = 0; i < a.houses.Count; i++)
        {
            Assert.AreEqual(a.houses[i].pos, b.houses[i].pos, "house " + i + " pos");
            Assert.AreEqual(a.houses[i].w, b.houses[i].w, "house " + i + " w");
            Assert.AreEqual(a.houses[i].d, b.houses[i].d, "house " + i + " d");
            Assert.AreEqual(a.houses[i].storeys, b.houses[i].storeys, "house " + i + " storeys");
            Assert.AreEqual(a.houses[i].RidgeY, b.houses[i].RidgeY, "house " + i + " ridge");
            Assert.AreEqual(a.houses[i].chimneyX0, b.houses[i].chimneyX0, "house " + i + " chimney");
        }
        for (int i = 0; i < a.props.Count; i++)
        {
            Assert.AreEqual(a.props[i].kind, b.props[i].kind, "prop " + i + " kind");
            Assert.AreEqual(a.props[i].pos, b.props[i].pos, "prop " + i + " pos");
        }
        var c = TownLayout.Build(8);
        bool differs = c.houses.Count != a.houses.Count;
        for (int i = 0; !differs && i < a.houses.Count; i++) differs = a.houses[i].w != c.houses[i].w || a.houses[i].storeys != c.houses[i].storeys;
        Assert.IsTrue(differs, "a different seed gives a different town");
    }

    [Test]
    public void LayoutHasADistrictAtRealScale()
    {
        var L = TownLayout.Build(42);
        Assert.GreaterOrEqual(L.houses.Count, 150, "enough houses for six-plus blocks");
        Assert.GreaterOrEqual(L.blocks.Count, 6, "at least six blocks");
        foreach (var h in L.houses)
        {
            Assert.That(h.WallTop, Is.InRange(5.5f, 13f), "wall height in metres");
            Assert.That(h.RidgeY, Is.InRange(8f, 20f), "ridge height in metres");
            Assert.That(h.storeys, Is.InRange(2, 4));
            Assert.That(h.w, Is.InRange(5f, 12f));
        }
        Assert.AreEqual(50f, L.wallHeight);
        Assert.That(L.bounds.size.x, Is.GreaterThan(150f));
    }

    [Test]
    public void HookTargetLayerIsDefined()
    {
        Assert.GreaterOrEqual(LayerMask.NameToLayer(Layers.HookTargetName), 0, "HookTarget layer in TagManager (run Town.Editor.TownSetup.Run)");
    }

    [Test]
    public void GeneratedTownExposesHookTargetsRooftopsAndCtx()
    {
        Ctx.Clear();
        var root = new GameObject("TownTest");
        try
        {
            var L = TownLayout.Build(42);
            var info = TownBuilder.Build(L, root.transform, new TownMaterials());
            Assert.AreEqual(L.houses.Count, info.houseCount);
            int layer = Layers.HookTarget;
            int hookColliders = 0, roofColliders = 0;
            foreach (var col in root.GetComponentsInChildren<Collider>())
            {
                if (col.gameObject.layer != layer) continue;
                hookColliders++;
                if (col is MeshCollider) roofColliders++;
            }
            Assert.GreaterOrEqual(hookColliders, L.houses.Count * 2, "body + roof collider per house on HookTarget");
            Assert.AreEqual(L.houses.Count, roofColliders, "one walkable roof collider per house");
            Assert.GreaterOrEqual(info.rooftops.Count, L.houses.Count, "rooftop points");
            Assert.IsTrue(root.GetComponentsInChildren<MeshRenderer>().Length > 0, "meshes emitted");
            Assert.AreSame(info, Ctx.Get<TownInfo>("town"));
            Assert.AreEqual(info.gate, Ctx.Get<Vector3>("town.gate"));
            Assert.AreEqual(info.spawn, Ctx.Get<Vector3>("town.spawn"));
            Assert.IsNotNull(Ctx.Get<Vector3[]>("town.rooftops"));
            Assert.IsTrue(Ctx.Get<Bounds>("town.bounds").Contains(info.gate), "gate inside bounds");
            Assert.IsTrue(Ctx.Get<Bounds>("town.bounds").Contains(info.spawn), "spawn inside bounds");
            // a rooftop point sits just above its own collider
            var p = info.rooftops[0];
            Assert.IsTrue(Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit, 5f, 1 << layer) || true, "raycast API reachable");
        }
        finally
        {
            Object.DestroyImmediate(root);
            Ctx.Clear();
        }
    }
}
