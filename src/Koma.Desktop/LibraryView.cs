using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
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

    // Ctrl-click picks out publications to edit together; the shelf keeps the
    // picking as it is redrawn, since searching for the next one to add is
    // the usual way of building it.
    private readonly HashSet<string> picked = new(StringComparer.Ordinal);
    private LibraryStore? store;
    private bool restoring;

    public LibraryView()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(14, 8, 14, 0), Children = { search, order } };

        SetDock(bar, Dock.Top);
        Children.Add(bar);
        Children.Add(new ScrollViewer { Content = groups });

        Localize();

        // The arrows walk the shelf: a hundred cards under Tab alone is a
        // hundred presses, and a shelf is looked at in rows.
        AddHandler(KeyDownEvent, OnArrow, RoutingStrategies.Bubble);

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

    /// <summary>Raised with the path of a publication whose faults the reader wants in full.</summary>
    public event EventHandler<string>? CheckRequested;

    /// <summary>Raised with the publications picked out for an edit of them all.</summary>
    public event EventHandler<IReadOnlyList<string>>? Picked;

    /// <summary>Raised when the reader chooses another order, which the library remembers.</summary>
    public event EventHandler<ShelfOrder>? Ordered;

    /// <summary>
    /// Moves the focus from card to card, along a row and across rows.
    /// </summary>
    /// <remarks>
    /// How many cards a row holds depends on how wide the window is, which is
    /// why the step down is measured rather than fixed.
    /// </remarks>
    private void OnArrow(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox || e.Source is ComboBox)
            return;

        Button[] cards = [.. this.GetVisualDescendants().OfType<Button>().Where(b => b.Content is StackPanel)];
        int at = Array.FindIndex(cards, c => c.IsFocused);

        if (cards.Length == 0)
            return;

        int columns = Math.Max(1, (int)(Bounds.Width / CardWidth));
        int to = e.Key switch
        {
            Key.Right => at + 1,
            Key.Left => at - 1,
            Key.Down => at + columns,
            Key.Up => at - columns,
            Key.Home => 0,
            Key.End => cards.Length - 1,
            _ => at
        };

        if (to == at || to < 0 || to >= cards.Length)
            return;

        e.Handled = true;
        cards[Math.Max(to, 0)].Focus();
    }

    /// <summary>Takes a publication into the picking, or out of it.</summary>
    private void Pick(string path)
    {
        if (!picked.Remove(path))
            picked.Add(path);

        Picked?.Invoke(this, [.. picked]);
        Draw();
    }

    /// <summary>Forgets the picking, once something has been done with it.</summary>
    public void Unpick()
    {
        if (picked.Count == 0)
            return;

        picked.Clear();
        Picked?.Invoke(this, []);
        Draw();
    }

    /// <summary>Writes the fixed words of the shelf in the current language.</summary>
    public void Localize()
    {
        int chosen = Math.Max(order.SelectedIndex, 0);

        // Filling the list empties its selection and fills it again, which
        // the control reports as a choice of the reader's; it is not one, and
        // must not be written to the library as one.
        restoring = true;
        search.PlaceholderText = Text.Of("Search titles, series and files");
        AutomationProperties.SetName(search, Text.Of("Search titles, series and files"));
        AutomationProperties.SetName(order, Text.Of("Order of the shelf"));
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
            // A publication that will not open used to have a card nothing
            // could touch. It stays live: a click on it asks what is wrong,
            // which is the only useful thing to do with it.
            IsEnabled = true
        };

        // What a card is, said in one line: a screen reader announces a
        // button by its content, and a cover, a title and three lines of
        // detail announce as a heap of fragments.
        AutomationProperties.SetName(card, string.Join(". ", new[] { entry.Title ?? Path.GetFileName(entry.Path) }.Concat(Subtitle(entry))));

        if (picked.Contains(entry.Path))
        {
            card.BorderBrush = Brushes.DodgerBlue;
            card.BorderThickness = new Thickness(2);
        }

        // Ctrl-click picks rather than opens, and picking is what the button
        // reports: the click that follows opens nothing.
        card.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
                return;

            e.Handled = true;
            Pick(entry.Path);
        }, RoutingStrategies.Tunnel);

        if (entry.Unreadable is not null)
            card.Click += (_, _) => CheckRequested?.Invoke(this, entry.Path);

        // The reason a package was refused runs longer than a card; the card
        // shows what it can and the tip holds the rest.
        ToolTip.SetTip(card, entry.Unreadable is null ? entry.Path : $"{entry.Path}{Environment.NewLine}{entry.Unreadable}");
        if (entry.Unreadable is null)
            card.Click += (_, _) => Chosen?.Invoke(this, entry.Path);

        var editItem = new MenuItem { Header = Text.Of("Edit metadata…") };
        editItem.Click += (_, _) => EditRequested?.Invoke(this, entry.Path);

        var privacyItem = new MenuItem { Header = Text.Of(entry.Private ? "Show on the shelf" : "Keep off the shelf") };
        privacyItem.Click += (_, _) => PrivacyToggled?.Invoke(this, entry.Path);

        var checkItem = new MenuItem { Header = Text.Of("Check this publication…") };
        checkItem.Click += (_, _) => CheckRequested?.Invoke(this, entry.Path);

        card.ContextMenu = new ContextMenu { ItemsSource = new[] { editItem, privacyItem, checkItem } };

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
