using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Koma.Core.Model;
using Koma.Core.Packaging;
using Koma.Core.Rendering;
using Koma.Core.Writing;

namespace Koma.Desktop;

/// <summary>
/// The pages of a publication, and what the package says about the one
/// chosen: its role, its span, its side of the spread, the chapter it opens,
/// and what a reader who cannot see it is told.
/// </summary>
/// <remarks>
/// One page is saved at a time, and each save rewrites the package, which
/// costs a copy of its entries and not a copy of its pages. The window says
/// whether anything was saved, so that the reader behind it knows to reopen
/// the publication.
/// </remarks>
internal sealed class PagesWindow : Window
{
    private static readonly (string Label, SpreadPosition Position)[] Positions =
    [
        ("Wherever it falls", SpreadPosition.Auto),
        ("Left of the spread", SpreadPosition.Left),
        ("Right of the spread", SpreadPosition.Right),
        ("Alone, centred", SpreadPosition.Center)
    ];

    // The height a page is shown at in the grid: enough to tell a cover from
    // an advertisement, not enough to read the text on it.
    private const double TileHeight = 150;

    private readonly string path;
    private readonly ListBox pages = new() { Width = 260, MaxHeight = 520 };
    private readonly ComboBox roles = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox span = new() { Content = Text.Of("Drawn across the whole spread") };
    private readonly ComboBox position = new() { ItemsSource = Positions.Select(p => Text.Of(p.Label)).ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox alternative = new() { AcceptsReturn = true, Height = 72, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox decorative = new() { Content = Text.Of("Decorative: carries nothing to describe") };
    private readonly TextBox chapter = new();
    private readonly TextBox printed = new();
    private readonly TextBox printedRight = new();
    private readonly StackPanel rightHalf;
    private readonly TextBlock problem = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    private readonly Button save = new() { Content = Text.Of("Save this page"), IsDefault = true };
    private readonly Button up = new() { Content = Text.Of("Move up") };
    private readonly Button down = new() { Content = Text.Of("Move down") };
    private readonly Button remove = new() { Content = Text.Of("Take this page out") };
    private readonly StackPanel form = new() { Spacing = 8, Margin = new Thickness(16, 0, 0, 0) };
    private readonly WritingNotice writing = new();

    private static readonly IImmutableSolidColorBrush MissingPage = new ImmutableSolidColorBrush(Color.FromArgb(40, 128, 128, 128));

    private readonly Dictionary<string, Bitmap> thumbnails = new(StringComparer.Ordinal);

    private string[] items = [];
    private string[] lines = [];
    private bool filling;

    public PagesWindow(string path, string? current)
    {
        this.path = path;

        Title = Text.Of("{0} — Pages", Path.GetFileName(path));
        Width = 760;
        Height = 520;

        rightHalf = Field(Text.Of("Number printed on its right half"), printedRight);
        rightHalf.IsVisible = false;

        // Only a page drawn across a spread carries two printed numbers, one
        // for each half of the spread it fills (§9.2).
        span.IsCheckedChanged += (_, _) => rightHalf.IsVisible = span.IsChecked == true;

        pages.SelectionChanged += (_, _) => Fill();
        save.Click += OnSave;
        up.Click += async (_, _) => await Move(-1);
        down.Click += async (_, _) => await Move(1);
        remove.Click += async (_, _) => await Remove();

        var close = new Button { Content = Text.Of("Close"), IsCancel = true };
        close.Click += (_, _) => Close(Saved);

        form.Children.Add(Field(Text.Of("Role"), roles));
        form.Children.Add(span);
        form.Children.Add(Field(Text.Of("Place in the spread"), position));
        form.Children.Add(Field(Text.Of("Number printed on the page"), printed));
        form.Children.Add(rightHalf);
        form.Children.Add(Field(Text.Of("Chapter opening here, if any"), chapter));
        form.Children.Add(Field(Text.Of("Alternative text"), alternative));
        form.Children.Add(decorative);
        form.Children.Add(writing);
        form.Children.Add(problem);
        form.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { up, down, remove } });
        form.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { close, save } });

        Content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16), Children = { pages, form } };

        Load(current);

        // The pages themselves, read from the package on a worker: a
        // publication of two hundred pages would otherwise hold the window
        // shut while it decoded them all.
        Reading = Decode();
    }

    /// <summary>Whether anything was written, which the reader behind needs to know.</summary>
    public bool Saved { get; private set; }

    /// <summary>The reading of the pages, for a test to wait on rather than guess at.</summary>
    internal Task Reading { get; }

    /// <summary>How many pages have been read, whatever the list has drawn of them.</summary>
    internal int Pictures => thumbnails.Count;

    /// <summary>
    /// Reads every page of the publication, small, and puts each one beside
    /// its line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A page is easier to recognise than an identifier: a reader editing
    /// p014 wants to see p014. They are decoded once and kept, since editing
    /// the metadata does not change the images.
    /// </para>
    /// <para>
    /// The package is opened for the reading and closed as soon as it is
    /// done, because saving rewrites the very file it is reading, and a file
    /// cannot be replaced while it is held. Saving waits for that, which is
    /// what the notice above the form is saying.
    /// </para>
    /// </remarks>
    private async Task Decode()
    {
        save.IsEnabled = false;

        try
        {
            foreach ((string item, Bitmap? page) in await Task.Run(Read))
            {
                if (page is not null)
                    thumbnails[item] = page;
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            problem.Text = e.Message;
        }

        save.IsEnabled = true;
        Fill();
        Redraw();
    }

    private List<(string Item, Bitmap? Page)> Read()
    {
        var read = new List<(string, Bitmap?)>();

        using FileStream file = File.OpenRead(path);
        PackageOpenResult result = PackageOpener.Open(file);

        using KomaPackage? package = result.Package;

        if (package is null)
            return read;

        foreach (ManifestItem item in package.Manifest.Items)
        {
            using Stream? resource = package.TryOpenResource(item.Href);

            if (resource is null)
                continue;

            try
            {
                // At the size it is shown, as the shelf decodes its covers.
                read.Add((item.Id, Bitmap.DecodeToHeight(resource, (int)TileHeight)));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException)
            {
                // A page that will not decode leaves a line without a
                // picture, which the report of the publication explains.
                read.Add((item.Id, null));
            }
        }

        return read;
    }

    private static StackPanel Field(string label, Control input) => new() { Spacing = 2, Children = { new TextBlock { Text = label, Opacity = 0.75 }, input } };

    /// <summary>
    /// Moves the chosen page one place earlier or later in the reading order
    /// (§8.8), and keeps it chosen where it lands.
    /// </summary>
    private async Task Move(int by)
    {
        if (pages.SelectedIndex < 0)
            return;

        string item = items[pages.SelectedIndex];

        await Written(() => PublicationEditor.MovePage(path, item, pages.SelectedIndex + by, DateTimeOffset.UtcNow), item);
    }

    /// <summary>
    /// Takes the chosen page out of the publication, its file included.
    /// </summary>
    /// <remarks>
    /// Asked for twice, since this one cannot be taken back: the page leaves
    /// the package, and the package is rewritten without it.
    /// </remarks>
    private async Task Remove()
    {
        if (pages.SelectedIndex < 0)
            return;

        string item = items[pages.SelectedIndex];

        if (!await Confirm.Ask(this, Text.Of("Take {0} out of the publication? The page and its file go, and this cannot be taken back.", item)))
            return;

        int chosen = pages.SelectedIndex;

        await Written(() => PublicationEditor.RemovePage(path, item, DateTimeOffset.UtcNow), null);

        pages.SelectedIndex = Math.Min(chosen, items.Length - 1);
    }

    /// <summary>
    /// Writes the package, off the interface thread, and reads the pages
    /// again from what was written.
    /// </summary>
    private async Task Written(Action write, string? chosen)
    {
        problem.Text = string.Empty;
        Busy(true);

        try
        {
            await Task.Run(write);
            Saved = true;
            Load(chosen);
        }
        catch (Exception refused) when (refused is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            problem.Text = refused.Message;
        }

        Busy(false);
    }

    /// <summary>
    /// Shows that the package is being written, and takes the pages and the
    /// form out of reach while it is.
    /// </summary>
    private void Busy(bool writing)
    {
        this.writing.Show(writing);
        form.IsEnabled = !writing;
        pages.IsEnabled = !writing;
        Cursor = new Cursor(writing ? StandardCursorType.Wait : StandardCursorType.Arrow);
    }

    /// <summary>
    /// The numbers printed on the page: one, or two when it is drawn across a
    /// spread and both halves are numbered.
    /// </summary>
    private string[] PrintedPages()
    {
        string first = (printed.Text ?? string.Empty).Trim();
        string second = span.IsChecked == true ? (printedRight.Text ?? string.Empty).Trim() : string.Empty;

        if (first.Length == 0)
            return [];

        return second.Length == 0 ? [first] : [first, second];
    }

    private void Load(string? current)
    {
        IReadOnlyList<(string Item, string Roles)> listed;

        try
        {
            listed = PublicationEditor.Pages(path);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            problem.Text = e.Message;
            save.IsEnabled = false;
            return;
        }

        items = [.. listed.Select(p => p.Item)];
        lines = [.. listed.Select((p, i) => $"{i + 1}.  {p.Item}   {p.Roles}")];
        Redraw();
        pages.SelectedIndex = Math.Max(0, Array.IndexOf(items, current));
    }

    /// <summary>
    /// Draws the pages: each line with its picture beside it, once the
    /// picture has been read.
    /// </summary>
    private void Redraw()
    {
        int chosen = pages.SelectedIndex;

        pages.ItemsSource = items.Select((item, i) => (Control)new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new Border
                {
                    Width = TileHeight * 0.7,
                    Height = TileHeight,
                    Background = thumbnails.ContainsKey(item) ? null : MissingPage,
                    Child = thumbnails.TryGetValue(item, out Bitmap? page) ? new Image { Source = page, Stretch = Stretch.Uniform } : null
                },
                new TextBlock { Text = lines[i], VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Width = 150 }
            }
        }).ToArray();

        pages.SelectedIndex = chosen;
    }

    /// <summary>Puts what the manifest says about the chosen page into the form.</summary>
    private void Fill()
    {
        if (pages.SelectedIndex < 0 || pages.SelectedIndex >= items.Length)
            return;

        PageEdit page;

        try
        {
            page = PublicationEditor.CurrentPage(path, items[pages.SelectedIndex]);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException)
        {
            problem.Text = e.Message;
            return;
        }

        // The roles offered are the core vocabulary, plus whatever this page
        // already carries: a private-use token, or several roles at once, is
        // not lost for having opened the form.
        string carried = string.Join(' ', page.Roles ?? []);
        string[] offered = [.. OpenVocabularies.PageRoles.Concat(carried.Length == 0 ? [] : [carried]).Distinct(StringComparer.Ordinal)];

        // The form is filled, not edited: nothing here is a reader's choice.
        filling = true;
        roles.ItemsSource = offered;
        roles.SelectedIndex = Math.Max(0, Array.IndexOf(offered, carried));
        chapter.Text = page.Chapter;
        printed.Text = page.PrintedPages is [string first, ..] ? first : string.Empty;
        printedRight.Text = page.PrintedPages is [_, string second, ..] ? second : string.Empty;
        rightHalf.IsVisible = page.PageSpan == 2;
        span.IsChecked = page.PageSpan == 2;
        position.SelectedIndex = Array.FindIndex(Positions, p => p.Position == (page.SpreadPosition ?? SpreadPosition.Auto));
        alternative.Text = page.AlternativeText;
        decorative.IsChecked = page.Decorative == true;
        problem.Text = string.Empty;
        filling = false;
    }

    private async void OnSave(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (filling || pages.SelectedIndex < 0)
            return;

        string item = items[pages.SelectedIndex];
        var edit = new PageEdit(
            [.. (roles.SelectedItem as string ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)],
            span.IsChecked == true ? 2 : 1,
            Positions[Math.Max(position.SelectedIndex, 0)].Position,
            (alternative.Text ?? string.Empty).Trim(),
            decorative.IsChecked == true,
            (chapter.Text ?? string.Empty).Trim(),
            PrintedPages());

        problem.Text = string.Empty;
        Busy(true);

        try
        {
            await Task.Run(() => PublicationEditor.EditPage(path, item, edit, DateTimeOffset.UtcNow));
            Saved = true;
            Load(item);
        }
        catch (Exception refused) when (refused is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            problem.Text = refused.Message;
        }

        Busy(false);
    }
}
