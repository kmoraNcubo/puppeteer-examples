using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tetris.Tests;

/// <summary>
/// Geometry and rotation of the seven tetrominoes. Each piece is spawned at the
/// origin so its world cells equal its anchor-local layout, and we assert both
/// the cell set of each pose and the length of the rotation cycle.
/// </summary>
[TestClass]
public sealed class PieceTests
{
    private static readonly Position Origin = new(0, 0);

    private static ImmutableHashSet<Position> CellsOf(params (int Row, int Column)[] cells) =>
        cells.Select(c => new Position(c.Row, c.Column)).ToImmutableHashSet();

    /// <summary>Rotates a piece <paramref name="times"/> times and returns the resulting cells.</summary>
    private static ImmutableHashSet<Position> PoseAfter(Piece piece, int times)
    {
        for (var i = 0; i < times; i++)
        {
            piece = piece.Rotate();
        }

        return piece.Cells;
    }

    [TestMethod]
    public void EveryPiece_HasExactlyFourCells()
    {
        foreach (PieceType type in System.Enum.GetValues<PieceType>())
        {
            var piece = Tetromino.Spawn(type, Origin);
            Assert.AreEqual(Piece.CellCount, piece.Cells.Count, $"{type} spawn pose");

            // …and in every pose reachable by rotation.
            var rotated = piece;
            for (var i = 0; i < 4; i++)
            {
                rotated = rotated.Rotate();
                Assert.AreEqual(Piece.CellCount, rotated.Cells.Count, $"{type} after {i + 1} rotations");
            }
        }
    }

    [TestMethod]
    public void OPiece_HasOneOrientation_RotationIsNoOp()
    {
        var o = Tetromino.Spawn(PieceType.O, Origin);
        var spawn = CellsOf((0, 0), (0, 1), (1, 0), (1, 1));

        Assert.IsTrue(spawn.SetEquals(o.Cells));
        Assert.IsTrue(spawn.SetEquals(PoseAfter(o, 1)), "one rotation");
        Assert.IsTrue(spawn.SetEquals(PoseAfter(o, 2)), "two rotations");
    }

    [TestMethod]
    public void IPiece_TogglesBetweenTwoOrientations()
    {
        var i = Tetromino.Spawn(PieceType.I, Origin);
        var horizontal = CellsOf((1, 0), (1, 1), (1, 2), (1, 3));
        var vertical = CellsOf((0, 2), (1, 2), (2, 2), (3, 2));

        Assert.IsTrue(horizontal.SetEquals(PoseAfter(i, 0)), "pose 0");
        Assert.IsTrue(vertical.SetEquals(PoseAfter(i, 1)), "pose 1");
        Assert.IsTrue(horizontal.SetEquals(PoseAfter(i, 2)), "cycles back at 2");
    }

    [TestMethod]
    public void SPiece_TogglesBetweenTwoOrientations()
    {
        var s = Tetromino.Spawn(PieceType.S, Origin);
        var pose0 = CellsOf((0, 1), (0, 2), (1, 0), (1, 1));
        var pose1 = CellsOf((0, 1), (1, 1), (1, 2), (2, 2));

        Assert.IsTrue(pose0.SetEquals(PoseAfter(s, 0)));
        Assert.IsTrue(pose1.SetEquals(PoseAfter(s, 1)));
        Assert.IsTrue(pose0.SetEquals(PoseAfter(s, 2)), "cycles back at 2");
    }

    [TestMethod]
    public void ZPiece_TogglesBetweenTwoOrientations()
    {
        var z = Tetromino.Spawn(PieceType.Z, Origin);
        var pose0 = CellsOf((0, 0), (0, 1), (1, 1), (1, 2));
        var pose1 = CellsOf((0, 2), (1, 1), (1, 2), (2, 1));

        Assert.IsTrue(pose0.SetEquals(PoseAfter(z, 0)));
        Assert.IsTrue(pose1.SetEquals(PoseAfter(z, 1)));
        Assert.IsTrue(pose0.SetEquals(PoseAfter(z, 2)), "cycles back at 2");
    }

    [TestMethod]
    public void TPiece_CyclesThroughFourOrientations()
    {
        var t = Tetromino.Spawn(PieceType.T, Origin);
        var poses = new[]
        {
            CellsOf((0, 1), (1, 0), (1, 1), (1, 2)),
            CellsOf((0, 1), (1, 1), (1, 2), (2, 1)),
            CellsOf((1, 0), (1, 1), (1, 2), (2, 1)),
            CellsOf((0, 1), (1, 0), (1, 1), (2, 1)),
        };

        for (var i = 0; i < 4; i++)
        {
            Assert.IsTrue(poses[i].SetEquals(PoseAfter(t, i)), $"pose {i}");
        }

        Assert.IsTrue(poses[0].SetEquals(PoseAfter(t, 4)), "cycles back at 4");
    }

    [TestMethod]
    public void JPiece_CyclesThroughFourOrientations()
    {
        var j = Tetromino.Spawn(PieceType.J, Origin);
        var poses = new[]
        {
            CellsOf((0, 0), (1, 0), (1, 1), (1, 2)),
            CellsOf((0, 1), (0, 2), (1, 1), (2, 1)),
            CellsOf((1, 0), (1, 1), (1, 2), (2, 2)),
            CellsOf((0, 1), (1, 1), (2, 0), (2, 1)),
        };

        for (var i = 0; i < 4; i++)
        {
            Assert.IsTrue(poses[i].SetEquals(PoseAfter(j, i)), $"pose {i}");
        }

        Assert.IsTrue(poses[0].SetEquals(PoseAfter(j, 4)), "cycles back at 4");
    }

    [TestMethod]
    public void LPiece_CyclesThroughFourOrientations()
    {
        var l = Tetromino.Spawn(PieceType.L, Origin);
        var poses = new[]
        {
            CellsOf((0, 2), (1, 0), (1, 1), (1, 2)),
            CellsOf((0, 1), (1, 1), (2, 1), (2, 2)),
            CellsOf((1, 0), (1, 1), (1, 2), (2, 0)),
            CellsOf((0, 0), (0, 1), (1, 1), (2, 1)),
        };

        for (var i = 0; i < 4; i++)
        {
            Assert.IsTrue(poses[i].SetEquals(PoseAfter(l, i)), $"pose {i}");
        }

        Assert.IsTrue(poses[0].SetEquals(PoseAfter(l, 4)), "cycles back at 4");
    }

    [TestMethod]
    public void Rotate_ReturnsANewImmutablePiece()
    {
        var t = Tetromino.Spawn(PieceType.T, new Position(5, 5));
        var rotated = t.Rotate();

        Assert.AreNotSame(t, rotated);
        Assert.AreEqual(0, t.Orientation.Index, "original is unchanged");
        Assert.AreEqual(1, rotated.Orientation.Index);
        Assert.AreEqual(t.Anchor, rotated.Anchor, "rotation keeps the anchor");
    }

    [TestMethod]
    public void Translate_MovesEveryCellByTheOffset()
    {
        var l = Tetromino.Spawn(PieceType.L, Origin);
        var moved = l.Translate(new Offset(3, 2));

        var expected = l.Cells.Select(c => c.Translate(new Offset(3, 2))).ToImmutableHashSet();
        Assert.IsTrue(expected.SetEquals(moved.Cells));
        Assert.AreNotSame(l, moved);
    }
}
