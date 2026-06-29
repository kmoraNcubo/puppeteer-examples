using System;

namespace Tetris;

/// <summary>
/// Thrown when a move verb is invoked on a well whose game has already ended.
/// Operating on a finished game is an invalid request, distinct from a move
/// that is merely blocked (which is a valid no-op). A caller is expected to
/// check the well's game-over query before acting; the well fails fast and is
/// left unchanged.
/// </summary>
internal sealed class GameOverException : Exception
{
    public GameOverException(string message) : base(message)
    {
    }
}
