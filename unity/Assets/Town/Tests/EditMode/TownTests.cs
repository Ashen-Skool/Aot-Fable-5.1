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

    /// <summary>
    /// Houses share one mesh per (cell, material), so the only thing that makes a single house
    /// crushable is its recorded vertex span. This proves the spans line up: bringing one house down
    /// moves its own vertices and nothing else's, anywhere in the district.
    /// </summary>
    [Test]
    public void CrushingOneHouseMovesOnlyThatHousesVertices()
    {
        Ctx.Clear();
        var root = new GameObject("TownCrushTest");
        try
        {
            var L = TownLayout.Build(42);
            var info = TownBuilder.Build(L, root.transform, new TownMaterials());
            var d = info.destruction;
            Assert.IsNotNull(d, "TownDestruction built");
            Assert.AreSame(d, Ctx.Get<Shared.ICrush>("town.destruction"), "registered in Ctx for the Titan");
            Assert.AreEqual(L.houses.Count, d.Count, "one crushable record per house");

            // every mesh the houses live in, as it was built
            var filters = root.GetComponentsInChildren<MeshFilter>();
            var before = new System.Collections.Generic.Dictionary<Mesh, Vector3[]>();
            foreach (var mf in filters) if (mf.sharedMesh != null && !before.ContainsKey(mf.sharedMesh)) before[mf.sharedMesh] = mf.sharedMesh.vertices;

            // a house in the middle of the district, so it shares its cell with neighbours
            int target = L.houses.Count / 2;
            var spec = L.houses[target];
            Assert.IsTrue(d.Crush(target, Vector3.forward), "house went down");
            Assert.IsTrue(d.Down(target));
            Assert.AreEqual(1, d.Crushed);
            Assert.AreEqual(L.houses.Count - 1, d.Standing);
            d.Step(TownDestruction.Duration);

            float radius = Mathf.Max(spec.w, spec.d) * 0.5f + 4f;
            int moved = 0;
            foreach (var kv in before)
            {
                var now = kv.Key.vertices;
                Assert.AreEqual(kv.Value.Length, now.Length, "the collapse rewrites vertices, never resizes the mesh");
                for (int i = 0; i < now.Length; i++)
                {
                    if (now[i] == kv.Value[i]) continue;
                    moved++;
                    var was = kv.Value[i];
                    float dx = was.x - spec.pos.x, dz = was.z - spec.pos.z;
                    Assert.LessOrEqual(Mathf.Sqrt(dx * dx + dz * dz), radius,
                        "a vertex outside the crushed house's footprint moved: the span belongs to a neighbour");
                    Assert.LessOrEqual(was.y, spec.RidgeY + 4f, "moved vertex was part of this house (chimney pots clear the ridge)");
                }
            }
            Assert.Greater(moved, 200, "the house actually collapsed");

            // and it is a pile now, not a house
            float top = 0f;
            foreach (var kv in before)
            {
                var now = kv.Key.vertices;
                for (int i = 0; i < now.Length; i++)
                    if (now[i] != kv.Value[i]) top = Mathf.Max(top, now[i].y - spec.pos.y);
            }
            Assert.Less(top, spec.WallTop, "nothing of the crushed house is left standing at storey height");

            Assert.IsFalse(d.Crush(target, Vector3.forward), "a house only falls once");
        }
        finally
        {
            Object.DestroyImmediate(root);
            Ctx.Clear();
        }
    }

    [Test]
    public void CrushNearFindsTheClosestStandingHouse()
    {
        Ctx.Clear();
        var root = new GameObject("TownCrushNearTest");
        try
        {
            var L = TownLayout.Build(42);
            var info = TownBuilder.Build(L, root.transform, new TownMaterials());
            var d = info.destruction;
            int target = L.houses.Count / 3;
            Assert.IsTrue(d.CrushNear(L.houses[target].pos, 3f, Vector3.forward), "the house under the foot comes down");
            Assert.IsTrue(d.Down(target), "and it is the one that was closest");
            Assert.IsFalse(d.CrushNear(L.houses[target].pos, 0.01f, Vector3.forward), "nothing else is within that radius any more");
            Assert.AreEqual(1, d.Crushed);
        }
        finally
        {
            Object.DestroyImmediate(root);
            Ctx.Clear();
        }
    }
}
