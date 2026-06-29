using System.Collections.Immutable;
using System.Linq;

namespace Tetris;

/// <summary>
/// The board — the aggregate root and the one mutable thing in the model. A
/// well composes three immutable figures: the boundary <see cref="Frame"/>,
/// the accumulated <see cref="Pile"/>, and the falling active <see cref="Piece"/>.
/// Every value object it holds is immutable; a state transition replaces a
/// reference rather than mutating in place. That discipline is what makes the
/// well's verbs deterministic — the property a Puppeteer actor needs, since
/// its journal of verb invocations will be replayed.
/// <para>
/// The well owns the invariants: the active piece always rests in free space,
/// every occupied cell lies inside the frame, and the pile never keeps a
/// complete row. It enforces them after every transition with
/// <see cref="AssertInvariants"/>.
/// </para>
/// <para>
/// This is the type that will later be wrapped as the framework's actor; for
/// now it is pure domain with no infrastructure. The verb-bearing class is
/// <c>internal</c> per the repository convention — discovered by reflection,
/// never referenced directly from outside the assembly.
/// </para>
/// </summary>
internal sealed class Well
{
    /// <summary>The boundary figure — walls and floor.</summary>
    public Frame Frame { get; }

    /// <summary>The accumulated landed blocks.</summary>
    public Pile Pile { get; private set; }

    /// <summary>The tetromino currently falling, or <c>null</c> once the game is over.</summary>
    public Piece? Active { get; private set; }

    /// <summary>True once a freshly spawned piece had nowhere legal to appear.</summary>
    public bool IsGameOver { get; private set; }

    /// <summary>How many rows have been cleared over the well's lifetime.</summary>
    public int ClearedLines { get; private set; }

    private readonly IPieceSource _pieces;

    /// <summary>
    /// Opens a well of the given interior size, drawing its first active piece
    /// from <paramref name="pieces"/>. If that first piece cannot even appear,
    /// the well opens already game-over.
    /// </summary>
    public Well(int width, int height, IPieceSource pieces)
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
    public void MoveLeft() => TryShift(Offset.Left);

    /// <summary>Slides the active piece one column right, unless that would collide.</summary>
    public void MoveRight() => TryShift(Offset.Right);

    /// <summary>
    /// Rotates the active piece one quarter-turn clockwise, unless the rotated
    /// pose would collide with a wall or the pile. No wall-kicks: a blocked
    /// rotation is simply rejected (see the README for this trade-off).
    /// </summary>
    public void Rotate()
    {
        if (Active is null)
        {
            return;
        }

        var candidate = Active.Rotate();
        if (!Collides(candidate))
        {
            Active = candidate;
        }

        AssertInvariants();
    }

    /// <summary>
    /// Advances the world by one step. If the active piece can descend a row it
    /// does; otherwise it <em>lands</em> — its cells join the pile, complete
    /// rows clear bottom-up, and the next piece is drawn. A newly drawn piece
    /// that immediately collides ends the game.
    /// </summary>
    public void Tick()
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
    public void Drop()
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
    /// and for asserting state in tests without exposing mutable internals.
    /// </summary>
    public ImmutableHashSet<Position> OccupiedInterior()
    {
        var occupied = Pile.Cells;
        if (Active is not null)
        {
            occupied = occupied.Union(Active.Cells);
        }

        return occupied.Where(Frame.Contains).ToImmutableHashSet();
    }
}
