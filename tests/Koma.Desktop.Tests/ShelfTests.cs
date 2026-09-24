using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Koma.Core.Rendering;
using Koma.Library;

namespace Koma.Desktop.Tests;

/// <summary>
/// The shelf, drawn without a screen.
/// </summary>
/// <remarks>
/// These are smoke tests, and they exist because of what got through: a
/// language switch that recursed until the stack gave out, a spread drawn at
/// nothing, a call left with two arguments where the method had three. None
/// of it showed at build time or in the other suites.
/// </remarks>
public sealed class ShelfTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("koma-shelf").FullName;

    [AvaloniaFact]
    public void DrawsACardForEveryPublicationItIsGiven()
    {
        (Window window, LibraryView shelf) = Shelf();

        shelf.Show(Entries(), new LibraryStore(folder), ShelfOrder.Title, new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(3, Cards(window));
    }

    [AvaloniaFact]
    public void LeavesThePrivateOnesOffUntilTheyAreAskedFor()
    {
        (Window window, LibraryView shelf) = Shelf();
        LibraryEntry[] entries = Entries();

        shelf.Show(entries, new LibraryStore(folder), ShelfOrder.Title, new HashSet<string>(StringComparer.Ordinal) { entries[0].Path });

        Assert.Equal(2, Cards(window));
    }

    [AvaloniaFact]
    public void ChangesLanguageWithoutTakingItForAChoiceOfTheReader()
    {
        // Filling a list empties its selection and fills it again, which the
        // control reports as a choice: once, that recursed until the stack
        // gave out, and it would have written an order nobody chose.
        (Window window, LibraryView shelf) = Shelf();
        var ordered = new List<ShelfOrder>();

        shelf.Ordered += (_, order) => ordered.Add(order);
        shelf.Show(Entries(), new LibraryStore(folder), ShelfOrder.Series, new HashSet<string>(StringComparer.Ordinal));

        Text.Current = UiLanguage.French;

        try
        {
            shelf.Localize();
        }
        finally
        {
            Text.Current = UiLanguage.English;
        }

        Assert.Empty(ordered);
        Assert.Equal(3, Cards(window));
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

    /// <summary>A shelf in a window, since a control draws nothing outside one.</summary>
    private static (Window Window, LibraryView Shelf) Shelf()
    {
        var shelf = new LibraryView();
        var window = new Window { Width = 1200, Height = 800, Content = shelf };

        window.Show();

        return (window, shelf);
    }

    private static int Cards(Window window) => window.GetVisualDescendants().OfType<Button>().Count(b => b.Content is StackPanel);

    private static LibraryEntry[] Entries() =>
    [
        Entry("un.koma", "Un"),
        Entry("deux.koma", "Deux"),
        Entry("trois.koma", "Trois")
    ];

    private static LibraryEntry Entry(string file, string title) => new()
    {
        Path = Path.Combine("books", file),
        Size = 1,
        Modified = DateTimeOffset.UnixEpoch,
        Title = title,
        PageCount = 12
    };
}
