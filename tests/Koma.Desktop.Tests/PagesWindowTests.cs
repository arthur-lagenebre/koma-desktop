using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Koma.TestSupport;

namespace Koma.Desktop.Tests;

/// <summary>
/// The pages of a publication, filled from a package on disk.
/// </summary>
[Collection(DrawnSuites.Name)]
public sealed class PagesWindowTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("koma-pages-window").FullName;

    [AvaloniaFact]
    public void ListsThePagesOfAPublicationAndFillsTheFormFromTheOneChosen()
    {
        string path = Path.Combine(folder, "valid-page-list.koma");
        File.Copy(Corpus.Package("valid-page-list.koma"), path);

        var window = new PagesWindow(path, "p002");
        window.Show();

        ListBox pages = window.GetVisualDescendants().OfType<ListBox>().First();
        ComboBox role = window.GetVisualDescendants().OfType<ComboBox>().First();

        // The four pages of the package, the second chosen, and the role it
        // carries in the manifest.
        Assert.Equal(4, pages.ItemCount);
        Assert.Equal(1, pages.SelectedIndex);
        Assert.Equal("story", role.SelectedItem);
    }

    [AvaloniaFact]
    public async Task ShowsThePagesThemselvesBesideTheirLines()
    {
        // A reader editing p014 wants to see p014, not the word p014. The
        // pages are deflated here, as a converted library's are, and not
        // stored as the corpus packages keep them: a decoder seeks, so the
        // bytes have to be in hand before it is asked to.
        string path = Path.Combine(folder, "deflated.koma");
        Deflated(Corpus.Package("valid-page-list.koma"), path);

        var window = new PagesWindow(path, null);
        window.Show();

        await window.Reading;

        // The pages that were read, not the controls the list has drawn:
        // headless realises nothing until a layout asks for it, and a list
        // realises only what fits on screen anyway.
        Assert.Equal(4, window.Pictures);
        Assert.Equal(4, window.GetVisualDescendants().OfType<ListBox>().First().ItemCount);
    }

    [AvaloniaFact]
    public void AsksForTheSecondPrintedNumberOnlyWhereThereCanBeOne()
    {
        // §9.2 lets a target name a half of a spread, and only a page drawn
        // across one has halves to name.
        string path = Path.Combine(folder, "valid-page-list.koma");
        File.Copy(Corpus.Package("valid-page-list.koma"), path);

        var window = new PagesWindow(path, "p002");
        window.Show();

        Assert.False(window.Input<TextBox>(Text.Of("Number printed on its right half")).IsEffectivelyVisible);

        // p004 is the one that fills its spread.
        window.GetVisualDescendants().OfType<ListBox>().First().SelectedIndex = 3;

        Assert.True(window.Input<TextBox>(Text.Of("Number printed on its right half")).IsEffectivelyVisible);
        Assert.Equal("3", window.Input<TextBox>(Text.Of("Number printed on the page")).Text);
    }

    /// <summary>
    /// The same package with every entry deflated rather than stored, which
    /// is what a conversion writes and what the corpus does not.
    /// </summary>
    private static void Deflated(string source, string destination)
    {
        using ZipArchive from = ZipFile.OpenRead(source);
        using var to = new ZipArchive(File.Create(destination), ZipArchiveMode.Create);

        foreach (ZipArchiveEntry entry in from.Entries)
        {
            // §2.1 keeps the mimetype stored and first; everything else is
            // deflated, as a real package has it.
            ZipArchiveEntry written = to.CreateEntry(entry.FullName, entry.FullName == "mimetype" ? CompressionLevel.NoCompression : CompressionLevel.Optimal);

            using Stream read = entry.Open();
            using Stream write = written.Open();

            read.CopyTo(write);
        }
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
}
