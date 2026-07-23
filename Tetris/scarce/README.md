# Scarce audience — an emulated ESP32-C6 (tiny screen + one button)

*Paper 9, Experiment B (change the AUDIENCE, not the obra).* A scarce device
receives the unchanged Tetris `Well` through a narrow *mirilla* — a low-bandwidth
screen — and drives it through a scarce instrument — a **single button**. **The
obra does not change.**

## Honest scope (read this first)

Alvaro has no physical ESP32, so this is **emulated**. Be precise about what it
demonstrates:

- It is a **scarce mirilla + scarce input** — the Experiment-B *audience* axis,
  constrained on **both** ends at once.
- It is **not** a constrained execution *stage*. The obra (the .NET `Well`) still
  runs on the PC host; an MCU cannot host it. The device is the audience's
  instrument, a thin client over WiFi/TCP — not the ground the play runs on. The
  "ESP32-C6" name is the shape we emulate (a WiFi MCU with a small OLED and a
  button); the emulator ([scarce-device.py](scarce-device.py)) is a stand-in, not
  a lived silicon integration. Nothing here is sold as more than it is.

## The seam: one new InputSource + one new OutputTarget, zero domain change

This audience adds **only** a new input mirilla and a new output mirilla — the
literal Experiment-B claim. Two new seam classes plus a backward-compatible host
switch:

- [`../input/ButtonSource.cs`](../input/ButtonSource.cs) — an `IInputSource`
  whose medium is one button. Its route table maps raw press *events* to logical
  commands, exactly as `KeyboardSource` maps a `ConsoleKey`.
- [`../input/ScarceSink.cs`](../input/ScarceSink.cs) — an `IOutputSink` (the same
  seam as `FrameFileSink` / `WebSocketSink` / `SseSink`) that repacks the SAME
  frame projection into a minimal row-bitmask frame and pushes it to the device.
- [`../input/TetrisStage.cs`](../input/TetrisStage.cs) — one optional ctor
  parameter (the output target; defaults to the frame file, so every existing
  usage is unchanged), and [`../input/Program.cs`](../input/Program.cs) gains a
  `button` source arm + `--sink scarce` + ports.

**The `domain/` diff is zero. So is the `actor/` diff** — not even the domain
wrapper changed. Verify: `git diff --stat -- ../domain ../actor` (empty).

### Two endpoints — `InputSource ≠ OutputTarget`, made physical

The device connects **two** TCP endpoints, mirroring the REST/SSE lab's split
(input `POST /moves` vs output `SSE /events`):

- **Button (input)** → `--button-port` (default 5112): the device *sends* raw
  press events; `ButtonSource` routes them to verbs.
- **Screen (output)** → `--scarce-port` (default 5113): the host *pushes* packed
  frames; the device renders them.

## The one-button grammar

One button cannot press six keys, so the firmware distinguishes press patterns;
`ButtonSource` routes each event to a verb:

| button event | → logical command | effect |
|---|---|---|
| `tap` | `rotate` | rotate |
| `double` | `right` | move right |
| `triple` | `left` | move left |
| `hold` | `drop` | hard drop |
| `longhold` | `quit` | stop |

Gravity comes from the `clock` source, so the device only has to *steer*.

## The scarce frame format

`ScarceSink` emits one ASCII line per frame (newline-terminated):

```
S1;W;H;cleared;flagsHex;rowsHex
```

`flagsHex` is one nibble (bit0 = awaiting, bit1 = game-over); `rowsHex` is H row
masks, each `ceil(W/4)` hex digits, bit `c` set ⇔ column `c` filled.

**Measured scarcity** (10×20 well): the packed frame is a **constant ~74 bytes**
(1 header + 20 row-masks), whatever the board holds. The engine's JSON frame is
**146 bytes with 4 cells filled and grows ~15 bytes per additional occupied
cell** (`{"r":18,"c":3},`), so a busy board is several hundred bytes. Same
projection, same seam — the mirilla just fits what an MCU with kilobytes of RAM
can hold. (`ScarceSink.LastPackedBytes` exposes the live figure.)

## Run it

**1. Start the host** (it listens on both ports):

```bash
dotnet run --project input/TetrisStage.csproj -- g1 \
    --sources button,clock --sink scarce --clock-ms 800
```

**2. Start the emulated device**:

```bash
python scarce/scarce-device.py --demo --quit   # scripted one-button play
python scarce/scarce-device.py                  # interactive: type events
```

The device paints a tiny OLED-style grid and prints the wire size of each frame;
the host logs `applied: rotate … right … left … drop …` as the one button steers
the same `Well`. Runs on any Python 3.x — no camera, no MediaPipe, no Docker, no
hardware.

## What was verified here (measure, don't proclaim)

- **`domain/` and `actor/` diffs = 0.** The only edits are the two new seam
  classes and a backward-compatible switch in `input/`.
- **Both seams, end-to-end, against the real domain via the real engine:** the
  one-button device drove `rotate/right/left/drop` into the warm `TetrisActor`
  (29 commands in the final run), and the host pushed packed 73–74-byte frames the
  device rendered as the falling well. Verified in this repo state.
- **Scarcity is measured, not asserted:** 74 B packed (constant) vs 146 B JSON at
  4 cells (growing with fill).
- **Not claimed:** a lived integration on real ESP32 silicon, or a constrained
  execution *stage*. This is an emulated scarce *mirilla* — see "Honest scope".

## Building inside a git worktree

Same note as [gesture/README.md](../gesture/README.md): the `actor/` engine
reference is relative to the main checkout, so engine-dependent projects need
either a main-checkout build or the build-time junction. The `domain/` builds
anywhere.
