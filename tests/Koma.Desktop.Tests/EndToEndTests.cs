using System.IO.Compression;
using Koma.Core.Importing;
using Koma.Core.Packaging;
using Koma.Core.Rendering;
using Koma.Core.Writing;
using Koma.TestSupport;

namespace Koma.Desktop.Tests;

/// <summary>
/// The two long pieces of work, run over real files: an edit written into
/// several publications, and an archive converted into one.
/// </summary>
/// <remarks>
/// The windows that ask for them are covered elsewhere; what is covered here
/// is what happens afterwards, which is where a file is either right or
/// ruined. No window is opened, so these are plain facts about the work
/// rather than about the interface.
/// </remarks>
[Collection(DrawnSuites.Name)]
public sealed class EndToEndTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("koma-end-to-end").FullName;

    [Fact]
    public void WritesOneEditIntoThreePublications()
    {
        string[] volumes = [Copy("un.koma"), Copy("deux.koma"), Copy("trois.koma")];
        var plan = new BatchEdit("Rivage", "3", NumberFrom: 1, Language: null, Direction: ReadingDirection.RightToLeft, Accessibility: null);
        var lines = new List<string>();

        (int changed, int refused) = MainWindow.EditTogether(volumes, plan, new Progress<(int Done, string Line)>(step => lines.Add(step.Line)));

        Assert.Equal((3, 0), (changed, refused));

        // The numbering follows the order the shelf showed, which is the
        // order the paths were given in.
        for (int i = 0; i < volumes.Length; i++)
        {
            MetadataEdit written = PublicationEditor.Current(volumes[i]);

            Assert.Equal("Rivage", written.Series!.Name);
            Assert.Equal((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), written.Series.Position);
            Assert.Equal(ReadingDirection.RightToLeft, written.Direction);
        }
    }

    [Fact]
    public void LeavesTheOthersAloneWhenOneRefuses()
    {
        // A volume that cannot take the edit stops itself, not the batch.
        string[] volumes = [Copy("un.koma"), Path.Combine(folder, "nowhere.koma"), Copy("trois.koma")];
        var plan = new BatchEdit(null, null, null, "fr", null, null);

        (int changed, int refused) = MainWindow.EditTogether(volumes, plan, new Progress<(int Done, string Line)>(_ => { }));

        Assert.Equal((2, 1), (changed, refused));
        Assert.Equal("fr", PublicationEditor.Current(volumes[2]).Language);
    }

    [Fact]
    public void ConvertsAnArchiveIntoAPublicationThatOpens()
    {
        string koma = Path.Combine(folder, "manga.koma");
        var steps = new List<string>();

        string report = MainWindow.Import(
            [(Corpus.Example("manga.cbz"), koma)],
            folders: [],
            new ConversionOptions(Checksums: true),
            new Progress<MainWindow.ImportStep>(step => steps.Add(step.Lines)));

        PackageOpenResult result = PackageOpener.Open(File.OpenRead(koma));

        using KomaPackage? package = result.Package;

        Assert.Equal(PackageOpenOutcome.Opened, result.Outcome);
        Assert.Equal("1 converted, 0 not converted.", report);

        // What a conversion assumed is said as it happens, not only at the end.
        Assert.Single(steps);
        Assert.Contains("manga.cbz", steps[0], StringComparison.Ordinal);

        // The package is written where it was asked for, and nowhere else.
        Assert.True(File.Exists(koma));
        Assert.False(File.Exists(Path.ChangeExtension(Corpus.Example("manga.cbz"), ".koma")));
    }

    [Fact]
    public void NeverWritesOverAPublicationThatIsThere()
    {
        string koma = Path.Combine(folder, "taken.koma");
        File.WriteAllText(koma, "not to be lost");

        string report = MainWindow.Import(
            [(Corpus.Example("bare.cbz"), koma)],
            folders: [],
            new ConversionOptions(),
            new Progress<MainWindow.ImportStep>(_ => { }));

        Assert.Equal("0 converted, 1 not converted.", report);
        Assert.Equal("not to be lost", File.ReadAllText(koma));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The operating system's to clean up.
        }
    }

    private string Copy(string name)
    {
        string path = Path.Combine(folder, name);
        File.Copy(Corpus.Package("valid-minimal.koma"), path);

        return path;
    }
}
