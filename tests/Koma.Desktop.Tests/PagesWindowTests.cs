using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Koma.TestSupport;

namespace Koma.Desktop.Tests;

/// <summary>
/// The pages of a publication, filled from a package on disk.
/// </summary>
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
