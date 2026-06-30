using System;
using System.IO;
using System.Linq;

namespace Tetris.Acting;

/// <summary>
/// Resolves the on-disk journal directory for a persistent session. The AI CLI
/// (writer) and the observer (reader) both compute it the same way from a shared,
/// machine-stable root, so a session id names the same journal across the two
/// separate processes.
/// </summary>
public static class SessionPaths
{
    /// <summary>The root under which every session's journal directory lives.</summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "tetris-sessions");

    /// <summary>The journal directory for <paramref name="session"/> (one subdirectory per id).</summary>
    public static string For(string session) => Path.Combine(Root, Safe(session));

    /// <summary>
    /// The live-frame file for <paramref name="session"/> — the ephemeral screen
    /// the push sink overwrites and the viewer watches. Kept beside (not inside)
    /// the journal directory so a <see cref="System.IO.FileSystemWatcher"/> sees a
    /// simple single-file change.
    /// </summary>
    public static string FrameFile(string session) => Path.Combine(Root, Safe(session) + ".frame");

    private static string Safe(string session)
    {
        var safe = string.Concat(session.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        if (safe.Length == 0)
        {
            throw new ArgumentException("Session id must contain at least one usable character.", nameof(session));
        }

        return safe;
    }
}
