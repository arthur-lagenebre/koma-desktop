using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Koma.Core.Packaging;

/// <summary>
/// Loads a core XML document from a package, within the limits of §13.1.
/// </summary>
/// <remarks>
/// Every reader of a core document goes through here. The settings below are
/// not hardening applied on top of parsing, they are the parsing: a package is
/// an untrusted file, and an <see cref="XmlReader"/> left at its defaults will
/// resolve external entities and follow a DTD out of the archive.
/// </remarks>
public static class KomaXml
{
    /// <summary>
    /// Reads an entry as XML.
    /// </summary>
    /// <returns>
    /// The document, or <see langword="null"/> with <paramref name="violation"/>
    /// set. A missing entry is not an error here: the caller knows whether the
    /// document was required.
    /// </returns>
    public static XDocument? TryLoad(ZipArchive archive, string entryName, out ContainerViolation? violation, ResourceLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(entryName);

        ResourceLimits profile = limits ?? ResourceLimits.Default;
        violation = null;

        ZipArchiveEntry? entry = archive.GetEntry(entryName);

        if (entry is null)
            return null;

        if (entry.Length > profile.MaxCoreDocumentBytes)
        {
            violation = new ContainerViolation(ContainerViolationCode.XmlDocumentSizeLimit, entryName, string.Create(CultureInfo.InvariantCulture, $"Declares {entry.Length} bytes, above the {profile.MaxCoreDocumentBytes} of §13.1."));

            return null;
        }

        try
        {
            using Stream raw = entry.Open();

            // Bounded by what the entry declares, not by the profile ceiling:
            // §13.1 requires the declaration to be enforced during
            // decompression, and this is where a core document is decompressed.
            using var bounded = new BoundedReadStream(raw, entry.Length, entryName);
            using var reader = XmlReader.Create(bounded, SecureSettings());

            var document = XDocument.Load(reader, LoadOptions.None);

            int depth = MaxDepth(document.Root);

            if (depth > profile.MaxXmlDepth)
            {
                violation = new ContainerViolation(ContainerViolationCode.XmlNestingLimit, entryName, string.Create(CultureInfo.InvariantCulture, $"Nests {depth} elements deep, above the {profile.MaxXmlDepth} of §13.1."));

                return null;
            }

            return document;
        }
        catch (XmlException e)
        {
            violation = new ContainerViolation(ContainerViolationCode.XmlNotWellFormed, entryName, e.Message);

            return null;
        }
        catch (DeclaredSizeExceededException e)
        {
            violation = new ContainerViolation(ContainerViolationCode.DeclaredSizeMismatch, entryName, e.Message);

            return null;
        }
        catch (InvalidDataException e)
        {
            // A corrupt deflate stream or a failed CRC.
            violation = new ContainerViolation(ContainerViolationCode.XmlNotWellFormed, entryName, e.Message);

            return null;
        }
    }

    private static XmlReaderSettings SecureSettings() => new()
    {
        // The two that matter. A DTD is an amplification vector and an entity
        // is a file-read primitive; neither has any use in a core document.
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        CloseInput = false,
        IgnoreWhitespace = false,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };

    /// <summary>
    /// Depth of the deepest element, the root counting as one.
    /// </summary>
    /// <remarks>
    /// Measured on the loaded tree rather than during reading. A document deep
    /// enough to matter is already bounded by the size limit above, so the
    /// tree exists before this runs and walking it costs nothing that has not
    /// already been paid.
    /// </remarks>
    private static int MaxDepth(XElement? element)
    {
        if (element is null)
            return 0;

        int deepest = 0;

        foreach (XElement child in element.Elements())
            deepest = Math.Max(deepest, MaxDepth(child));

        return deepest + 1;
    }
}
