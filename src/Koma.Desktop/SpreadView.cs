using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Koma.Core.Rendering;

namespace Koma.Desktop;

/// <summary>
/// Draws one spread, as <see cref="SpreadLayout"/> places it.
/// </summary>
internal sealed class SpreadView : Control
{
    private static readonly IBrush Backdrop = new ImmutableSolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));

    // A withheld page keeps its place in the spread (§16). This is the place,
    // visibly empty, so that the reader sees a page is missing rather than a
    // spread that looks complete.
    private static readonly IBrush Withheld = new ImmutableSolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A));

    private Publication? publication;
    private Spread? spread;

    public SpreadView()
    {
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    public void Show(Publication? shown, Spread? spreadShown)
    {
        publication = shown;
        spread = spreadShown;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.FillRectangle(Backdrop, new Rect(Bounds.Size));

        if (publication is null || spread is null)
            return;

        foreach (PlacedItem placed in SpreadLayout.Arrange(spread, publication.DeclaredSize, Bounds.Width, Bounds.Height))
        {
            var bounds = new Rect(placed.Bounds.X, placed.Bounds.Y, placed.Bounds.Width, placed.Bounds.Height);

            // §10.5 composites over the item's background, and §10.4 fills an
            // empty half with its companion's.
            context.FillRectangle(publication.Background(placed.Item), bounds);

            if (placed.IsEmptyHalf)
                continue;

            // Still being decoded: the background holds the place until the
            // page is ready and the window asks for another frame.
            ShownPage? page = publication.Loaded(placed.Item);

            if (page is null)
                continue;

            // The whole raster goes into the declared box: §16 scales a page
            // whose raster disagrees with its declaration to the declaration.
            if (page.Image is null)
                context.FillRectangle(Withheld, bounds);
            else
                context.DrawImage(page.Image, new Rect(page.Image.Size), bounds);
        }
    }
}
