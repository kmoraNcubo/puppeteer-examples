#!/usr/bin/env python3
"""
gesture-cam — the CAMERA "hardware" for the Tetris gesture audience.

This is the physical medium behind the C# ``GestureSource`` (an ``IInputSource``),
exactly as ``TetrisSend`` is the client behind ``PipeSource``. It watches a plain
webcam, recognises a hand POSE with MediaPipe Hands, and streams that pose's
"scancode" (a token like ``palm_left``) over a loopback TCP line-stream to the
gesture source. The C# side's route table turns the pose into a logical command
(``left`` / ``right`` / ``rotate`` / ``tick`` / ``drop`` / ``quit``).

    The domain never learns a camera exists. It receives "move left" — the same
    string the keyboard and the AI pipe submit. That is the whole point of the
    Experiment-B audience swap: change the mirilla, not the obra.

Two modes:
  --mode camera   (default) webcam + MediaPipe -> pose tokens. The lived leg.
                  Needs a webcam + `pip install opencv-python mediapipe`
                  (MediaPipe currently ships wheels for Python <= 3.12).
  --mode demo     no camera, no MediaPipe: emits a scripted sequence of pose
                  tokens so the seam can be exercised end-to-end anywhere
                  (any Python 3.x). Use this to see the domain advance without
                  hardware.

Pose vocabulary (must match GestureSource.Route in the C# side):
  palm_left   hand held to the LEFT third of the frame     -> left
  palm_right  hand held to the RIGHT third of the frame    -> right
  open        open palm, centred (5 fingers)               -> rotate
  point_down  index only, centred                          -> tick (soft drop)
  fist        closed fist, centred (0 fingers)             -> drop (hard drop)
  peace       index + middle, centred (2 fingers)          -> quit

Usage:
  # 1) start the host first (it listens):
  #    dotnet run --project input/TetrisStage.csproj -- g1 --sources gesture,clock --clock-ms 800
  # 2) then run this sidecar (it connects):
  python gesture/gesture-cam.py --mode camera --port 5111
  python gesture/gesture-cam.py --mode demo   --port 5111
"""

import argparse
import socket
import sys
import time


# --- transport -------------------------------------------------------------
# The sidecar is the CLIENT; GestureSource is the SERVER (TcpListener). We
# connect, then stream newline-delimited pose tokens. On a dropped connection
# we retry, so the host can be restarted without restarting the camera.

class PoseLink:
    def __init__(self, host: str, port: int):
        self.host = host
        self.port = port
        self.sock: socket.socket | None = None

    def connect(self, retries: int = 60) -> None:
        for attempt in range(retries):
            try:
                s = socket.create_connection((self.host, self.port), timeout=2)
                s.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                self.sock = s
                print(f"[gesture-cam] connected to {self.host}:{self.port}", flush=True)
                return
            except OSError:
                if attempt == 0:
                    print(f"[gesture-cam] waiting for host on {self.host}:{self.port} ...", flush=True)
                time.sleep(0.5)
        raise SystemExit(f"[gesture-cam] could not connect to {self.host}:{self.port}")

    def send(self, pose: str) -> None:
        if self.sock is None:
            return
        try:
            self.sock.sendall((pose + "\n").encode("ascii"))
            print(f"[gesture-cam] -> {pose}", flush=True)
        except OSError:
            print("[gesture-cam] host disconnected", flush=True)
            self.sock = None

    def close(self) -> None:
        if self.sock is not None:
            try:
                self.sock.close()
            finally:
                self.sock = None


# --- demo mode (no hardware) ------------------------------------------------
# A scripted stroll through every route so the seam is verifiable without a
# camera. Deliberately does NOT send `peace` (quit) so the caller controls
# termination (Ctrl-C, or --quit to append it).

DEMO_SCRIPT = [
    ("palm_left", 0.6), ("palm_left", 0.6),
    ("open", 0.6),                                # rotate
    ("palm_right", 0.6), ("palm_right", 0.6),
    ("point_down", 0.4),                          # soft drop
    ("fist", 0.8),                                # hard drop -> lands, host spawns next
    ("palm_left", 0.6),
    ("open", 0.6),
    ("fist", 0.8),
]


def run_demo(link: PoseLink, loops: int, quit_at_end: bool) -> None:
    for i in range(loops):
        print(f"[gesture-cam] demo loop {i + 1}/{loops}", flush=True)
        for pose, hold in DEMO_SCRIPT:
            link.send(pose)
            time.sleep(hold)
            if link.sock is None:
                return
    if quit_at_end:
        link.send("peace")  # -> quit


# --- camera mode (webcam + MediaPipe) ---------------------------------------

def classify(landmarks, handedness_label: str) -> str | None:
    """Map 21 hand landmarks to a pose token. Position picks left/right; finger
    count picks the centred gestures. This is a deliberately simple starting
    classifier — the recognition quality is NOT the experiment's claim (the seam
    is); tune the thresholds/gestures freely without touching the C# side."""
    # Centroid x in [0,1] (image is mirrored for a natural "mirror" feel by the
    # caller, so left-on-screen == user's left).
    xs = [lm.x for lm in landmarks]
    cx = sum(xs) / len(xs)

    if cx < 0.35:
        return "palm_left"
    if cx > 0.65:
        return "palm_right"

    # Centred: classify by which fingers are extended.
    # Landmark indices: tips 4,8,12,16,20; pip joints 3,6,10,14,18.
    tips = [4, 8, 12, 16, 20]
    pips = [3, 6, 10, 14, 18]
    extended = []
    # Thumb: extended if tip is horizontally outside the ip joint (handedness).
    if handedness_label == "Right":
        extended.append(landmarks[4].x < landmarks[3].x)
    else:
        extended.append(landmarks[4].x > landmarks[3].x)
    # Other four fingers: extended if tip is above (smaller y than) the pip.
    for tip, pip in zip(tips[1:], pips[1:]):
        extended.append(landmarks[tip].y < landmarks[pip].y)

    count = sum(1 for e in extended if e)
    index_only = extended[1] and not any(extended[i] for i in (0, 2, 3, 4))
    peace = extended[1] and extended[2] and not extended[3] and not extended[4]

    if count == 0:
        return "fist"          # -> drop
    if count >= 5:
        return "open"          # -> rotate
    if peace:
        return "peace"         # -> quit
    if index_only:
        return "point_down"    # -> tick (soft drop)
    return None                # ambiguous: emit nothing (route table drops it)


def run_camera(link: PoseLink, cam_index: int, stable_frames: int, repeat_ms: int) -> None:
    try:
        import cv2
        import mediapipe as mp
    except ImportError as e:
        raise SystemExit(
            "[gesture-cam] camera mode needs opencv-python + mediapipe:\n"
            "    pip install opencv-python mediapipe\n"
            "  (MediaPipe currently ships wheels for Python <= 3.12; if you are on\n"
            "   a newer Python, use a 3.12 venv, or run --mode demo to test the seam.)\n"
            f"  import error: {e}")

    hands = mp.solutions.hands.Hands(
        model_complexity=0, max_num_hands=1,
        min_detection_confidence=0.6, min_tracking_confidence=0.5)
    cap = cv2.VideoCapture(cam_index)
    if not cap.isOpened():
        raise SystemExit(f"[gesture-cam] could not open webcam index {cam_index}")

    print("[gesture-cam] camera up — show a hand. Ctrl-C to stop.", flush=True)
    last_pose: str | None = None
    stable = 0
    last_emit = 0.0
    try:
        while True:
            ok, frame = cap.read()
            if not ok:
                continue
            frame = cv2.flip(frame, 1)  # mirror for a natural feel
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            result = hands.process(rgb)

            pose: str | None = None
            if result.multi_hand_landmarks:
                lms = result.multi_hand_landmarks[0].landmark
                label = "Right"
                if result.multi_handedness:
                    label = result.multi_handedness[0].classification[0].label
                pose = classify(lms, label)

            # Debounce: require the pose to persist a few frames, then rate-limit
            # repeats so a held gesture shuttles the piece without flooding.
            now = time.monotonic()
            if pose is not None and pose == last_pose:
                stable += 1
            else:
                stable = 1
                last_pose = pose
            if (pose is not None and stable >= stable_frames
                    and (now - last_emit) * 1000 >= repeat_ms):
                link.send(pose)
                last_emit = now
                if pose == "peace":
                    break
                if link.sock is None:
                    break
    finally:
        cap.release()
        hands.close()


def main() -> int:
    ap = argparse.ArgumentParser(description="Tetris gesture camera sidecar")
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=5111)
    ap.add_argument("--mode", choices=["camera", "demo"], default="camera")
    ap.add_argument("--cam-index", type=int, default=0)
    ap.add_argument("--stable-frames", type=int, default=3,
                    help="frames a pose must persist before it is emitted")
    ap.add_argument("--repeat-ms", type=int, default=180,
                    help="min ms between repeated emissions of a held pose")
    ap.add_argument("--loops", type=int, default=1, help="demo mode: script repetitions")
    ap.add_argument("--quit", action="store_true", help="demo mode: send quit at the end")
    args = ap.parse_args()

    link = PoseLink(args.host, args.port)
    link.connect()
    try:
        if args.mode == "demo":
            run_demo(link, args.loops, args.quit)
        else:
            run_camera(link, args.cam_index, args.stable_frames, args.repeat_ms)
    except KeyboardInterrupt:
        print("\n[gesture-cam] stopped.", flush=True)
    finally:
        link.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
