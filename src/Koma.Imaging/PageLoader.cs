using System.Collections.ObjectModel;
using Koma.Core.Model;
using Koma.Core.Packaging;
using SkiaSharp;

namespace Koma.Imaging;

/// <summary>
/// One page as a reading system has it: pixels to show, or the reason there
/// are none, and whatever the resource layer found either way.
/// </summary>
public sealed class LoadedPage : IDisposable
{
    internal LoadedPage(ManifestItem item, SKBitmap? bitmap, ReadOnlyCollection<ContainerViolation> violations)
    {
        Item = item;
        Bitmap = bitmap;
        Violations = violations;
    }

    public ManifestItem Item { get; }

    /// <summary>
    /// The raster as stored, or <see langword="null"/> when the page is
    /// withheld. Its size is the raster's own; layout goes by the declared
    /// dimensions of <see cref="Item"/> (§16), which may differ.
    /// </summary>
    public SKBitmap? Bitmap { get; }

    /// <summary>
    /// Everything the resource layer found. A shown page with any of these
    /// must say so to the user (§16), and so must a withheld one.
    /// </summary>
    public ReadOnlyCollection<ContainerViolation> Violations { get; }

    /// <summary>
    /// Whether the page stays off screen. It keeps its place in the spine all
    /// the same, and the publication is no longer complete (§16).
    /// </summary>
    public bool IsWithheld => Bitmap is null;

    public void Dispose() => Bitmap?.Dispose();
}

/// <summary>
/// Checks a page when the reader reaches it, then decodes it if §16 lets it
/// be shown.
/// </summary>
/// <remarks>
/// The page is read once: the checks hand back the bytes they read, and those
/// are the bytes decoded. The decoder repeats its own header and limit checks
/// regardless, so that it stays safe for a caller that reaches it by another
/// road.
/// </remarks>
public static class PageLoader
{
    /// <exception cref="InsufficientMemoryException">
    /// The page is within the limits and its pixels could still not be allocated.
    /// </exception>
    public static LoadedPage Load(KomaPackage package, ManifestItem item)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(item);

        var violations = new List<ContainerViolation>();
        byte[]? bytes = PageResourceChecks.Check(package, item, violations);

        if (bytes is null || violations.Exists(PageResourceChecks.Withholds))
            return new LoadedPage(item, null, violations.AsReadOnly());

        SKBitmap? bitmap = PageDecoder.TryDecode(bytes, item.Href, package.Limits, out ContainerViolation? violation);

        if (violation is not null)
            violations.Add(violation);

        return new LoadedPage(item, bitmap, violations.AsReadOnly());
    }
}
