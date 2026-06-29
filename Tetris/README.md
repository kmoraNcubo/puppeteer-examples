# Tetris

A clean, infrastructure-free domain model of Tetris — pieces, the well, the
pile, the boundary, collision, line clears, and game over — built as a showcase
of rich object-oriented modelling. There is **no Puppeteer reference** anywhere
in the domain: it is plain C# that builds and tests standalone, and its source
reads as if no framework exists. A later, separate phase will wrap the aggregate
root for the "distributed observation" labs — see
[Where this is going](#where-this-is-going) below. The solution is
[`Tetris.sln`](Tetris.sln).

## Layout

```
Tetris/
├── Tetris.sln
├── domain/         TetrisDomain library        (no Puppeteer reference)
├── domain.tests/   MSTest invariant + behaviour suite
└── console/        TetrisConsole demo           (pure domain, no Puppeteer)
```

[`domain/`](domain/) holds the model. **Exactly one type is public** — the
anchor `TetrisDomain`, an empty type that lets a host hand the assembly to a
host with `typeof(TetrisDomain).Assembly`, the same convention as HelloWorld's
`WelcomeDomain`. *Everything else is `internal`*: `Shape`, `Position`, `Piece`
and its seven subclasses, `Pile`, `Frame`, `Orientation`, `PieceType`,
`Tetromino`, the piece sources, the exceptions, and the aggregate root `Well`.
From outside the assembly the model presents no surface at all but the anchor,
so a caller cannot fabricate an invalid placement — there is no public
`Position` to hand the well a cell outside the frame. The trusted insiders are
the test suite and the console demo, granted access through
`[assembly: InternalsVisibleTo(...)]` in [`AssemblyInfo.cs`](domain/AssemblyInfo.cs);
a host reaches the verbs by reflection over the assembly.

| Project | What it is |
|---|---|
| [`domain/`](domain/) | The clean DDD model. Immutable value objects + one mutable aggregate root. Only `TetrisDomain` is public. |
| [`domain.tests/`](domain.tests/) | 41 MSTest cases covering geometry, collision, line clears, invariants, and determinism. |
| [`console/`](console/) | A pure-domain demo that plays a fixed script and renders the well as ASCII. |

## The unifying abstraction: figures within figures

Everything occupied in a Tetris well is the same kind of thing — *a set of
occupied cells in a grid*. A falling piece is such a set. The accumulated pile
is such a set. And — the decision the whole model turns on — **so is the
boundary**. The walls and floor are not a special case checked with arithmetic;
they are a figure of permanently occupied *sentinel* cells, exactly like a piece
or the pile.

That single idea is expressed as the abstract [`Shape`](domain/Shape.cs):

```csharp
internal abstract class Shape
{
    public abstract ImmutableHashSet<Position> Cells { get; }
    public bool Occupies(Position p) => Cells.Contains(p);
    public bool Intersects(Shape other) => Cells.Overlaps(other.Cells);
}
```

`Piece`, `Pile`, and `Frame` all derive from `Shape`. They are *figures within
figures*: the well is a figure (the frame) that contains figures (the pile and
the falling piece).

### The frontier is a figure

`Frame` is the answer to "is the boundary just another shape?" — **yes.** It
materialises the left wall (column −1), the right wall (column `Width`), and the
floor (row `Height`) as occupied cells, leaving the ceiling open so pieces can
spawn in the sky above row 0 and fall in. Because the frontier is a figure, the
question "did the piece hit a wall?" is not different in kind from "did it hit
the pile?".

### One collision rule, not three

Naïve Tetris code has three collision checks: against the left/right walls,
against the floor, and against the pile. Here there is **one**, because all
three obstacles are figures:

```csharp
private bool Collides(Piece candidate) =>
    candidate.Intersects(Frame) || candidate.Intersects(Pile);
```

A piece pressing into a wall, a piece resting on the floor, and a piece landing
on a stack are the same event: the candidate's cells overlap some occupied
figure. The rule lives once, in [`Well`](domain/Well.cs), and reads like its own
definition.

## The pieces: polymorphism across the seven tetrominoes

[`Piece`](domain/Piece.cs) is an abstract `Shape` of exactly four cells with a
`PieceType` and an `Orientation`. The seven tetrominoes
([`Pieces.cs`](domain/Pieces.cs)) are concrete subclasses — `IPiece`, `OPiece`,
`TPiece`, `SPiece`, `ZPiece`, `JPiece`, `LPiece`. Each subclass supplies exactly
**one** thing: the anchor-local layout of its four cells for a given pose. The
base class owns everything else — anchoring into the well, exposing world cells,
rotating, translating, and enforcing the four-cell invariant.

The pieces differ in their rotational symmetry, and the polymorphism captures
that difference *naturally* rather than with conditionals:

| Piece | Distinct orientations | Why |
|---|---|---|
| `O` | 1 | The square looks the same from every side; rotation is a no-op. |
| `I`, `S`, `Z` | 2 | A bar or skew has only two appearances. |
| `T`, `J`, `L` | 4 | Each pose is genuinely distinct. |

[`Orientation`](domain/Orientation.cs) is a value object that knows its piece's
`DistinctCount` and cycles `Index` modulo that count, so `O` never leaves pose
0, `S` toggles 0↔1, and `T` walks 0→1→2→3→0. Rotation is **directional**:
`Piece.Rotate(RotationDirection)` steps the index forward for `Clockwise` and
backward for `CounterClockwise`, wrapping at the piece's distinct count, so the
two directions are exact inverses (turn one way then the other and you are back
where you started). Rotation is immutable: `Rotate` returns a *new* piece;
`Translate(offset)` likewise. A piece never mutates and never knows about walls
or the pile — only about its own shape. The well decides legality; the piece
only offers candidates.

## The pile mutates bottom-up

[`Pile`](domain/Pile.cs) is the accumulated landed blocks — *the floor that does
not move but mutates*. It is immutable: `Integrate(figure)` returns a new pile
with a landed figure's cells merged in, and `ClearCompleteRows()` returns a new
pile with full rows removed.

The clear is genuinely **bottom-up**. A surviving cell drops by the number of
cleared rows strictly *below* it:

```csharp
var clearedBelow = completed.Count(clearedRow => clearedRow > cell.Row);
survivors.Add(cell.Translate(new Offset(clearedBelow, 0)));
```

So a block sitting two rows above a vanished line ends one row lower; a block
above *two* vanished lines ends two rows lower; and a tower spanning a
non-adjacent pair of cleared rows collapses correctly because each surviving
cell counts only the clears beneath it. The tests pin all three cases.

## Choosing the next piece, and determinism

Deciding which tetromino comes next is honest domain logic, and the well makes
that decision — but through a seam, [`IPieceSource`](domain/IPieceSource.cs), so
the *policy* lives in one swappable place:

- `RandomPieceSource` picks each piece uniformly at random. It is the natural,
  default source: the parameterless `new Well(width, height)` constructor uses
  it, so an ordinary game is unpredictable.
- `ScriptedPieceSource` hands out a fixed sequence. Supplied via
  `new Well(width, height, source)`, it makes a game exactly reproducible — the
  same sequence fed to a fresh well always produces the same play. The tests and
  the demo use it.

Once the piece sequence is fixed, the well is **deterministic**: the same
construction, the same piece sequence, and the same sequence of verbs always
reach the same state. Every value object is immutable, and a transition replaces
a reference rather than mutating in place — the only nondeterminism anywhere is
the random source, and it is injectable precisely so it can be pinned. The
`DeterminismTests` replay the same script ten times and assert a single,
identical state fingerprint.

## The aggregate root and its verbs

[`Well`](domain/Well.cs) is the only mutable thing in the model. It composes the
`Frame`, the `Pile`, and the active `Piece`, and exposes guarded verbs (all
`internal`, since the class itself is internal):

- `MoveLeft()`, `MoveRight()` — shift the active piece one column and apply it
  **iff** `!Collides(candidate)`; otherwise the move is rejected as a no-op.
- `Rotate(RotationDirection)`, with `RotateClockwise()` / `RotateCounterClockwise()`
  convenience verbs — turn the active piece and apply it iff the rotated pose
  does not collide. There are no wall-kicks (see the trade-off below).
- `Tick()` — descend one row if free; otherwise **land**: integrate the active
  piece into the pile, clear complete rows bottom-up, and draw the next piece.
- `Drop()` — descend until resting, then land (the hard drop).

Drawing the next piece (`Spawn`) is **not** an external verb — it is `private`,
triggered only by construction and by landing. Likewise `Collides`, `Land`,
`TryShift`, `SpawnAnchor`, and `AssertInvariants` are all private; the surface
is exactly the moves a player can make plus the read members the renderer needs
(`IsGameOver`, `ClearedLines`, `OccupiedInterior`). A newly spawned piece that
immediately collides ends the game (`IsGameOver`), after which the verbs are
inert. A simple `ClearedLines` counter is the only score-like state kept, and it
stays clean.

## Invariants (and where they live)

Every transition ends with `Well.AssertInvariants()`, the executable statement
of what a valid well is. It never fires in correct play; it turns any modelling
bug into a loud `WellInvariantException` rather than silent corruption.

| Invariant | Enforced where |
|---|---|
| A piece has **exactly four** distinct cells | `Piece` constructor → `InvalidPieceException` |
| An orientation matches its piece's symmetry | `Piece` constructor (pose `DistinctCount` vs piece's count) |
| There is an active piece **iff** the game is not over | `Well.AssertInvariants()` — `(Active is null) == IsGameOver` |
| The active piece rests in **free space** | `Well.AssertInvariants()` via the unified `Collides` |
| Every occupied cell lies **inside the frame** | `Well.AssertInvariants()` via `Frame.Contains` |
| The pile **never retains a complete row** | `Pile.ClearCompleteRows()` by construction; re-checked in `AssertInvariants()` |
| **Determinism** of `(construction + piece sequence + verbs)` | All transitions replace immutable values; the only randomness is the injectable `IPieceSource` |

## Build and run

From this folder:

```
dotnet build Tetris.sln
dotnet test Tetris.sln
dotnet run --project console/TetrisConsole.csproj
```

The domain has no Puppeteer dependency, so it builds standalone; the test
project uses MSTest from nuget.org. The console demo plays a fixed scripted
sequence and prints the well after each step — deterministic, so it renders the
same frames every run. `@` is the falling piece, `#` a landed block, `.` empty
interior; `|` and `=` draw the frame.

## Trade-offs and open questions

- **No wall-kicks.** A rotation that would collide is simply rejected. Modern
  Tetris guidelines (SRS) nudge the piece by small offsets to find a legal pose.
  That is a clean extension — try a short list of candidate offsets after the
  rotated pose and accept the first that does not collide — but it adds a kick
  table that muddies the "one collision rule" story, so it is left out here.
- **Spawn placement.** Pieces spawn with their 4-wide bounding box centred and
  anchored at row 0; the open sky above is interior. A different "spawn in the
  vanish zone above the field" convention is possible but does not change the
  spatial model.
- **`Integrate` takes a `Shape`, not a `Piece`.** The pile cares only about
  cells, not about which kind of figure they came from — the same indifference
  that unifies collision. In play the figure is always the landed piece.
- **Rotation system.** The layouts follow the common SRS cell positions, but
  with simple pivot-free rotation (each pose is an independent layout). For a
  pure spatial model this is enough; a true SRS pivot is an extension.

## Where this is going

The domain above is deliberately framework-free; nothing in its source mentions
Puppeteer, actors, or journals. This section is the forward-looking framing that
those source files deliberately omit.

In a later, separate phase the `Well` aggregate root becomes a Puppeteer V2
actor. The deterministic shape is what makes that wrapping clean: an actor's
journal records the sequence of verb invocations and is *replayed* to rebuild
state, so any nondeterminism would make two replays of the same journal diverge.
The model is already a pure function of *(construction + piece sequence +
verbs)*, with the one source of randomness — the next-piece draw — isolated
behind `IPieceSource`. Under the framework that random draw is **captured**
(via the framework's `Eval` mechanism, which records a nondeterministic result
the first time and replays the recorded value thereafter), so a replayed journal
reproduces the very same game even though the live game drew its pieces at
random. The same actor will then be observed across three topologies — a console
monolith, two decentralised phones, and a web screen with several simultaneous
viewers — for the distributed-observation labs.

## Conceptual entry point

The design conditions this example illustrates — a clean domain with zero
infrastructure, immutable value objects with a single mutable aggregate root,
and determinism as a precondition for journal replay — are developed in the
companion papers repository
[`alvaroNCubo/puppeteer-papers`](https://github.com/alvaroNCubo/puppeteer-papers).
Paper 1 (*Anti-porosity*) is the entry point; the distributed-observation labs
this model is destined for belong to Paper 9.
