using Koma.Core.Model;
using Koma.Core.Packaging;
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
    public void NumbersTheCorpusPublicationInReadingOrder()
    {
        // valid-page-list reads right to left, pairs its first two story pages
        // and ends on a two-page spread labelled half by half: the labels of
        // its spreads, taken in turn, must count up.
        using FileStream file = File.OpenRead(Path.Combine(ConformanceCorpusTests.CorpusRoot(), "packages", "valid-page-list.koma"));
        PackageOpenResult result = PackageOpener.Open(file, leaveOpen: true);

        using KomaPackage? package = result.Package;
        Assert.NotNull(package);
        Assert.NotNull(package.Navigation);

        string[] expected = ["i", "1", "2", "3", "4"];
        IEnumerable<string> labels = package.Paginate(viewportFitsTwo: true).SelectMany(spread => PageLabels.Of(spread, package.Navigation.PageList, package.Metadata.Direction));

        Assert.Equal(expected, labels);
    }

    [Fact]
    public void SkipsAnEmptyHalfAndAnUnlabelledItem()
    {
        Assert.Empty(PageLabels.Of(new PairSpread(null, "p001"), PageList, ReadingDirection.LeftToRight));
    }
}
