using Koma.Core.Model;
using Koma.Core.Packaging;
using SkiaSharp;

namespace Koma.Imaging.Tests;

/// <summary>
/// §16 over the corpus: which pages in error are shown, which are withheld,
/// and that a shown one still carries its fault.
/// </summary>
public sealed class PageLoaderTests
{
    [Fact]
    public void ShowsEveryPageOfAValidPublication()
    {
        using KomaPackage package = Open("valid-minimal.koma");

        foreach (ManifestItem item in package.Manifest.Items)
        {
            using LoadedPage page = PageLoader.Load(package, item);

            Assert.False(page.IsWithheld, $"{item.Id} was withheld.");
            Assert.Empty(page.Violations);
        }
    }

    [Theory]
    [InlineData("L4-residual-exif-orientation.koma", "p001", ContainerViolationCode.ExifOrientationResidue)]
    [InlineData("L4-failed-checksum.koma", "p001", ContainerViolationCode.ChecksumMismatch)]
    [InlineData("L4-media-type-mismatch.koma", "p002", ContainerViolationCode.MediaTypeMismatch)]
    [InlineData("L4-wrong-dimensions.koma", "p002", ContainerViolationCode.DimensionsMismatch)]
    [InlineData("L4-animated-page.koma", "p004", ContainerViolationCode.AnimatedPageResource)]
    public void ShowsAPageWhoseFaultLeavesItSafeToShow(string package, string id, string code)
    {
        using LoadedPage page = Load(package, id);

        Assert.False(page.IsWithheld);
        Assert.Contains(page.Violations, v => v.Code == code);
    }

    [Theory]
    [InlineData("L4-page-too-wide.koma")]
    [InlineData("L4-page-too-large.koma")]
    public void WithholdsAPageBeyondThePixelLimits(string package)
    {
        using LoadedPage page = Load(package, "p002");

        Assert.True(page.IsWithheld);
        Assert.Contains(page.Violations, v => v.Code == ContainerViolationCode.PagePixelLimit);
    }

    [Fact]
    public void ShowsTheRasterAsItIsAndLeavesTheDeclaredSizeToLayout()
    {
        // The manifest says 801 wide and the raster is 800. §16 has the page
        // scaled to what is declared, which is the renderer's work: the
        // loader hands over the raster untouched.
        using LoadedPage page = Load("L4-wrong-dimensions.koma", "p002");

        Assert.NotNull(page.Bitmap);
        Assert.Equal(800, page.Bitmap.Width);
        Assert.Equal(801, page.Item.Width);
    }

    [Fact]
    public void ShowsTheFirstFrameOfAnAnimatedPage()
    {
        // The corpus animation is red, then blue. Lossy WebP may move a flat
        // field by a step or two.
        using LoadedPage page = Load("L4-animated-page.koma", "p004");

        Assert.NotNull(page.Bitmap);
        SKColor pixel = page.Bitmap.GetPixel(800, 600);
        Assert.InRange(pixel.Red, 250, 255);
        Assert.InRange(pixel.Blue, 0, 5);
    }

    private static LoadedPage Load(string package, string id)
    {
        using KomaPackage opened = Open(package);
        ManifestItem? item = opened.Manifest.Item(id);
        Assert.NotNull(item);

        return PageLoader.Load(opened, item);
    }

    private static KomaPackage Open(string package)
    {
        // The package owns the stream from here and closes it when disposed.
        PackageOpenResult result = PackageOpener.Open(File.OpenRead(CorpusPage.PathOf(package)));

        Assert.Equal(PackageOpenOutcome.Opened, result.Outcome);
        Assert.NotNull(result.Package);

        return result.Package;
    }
}
