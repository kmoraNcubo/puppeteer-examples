using System.Text;
using Tetris;

// An interactive, keyboard-driven Tetris you play in the terminal. It references
// only the domain — no Puppeteer — and drives the clean Well directly. This is
// the "console monolith": the host supplies everything the domain externalizes —
// the keyboard (inbound commands), the clock (gravity), the randomness (which
// piece to Spawn), and the rendering. The domain stays a pure, deterministic
// function of the operation stream; the messy real-world concerns live out here.

const int width = 10;
const int height = 20;
var gravityInterval = TimeSpan.FromMilliseconds(500);

// --auto self-plays random moves for a few seconds, non-interactively — a
// headless rendering smoke-test. Without it, the game is keyboard-driven.
var auto = args.Contains("--auto");

// When stdout is redirected (e.g. a headless --auto smoke-test) the cursor APIs
// are unavailable, so we fall back to plain appended frames instead of in-place
// redraw.
var interactiveConsole = !Console.IsOutputRedirected;

var well = new Well(width, height);

// The host owns randomness. The domain never picks a piece; the host hands one
// in via Spawn(type). This System.Random is exactly the seam a Puppeteer
// reaction will later fill — the framework would record (Eval) the draw so a
// replayed journal reproduces the same game.
var random = new Random(auto ? 12345 : Environment.TickCount);
var pieceTypes = Enum.GetValues<PieceType>();
PieceType NextPiece() => pieceTypes[random.Next(pieceTypes.Length)];

// Query-first contract, by construction: the host inspects the well's queries
// (IsGameOver / IsAwaitingPiece / Active) before issuing any operation, so it
// never relies on catching TetrisRuleException.
void SpawnIfAwaiting()
{
    if (well.IsAwaitingPiece)
    {
        well.Spawn(NextPiece());
    }
}

if (interactiveConsole)
{
    Console.CursorVisible = false;
}

try
{
    if (interactiveConsole)
    {
        Console.Clear();
    }

    SpawnIfAwaiting(); // the host supplies the first piece
    Render(well);

    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8); // only used by --auto
    var nextGravity = DateTime.UtcNow + gravityInterval;

    while (!well.IsGameOver)
    {
        var changed = false;

        if (auto)
        {
            if (DateTime.UtcNow >= deadline)
            {
                break;
            }

            changed |= ApplyAutoMove();
            Thread.Sleep(40);
        }
        else if (Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true).Key;
            if (key is ConsoleKey.Q or ConsoleKey.Escape)
            {
                break;
            }

            changed |= ApplyKey(key);
        }

        // The clock: on each gravity tick the host asks the domain to advance.
        if (DateTime.UtcNow >= nextGravity)
        {
            if (well.Active is not null)
            {
                well.Tick();
                changed = true;
            }

            nextGravity = DateTime.UtcNow + gravityInterval;
        }

        // A landing leaves the well between pieces; the host feeds the next one.
        if (well.IsAwaitingPiece)
        {
            SpawnIfAwaiting();
            changed = true;
        }

        if (changed)
        {
            Render(well);
        }

        if (!auto)
        {
            Thread.Sleep(15); // keep the poll loop from spinning hot
        }
    }

    Render(well); // final frame (GAME OVER banner if the pile reached the top)
}
finally
{
    if (interactiveConsole)
    {
        Console.CursorVisible = true;
        Console.SetCursorPosition(0, height + 4);
    }
}

// --- input -----------------------------------------------------------------

// Every verb is guarded by a query first — the host only moves when a piece is
// active, so TetrisRuleException is never used for control flow.
bool ApplyKey(ConsoleKey key)
{
    if (well.Active is null)
    {
        return false;
    }

    switch (key)
    {
        case ConsoleKey.LeftArrow: well.MoveLeft(); return true;
        case ConsoleKey.RightArrow: well.MoveRight(); return true;
        case ConsoleKey.UpArrow: well.Rotate(); return true;
        case ConsoleKey.DownArrow: well.Tick(); return true;   // soft drop
        case ConsoleKey.Spacebar: well.Drop(); return true;    // hard drop
        default: return false;
    }
}

bool ApplyAutoMove()
{
    if (well.Active is null)
    {
        return false;
    }

    switch (random.Next(5))
    {
        case 0: well.MoveLeft(); return true;
        case 1: well.MoveRight(); return true;
        case 2: well.Rotate(); return true;
        case 3: well.Tick(); return true;
        default: well.Drop(); return true;
    }
}

// --- rendering -------------------------------------------------------------

void Render(Well well)
{
    var occupied = well.OccupiedInterior();

    var sb = new StringBuilder();
    sb.AppendLine("TETRIS — ←/→ move   ↑ rotate   ↓ soft drop   Space hard drop   Q/Esc quit");
    sb.AppendLine($"Lines cleared: {well.ClearedLines}");
    sb.AppendLine();

    for (var row = 0; row < well.Frame.Height; row++)
    {
        sb.Append('|'); // left wall
        for (var column = 0; column < well.Frame.Width; column++)
        {
            sb.Append(occupied.Contains(new Position(row, column)) ? "[]" : "  ");
        }

        sb.AppendLine("|"); // right wall
    }

    sb.Append('+').Append(new string('=', well.Frame.Width * 2)).AppendLine("+"); // floor

    if (well.IsGameOver)
    {
        sb.AppendLine();
        sb.AppendLine("            G A M E   O V E R");
    }

    if (interactiveConsole)
    {
        // Cursor-home redraw rather than Console.Clear, to avoid flicker.
        Console.SetCursorPosition(0, 0);
        Console.Write(sb.ToString());
    }
    else
    {
        // Headless (redirected) fallback: append the frame.
        Console.WriteLine(sb.ToString());
    }
}
