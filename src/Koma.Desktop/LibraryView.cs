using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Koma.Library;

namespace Koma.Desktop;

/// <summary>
/// The shelf: one card per publication, with its cover, its title and the
/// file it came from, under a search box and a choice of order.
/// </summary>
/// <remarks>
/// Built from the index alone, so a library of a thousand volumes is shown
/// without opening one of them. What is shown, in what order and under which
/// headings is <see cref="ShelfArrangement"/>'s to decide; this control only
/// draws it, and draws it again as the search or the order changes, without
/// asking the index for anything new.
/// </remarks>
internal sealed class LibraryView : DockPanel
{
    private const double CardWidth = 232;
    private const double CoverHeight = 260;

    // Every card is the same height, cover or no cover, so that the rows line
    // up instead of stepping around the ones that have none: the cover, then
    // two lines of title, one of file name and three of whatever is left.
    private const double CardHeight = 430;

    private static readonly IBrush MissingCover = new ImmutableSolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));

    private static readonly (string Label, ShelfOrder Order)[] Orders =
    [
        ("By title", ShelfOrder.Title),
        ("By series", ShelfOrder.Series),
        ("Recently read", ShelfOrder.RecentlyRead)
    ];

    private readonly StackPanel groups = new() { Margin = new Thickness(8) };
    private readonly TextBox search = new() { PlaceholderText = "Search titles, series and files", Width = 320 };
    private readonly ComboBox order = new() { ItemsSource = Orders.Select(o => o.Label).ToArray(), SelectedIndex = 0 };

    private IReadOnlyList<LibraryEntry> entries = [];
    private LibraryStore? store;

    public LibraryView()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(14, 8, 14, 0), Children = { search, order } };

        SetDock(bar, Dock.Top);
        Children.Add(bar);
        Children.Add(new ScrollViewer { Content = groups });

        search.TextChanged += (_, _) => Draw();
        order.SelectionChanged += (_, _) => Draw();
    }

    /// <summary>Raised with the path of the publication the reader chose.</summary>
    public event EventHandler<string>? Chosen;

    /// <summary>Raised with the path of a publication whose metadata the reader wants to edit.</summary>
    public event EventHandler<string>? EditRequested;

    public void Show(IReadOnlyList<LibraryEntry> entries, LibraryStore store)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(store);

        this.entries = entries;
        this.store = store;

        Draw();
    }

    private void Draw()
    {
        groups.Children.Clear();

        if (store is null)
            return;

        ShelfOrder chosen = Orders[Math.Max(order.SelectedIndex, 0)].Order;
        IReadOnlyList<ShelfGroup> arranged = ShelfArrangement.Arrange(entries, search.Text, chosen);

        if (arranged.Count == 0 && entries.Count > 0)
        {
            groups.Children.Add(new TextBlock { Text = "Nothing on the shelf matches.", Opacity = 0.7, Margin = new Thickness(6) });
            return;
        }

        foreach (ShelfGroup group in arranged)
        {
            if (group.Heading is not null)
                groups.Children.Add(new TextBlock { Text = group.Heading, FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Thickness(6, 14, 6, 2) });

            var shelf = new WrapPanel { ItemWidth = CardWidth, ItemHeight = CardHeight };

            foreach (LibraryEntry entry in group.Entries)
                shelf.Children.Add(Card(entry, store));

            groups.Children.Add(shelf);
        }
    }

    private Button Card(LibraryEntry entry, LibraryStore store)
    {
        var contents = new StackPanel { Spacing = 4 };

        string file = Path.GetFileName(entry.Path);

        // A publication that will not open has no cover to be missing, so its
        // reason takes the place a cover would have held.
        if (entry.Unreadable is null)
            contents.Children.Add(Cover(entry, store));

        contents.Children.Add(Line(entry.Title ?? file, FontWeight.SemiBold, lines: 2));

        // A publication with no title is already shown under its file name;
        // naming the file twice says nothing the second time.
        if (entry.Title is not null)
            contents.Children.Add(Line(file, FontWeight.Normal, lines: 2, opacity: 0.55));

        contents.Children.Add(Line(Subtitle(entry), FontWeight.Normal, lines: entry.Unreadable is null ? 3 : 8, opacity: 0.75));

        var card = new Button
        {
            Content = contents,
            Margin = new Thickness(6),
            Padding = new Thickness(8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            IsEnabled = entry.Unreadable is null
        };

        // The reason a package was refused runs longer than a card; the card
        // shows what it can and the tip holds the rest.
        ToolTip.SetTip(card, entry.Unreadable is null ? entry.Path : $"{entry.Path}{Environment.NewLine}{entry.Unreadable}");
        card.Click += (_, _) => Chosen?.Invoke(this, entry.Path);

        var editItem = new MenuItem { Header = "Edit metadata…" };
        editItem.Click += (_, _) => EditRequested?.Invoke(this, entry.Path);
        card.ContextMenu = new ContextMenu { ItemsSource = new[] { editItem } };

        return card;
    }

    private static Border Cover(LibraryEntry entry, LibraryStore store)
    {
        Bitmap? cover = Thumbnail(entry, store);

        return new Border
        {
            Height = CoverHeight,
            Background = cover is null ? MissingCover : null,
            Child = cover is null ? null : new Image { Source = cover, Stretch = Stretch.Uniform }
        };
    }

    private static Bitmap? Thumbnail(LibraryEntry entry, LibraryStore store)
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

    private static TextBlock Line(string text, FontWeight weight, int lines, double opacity = 1)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = weight,
            Opacity = opacity,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = lines
        };
    }

    private static string Subtitle(LibraryEntry entry)
    {
        if (entry.Unreadable is not null)
            return entry.Unreadable;

        string pages = entry.PageCount == 1 ? "1 page" : $"{entry.PageCount} pages";

        // A position from an older index has no page number to show.
        string progress = entry.LastItem is null ? pages : entry.LastPage == 0 ? $"{pages} · started" : $"page {entry.LastPage} of {entry.PageCount}";

        // The series first, so that a volume says where it belongs when the
        // shelf is not grouped by series.
        return entry.Series is null ? progress : $"{Volume(entry)} · {progress}";
    }

    private static string Volume(LibraryEntry entry) => entry.SeriesPosition is null ? entry.Series! : $"{entry.Series} {entry.SeriesPosition}";
}
