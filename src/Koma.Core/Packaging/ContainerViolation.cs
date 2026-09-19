namespace Koma.Core.Packaging;

/// <summary>
/// Whether a defect stops a publication from being read.
/// </summary>
/// <remarks>
/// §15 reports errors and warnings, and the corpus expects a package whose only
/// defect is a warning to be valid to read. Without the distinction a reader
/// refuses publications the specification says it must open — §8.8 allows a
/// resource outside the spine and only asks that it be noticed.
/// </remarks>
public enum ViolationSeverity
{
    Error,
    Warning
}

/// <summary>
/// Codes for defects a reader can report.
/// </summary>
/// <remarks>
/// All of these are normative: the first group is defined by example in the
/// conformance corpus, the second by the table of §15.1. The distinction is
/// kept because it says which ones a corpus package currently exercises, and
/// therefore which ones this implementation can get wrong without any test
/// noticing.
/// </remarks>
public static class ContainerViolationCode
{
    // Exercised by a corpus package.
    public const string MimetypeContent = "mimetype-content";
    public const string MimetypePosition = "mimetype-position";
    public const string MimetypeCompression = "mimetype-compression";
    public const string PathTraversal = "path-traversal";
    public const string AbsolutePath = "absolute-path";
    public const string DuplicateLogicalEntry = "duplicate-logical-entry";
    public const string CompressionRatioLimit = "compression-ratio-limit";
    public const string DeclaredSizeMismatch = "declared-size-mismatch";
    public const string UnnamespacedElementInExtensions = "unnamespaced-element-in-extensions";
    public const string TokenListDuplicate = "tokenlist-duplicate";
    public const string AccessibilityHazardConflict = "accessibility-hazard-conflict";
    public const string FrontCoverMissing = "front-cover-missing";
    public const string FrontCoverDuplicate = "front-cover-duplicate";
    public const string FrontCoverNotInSpine = "front-cover-not-in-spine";
    public const string FrontCoverNotFirst = "front-cover-not-first";
    public const string SpineTargetMissing = "spine-target-missing";
    public const string SpineDuplicateItem = "spine-duplicate-item";
    public const string Span2SpreadPosition = "span2-spread-position";
    public const string DecorativeWithAlternativeText = "decorative-with-alternative-text";
    public const string ResourceOutsideSpine = "resource-outside-spine";
    public const string NavigationTargetOutsideSpine = "navigation-target-outside-spine";
    public const string MediaTypeMismatch = "media-type-mismatch";
    public const string DimensionsMismatch = "dimensions-mismatch";
    public const string AnimatedPageResource = "animated-page-resource";
    public const string ChecksumMismatch = "checksum-mismatch";
    public const string ExifOrientationResidue = "exif-orientation-residue";
    public const string NoNavigationDocument = "no-navigation-document";
    public const string PrivateUseToken = "private-use-token";

    // Defined by §15.1; no corpus package yet.
    public const string NotAZip = "not-a-zip";
    public const string MultipartArchive = "multipart-archive";
    public const string PathEmpty = "path-empty";
    public const string PathBackslash = "path-backslash";
    public const string PathEmptySegment = "path-empty-segment";
    public const string PathNotNormalized = "path-not-normalized";
    public const string EntryCountLimit = "entry-count-limit";
    public const string UncompressedSizeLimit = "uncompressed-size-limit";
    public const string MissingRequiredXml = "missing-required-xml";
    public const string XmlNotWellFormed = "xml-not-well-formed";
    public const string XmlDocumentSizeLimit = "xml-document-size-limit";
    public const string XmlNestingLimit = "xml-nesting-limit";
    public const string SchemaInvalidContainer = "schema-invalid:container";
    public const string SchemaInvalidMetadata = "schema-invalid:metadata";
    public const string SchemaInvalidManifest = "schema-invalid:manifest";
    public const string MissingPageResource = "missing-page-resource";
    public const string UnreadablePageResource = "unreadable-page-resource";
}

/// <summary>
/// One defect found while reading a package.
/// </summary>
/// <param name="Code">A <see cref="ContainerViolationCode"/> value.</param>
/// <param name="EntryName">The entry at fault, where one entry is at fault.</param>
/// <param name="Message">A description for a log or a diagnostic pane.</param>
public sealed record ContainerViolation(string Code, string? EntryName, string Message)
{
    /// <summary>
    /// Defaults to <see cref="ViolationSeverity.Error"/>, so a defect has to be
    /// declared harmless on purpose rather than by omission.
    /// </summary>
    public ViolationSeverity Severity { get; init; } = ViolationSeverity.Error;
}
