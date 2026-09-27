using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Koma.Core.Rendering;
using Koma.Core.Writing;
using Koma.Library;

namespace Koma.Desktop.Tests;

/// <summary>
/// Picking several publications out of the shelf, to edit what they have in
/// common.
/// </summary>
public sealed class BatchEditTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("koma-batch").FullName;

    [AvaloniaFact]
    public void PicksPublicationsWithControlAndForgetsThemOnCommand()
    {
        var shelf = new LibraryView();
        var window = new Window { Width = 1200, Height = 800, Content = shelf };
        var picked = new List<IReadOnlyList<string>>();

        window.Show();
        shelf.Picked += (_, paths) => picked.Add(paths);
        shelf.Show(Entries(), new LibraryStore(folder), ShelfOrder.Title, new HashSet<string>(StringComparer.Ordinal));

        Pick(window, 0);
        Pick(window, 1);

        Assert.Equal(2, picked[^1].Count);

        // Picking the same one again takes it out: the modifier toggles.
        Pick(window, 1);
        Assert.Single(picked[^1]);

        shelf.Unpick();
        Assert.Empty(picked[^1]);
    }

    [AvaloniaFact]
    public void DropsAWholePickingAtOnce()
    {
        // A picking of thirty is thirty control-clicks to undo one at a time.
        var shelf = new LibraryView();
        var window = new Window { Width = 1200, Height = 800, Content = shelf };
        var picked = new List<IReadOnlyList<string>>();

        window.Show();
        shelf.Picked += (_, paths) => picked.Add(paths);
        shelf.Show(Entries(), new LibraryStore(folder), ShelfOrder.Title, new HashSet<string>(StringComparer.Ordinal));

        Pick(window, 0);
        Pick(window, 1);
        Pick(window, 2);

        Assert.Equal(3, picked[^1].Count);

        shelf.Unpick();

        Assert.Empty(picked[^1]);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    public void AddsToWhatAPublicationHasOrStandsInPlaceOfIt(bool replace, int expected)
    {
        // Adding is what a batch is usually for — a genre shared by a series
        // does not mean the volumes share nothing else — and replacing is for
        // a conversion that put the wrong thing everywhere.
        SubjectEdit[] had = [new SubjectEdit("keyword", "Nemo")];
        SubjectEdit[] given = [new SubjectEdit("genre", "Aventure")];

        IReadOnlyList<SubjectEdit>? written = MainWindow.Merged(given, had, replace, (left, right) => left.Type == right.Type && left.Text == right.Text);

        Assert.Equal(expected, written!.Count);
        Assert.Contains(written, s => s.Text == "Aventure");
    }

    [AvaloniaFact]
    public void SaysNothingAboutAListNobodyTicked()
    {
        // A list left alone is a list left alone: a batch that wrote an empty
        // one would clear forty publications at once.
        var window = new BatchEditWindow(3);
        window.Show();

        window.FindButton(b => b.Content as string == "Save").Press();

        Assert.Null(window.Plan?.People);
        Assert.Null(window.Plan?.Subjects);
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

    /// <summary>A control-click on the card at that place, which picks it.</summary>
    private static void Pick(Window window, int index)
    {
        Button card = window.GetVisualDescendants().OfType<Button>().Where(b => b.Content is StackPanel).ElementAt(index);

        card.RaiseEvent(new PointerPressedEventArgs(card, new Pointer(0, PointerType.Mouse, true), card, default, 0, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.Control)
        {
            RoutedEvent = InputElement.PointerPressedEvent
        });
    }

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
