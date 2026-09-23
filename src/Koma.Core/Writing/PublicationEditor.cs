using System.IO.Compression;
using System.Xml.Linq;
using Koma.Core.Model;
using Koma.Core.Packaging;
using Koma.Core.Schemas;
using Koma.Core.Versioning;

namespace Koma.Core.Writing;

/// <summary>
/// Edits a publication on disk: its metadata, and what its manifest says
/// about a page.
/// </summary>
/// <remarks>
/// The edited document is checked against its schema and read back by the
/// same reader and vocabulary checks the opener uses before anything is
/// written: an edit that would make the publication one this application
/// refuses to open is refused instead, and the file keeps what it had.
/// </remarks>
public static class PublicationEditor
{
    /// <summary>The metadata fields an edit can change, as the publication has them now.</summary>
    public static MetadataEdit Current(string path) => MetadataEditor.Read(Load(path, CorePaths.Metadata)!);

    /// <summary>What the manifest says about a page, as the values an editing form starts from.</summary>
    public static PageEdit CurrentPage(string path, string item) => PageEditor.Read(Load(path, CorePaths.Manifest)!, item);

    /// <summary>
    /// Changes what the manifest says about one page, and the landmarks that
    /// follow from it.
    /// </summary>
    /// <exception cref="ArgumentException">A value §8 does not allow.</exception>
    /// <exception cref="InvalidDataException">The edited documents would not be read back.</exception>
    public static void EditPage(string path, string item, PageEdit edit, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(edit);

        (XDocument manifest, XDocument? navigation) = PageEditor.Apply(Load(path, CorePaths.Manifest)!, Load(path, CorePaths.Navigation), item, edit);

        // §7.2.1: a core document changed is a new release, whatever changed
        // in it. An empty edit stamps the date and nothing else.
        XDocument metadata = MetadataEditor.Apply(Load(path, CorePaths.Metadata)!, new MetadataEdit(), now);

        var violations = new List<ContainerViolation>();

        Inspect(manifest, CorePaths.Manifest, violations);
        Inspect(metadata, CorePaths.Metadata, violations);

        if (navigation is not null)
            Inspect(navigation, CorePaths.Navigation, violations);

        Refuse(violations);

        var written = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [CorePaths.Manifest] = CanonicalXml.Write(manifest),
            [CorePaths.Metadata] = CanonicalXml.Write(metadata)
        };

        if (navigation is not null)
            written[CorePaths.Navigation] = CanonicalXml.Write(navigation);

        PackageRewriter.Rewrite(path, written);
    }

    /// <exception cref="ArgumentException">A value §4.3 does not allow.</exception>
    /// <exception cref="InvalidDataException">The edited metadata would not be read back.</exception>
    public static void EditMetadata(string path, MetadataEdit edit, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(edit);

        XDocument edited = MetadataEditor.Apply(Load(path, CorePaths.Metadata)!, edit, now);
        var violations = new List<ContainerViolation>();

        Inspect(edited, CorePaths.Metadata, violations);
        Refuse(violations);

        PackageRewriter.Rewrite(path, new Dictionary<string, byte[]>(StringComparer.Ordinal) { [CorePaths.Metadata] = CanonicalXml.Write(edited) });
    }

    /// <summary>
    /// One core document of the package, or <see langword="null"/> for a
    /// navigation document the package does not carry, which §1 allows.
    /// </summary>
    private static XDocument? Load(string path, string entry)
    {
        ArgumentNullException.ThrowIfNull(path);

        using ZipArchive archive = ZipFile.OpenRead(path);
        ZipArchiveEntry? found = archive.GetEntry(entry);

        if (found is null)
            return entry == CorePaths.Navigation ? null : throw new InvalidDataException($"The package has no {entry} (§1).");

        using Stream stream = found.Open();

        return XDocument.Load(stream);
    }

    /// <summary>
    /// Reads an edited document back the way the opener reads it: its schema,
    /// its reader, and the vocabularies of §4.5.
    /// </summary>
    private static void Inspect(XDocument document, string entry, List<ContainerViolation> violations)
    {
        if (KomaSchemas.Check(document, entry) is { } schema)
            violations.Add(schema);

        bool read = entry switch
        {
            CorePaths.Manifest => ManifestReader.Read(document, entry, KomaVersion.Supported, violations) is not null,
            CorePaths.Metadata => MetadataReader.Read(document, entry, KomaVersion.Supported, violations) is not null,
            _ => NavigationReader.Read(document, entry, KomaVersion.Supported, null, violations) is not null
        };

        if (read)
            OpenVocabularies.Check(document, entry, ProcessingMode.Strict, violations);
    }

    /// <summary>
    /// Refuses the edit when the publication would no longer open, an unknown
    /// token aside: §4.5.1 has a reader read past one, and so does this.
    /// </summary>
    private static void Refuse(List<ContainerViolation> violations)
    {
        ContainerViolation[] errors = [.. violations.Where(v => v.Severity == ViolationSeverity.Error && v.Code != ContainerViolationCode.UnknownToken)];

        if (errors.Length > 0)
            throw new InvalidDataException(string.Join(Environment.NewLine, errors.Select(e => $"{e.Code} — {e.Message}")));
    }
}
