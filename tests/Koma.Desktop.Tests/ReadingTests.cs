using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Koma.Library;
using Koma.TestSupport;

namespace Koma.Desktop.Tests;

/// <summary>
/// Reading: opening a publication from the shelf, and turning its pages.
/// </summary>
/// <remarks>
/// The window is given a library of its own, in a folder of the test's, so
/// that the tests neither read nor write the library of whoever runs them.
/// </remarks>
public sealed class ReadingTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("koma-reading").FullName;

    [AvaloniaFact]
    public void OpensAPublicationFromTheShelfAndTurnsItsPages()
    {
        MainWindow window = Reader();

        window.FindButton(b => b.Content is StackPanel).Press();

        // §7.3 gives a publication one name, and the window takes it. The
        // counter carries the printed page of §9.2 before the spread, this
        // package having a page list.
        Assert.Contains("Corpus de conformite", window.Title, StringComparison.Ordinal);
        Assert.Contains("1 / 3", Counter(window), StringComparison.Ordinal);

        // This publication reads right to left (§7.11), so the left arrow is
        // the one that moves forward: §10.2 turns towards the reading, not
        // towards a side of the keyboard.
        Turn(window, Key.Left);
        Assert.Contains("2 / 3", Counter(window), StringComparison.Ordinal);

        Turn(window, Key.Right);
        Assert.Contains("1 / 3", Counter(window), StringComparison.Ordinal);

        // The first spread is the first: a page before it is no page.
        Turn(window, Key.Right);
        Assert.Contains("1 / 3", Counter(window), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void GoesBackToTheShelfOnEscape()
    {
        MainWindow window = Reader();

        window.FindButton(b => b.Content is StackPanel).Press();
        Turn(window, Key.Escape);

        // The shelf again: its count, and no publication named in the title.
        Assert.Equal("KOMA", window.Title);
        Assert.Contains("publication", Counter(window), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void ShowsTheLandmarksInTheOrderThePagesCome()
    {
        // §9.3 fixes no order for the landmarks, so the reader puts them in
        // the one a reader would follow, whatever the document lists.
        MainWindow window = Reader();

        window.FindButton(b => b.Content is StackPanel).Press();

        ListBox landmarks = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "LandmarkList");
        string[] shown = [.. landmarks.Items.OfType<ListBoxItem>().Select(i => (string?)i.Content ?? string.Empty)];

        // valid-page-list has the cover on the first page and the story on
        // the second.
        Assert.Equal(2, shown.Length);
        Assert.StartsWith("Front cover", shown[0], StringComparison.Ordinal);
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

    /// <summary>A reader over a library holding one publication of the corpus.</summary>
    private MainWindow Reader()
    {
        string books = Path.Combine(folder, "books");
        Directory.CreateDirectory(books);
        File.Copy(Corpus.Package("valid-page-list.koma"), Path.Combine(books, "valid-page-list.koma"));

        var store = new LibraryStore(Path.Combine(folder, "library"));
        LibraryIndex scanned = LibraryScanner.Scan(new LibraryIndex { Folders = [books] }, store);

        store.Save(scanned);

        var window = new MainWindow(store);
        window.Show();

        return window;
    }

    private static void Turn(MainWindow window, Key key) => window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });

    /// <summary>What the bar says: the spread being read, or the size of the shelf.</summary>
    private static string Counter(MainWindow window) => window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "SpreadCounter").Text ?? string.Empty;
}
