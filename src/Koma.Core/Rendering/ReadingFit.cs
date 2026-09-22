namespace Koma.Core.Rendering;

/// <summary>How a spread is fitted to the window before any zoom.</summary>
public enum FitMode
{
    /// <summary>The whole spread in view: the way a page is first seen.</summary>
    Page,

    /// <summary>The spread as wide as the window, scrolled down: the way a dense page is read.</summary>
    Width
}

/// <summary>
/// The size a spread is drawn at, from the window, a fit and a zoom.
/// </summary>
/// <remarks>
/// A spread keeps its proportions whatever the fit (§10.5): the canvas is the
/// spread's own shape scaled, never the window's shape, so that zooming in
/// scrolls over pages and not over the bands beside them. Where the canvas is
/// smaller than the window, the window centres it.
/// </remarks>
public static class ReadingFit
{
    public const double MinimumZoom = 0.25;

    public const double MaximumZoom = 8;

    /// <summary>One zoom step: four steps double the size.</summary>
    public const double Step = 1.189207115002721;

    /// <summary>The width over the height of a spread as laid out, whatever the size it was laid out at.</summary>
    public static double Aspect(IReadOnlyList<PlacedItem> placed)
    {
        ArgumentNullException.ThrowIfNull(placed);

        if (placed.Count == 0)
            return 1;

        double left = placed.Min(p => p.Bounds.X);
        double top = placed.Min(p => p.Bounds.Y);
        double right = placed.Max(p => p.Bounds.X + p.Bounds.Width);
        double bottom = placed.Max(p => p.Bounds.Y + p.Bounds.Height);

        return bottom > top ? (right - left) / (bottom - top) : 1;
    }

    /// <summary>
    /// The canvas for a spread of this aspect in a window of this size.
    /// </summary>
    public static (double Width, double Height) Canvas(double aspect, double windowWidth, double windowHeight, FitMode mode, double zoom)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(aspect);

        if (windowWidth <= 0 || windowHeight <= 0)
            return (0, 0);

        double width = mode == FitMode.Width ? windowWidth : Math.Min(windowWidth, windowHeight * aspect);
        double scale = Math.Clamp(zoom, MinimumZoom, MaximumZoom);

        return (width * scale, width / aspect * scale);
    }

    /// <summary>The zoom one step in or out, within the bounds, snapping back to 1 when a step passes it.</summary>
    public static double Zoom(double zoom, int steps)
    {
        double next = Math.Clamp(zoom * Math.Pow(Step, steps), MinimumZoom, MaximumZoom);

        // Rounding over many steps would leave 0.9999 where the reader meant
        // the size the fit gives; a step that crosses 1 lands on it.
        return (zoom < 1 && next > 1) || (zoom > 1 && next < 1) || Math.Abs(next - 1) < 1e-9 ? 1 : next;
    }
}
