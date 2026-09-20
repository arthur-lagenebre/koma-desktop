using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Koma.Core.Model;
using Koma.Core.Packaging;
using Koma.Core.Rendering;
using Koma.Imaging;
using SkiaSharp;

namespace Koma.Desktop;

/// <summary>
/// An open publication, paginated for the current viewport, with the pages
/// near the reader decoded.
/// </summary>
/// <remarks>
/// <para>
/// Pages are decoded on a worker by <see cref="PageCache{TPage}"/>, which is
/// the only code that reads the archive once it is open. Everything the
/// interface thread asks of this class — pagination, declared sizes,
/// backgrounds — comes from the manifest and the metadata, which are in
/// memory, so the interface never waits on the archive.
/// </para>
/// <para>
/// Only the pages near the reader are kept: a long publication read to the
/// end must not hold every page it has shown.
/// </para>
/// </remarks>
internal sealed class Publication : IDisposable
{
    private readonly KomaPackage package;
    private readonly PageCache<ShownPage> pages;
    private readonly ConcurrentDictionary<string, bool> withheld = new();
    private bool? fitsTwo;

    public Publication(KomaPackage package, IReadOnlyList<ContainerViolation> openingNotes)
    {
        this.package = package;
        OpeningNotes = openingNotes;
        pages = new PageCache<ShownPage>(Build);
    }

    public ReadingDirection Direction => package.Metadata.Direction;

    /// <summary>What <c>nav.xml</c> offers, or <see langword="null"/> without one.</summary>
    public PublicationNavigation? Navigation => package.Navigation;

    /// <summary>The warnings the opener raised, which the reader is shown throughout.</summary>
    public IReadOnlyList<ContainerViolation> OpeningNotes { get; }

    public IReadOnlyList<Spread> Spreads { get; private set; } = [];

    /// <summary>
    /// Pages withheld so far. The count survives the page leaving memory: once
    /// one is found, the publication is not complete (§16), wherever the
    /// reader goes next.
    /// </summary>
    public int WithheldCount => withheld.Count;

    /// <summary>
    /// Paginates for a viewport, and says whether the pagination changed.
    /// </summary>
    public bool Paginate(bool viewportFitsTwo)
    {
        if (fitsTwo == viewportFitsTwo)
            return false;

        fitsTwo = viewportFitsTwo;
        Spreads = package.Paginate(viewportFitsTwo);

        return true;
    }

    public (int Width, int Height) DeclaredSize(string item)
    {
        ManifestItem declared = Item(item);

        return (declared.Width, declared.Height);
    }

    /// <summary>
    /// What shows through transparency (§10.5), and what fills an empty half
    /// beside the item (§10.4). It comes from the manifest, so a page not yet
    /// decoded already has its place drawn in the right colour.
    /// </summary>
    /// <remarks>
    /// Nothing checks <c>background-color</c> against the <c>#RRGGBB</c> form
    /// of §10.5 yet, so a value that does not parse falls back to the default
    /// rather than failing a page that is otherwise fine to show.
    /// </remarks>
    public IBrush Background(string item) => new ImmutableSolidColorBrush(Color.TryParse(Item(item).BackgroundColor, out Color colour) ? colour : Colors.White);

    /// <summary>The page if it is decoded, without waiting for it.</summary>
    public ShownPage? Loaded(string item) => pages.TryGet(item);

    /// <summary>The page, decoded on the worker if it is not already.</summary>
    public Task<ShownPage> PageAsync(string item) => pages.GetAsync(item);

    /// <summary>Drops every decoded or queued page but those of the given items.</summary>
    public void Retain(IEnumerable<string> items) => pages.Retain(items);

    public void Dispose()
    {
        // The cache waits for the page in flight, so the archive is closed
        // with nothing still reading it.
        pages.Dispose();
        package.Dispose();
    }

    private ShownPage Build(string item)
    {
        using LoadedPage loaded = PageLoader.Load(package, Item(item));
        var page = new ShownPage(loaded.Item, loaded.Bitmap is null ? null : ToAvalonia(loaded.Bitmap), loaded.Violations);

        if (page.IsWithheld)
            withheld.TryAdd(item, true);

        return page;
    }

    // Spine targets are checked at open (§8.8), so every item a spread names
    // is in the manifest; failing here means the opener let one through.
    private ManifestItem Item(string id) => package.Manifest.Item(id) ?? throw new InvalidOperationException($"Item '{id}' is in a spread and not in the manifest.");

    // Avalonia copies the pixels, so the Skia bitmap can go as soon as this
    // returns. Both sides are RGBA, premultiplied, which is what the decoder
    // produces.
    private static Bitmap ToAvalonia(SKBitmap bitmap) => new(PixelFormat.Rgba8888, AlphaFormat.Premul, bitmap.GetPixels(), new PixelSize(bitmap.Width, bitmap.Height), new Vector(96, 96), bitmap.RowBytes);
}
