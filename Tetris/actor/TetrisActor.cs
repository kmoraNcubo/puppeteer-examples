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
    private readonly PerformanceV2 performance;

    public TetrisActor(string actorName, int width, int height)
    {
        // The framework discovers the domain (Well, Piece, …) by reflection over
        // this assembly; only the public anchor TetrisDomain is needed as the seam.
        performance = new PerformanceV2(actorName, typeof(TetrisDomain).Assembly)
            .ConfigureStorage(DatabaseType.IN_MEMORY, "InMemory")
            .Start();

        // Seed the aggregate into actor state. 'upgrade' runs its body once and
        // is recognised as already-applied on every later rehydration.
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

        // Literal command — the journal records well.Spawn('T'); the engine
        // coerces the string to the PieceType enum by member name.
        performance.Using($"well.Spawn('{letter}');").PerformCommand();
    }

    /// <summary>Slides the active piece one column left (a blocked slide is a no-op).</summary>
    public void MoveLeft() => performance.Using("well.MoveLeft();").PerformCommand();

    /// <summary>Slides the active piece one column right (a blocked slide is a no-op).</summary>
    public void MoveRight() => performance.Using("well.MoveRight();").PerformCommand();

    /// <summary>Rotates the active piece one step (a blocked rotation is a no-op).</summary>
    public void Rotate() => performance.Using("well.Rotate();").PerformCommand();

    /// <summary>Advances the active piece one row; lands it if it cannot descend.</summary>
    public void Tick() => performance.Using("well.Tick();").PerformCommand();

    /// <summary>Hard-drops the active piece to its resting place and lands it.</summary>
    public void Drop() => performance.Using("well.Drop();").PerformCommand();

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
        // render entirely from it. The separate active-cells list is offered for
        // convenience and is queried only when a piece is actually falling
        // (query-first: never touch well.Active while awaiting / over).
        var active = (scalars.IsGameOver || scalars.IsAwaitingPiece)
            ? (IReadOnlyList<Cell>)Array.Empty<Cell>()
            : QueryCells("well.Active.Cells");

        return new WellSnapshot(
            scalars.Width,
            scalars.Height,
            occupied,
            active,
            scalars.ClearedLines,
            scalars.IsGameOver,
            scalars.IsAwaitingPiece);
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
