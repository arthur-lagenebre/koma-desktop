namespace Koma.Desktop.Tests;

/// <summary>
/// Where an import writes what it converts.
/// </summary>
public sealed class ImportDestinationTests
{
    [Fact]
    public void WritesBesideEachArchiveWhenNoFolderIsChosen()
    {
        string[] archives = [Path.Combine("books", "666", "1 - Ante demonium.cbz")];

        (_, string koma) = MainWindow.Destinations(archives, root: null, destination: null).Single();

        Assert.Equal(Path.Combine("books", "666", "1 - Ante demonium.koma"), koma);
    }

    [Fact]
    public void KeepsTheTreeAFolderOfArchivesSatIn()
    {
        // A collection is filed in folders, and flattening eighty volumes
        // into one folder would lose that filing.
        string root = Path.Combine("books");
        string[] archives =
        [
            Path.Combine("books", "666", "1 - Ante demonium.cbz"),
            Path.Combine("books", "Motor Girl.cbz")
        ];

        (string Cbz, string Koma)[] written = MainWindow.Destinations(archives, root, Path.Combine("out"));

        Assert.Equal(Path.Combine("out", "666", "1 - Ante demonium.koma"), written[0].Koma);
        Assert.Equal(Path.Combine("out", "Motor Girl.koma"), written[1].Koma);
    }

    [Fact]
    public void PutsChosenFilesStraightIntoTheFolder()
    {
        // Files chosen one by one have no tree to keep: they may come from
        // anywhere, and nothing was said about where they sat.
        string[] archives = [Path.Combine("elsewhere", "deep", "Lilith.cbz")];

        (_, string koma) = MainWindow.Destinations(archives, root: null, destination: Path.Combine("out")).Single();

        Assert.Equal(Path.Combine("out", "Lilith.koma"), koma);
    }
}
