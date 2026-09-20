using Koma.Core.Rendering;

namespace Koma.Core.Tests;

/// <summary>
/// The on-screen placement of a spread, from declared sizes alone.
/// </summary>
public sealed class SpreadLayoutTests
{
    private const int Precision = 6;

    private static readonly Dictionary<string, (int Width, int Height)> Sizes = new()
    {
        ["portrait"] = (800, 1200),
        ["landscape"] = (1600, 1200)
    };

    [Fact]
    public void Whole_FitsTheViewportAndCentres()
    {
        PlacedItem placed = Assert.Single(Arrange(new SingleSpread("portrait"), 1000, 1000));

        AssertBounds(new LayoutRect(500 - 1000.0 / 3, 0, 2000.0 / 3, 1000), placed.Bounds);
        Assert.False(placed.IsEmptyHalf);
    }

    [Fact]
    public void Pair_MeetsAtTheMiddle()
    {
        IReadOnlyList<PlacedItem> placed = Arrange(new PairSpread("portrait", "portrait"), 2000, 1000);

        AssertBounds(new LayoutRect(1000 - 2000.0 / 3, 0, 2000.0 / 3, 1000), placed[0].Bounds);
        AssertBounds(new LayoutRect(1000, 0, 2000.0 / 3, 1000), placed[1].Bounds);
    }

    [Fact]
    public void Pair_KeepsTheGutterInTheMiddleForUnlikeWidths()
    {
        // The wide half fills its side first, and the narrow one follows it
        // down to the same height rather than pulling the gutter over.
        IReadOnlyList<PlacedItem> placed = Arrange(new PairSpread("landscape", "portrait"), 2000, 1000);

        AssertBounds(new LayoutRect(0, 125, 1000, 750), placed[0].Bounds);
        AssertBounds(new LayoutRect(1000, 125, 500, 750), placed[1].Bounds);
    }

    [Fact]
    public void Pair_GivesAnEmptyHalfTheSizeAndBackgroundOfItsCompanion()
    {
        IReadOnlyList<PlacedItem> placed = Arrange(new PairSpread(null, "portrait"), 2000, 1000);

        Assert.Equal(("portrait", true), (placed[0].Item, placed[0].IsEmptyHalf));
        Assert.Equal(("portrait", false), (placed[1].Item, placed[1].IsEmptyHalf));
        Assert.Equal(placed[1].Bounds.Width, placed[0].Bounds.Width, Precision);
    }

    [Theory]
    [InlineData(1200, 800, true)]
    [InlineData(800, 1200, false)]
    [InlineData(1000, 1000, false)]
    public void FitsTwo_OnlyWhenWiderThanTall(double width, double height, bool fitsTwo)
    {
        Assert.Equal(fitsTwo, SpreadLayout.FitsTwo(width, height));
    }

    [Fact]
    public void Items_ListsWhatIsShownAndSkipsAnEmptyHalf()
    {
        string[] half = ["portrait"];
        string[] both = ["landscape", "portrait"];

        Assert.Equal(half, SpreadLayout.Items(new PairSpread(null, "portrait")));
        Assert.Equal(both, SpreadLayout.Items(new PairSpread("landscape", "portrait")));
    }

    private static IReadOnlyList<PlacedItem> Arrange(Spread spread, double width, double height) => SpreadLayout.Arrange(spread, item => Sizes[item], width, height);

    private static void AssertBounds(LayoutRect expected, LayoutRect actual)
    {
        Assert.Equal(expected.X, actual.X, Precision);
        Assert.Equal(expected.Y, actual.Y, Precision);
        Assert.Equal(expected.Width, actual.Width, Precision);
        Assert.Equal(expected.Height, actual.Height, Precision);
    }
}
