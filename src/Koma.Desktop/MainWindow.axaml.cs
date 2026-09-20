using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Koma.Core;
using Koma.Core.Packaging;
using Koma.Core.Rendering;

namespace Koma.Desktop;

/// <remarks>
/// Disposable because it owns the open publication. A window's lifetime ends
/// in <see cref="OnClosed"/>, which disposes it, so nothing else has to.
/// </remarks>
internal sealed partial class MainWindow : Window, IDisposable
{
    private static readonly FilePickerFileType KomaFiles = new("KOMA publication") { Patterns = ["*.koma"] };

    private Publication? publication;
    private int current;

    public MainWindow()
    {
        InitializeComponent();

        OpenButton.Click += OnOpenClicked;
        View.SizeChanged += OnViewSizeChanged;

        // Tunnelling, so that the arrows turn pages before focus navigation
        // can use them to move between controls.
        AddHandler(KeyDownEvent, OnNavigationKey, RoutingStrategies.Tunnel);
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

        Open(stream, Path.GetFileName(path));
    }

    public void Dispose()
    {
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
            Open(await files[0].OpenReadAsync(), files[0].Name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status.Text = $"{files[0].Name}: {exception.Message}";
        }
    }

    private void Open(Stream stream, string name)
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

        publication?.Dispose();
        publication = new Publication(result.Package, result.Violations);
        current = 0;
        Title = $"{name} — KOMA";

        Repaginate();
        ShowCurrent();
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

        if (!publication.Paginate(SpreadLayout.FitsTwo(View.Bounds.Width, View.Bounds.Height)))
            return false;

        current = anchor is null ? 0 : IndexOf(publication.Spreads, anchor);

        return true;
    }

    private void OnNavigationKey(object? sender, KeyEventArgs e)
    {
        if (publication is null)
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

        // Decoded here rather than when drawn, so that the status can report
        // the faults of what is on screen, as §16 requires.
        List<ShownPage> shown = [.. SpreadLayout.Items(spread).Select(publication.Page)];
        IEnumerable<string> near = Enumerable.Range(current - 1, 3).Where(i => i >= 0 && i < spreads.Count).SelectMany(i => SpreadLayout.Items(spreads[i]));
        publication.Retain(near);

        View.Show(publication, spread);
        SpreadCounter.Text = string.Create(CultureInfo.InvariantCulture, $"{current + 1} / {spreads.Count}");
        Status.Text = StatusOf(publication, shown);
    }

    private static string StatusOf(Publication publication, IEnumerable<ShownPage> shown)
    {
        var text = new StringBuilder();

        // Warnings from opening are legal, and §8.8 asks for at least one of
        // them to be noticed; they stay in view for the whole reading.
        foreach (ContainerViolation note in publication.OpeningNotes)
            text.AppendLine(Describe(note));

        foreach (ShownPage page in shown)
        {
            foreach (ContainerViolation fault in page.Faults)
                text.AppendLine(page.Item.Id + ": " + Describe(fault));
        }

        if (publication.WithheldCount > 0)
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Incomplete: {publication.WithheldCount} page(s) could not be shown (§16)."));

        return text.ToString().TrimEnd();
    }

    private static int IndexOf(IReadOnlyList<Spread> spreads, string item)
    {
        for (int i = 0; i < spreads.Count; i++)
        {
            if (SpreadLayout.Items(spreads[i]).Contains(item))
                return i;
        }

        return 0;
    }

    private static string Describe(ContainerViolation violation) => $"{violation.Code} — {violation.Message}";

    private static string DescribeAll(IEnumerable<ContainerViolation> violations) => string.Join(Environment.NewLine, violations.Select(Describe));
}
