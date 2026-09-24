using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Koma.Core;
using Koma.Core.Importing;
using Koma.Core.Model;
using Koma.Core.Packaging;
using Koma.Core.Rendering;
using Koma.Core.Writing;
using Koma.Imaging;
using Koma.Library;

namespace Koma.Desktop;

/// <remarks>
/// Disposable because it owns the open publication. A window's lifetime ends
/// in <see cref="OnClosed"/>, which disposes it, so nothing else has to.
/// </remarks>
internal sealed partial class MainWindow : Window, IDisposable
{
    // Built when the picker opens, not when the type loads: the language is
    // read from the library, which is not there yet at that point.
    private static FilePickerFileType KomaFiles => new(Text.Of("KOMA publication")) { Patterns = ["*.koma"] };

    private static FilePickerFileType CbzFiles => new(Text.Of("Comic book archive")) { Patterns = ["*.cbz"] };

    // What an import from the shelf asks of the converter: checksums, so that
    // a page damaged later is caught (§8.6), and the rest as the reference
    // converter defaults it. Whether the original ComicInfo travels with the
    // package is the reader's to say, once for the whole import.
    private static readonly ConversionOptions ImportOptions = new(Checksums: true);

    // The reader's language first; NavigationLabel.Choose falls back on the
    // same primary language, then on the first label, from there.
    private static readonly string[] Languages = [CultureInfo.CurrentUICulture.Name];

    private readonly LibraryStore store;

    private LibraryIndex library;
    private Publication? publication;
    private string? openPath;
    private int current;
    private bool scanning;
    private FitMode fit = FitMode.Page;
    private double zoom = 1;
    private WindowState windowed = WindowState.Normal;
    private bool restoring;
    private bool localizing;
    private string? rightClicked;
    private Point? pressed;
    private bool importing;

    /// <summary>The window over the library of whoever is running it.</summary>
    /// <remarks>
    /// A debug build is one run from the repository; a release is the copy a
    /// reader downloads. The two keep separate libraries, so that neither
    /// inherits the folders and positions of the other.
    /// </remarks>
    public MainWindow()
#if DEBUG
        : this(LibraryStore.ForCurrentUser(development: true))
#else
        : this(LibraryStore.ForCurrentUser(development: false))
#endif
    {
    }

    /// <summary>
    /// The window over a library given to it.
    /// </summary>
    /// <remarks>
    /// Given rather than fetched, so that a test can put a library of its own
    /// in front of the window instead of the one belonging to whoever runs
    /// the tests.
    /// </remarks>
    internal MainWindow(LibraryStore store)
    {
        this.store = store;

        InitializeComponent();

        library = store.Load();
        Text.Current = library.Language == nameof(UiLanguage.French) ? UiLanguage.French : UiLanguage.English;

        OpenButton.Click += OnOpenClicked;
        LibraryButton.Click += (_, _) => ShowLibrary();
        FoldersButton.Click += OnFoldersClicked;
        RefreshButton.Click += async (_, _) => await ScanAsync();
        PrivateButton.IsCheckedChanged += (_, _) => ShowLibrary();
        Shelf.PrivacyToggled += (_, path) => ChangePrivacy(path);
        Shelf.CheckRequested += async (_, path) => await CheckAsync(path);
        LanguageChoice.SelectionChanged += (_, _) =>
        {
            if (!localizing)
                ChangeLanguage(LanguageChoice.SelectedIndex == 1 ? UiLanguage.French : UiLanguage.English);
        };
        Shelf.Ordered += (_, chosen) => RememberOrder(chosen);
        ImportButton.Click += OnImportClicked;
        EditButton.Click += async (_, _) => await EditAsync(openPath);
        PagesButton.Click += async (_, _) => await PagesAsync(openPath, ItemOf(current));

        // The page under the pointer, so that a page is edited where it is
        // seen rather than found again in a list.
        var editPage = new MenuItem { Header = Text.Of("Edit this page…") };
        editPage.Click += async (_, _) => await PagesAsync(openPath, rightClicked ?? ItemOf(current));
        Scroller.ContextMenu = new ContextMenu { ItemsSource = new[] { editPage } };
        Shelf.EditRequested += async (_, path) => await EditAsync(path);
        Shelf.Chosen += (_, path) => OpenPath(path);
        ContentsButton.IsCheckedChanged += (_, _) => NavigationPanel.IsVisible = ContentsButton.IsChecked == true;
        Contents.SelectionChanged += (_, _) => GoToTarget(Contents.SelectedItem);
        LandmarkList.SelectionChanged += (_, _) => GoToTarget(LandmarkList.SelectedItem);

        // The host and not the page view: the view has no size while the
        // shelf is up, and pagination follows the space a page would have.
        ViewHost.SizeChanged += OnViewSizeChanged;
        // Filling a list empties its selection and fills it again, which the
        // control reports as a choice: while the words are being changed,
        // nothing here is one.
        FitChoice.SelectionChanged += (_, _) =>
        {
            if (!localizing)
                ChangeFit(FitChoice.SelectedIndex == 1 ? FitMode.Width : FitMode.Page);
        };
        ResumeButton.Click += (_, _) => OpenLastRead();

        // Tunnelling, so that Ctrl and the wheel zoom before the scroller
        // scrolls, and a wheel with nothing to scroll turns the page.
        Scroller.AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);

        // Bubbling, so that a click on a scrollbar stays the scrollbar's.
        Scroller.PointerPressed += OnPointerPressed;
        Scroller.PointerReleased += OnPointerReleased;

        // Tunnelling, so that the arrows turn pages before focus navigation
        // can use them to move between controls.
        AddHandler(KeyDownEvent, OnNavigationKey, RoutingStrategies.Tunnel);

        Localize();
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
            Title = Text.Of("Open a KOMA publication"),
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
            Status.Text = result.Outcome == PackageOpenOutcome.UnsupportedVersion ? $"{name} is KOMA {result.DeclaredVersion?.ToString() ?? "of an unknown version"}; this build reads {KomaVersion.Supported} only." : $"{name} cannot be opened.{Environment.NewLine}{DescribeAll(result.Violations)}";
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

        // A publication is read the way it was left: a manga at the width of
        // the screen, an album whole. A zoom of zero is a publication never
        // read, or one the library does not know, which starts at its fit.
        fit = entry?.Fit ?? FitMode.Page;
        zoom = entry is { Zoom: > 0 } ? entry.Zoom : 1;

        restoring = true;
        FitChoice.SelectedIndex = fit == FitMode.Width ? 1 : 0;
        restoring = false;
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

        LibraryEntry recorded = entry with { LastItem = item, LastPage = publication.PageNumber(item), LastOpened = DateTimeOffset.UtcNow, Fit = fit, Zoom = zoom };
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

    /// <summary>The publication read last, which the shelf offers to take up again.</summary>
    private LibraryEntry? LastRead => library.Entries.Where(e => e.Unreadable is null && e.LastOpened is not null).MaxBy(e => e.LastOpened);

    /// <summary>
    /// Keeps the order the reader chose, so that the shelf opens the way they
    /// left it.
    /// </summary>
    private void RememberOrder(ShelfOrder chosen)
    {
        if (library.Order == chosen)
            return;

        library = library with { Order = chosen };

        try
        {
            store.Save(library);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status.Text = e.Message;
        }
    }

    /// <summary>
    /// Writes the fixed words of the window in the current language, and the
    /// changing ones again where they are built.
    /// </summary>
    private void Localize()
    {
        localizing = true;

        LibraryButton.Content = Text.Of("Library");
        FoldersButton.Content = Text.Of("Folders…");
        RefreshButton.Content = Text.Of("Refresh");
        PrivateButton.Content = Text.Of("Show private");
        ImportButton.Content = Text.Of("Import CBZ…");
        OpenButton.Content = Text.Of("Open…");
        ContentsButton.Content = Text.Of("Contents");
        EditButton.Content = Text.Of("Edit…");
        PagesButton.Content = Text.Of("Pages…");

        int fit = Math.Max(FitChoice.SelectedIndex, 0);
        FitChoice.ItemsSource = new[] { Text.Of("Fit page"), Text.Of("Fit width") };
        FitChoice.SelectedIndex = fit;

        int language = Text.Current == UiLanguage.French ? 1 : 0;
        LanguageChoice.ItemsSource = new[] { Text.Of("English"), Text.Of("French") };
        LanguageChoice.SelectedIndex = language;

        localizing = false;

        Shelf.Localize();
        ShowResume();
    }

    /// <summary>
    /// Changes the language of the interface, and remembers it.
    /// </summary>
    private void ChangeLanguage(UiLanguage language)
    {
        if (Text.Current == language)
            return;

        Text.Current = language;
        library = library with { Language = language.ToString() };

        try
        {
            store.Save(library);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status.Text = e.Message;
        }

        Localize();
    }

    /// <summary>
    /// The publications the shelf keeps out of sight: the private ones,
    /// unless the reader has asked for them.
    /// </summary>
    /// <remarks>
    /// Asking is a button and not a passphrase, and it is never remembered:
    /// the shelf opens without them every time, which is the whole point of
    /// marking one.
    /// </remarks>
    private HashSet<string> Hidden()
    {
        if (PrivateButton.IsChecked == true)
            return [];

        return [.. library.Entries.Where(e => ShelfArrangement.IsPrivate(e, library.PrivateFolders)).Select(e => e.Path)];
    }

    /// <summary>
    /// Opens a publication for what it has to say about itself, and shows all
    /// of it.
    /// </summary>
    /// <remarks>
    /// A card holds one line of one fault. Whoever wants to repair a file
    /// wants every fault, with the entry each one is about, which is what the
    /// opener already reports and nothing was showing.
    /// </remarks>
    private async Task CheckAsync(string path)
    {
        string report;

        try
        {
            report = await Task.Run(() => Check(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status.Text = e.Message;
            return;
        }

        new ReportWindow(Text.Of("{0} — report", Path.GetFileName(path)), report).Show(this);
    }

    private static string Check(string path)
    {
        using FileStream file = File.OpenRead(path);

        PackageOpenResult result = PackageOpener.Open(file);

        using KomaPackage? package = result.Package;

        var report = new StringBuilder();

        report.AppendLine(path);
        report.AppendLine(result.Outcome switch
        {
            PackageOpenOutcome.Opened => Text.Of("Opens."),
            PackageOpenOutcome.UnsupportedVersion => Text.Of("KOMA {0}, which this build does not read.", result.DeclaredVersion?.ToString() ?? "?"),
            _ => Text.Of("Does not open.")
        });

        if (result.Violations.Count == 0)
        {
            report.AppendLine(Text.Of("Nothing to report."));

            return report.ToString();
        }

        report.AppendLine();

        foreach (ContainerViolation violation in result.Violations)
            report.AppendLine(CultureInfo.InvariantCulture, $"{violation.Severity.ToString().ToUpperInvariant()}  {violation.Code} — {violation.Message}");

        return report.ToString();
    }

    /// <summary>Marks a publication private, or brings it back to the shelf.</summary>
    private void ChangePrivacy(string path)
    {
        if (Entry(path) is not { } entry)
            return;

        LibraryEntry marked = entry with { Private = !entry.Private };
        library = library with { Entries = [.. library.Entries.Select(e => e.Path == path ? marked : e)] };

        try
        {
            store.Save(library);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status.Text = e.Message;
        }

        ShowLibrary();
    }

    private void ShowResume()
    {
        LibraryEntry? entry = LastRead;

        // A private publication does not name itself on a button in front of
        // whoever is in the room.
        if (entry is not null && Hidden().Contains(entry.Path))
            entry = null;

        ResumeButton.IsVisible = entry is not null;

        if (entry is null)
            return;

        // The title on the button, so that the reader knows what they are
        // taking up without hunting for it on the shelf.
        ResumeButton.Content = Text.Of("Resume {0}", entry.Title ?? Path.GetFileName(entry.Path));
        ToolTip.SetTip(ResumeButton, entry.LastPage == 0 ? entry.Path : string.Create(CultureInfo.InvariantCulture, $"{entry.Path}{Environment.NewLine}page {entry.LastPage} of {entry.PageCount}"));
    }

    private void OpenLastRead()
    {
        if (LastRead is { } entry)
            OpenPath(entry.Path);
    }

    private void ShowLibrary()
    {
        // The reader is leaving the publication on screen, so where they are
        // in it is worth keeping before the shelf takes its place.
        RecordPosition();

        // No publication is on screen to name the window any more.
        Title = "KOMA";

        Shelf.Show(library.Entries, store, library.Order, Hidden());
        Shelf.IsVisible = true;
        RefreshButton.IsVisible = true;
        PrivateButton.IsVisible = true;
        ShowResume();
        EditButton.IsVisible = false;
        PagesButton.IsVisible = false;
        FitChoice.IsVisible = false;
        Scroller.IsVisible = false;
        NavigationPanel.IsVisible = false;
        ContentsButton.IsVisible = false;
        // What is on the shelf, not what the library holds: a count that
        // included the hidden ones would say how many there are.
        int shown = library.Entries.Count - Hidden().Count;

        SpreadCounter.Text = shown == 1 ? Text.Of("{0} publication", 1) : Text.Of("{0} publications", shown);

        if (!scanning)
            Status.Text = library.Folders.Count == 0 ? Text.Of("No folder is watched yet. Add one to fill the library.") : string.Join(Environment.NewLine, library.Folders);
    }

    private void ShowReader()
    {
        Shelf.IsVisible = false;
        ResumeButton.IsVisible = false;
        RefreshButton.IsVisible = false;
        PrivateButton.IsVisible = false;
        Scroller.IsVisible = true;
        FitChoice.IsVisible = true;

        // Only a file on disk can be rewritten; one opened through a picker
        // that gave no path cannot.
        EditButton.IsVisible = openPath is not null;
        PagesButton.IsVisible = openPath is not null;
    }

    /// <summary>
    /// Opens the folders the library watches, and follows whatever the reader
    /// did to them.
    /// </summary>
    /// <remarks>
    /// A folder removed takes its publications off the shelf at the next
    /// scan, which also forgets the covers it had made for them.
    /// </remarks>
    private async void OnFoldersClicked(object? sender, RoutedEventArgs e)
    {
        var window = new FoldersWindow(library.Folders, library.PrivateFolders);

        if (!await window.ShowDialog<bool>(this))
            return;

        library = library with { Folders = [.. window.Watched], PrivateFolders = [.. window.Secret] };

        // Written now rather than only by the scan, which a scan already
        // running would skip: the folders a reader chose are not lost to
        // timing.
        try
        {
            store.Save(library);
        }
        catch (Exception written) when (written is IOException or UnauthorizedAccessException)
        {
            Status.Text = written.Message;
        }

        await ScanAsync();
    }

    /// <summary>
    /// Edits a publication's metadata, then brings the shelf up to date.
    /// </summary>
    /// <remarks>
    /// The publication being edited is closed first if it is open, its place
    /// recorded: its package holds the file open, and Windows will not
    /// replace a file in use. If the reader was reading it, it is opened
    /// again afterwards where it was, whether or not the edit was saved; from
    /// the shelf, the shelf stays.
    /// </remarks>
    /// <summary>
    /// The page drawn under a point of the view, or none where the spread
    /// leaves a half empty.
    /// </summary>
    private string? PageAt(Point point)
    {
        if (publication is null || publication.Spreads.Count == 0)
            return null;

        foreach (PlacedItem placed in SpreadLayout.Arrange(publication.Spreads[current], publication.DeclaredSize, View.Bounds.Width, View.Bounds.Height))
        {
            LayoutRect bounds = placed.Bounds;

            if (!placed.IsEmptyHalf && point.X >= bounds.X && point.X <= bounds.X + bounds.Width && point.Y >= bounds.Y && point.Y <= bounds.Y + bounds.Height)
                return placed.Item;
        }

        return null;
    }

    private string? ItemOf(int spread) => publication is { Spreads.Count: > 0 } ? SpreadLayout.Items(publication.Spreads[spread]).FirstOrDefault() : null;

    /// <summary>
    /// Opens the pages of the publication, on the page given, and reopens the
    /// publication behind them.
    /// </summary>
    /// <remarks>
    /// The publication is closed first for the same reason an edit closes it:
    /// its package holds the file, and each save rewrites the file.
    /// </remarks>
    private async Task PagesAsync(string? path, string? item)
    {
        if (path is null)
            return;

        bool reading = !Shelf.IsVisible;

        RecordPosition();
        View.Show(null, null);
        publication?.Dispose();
        publication = null;

        bool saved = await new PagesWindow(path, item).ShowDialog<bool>(this);

        if (reading)
            OpenPath(path);

        if (saved)
            await ScanAsync();
    }

    private async Task EditAsync(string? path)
    {
        if (path is null)
            return;

        MetadataEdit current;

        try
        {
            current = await Task.Run(() => PublicationEditor.Current(path));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            Status.Text = $"{Path.GetFileName(path)}: {e.Message}";
            return;
        }

        bool open = publication is not null && openPath == path;
        bool reopen = open && !Shelf.IsVisible;

        if (open)
        {
            RecordPosition();
            View.Show(null, null);
            publication?.Dispose();
            publication = null;
        }

        bool saved = await new EditWindow(path, current).ShowDialog<bool>(this);

        if (reopen)
            OpenPath(path);

        if (saved)
            await ScanAsync();
    }

    private async void OnImportClicked(object? sender, RoutedEventArgs e)
    {
        if (await new ImportWindow().ShowDialog<ImportChoice?>(this) is not { } choice)
            return;

        (string? root, string[] archives) = choice.Folder ? await FolderOfArchives() : (null, await ChosenArchives());

        if (archives.Length > 0)
            await ImportAsync(Destinations(archives, root, choice.Destination), ImportOptions with { KeepComicInfo = choice.KeepComicInfo, NumberFromFileName = choice.NumberFromFileName });
    }

    private async Task<string[]> ChosenArchives()
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Text.Of("Import comic book archives"),
            AllowMultiple = true,
            FileTypeFilter = [CbzFiles]
        });

        return [.. files.Select(f => f.TryGetLocalPath()).OfType<string>()];
    }

    /// <summary>
    /// Every archive of a chosen folder, subfolders included, in the order a
    /// reader numbers volumes.
    /// </summary>
    /// <summary>
    /// Where each archive's package is written: beside it, or under the
    /// folder chosen, keeping whatever tree it sat in.
    /// </summary>
    /// <remarks>
    /// A collection is filed in folders, and a conversion that flattened
    /// eighty volumes into one folder would lose that filing. Only the tree
    /// below the folder that was imported is kept, since that is the part
    /// that was chosen.
    /// </remarks>
    internal static (string Cbz, string Koma)[] Destinations(string[] archives, string? root, string? destination)
    {
        if (destination is null)
            return [.. archives.Select(cbz => (cbz, Path.ChangeExtension(cbz, ".koma")))];

        return
        [
            .. archives.Select(cbz =>
            {
                string relative = root is null ? Path.GetFileName(cbz) : Path.GetRelativePath(root, cbz);

                return (cbz, Path.ChangeExtension(Path.Combine(destination, relative), ".koma"));
            })
        ];
    }

    private async Task<(string? Root, string[] Archives)> FolderOfArchives()
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Text.Of("Import every archive of a folder"),
            AllowMultiple = false
        });

        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } folder)
            return (null, []);

        string[] archives;

        try
        {
            archives = [.. Directory.EnumerateFiles(folder, "*.cbz", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).Order(NaturalOrder.Instance)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status.Text = e.Message;
            return (null, []);
        }

        if (archives.Length == 0)
            Status.Text = $"No .cbz archive under {folder}.";

        return (folder, archives);
    }

    /// <summary>
    /// Converts archives beside themselves, one at a time off the interface
    /// thread, then shows what the conversions said and rescans.
    /// </summary>
    /// <remarks>
    /// Beside the archive, under the same name: a collection kept in a
    /// watched folder gains its KOMA files where it already is, and nothing
    /// is scattered elsewhere. An existing file is never overwritten; it may
    /// be one the reader has been annotating, or one made by another tool.
    /// </remarks>
    private async Task ImportAsync((string Cbz, string Koma)[] archives, ConversionOptions options)
    {
        if (importing)
            return;

        importing = true;
        ImportButton.IsEnabled = false;

        var progress = new Progress<int>(done => Status.Text = Text.Of("Importing… {0} / {1}", done, archives.Length));
        IReadOnlyList<string> folders = library.Folders;
        string report;

        try
        {
            report = await Task.Run(() => Import(archives, folders, options, progress));
        }
        finally
        {
            importing = false;
            ImportButton.IsEnabled = true;
        }

        new ReportWindow(Text.Of("Import report — KOMA"), report).Show(this);

        await ScanAsync();
    }

    private static string Import((string Cbz, string Koma)[] archives, IReadOnlyList<string> folders, ConversionOptions options, IProgress<int> progress)
    {
        var report = new StringBuilder();
        int converted = 0;
        int refused = 0;

        for (int i = 0; i < archives.Length; i++)
        {
            (string cbz, string koma) = archives[i];

            // The tree under a chosen folder is kept, so the folders it needs
            // may not be there yet.
            Directory.CreateDirectory(Path.GetDirectoryName(koma)!);

            report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{cbz}{Environment.NewLine}  → {koma}"));

            if (File.Exists(koma))
            {
                report.AppendLine("  left as it was: a file of that name exists already");
                refused++;
            }
            else
            {
                try
                {
                    CbzConversion conversion = CbzConverter.Convert(cbz, koma, options);

                    foreach (string note in conversion.Notes)
                        report.AppendLine("  " + note);

                    if (!folders.Any(folder => IsWithin(koma, folder)))
                        report.AppendLine("  not in a watched folder: it will not appear on the shelf until its folder is added");

                    converted++;
                }
                catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    // A half-written file would be worse than none: it would
                    // look like a publication to the next scan.
                    TryDelete(koma);
                    report.AppendLine("  refused: " + e.Message);
                    refused++;
                }
            }

            report.AppendLine();
            progress.Report(i + 1);
        }

        report.Insert(0, string.Create(CultureInfo.InvariantCulture, $"{converted} converted, {refused} not converted.{Environment.NewLine}{Environment.NewLine}"));

        return report.ToString();
    }

    private static bool IsWithin(string path, string folder)
    {
        string relative = Path.GetRelativePath(folder, path);

        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nothing more to do: the report already says the conversion failed.
        }
    }

    /// <summary>
    /// Brings the library up to date, off the interface thread.
    /// </summary>
    private async Task ScanAsync()
    {
        if (scanning)
            return;

        scanning = true;
        Status.Text = Text.Of("Scanning…");

        try
        {
            var progress = new Progress<LibraryScanProgress>(step => Status.Text = Text.Of("Scanning… {0} / {1}", step.Done, step.Total));
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
        // A new pagination shows its spread; the same pagination only needs
        // the canvas resized to the new window.
        if (Repaginate())
            ShowCurrent();
        else if (publication is { Spreads.Count: > 0 })
            SizeCanvas(publication.Spreads[current]);
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
        if (e.Key == Key.F11)
        {
            e.Handled = true;
            ToggleFullScreen();
            return;
        }

        // Escape leaves full screen first, as every application does, and
        // only then goes back to the shelf.
        if (e.Key == Key.Escape && WindowState == WindowState.FullScreen)
        {
            e.Handled = true;
            ToggleFullScreen();
            return;
        }

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

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && ZoomSteps(e.Key) is { } steps)
        {
            e.Handled = true;
            Zoom(steps);
            return;
        }

        // Space reads down a page taller than the window before it turns it,
        // as a reader does with a dense page at the width of the screen.
        if (e.Key == Key.Space && ScrollDown())
        {
            e.Handled = true;
            return;
        }

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
        ScrollToStart();
    }

    /// <summary>
    /// Ctrl with plus, minus or zero, whether from the main keys or the keypad,
    /// as zoom steps; zero steps back to the fit.
    /// </summary>
    private static int? ZoomSteps(Key key) => key switch
    {
        Key.OemPlus or Key.Add => 1,
        Key.OemMinus or Key.Subtract => -1,
        Key.D0 or Key.NumPad0 => 0,
        _ => null
    };

    private void Zoom(int steps)
    {
        zoom = steps == 0 ? 1 : ReadingFit.Zoom(zoom, steps);
        ShowCurrent();
    }

    private void ChangeFit(FitMode mode)
    {
        // Restoring a publication's own fit is not the reader choosing one,
        // and must not throw away the zoom that came with it.
        if (restoring)
            return;

        fit = mode;
        zoom = 1;
        ShowCurrent();
        ScrollToStart();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPointProperties button = e.GetCurrentPoint(Scroller).Properties;

        // The side buttons of a mouse browse as they do everywhere: back is
        // back, whatever the direction of reading.
        if (button.IsXButton1Pressed || button.IsXButton2Pressed)
        {
            e.Handled = true;
            GoTo(button.IsXButton2Pressed ? current + 1 : current - 1);
            return;
        }

        if (button.IsRightButtonPressed)
            rightClicked = PageAt(e.GetPosition(View));

        pressed = button.IsLeftButtonPressed ? e.GetPosition(Scroller) : null;
    }

    /// <summary>
    /// A click turns the page; a drag does not.
    /// </summary>
    /// <remarks>
    /// The half the pages are read towards moves forward (§10.2), as the
    /// arrows do. A pointer that travelled was dragging something — a
    /// scrollbar, a selection — and turning the page under it would be a
    /// surprise.
    /// </remarks>
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        Point? start = pressed;
        pressed = null;

        if (publication is null || Shelf.IsVisible || start is null || e.InitialPressMouseButton != MouseButton.Left)
            return;

        Point at = e.GetPosition(Scroller);

        if (Math.Abs(at.X - start.Value.X) > 6 || Math.Abs(at.Y - start.Value.Y) > 6)
            return;

        e.Handled = true;
        GoTo(ReadingGesture.TurnsForward(at.X, Scroller.Bounds.Width, publication.Direction) ? current + 1 : current - 1);
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (publication is null || Shelf.IsVisible)
            return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            Zoom(e.Delta.Y > 0 ? 1 : -1);
            return;
        }

        // Nothing to scroll: the wheel turns the page instead, down for the
        // next one, whatever the direction of reading.
        if (View.Height <= Scroller.Bounds.Height + 0.5 && e.Delta.Y != 0)
        {
            e.Handled = true;
            GoTo(e.Delta.Y < 0 ? current + 1 : current - 1);
        }
    }

    /// <summary>
    /// Scrolls down most of a window's height when the spread runs below it,
    /// and says whether there was anything left to scroll.
    /// </summary>
    private bool ScrollDown()
    {
        double bottom = View.Height - Scroller.Bounds.Height;

        if (bottom <= 0.5 || Scroller.Offset.Y >= bottom - 0.5)
            return false;

        Scroller.Offset = Scroller.Offset.WithY(Math.Min(bottom, Scroller.Offset.Y + Scroller.Bounds.Height * 0.9));

        return true;
    }

    /// <summary>
    /// A new spread starts at its top, and at the side reading starts from:
    /// the right of a right-to-left page too wide for the window.
    /// </summary>
    private void ScrollToStart()
    {
        // After the next layout, once the scroller knows the new extent.
        Dispatcher.UIThread.Post(() =>
        {
            double right = Math.Max(0, View.Bounds.Width - Scroller.Bounds.Width);
            Scroller.Offset = new Vector(publication?.Direction == ReadingDirection.RightToLeft ? right : 0, 0);
        }, DispatcherPriority.Background);
    }

    private void ToggleFullScreen()
    {
        bool entering = WindowState != WindowState.FullScreen;

        if (entering)
            windowed = WindowState;

        WindowState = entering ? WindowState.FullScreen : windowed;

        // Full screen is for the pages: the bar and the status line go, and
        // come back with the window.
        TopBar.IsVisible = !entering;
        Status.IsVisible = !entering;
    }

    /// <summary>
    /// Sizes the page view for the spread on screen: fitted as the reader
    /// chose, then zoomed, in the spread's own proportions.
    /// </summary>
    private void SizeCanvas(Spread spread)
    {
        if (publication is null)
            return;

        // The host and not the scroller: a scroller hidden while the shelf was
        // up has no size until the next layout, and a spread sized against
        // nothing is drawn as nothing until something else asks for a frame.
        double aspect = ReadingFit.Aspect(SpreadLayout.Arrange(spread, publication.DeclaredSize, 1000, 1000));
        (double width, double height) = ReadingFit.Canvas(aspect, ViewHost.Bounds.Width, ViewHost.Bounds.Height, fit, zoom);

        View.Width = width;
        View.Height = height;
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

        SizeCanvas(spread);
        View.Show(publication, spread);
        SpreadCounter.Text = CounterOf(publication, spread, current, spreads.Count) + (zoom == 1 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"  ·  {zoom * 100:0} %"));
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
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Incomplete: {publication.WithheldCount} page(s) could not be shown."));

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
