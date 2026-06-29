using System.Collections.Immutable;
using System.Linq;

namespace Tetris;

/// <summary>
/// The board — the aggregate root and the one mutable thing in the model. A
/// well composes three figures: the boundary <see cref="Frame"/>, the
/// accumulated <see cref="Pile"/>, and the falling active <see cref="Piece"/>.
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
/// Contract for the move verbs: operating on a finished game is invalid and
/// throws <see cref="GameOverException"/> — a caller is expected to check
/// <see cref="IsGameOver"/> first. A move that is merely <em>blocked</em> (it
/// would collide with a wall or the pile) is a valid no-op: the piece stays put
/// and nothing is thrown.
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
    /// reproducible.
    /// </summary>
    internal Well(int width, int height, IPieceSource pieces)
    {
        if (width < 4)
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(width), width, "A well must be at least 4 columns wide to admit a piece.");
        }

        if (height < 2)
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(height), height, "A well must be at least 2 rows tall to admit a piece.");
        }

        Frame = new Frame(width, height);
        Pile = Pile.Empty(width);
        _pieces = pieces;

        if (!IsGameOver)
        {
            Spawn();
        }

        AssertInvariants();
    }

    /// <summary>
    /// Whether the game has ended — <em>derived</em>, not stored. The game is
    /// over exactly when the pile has risen into the <see cref="SpawnRegion"/>,
    /// so a freshly drawn piece would have nowhere clear to appear. The small
    /// spawn region iterates and probes the pile's O(1) membership.
    /// </summary>
    internal bool IsGameOver => SpawnRegion.Intersects(Pile);

    /// <summary>The top-left corner of the 4-wide spawn bounding box.</summary>
    private Position SpawnAnchor => new(0, (Frame.Width - 4) / 2);

    /// <summary>
    /// The cells a freshly spawned piece is born into: the top two rows across
    /// the four spawn columns. Every tetromino's spawn pose lies within this
    /// box, so if the pile reaches any of these cells the next piece cannot
    /// appear — that is game over.
    /// </summary>
    private Shape SpawnRegion
    {
        get
        {
            var anchorColumn = SpawnAnchor.Column;
            var cells =
                from row in Enumerable.Range(0, 2)
                from column in Enumerable.Range(anchorColumn, 4)
                select new Position(row, column);
            return new CellSet(cells.ToImmutableHashSet());
        }
    }

    /// <summary>
    /// The single legality rule. A candidate placement is legal iff its cells
    /// overlap neither the boundary nor the pile. Because the frame is itself a
    /// figure, wall-, floor- and pile-collision are this one
    /// <see cref="Shape.Intersects"/> test, not three special cases. The
    /// four-cell piece is the small figure that iterates; the frame and pile are
    /// only probed.
    /// </summary>
    private bool Collides(Piece candidate) =>
        candidate.Intersects(Frame) || candidate.Intersects(Pile);

    /// <summary>Slides the active piece one column left; a blocked slide is a no-op.</summary>
    internal void MoveLeft() => Shift(Offset.Left);

    /// <summary>Slides the active piece one column right; a blocked slide is a no-op.</summary>
    internal void MoveRight() => Shift(Offset.Right);

    /// <summary>
    /// Rotates the active piece one quarter-turn in the single rotation sense. A
    /// rotation that would collide with a wall or the pile is rejected as a
    /// no-op (no wall-kicks; see the README). Throws if the game is over.
    /// </summary>
    internal void Rotate()
    {
        RequireInPlay();

        var candidate = Active!.Rotate();
        if (!Collides(candidate))
        {
            Active = candidate;
        }

        AssertInvariants();
    }

    /// <summary>
    /// Advances the world by one step. If the active piece can descend a row it
    /// does; otherwise it <em>lands</em>. Throws if the game is over.
    /// </summary>
    internal void Tick()
    {
        RequireInPlay();

        var descended = Active!.Translate(Offset.Down);
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
    /// hard drop. Throws if the game is over.
    /// </summary>
    internal void Drop()
    {
        RequireInPlay();

        var resting = Active!;
        while (!Collides(resting.Translate(Offset.Down)))
        {
            resting = resting.Translate(Offset.Down);
        }

        Active = resting;
        Land();
    }

    private void Shift(Offset offset)
    {
        RequireInPlay();

        var candidate = Active!.Translate(offset);
        if (!Collides(candidate))
        {
            Active = candidate;
        }

        AssertInvariants();
    }

    /// <summary>Guards the move verbs: a finished game cannot be operated on.</summary>
    private void RequireInPlay()
    {
        if (IsGameOver)
        {
            throw new GameOverException(
                "The game is over; check IsGameOver before operating on the well.");
        }
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

        // The piece has settled. Validate before bringing in the next one: if
        // the pile now reaches the spawn region the game is over and we simply
        // do not spawn — Active stays null and IsGameOver (derived) stays true.
        Active = null;
        if (!IsGameOver)
        {
            Spawn();
        }

        AssertInvariants();
    }

    /// <summary>
    /// Places the next piece at the spawn anchor. Precondition: the game is not
    /// over (the spawn region is clear). Calling this on a finished game is an
    /// internal bug, distinct from the caller-facing <see cref="GameOverException"/>.
    /// </summary>
    private void Spawn()
    {
        if (IsGameOver)
        {
            throw new WellInvariantException("Spawn was called on a finished game.");
        }

        Active = Tetromino.Spawn(_pieces.Next(), SpawnAnchor);
    }

    /// <summary>
    /// Re-checks the well's invariants after every transition. These never fire
    /// in correct play; they are the executable statement of what "a valid
    /// well" means, and the safety net that turns any modelling bug into a loud
    /// failure rather than silent corruption.
    /// </summary>
    private void AssertInvariants()
    {
        // There is an active falling piece exactly when the game is not over.
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

    /// <summary>
    /// A bare set of cells as a <see cref="Shape"/> — used for the spawn region,
    /// which is just a handful of cells with no behaviour of its own.
    /// </summary>
    private sealed class CellSet : Shape
    {
        public CellSet(ImmutableHashSet<Position> cells) => Cells = cells;

        public override ImmutableHashSet<Position> Cells { get; }
    }
}
