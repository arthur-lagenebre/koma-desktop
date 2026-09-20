using Avalonia;
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
/// Pages are decoded when the reader reaches them and dropped once the reader
/// has moved two spreads away: a long publication read to the end must not
/// hold every page it has shown.
/// </remarks>
internal sealed class Publication(KomaPackage package, IReadOnlyList<ContainerViolation> openingNotes) : IDisposable
{
    private readonly Dictionary<string, ShownPage> pages = [];
    private readonly HashSet<string> withheld = [];
    private bool? fitsTwo;

    public ReadingDirection Direction => package.Metadata.Direction;

    /// <summary>The warnings the opener raised, which the reader is shown throughout.</summary>
    public IReadOnlyList<ContainerViolation> OpeningNotes { get; } = openingNotes;

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

    public ShownPage Page(string item)
    {
        if (pages.TryGetValue(item, out ShownPage? page))
            return page;

        using LoadedPage loaded = PageLoader.Load(package, Item(item));
        page = new ShownPage(loaded.Item, loaded.Bitmap is null ? null : ToAvalonia(loaded.Bitmap), loaded.Violations);
        pages.Add(item, page);

        if (page.IsWithheld)
            withheld.Add(item);

        return page;
    }

    /// <summary>Drops every decoded page but those of the given items.</summary>
    public void Retain(IEnumerable<string> items)
    {
        HashSet<string> keep = [.. items];

        foreach (string item in pages.Keys.Where(item => !keep.Contains(item)).ToList())
        {
            pages[item].Dispose();
            pages.Remove(item);
        }
    }

    public void Dispose()
    {
        foreach (ShownPage page in pages.Values)
            page.Dispose();

        pages.Clear();
        package.Dispose();
    }

    // Spine targets are checked at open (§8.8), so every item a spread names
    // is in the manifest; failing here means the opener let one through.
    private ManifestItem Item(string id) => package.Manifest.Item(id) ?? throw new InvalidOperationException($"Item '{id}' is in a spread and not in the manifest.");

    // Avalonia copies the pixels, so the Skia bitmap can go as soon as this
    // returns. Both sides are RGBA, premultiplied, which is what the decoder
    // produces.
    private static Bitmap ToAvalonia(SKBitmap bitmap) => new(PixelFormat.Rgba8888, AlphaFormat.Premul, bitmap.GetPixels(), new PixelSize(bitmap.Width, bitmap.Height), new Vector(96, 96), bitmap.RowBytes);
}
