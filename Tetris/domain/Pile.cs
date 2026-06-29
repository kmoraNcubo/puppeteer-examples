using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Tetris;

/// <summary>
/// The accumulated landed blocks — the floor that does not move but mutates.
/// A <see cref="Shape"/> like everything else, so the collision rule treats it
/// no differently from a wall. A pile is immutable: landing a piece
/// (<see cref="Integrate"/>) and clearing rows (<see cref="ClearCompleteRows"/>)
/// each return a <em>new</em> pile.
/// <para>
/// The pile carries the well's <see cref="Width"/> because "complete" is
/// width-relative: a row is complete when every interior column in it is
/// occupied. The class enforces one invariant — <b>a pile never retains a
/// complete row</b> — by construction: any pile that still contained a full
/// row would not be a valid resting state, so <see cref="ClearCompleteRows"/>
/// is the canonical way to settle a pile after a landing.
/// </para>
/// </summary>
public sealed class Pile : Shape
{
    /// <summary>The well's interior width; sets what "a complete row" means.</summary>
    public int Width { get; }

    private readonly ImmutableHashSet<Position> _cells;

    private Pile(int width, ImmutableHashSet<Position> cells)
    {
        Width = width;
        _cells = cells;
    }

    /// <summary>An empty pile for a well of the given interior width.</summary>
    public static Pile Empty(int width)
    {
        if (width <= 0)
        {
            throw new System.ArgumentOutOfRangeException(nameof(width));
        }

        return new Pile(width, ImmutableHashSet<Position>.Empty);
    }

    /// <inheritdoc />
    public override ImmutableHashSet<Position> Cells => _cells;

    /// <summary>
    /// Merges a landed figure's cells into the pile, returning the new pile.
    /// In play the figure is always the active <see cref="Piece"/> that just
    /// came to rest; the parameter is the more general <see cref="Shape"/>
    /// because the pile cares only about cells, not about which kind of figure
    /// they came from — the same indifference that unifies the collision rule.
    /// The caller (the well) is responsible for having checked the figure
    /// actually rests here; <see cref="Integrate"/> is the pure merge.
    /// </summary>
    public Pile Integrate(Shape figure) =>
        new(Width, _cells.Union(figure.Cells));

    /// <summary>The row indices that are completely filled across the width.</summary>
    public ImmutableSortedSet<int> CompleteRows()
    {
        var occupiedByRow = _cells
            .GroupBy(cell => cell.Row)
            .Where(group => group.Select(cell => cell.Column).Distinct().Count() == Width)
            .Select(group => group.Key);

        return ImmutableSortedSet.CreateRange(occupiedByRow);
    }

    /// <summary>
    /// Removes every complete row and lets the blocks above each cleared row
    /// fall by the number of cleared rows beneath them — the bottom-up
    /// settling that makes a tower above a vanished line end one row lower.
    /// Returns a new pile that, by construction, retains no complete row.
    /// </summary>
    public Pile ClearCompleteRows()
    {
        var completed = CompleteRows();
        if (completed.IsEmpty)
        {
            return this;
        }

        var survivors = ImmutableHashSet.CreateBuilder<Position>();
        foreach (var cell in _cells)
        {
            if (completed.Contains(cell.Row))
            {
                continue; // this cell was on a cleared line; it vanishes
            }

            // A surviving cell drops by one row for every cleared line strictly
            // below it. Processing the whole pile this way is the bottom-up
            // collapse: lines lower in the well pull everything above them down.
            var clearedBelow = completed.Count(clearedRow => clearedRow > cell.Row);
            survivors.Add(cell.Translate(new Offset(clearedBelow, 0)));
        }

        return new Pile(Width, survivors.ToImmutable());
    }
}
