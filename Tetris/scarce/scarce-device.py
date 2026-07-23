#!/usr/bin/env python3
"""
scarce-device — an emulated ESP32-C6 with a tiny screen and ONE button.

This stands in for the physical device (Alvaro has none). It is the scarce
AUDIENCE of Paper 9, Experiment B: the same Tetris `Well` received through a
narrow *mirilla* — a low-bandwidth screen — and driven through a scarce
instrument — a single button. **The obra does not change.** The domain still
runs on the PC host (`TetrisStage`); this device is the audience's instrument,
not the stage the obra runs on. (An MCU cannot host the .NET domain, so this is
honestly a constrained *mirilla*, not a constrained execution stage.)

The C6 has WiFi, so the device reaches the host over two TCP endpoints — the two
seams kept physically separate, as in the REST/SSE lab:
  * SCREEN  (output): connect to --screen-port, receive packed frames, render.
  * BUTTON  (input) : connect to --button-port, send raw button EVENTS.

Packed screen frame (what a scarce mirilla receives — tens of bytes, not the
host's JSON):  S1;W;H;cleared;flagsHex;rowsHex
  flagsHex : one nibble, bit0=awaiting, bit1=game-over
  rowsHex  : H row masks, each ceil(W/4) hex digits, bit c set => column c filled

One-button grammar (the device firmware turns press patterns into events; the
host's ButtonSource routes events -> verbs):
  tap      -> rotate      double -> right      triple -> left
  hold     -> drop        longhold -> quit

Usage:
  # host first (it listens on both ports):
  #   dotnet run --project input/TetrisStage.csproj -- g1 \
  #       --sources button,clock --sink scarce --clock-ms 800
  python scarce/scarce-device.py --demo            # scripted one-button play
  python scarce/scarce-device.py                   # interactive: type events
"""

import argparse
import socket
import sys
import threading
import time


def connect(host: str, port: int, what: str, retries: int = 60) -> socket.socket:
    for attempt in range(retries):
        try:
            s = socket.create_connection((host, port), timeout=2)
            s.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
            print(f"[scarce-device] {what} connected to {host}:{port}", flush=True)
            return s
        except OSError:
            if attempt == 0:
                print(f"[scarce-device] waiting for host {what} on {host}:{port} ...", flush=True)
            time.sleep(0.5)
    raise SystemExit(f"[scarce-device] could not connect {what} to {host}:{port}")


# --- screen (output) -------------------------------------------------------

def render(line: str) -> None:
    """Decode one packed frame and paint the tiny screen."""
    try:
        magic, w, h, cleared, flags_hex, rows_hex = line.split(";", 5)
    except ValueError:
        return
    if magic != "S1":
        return
    w, h, cleared = int(w), int(h), int(cleared)
    flags = int(flags_hex, 16)
    over, awaiting = bool(flags & 2), bool(flags & 1)
    digits = (w + 3) // 4

    rows = []
    for r in range(h):
        chunk = rows_hex[r * digits:(r + 1) * digits]
        mask = int(chunk, 16) if chunk else 0
        rows.append("".join("[]" if (mask >> c) & 1 else "  " for c in range(w)))

    out = ["\x1b[2J\x1b[H"]  # clear + home: a scarce screen only ever holds the latest frame
    out.append(f"+-- ESP32-C6 OLED ({w}x{h}) --  screen frame = {len(line)} B on the wire")
    out.append(f"| lines={cleared}  {'GAME OVER' if over else ('awaiting' if awaiting else 'falling')}")
    top = "." * (w * 2 + 2)
    out.append("  " + top)
    for row in rows:
        out.append("  :" + row + ":")
    out.append("  " + "'" * (w * 2 + 2))
    print("\n".join(out), flush=True)


def screen_loop(sock: socket.socket, stop: threading.Event) -> None:
    buf = ""
    sock.settimeout(0.5)
    while not stop.is_set():
        try:
            data = sock.recv(4096)
        except socket.timeout:
            continue
        except OSError:
            break
        if not data:
            break
        buf += data.decode("ascii", errors="ignore")
        while "\n" in buf:
            line, buf = buf.split("\n", 1)
            if line.strip():
                render(line.strip())


# --- button (input) --------------------------------------------------------

DEMO_SCRIPT = [
    ("tap", 0.6),      # rotate
    ("double", 0.6),   # right
    ("double", 0.6),   # right
    ("triple", 0.6),   # left
    ("tap", 0.6),      # rotate
    ("hold", 0.9),     # hard drop -> lands, host spawns next
    ("double", 0.6),   # right
    ("hold", 0.9),     # drop
]


def send_event(sock: socket.socket, ev: str) -> bool:
    try:
        sock.sendall((ev + "\n").encode("ascii"))
        print(f"[scarce-device] button -> {ev}", flush=True)
        return True
    except OSError:
        print("[scarce-device] button channel lost", flush=True)
        return False


def run_demo(sock: socket.socket, loops: int, quit_at_end: bool) -> None:
    for i in range(loops):
        for ev, hold in DEMO_SCRIPT:
            if not send_event(sock, ev):
                return
            time.sleep(hold)
    if quit_at_end:
        send_event(sock, "longhold")  # -> quit


def run_interactive(sock: socket.socket) -> None:
    print("[scarce-device] type an event per line: tap|double|triple|hold|longhold "
          "(Ctrl-D to stop)", flush=True)
    for raw in sys.stdin:
        ev = raw.strip().lower()
        if not ev:
            continue
        if not send_event(sock, ev):
            return
        if ev == "longhold":
            return


def main() -> int:
    ap = argparse.ArgumentParser(description="Emulated ESP32-C6 scarce Tetris device")
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--screen-port", type=int, default=5113)
    ap.add_argument("--button-port", type=int, default=5112)
    ap.add_argument("--demo", action="store_true", help="play a scripted one-button sequence")
    ap.add_argument("--loops", type=int, default=1)
    ap.add_argument("--quit", action="store_true", help="demo: send quit (longhold) at the end")
    ap.add_argument("--no-screen", action="store_true", help="button only (skip the screen channel)")
    args = ap.parse_args()

    stop = threading.Event()
    screen_thread = None
    if not args.no_screen:
        screen_sock = connect(args.host, args.screen_port, "screen")
        screen_thread = threading.Thread(target=screen_loop, args=(screen_sock, stop), daemon=True)
        screen_thread.start()

    button_sock = connect(args.host, args.button_port, "button")
    try:
        if args.demo:
            run_demo(button_sock, args.loops, args.quit)
            time.sleep(0.4)  # let the final frame paint
        else:
            run_interactive(button_sock)
    except KeyboardInterrupt:
        print("\n[scarce-device] stopped.", flush=True)
    finally:
        stop.set()
        try:
            button_sock.close()
        except OSError:
            pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
