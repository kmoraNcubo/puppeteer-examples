using System;

namespace Tetris;

/// <summary>
/// Thrown when a piece's geometry would violate the defining invariant of a
/// tetromino — that it occupies exactly four distinct cells. Construction of a
/// malformed piece fails loudly rather than letting a degenerate shape leak
/// into the well.
/// </summary>
public sealed class InvalidPieceException : Exception
{
    public InvalidPieceException(string message) : base(message)
    {
    }
}
