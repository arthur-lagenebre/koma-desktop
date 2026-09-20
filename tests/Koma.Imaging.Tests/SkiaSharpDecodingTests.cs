using Koma.Core.Model;
using SkiaSharp;

namespace Koma.Imaging.Tests;

/// <summary>
/// What SkiaSharp does with the page images §16 requires a reading system to
/// handle.
/// </summary>
/// <remarks>
/// <para>
/// These characterise a dependency, as <c>MimetypeEntryTests</c> does for
/// <c>System.IO.Compression</c>: the render pipeline is built on their outcome,
/// and an upgrade that changes it must fail here before it rotates a page on
/// screen.
/// </para>
/// <para>
/// §8.2 requires pixels to be rendered as stored. Skia has two roads from
/// encoded bytes to pixels and they disagree: <see cref="SKCodec"/> reports the
/// EXIF origin and leaves the pixels alone, the lazily decoded
/// <see cref="SKImage"/> applies it. Only the first is usable. Both are pinned,
/// so that the reason for avoiding the second stays on record.
/// </para>
/// <para>
/// The binding is not the native library. <see cref="SKCodec.FrameCount"/> is
/// the size of Skia's frame info, which is empty for a still image, where the
/// native frame count would be 1. Probing Skia directly predicted the wrong
/// value, which is why these tests go through SkiaSharp and nothing else.
/// </para>
/// </remarks>
public sealed class SkiaSharpDecodingTests
{
    private const string Valid = "valid-minimal.koma";
    private const string Animated = "L4-animated-page.koma";
    private const string ExifResidue = "L4-residual-exif-orientation.koma";
    private const string ExifPage = "pages/001.jpg";

    [Fact]
    public void Codec_DecodesStaticWebP()
    {
        using SKCodec codec = OpenCodec(CorpusPage.Read(Valid, "pages/004.webp"));
        using SKBitmap bitmap = DecodeAsStored(codec);

        Assert.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat);
        Assert.Equal(0, codec.FrameCount);
        Assert.Equal((1600, 1200), (bitmap.Width, bitmap.Height));
        // A header reader would pass everything above; only a pixel proves the
        // VP8 stream was decoded. Lossy WebP may move a flat field by a step.
        AssertGrey(220, bitmap.GetPixel(800, 600), tolerance: 2);
    }

    [Fact]
    public void Codec_SeesTheFramesOfAnAnimatedWebP()
    {
        byte[] bytes = CorpusPage.Read(Animated, "pages/004.webp");
        using SKCodec codec = OpenCodec(bytes);

        // If the decoder and the header reader disagreed on what counts as
        // animated, a page the resource checks refuse could still be shown as
        // its first frame, or the reverse.
        Assert.Equal(2, codec.FrameCount);
        Assert.True(PageImageReader.TryRead(bytes)?.IsAnimated);
    }

    [Fact]
    public void Codec_ReportsExifOrientationWithoutApplyingIt()
    {
        byte[] bytes = CorpusPage.Read(ExifResidue, ExifPage);
        using SKCodec codec = OpenCodec(bytes);
        using SKBitmap bitmap = DecodeAsStored(codec);

        Assert.Equal(SKEncodedOrigin.RightTop, codec.EncodedOrigin);
        Assert.Equal(6, PageImageReader.TryRead(bytes)?.ExifOrientation);
        Assert.Equal((800, 1200), (bitmap.Width, bitmap.Height));
    }

    [Fact]
    public void BitmapDecode_IgnoresExifOrientation()
    {
        using SKBitmap? bitmap = SKBitmap.Decode(CorpusPage.Read(ExifResidue, ExifPage));

        Assert.NotNull(bitmap);
        Assert.Equal((800, 1200), (bitmap.Width, bitmap.Height));
    }

    [Fact]
    public void EncodedImage_AppliesExifOrientation()
    {
        using SKData data = SKData.CreateCopy(CorpusPage.Read(ExifResidue, ExifPage));
        using SKImage? image = SKImage.FromEncodedData(data);

        Assert.NotNull(image);
        // Swapped: the double rotation §8.2 exists to prevent. This is why no
        // page is ever decoded through SKImage.FromEncodedData, nor through a
        // wrapper that might use it — Avalonia's Bitmap(Stream) included.
        Assert.Equal((1200, 800), (image.Width, image.Height));
    }

    [Theory]
    [InlineData("pages/001.jpg")]
    [InlineData("pages/002.png")]
    [InlineData("pages/004.webp")]
    public void Codec_AgreesWithHeaderReader(string entry)
    {
        byte[] bytes = CorpusPage.Read(Valid, entry);
        using SKCodec codec = OpenCodec(bytes);
        PageImageFacts? facts = PageImageReader.TryRead(bytes);

        // §13.1 caps pixels per page, and the cap can only hold before decoding
        // if the header reader's dimensions are the ones the decoder allocates.
        Assert.NotNull(facts);
        Assert.Equal((facts.Width, facts.Height), (codec.Info.Width, codec.Info.Height));
        // Zero is the binding's word for a still image in every format, not a
        // quirk of WebP.
        Assert.Equal(0, codec.FrameCount);
    }

    private static SKCodec OpenCodec(byte[] bytes)
    {
        // The native codec takes its own reference to the data.
        using SKData data = SKData.CreateCopy(bytes);
        SKCodec? codec = SKCodec.Create(data);
        Assert.NotNull(codec);
        return codec;
    }

    private static SKBitmap DecodeAsStored(SKCodec codec)
    {
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        Assert.Equal(SKCodecResult.Success, codec.GetPixels(info, bitmap.GetPixels()));
        return bitmap;
    }

    private static void AssertGrey(int expected, SKColor actual, int tolerance)
    {
        Assert.InRange(actual.Red, expected - tolerance, expected + tolerance);
        Assert.InRange(actual.Green, expected - tolerance, expected + tolerance);
        Assert.InRange(actual.Blue, expected - tolerance, expected + tolerance);
    }
}
