using System.Collections.Immutable;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tetris.Tests;

/// <summary>
/// The accumulated floor: integrating a landed piece, detecting complete rows,
/// and the bottom-up collapse that pulls surviving blocks downward.
/// </summary>
[TestClass]
public sealed class PileTests
{
    /// <summary>Builds a pile from explicit (row, column) cells via a tiny one-row shape.</summary>
    private static Pile PileWith(int width, params (int Row, int Column)[] cells)
    {
        var pile = Pile.Empty(width);
        foreach (var (row, column) in cells)
        {
            pile = pile.Integrate(new SingleCell(row, column));
        }

        return pile;
    }

    /// <summary>A one-cell shape, only for assembling test piles.</summary>
    private sealed class SingleCell : Shape
    {
        public SingleCell(int row, int column) =>
            Cells = ImmutableHashSet.Create(new Position(row, column));

        public override ImmutableHashSet<Position> Cells { get; }
    }

    [TestMethod]
    public void Integrate_AddsAPiecesCellsToThePile()
    {
        var piece = Tetromino.Spawn(PieceType.O, new Position(2, 1));
        var pile = Pile.Empty(4).Integrate(piece);

        Assert.IsTrue(pile.Cells.SetEquals(piece.Cells));
        foreach (var cell in piece.Cells)
        {
            Assert.IsTrue(pile.Occupies(cell));
        }
    }

    [TestMethod]
    public void CompleteRows_FindsFullyFilledRowsOnly()
    {
        // Width 3. Row 5 full; row 4 missing one column.
        var pile = PileWith(3,
            (5, 0), (5, 1), (5, 2),
            (4, 0), (4, 1));

        var complete = pile.CompleteRows();
        Assert.AreEqual(1, complete.Count);
        Assert.IsTrue(complete.Contains(5));
        Assert.IsFalse(complete.Contains(4));
    }

    [TestMethod]
    public void ClearCompleteRows_RemovesTheRow_AndDropsBlocksAbove_ByOne()
    {
        // Width 2. Bottom row 5 is full; a single block sits above it at row 4.
        var pile = PileWith(2,
            (5, 0), (5, 1),
            (4, 0));

        var cleared = pile.ClearCompleteRows();

        // The full row is gone; the lone block above ends one row lower (4 -> 5).
        Assert.IsTrue(cleared.CompleteRows().IsEmpty, "no complete row remains");
        Assert.IsTrue(cleared.Occupies(new Position(5, 0)), "block above fell one row");
        Assert.IsFalse(cleared.Occupies(new Position(4, 0)), "old position is empty");
        Assert.AreEqual(1, cleared.Cells.Count);
    }

    [TestMethod]
    public void ClearCompleteRows_ClearsMultipleSimultaneousRows()
    {
        // Width 2. Rows 4 and 5 both full; a block at row 3 sits above both.
        var pile = PileWith(2,
            (5, 0), (5, 1),
            (4, 0), (4, 1),
            (3, 1));

        var cleared = pile.ClearCompleteRows();

        // Two rows vanished, so the surviving block drops by two (3 -> 5).
        Assert.IsTrue(cleared.CompleteRows().IsEmpty);
        Assert.AreEqual(1, cleared.Cells.Count);
        Assert.IsTrue(cleared.Occupies(new Position(5, 1)), "block above fell two rows");
    }

    [TestMethod]
    public void ClearCompleteRows_IsBottomUp_BlockAboveAClearedLineDropsByCountBelowIt()
    {
        // Width 2. A non-adjacent clear: row 5 (bottom) full, row 4 NOT full,
        // row 3 full, and a survivor at row 2.
        //   row 2: (2,1)        survivor
        //   row 3: full         cleared
        //   row 4: (4,0)        survivor (one cleared row below it -> drops 1)
        //   row 5: full         cleared
        var pile = PileWith(2,
            (2, 1),
            (3, 0), (3, 1),
            (4, 0),
            (5, 0), (5, 1));

        var cleared = pile.ClearCompleteRows();

        Assert.IsTrue(cleared.CompleteRows().IsEmpty);
        Assert.AreEqual(2, cleared.Cells.Count);

        // Survivor at row 4 had exactly one cleared row below it (row 5): 4 -> 5.
        Assert.IsTrue(cleared.Occupies(new Position(5, 0)), "row-4 survivor drops by one");

        // Survivor at row 2 had two cleared rows below it (rows 3 and 5): 2 -> 4.
        Assert.IsTrue(cleared.Occupies(new Position(4, 1)), "row-2 survivor drops by two");
    }

    [TestMethod]
    public void ClearCompleteRows_OnAPileWithoutFullRows_IsANoOp()
    {
        var pile = PileWith(3, (5, 0), (5, 1));
        var cleared = pile.ClearCompleteRows();

        Assert.IsTrue(cleared.Cells.SetEquals(pile.Cells));
    }
}
