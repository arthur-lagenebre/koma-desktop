using System.IO.Compression;
using System.Xml.Linq;
using Koma.Core.Model;
using Koma.Core.Packaging;
using Koma.Core.Schemas;
using Koma.Core.Versioning;

namespace Koma.Core.Writing;

/// <summary>
/// Edits the metadata of a publication on disk.
/// </summary>
/// <remarks>
/// The edited document is checked against its schema and read back by the
/// same reader and vocabulary checks the opener uses before anything is
/// written: an edit that would make the publication one this application
/// refuses to open is refused instead, and the file keeps what it had.
/// </remarks>
public static class PublicationEditor
{
    /// <summary>The fields an edit can change, as the publication has them now.</summary>
    public static MetadataEdit Current(string path) => MetadataEditor.Read(Load(path));

    /// <exception cref="ArgumentException">A value §4.3 does not allow.</exception>
    /// <exception cref="InvalidDataException">The edited metadata would not be read back.</exception>
    public static void EditMetadata(string path, MetadataEdit edit, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(edit);

        XDocument original = Load(path);
        XDocument edited = MetadataEditor.Apply(original, edit, now);
        var violations = new List<ContainerViolation>();

        if (KomaSchemas.Check(edited, CorePaths.Metadata) is { } schema)
            violations.Add(schema);

        if (MetadataReader.Read(edited, CorePaths.Metadata, KomaVersion.Supported, violations) is not null)
            OpenVocabularies.Check(edited, CorePaths.Metadata, ProcessingMode.Strict, violations);

        ContainerViolation[] errors = [.. violations.Where(v => v.Severity == ViolationSeverity.Error && v.Code != ContainerViolationCode.UnknownToken)];

        if (errors.Length > 0)
            throw new InvalidDataException(string.Join(Environment.NewLine, errors.Select(e => $"{e.Code} — {e.Message}")));

        PackageRewriter.Rewrite(path, new Dictionary<string, byte[]>(StringComparer.Ordinal) { [CorePaths.Metadata] = CanonicalXml.Write(edited) });
    }

    private static XDocument Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        using ZipArchive archive = ZipFile.OpenRead(path);
        ZipArchiveEntry entry = archive.GetEntry(CorePaths.Metadata) ?? throw new InvalidDataException("The package has no metadata (§1).");
        using Stream stream = entry.Open();

        return XDocument.Load(stream);
    }
}
