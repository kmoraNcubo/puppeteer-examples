using System.Collections.Immutable;

namespace Tetris;

/// <summary>
/// The boundary of the well — its two walls and its floor — modelled as a
/// <see cref="Shape"/> of permanently occupied <em>sentinel</em> cells. This
/// is the answer to "is the frontier just another figure?": <b>yes</b>. The
/// frame is a figure of occupied cells exactly like a piece or the pile, which
/// is what collapses three apparent collision rules into one. A piece bumping
/// the left wall, hitting the floor, or landing on the pile are all the same
/// event — the candidate's cells overlap some occupied figure.
/// <para>
/// The well is read like text: rows grow downward, columns rightward. The
/// interior playing field is rows <c>[0, Height)</c> × columns
/// <c>[0, Width)</c>. The left wall sits at column <c>-1</c>, the right wall at
/// column <c>Width</c>, and the floor at row <c>Height</c>. The ceiling is left
/// open: pieces spawn in the open sky above row 0 and fall in, so there is no
/// top edge to the frame.
/// </para>
/// </summary>
public sealed class Frame : Shape
{
    /// <summary>Number of interior columns (the playable width).</summary>
    public int Width { get; }

    /// <summary>Number of interior rows (the playable height).</summary>
    public int Height { get; }

    private readonly ImmutableHashSet<Position> _cells;

    public Frame(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(width), "A well needs a positive interior.");
        }

        Width = width;
        Height = height;

        var builder = ImmutableHashSet.CreateBuilder<Position>();

        // The two walls run the full height (and one row into the floor line,
        // closing the corners).
        for (var row = 0; row <= height; row++)
        {
            builder.Add(new Position(row, -1));      // left wall
            builder.Add(new Position(row, width));   // right wall
        }

        // The floor spans the full width beneath the interior.
        for (var column = 0; column < width; column++)
        {
            builder.Add(new Position(height, column));
        }

        _cells = builder.ToImmutable();
    }

    /// <inheritdoc />
    public override ImmutableHashSet<Position> Cells => _cells;

    /// <summary>
    /// True when <paramref name="position"/> lies in the interior column range
    /// and at or above the floor — i.e. it is a cell a landed block may legally
    /// occupy. Rows above 0 (the open sky a piece spawns in) are interior too;
    /// only the walls and floor are off-limits.
    /// </summary>
    public bool Contains(Position position) =>
        position.Column >= 0
        && position.Column < Width
        && position.Row < Height;
}
