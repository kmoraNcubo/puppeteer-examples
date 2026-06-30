using System;
using System.Collections.Generic;
using System.Text.Json;
using Choreography.Theater;
using Puppeteer;
using Tetris;

namespace Tetris.Acting;

/// <summary>
/// A typed facade over a Puppeteer <see cref="PerformanceV2"/> that wraps the
/// clean <see cref="Well"/> domain. Hosts — this console, and later phone/web
/// hosts — talk only to these typed methods and the typed <see cref="Snapshot"/>;
/// every DSL string, every <c>PerformCmd</c>/<c>PerformQry</c> call, lives here.
/// <para>
/// The well is materialised inside the actor's state by a once-applied
/// <c>upgrade</c>, and each inbound method formats a small DSL command against
/// that <c>well</c>. The journal therefore records the exact command stream, so
/// the game is replayable.
/// </para>
/// </summary>
public sealed class TetrisActor : IDisposable
{
    // Check guards (the GENTLE preconditions). PerformCheckThenCommand enacts the
    // command only when the Check condition is true and otherwise leaves state
    // untouched — so in normal play the domain's hard TetrisRuleException is never
    // reached; the domain invariant stays the backstop. "A piece is active" is
    // exactly "not over and not awaiting"; "can spawn" is "awaiting a piece".
    private const string ActivePieceCheck =
        "{ Check(well.IsGameOver == false && well.IsAwaitingPiece == false) WARNING 'no active piece'; }";
    private const string AwaitingPieceCheck =
        "{ Check(well.IsAwaitingPiece == true) WARNING 'not awaiting a piece'; }";

    private readonly PerformanceV2 performance;

    /// <summary>
    /// Opens an in-memory session (state lost on process exit). This is the ctor
    /// the human console uses.
    /// </summary>
    public TetrisActor(string actorName, int width, int height)
        : this(actorName, width, height, DatabaseType.IN_MEMORY, "InMemory")
    {
    }

    /// <summary>
    /// Opens a session backed by a persistent FileSystem journal at
    /// <paramref name="journalDirectory"/>. State survives across process exits:
    /// a later <see cref="TetrisActor"/> on the same directory rehydrates by
    /// replaying the journal. Used by the per-op AI CLI and the observer.
    /// </summary>
    public static TetrisActor Persistent(string actorName, int width, int height, string journalDirectory) =>
        new(actorName, width, height, DatabaseType.FileSystem, $"path={journalDirectory};maxFileSize=4194304");

    private TetrisActor(string actorName, int width, int height, DatabaseType storage, string connectionString)
    {
        // The framework discovers the domain (Well, Piece, …) by reflection over
        // this assembly; only the public anchor TetrisDomain is needed as the seam.
        performance = new PerformanceV2(actorName, typeof(TetrisDomain).Assembly)
            .ConfigureStorage(storage, connectionString)
            .Start();

        // Seed the aggregate into actor state. 'upgrade' runs its body once and
        // is recognised as already-applied on every later rehydration — so a
        // persistent session that already has a 'seed' entry keeps its well.
        performance.Using($"upgrade('seed') {{ well = Well({width}, {height}); }}").PerformCommand();
    }

    // ── Inbound verbs (the controller surface) ─────────────────────────────

    /// <summary>
    /// Spawns the next piece. The piece is chosen by the DOMAIN — a query over
    /// <c>well.NextPieceLetter()</c>, whose randomness is transient and never
    /// journaled — and the resolved letter is then issued as a LITERAL command,
    /// so the journal records <c>well.Spawn('T');</c>. The captured letter
    /// crystallises as a journaled literal, and a replay re-applies that exact
    /// <c>Spawn('T')</c> deterministically, with no RNG on the replay path. The
    /// framework coerces the string letter to the domain's <c>PieceType</c> enum
    /// by member name (verified: <c>'T'</c> binds to <c>PieceType.T</c>).
    /// <para>
    /// Design note: the canonical intent was to do this with the <c>Eval</c>
    /// parameter modifier (<c>well.Spawn(@f)</c> with <c>f</c> = Eval
    /// <c>well.NextPieceLetter()</c>). That journals the right literal, but the
    /// current engine MIS-REPLAYS multiple Eval-action invocations (their literal
    /// arguments get crossed on rehydration), so two replays of the same journal
    /// diverge. The query-then-literal form below is behaviourally identical
    /// live, journals the same readable literal, AND replays deterministically.
    /// Revisit the Eval form once the engine replay bug is fixed.
    /// </para>
    /// </summary>
    public void SpawnNext()
    {
        // The domain decides (transient RNG, not journaled). NextPieceLetter()
        // returns one of "I,O,T,S,Z,J,L".
        var letter = QueryString("print well.NextPieceLetter() letter;", "letter");

        // Check-then-command: spawn only when the well is awaiting a piece. The
        // journal records well.Spawn('T'); the engine coerces the string to the
        // PieceType enum by member name.
        performance.Using(AwaitingPieceCheck, $"well.Spawn('{letter}');").PerformCheckThenCommand();
    }

    /// <summary>Slides the active piece one column left (a blocked slide is a no-op).</summary>
    public void MoveLeft() => GuardedVerb("well.MoveLeft();");

    /// <summary>Slides the active piece one column right (a blocked slide is a no-op).</summary>
    public void MoveRight() => GuardedVerb("well.MoveRight();");

    /// <summary>Rotates the active piece one step (a blocked rotation is a no-op).</summary>
    public void Rotate() => GuardedVerb("well.Rotate();");

    /// <summary>Advances the active piece one row; lands it if it cannot descend.</summary>
    public void Tick() => GuardedVerb("well.Tick();");

    /// <summary>Hard-drops the active piece to its resting place and lands it.</summary>
    public void Drop() => GuardedVerb("well.Drop();");

    // Every move verb is check-guarded by the active-piece precondition, so the
    // command runs only while a piece is falling; otherwise it is a clean no-op
    // (state untouched) and the domain's hard guard is never tripped.
    private void GuardedVerb(string command) =>
        performance.Using(ActivePieceCheck, command).PerformCheckThenCommand();

    // ── Typed read for rendering + control flow ────────────────────────────

    /// <summary>
    /// Runs queries over the well and parses the results into an immutable
    /// <see cref="WellSnapshot"/> — the only window a host has into game state.
    /// </summary>
    public WellSnapshot Snapshot()
    {
        var scalars = QueryScalars();
        var occupied = QueryCells("well.OccupiedInterior()");

        // OccupiedInterior already includes the falling piece, so a host can
        // render entirely from it. The active piece's cells and TYPE are queried
        // only when a piece is actually falling (query-first: never touch
        // well.Active while awaiting / over).
        var hasActive = !scalars.IsGameOver && !scalars.IsAwaitingPiece;
        var active = hasActive
            ? QueryCells("well.Active.Cells")
            : (IReadOnlyList<Cell>)Array.Empty<Cell>();

        // The active piece's PieceType, rendered by the formatter as its member
        // name (e.g. "T"); null when there is no falling piece.
        var activeType = hasActive
            ? QueryString("print well.Active.Type t;", "t")
            : null;

        return new WellSnapshot(
            scalars.Width,
            scalars.Height,
            occupied,
            active,
            scalars.ClearedLines,
            scalars.IsGameOver,
            scalars.IsAwaitingPiece,
            activeType);
    }

    /// <summary>Runs a single-scalar string query and returns the value under <paramref name="key"/>.</summary>
    private string QueryString(string script, string key)
    {
        var json = performance.Using(script).PerformQuery();
        using var doc = ParseDocument(json);
        return doc.RootElement.GetProperty(key).GetString()
            ?? throw new InvalidOperationException($"Query '{script}' returned no '{key}'.");
    }

    private (int Width, int Height, int ClearedLines, bool IsGameOver, bool IsAwaitingPiece) QueryScalars()
    {
        var json = performance.Using(
            "print well.Frame.Width width, well.Frame.Height height, " +
            "well.ClearedLines cleared, well.IsGameOver over, well.IsAwaitingPiece awaiting;").PerformQuery();

        using var doc = ParseDocument(json);
        var root = doc.RootElement;
        return (
            root.GetProperty("width").GetInt32(),
            root.GetProperty("height").GetInt32(),
            root.GetProperty("cleared").GetInt32(),
            root.GetProperty("over").GetBoolean(),
            root.GetProperty("awaiting").GetBoolean());
    }

    private IReadOnlyList<Cell> QueryCells(string cellsExpression)
    {
        // A foreach-print loop renders as a JSON array keyed by the LOOP VARIABLE
        // name, each element carrying the print aliases — e.g. for
        // `foreach (cell in ...) { print cell.Row r, cell.Column c; }` the engine
        // emits {"cell":[{"r":0,"c":3},{"r":1,"c":4},...]}. An empty loop
        // collapses to "" (normalised to "{}" below).
        var json = performance.Using(
            $"foreach (cell in {cellsExpression}) {{ print cell.Row r, cell.Column c; }}").PerformQuery();

        var cells = new List<Cell>();
        using var doc = ParseDocument(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("cell", out var array)
            && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in array.EnumerateArray())
            {
                cells.Add(new Cell(element.GetProperty("r").GetInt32(), element.GetProperty("c").GetInt32()));
            }
        }

        return cells;
    }

    /// <summary>
    /// The engine collapses an empty document to the empty string; normalise that
    /// to <c>{}</c> so System.Text.Json always has something to parse.
    /// </summary>
    private static JsonDocument ParseDocument(string json) =>
        JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);

    public void Dispose() => performance.Dispose();
}
