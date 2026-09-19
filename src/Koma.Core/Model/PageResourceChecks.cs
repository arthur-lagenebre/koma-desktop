using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using Koma.Core.Packaging;

namespace Koma.Core.Model;

/// <summary>
/// The resource layer of §15: what the page bytes say against what the manifest
/// declares about them.
/// </summary>
/// <remarks>
/// <para>
/// This is a pass of its own and not a step of
/// <see cref="PackageOpener.Open"/>, deliberately. Every check here reads a
/// whole page resource, so running them at open time would decompress the
/// entire publication before the first page could be shown, and would make
/// scanning a library cost as much as reading it. A reading system verifies a
/// page when it comes to that page; a validator runs this over everything.
/// </para>
/// <para>
/// The distinction is the same one §15 draws between its layers, and it is why
/// the caller decides when to pay for this rather than the opener deciding for
/// them.
/// </para>
/// </remarks>
public static class PageResourceChecks
{
    /// <summary>
    /// Checks every declared page resource.
    /// </summary>
    public static ReadOnlyCollection<ContainerViolation> CheckAll(KomaPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var violations = new List<ContainerViolation>();

        foreach (ManifestItem item in package.Manifest.Items)
            Check(package, item, violations);

        return violations.AsReadOnly();
    }

    /// <summary>
    /// Checks one page resource.
    /// </summary>
    public static void Check(KomaPackage package, ManifestItem item, List<ContainerViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(violations);

        byte[]? bytes = Read(package, item, violations);

        if (bytes is null)
            return;

        if (item.Sha256 is not null)
        {
            string actual = Convert.ToHexStringLower(SHA256.HashData(bytes));

            // §8.6 takes the digest over the uncompressed image bytes, so a
            // mismatch is about the resource and not about the archive; the
            // ZIP has its own CRC and it passed.
            if (!string.Equals(actual, item.Sha256, StringComparison.OrdinalIgnoreCase))
                violations.Add(new ContainerViolation(ContainerViolationCode.ChecksumMismatch, item.Href, $"Item '{item.Id}' declares {item.Sha256} and the bytes hash to {actual} (§8.6)."));
        }

        PageImageFacts? facts = PageImageReader.TryRead(bytes);

        if (facts is null)
        {
            violations.Add(new ContainerViolation(ContainerViolationCode.UnreadablePageResource, item.Href, $"Item '{item.Id}' is not a readable JPEG, PNG or WebP (§8.1)."));
            return;
        }

        // §8.1: the declared media type must match the actual byte signature.
        // Checked before the dimensions, since a file read as the wrong format
        // would give dimensions that mean nothing.
        if (facts.MediaType != item.MediaType)
        {
            violations.Add(new ContainerViolation(ContainerViolationCode.MediaTypeMismatch, item.Href, $"Item '{item.Id}' declares {item.MediaType} and the bytes are {facts.MediaType} (§8.1)."));
            return;
        }

        if (facts.Width != item.Width || facts.Height != item.Height)
            violations.Add(new ContainerViolation(ContainerViolationCode.DimensionsMismatch, item.Href, string.Create(CultureInfo.InvariantCulture, $"Item '{item.Id}' declares {item.Width}x{item.Height} and the bytes are {facts.Width}x{facts.Height} (§8.2).")));

        if (facts.IsAnimated)
            violations.Add(new ContainerViolation(ContainerViolationCode.AnimatedPageResource, item.Href, $"Item '{item.Id}' is animated; §8.1 requires pages to be static."));

        // §8.2: the producer applies the rotation and leaves no tag, or leaves
        // one that says no rotation. A reading system ignores the tag either
        // way, so this is about the producer's work, not about rendering.
        if (facts.ExifOrientation is int orientation && orientation != 1)
            violations.Add(new ContainerViolation(ContainerViolationCode.ExifOrientationResidue, item.Href, string.Create(CultureInfo.InvariantCulture, $"Item '{item.Id}' carries EXIF orientation {orientation}; §8.2 requires 1 or absent.")));
    }

    private static byte[]? Read(KomaPackage package, ManifestItem item, List<ContainerViolation> violations)
    {
        using Stream? resource = package.TryOpenResource(item.Href);

        if (resource is null)
        {
            violations.Add(new ContainerViolation(ContainerViolationCode.MissingPageResource, item.Href, $"Item '{item.Id}' declares a resource the package does not contain (§8.1)."));
            return null;
        }

        try
        {
            using var buffer = new MemoryStream();
            resource.CopyTo(buffer);

            return buffer.ToArray();
        }
        catch (DeclaredSizeExceededException e)
        {
            violations.Add(new ContainerViolation(ContainerViolationCode.DeclaredSizeMismatch, item.Href, e.Message));
            return null;
        }
        catch (InvalidDataException e)
        {
            violations.Add(new ContainerViolation(ContainerViolationCode.UnreadablePageResource, item.Href, e.Message));
            return null;
        }
    }
}
