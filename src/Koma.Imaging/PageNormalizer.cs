using System.Collections.ObjectModel;
using Koma.Core.Model;
using SkiaSharp;

namespace Koma.Imaging;

/// <summary>
/// A page as a package can carry it.
/// </summary>
/// <param name="Notes">
/// What was assumed or given up on the way, in words a person converting a
/// library can act on. Empty when the page passed through untouched.
/// </param>
public sealed record NormalizedPage(byte[] Data, string MediaType, int Width, int Height, ReadOnlyCollection<string> Notes);

/// <summary>
/// Turns an image from an archive into a page §8.1 allows, deciding as
/// <c>tools/cbz_to_koma.py</c> decides.
/// </summary>
/// <remarks>
/// <para>
/// A page that is already a static RGB JPEG, PNG or WebP with no EXIF
/// rotation passes through byte for byte: JPEG data is never recompressed,
/// and an embedded profile survives because nothing touched it.
/// </para>
/// <para>
/// Anything else is decoded once. Only the first frame of an animation is
/// kept, an EXIF rotation is applied to the pixels (§8.2), and CMYK becomes
/// RGB. A JPEG is written back as a JPEG at quality 95, which costs less
/// than promoting an already lossy page to PNG; everything else becomes PNG.
/// </para>
/// <para>
/// The decisions are the reference converter's; the bytes cannot be. Skia
/// and Pillow do not write the same PNG, so two tools agree on which pages
/// were re-encoded, into what and at which size, not on the files.
/// </para>
/// </remarks>
public static class PageNormalizer
{
    private const int JpegQuality = 95;

    /// <param name="name">The page's name in its archive, for the notes.</param>
    /// <returns>The page, or <see langword="null"/> when nothing here can decode it.</returns>
    public static NormalizedPage? Normalize(byte[] data, string name)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(name);

        PageImageFacts? facts = PageImageReader.TryRead(data);

        if (facts is { IsAnimated: false, IsCmyk: false } && facts.ExifOrientation is null or 1)
            return new NormalizedPage(data, facts.MediaType, facts.Width, facts.Height, ReadOnlyCollection<string>.Empty);

        using SKData encoded = SKData.CreateCopy(data);
        using SKCodec? codec = SKCodec.Create(encoded);

        // Skia reads GIF and BMP, not TIFF, where Pillow reads all three: a
        // page the reference converter keeps can be one this one cannot.
        if (codec is null)
            return null;

        var notes = new List<string>();
        bool jpeg = codec.EncodedFormat == SKEncodedImageFormat.Jpeg;

        // The reference converter says it once: an animated WebP loses its
        // animation and its format together.
        if (codec.FrameCount > 1)
            notes.Add($"{name}: animated image, only the first frame is kept" + (codec.EncodedFormat == SKEncodedImageFormat.Webp ? ", and the page is re-encoded as PNG" : string.Empty));

        using SKBitmap? decoded = Decode(codec, facts?.IsCmyk ?? false);

        if (decoded is null)
            return null;

        using SKBitmap page = Orient(decoded, codec.EncodedOrigin);

        if (codec.EncodedOrigin != SKEncodedOrigin.TopLeft)
            notes.Add($"{name}: EXIF orientation {(int)codec.EncodedOrigin} applied to the pixels");

        // A profile describes the ink of the CMYK source, which the written
        // page no longer has, so none is carried over.
        if (facts?.IsCmyk ?? false)
            notes.Add($"{name}: CMYK converted to RGB, outside the base profile; its profile, which describes the CMYK source, is not carried over");

        if (facts is null)
            notes.Add($"{name}: {codec.EncodedFormat.ToString().ToUpperInvariant()} re-encoded as PNG");

        using SKData? written = Encode(page, jpeg);

        if (written is null)
            return null;

        return new NormalizedPage(written.ToArray(), jpeg ? "image/jpeg" : "image/png", page.Width, page.Height, notes.AsReadOnly());
    }

    /// <remarks>
    /// JPEG at quality 95 without chroma subsampling, as the reference
    /// converter writes it: the page was lossy already, and halving its colour
    /// resolution on top would be a second loss nobody asked for.
    /// </remarks>
    private static SKData? Encode(SKBitmap page, bool jpeg)
    {
        using SKPixmap pixels = page.PeekPixels();

        return jpeg ? pixels.Encode(new SKJpegEncoderOptions(JpegQuality, SKJpegEncoderDownsample.Downsample444, SKJpegEncoderAlphaOption.Ignore)) : pixels.Encode(SKPngEncoderOptions.Default);
    }

    /// <remarks>
    /// Decoded in the page's own colour space, so that the pixels keep their
    /// values and the encoder describes them with the profile they came with;
    /// only CMYK is brought to sRGB, since its profile describes ink that the
    /// written page no longer has.
    /// </remarks>
    private static SKBitmap? Decode(SKCodec codec, bool cmyk)
    {
        SKColorSpace? space = cmyk ? SKColorSpace.CreateSrgb() : codec.Info.ColorSpace;

        // Opaque stays opaque, so that a page with no transparency is not
        // written with an alpha channel it never had.
        SKAlphaType alpha = codec.Info.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Premul;
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, alpha, space);
        var bitmap = new SKBitmap();

        if (!bitmap.TryAllocPixels(info) || codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success)
        {
            bitmap.Dispose();
            return null;
        }

        return bitmap;
    }

    /// <summary>
    /// Applies an EXIF orientation to the pixels, as §8.2 asks of a producer.
    /// </summary>
    /// <remarks>
    /// Each origin is the matrix that takes a stored pixel to where a reader
    /// should see it; the eight were checked against Pillow's exif_transpose,
    /// which the reference converter uses.
    /// </remarks>
    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        float w = source.Width;
        float h = source.Height;
        bool turned = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

        SKMatrix matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => SKMatrix.Identity
        };

        var info = source.Info.WithSize(turned ? source.Height : source.Width, turned ? source.Width : source.Height);
        var oriented = new SKBitmap(info);

        using var canvas = new SKCanvas(oriented);
        canvas.SetMatrix(matrix);
        canvas.DrawBitmap(source, 0, 0);

        return oriented;
    }
}
