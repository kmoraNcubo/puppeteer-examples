namespace Tetris;

/// <summary>
/// Which way a piece turns. The two directions are exact inverses: turning one
/// way and then the other leaves the piece in its original pose.
/// </summary>
internal enum RotationDirection
{
    /// <summary>A quarter-turn clockwise — the next pose in the cycle.</summary>
    Clockwise,

    /// <summary>A quarter-turn counter-clockwise — the previous pose in the cycle.</summary>
    CounterClockwise,
}
