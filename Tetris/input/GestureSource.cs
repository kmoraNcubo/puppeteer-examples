using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Tetris.Input;

/// <summary>
/// Input source whose medium is a CAMERA. Transport: a loopback TCP line-stream
/// server (127.0.0.1:&lt;port&gt;) that a webcam sidecar connects to and streams
/// hand-POSE tokens over — the server role and per-connection loop are the same
/// shape as <see cref="PipeSource"/>. Routing: a fixed pose-map (this source's
/// route table) turning each raw pose signal into a logical command, exactly as
/// <see cref="KeyboardSource"/> maps a raw <see cref="ConsoleKey"/>.
/// <para>
/// The camera is, to the automaton, just another keyboard: MediaPipe recognises a
/// hand shape and emits its "scancode" (a pose token like <c>palm_left</c>); this
/// source's keymap turns that scancode into a verb. The gesture recognition lives
/// entirely in the sidecar (the "camera hardware"); the DOMAIN receives
/// <c>left</c> / <c>right</c> / <c>rotate</c> / … and never learns a camera exists.
/// </para>
/// <para>
/// Pose → logical-command route table (the mirror of <c>KeyboardSource.ApplyKey</c>):
/// <list type="bullet">
/// <item><c>palm_left</c> → <c>left</c></item>
/// <item><c>palm_right</c> → <c>right</c></item>
/// <item><c>open</c> (open palm) → <c>rotate</c></item>
/// <item><c>point_down</c> → <c>tick</c> (soft drop)</item>
/// <item><c>fist</c> → <c>drop</c> (hard drop)</item>
/// <item><c>peace</c> → <c>quit</c></item>
/// </list>
/// Unknown tokens are dropped — the route table simply has no entry for them,
/// just as <see cref="KeyboardSource"/> drops unmapped keys.
/// </para>
/// </summary>
public sealed class GestureSource : IInputSource
{
    private readonly int port;

    public GestureSource(int port) => this.port = port;

    public string Name => "gesture";

    public void Run(Action<string> submit, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        // Closing the listener is what unblocks a pending AcceptTcpClient when the
        // token fires (mirrors PipeSource closing on cancellation).
        using var _ = ct.Register(() => { try { listener.Stop(); } catch { } });

        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    // Block until the sidecar connects (or cancellation stops the
                    // listener). The dedicated-thread contract of IInputSource makes
                    // synchronously waiting here intentional and safe.
                    client = listener.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    return; // listener stopped by cancellation
                }
                catch (InvalidOperationException)
                {
                    return; // listener already stopped
                }

                if (Serve(client, submit, ct) == StopReason.Quit)
                {
                    return;
                }
            }
        }
        finally
        {
            try { listener.Stop(); } catch { }
        }
    }

    private enum StopReason { ClientGone, Quit }

    // One persistent connection carries a NEWLINE-DELIMITED stream of pose tokens
    // (unlike PipeSource's one-command-per-connection, a camera streams). Reading
    // is cancellation-responsive because the token closes the client, unblocking
    // the blocking ReadLine.
    private StopReason Serve(TcpClient client, Action<string> submit, CancellationToken ct)
    {
        using (client)
        using (ct.Register(() => { try { client.Close(); } catch { } }))
        {
            client.NoDelay = true;
            using var reader = new StreamReader(client.GetStream());

            while (!ct.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = reader.ReadLine();
                }
                catch (IOException)
                {
                    return StopReason.ClientGone; // client dropped or cancelled
                }
                catch (ObjectDisposedException)
                {
                    return StopReason.ClientGone;
                }

                if (line is null)
                {
                    return StopReason.ClientGone; // sidecar disconnected; await the next
                }

                // Routing: raw pose signal -> logical command. Unmapped poses are
                // dropped (the route table simply has no entry for them).
                var command = Route(line.Trim().ToLowerInvariant());
                if (command is null)
                {
                    continue;
                }

                submit(command);
                if (command == "quit")
                {
                    return StopReason.Quit;
                }
            }

            return StopReason.ClientGone;
        }
    }

    // The source's route table: hand-pose "scancode" -> logical command. The
    // symmetric sibling of KeyboardSource's ConsoleKey switch.
    private static string? Route(string pose) => pose switch
    {
        "palm_left" => "left",
        "palm_right" => "right",
        "open" => "rotate",
        "point_down" => "tick",  // soft drop
        "fist" => "drop",         // hard drop
        "peace" => "quit",
        _ => null,
    };
}
