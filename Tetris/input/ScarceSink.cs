using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Puppeteer;
using Tetris.Acting;

namespace Tetris.Input;

/// <summary>
/// An <see cref="IOutputSink"/> for a SCARCE device (an emulated ESP32-C6 with a
/// tiny screen). It is the exact same output seam as
/// <see cref="Tetris.Acting.FrameFileSink"/> / <c>WebSocketSink</c> / <c>SseSink</c>
/// — only the WIRE and the FORMAT change. The mirilla is deliberately narrow:
/// where the other sinks forward the engine's JSON frame (hundreds of bytes), this
/// one repacks the SAME projection into a minimal row-bitmask frame (tens of
/// bytes), the working set a device with kilobytes of RAM can actually hold. The
/// obra does not change; only the instrument through which this audience sees it.
/// <para>
/// Transport: a loopback TCP server the device connects to (a WiFi C6 receiving
/// its screen). This is a SEPARATE endpoint from <see cref="ButtonSource"/>'s
/// input port — <c>InputSource ≠ OutputTarget</c>, made physical, as in the
/// REST/SSE lab (input POST vs output SSE).
/// </para>
/// <para>
/// Packed frame line (ASCII, newline-terminated):
/// <c>S1;W;H;cleared;flagsHex;rowsHex</c> where <c>flagsHex</c> is one nibble
/// (bit0 = awaiting, bit1 = game-over) and <c>rowsHex</c> is H row masks
/// concatenated, each ceil(W/4) hex digits, bit <c>c</c> set ⇔ column <c>c</c>
/// occupied. For the 10×20 well that is a ~70-byte line vs the ~300-byte JSON.
/// </para>
/// </summary>
public sealed class ScarceSink : IOutputSink, IDisposable
{
    private readonly TcpListener listener;
    private readonly Thread acceptThread;
    private readonly CancellationTokenSource cts = new();
    private readonly object gate = new();

    private TcpClient? client;
    private string? lastFrame;   // cached so a device that connects mid-game gets the screen at once
    private long lastPackedBytes; // last frame size on the wire (for the scarcity measurement)

    public ScarceSink(int port)
    {
        listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "scarce-accept" };
        acceptThread.Start();
    }

    /// <summary>Size in bytes of the most recently pushed packed frame (0 if none yet).</summary>
    public long LastPackedBytes => Interlocked.Read(ref lastPackedBytes);

    // Accept devices one at a time; the newest connection becomes the screen and
    // immediately receives the cached current frame (a scarce device holds only the
    // latest frame, never history).
    private void AcceptLoop()
    {
        while (!cts.IsCancellationRequested)
        {
            TcpClient accepted;
            try
            {
                accepted = listener.AcceptTcpClient();
            }
            catch (SocketException)
            {
                return; // listener stopped
            }
            catch (InvalidOperationException)
            {
                return;
            }

            accepted.NoDelay = true;
            string? snapshot;
            lock (gate)
            {
                try { client?.Close(); } catch { }
                client = accepted;
                snapshot = lastFrame;
            }

            if (snapshot is not null)
            {
                TrySend(snapshot);
            }
        }
    }

    public void Push(in PushDocument document)
    {
        var snap = FrameDocument.Parse(document.Document);
        if (snap is null)
        {
            return; // an empty / unparseable frame: nothing to paint
        }

        var line = Pack(snap);
        Interlocked.Exchange(ref lastPackedBytes, Encoding.ASCII.GetByteCount(line));

        lock (gate)
        {
            lastFrame = line;
        }
        TrySend(line);
    }

    private void TrySend(string line)
    {
        lock (gate)
        {
            if (client is null)
            {
                return; // no device attached yet; the frame is cached for when one is
            }

            try
            {
                var bytes = Encoding.ASCII.GetBytes(line);
                client.GetStream().Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex) when (ex is System.IO.IOException or ObjectDisposedException or InvalidOperationException)
            {
                // Best-effort, like FrameFileSink: drop the frame and the dead client;
                // the next push (or reconnect) repaints from fresh state.
                try { client.Close(); } catch { }
                client = null;
            }
        }
    }

    // Repack the SAME projection into the minimal row-bitmask frame.
    private static string Pack(WellSnapshot snap)
    {
        var occupied = new bool[snap.Height, snap.Width];
        foreach (var cell in snap.Occupied)
        {
            if (cell.Row >= 0 && cell.Row < snap.Height && cell.Column >= 0 && cell.Column < snap.Width)
            {
                occupied[cell.Row, cell.Column] = true;
            }
        }

        var hexDigits = (snap.Width + 3) / 4;
        var flags = (snap.IsGameOver ? 2 : 0) | (snap.IsAwaitingPiece ? 1 : 0);

        var sb = new StringBuilder();
        sb.Append("S1;").Append(snap.Width).Append(';').Append(snap.Height).Append(';')
          .Append(snap.ClearedLines).Append(';').Append(flags.ToString("X")).Append(';');

        for (var row = 0; row < snap.Height; row++)
        {
            var mask = 0;
            for (var col = 0; col < snap.Width; col++)
            {
                if (occupied[row, col])
                {
                    mask |= 1 << col;
                }
            }

            sb.Append(mask.ToString("X" + hexDigits));
        }

        sb.Append('\n');
        return sb.ToString();
    }

    public void Dispose()
    {
        cts.Cancel();
        try { listener.Stop(); } catch { }
        lock (gate)
        {
            try { client?.Close(); } catch { }
            client = null;
        }
        try { acceptThread.Join(TimeSpan.FromSeconds(1)); } catch { }
        cts.Dispose();
    }
}
