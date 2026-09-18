namespace Koma.Core.Packaging;

/// <summary>
/// Codes for container-level defects.
/// </summary>
/// <remarks>
/// <para>
/// The first group is taken from the conformance corpus, which is normative by
/// example. The second group has no counterpart there: §3 forbids backslashes,
/// empty segments and non-NFC names, but neither §15 nor the corpus names a
/// code for them, so these spellings are this implementation's proposal and are
/// liable to change when the specification catches up.
/// </para>
/// <para>
/// Keeping the distinction visible matters more than picking good names. Folding
/// an unnamed defect into <see cref="PathTraversal"/> would report
/// <c>images\page.webp</c> — which escapes nothing — as an escape attempt, and
/// would hide from the corpus that a case is missing.
/// </para>
/// </remarks>
public static class ContainerViolationCode
{
    // Defined by the corpus.
    public const string PathTraversal = "path-traversal";
    public const string AbsolutePath = "absolute-path";
    public const string DuplicateLogicalEntry = "duplicate-logical-entry";

    // Proposed; no corpus case yet.
    public const string PathBackslash = "path-backslash";
    public const string PathEmptySegment = "path-empty-segment";
    public const string PathNotNormalized = "path-not-normalized";
    public const string PathEmpty = "path-empty";
    public const string EntryCountLimit = "entry-count-limit";
    public const string UncompressedSizeLimit = "uncompressed-size-limit";
    public const string CompressionRatioLimit = "compression-ratio-limit";
}

/// <summary>
/// One container-level defect found by <see cref="ArchiveInspector"/>.
/// </summary>
/// <param name="Code">A <see cref="ContainerViolationCode"/> value.</param>
/// <param name="EntryName">The entry at fault, where one entry is at fault.</param>
/// <param name="Message">A description for a log or a diagnostic pane.</param>
public sealed record ContainerViolation(string Code, string? EntryName, string Message);
