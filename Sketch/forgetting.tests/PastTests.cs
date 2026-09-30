using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sketch.Acting;

namespace Sketch.Forgetting.Tests;

[TestClass]
public class PastTests
{
    private const string Actor = "canvas";

    [TestMethod]
    public void ThePictureAtAMidSessionEntry_ShowsTheStrokesThatWereOnTheCanvasThen()
    {
        using var temp = new TempJournal();
        using var sketch = SketchActor.Open(Actor, temp.Root, 320, 200);
        var session = new ScriptedSession(sketch);
        session.Survivor(1, 10, 10, 300, 10, "graphite");
        long midSession = session.Tentative(100);
        session.EraseTentative(100);
        session.Survivor(2, 300, 10, 300, 190, "indigo");

        var then = sketch.PictureAt(midSession);

        CollectionAssert.AreEqual(new[] { 1, 100 }, then.Strokes.Select(s => s.Id).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2 }, sketch.Picture().Strokes.Select(s => s.Id).ToArray());
    }

    [TestMethod]
    public void ForgettingThePairs_IsSafeForThePresentPicture_ButNotForThePictureOfThePast()
    {
        using var temp = new TempJournal();
        using var sketch = SketchActor.Open(Actor, temp.Root, 320, 200);
        var session = new ScriptedSession(sketch);
        session.Survivor(1, 10, 10, 300, 10, "graphite");
        long midSession = session.Tentative(100);
        session.EraseTentative(100);
        session.Survivor(2, 300, 10, 300, 190, "indigo");
        var pairs = sketch.Preview().WouldElide;

        Assert.IsTrue(sketch.Prove(pairs).IsSafe, "the present is safe to forget");
        var past = sketch.ProveAt(midSession, pairs.Where(entry => entry <= midSession).ToArray());

        Assert.IsFalse(past.IsSafe, "the same forgetting is not safe for a question about the past");
        var picture = past.Changes.Single(change => change.Observation == "picture");
        CollectionAssert.AreEqual(new[] { 1, 100 }, PictureSnapshot.Parse(picture.WithoutElision).Strokes.Select(s => s.Id).ToArray());
        CollectionAssert.AreEqual(new[] { 1 }, PictureSnapshot.Parse(picture.WithElision).Strokes.Select(s => s.Id).ToArray());
    }
}
