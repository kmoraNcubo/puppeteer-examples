using Tetris.Acting;

// Tetris WATCH — a live, foreground, READ-ONLY viewer driven by the OutputTarget
// PUSH channel. It is a DIRECT receiver: the game EMITS each frame (a reaction's
// Program.Emit, fired as the AI CLI applies an op) and pushes it to the session's
// frame file; this viewer watches that file with a FileSystemWatcher and repaints
// the instant a new frame arrives. It renders the EMITTED projection — it never
// re-queries or replays the journal, so it is a viewer of the game's own frame,
// not a narrator reconstructing the board.
//
// Usage: TetrisWatch <session>
//
// (The pull-poll Tetris/observer remains only as a documented fallback for when a
// push channel is unavailable; this push viewer is the primary, direct path.)

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: TetrisWatch <session>");
    return 2;
}

var session = args[0];
var framePath = SessionPaths.FrameFile(session);
var frameDir = Path.GetDirectoryName(framePath)!;
var frameName = Path.GetFileName(framePath);
Directory.CreateDirectory(frameDir);

var stop = false;
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop = true; };

var interactiveConsole = !Console.IsOutputRedirected;
if (interactiveConsole)
{
    Console.CursorVisible = false;
}

// Event-driven repaint: a FileSystemWatcher signals when the frame file changes;
// the render loop wakes, debounces a burst of writes, reads the latest frame, and
// repaints. No timed polling, no journal access.
using var signal = new ManualResetEventSlim(false);
using var watcher = new FileSystemWatcher(frameDir, frameName)
{
    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
    EnableRaisingEvents = true,
};
watcher.Changed += (_, _) => signal.Set();
watcher.Created += (_, _) => signal.Set();
watcher.Renamed += (_, _) => signal.Set();

try
{
    if (interactiveConsole)
    {
        Console.Clear();
    }

    string? lastFrame = null;
    Repaint(ref lastFrame); // paint whatever frame already exists

    while (!stop)
    {
        // Q / Esc also exit (interactive only).
        if (interactiveConsole && Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true).Key;
            if (key is ConsoleKey.Q or ConsoleKey.Escape)
            {
                break;
            }
        }

        // Wake on a push (file change) or periodically to re-check the quit key.
        if (signal.Wait(TimeSpan.FromMilliseconds(200)))
        {
            signal.Reset();
            Thread.Sleep(40); // debounce a burst of rapid writes (e.g. land+spawn)
            Repaint(ref lastFrame);
        }
    }
}
finally
{
    if (interactiveConsole)
    {
        Console.CursorVisible = true;
        Console.SetCursorPosition(0, 26);
    }
}

return 0;

void Repaint(ref string? lastFrame)
{
    var document = ReadFrame();
    if (document is null || document == lastFrame)
    {
        return;
    }

    lastFrame = document;

    var snapshot = FrameDocument.Parse(document);
    if (snapshot is null)
    {
        return; // a half-written or empty frame; the next push will redraw
    }

    var hud = snapshot.IsGameOver
        ? "game over"
        : snapshot.IsAwaitingPiece ? "awaiting piece" : $"falling: {snapshot.ActiveType}";
    var board = BoardRenderer.Board(snapshot, $"WATCHING — session {session} (live push, read-only)   [{hud}]");
    var output = board + Environment.NewLine + "(Q/Esc/Ctrl-C to quit)";

    if (interactiveConsole)
    {
        Console.SetCursorPosition(0, 0);
        Console.Write(output);
    }
    else
    {
        Console.WriteLine(output);
    }
}

// Read the pushed frame document; tolerate transient read races with the sink's
// atomic move (return null, the next change re-triggers).
string? ReadFrame()
{
    try
    {
        return File.Exists(framePath) ? File.ReadAllText(framePath) : null;
    }
    catch (IOException)
    {
        return null;
    }
}
