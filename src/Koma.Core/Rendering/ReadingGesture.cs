namespace Koma.Core.Rendering;

/// <summary>
/// Where a click lands in a publication's reading order.
/// </summary>
public static class ReadingGesture
{
    /// <summary>
    /// Whether a click at <paramref name="x"/> in a view of this width turns
    /// forward.
    /// </summary>
    /// <remarks>
    /// The half the pages are read towards moves forward, as the arrow keys
    /// do (§10.2): the right half of a left-to-right publication, the left
    /// half of a right-to-left one. A click exactly on the middle turns
    /// forward, so that a click always does something.
    /// </remarks>
    public static bool TurnsForward(double x, double width, ReadingDirection direction)
    {
        bool right = x >= width / 2;

        return direction == ReadingDirection.RightToLeft ? !right : right;
    }
}
