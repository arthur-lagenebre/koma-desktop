namespace Koma.Core.Packaging;

/// <summary>
/// Codes for container-level defects.
/// </summary>
/// <remarks>
/// All of these are normative: the first group is defined by example in the
/// conformance corpus, the second by the table of §15.1. The distinction is
/// kept because it says which ones a corpus package currently exercises, and
/// therefore which ones an implementation can get wrong without any test
/// noticing.
/// </remarks>
public static class ContainerViolationCode
{
    // Exercised by a corpus package.
    public const string PathTraversal = "path-traversal";
    public const string AbsolutePath = "absolute-path";
    public const string DuplicateLogicalEntry = "duplicate-logical-entry";

    // Defined by §15.1; no corpus package yet.
    public const string PathEmpty = "path-empty";
    public const string PathBackslash = "path-backslash";
    public const string PathEmptySegment = "path-empty-segment";
    public const string PathNotNormalized = "path-not-normalized";
    public const string EntryCountLimit = "entry-count-limit";
    public const string UncompressedSizeLimit = "uncompressed-size-limit";
    public const string CompressionRatioLimit = "compression-ratio-limit";
    public const string DeclaredSizeMismatch = "declared-size-mismatch";

    // Used by the reference validator, defined nowhere.
    public const string NotAZip = "not-a-zip";
    public const string MultipartArchive = "multipart-archive";
    public const string MissingRequiredXml = "missing-required-xml";
    public const string XmlNotWellFormed = "xml-not-well-formed";
    public const string SchemaInvalidContainer = "schema-invalid:container";
    public const string MimetypeContent = "mimetype-content";
    public const string MimetypePosition = "mimetype-position";
    public const string MimetypeCompression = "mimetype-compression";

    // Named by no one.
    public const string XmlDocumentSizeLimit = "xml-document-size-limit";
    public const string XmlNestingLimit = "xml-nesting-limit";
}

/// <summary>
/// One container-level defect found by <see cref="ArchiveInspector"/>.
/// </summary>
/// <param name="Code">A <see cref="ContainerViolationCode"/> value.</param>
/// <param name="EntryName">The entry at fault, where one entry is at fault.</param>
/// <param name="Message">A description for a log or a diagnostic pane.</param>
public sealed record ContainerViolation(string Code, string? EntryName, string Message);
