using System.Text;
using Tetris;

// A pure-domain demo. It opens an empty Well, supplies a fixed sequence of
// pieces (the host chooses which piece comes — the domain is told via Spawn),
// drives a fixed list of moves, and renders after each step. Deterministic: the
// same construction and the same command stream always reach the same state.

const int width = 10;
const int height = 16;

var well = new Well(width, height);

// Each step labels a verb. The host spawns each piece, manoeuvres it, and drops
// it before spawning the next.
var script = new (string Label, Action<Well> Apply)[]
{
    ("spawn I",       w => w.Spawn(PieceType.I)),
    ("rotate I",      w => w.Rotate()),
    ("slam I left",   w => { w.MoveLeft(); w.MoveLeft(); w.MoveLeft(); w.MoveLeft(); w.MoveLeft(); }),
    ("drop I",        w => w.Drop()),
    ("spawn O",       w => w.Spawn(PieceType.O)),
    ("slam O left",   w => { w.MoveLeft(); w.MoveLeft(); w.MoveLeft(); w.MoveLeft(); w.MoveLeft(); }),
    ("drop O",        w => w.Drop()),
    ("spawn T",       w => w.Spawn(PieceType.T)),
    ("nudge T right", w => w.MoveRight()),
    ("drop T",        w => w.Drop()),
    ("spawn L",       w => w.Spawn(PieceType.L)),
    ("drop L",        w => w.Drop()),
    ("spawn J",       w => w.Spawn(PieceType.J)),
    ("slam J right",  w => { w.MoveRight(); w.MoveRight(); w.MoveRight(); w.MoveRight(); }),
    ("drop J",        w => w.Drop()),
};

Render(well, "open");
foreach (var (label, apply) in script)
{
    apply(well);
    Render(well, label);
    if (well.IsGameOver)
    {
        Console.WriteLine("== game over ==");
        break;
    }
}

Console.WriteLine($"Lines cleared over the run: {well.ClearedLines}");

// --- rendering -------------------------------------------------------------

static void Render(Well well, string label)
{
    var frame = well.Frame;
    var occupied = well.OccupiedInterior();
    var active = well.Active?.Cells ?? System.Collections.Immutable.ImmutableHashSet<Position>.Empty;

    var sb = new StringBuilder();
    sb.Append($"--- {label}  (cleared: {well.ClearedLines})");
    if (well.IsGameOver)
    {
        sb.Append("  [GAME OVER]");
    }

    sb.AppendLine(" ---");

    for (var row = 0; row < frame.Height; row++)
    {
        sb.Append('|'); // left wall
        for (var column = 0; column < frame.Width; column++)
        {
            var cell = new Position(row, column);
            char glyph;
            if (active.Contains(cell))
            {
                glyph = '@'; // the falling piece
            }
            else if (occupied.Contains(cell))
            {
                glyph = '#'; // a landed block
            }
            else
            {
                glyph = '.'; // empty interior
            }

            sb.Append(glyph);
        }

        sb.AppendLine("|"); // right wall
    }

    sb.Append('+');
    sb.Append(new string('=', frame.Width)); // floor
    sb.AppendLine("+");
    Console.WriteLine(sb.ToString());
}
