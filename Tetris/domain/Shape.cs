using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Tetris;

/// <summary>
/// A set of occupied cells in the grid — the single abstraction the whole
/// model is built from. A falling <see cref="Piece"/>, the accumulated
/// <see cref="Pile"/>, and the boundary <see cref="Frame"/> are all the same
/// kind of thing: <em>figures within figures</em>. Because they share this
/// spine, "is this placement legal?" is one question asked the same way of
/// every figure — see <see cref="Intersects"/>.
/// <para>
/// A shape is immutable: its <see cref="Cells"/> never change, and
/// <see cref="Translate"/> returns a new shape. The grid coordinates are
/// abstract — a shape knows nothing of walls, scores, timers, or rendering.
/// </para>
/// </summary>
public abstract class Shape
{
    /// <summary>The cells this shape occupies. Never empty for a piece; may be
    /// empty for an early <see cref="Pile"/>.</summary>
    public abstract ImmutableHashSet<Position> Cells { get; }

    /// <summary>True when <paramref name="position"/> is one of this shape's cells.</summary>
    public bool Occupies(Position position) => Cells.Contains(position);

    /// <summary>
    /// True when this shape and <paramref name="other"/> share at least one
    /// cell. This is <em>the</em> collision primitive: wall-, floor- and
    /// pile-collision are not three rules but three applications of this one.
    /// </summary>
    public bool Intersects(Shape other) => Cells.Overlaps(other.Cells);

    /// <summary>The set of cells obtained by translating every cell by the offset.</summary>
    protected ImmutableHashSet<Position> TranslatedCells(Offset offset) =>
        Cells.Select(cell => cell.Translate(offset)).ToImmutableHashSet();
}
