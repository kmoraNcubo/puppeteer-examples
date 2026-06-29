using System;

namespace Tetris;

/// <summary>
/// Thrown when a well transition would leave the board in a state that
/// contradicts its invariants — an active piece overlapping an occupied
/// figure, a cell outside the frame, or a pile that kept a complete row. In
/// correct play it never fires; it exists so that a modelling mistake surfaces
/// immediately and loudly instead of corrupting the game silently.
/// </summary>
internal sealed class WellInvariantException : Exception
{
    public WellInvariantException(string message) : base(message)
    {
    }
}
