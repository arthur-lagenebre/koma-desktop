using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace Koma.Desktop.Tests;

/// <summary>
/// The two windows that ask before doing something: what to import and how,
/// and which folders the library watches.
/// </summary>
[Collection(DrawnSuites.Name)]
public sealed class ChoicesTests
{
    [AvaloniaFact]
    public void TheImportWindowCarriesWhatWasTicked()
    {
        var window = new ImportWindow();
        window.Show();

        Tick(window, "Keep the original ComicInfo.xml in each package", false);
        Tick(window, "Number the volumes from the start of their file names", true);

        window.FindButton(b => b.Content as string == "Choose a folder…").Press();

        Assert.NotNull(window.Chosen);
        Assert.True(window.Chosen!.Folder);
        Assert.False(window.Chosen.KeepComicInfo);
        Assert.True(window.Chosen.NumberFromFileName);

        // Nothing was chosen to write into, so the packages go beside their
        // archives, which is what null means here.
        Assert.Null(window.Chosen.Destination);
    }

    [AvaloniaFact]
    public void TheFoldersWindowRemovesAFolderAndMarksOnePrivate()
    {
        string[] watched = [Path.Combine("books", "bd"), Path.Combine("books", "manga")];

        var window = new FoldersWindow(watched, []);
        window.Show();

        ListBox folders = window.GetVisualDescendants().OfType<ListBox>().First();
        folders.SelectedIndex = 1;

        window.FindButton(b => b.Content as string == "Keep off the shelf").Press();

        string[] hidden = [watched[1]];
        Assert.Equal(hidden, window.Secret);

        // A folder removed takes its privacy with it: what is not watched has
        // nothing to hide.
        window.FindButton(b => b.Content as string == "Remove").Press();

        string[] left = [watched[0]];
        Assert.Equal(left, window.Watched);
        Assert.Empty(window.Secret);
        Assert.True(window.Changed);
    }

    private static void Tick(Window window, string label, bool ticked) =>
        window.GetVisualDescendants().OfType<CheckBox>().First(b => (string?)b.Content == label).IsChecked = ticked;
}
