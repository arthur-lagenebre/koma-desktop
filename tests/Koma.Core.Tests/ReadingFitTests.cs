using Koma.Core.Rendering;

namespace Koma.Core.Tests;

/// <summary>
/// The size a spread is drawn at: fitted to the page or to the width, then
/// zoomed, always in the spread's own proportions.
/// </summary>
public sealed class ReadingFitTests
{
    [Fact]
    public void FitsTheWholeSpreadInsideTheWindow()
    {
        // A double page, 3:2, in a window 1600 by 800: the height decides.
        Assert.Equal((1200d, 800d), ReadingFit.Canvas(1.5, 1600, 800, FitMode.Page, 1));
    }

    [Fact]
    public void FitsTheWidthAndLetsTheHeightRun()
    {
        // A single page, 2:3, as wide as the window: taller than it, scrolled.
        Assert.Equal((800d, 1200d), ReadingFit.Canvas(2.0 / 3, 800, 600, FitMode.Width, 1));
    }

    [Fact]
    public void ZoomsTheSpreadAndNotTheWindow()
    {
        // Twice the fitted spread, not twice the window: nothing to scroll
        // but pages.
        Assert.Equal((2400d, 1600d), ReadingFit.Canvas(1.5, 1600, 800, FitMode.Page, 2));
    }

    [Fact]
    public void MeasuresTheAspectOfASpreadAsLaidOut()
    {
        PlacedItem[] placed =
        [
            new("p002", new LayoutRect(100, 50, 400, 600), false),
            new("p003", new LayoutRect(500, 50, 400, 600), false)
        ];

        Assert.Equal(800.0 / 600, ReadingFit.Aspect(placed), 9);
    }

    [Theory]
    [InlineData(1, 4, 2)]
    [InlineData(1, -4, 0.5)]
    [InlineData(0.9, 1, 1)]
    [InlineData(8, 1, 8)]
    [InlineData(0.25, -1, 0.25)]
    public void StepsTheZoomWithinBoundsAndBackToOne(double zoom, int steps, double expected)
    {
        // Four steps double; a step across 1 lands on 1; the bounds hold.
        Assert.Equal(expected, ReadingFit.Zoom(zoom, steps), 9);
    }
}
