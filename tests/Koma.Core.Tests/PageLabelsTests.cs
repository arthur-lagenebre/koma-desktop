using Koma.Core.Model;
using Koma.Core.Rendering;

namespace Koma.Core.Tests;

/// <summary>
/// Page-list labels gathered for a spread, in the order it is read.
/// </summary>
public sealed class PageLabelsTests
{
    private static readonly PageTarget[] PageList =
    [
        new("p002", "1", null),
        new("p003", "2", null),
        new("p004", "3", PhysicalSide.Left),
        new("p004", "4", PhysicalSide.Right)
    ];

    [Fact]
    public void ReadsAPairLeftToRight()
    {
        string[] expected = ["1", "2"];

        Assert.Equal(expected, PageLabels.Of(new PairSpread("p002", "p003"), PageList, ReadingDirection.LeftToRight));
    }

    [Fact]
    public void ReadsAPairRightToLeft()
    {
        // In a right-to-left publication the right half comes first.
        string[] expected = ["2", "1"];

        Assert.Equal(expected, PageLabels.Of(new PairSpread("p002", "p003"), PageList, ReadingDirection.RightToLeft));
    }

    [Theory]
    [InlineData(ReadingDirection.LeftToRight, "3", "4")]
    [InlineData(ReadingDirection.RightToLeft, "4", "3")]
    public void ReadsTheHalvesOfADoublePageInReadingOrder(ReadingDirection direction, string first, string second)
    {
        string[] expected = [first, second];

        Assert.Equal(expected, PageLabels.Of(new CenteredSpread("p004"), PageList, direction));
    }

    [Fact]
    public void SkipsAnEmptyHalfAndAnUnlabelledItem()
    {
        Assert.Empty(PageLabels.Of(new PairSpread(null, "p001"), PageList, ReadingDirection.LeftToRight));
    }
}
