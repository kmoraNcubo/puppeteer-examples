# Gesture audience — a webcam mirilla for the same `Well`

*Paper 9, Experiment B (change the AUDIENCE, not the obra).* This is an extra
**B-shadow**: a new audience receives the unchanged Tetris `Well` through a new
instrument (a *mirilla*) — a plain webcam. **The obra does not change.** The
domain receives `left` / `right` / `rotate` / `tick` / `drop` / `quit` — the
same logical commands the keyboard and the AI pipe submit — and never learns a
camera exists.

## The seam: one new `IInputSource`, zero domain change

A new audience adds **only a new `InputSource`**. Here that is a single class:

- [`../input/GestureSource.cs`](../input/GestureSource.cs) — an `IInputSource`
  whose **medium** is a camera and whose **route table** maps a hand *pose* to a
  logical command, exactly as [`KeyboardSource`](../input/KeyboardSource.cs) maps
  a raw `ConsoleKey`. To the automaton, the camera is just another keyboard whose
  "keys" are hand poses.
- One arm added to the `--sources` switch in
  [`../input/Program.cs`](../input/Program.cs) (`gesture`) plus a `--gesture-port`.

That is the whole change to the C# side. **The `domain/` diff is zero** — the
`Well`, its pieces, the pile, the frame, every invariant: byte-for-byte
untouched. The zeros are the claim; run `git diff --stat -- ../domain` to see it.

### Where recognition lives (and where routing lives)

Two levels of routing, the same as every other source:

1. **Camera → pose token** (this sidecar): MediaPipe recognises a hand shape and
   emits its "scancode" — a token like `palm_left`. All the vision lives *here*,
   in the "hardware", never in the domain.
2. **Pose token → logical command** (`GestureSource.Route`): the source's own
   keymap. `palm_left → left`, `fist → drop`, …
3. **Logical command → actor verb** (`TetrisStage.Apply`, unchanged): shared by
   every source.

The sidecar is to `GestureSource` what `TetrisSend` is to `PipeSource`: the thin
client that speaks the medium. It is **not** part of `Tetris.sln` — it is the
camera driver, and it *cannot* touch the domain.

## Pose vocabulary

| Pose (camera) | token | → logical command | effect |
|---|---|---|---|
| hand in the **left** third of frame | `palm_left` | `left` | move left |
| hand in the **right** third of frame | `palm_right` | `right` | move right |
| **open palm**, centred (5 fingers) | `open` | `rotate` | rotate |
| **index only**, centred | `point_down` | `tick` | soft drop |
| **fist**, centred (0 fingers) | `fist` | `drop` | hard drop |
| **peace** sign, centred (2 fingers) | `peace` | `quit` | stop |

The classifier in `gesture-cam.py` is deliberately simple — recognition quality
is **not** the experiment's claim (the seam is). Tune the poses freely; the C#
side never changes.

## Run it

**1. Start the host** (it listens on `127.0.0.1:5111`):

```bash
dotnet run --project input/TetrisStage.csproj -- g1 --sources gesture,clock --clock-ms 800
```

Add a live grid in another terminal with the watcher if you like:
`dotnet run --project watch/TetrisWatch.csproj -- g1`.

**2a. Live camera leg** (needs a webcam + MediaPipe):

```bash
pip install opencv-python mediapipe
python gesture/gesture-cam.py --mode camera --port 5111
```

> **Python version:** MediaPipe currently ships wheels for **Python ≤ 3.12**. On
> a newer interpreter, use a 3.12 virtualenv for the camera leg, or use `demo`
> mode (below) to exercise the seam with no camera and no MediaPipe.

**2b. No-hardware demo leg** (scripted poses over the same wire — runs on any
Python 3.x):

```bash
python gesture/gesture-cam.py --mode demo --port 5111 --quit
```

You will see the host log `applied: left … rotate … right … tick … drop …` as
the pose stream drives the `Well`, pieces landing and respawning — the same
behaviour as the keyboard, from a different mirilla.

## What was verified here (measure, don't proclaim)

- **`domain/` diff = 0.** Not even the `actor/` project or the engine changed.
  The only edits are the new `GestureSource.cs` and ~5 lines in `Program.cs`.
- **Seam, end-to-end, against the real domain via the real engine:** the `demo`
  pose stream was routed through `GestureSource` into the warm `TetrisActor`;
  all six routes applied (`left/right/rotate/tick/drop`) and `peace→quit` stopped
  the host cleanly (exit 0). Verified in this repo state.
- **Not** verified by the author: the live *visual* recognition on a physical
  webcam (no camera was available at build time). The camera leg is provided and
  correct-by-construction; the vision quality is the user's to exercise and tune.

## Note for building inside a git worktree

The `actor/` project references the Pacifico engine by a path relative to the
**main checkout** (`..\..\..\Puppeteer Pacifico`). From a nested worktree
(`.claude/worktrees/<name>/`) that path does not resolve, so the engine-dependent
projects will not build there out of the box. Either build from the main
checkout, or recreate the build-time junction used to verify this work (it
touches no tracked file):

```powershell
New-Item -ItemType Junction `
  -Path "<repo>\Tetris\.claude\worktrees\Puppeteer Pacifico" `
  -Target "<repo-parent>\Puppeteer Pacifico"
```

The pure-domain projects (`domain/`, `domain.tests/`) build anywhere — they have
no engine reference, which is the point.
