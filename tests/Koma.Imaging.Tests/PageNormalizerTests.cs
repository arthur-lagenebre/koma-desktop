using System.IO.Compression;
using Koma.TestSupport;
using SkiaSharp;

namespace Koma.Imaging.Tests;

/// <summary>
/// Pages made fit for a package, decided as <c>tools/cbz_to_koma.py</c>
/// decides for the same images.
/// </summary>
/// <remarks>
/// The expected media types, sizes and notes are the reference converter's
/// output on these fixtures, notes word for word. The bytes of a re-encoded
/// page cannot be: Skia and Pillow do not write the same file.
/// </remarks>
public sealed class PageNormalizerTests
{
    [Theory]
    [InlineData("bare.cbz", "001.png")]
    [InlineData("manga.cbz", "page10.jpg")]
    public void PassesAConformingPageThroughByteForByte(string cbz, string entry)
    {
        byte[] data = Read(cbz, entry);

        NormalizedPage? page = PageNormalizer.Normalize(data, entry);

        Assert.NotNull(page);
        Assert.Equal(data, page.Data);
        Assert.Empty(page.Notes);
    }

    [Theory]
    [InlineData("cover.jpg", "image/jpeg", 800, 1200, new[] { "cover.jpg: EXIF orientation 6 applied to the pixels" })]
    [InlineData("002.gif", "image/png", 600, 900, new[] { "002.gif: animated image, only the first frame is kept", "002.gif: GIF re-encoded as PNG" })]
    [InlineData("003.bmp", "image/png", 600, 900, new[] { "003.bmp: BMP re-encoded as PNG" })]
    public void ReEncodesWhatAPackageCannotCarryAsTheReferenceConverterDoes(string entry, string mediaType, int width, int height, string[] notes)
    {
        NormalizedPage? page = PageNormalizer.Normalize(Read("messy.cbz", entry), entry);

        Assert.NotNull(page);
        Assert.Equal((mediaType, width, height), (page.MediaType, page.Width, page.Height));
        Assert.Equal(notes, page.Notes);

        // Whatever came out is a page a package can carry, and says so itself.
        using SKBitmap? decoded = SKBitmap.Decode(page.Data);
        Assert.NotNull(decoded);
        Assert.Equal((width, height), (decoded.Width, decoded.Height));
    }

    [Fact]
    public void TurnsThePixelsTheWayTheOrientationSays()
    {
        // 40 by 20, red in the top-left quarter, stored with orientation 6: a
        // reader should see it turned a quarter clockwise, 20 by 40, with the
        // red in the top-right quarter. The flat fixtures cannot tell a
        // clockwise turn from an anticlockwise one; this can.
        NormalizedPage? page = PageNormalizer.Normalize(WithOrientation(RedCorner(), 6), "turned.jpg");

        Assert.NotNull(page);
        Assert.Equal((20, 40), (page.Width, page.Height));

        using SKBitmap? decoded = SKBitmap.Decode(page.Data);
        Assert.NotNull(decoded);
        Assert.True(decoded.GetPixel(15, 5).Red > 200 && decoded.GetPixel(15, 5).Green < 60, "The top-right quarter should be red.");
        Assert.True(decoded.GetPixel(5, 5).Green > 200, "The top-left quarter should be white.");
    }

    private static byte[] RedCorner()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(40, 20, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var red = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(0, 0, 20, 10, red);
        }

        using SKPixmap pixels = bitmap.PeekPixels();
        using SKData? jpeg = pixels.Encode(new SKJpegEncoderOptions(100, SKJpegEncoderDownsample.Downsample444, SKJpegEncoderAlphaOption.Ignore));
        Assert.NotNull(jpeg);

        return jpeg.ToArray();
    }

    /// <summary>
    /// A JPEG with an EXIF segment carrying one tag, the orientation, spliced
    /// in after the start-of-image marker. Big-endian, as a camera writes it.
    /// </summary>
    private static byte[] WithOrientation(byte[] jpeg, ushort orientation)
    {
        byte[] exif =
        [
            0xFF, 0xE1, 0x00, 0x22,
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0x00, 0x00,
            (byte)'M', (byte)'M', 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08,
            0x00, 0x01,
            0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, (byte)(orientation >> 8), (byte)orientation, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00
        ];

        return [.. jpeg[..2], .. exif, .. jpeg[2..]];
    }

    private static byte[] Read(string cbz, string entry)
    {
        using ZipArchive archive = ZipFile.OpenRead(Corpus.Example(cbz));
        using Stream stream = archive.GetEntry(entry)!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        return buffer.ToArray();
    }
}
