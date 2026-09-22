using Koma.Core.Rendering;

namespace Koma.Core.Tests;

/// <summary>
/// Which way a click turns, in each direction of reading (§10.2).
/// </summary>
public sealed class ReadingGestureTests
{
    [Theory]
    [InlineData(900, ReadingDirection.LeftToRight, true)]
    [InlineData(100, ReadingDirection.LeftToRight, false)]
    [InlineData(900, ReadingDirection.RightToLeft, false)]
    [InlineData(100, ReadingDirection.RightToLeft, true)]
    [InlineData(500, ReadingDirection.LeftToRight, true)]
    public void TurnsTowardsTheHalfThatIsRead(double x, ReadingDirection direction, bool forward)
    {
        // The last case is the middle, which turns forward: a click always
        // does something.
        Assert.Equal(forward, ReadingGesture.TurnsForward(x, 1000, direction));
    }
}
