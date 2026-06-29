using System.Collections.Generic;

namespace Tetris;

/// <summary>
/// The source of the next tetromino <em>type</em> to enter the well. Piece
/// selection is an <em>external</em>, deterministic input — never an internal
/// <c>Random</c>. This is deliberate: the well will later become a Puppeteer
/// actor whose journal is replayed, and any hidden randomness would make two
/// replays of the same journal diverge. By demanding the type from outside,
/// the well stays a pure function of (initial state + the sequence of inputs),
/// which is exactly the determinism a replayable actor needs.
/// <para>
/// In a real game the "outside" might be a seeded bag-randomiser living in the
/// host; in the tests it is a fixed list. The domain neither knows nor cares —
/// it only knows the next type is given to it.
/// </para>
/// </summary>
public interface IPieceSource
{
    /// <summary>The next tetromino type to spawn.</summary>
    PieceType Next();
}

/// <summary>
/// A piece source that hands out a fixed, finite sequence of types in order —
/// the deterministic input used by tests and scripted demos. When the script
/// is exhausted it throws, making "the game ran past its script" a loud error
/// rather than silent nondeterminism.
/// </summary>
public sealed class ScriptedPieceSource : IPieceSource
{
    private readonly Queue<PieceType> _script;

    public ScriptedPieceSource(params PieceType[] sequence)
    {
        _script = new Queue<PieceType>(sequence);
    }

    /// <inheritdoc />
    public PieceType Next()
    {
        if (_script.Count == 0)
        {
            throw new System.InvalidOperationException(
                "The scripted piece sequence is exhausted; supply more pieces.");
        }

        return _script.Dequeue();
    }
}
