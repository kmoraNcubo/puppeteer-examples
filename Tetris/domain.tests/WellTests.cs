using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tetris.Tests;

/// <summary>
/// Behaviour of the aggregate root: guarded moves, landing, line clears driven
/// through the well, game over, and determinism. Pieces enter through a
/// scripted source so every scenario is exact and replayable.
/// </summary>
[TestClass]
public sealed class WellTests
{
    private static Well NarrowWell(int width, int height, params PieceType[] script) =>
        new(width, height, new ScriptedPieceSource(script));

    [TestMethod]
    public void Open_SpawnsTheFirstScriptedPiece_Centred()
    {
        // Width 10 -> 4-wide box anchored at column (10-4)/2 = 3.
        var well = NarrowWell(10, 20, PieceType.O);

        Assert.IsFalse(well.IsGameOver);
        Assert.IsNotNull(well.Active);
        Assert.AreEqual(PieceType.O, well.Active!.Type);
        // O spawn cells: (0,3)(0,4)(1,3)(1,4)
        Assert.IsTrue(well.Active.Occupies(new Position(0, 3)));
        Assert.IsTrue(well.Active.Occupies(new Position(1, 4)));
    }

    [TestMethod]
    public void MoveLeft_IsBlockedByTheWall_AndIsANoOpAtTheEdge()
    {
        // Width 4 -> O spawns at column 0, already against the left wall.
        var well = NarrowWell(4, 10, PieceType.O, PieceType.O);
        var before = well.Active!.Cells;

        well.MoveLeft(); // would push into column -1 (the wall): rejected

        Assert.IsTrue(well.Active!.Cells.SetEquals(before), "move into the wall is a no-op");
    }

    [TestMethod]
    public void MoveRight_SlidesUntilItMeetsTheWall_ThenStops()
    {
        var well = NarrowWell(4, 10, PieceType.O, PieceType.O);
        // O occupies columns 0..1; can move right once to columns 1..2,
        // and again to columns 2..3 (right wall at column 4). A third is blocked.
        well.MoveRight();
        well.MoveRight();
        Assert.IsTrue(well.Active!.Occupies(new Position(0, 2)));
        Assert.IsTrue(well.Active.Occupies(new Position(0, 3)));

        well.MoveRight(); // into the wall: rejected
        Assert.IsTrue(well.Active.Occupies(new Position(0, 3)), "still against the wall");
    }

    [TestMethod]
    public void Tick_DescendsUntilTheFloor_ThenLands()
    {
        // Width 4, height 4. O spawns at rows 0..1. Floor is row 4.
        var well = NarrowWell(4, 4, PieceType.O, PieceType.O);

        // Tick down: rows 0..1 -> 1..2 -> 2..3. Next tick would hit the floor (row 4).
        well.Tick(); // -> rows 1..2
        well.Tick(); // -> rows 2..3
        Assert.IsTrue(well.Active!.Occupies(new Position(3, 0)), "resting on the floor line");

        well.Tick(); // lands; the next O spawns
        Assert.IsTrue(well.Pile.Occupies(new Position(3, 0)), "landed cell joined the pile");
        Assert.IsTrue(well.Pile.Occupies(new Position(2, 1)));
        Assert.AreEqual(PieceType.O, well.Active!.Type, "the next piece is active");
        Assert.IsTrue(well.Active.Occupies(new Position(0, 0)), "next piece spawned at the top");
    }

    [TestMethod]
    public void Drop_LandsAPieceOnTopOfThePile()
    {
        var well = NarrowWell(4, 6, PieceType.O, PieceType.O, PieceType.O);

        well.Drop(); // first O rests on the floor: rows 4..5
        Assert.IsTrue(well.Pile.Occupies(new Position(5, 0)));
        Assert.IsTrue(well.Pile.Occupies(new Position(4, 1)));

        well.Drop(); // second O lands on top of the first: rows 2..3
        Assert.IsTrue(well.Pile.Occupies(new Position(3, 0)), "stacked on top of the pile");
        Assert.IsTrue(well.Pile.Occupies(new Position(2, 1)));
    }

    [TestMethod]
    public void Landing_IntegratesThePieceIntoThePile()
    {
        // Height 4: floor at row 4. An O spawning at rows 0..1 falls until its
        // bottom rests on row 3 (just above the floor) — landing cells rows 2..3.
        var well = NarrowWell(4, 4, PieceType.O, PieceType.O);

        well.Drop();

        foreach (var cell in new[]
                 {
                     new Position(2, 0), new Position(2, 1),
                     new Position(3, 0), new Position(3, 1),
                 })
        {
            Assert.IsTrue(well.Pile.Occupies(cell), $"pile should contain {cell}");
        }
    }

    [TestMethod]
    public void Rotate_IsAllowedInOpenSpace_AndTogglesThePose()
    {
        // Width 4, height 8. An I-piece spawns horizontal across columns 0..3
        // (row 1). Rotating to vertical sweeps column 2 across rows 0..3, which
        // is open, so the rotation is accepted; rotating back is accepted too.
        var well = NarrowWell(4, 8, PieceType.I, PieceType.I);

        Assert.AreEqual(0, well.Active!.Orientation.Index, "spawns horizontal");
        well.RotateClockwise();
        Assert.AreEqual(1, well.Active!.Orientation.Index, "rotates to vertical in open space");
        well.RotateClockwise();
        Assert.AreEqual(0, well.Active!.Orientation.Index, "toggles back to horizontal");
    }

    [TestMethod]
    public void RotateCounterClockwise_ReversesClockwise_ThroughTheWell()
    {
        // Width 6, height 8 so a tee has room to turn freely. The tee cycles
        // 0->1->2->3 clockwise; counter-clockwise must walk it back the same way.
        var well = NarrowWell(6, 8, PieceType.T, PieceType.T);

        well.RotateClockwise();
        Assert.AreEqual(1, well.Active!.Orientation.Index);
        well.RotateClockwise();
        Assert.AreEqual(2, well.Active!.Orientation.Index);

        well.RotateCounterClockwise();
        Assert.AreEqual(1, well.Active!.Orientation.Index, "counter-clockwise steps back");
        well.RotateCounterClockwise();
        Assert.AreEqual(0, well.Active!.Orientation.Index);
        well.RotateCounterClockwise();
        Assert.AreEqual(3, well.Active!.Orientation.Index, "wraps below zero to the last pose");
    }

    [TestMethod]
    public void Rotate_IsRejectedWhenItWouldCollideWithTheWall()
    {
        // Width 4, height 8. The I-piece spawns horizontal (cols 0..3, row 1).
        // Turn it vertical (column 2), then shove it to the left wall. Turning
        // it back to horizontal there would sweep columns -2..1 — across the
        // left wall — so the rotation must be rejected.
        var well = NarrowWell(4, 8, PieceType.I, PieceType.I);

        well.RotateClockwise();   // -> vertical, column 2
        well.MoveLeft();          // -> column 1
        well.MoveLeft();          // -> column 0 (against the left wall)
        Assert.IsTrue(well.Active!.Occupies(new Position(0, 0)), "vertical bar at the left wall");

        var before = well.Active!.Cells;
        well.RotateCounterClockwise(); // back to horizontal would cross the wall
        Assert.IsTrue(well.Active!.Cells.SetEquals(before), "rotation into the wall rejected");
        Assert.AreEqual(1, well.Active!.Orientation.Index, "still vertical");
    }

    [TestMethod]
    public void Rotate_IsRejectedWhenItWouldCollideWithThePile()
    {
        // Width 4, height 8. Build a two-cell-tall pile under the right half so
        // that an I-piece, rotated to vertical against it, would overlap.
        //
        // Plan: drop an O to the floor, then shove it right so it rests at
        // columns 2..3, rows 6..7. Then take a horizontal I (cols 0..3, row 1),
        // tick it down to sit at row 5 (just above the O at rows 6..7 in cols
        // 2..3), and attempt to rotate to vertical. Vertical-I cells would be
        // column 2 across rows 4..7 — rows 6 and 7 in column 2 are occupied by
        // the O, so the rotation must be rejected.
        var well = NarrowWell(4, 8, PieceType.O, PieceType.I, PieceType.O);

        well.MoveRight();
        well.MoveRight();
        well.Drop(); // O rests at rows 6..7, cols 2..3

        Assert.IsTrue(well.Pile.Occupies(new Position(6, 2)));
        Assert.IsTrue(well.Pile.Occupies(new Position(7, 3)));

        // I is now active, horizontal at row 1 cols 0..3. Tick it down to row 5
        // (cells (5,0..3)). The next tick down would put a cell at row 6 col 2/3?
        // No — horizontal I spans all columns at one row; descending to row 6
        // would overlap the O at (6,2)(6,3), so it lands at row 5 if we let it.
        // We instead stop one tick early and rotate there.
        for (var i = 0; i < 4; i++) // rows 1 -> 5
        {
            well.Tick();
        }
        Assert.AreEqual(PieceType.I, well.Active!.Type, "I has not landed yet");
        Assert.IsTrue(well.Active!.Occupies(new Position(5, 2)), "I rests at row 5");

        var beforeRotate = well.Active!.Cells;
        well.RotateClockwise(); // vertical would be column 2 rows 4..7; rows 6,7 are filled
        Assert.IsTrue(well.Active!.Cells.SetEquals(beforeRotate), "rotation into the pile rejected");
        Assert.AreEqual(0, well.Active!.Orientation.Index, "still horizontal");
    }

    [TestMethod]
    public void Tick_ClearsACompleteRow_ThroughTheWell()
    {
        // Width 4. Two O pieces side by side fill a 2-wide-by-2-tall block;
        // that is not a full row. Instead use I pieces: a horizontal I fills
        // all 4 columns of one row. Stack two horizontal I's to fill two rows.
        var well = NarrowWell(4, 6, PieceType.I, PieceType.I, PieceType.O);

        well.Drop(); // I rests on floor: horizontal across row 5 (cols 0..3) — a full row!
        // It clears immediately on landing.
        Assert.AreEqual(1, well.ClearedLines, "the full floor row cleared");
        Assert.IsTrue(well.Pile.CompleteRows().IsEmpty);
        Assert.AreEqual(0, well.Pile.Cells.Count, "pile is empty after the clear");

        well.Drop(); // second I again fills and clears the floor row
        Assert.AreEqual(2, well.ClearedLines);
        Assert.AreEqual(0, well.Pile.Cells.Count);
    }

    [TestMethod]
    public void LineClear_ThroughWell_DropsAStackedBlockOneRowLower()
    {
        // Width 4, height 6.
        // Plan: fill the bottom row with an I (clears), but first park a single
        // O on top so that after the clear it ends one row lower.
        // Simpler deterministic plan: drop an O to the left, then an I that
        // completes... but O occupies 2 columns, the I would overlap. Instead:
        // Use width 4 and pieces that compose cleanly.
        //
        // Step 1: O dropped to the floor occupies cols 0..1, rows 4..5.
        // Step 2: O dropped occupies cols 2..3, rows 4..5 — together rows 4 and 5
        //         are BOTH full across all 4 columns -> two rows clear at once.
        // That leaves an empty pile, not a "stacked block" case. To get a
        // survivor, add a third O on top of the first before the second lands.
        //
        // Deterministic survivor scenario:
        //   - O #1 -> floor left  (cols 0..1, rows 4..5)
        //   - O #2 stacked on #1  (cols 0..1, rows 2..3)
        //   - O #3 -> floor right (cols 2..3, rows 4..5) completes rows 4 and 5
        // Result: rows 4 and 5 clear; the surviving block (rows 2..3, cols 0..1)
        // drops by two -> rows 4..5.
        var well = NarrowWell(4, 6, PieceType.O, PieceType.O, PieceType.O, PieceType.O);

        well.Drop();                 // O#1 -> rows 4..5, cols 0..1
        well.Drop();                 // O#2 -> rows 2..3, cols 0..1 (on top of #1)
        Assert.AreEqual(0, well.ClearedLines, "nothing full yet");

        well.MoveRight();            // shift O#3 right: cols 0..1 -> 1..2
        well.MoveRight();            // -> cols 2..3
        well.Drop();                 // O#3 -> rows 4..5, cols 2..3 => rows 4 and 5 full

        Assert.AreEqual(2, well.ClearedLines, "two rows cleared at once");
        Assert.IsTrue(well.Pile.CompleteRows().IsEmpty);
        // The survivor block (was rows 2..3, cols 0..1) dropped by two rows.
        Assert.IsTrue(well.Pile.Occupies(new Position(4, 0)));
        Assert.IsTrue(well.Pile.Occupies(new Position(5, 1)));
        Assert.AreEqual(4, well.Pile.Cells.Count, "only the 2x2 survivor remains");
    }

    [TestMethod]
    public void GameOver_WhenAFreshlySpawnedPieceImmediatelyCollides()
    {
        // Width 4, height 2 — a very shallow well. Stack O pieces until a fresh
        // spawn has nowhere to go.
        var well = NarrowWell(4, 2,
            PieceType.O, // #1
            PieceType.O, // #2 should not fit -> game over on its spawn
            PieceType.O);

        // O#1 spawns at rows 0..1 (the whole height). Drop it: rests on floor
        // rows 0..1 (height 2 -> floor at row 2). It fills rows 0..1 cols 0..1.
        well.Drop();

        // Spawning O#2 at rows 0..1 cols 0..1 now overlaps the pile -> game over.
        Assert.IsTrue(well.IsGameOver, "no room for the next piece");
        Assert.IsNull(well.Active, "no active piece once the game is over");

        // Verbs are inert after game over.
        well.MoveLeft();
        well.Tick();
        well.RotateClockwise();
        Assert.IsTrue(well.IsGameOver);
    }

    [TestMethod]
    public void GameOver_OnOpen_WhenTheVeryFirstPieceCannotAppear()
    {
        // Height 1, width 4. O needs 2 rows; its spawn cells reach row 1 = floor.
        var well = NarrowWell(4, 1, PieceType.O);

        Assert.IsTrue(well.IsGameOver, "the first piece collides with the floor on spawn");
        Assert.IsNull(well.Active);
    }

    [TestMethod]
    public void ActivePieceExists_IfAndOnlyIf_TheGameIsNotOver()
    {
        // While play continues there is always an active piece…
        var playing = NarrowWell(4, 6, PieceType.O, PieceType.O);
        Assert.IsFalse(playing.IsGameOver);
        Assert.IsNotNull(playing.Active);

        // …and once the game is over there is none. (The invariant is also
        // checked inside the well after every transition.)
        var over = NarrowWell(4, 1, PieceType.O);
        Assert.IsTrue(over.IsGameOver);
        Assert.IsNull(over.Active);
    }

    [TestMethod]
    public void DefaultConstructor_DrawsItsPiecesAtRandom_AndOpensPlayable()
    {
        // No source supplied: the well decides the next piece itself, at random.
        // On a normal-sized board the first piece always fits, so play opens.
        var well = new Well(10, 20);

        Assert.IsFalse(well.IsGameOver);
        Assert.IsNotNull(well.Active);
        CollectionAssert.Contains(System.Enum.GetValues<PieceType>(), well.Active!.Type);
    }
}
