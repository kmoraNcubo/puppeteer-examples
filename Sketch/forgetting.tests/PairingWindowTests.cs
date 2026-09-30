using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sketch.Acting;

namespace Sketch.Forgetting.Tests;

/// <summary>
/// How far apart a Draw and its Erase may be and still be paired by the rule. A Draw
/// still waiting for its Erase is a match in progress, and the engine prunes a match
/// that has not advanced for more than 1000 entries; measured from the Draw's entry,
/// the Erase must arrive within 1001 entries.
/// </summary>
[TestClass]
public class PairingWindowTests
{
    private const string Actor = "canvas";

    [DataTestMethod]
    [DataRow(2)]
    [DataRow(1000)]
    [DataRow(1001)]
    public void AnEraseWithin1001EntriesOfItsDraw_IsPaired(int distance) =>
        Assert.IsTrue(IsPaired(distance));

    [DataTestMethod]
    [DataRow(1002)]
    [DataRow(1500)]
    public void AnEraseFartherThan1001EntriesFromItsDraw_IsNotPaired(int distance) =>
        Assert.IsFalse(IsPaired(distance));

    // Draws stroke 1, fills the journal with other strokes until the Erase of stroke 1
    // lands exactly `distance` entries after its Draw, and asks the rule's preview.
    private static bool IsPaired(int distance)
    {
        using var temp = new TempJournal();
        using var sketch = SketchActor.Open(Actor, temp.Root, 320, 200);
        var session = new ScriptedSession(sketch);
        session.Hesitation(90);                       // both Define rows are journaled first
        long drawn = session.Tentative(1);
        int filler = 1000;
        while (sketch.CurrentEntryId < drawn + distance - 1)
        {
            session.Survivor(filler++, 0, 0, 1, 1, "graphite");
        }

        long erased = session.EraseTentative(1);
        session.Survivor(filler, 0, 0, 1, 1, "graphite");
        Assert.AreEqual(drawn + distance, erased);

        var preview = sketch.Preview();
        bool paired = preview.WouldElide.Contains(drawn);
        Assert.AreEqual(paired, preview.WouldElide.Contains(erased), "a pair is elided whole or not at all");
        return paired;
    }
}
