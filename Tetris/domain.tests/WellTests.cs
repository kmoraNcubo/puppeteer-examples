using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tetris.Tests;

/// <summary>
/// Behaviour of the aggregate root: guarded moves, landing, line clears driven
/// through the well, the derived game-over, and the throw-vs-no-op contract.
/// Pieces enter through a scripted source so every scenario is exact and
/// replayable.
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

        well.MoveLeft(); // would push into column -1 (the wall): rejected, no throw

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
        // Width 4, height 6. O spawns at rows 0..1. Floor is row 6.
        var well = NarrowWell(4, 6, PieceType.O, PieceType.O);

        well.Tick(); // -> rows 1..2
        well.Tick(); // -> rows 2..3
        well.Tick(); // -> rows 3..4
        well.Tick(); // -> rows 4..5
        Assert.IsTrue(well.Active!.Occupies(new Position(5, 0)), "resting on the floor line");

        well.Tick(); // lands; the next O spawns
        Assert.IsTrue(well.Pile.Occupies(new Position(5, 0)), "landed cell joined the pile");
        Assert.IsTrue(well.Pile.Occupies(new Position(4, 1)));
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
        // Height 6: floor at row 6. An O falls until its bottom rests on row 5 —
        // landing cells rows 4..5.
        var well = NarrowWell(4, 6, PieceType.O, PieceType.O);

        well.Drop();

        foreach (var cell in new[]
                 {
                     new Position(4, 0), new Position(4, 1),
                     new Position(5, 0), new Position(5, 1),
                 })
        {
            Assert.IsTrue(well.Pile.Occupies(cell), $"pile should contain {cell}");
        }
    }

    [TestMethod]
    public void Rotate_IsAllowedInOpenSpace_AndCyclesThePose()
    {
        // Width 4, height 8. An I-piece spawns horizontal across columns 0..3
        // (row 1). Rotating sweeps column 2 across rows 0..3, which is open, so
        // the rotation is accepted; rotating again cycles it back (2 poses).
        var well = NarrowWell(4, 8, PieceType.I, PieceType.I);

        Assert.AreEqual(0, well.Active!.Orientation.Index, "spawns horizontal");
        well.Rotate();
        Assert.AreEqual(1, well.Active!.Orientation.Index, "rotates to vertical in open space");
        well.Rotate();
        Assert.AreEqual(0, well.Active!.Orientation.Index, "cycles back to horizontal");
    }

    [TestMethod]
    public void Rotate_IsRejectedWhenItWouldCollideWithTheWall()
    {
        // Width 4, height 8. The I-piece spawns horizontal (cols 0..3, row 1).
        // Turn it vertical (column 2), then shove it to the left wall. Turning
        // it again (back to horizontal) there would sweep columns -2..1 — across
        // the left wall — so the rotation must be rejected as a no-op.
        var well = NarrowWell(4, 8, PieceType.I, PieceType.I);

        well.Rotate();            // -> vertical, column 2
        well.MoveLeft();          // -> column 1
        well.MoveLeft();          // -> column 0 (against the left wall)
        Assert.IsTrue(well.Active!.Occupies(new Position(0, 0)), "vertical bar at the left wall");

        var before = well.Active!.Cells;
        well.Rotate(); // back to horizontal would cross the wall: rejected, no throw
        Assert.IsTrue(well.Active!.Cells.SetEquals(before), "rotation into the wall rejected");
        Assert.AreEqual(1, well.Active!.Orientation.Index, "still vertical");
    }

    [TestMethod]
    public void Rotate_IsRejectedWhenItWouldCollideWithThePile()
    {
        // Width 4, height 8. Build a two-cell-tall pile under the right half so
        // that an I-piece, rotated to vertical against it, would overlap.
        //
        // Drop an O shoved right (rests rows 6..7, cols 2..3). Tick a horizontal
        // I down to row 5; rotating to vertical would sweep column 2 across rows
        // 4..7 — rows 6,7 there are filled — so the rotation is rejected.
        var well = NarrowWell(4, 8, PieceType.O, PieceType.I, PieceType.O);

        well.MoveRight();
        well.MoveRight();
        well.Drop(); // O rests at rows 6..7, cols 2..3

        Assert.IsTrue(well.Pile.Occupies(new Position(6, 2)));
        Assert.IsTrue(well.Pile.Occupies(new Position(7, 3)));

        for (var i = 0; i < 4; i++) // rows 1 -> 5
        {
            well.Tick();
        }
        Assert.AreEqual(PieceType.I, well.Active!.Type, "I has not landed yet");
        Assert.IsTrue(well.Active!.Occupies(new Position(5, 2)), "I rests at row 5");

        var beforeRotate = well.Active!.Cells;
        well.Rotate(); // vertical would be column 2 rows 4..7; rows 6,7 are filled
        Assert.IsTrue(well.Active!.Cells.SetEquals(beforeRotate), "rotation into the pile rejected");
        Assert.AreEqual(0, well.Active!.Orientation.Index, "still horizontal");
    }

    [TestMethod]
    public void Tick_ClearsACompleteRow_ThroughTheWell()
    {
        // Width 4. A horizontal I fills all 4 columns of one row. Stack two to
        // clear the floor row twice.
        var well = NarrowWell(4, 6, PieceType.I, PieceType.I, PieceType.O);

        well.Drop(); // I rests on the floor: horizontal across row 5 — a full row, clears at once
        Assert.AreEqual(1, well.ClearedLines, "the full floor row cleared");
        Assert.IsTrue(well.Pile.CompleteRows().IsEmpty);
        Assert.AreEqual(0, well.Pile.Cells.Count, "pile is empty after the clear");

        well.Drop(); // second I again fills and clears the floor row
        Assert.AreEqual(2, well.ClearedLines);
        Assert.AreEqual(0, well.Pile.Cells.Count);
    }

    [TestMethod]
    public void LineClear_ThroughWell_DropsAStackedBlockLower()
    {
        // Width 4, height 6. Survivor scenario:
        //   - O #1 -> floor left  (cols 0..1, rows 4..5)
        //   - O #2 stacked on #1  (cols 0..1, rows 2..3)
        //   - O #3 -> floor right (cols 2..3, rows 4..5) completes rows 4 and 5
        // Rows 4 and 5 clear; the surviving block (rows 2..3, cols 0..1) drops by
        // two -> rows 4..5.
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
    public void IsGameOver_FlipsWhenThePileRisesIntoTheSpawnRegion()
    {
        // Width 4, height 2 — a shallow well. The spawn region is rows 0..1,
        // columns 0..3. An O dropped onto the floor (rows 0..1, cols 0..1) lands
        // straight into the spawn region, so the derived game-over flips.
        var well = NarrowWell(4, 2, PieceType.O, PieceType.O);

        Assert.IsFalse(well.IsGameOver, "play opens normally");
        Assert.IsNotNull(well.Active);

        well.Drop(); // O fills rows 0..1, cols 0..1 — inside the spawn region

        Assert.IsTrue(well.IsGameOver, "pile has risen into the spawn region");
        Assert.IsNull(well.Active, "no active piece once the game is over");
    }

    [TestMethod]
    public void EveryVerb_ThrowsOnAFinishedGame_AndLeavesTheWellUnchanged()
    {
        var well = NarrowWell(4, 2, PieceType.O, PieceType.O);
        well.Drop(); // reach game over
        Assert.IsTrue(well.IsGameOver);

        // Snapshot the full state before each invalid operation.
        var pileBefore = well.Pile.Cells;
        var clearedBefore = well.ClearedLines;

        AssertVerbThrowsAndStateUnchanged(well, w => w.MoveLeft(), pileBefore, clearedBefore);
        AssertVerbThrowsAndStateUnchanged(well, w => w.MoveRight(), pileBefore, clearedBefore);
        AssertVerbThrowsAndStateUnchanged(well, w => w.Rotate(), pileBefore, clearedBefore);
        AssertVerbThrowsAndStateUnchanged(well, w => w.Tick(), pileBefore, clearedBefore);
        AssertVerbThrowsAndStateUnchanged(well, w => w.Drop(), pileBefore, clearedBefore);
    }

    private static void AssertVerbThrowsAndStateUnchanged(
        Well well, System.Action<Well> verb,
        System.Collections.Immutable.ImmutableHashSet<Position> pileBefore, int clearedBefore)
    {
        Assert.ThrowsException<GameOverException>(() => verb(well));

        Assert.IsTrue(well.IsGameOver, "still game over");
        Assert.IsNull(well.Active, "still no active piece");
        Assert.IsTrue(well.Pile.Cells.SetEquals(pileBefore), "pile unchanged by the failed verb");
        Assert.AreEqual(clearedBefore, well.ClearedLines, "cleared count unchanged by the failed verb");
    }

    [TestMethod]
    public void BlockedMove_IsANoOp_NotAThrow()
    {
        // A move blocked by a wall is a valid no-op: the piece stays and nothing
        // is thrown. (Contrast with operating on a finished game, which throws.)
        var well = NarrowWell(4, 10, PieceType.O, PieceType.O);
        var before = well.Active!.Cells;

        well.MoveLeft(); // against the left wall

        Assert.IsTrue(well.Active!.Cells.SetEquals(before), "blocked move left the piece in place");
        Assert.IsFalse(well.IsGameOver);
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
        var over = NarrowWell(4, 2, PieceType.O, PieceType.O);
        over.Drop();
        Assert.IsTrue(over.IsGameOver);
        Assert.IsNull(over.Active);
    }

    [TestMethod]
    public void Collision_RejectsWallFloorAndPile_WithTheFramePredicate()
    {
        // The frame is a boundary predicate, not a cell set; collision still
        // catches all three cases through the same membership probe.
        var well = NarrowWell(6, 6, PieceType.O, PieceType.O, PieceType.O);

        // Left wall: spawn at cols 1..2 (anchor (6-4)/2 = 1), shove left to the wall.
        well.MoveLeft(); // cols 0..1, against the wall
        var atWall = well.Active!.Cells;
        well.MoveLeft(); // blocked by the wall predicate
        Assert.IsTrue(well.Active!.Cells.SetEquals(atWall), "left wall blocks");

        // Floor + pile: drop to the floor (cols 0..1, rows 4..5), then a second
        // O — which spawns at cols 1..2 — stacks where it meets the pile.
        well.Drop(); // rests on the floor (frame predicate stopped it)
        Assert.IsTrue(well.Pile.Occupies(new Position(5, 0)), "stopped by the floor");
        well.Drop(); // second O (cols 1..2) stops on the pile under column 1
        Assert.IsTrue(well.Pile.Occupies(new Position(3, 1)), "stopped by the pile");
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

    [TestMethod]
    public void Constructor_RejectsAWellTooSmallToAdmitAPiece()
    {
        Assert.ThrowsException<System.ArgumentOutOfRangeException>(
            () => new Well(3, 10, new ScriptedPieceSource(PieceType.O)));
        Assert.ThrowsException<System.ArgumentOutOfRangeException>(
            () => new Well(4, 1, new ScriptedPieceSource(PieceType.O)));
    }
}
