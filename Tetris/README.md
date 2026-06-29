# Tetris

A clean, infrastructure-free domain model of Tetris — pieces, the well, the
pile, the boundary, collision, line clears, and game over — built as a showcase
of rich object-oriented modelling. There is **no Puppeteer reference** anywhere
in the domain: it is plain C# that builds and tests standalone. A later, separate
phase will wrap the aggregate root as a Puppeteer V2 actor to run "distributed
observation" labs (see the papers below). The solution is [`Tetris.sln`](Tetris.sln).

## Layout

```
Tetris/
├── Tetris.sln
├── domain/         TetrisDomain library        (no Puppeteer reference)
├── domain.tests/   MSTest invariant + behaviour suite
└── console/        TetrisConsole demo           (pure domain, no Puppeteer)
```

[`domain/`](domain/) holds the model. Its only public *anchor* is
`TetrisDomain`, an empty type that lets a future host hand the assembly to the
framework with `typeof(TetrisDomain).Assembly` — the same convention as
HelloWorld's `WelcomeDomain`. The aggregate root, `Well`, is `internal`: nothing
outside the assembly references it directly. The value objects it composes are
public, because they are pure data with no behaviour worth hiding, but the
verb-bearing root stays internal so that the framework reaches it by reflection
later, not by a compile-time call.

| Project | What it is |
|---|---|
| [`domain/`](domain/) | The clean DDD model. Immutable value objects + one mutable aggregate root. |
| [`domain.tests/`](domain.tests/) | 35 MSTest cases covering geometry, collision, line clears, invariants, and determinism. |
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
public abstract class Shape
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
| `O` | 1 | The square looks the same from every side; `Rotate()` is a no-op. |
| `I`, `S`, `Z` | 2 | A bar or skew has only two appearances. |
| `T`, `J`, `L` | 4 | Each pose is genuinely distinct. |

[`Orientation`](domain/Orientation.cs) is a value object that knows its piece's
`DistinctCount` and cycles `Index` modulo that count, so `O` never leaves pose
0, `S` toggles 0↔1, and `T` walks 0→1→2→3→0. Rotation is immutable: `Rotate()`
returns a *new* piece; `Translate(offset)` likewise. A piece never mutates and
never knows about walls or the pile — only about its own shape. The well decides
legality; the piece only offers candidates.

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

## Determinism: the next piece comes from outside

The well never rolls dice. Piece selection is an **external** input through
[`IPieceSource`](domain/IPieceSource.cs); the tests and the demo use a
`ScriptedPieceSource` that hands out a fixed sequence. This is deliberate. The
`Well` is engineered to become a Puppeteer actor whose journal of verb
invocations is replayed; an internal `Random` would make two replays of the same
journal diverge. By demanding the next piece from the outside, the well stays a
pure function of *(initial size + piece sequence + verb sequence)*. The
`DeterminismTests` replay the same script ten times and assert a single,
identical state fingerprint.

## The aggregate root and its verbs

[`Well`](domain/Well.cs) is the only mutable thing in the model. It composes the
`Frame`, the `Pile`, and the active `Piece`, and exposes guarded verbs:

- `MoveLeft()`, `MoveRight()`, `Rotate()` — compute the candidate placement and
  apply it **iff** `!Collides(candidate)`; otherwise the move is rejected as a
  no-op. There are no wall-kicks (see the trade-off below).
- `Tick()` — descend one row if free; otherwise **land**: integrate the active
  piece into the pile, clear complete rows bottom-up, and draw the next piece.
- `Drop()` — descend until resting, then land (the hard drop).

A newly spawned piece that immediately collides ends the game (`IsGameOver`),
after which the verbs are inert. A simple `ClearedLines` counter is the only
score-like state kept, and it stays clean.

## Invariants (and where they live)

Every transition ends with `Well.AssertInvariants()`, the executable statement
of what a valid well is. It never fires in correct play; it turns any modelling
bug into a loud `WellInvariantException` rather than silent corruption.

| Invariant | Enforced where |
|---|---|
| A piece has **exactly four** distinct cells | `Piece` constructor → `InvalidPieceException` |
| An orientation matches its piece's symmetry | `Piece` constructor (pose `DistinctCount` vs piece's count) |
| The active piece rests in **free space** | `Well.AssertInvariants()` via the unified `Collides` |
| Every occupied cell lies **inside the frame** | `Well.AssertInvariants()` via `Frame.Contains` |
| The pile **never retains a complete row** | `Pile.ClearCompleteRows()` by construction; re-checked in `AssertInvariants()` |
| **Determinism** of `(state + inputs)` | No internal RNG; piece input is external (`IPieceSource`) |

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

## Conceptual entry point

The design conditions this example illustrates — a clean domain with zero
infrastructure, immutable value objects with a single mutable aggregate root,
and determinism as a precondition for journal replay — are developed in the
companion papers repository
[`alvaroNCubo/puppeteer-papers`](https://github.com/alvaroNCubo/puppeteer-papers).
Paper 1 (*Anti-porosity*) is the entry point; the distributed-observation labs
this model is destined for belong to Paper 9.
