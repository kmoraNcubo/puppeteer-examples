using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tetris.Tests;

/// <summary>
/// The property the whole model is shaped around: a well is a pure function of
/// its initial size, its piece sequence, and the sequence of verbs applied to
/// it. Replaying the same inputs yields a byte-for-byte identical state. This
/// is what will let the well become a Puppeteer actor whose journal can be
/// replayed without divergence — there is no hidden randomness anywhere.
/// </summary>
[TestClass]
public sealed class DeterminismTests
{
    /// <summary>The fixed move script applied to each replay.</summary>
    private enum Move { Left, Right, Rotate, Tick, Drop }

    private static readonly PieceType[] Sequence =
    [
        PieceType.T, PieceType.I, PieceType.O, PieceType.S,
        PieceType.Z, PieceType.J, PieceType.L, PieceType.T,
        PieceType.O, PieceType.I,
    ];

    private static readonly Move[] Script =
    [
        Move.Left, Move.Rotate, Move.Tick, Move.Right, Move.Drop,
        Move.Rotate, Move.Rotate, Move.Left, Move.Drop, Move.Tick,
        Move.Right, Move.Drop, Move.Left, Move.Left, Move.Drop,
        Move.Rotate, Move.Drop, Move.Tick, Move.Tick, Move.Drop,
    ];

    private static Well Replay()
    {
        var well = new Well(8, 16, new ScriptedPieceSource(Sequence));
        foreach (var move in Script)
        {
            switch (move)
            {
                case Move.Left: well.MoveLeft(); break;
                case Move.Right: well.MoveRight(); break;
                case Move.Rotate: well.Rotate(); break;
                case Move.Tick: well.Tick(); break;
                case Move.Drop: well.Drop(); break;
            }
        }

        return well;
    }

    /// <summary>A stable, order-independent fingerprint of the well's full state.</summary>
    private static string Fingerprint(Well well)
    {
        var cells = string.Join(
            "|",
            well.OccupiedInterior()
                .OrderBy(c => c.Row)
                .ThenBy(c => c.Column)
                .Select(c => $"{c.Row},{c.Column}"));

        var active = well.Active is null
            ? "none"
            : $"{well.Active.Type}:{well.Active.Orientation.Index}:{well.Active.Anchor.Row},{well.Active.Anchor.Column}";

        return $"over={well.IsGameOver};cleared={well.ClearedLines};active={active};cells={cells}";
    }

    [TestMethod]
    public void SameInputsAndPieceSequence_YieldIdenticalState()
    {
        var first = Fingerprint(Replay());
        var second = Fingerprint(Replay());

        Assert.AreEqual(first, second, "two replays of the same inputs must match exactly");
    }

    [TestMethod]
    public void TenReplays_AllAgree()
    {
        var fingerprints = Enumerable.Range(0, 10).Select(_ => Fingerprint(Replay())).Distinct().ToList();
        Assert.AreEqual(1, fingerprints.Count, "every replay collapses to a single state");
    }
}
