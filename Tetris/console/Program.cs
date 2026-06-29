using System.Text;
using Tetris;

// A pure-domain demo. It opens a Well, feeds it a fixed piece sequence and a
// fixed list of moves, and renders the well to the console after each step.
// Because the domain is deterministic, this program prints the same thing on
// every run: the same construction, the same piece sequence and the same verbs
// always reach the same state.

const int width = 10;
const int height = 16;

// The pieces that will enter, in order — an external, deterministic input.
var pieces = new ScriptedPieceSource(
    PieceType.I, PieceType.O, PieceType.T, PieceType.L,
    PieceType.J, PieceType.S, PieceType.Z, PieceType.I,
    PieceType.O, PieceType.T, PieceType.L, PieceType.J);

var well = new Well(width, height, pieces);

// A scripted choreography of verbs. Each entry is one labelled step.
var script = new (string Label, Action<Well> Apply)[]
{
    ("spawn I",            _ => { }),
    ("rotate I",           w => w.RotateClockwise()),
    ("slam I left",        w => { w.MoveLeft(); w.MoveLeft(); w.MoveLeft(); w.MoveLeft(); w.MoveLeft(); }),
    ("drop I",             w => w.Drop()),
    ("slam O left",        w => { w.MoveLeft(); w.MoveLeft(); w.MoveLeft(); w.MoveLeft(); w.MoveLeft(); }),
    ("drop O",             w => w.Drop()),
    ("nudge T right",      w => w.MoveRight()),
    ("drop T",             w => w.Drop()),
    ("drop L",             w => w.Drop()),
    ("slam J right",       w => { w.MoveRight(); w.MoveRight(); w.MoveRight(); w.MoveRight(); }),
    ("drop J",             w => w.Drop()),
    ("drop S",             w => w.Drop()),
    ("drop Z",             w => w.Drop()),
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
