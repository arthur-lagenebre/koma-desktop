namespace Koma.Core.Rendering;

/// <summary>A rectangle in device-independent units, origin at the top left.</summary>
public readonly record struct LayoutRect(double X, double Y, double Width, double Height);

/// <summary>Where one item of a spread goes on screen.</summary>
/// <param name="Item">
/// The item drawn here; for an empty half, the item that half accompanies.
/// </param>
/// <param name="Bounds">The rectangle the item fills, at its declared proportions.</param>
/// <param name="IsEmptyHalf">
/// Whether this is the empty half of a pair, which §10.4 fills with the
/// background of <paramref name="Item"/> and nothing else.
/// </param>
public readonly record struct PlacedItem(string Item, LayoutRect Bounds, bool IsEmptyHalf);

/// <summary>
/// Places the items of a spread in a viewport.
/// </summary>
/// <remarks>
/// <para>
/// §10 fixes which items share a spread and on which side; how large they are
/// drawn is the reading system's business. That part is kept here, free of
/// any interface package, so that it can be tested like the pagination it
/// follows from.
/// </para>
/// <para>
/// Every size comes from the manifest, never from a raster: §16 has the
/// declared dimensions govern layout, and a page whose raster disagrees is
/// scaled to them. Layout therefore never waits on a decode.
/// </para>
/// </remarks>
public static class SpreadLayout
{
    /// <summary>
    /// Whether the viewport takes spread mode.
    /// </summary>
    /// <remarks>
    /// §10.1 asks for spread mode under <c>auto</c> when the viewport is wider
    /// than tall. The same test answers <c>force</c>'s question of whether two
    /// items can be shown side by side: in a viewport taller than wide, two
    /// pages side by side are each drawn smaller than one would be alone.
    /// </remarks>
    public static bool FitsTwo(double width, double height) => width > height;

    /// <summary>The items a spread shows, left to right on screen.</summary>
    public static IEnumerable<string> Items(Spread spread)
    {
        ArgumentNullException.ThrowIfNull(spread);

        return spread switch
        {
            SingleSpread single => [single.Item],
            CenteredSpread centered => [centered.Item],
            PairSpread pair => new[] { pair.Left, pair.Right }.OfType<string>(),
            _ => throw new ArgumentOutOfRangeException(nameof(spread), spread, "Not a spread of §10.")
        };
    }

    /// <summary>
    /// Places a spread in a viewport of the given size.
    /// </summary>
    /// <param name="declaredSize">The declared width and height of an item, from the manifest.</param>
    public static IReadOnlyList<PlacedItem> Arrange(Spread spread, Func<string, (int Width, int Height)> declaredSize, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(spread);
        ArgumentNullException.ThrowIfNull(declaredSize);

        return spread switch
        {
            SingleSpread single => [Whole(single.Item, declaredSize, width, height)],
            CenteredSpread centered => [Whole(centered.Item, declaredSize, width, height)],
            PairSpread pair => Pair(pair, declaredSize, width, height),
            _ => throw new ArgumentOutOfRangeException(nameof(spread), spread, "Not a spread of §10.")
        };
    }

    private static PlacedItem Whole(string item, Func<string, (int Width, int Height)> declaredSize, double width, double height)
    {
        (double itemWidth, double itemHeight) = Size(item, declaredSize);
        double scale = Math.Min(width / itemWidth, height / itemHeight);
        double placedWidth = itemWidth * scale;
        double placedHeight = itemHeight * scale;

        return new PlacedItem(item, new LayoutRect((width - placedWidth) / 2, (height - placedHeight) / 2, placedWidth, placedHeight), IsEmptyHalf: false);
    }

    private static PlacedItem[] Pair(PairSpread pair, Func<string, (int Width, int Height)> declaredSize, double width, double height)
    {
        // §10.4 never leaves both halves empty. An empty half takes the size
        // of the item it accompanies, so that the spread keeps its shape.
        string left = pair.Left ?? pair.Right ?? throw new ArgumentException("A pair with both halves empty is not a spread of §10.4.", nameof(pair));
        string right = pair.Right ?? left;
        double leftAspect = Aspect(left, declaredSize);
        double rightAspect = Aspect(right, declaredSize);

        // Both halves at one height, meeting at the middle of the viewport: the
        // gutter stays where it is as the reader turns pages of unlike widths.
        double pageHeight = Math.Min(height, Math.Min(width / 2 / leftAspect, width / 2 / rightAspect));
        double top = (height - pageHeight) / 2;
        double middle = width / 2;
        double leftWidth = leftAspect * pageHeight;
        double rightWidth = rightAspect * pageHeight;

        return
        [
            new PlacedItem(left, new LayoutRect(middle - leftWidth, top, leftWidth, pageHeight), pair.Left is null),
            new PlacedItem(right, new LayoutRect(middle, top, rightWidth, pageHeight), pair.Right is null)
        ];
    }

    private static double Aspect(string item, Func<string, (int Width, int Height)> declaredSize)
    {
        (double itemWidth, double itemHeight) = Size(item, declaredSize);

        return itemWidth / itemHeight;
    }

    private static (double Width, double Height) Size(string item, Func<string, (int Width, int Height)> declaredSize)
    {
        (int itemWidth, int itemHeight) = declaredSize(item);

        // The schema makes both positive. A zero here would put an infinity
        // into every rectangle of the spread.
        if (itemWidth <= 0 || itemHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(declaredSize), $"Item '{item}' has no usable declared size.");

        return (itemWidth, itemHeight);
    }
}
