using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Tetris.Input;

/// <summary>
/// Input source whose medium is a SINGLE PHYSICAL BUTTON on a scarce device (an
/// emulated ESP32-C6). Transport: a loopback TCP line-stream server — the same
/// server role and per-connection loop as <see cref="PipeSource"/> /
/// <see cref="GestureSource"/> (a WiFi-class C6 reaches the host over a socket).
/// Routing: a fixed grammar (this source's route table) turning each raw button
/// EVENT into a logical command, exactly as <see cref="KeyboardSource"/> maps a
/// raw <see cref="ConsoleKey"/>.
/// <para>
/// One button cannot press six keys, so the audience is genuinely constrained —
/// yet it drives the SAME obra. The device firmware distinguishes press patterns
/// (tap / double / triple / hold / long-hold) and emits the event name; this
/// source's grammar turns each into a verb. The DOMAIN receives
/// <c>rotate</c> / <c>right</c> / <c>left</c> / <c>drop</c> — the same strings the
/// keyboard submits — and never learns the audience had only one button.
/// </para>
/// <para>
/// Button-event → logical-command route table (the one-button grammar):
/// <list type="bullet">
/// <item><c>tap</c> → <c>rotate</c></item>
/// <item><c>double</c> → <c>right</c></item>
/// <item><c>triple</c> → <c>left</c></item>
/// <item><c>hold</c> → <c>drop</c> (hard drop)</item>
/// <item><c>longhold</c> → <c>quit</c></item>
/// </list>
/// Unknown events are dropped — the route table simply has no entry for them.
/// </para>
/// </summary>
public sealed class ButtonSource : IInputSource
{
    private readonly int port;

    public ButtonSource(int port) => this.port = port;

    public string Name => "button";

    public void Run(Action<string> submit, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        using var _ = ct.Register(() => { try { listener.Stop(); } catch { } });

        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = listener.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    return; // listener stopped by cancellation
                }
                catch (InvalidOperationException)
                {
                    return;
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
                    return StopReason.ClientGone;
                }
                catch (ObjectDisposedException)
                {
                    return StopReason.ClientGone;
                }

                if (line is null)
                {
                    return StopReason.ClientGone; // device disconnected; await the next
                }

                // Routing: raw button event -> logical command. Unmapped events dropped.
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

    // The source's route table: one-button event "scancode" -> logical command.
    // The symmetric sibling of KeyboardSource's ConsoleKey switch.
    private static string? Route(string ev) => ev switch
    {
        "tap" => "rotate",
        "double" => "right",
        "triple" => "left",
        "hold" => "drop",     // hard drop
        "longhold" => "quit",
        _ => null,
    };
}
