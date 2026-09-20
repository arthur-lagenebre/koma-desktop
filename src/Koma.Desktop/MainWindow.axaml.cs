using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Koma.Core;
using Koma.Core.Model;
using Koma.Core.Packaging;
using Koma.Core.Rendering;
using Koma.Library;

namespace Koma.Desktop;

/// <remarks>
/// Disposable because it owns the open publication. A window's lifetime ends
/// in <see cref="OnClosed"/>, which disposes it, so nothing else has to.
/// </remarks>
internal sealed partial class MainWindow : Window, IDisposable
{
    private static readonly FilePickerFileType KomaFiles = new("KOMA publication") { Patterns = ["*.koma"] };

    // The reader's language first; NavigationLabel.Choose falls back on the
    // same primary language, then on the first label, from there.
    private static readonly string[] Languages = [CultureInfo.CurrentUICulture.Name];

    private readonly LibraryStore store = LibraryStore.ForCurrentUser();

    private LibraryIndex library;
    private Publication? publication;
    private string? openPath;
    private int current;
    private bool scanning;

    public MainWindow()
    {
        InitializeComponent();

        library = store.Load();

        OpenButton.Click += OnOpenClicked;
        LibraryButton.Click += (_, _) => ShowLibrary();
        AddFolderButton.Click += OnAddFolderClicked;
        Shelf.Chosen += (_, path) => OpenPath(path);
        ContentsButton.IsCheckedChanged += (_, _) => NavigationPanel.IsVisible = ContentsButton.IsChecked == true;
        Contents.SelectionChanged += (_, _) => GoToTarget(Contents.SelectedItem);
        LandmarkList.SelectionChanged += (_, _) => GoToTarget(LandmarkList.SelectedItem);

        // The host and not the page view: the view has no size while the
        // shelf is up, and pagination follows the space a page would have.
        ViewHost.SizeChanged += OnViewSizeChanged;

        // Tunnelling, so that the arrows turn pages before focus navigation
        // can use them to move between controls.
        AddHandler(KeyDownEvent, OnNavigationKey, RoutingStrategies.Tunnel);

        ShowLibrary();

        // A file named on the command line opens over the shelf a moment later.
        _ = ScanAsync();
    }

    public void OpenPath(string path)
    {
        Stream stream;

        try
        {
            stream = File.OpenRead(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status.Text = $"{path}: {e.Message}";
            return;
        }

        Open(stream, Path.GetFileName(path), path);
    }

    public void Dispose()
    {
        RecordPosition();
        publication?.Dispose();
        publication = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    private async void OnOpenClicked(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a KOMA publication",
            FileTypeFilter = [KomaFiles]
        });

        if (files.Count == 0)
            return;

        try
        {
            // The package reads its pages from this stream until it is closed,
            // so the stream is handed over rather than disposed here.
            Open(await files[0].OpenReadAsync(), files[0].Name, files[0].TryGetLocalPath());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status.Text = $"{files[0].Name}: {exception.Message}";
        }
    }

    private void Open(Stream stream, string name, string? path)
    {
        PackageOpenResult result = PackageOpener.Open(stream);

        if (result.Outcome != PackageOpenOutcome.Opened || result.Package is null)
        {
            stream.Dispose();

            // §5.0 keeps a file from another era of the format apart from a
            // broken one, and so does the message.
            Status.Text = result.Outcome == PackageOpenOutcome.UnsupportedVersion ? $"{name} is KOMA {result.DeclaredVersion?.ToString() ?? "of an unknown version"}; this build reads {KomaVersion.Supported} only (§5.0)." : $"{name} cannot be opened.{Environment.NewLine}{DescribeAll(result.Violations)}";
            return;
        }

        // Where the reader was in the publication being closed, before it is.
        RecordPosition();

        publication?.Dispose();
        publication = new Publication(result.Package, result.Violations);
        openPath = path;
        current = 0;
        // §7.3 gives a publication one name; the file it arrived in is the
        // shelf's business, not the window's.
        Title = $"{publication.Title} — KOMA";

        ShowReader();
        ShowNavigation(publication.Navigation);
        Repaginate();
        Resume();
        ShowCurrent();
    }

    /// <summary>
    /// Opens where the reader left off, if the library remembers a page.
    /// </summary>
    private void Resume()
    {
        LibraryEntry? entry = Entry(openPath);

        if (publication is not null && entry?.LastItem is { } item && SpreadOf(publication.Spreads, item) is int index)
            current = index;
    }

    /// <summary>
    /// Records where the reader is, for the publication to reopen there.
    /// </summary>
    /// <remarks>
    /// The first item of the spread on screen, never its number: §10.1
    /// paginates for the window, so a number means another page in a window
    /// of another shape. Only a publication of the library is remembered; a
    /// file opened from elsewhere has nowhere to be remembered.
    /// </remarks>
    private void RecordPosition()
    {
        if (publication is null || publication.Spreads.Count == 0 || Entry(openPath) is not { } entry)
            return;

        string? item = SpreadLayout.Items(publication.Spreads[current]).FirstOrDefault();

        if (item is null)
            return;

        LibraryEntry recorded = entry with { LastItem = item, LastPage = publication.PageNumber(item), LastOpened = DateTimeOffset.UtcNow };
        library = library with { Entries = [.. library.Entries.Select(e => e.Path == entry.Path ? recorded : e)] };

        try
        {
            store.Save(library);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Losing a reading position is not worth interrupting a reader
            // who is closing a window or opening another publication.
            Status.Text = e.Message;
        }
    }

    private LibraryEntry? Entry(string? path) => path is null ? null : library.Entries.FirstOrDefault(e => e.Path == path);

    private void ShowLibrary()
    {
        // The reader is leaving the publication on screen, so where they are
        // in it is worth keeping before the shelf takes its place.
        RecordPosition();

        // No publication is on screen to name the window any more.
        Title = "KOMA";

        Shelf.Show(library.Entries, store);
        Shelf.IsVisible = true;
        View.IsVisible = false;
        NavigationPanel.IsVisible = false;
        ContentsButton.IsVisible = false;
        SpreadCounter.Text = library.Entries.Count == 1 ? "1 publication" : string.Create(CultureInfo.InvariantCulture, $"{library.Entries.Count} publications");

        if (!scanning)
            Status.Text = library.Folders.Count == 0 ? "No folder is watched yet. Add one to fill the library." : string.Join(Environment.NewLine, library.Folders);
    }

    private void ShowReader()
    {
        Shelf.IsVisible = false;
        View.IsVisible = true;
    }

    private async void OnAddFolderClicked(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Add a folder of KOMA publications",
            AllowMultiple = true
        });

        // A folder the platform cannot name as a path is one the scanner
        // cannot walk, so it is not added.
        string[] added = [.. folders.Select(f => f.TryGetLocalPath()).OfType<string>().Except(library.Folders, StringComparer.Ordinal)];

        if (added.Length == 0)
            return;

        library = library with { Folders = [.. library.Folders, .. added] };

        await ScanAsync();
    }

    /// <summary>
    /// Brings the library up to date, off the interface thread.
    /// </summary>
    private async Task ScanAsync()
    {
        if (scanning)
            return;

        scanning = true;
        Status.Text = "Scanning…";

        try
        {
            var progress = new Progress<LibraryScanProgress>(step => Status.Text = string.Create(CultureInfo.InvariantCulture, $"Scanning… {step.Done} / {step.Total}"));
            LibraryIndex scanned = library;

            // Saved on the worker too: writing the index is the scan's last
            // step, not something the window has to remember to do.
            library = await Task.Run(() =>
            {
                LibraryIndex updated = LibraryScanner.Scan(scanned, store, progress);
                store.Save(updated);

                return updated;
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status.Text = e.Message;
            return;
        }
        finally
        {
            scanning = false;
        }

        if (Shelf.IsVisible)
            ShowLibrary();
    }

    private void OnViewSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (Repaginate())
            ShowCurrent();
    }

    private bool Repaginate()
    {
        if (publication is null)
            return false;

        // The page in view before is in view after, whatever the new
        // pagination groups it with.
        string? anchor = publication.Spreads.Count > 0 ? SpreadLayout.Items(publication.Spreads[current]).FirstOrDefault() : null;

        if (!publication.Paginate(SpreadLayout.FitsTwo(ViewHost.Bounds.Width, ViewHost.Bounds.Height)))
            return false;

        current = anchor is null ? 0 : SpreadOf(publication.Spreads, anchor) ?? 0;

        return true;
    }

    private void OnNavigationKey(object? sender, KeyEventArgs e)
    {
        // Escape goes back to the shelf, from where the publication reopens
        // where it was left.
        if (e.Key == Key.Escape)
        {
            if (!Shelf.IsVisible)
            {
                e.Handled = true;
                ShowLibrary();
            }

            return;
        }

        if (publication is null || Shelf.IsVisible)
            return;

        // In the panel, the arrows and Home and End browse the contents, and a
        // new selection takes the reader there; the page keys still turn pages.
        if (e.Source is Visual source && NavigationPanel.IsVisualAncestorOf(source) && e.Key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End)
            return;

        bool leftToRight = publication.Direction == ReadingDirection.LeftToRight;

        // The arrow pointing the way the pages are read moves forward: right
        // in a left-to-right publication, left in a right-to-left one (§10.2).
        int? target = e.Key switch
        {
            Key.PageDown or Key.Space => current + 1,
            Key.PageUp or Key.Back => current - 1,
            Key.Right => leftToRight ? current + 1 : current - 1,
            Key.Left => leftToRight ? current - 1 : current + 1,
            Key.Home => 0,
            Key.End => publication.Spreads.Count - 1,
            _ => null
        };

        if (target is null)
            return;

        e.Handled = true;
        GoTo(target.Value);
    }

    private void GoTo(int index)
    {
        if (publication is null || publication.Spreads.Count == 0)
            return;

        current = Math.Clamp(index, 0, publication.Spreads.Count - 1);
        ShowCurrent();
    }

    private void ShowCurrent()
    {
        if (publication is null || publication.Spreads.Count == 0)
            return;

        IReadOnlyList<Spread> spreads = publication.Spreads;
        Spread spread = spreads[current];
        List<string> onScreen = [.. SpreadLayout.Items(spread)];
        List<string> near = [.. Enumerable.Range(current - 1, 3).Where(i => i >= 0 && i < spreads.Count).SelectMany(i => SpreadLayout.Items(spreads[i]))];

        // Dropped first, so that pages queued for a spread the reader has
        // left do not hold up the ones now wanted.
        publication.Retain(near);

        View.Show(publication, spread);
        SpreadCounter.Text = CounterOf(publication, spread, current, spreads.Count);
        Status.Text = StatusOf(publication, onScreen);

        _ = LoadAsync(publication, spread, onScreen, near);
    }

    /// <summary>
    /// Decodes the pages on screen, then the ones either side, and redraws
    /// once the first are ready if the reader is still there.
    /// </summary>
    private async Task LoadAsync(Publication shown, Spread spread, List<string> onScreen, List<string> near)
    {
        try
        {
            await Task.WhenAll(onScreen.Select(shown.PageAsync));
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
        {
            // The reader turned the page, or closed the publication, before
            // these were ready. Whatever is shown now has its own load.
            return;
        }
        catch (InsufficientMemoryException e)
        {
            if (IsShowing(shown, spread))
                Status.Text = StatusOf(shown, onScreen) + Environment.NewLine + e.Message;

            return;
        }

        if (!IsShowing(shown, spread))
            return;

        View.InvalidateVisual();
        Status.Text = StatusOf(shown, onScreen);

        // The next turn, either way, finds its pages decoded. A neighbour
        // that fails is reported when it comes on screen, not before.
        foreach (string item in near.Except(onScreen))
            _ = shown.PageAsync(item);
    }

    private bool IsShowing(Publication shown, Spread spread) => ReferenceEquals(shown, publication) && current < shown.Spreads.Count && shown.Spreads[current] == spread;

    private static string StatusOf(Publication publication, IEnumerable<string> onScreen)
    {
        var text = new StringBuilder();

        // Warnings from opening are legal, and §8.8 asks for at least one of
        // them to be noticed; they stay in view for the whole reading.
        foreach (ContainerViolation note in publication.OpeningNotes)
            text.AppendLine(Describe(note));

        // Only decoded pages have faults to report; a page still on the worker
        // is reported when the redraw that follows its decoding comes.
        foreach (ShownPage page in onScreen.Select(publication.Loaded).OfType<ShownPage>())
        {
            foreach (ContainerViolation fault in page.Faults)
                text.AppendLine(page.Item.Id + ": " + Describe(fault));
        }

        if (publication.WithheldCount > 0)
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Incomplete: {publication.WithheldCount} page(s) could not be shown (§16)."));

        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// Fills the panel from <c>nav.xml</c>, or hides it for a publication
    /// without one.
    /// </summary>
    private void ShowNavigation(PublicationNavigation? navigation)
    {
        Contents.Items.Clear();
        LandmarkList.Items.Clear();

        if (navigation is null)
        {
            ContentsButton.IsVisible = false;
            NavigationPanel.IsVisible = false;
            return;
        }

        foreach (TocEntry entry in navigation.TableOfContents)
            Contents.Items.Add(TreeItemOf(entry));

        foreach (Landmark landmark in navigation.Landmarks)
            LandmarkList.Items.Add(new ListBoxItem { Content = LandmarkName(landmark), Tag = landmark.Item });

        Contents.IsVisible = navigation.TableOfContents.Count > 0;
        LandmarkList.IsVisible = navigation.Landmarks.Count > 0;
        ContentsButton.IsVisible = Contents.IsVisible || LandmarkList.IsVisible;
        NavigationPanel.IsVisible = ContentsButton.IsVisible && ContentsButton.IsChecked == true;
    }

    private static TreeViewItem TreeItemOf(TocEntry entry)
    {
        var node = new TreeViewItem { Header = NavigationLabel.Choose(entry.Labels, Languages)?.Text, Tag = entry.Item, IsExpanded = true };

        foreach (TocEntry child in entry.Children)
            node.Items.Add(TreeItemOf(child));

        return node;
    }

    /// <summary>
    /// A landmark's own label if it has one, else its type made readable:
    /// the type is a token, and <c>body-start</c> reads as Body start.
    /// </summary>
    private static string LandmarkName(Landmark landmark)
    {
        string? label = NavigationLabel.Choose(landmark.Labels, Languages)?.Text;

        if (label is not null)
            return label;

        string words = landmark.Type.Replace('-', ' ');

        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    private void GoToTarget(object? selected)
    {
        if (publication is null || selected is not Control { Tag: string item })
            return;

        if (SpreadOf(publication.Spreads, item) is int index)
            GoTo(index);
    }

    /// <summary>
    /// The page-list labels on screen with the spread's place, or the place
    /// alone for a publication without a page list.
    /// </summary>
    private static string CounterOf(Publication publication, Spread spread, int index, int count)
    {
        string place = string.Create(CultureInfo.InvariantCulture, $"{index + 1} / {count}");
        IReadOnlyList<string> labels = PageLabels.Of(spread, publication.Navigation?.PageList ?? ReadOnlyCollection<PageTarget>.Empty, publication.Direction);

        return labels.Count switch
        {
            0 => place,
            1 => $"p. {labels[0]}  ·  {place}",
            _ => $"p. {labels[0]}–{labels[^1]}  ·  {place}"
        };
    }

    private static int? SpreadOf(IReadOnlyList<Spread> spreads, string item)
    {
        for (int i = 0; i < spreads.Count; i++)
        {
            if (SpreadLayout.Items(spreads[i]).Contains(item))
                return i;
        }

        return null;
    }

    private static string Describe(ContainerViolation violation) => $"{violation.Code} — {violation.Message}";

    private static string DescribeAll(IEnumerable<ContainerViolation> violations) => string.Join(Environment.NewLine, violations.Select(Describe));
}
