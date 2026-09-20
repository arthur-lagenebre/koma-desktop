using System.Globalization;
using Koma.Core.Model;
using Koma.Core.Packaging;
using SkiaSharp;

namespace Koma.Imaging;

/// <summary>
/// Turns the bytes of a page resource into pixels, as stored.
/// </summary>
/// <remarks>
/// <para>
/// The order is the contract. The header is held against the pixel limits of
/// §13.1 before the decoder sees a byte, and the decoder's own reading of the
/// dimensions must agree with the header's before anything is allocated: a
/// limit judged on one size and an allocation made for another would be no
/// limit at all.
/// </para>
/// <para>
/// Decoding goes through <see cref="SKCodec"/> only, which reports EXIF
/// orientation without applying it (§8.2); <c>SkiaSharpDecodingTests</c> is
/// where that is established. Embedded colour profiles are not applied yet,
/// which leaves the data treated as sRGB: the fallback §8.3 allows.
/// </para>
/// <para>
/// This answers whether a page can be decoded, not whether it should be shown.
/// Which faults of the resource layer keep a page off screen is for the caller
/// to decide.
/// </para>
/// </remarks>
public static class PageDecoder
{
    /// <summary>
    /// Decodes a page resource, or says why it will not.
    /// </summary>
    /// <returns>
    /// A bitmap the caller owns and disposes, or <see langword="null"/> with
    /// <paramref name="violation"/> set.
    /// </returns>
    /// <exception cref="InsufficientMemoryException">
    /// The page is within the limits and the pixels could still not be allocated.
    /// </exception>
    public static SKBitmap? TryDecode(byte[] bytes, string entryName, ResourceLimits limits, out ContainerViolation? violation)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(entryName);
        ArgumentNullException.ThrowIfNull(limits);

        violation = null;
        PageImageFacts? facts = PageImageReader.TryRead(bytes);

        if (facts is null)
        {
            violation = Unreadable(entryName, "Not a JPEG, PNG or WebP whose header can be read (§8.1).");
            return null;
        }

        if (limits.ExceedsPixelLimits(facts.Width, facts.Height))
        {
            violation = new ContainerViolation(ContainerViolationCode.PagePixelLimit, entryName, string.Create(CultureInfo.InvariantCulture, $"{facts.Width}x{facts.Height} is beyond {limits.MaxPixelsPerSide} pixels per side or {limits.MaxPixelsPerPage} per page (§13.1)."));
            return null;
        }

        using SKData data = SKData.CreateCopy(bytes);
        using SKCodec? codec = SKCodec.Create(data);

        if (codec is null)
        {
            violation = Unreadable(entryName, "The decoder does not recognise it (§8.1).");
            return null;
        }

        if (codec.Info.Width != facts.Width || codec.Info.Height != facts.Height)
        {
            violation = Unreadable(entryName, string.Create(CultureInfo.InvariantCulture, $"The header says {facts.Width}x{facts.Height} and the decoder reads {codec.Info.Width}x{codec.Info.Height}; the limits of §13.1 were judged on the first."));
            return null;
        }

        var info = new SKImageInfo(facts.Width, facts.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap();

        // Within the limits, a failed allocation is the machine's shortfall and
        // not a fault of the page, so it is not reported as one.
        if (!bitmap.TryAllocPixels(info))
        {
            bitmap.Dispose();
            throw new InsufficientMemoryException(string.Create(CultureInfo.InvariantCulture, $"Could not allocate {facts.Width}x{facts.Height} pixels for {entryName}."));
        }

        SKCodecResult result = codec.GetPixels(info, bitmap.GetPixels());

        // Anything short of complete is a page that cannot be decoded. Skia
        // hands back a truncated stream with the missing rows left blank, and
        // shown as it is, a damaged page would pass for an odd one.
        if (result != SKCodecResult.Success)
        {
            bitmap.Dispose();
            violation = Unreadable(entryName, $"Decoding stopped with {result} (§8.1).");
            return null;
        }

        return bitmap;
    }

    private static ContainerViolation Unreadable(string entryName, string message) => new(ContainerViolationCode.UnreadablePageResource, entryName, message);
}
