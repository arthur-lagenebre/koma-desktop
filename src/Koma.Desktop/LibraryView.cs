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
    // two lines of title and three of whatever is left.
    private const double CardHeight = 400;

    private const double CardMargin = 6;

    private static readonly IBrush MissingCover = new ImmutableSolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));

    private static readonly (string Label, ShelfOrder Order)[] Orders =
    [
        ("By title", ShelfOrder.Title),
        ("By series", ShelfOrder.Series),
        ("Recently read", ShelfOrder.RecentlyRead)
    ];

    private readonly StackPanel groups = new() { Margin = new Thickness(8) };
    private readonly TextBox search = new() { Width = 320 };
    private readonly ComboBox order = new() { SelectedIndex = 0 };

    /// <summary>How many decoded covers are kept before the shelf starts over.</summary>
    private const int CoverCache = 400;

    private readonly Dictionary<string, Bitmap> covers = new(StringComparer.Ordinal);

    // One cover at a time: a thousand cards would otherwise put a thousand
    // decodes on the thread pool at once, and the first would arrive last.
    // A chain of tasks rather than a lock: nothing here is held between
    // cards, so there is nothing to release, or to dispose of.
    private Task reading = Task.CompletedTask;

    private int drawing;

    private IReadOnlyList<LibraryEntry> entries = [];
    private IReadOnlySet<string> hidden = new HashSet<string>(StringComparer.Ordinal);
    private LibraryStore? store;
    private bool restoring;

    public LibraryView()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(14, 8, 14, 0), Children = { search, order } };

        SetDock(bar, Dock.Top);
        Children.Add(bar);
        Children.Add(new ScrollViewer { Content = groups });

        Localize();

        search.TextChanged += (_, _) => Draw();
        order.SelectionChanged += (_, _) =>
        {
            Draw();

            if (!restoring)
                Ordered?.Invoke(this, Orders[Math.Max(order.SelectedIndex, 0)].Order);
        };
    }

    /// <summary>Raised with the path of the publication the reader chose.</summary>
    public event EventHandler<string>? Chosen;

    /// <summary>Raised with the path of a publication whose metadata the reader wants to edit.</summary>
    public event EventHandler<string>? EditRequested;

    /// <summary>Raised with the path of a publication the reader marks private, or brings back.</summary>
    public event EventHandler<string>? PrivacyToggled;

    /// <summary>Raised when the reader chooses another order, which the library remembers.</summary>
    public event EventHandler<ShelfOrder>? Ordered;

    /// <summary>Writes the fixed words of the shelf in the current language.</summary>
    public void Localize()
    {
        int chosen = Math.Max(order.SelectedIndex, 0);

        // Filling the list empties its selection and fills it again, which
        // the control reports as a choice of the reader's; it is not one, and
        // must not be written to the library as one.
        restoring = true;
        search.PlaceholderText = Text.Of("Search titles, series and files");
        order.ItemsSource = Orders.Select(o => Text.Of(o.Label)).ToArray();
        order.SelectedIndex = chosen;
        restoring = false;

        Draw();
    }

    public void Show(IReadOnlyList<LibraryEntry> entries, LibraryStore store, ShelfOrder chosen, IReadOnlySet<string> hidden)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(hidden);

        this.entries = entries;
        this.store = store;
        this.hidden = hidden;

        // Set before drawing, and without passing for a choice of the
        // reader's: it is the order they chose last time.
        restoring = true;
        order.SelectedIndex = Math.Max(0, Array.FindIndex(Orders, o => o.Order == chosen));
        restoring = false;

        Draw();
    }

    private void Draw()
    {
        // Covers still being read belong to the shelf as it was; this one is
        // drawn again, and theirs is gone.
        drawing++;
        groups.Children.Clear();

        if (store is null)
            return;

        ShelfOrder chosen = Orders[Math.Max(order.SelectedIndex, 0)].Order;
        IReadOnlyList<ShelfGroup> arranged = ShelfArrangement.Arrange(entries, search.Text, chosen, hidden: hidden);

        if (arranged.Count == 0 && entries.Count > hidden.Count)
        {
            groups.Children.Add(new TextBlock { Text = Text.Of("Nothing on the shelf matches."), Opacity = 0.7, Margin = new Thickness(6) });
            return;
        }

        foreach (ShelfGroup group in arranged)
        {
            if (group.Heading is not null)
                groups.Children.Add(new TextBlock { Text = Text.Of(group.Heading), FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Thickness(6, 14, 6, 2) });

            var shelf = new WrapPanel { ItemWidth = CardWidth, ItemHeight = CardHeight };

            foreach (LibraryEntry entry in group.Entries)
                shelf.Children.Add(Card(entry, store));

            groups.Children.Add(shelf);
        }
    }

    private Button Card(LibraryEntry entry, LibraryStore store)
    {
        var contents = new StackPanel { Spacing = 4 };

        // A publication that will not open has no cover to be missing, so its
        // reason takes the place a cover would have held.
        if (entry.Unreadable is null)
            contents.Children.Add(Cover(entry, store));

        // The file name is in the tip rather than on the card: a shelf is
        // read by titles, and a file name is what one looks up when something
        // is wrong.
        contents.Children.Add(Line(entry.Title ?? Path.GetFileName(entry.Path), FontWeight.SemiBold, lines: 2));
        string[] subtitle = Subtitle(entry);

        contents.Children.Add(Line(subtitle[0], FontWeight.Normal, lines: entry.Unreadable is null ? 2 : 8, opacity: 0.75));

        if (subtitle.Length > 1)
            contents.Children.Add(Line(subtitle[1], FontWeight.Normal, lines: 2, opacity: 0.75));

        // The same box for every publication, whatever it has to say: boxes
        // of different heights read as a shelf of different things.
        var card = new Button
        {
            Content = contents,
            Width = CardWidth - (2 * CardMargin),
            Height = CardHeight - (2 * CardMargin),
            Margin = new Thickness(CardMargin),
            Padding = new Thickness(8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            IsEnabled = entry.Unreadable is null
        };

        // The reason a package was refused runs longer than a card; the card
        // shows what it can and the tip holds the rest.
        ToolTip.SetTip(card, entry.Unreadable is null ? entry.Path : $"{entry.Path}{Environment.NewLine}{entry.Unreadable}");
        card.Click += (_, _) => Chosen?.Invoke(this, entry.Path);

        var editItem = new MenuItem { Header = Text.Of("Edit metadata…") };
        editItem.Click += (_, _) => EditRequested?.Invoke(this, entry.Path);

        var privacyItem = new MenuItem { Header = Text.Of(entry.Private ? "Show on the shelf" : "Keep off the shelf") };
        privacyItem.Click += (_, _) => PrivacyToggled?.Invoke(this, entry.Path);

        card.ContextMenu = new ContextMenu { ItemsSource = new[] { editItem, privacyItem } };

        return card;
    }

    /// <summary>
    /// The cover of a card: the one already decoded, or a grey box that fills
    /// in when the file has been read.
    /// </summary>
    /// <remarks>
    /// Decoding happens on a worker, one cover at a time and in the order the
    /// cards were built, so that the shelf is drawn at once and fills from
    /// the top. A library of a thousand volumes would otherwise decode a
    /// thousand images before showing anything.
    /// </remarks>
    private Border Cover(LibraryEntry entry, LibraryStore store)
    {
        var box = new Border { Height = CoverHeight, Background = MissingCover };

        if (entry.Thumbnail is null)
            return box;

        if (covers.TryGetValue(entry.Thumbnail, out Bitmap? decoded))
        {
            box.Background = null;
            box.Child = new Image { Source = decoded, Stretch = Stretch.Uniform };

            return box;
        }

        reading = Decode(reading, entry.Thumbnail, store.ThumbnailPath(entry.Thumbnail), box, drawing);

        return box;
    }

    /// <summary>
    /// Reads a cover and puts it in its box, unless the shelf has been drawn
    /// again since.
    /// </summary>
    private async Task Decode(Task previous, string name, string path, Border box, int drawn)
    {
        // The cards are queued in the order they were built, so the shelf
        // fills from the top, which is the order it is looked at.
        await previous;

        // At the size it is shown: a thumbnail decoded whole would cost four
        // times the memory for pixels nobody sees.
        Bitmap? cover = await Task.Run(() => Read(path));

        if (cover is null)
            return;

        // Bounded rather than boundless: a decoded cover is a few hundred
        // kilobytes, and a library can hold thousands. Past the bound the
        // shelf decodes again rather than grow without end. Dropped and not
        // disposed: a card on screen may still be drawing one, and the
        // collector takes them once no card is.
        if (covers.Count >= CoverCache)
            covers.Clear();

        covers[name] = cover;

        if (drawn == drawing)
            Place(box, cover);
    }

    private static void Place(Border box, Bitmap cover)
    {
        box.Background = null;
        box.Child = new Image { Source = cover, Stretch = Stretch.Uniform };
    }

    private static Bitmap? Read(string path)
    {
        try
        {
            using FileStream file = File.OpenRead(path);

            return Bitmap.DecodeToHeight(file, (int)CoverHeight);
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

    /// <summary>
    /// What a card says under the title: the series on its own line, then the
    /// volume and how far the reading got on the next.
    /// </summary>
    /// <remarks>
    /// A series called "20 000 siècles sous les mers" takes a line by itself,
    /// and a number lost at the end of it would be read as part of the name.
    /// </remarks>
    private static string[] Subtitle(LibraryEntry entry)
    {
        if (entry.Unreadable is not null)
            return [entry.Unreadable];

        string pages = entry.PageCount == 1 ? Text.Of("1 page") : Text.Of("{0} pages", entry.PageCount);

        // A position from an older index has no page number to show.
        string progress = entry.LastItem is null ? pages : entry.LastPage == 0 ? Text.Of("{0} · started", pages) : Text.Of("page {0} of {1}", entry.LastPage, entry.PageCount);

        if (entry.Series is null)
            return [progress];

        return Volume(entry) is { } volume ? [entry.Series, $"{volume} · {progress}"] : [entry.Series, progress];
    }

    /// <summary>
    /// A publication's place in its series, or none where the number says
    /// nothing.
    /// </summary>
    /// <remarks>
    /// The one volume of a one-volume series is a book, not a volume 1 of 1.
    /// </remarks>
    private static string? Volume(LibraryEntry entry)
    {
        if (entry.SeriesPosition is not { } position || (position == "1" && entry.SeriesTotal == "1"))
            return null;

        return entry.SeriesTotal is { } total ? Text.Of("{0} of {1}", position, total) : position;
    }
}
