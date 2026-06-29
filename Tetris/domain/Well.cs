using System.Collections.Immutable;
using System.Linq;

namespace Tetris;

/// <summary>
/// The board — the aggregate root and the one mutable thing in the model. A
/// well composes three immutable figures: the boundary <see cref="Frame"/>,
/// the accumulated <see cref="Pile"/>, and the falling active <see cref="Piece"/>.
/// <para>
/// The well is deterministic: the same construction, fed the same piece
/// sequence and driven by the same sequence of verbs, always reaches the same
/// state. Every value object it holds is immutable, and a state transition
/// replaces a reference rather than mutating in place; the only choice the well
/// makes is which piece comes next, and that is delegated to an
/// <see cref="IPieceSource"/> so it can be made either random or exactly
/// reproducible from the outside.
/// </para>
/// <para>
/// The well owns the invariants: there is an active falling piece exactly when
/// the game is not over, the active piece always rests in free space, every
/// occupied cell lies inside the frame, and the pile never keeps a complete
/// row. It enforces them after every transition with
/// <see cref="AssertInvariants"/>.
/// </para>
/// </summary>
internal sealed class Well
{
    /// <summary>The boundary figure — walls and floor.</summary>
    internal Frame Frame { get; }

    /// <summary>The accumulated landed blocks.</summary>
    internal Pile Pile { get; private set; }

    /// <summary>The tetromino currently falling, or <c>null</c> once the game is over.</summary>
    internal Piece? Active { get; private set; }

    /// <summary>True once a freshly spawned piece had nowhere legal to appear.</summary>
    internal bool IsGameOver { get; private set; }

    /// <summary>How many rows have been cleared over the well's lifetime.</summary>
    internal int ClearedLines { get; private set; }

    private readonly IPieceSource _pieces;

    /// <summary>
    /// Opens a well of the given interior size whose pieces are drawn at random.
    /// This is the natural way to start an actual game.
    /// </summary>
    internal Well(int width, int height) : this(width, height, new RandomPieceSource())
    {
    }

    /// <summary>
    /// Opens a well of the given interior size, drawing its pieces from
    /// <paramref name="pieces"/>. Supplying a source makes the game
    /// reproducible. If the first piece cannot even appear, the well opens
    /// already game-over.
    /// </summary>
    internal Well(int width, int height, IPieceSource pieces)
    {
        Frame = new Frame(width, height);
        Pile = Pile.Empty(width);
        _pieces = pieces;

        Spawn();
        AssertInvariants();
    }

    /// <summary>The column anchor that centres a 4-wide bounding box.</summary>
    private Position SpawnAnchor => new(0, (Frame.Width - 4) / 2);

    /// <summary>
    /// The single legality rule. A candidate placement is legal iff its cells
    /// do not overlap the union of the boundary and the pile. Because the
    /// frame is itself a figure, wall-, floor- and pile-collision are this one
    /// <see cref="Shape.Intersects"/> test, not three special cases.
    /// </summary>
    private bool Collides(Piece candidate) =>
        candidate.Intersects(Frame) || candidate.Intersects(Pile);

    /// <summary>Slides the active piece one column left, unless that would collide.</summary>
    internal void MoveLeft() => TryShift(Offset.Left);

    /// <summary>Slides the active piece one column right, unless that would collide.</summary>
    internal void MoveRight() => TryShift(Offset.Right);

    /// <summary>
    /// Rotates the active piece one quarter-turn in the given direction, unless
    /// the rotated pose would collide with a wall or the pile, in which case the
    /// turn is rejected. No wall-kicks: a blocked rotation is simply rejected
    /// (see the README for this trade-off).
    /// </summary>
    internal void Rotate(RotationDirection direction)
    {
        if (Active is null)
        {
            return;
        }

        var candidate = Active.Rotate(direction);
        if (!Collides(candidate))
        {
            Active = candidate;
        }

        AssertInvariants();
    }

    /// <summary>Rotates the active piece a quarter-turn clockwise.</summary>
    internal void RotateClockwise() => Rotate(RotationDirection.Clockwise);

    /// <summary>Rotates the active piece a quarter-turn counter-clockwise.</summary>
    internal void RotateCounterClockwise() => Rotate(RotationDirection.CounterClockwise);

    /// <summary>
    /// Advances the world by one step. If the active piece can descend a row it
    /// does; otherwise it <em>lands</em> — its cells join the pile, complete
    /// rows clear bottom-up, and the next piece is drawn. A newly drawn piece
    /// that immediately collides ends the game.
    /// </summary>
    internal void Tick()
    {
        if (Active is null)
        {
            return;
        }

        var descended = Active.Translate(Offset.Down);
        if (!Collides(descended))
        {
            Active = descended;
            AssertInvariants();
            return;
        }

        Land();
    }

    /// <summary>
    /// Drops the active piece straight down until it rests, then lands it — the
    /// hard drop. Equivalent to ticking until the piece can fall no further.
    /// </summary>
    internal void Drop()
    {
        if (Active is null)
        {
            return;
        }

        var resting = Active;
        while (!Collides(resting.Translate(Offset.Down)))
        {
            resting = resting.Translate(Offset.Down);
        }

        Active = resting;
        Land();
    }

    private void TryShift(Offset offset)
    {
        if (Active is null)
        {
            return;
        }

        var candidate = Active.Translate(offset);
        if (!Collides(candidate))
        {
            Active = candidate;
        }

        AssertInvariants();
    }

    private void Land()
    {
        // The active piece is, by the caller's guarantee, resting in free space.
        Pile = Pile.Integrate(Active!);

        var cleared = Pile.CompleteRows().Count;
        if (cleared > 0)
        {
            Pile = Pile.ClearCompleteRows();
            ClearedLines += cleared;
        }

        Spawn();
        AssertInvariants();
    }

    private void Spawn()
    {
        var next = Tetromino.Spawn(_pieces.Next(), SpawnAnchor);
        if (Collides(next))
        {
            // The new piece has nowhere legal to appear: the pile has reached
            // the ceiling. The game is over and there is no active piece.
            Active = null;
            IsGameOver = true;
            return;
        }

        Active = next;
    }

    /// <summary>
    /// Re-checks the well's invariants after every transition. These never fire
    /// in correct play; they are the executable statement of what "a valid
    /// well" means, and the safety net that turns any modelling bug into a loud
    /// failure rather than silent corruption.
    /// </summary>
    private void AssertInvariants()
    {
        // There is an active falling piece exactly when the game is not over:
        // a new piece is created whenever play continues.
        if ((Active is null) != IsGameOver)
        {
            throw new WellInvariantException(
                "A well has an active piece if and only if the game is not over.");
        }

        // The pile never retains a complete row.
        if (!Pile.CompleteRows().IsEmpty)
        {
            throw new WellInvariantException("The pile retained a complete row.");
        }

        // Every landed cell lies inside the frame (interior columns, above floor).
        foreach (var cell in Pile.Cells)
        {
            if (!Frame.Contains(cell))
            {
                throw new WellInvariantException($"Pile cell {cell} lies outside the frame.");
            }
        }

        if (Active is null)
        {
            return;
        }

        // The active piece rests in free space — it touches neither the
        // boundary nor the pile.
        if (Collides(Active))
        {
            throw new WellInvariantException($"The active piece {Active} intersects an occupied figure.");
        }

        // Every active cell lies within the interior column range. (Active
        // cells may sit above row 0, in the open sky, before they fall in.)
        foreach (var cell in Active.Cells)
        {
            if (cell.Column < 0 || cell.Column >= Frame.Width || cell.Row >= Frame.Height)
            {
                throw new WellInvariantException($"Active cell {cell} lies outside the frame.");
            }
        }
    }

    /// <summary>
    /// A read-only snapshot of every occupied interior cell — the union of the
    /// pile and the active piece, clipped to the interior. Handy for rendering
    /// and for asserting state without exposing mutable internals.
    /// </summary>
    internal ImmutableHashSet<Position> OccupiedInterior()
    {
        var occupied = Pile.Cells;
        if (Active is not null)
        {
            occupied = occupied.Union(Active.Cells);
        }

        return occupied.Where(Frame.Contains).ToImmutableHashSet();
    }
}
