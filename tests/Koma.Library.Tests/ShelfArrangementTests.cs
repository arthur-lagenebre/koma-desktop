using System.Globalization;

namespace Koma.Library.Tests;

/// <summary>
/// What the shelf shows and in what order: search, the three orders, and the
/// series a volume is filed under.
/// </summary>
public sealed class ShelfArrangementTests
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private static readonly LibraryEntry[] Shelf =
    [
        Entry("rivage-10.koma", "La marée", "Rivage", "10", opened: 3),
        Entry("666-03.koma", "Demonio fortissimo", "666", null),
        Entry("666-01.koma", "Ante demonium", "666", null),
        Entry("rivage-2.koma", "Le port", "Rivage", "2", opened: 1),
        Entry("rivage-hs1.koma", "Hors-série", "Rivage", "HS1"),
        Entry("rivage-extra.koma", "Carnet", "Rivage", null),
        Entry("aube-1.koma", "Première lueur", "Aube", "1", opened: 2),
        Entry("eleve.koma", "Élève", null, null),
        Entry("broken.koma", null, null, null, unreadable: "mimetype-content — …")
    ];

    [Fact]
    public void FilesEachSeriesInTheOrderOfItsNumbers()
    {
        IReadOnlyList<ShelfGroup> groups = ShelfArrangement.Arrange(Shelf, null, ShelfOrder.Series, French);

        string?[] headings = ["666", "Aube", "Rivage", ShelfArrangement.NoSeries, ShelfArrangement.Unreadable];
        Assert.Equal(headings, groups.Select(g => g.Heading));

        // 2 before 10, as a reader counts; a special after the numbers; an
        // unnumbered volume last.
        string[] rivage = ["Le port", "La marée", "Hors-série", "Carnet"];
        Assert.Equal(rivage, groups[2].Entries.Select(e => e.Title));

        // A series whose volumes carry no number is filed by the numbers in
        // their file names, which is where a converted collection keeps them.
        string[] numbered = ["Ante demonium", "Demonio fortissimo"];
        Assert.Equal(numbered, groups[0].Entries.Select(e => e.Title));
    }

    [Fact]
    public void SortsByTitleWithTheUnreadableLastUnderTheirOwnHeading()
    {
        IReadOnlyList<ShelfGroup> groups = ShelfArrangement.Arrange(Shelf, null, ShelfOrder.Title, French);

        string[] titles = ["Ante demonium", "Carnet", "Demonio fortissimo", "Élève", "Hors-série", "La marée", "Le port", "Première lueur"];
        Assert.Equal(titles, groups[0].Entries.Select(e => e.Title));
        Assert.Null(groups[0].Heading);
        Assert.Equal(ShelfArrangement.Unreadable, groups[1].Heading);
    }

    [Fact]
    public void PutsWhatWasReadLastFirst()
    {
        IReadOnlyList<ShelfGroup> groups = ShelfArrangement.Arrange(Shelf, null, ShelfOrder.RecentlyRead, French);

        string[] read = ["La marée", "Première lueur", "Le port"];
        Assert.Equal(read, groups[0].Entries.Take(3).Select(e => e.Title));
    }

    [Theory]
    [InlineData("eleve", "Élève")]
    [InlineData("MARÉE", "La marée")]
    [InlineData("aube", "Première lueur")]
    [InlineData("hs1", "Hors-série")]
    public void FindsATitleASeriesOrAFileCaseAndAccentsAside(string query, string title)
    {
        // "eleve" finds "Élève"; "aube" finds its series; "hs1" its file.
        IReadOnlyList<ShelfGroup> groups = ShelfArrangement.Arrange(Shelf, query, ShelfOrder.Title, French);

        Assert.Equal(title, Assert.Single(Assert.Single(groups).Entries).Title);
    }

    [Fact]
    public void ShowsNothingRatherThanEmptyHeadingsWhenNothingMatches()
    {
        Assert.Empty(ShelfArrangement.Arrange(Shelf, "zzz", ShelfOrder.Series, French));
    }

    private static LibraryEntry Entry(string file, string? title, string? series, string? position, int? opened = null, string? unreadable = null) => new()
    {
        Path = Path.Combine("books", file),
        Size = 1,
        Modified = DateTimeOffset.UnixEpoch,
        Title = title,
        Series = series,
        SeriesPosition = position,
        Unreadable = unreadable,
        LastOpened = opened is null ? null : DateTimeOffset.UnixEpoch.AddDays(opened.Value)
    };
}
