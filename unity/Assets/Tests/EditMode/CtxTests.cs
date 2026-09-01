using NUnit.Framework;
using Shared;

public class CtxTests
{
    [SetUp] public void Reset() => Ctx.Clear();

    [Test]
    public void SetThenGetReturnsValue()
    {
        Ctx.Set("answer", 42);
        Assert.AreEqual(42, Ctx.Get<int>("answer"));
        Assert.IsTrue(Ctx.Has("answer"));
    }

    [Test]
    public void MissingOrWrongTypeReturnsDefault()
    {
        Assert.AreEqual(0, Ctx.Get<int>("nope"));
        Assert.IsNull(Ctx.Get<string>("nope"));
        Ctx.Set("s", "text");
        Assert.AreEqual(0, Ctx.Get<int>("s"));
    }

    [Test]
    public void RequireThrowsWhenMissing()
    {
        Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => Ctx.Require<string>("missing"));
    }

    [Test]
    public void SetOverwritesAndRemoveWorks()
    {
        Ctx.Set("k", 1);
        Ctx.Set("k", 2);
        Assert.AreEqual(2, Ctx.Get<int>("k"));
        Assert.IsTrue(Ctx.Remove("k"));
        Assert.IsFalse(Ctx.Has("k"));
    }
}
