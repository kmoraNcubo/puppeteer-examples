using System.Collections.Generic;

namespace Tetris;

/// <summary>
/// The source of the next tetromino <em>type</em> to enter the well. Choosing
/// the next piece is part of how a game unfolds; isolating it behind this seam
/// lets a real game decide at random while a test pins an exact sequence, with
/// the well's behaviour identical either way.
/// </summary>
internal interface IPieceSource
{
    /// <summary>The next tetromino type to spawn.</summary>
    PieceType Next();
}

/// <summary>
/// A piece source that picks each next tetromino uniformly at random. This is
/// the natural source for an actual game — the well draws an unpredictable
/// piece each time one is needed.
/// </summary>
internal sealed class RandomPieceSource : IPieceSource
{
    private static readonly PieceType[] All =
        System.Enum.GetValues<PieceType>();

    private readonly System.Random _random;

    /// <summary>A source seeded by the system clock.</summary>
    public RandomPieceSource() : this(new System.Random())
    {
    }

    /// <summary>A source backed by a caller-supplied <see cref="System.Random"/>.</summary>
    public RandomPieceSource(System.Random random)
    {
        _random = random;
    }

    /// <inheritdoc />
    public PieceType Next() => All[_random.Next(All.Length)];
}

/// <summary>
/// A piece source that hands out a fixed, finite sequence of types in order.
/// Supplying the sequence makes a game exactly reproducible: the same sequence
/// fed to a fresh well always produces the same play. When the sequence is
/// exhausted it throws, so running past the script is a loud error rather than
/// a silent surprise.
/// </summary>
internal sealed class ScriptedPieceSource : IPieceSource
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
