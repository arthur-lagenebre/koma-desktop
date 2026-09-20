using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Koma.Library;

namespace Koma.Desktop;

/// <summary>
/// The shelf: one card per publication, with its cover and its title.
/// </summary>
/// <remarks>
/// Built from the index alone, so a library of a thousand volumes is shown
/// without opening one of them. A publication that cannot be opened keeps its
/// place, disabled, with the reason where the page count would be: a file that
/// has gone wrong is worth seeing.
/// </remarks>
internal sealed class LibraryView : ScrollViewer
{
    private readonly WrapPanel shelf = new() { Margin = new Thickness(8) };

    public LibraryView()
    {
        Content = shelf;
    }

    /// <summary>Raised with the path of the publication the reader chose.</summary>
    public event EventHandler<string>? Chosen;

    public void Show(IReadOnlyList<LibraryEntry> entries, LibraryStore store)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(store);

        shelf.Children.Clear();

        foreach (LibraryEntry entry in entries.OrderBy(e => e.Title ?? Path.GetFileName(e.Path), StringComparer.CurrentCultureIgnoreCase))
            shelf.Children.Add(Card(entry, store));
    }

    private Button Card(LibraryEntry entry, LibraryStore store)
    {
        var contents = new StackPanel { Spacing = 6 };
        Bitmap? cover = Cover(entry, store);

        if (cover is not null)
            contents.Children.Add(new Image { Source = cover, Height = 260, Stretch = Stretch.Uniform });

        contents.Children.Add(new TextBlock { Text = entry.Title ?? Path.GetFileName(entry.Path), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold });
        contents.Children.Add(new TextBlock { Text = Subtitle(entry), TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });

        var card = new Button
        {
            Content = contents,
            Width = 220,
            Margin = new Thickness(6),
            Padding = new Thickness(8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsEnabled = entry.Unreadable is null
        };

        card.Click += (_, _) => Chosen?.Invoke(this, entry.Path);

        return card;
    }

    private static Bitmap? Cover(LibraryEntry entry, LibraryStore store)
    {
        if (entry.Thumbnail is null)
            return null;

        try
        {
            return new Bitmap(store.ThumbnailPath(entry.Thumbnail));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A thumbnail deleted behind our back leaves a card without a
            // cover, which is better than a shelf that will not draw.
            return null;
        }
    }

    private static string Subtitle(LibraryEntry entry)
    {
        if (entry.Unreadable is not null)
            return entry.Unreadable;

        string pages = entry.PageCount == 1 ? "1 page" : $"{entry.PageCount} pages";

        return entry.LastItem is null ? pages : $"{pages} · started";
    }
}
