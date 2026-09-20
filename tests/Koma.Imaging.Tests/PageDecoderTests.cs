using Koma.Core.Packaging;
using SkiaSharp;

namespace Koma.Imaging.Tests;

/// <summary>
/// The decoder over the corpus page images: what it produces, and what it
/// refuses before allocating anything.
/// </summary>
public sealed class PageDecoderTests
{
    private const string Valid = "valid-minimal.koma";

    [Theory]
    [InlineData("pages/001.jpg", 800, 1200)]
    [InlineData("pages/002.png", 800, 1200)]
    [InlineData("pages/003.png", 800, 1200)]
    [InlineData("pages/004.webp", 1600, 1200)]
    public void DecodesEveryPageOfAValidPublication(string entry, int width, int height)
    {
        using SKBitmap? bitmap = Decode(Valid, entry, ResourceLimits.Default, out ContainerViolation? violation);

        Assert.Null(violation);
        Assert.NotNull(bitmap);
        Assert.Equal((width, height), (bitmap.Width, bitmap.Height));
    }

    [Fact]
    public void DecodesThePixelsAndNotOnlyTheHeader()
    {
        // PNG is lossless, so the flat field comes back exact.
        using SKBitmap? bitmap = Decode(Valid, "pages/002.png", ResourceLimits.Default, out _);

        Assert.NotNull(bitmap);
        Assert.Equal(new SKColor(220, 220, 220), bitmap.GetPixel(400, 600));
    }

    [Theory]
    [InlineData("pages/001.jpg")]
    [InlineData("pages/002.png")]
    [InlineData("pages/004.webp")]
    public void ConvertsAnEmbeddedProfileToSrgb(string entry)
    {
        // §8.3: (200, 50, 50) in the corpus's wide-gamut profile is about
        // (232, 46, 46) in sRGB. A decoder that ignored the profile would
        // hand back the stored value.
        using SKBitmap? bitmap = Decode("valid-icc-profiles.koma", entry, ResourceLimits.Default, out ContainerViolation? violation);

        Assert.Null(violation);
        Assert.NotNull(bitmap);

        SKColor pixel = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);

        Assert.InRange(pixel.Red, 229, 235);
        Assert.InRange(pixel.Green, 43, 49);
        Assert.InRange(pixel.Blue, 43, 51);
    }

    [Fact]
    public void HonoursPngGamma()
    {
        // No profile, gAMA 1.0: stored grey 128 is linear, about 188 in sRGB.
        using SKBitmap? bitmap = Decode("valid-png-gamma.koma", "pages/002.png", ResourceLimits.Default, out _);

        Assert.NotNull(bitmap);
        Assert.InRange(bitmap.GetPixel(400, 600).Red, 186, 190);
    }

    [Fact]
    public void LeavesAPageWithoutColourInformationAsItIs()
    {
        // Page 3 of the same package has no profile: sRGB already, unchanged.
        using SKBitmap? bitmap = Decode("valid-icc-profiles.koma", "pages/003.png", ResourceLimits.Default, out _);

        Assert.NotNull(bitmap);
        Assert.Equal(new SKColor(220, 220, 220), bitmap.GetPixel(400, 600));
    }

    [Fact]
    public void RendersPixelsAsStoredWhateverTheExifSays()
    {
        // The residue is the producer's fault and the resource pass reports
        // it. The decoder's part in §8.2 is only not to act on it.
        using SKBitmap? bitmap = Decode("L4-residual-exif-orientation.koma", "pages/001.jpg", ResourceLimits.Default, out ContainerViolation? violation);

        Assert.Null(violation);
        Assert.NotNull(bitmap);
        Assert.Equal((800, 1200), (bitmap.Width, bitmap.Height));
    }

    [Theory]
    [InlineData("L4-page-too-wide.koma")]
    [InlineData("L4-page-too-large.koma")]
    public void RefusesAPageBeyondThePixelLimits(string package)
    {
        using SKBitmap? bitmap = Decode(package, "pages/002.png", ResourceLimits.Default, out ContainerViolation? violation);

        Assert.Null(bitmap);
        Assert.Equal(ContainerViolationCode.PagePixelLimit, violation?.Code);
    }

    [Fact]
    public void JudgesTheLimitsOfTheProfileItIsGiven()
    {
        // A page the default profile accepts, refused under a tighter one: the
        // limit comes from the argument and not from a constant.
        ResourceLimits tight = ResourceLimits.Default with { MaxPixelsPerSide = 1_000 };

        using SKBitmap? bitmap = Decode(Valid, "pages/002.png", tight, out ContainerViolation? violation);

        Assert.Null(bitmap);
        Assert.Equal(ContainerViolationCode.PagePixelLimit, violation?.Code);
    }

    [Fact]
    public void RefusesBytesThatAreNotAnImage()
    {
        using SKBitmap? bitmap = PageDecoder.TryDecode("not an image"u8.ToArray(), "pages/002.png", ResourceLimits.Default, out ContainerViolation? violation);

        Assert.Null(bitmap);
        Assert.Equal(ContainerViolationCode.UnreadablePageResource, violation?.Code);
    }

    [Theory]
    [InlineData("pages/001.jpg")]
    [InlineData("pages/002.png")]
    [InlineData("pages/004.webp")]
    public void RefusesATruncatedPage(string entry)
    {
        // The header survives the cut, so every check made before decoding
        // passes; only the decoder can find the rest missing.
        byte[] page = CorpusPage.Read(Valid, entry);

        using SKBitmap? bitmap = PageDecoder.TryDecode(page[..(page.Length / 2)], entry, ResourceLimits.Default, out ContainerViolation? violation);

        Assert.Null(bitmap);
        Assert.Equal(ContainerViolationCode.UnreadablePageResource, violation?.Code);
    }

    private static SKBitmap? Decode(string package, string entry, ResourceLimits limits, out ContainerViolation? violation) => PageDecoder.TryDecode(CorpusPage.Read(package, entry), entry, limits, out violation);
}
