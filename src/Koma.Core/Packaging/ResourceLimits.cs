namespace Koma.Core.Packaging;

/// <summary>
/// The resource limits of §13.1.
/// </summary>
/// <remarks>
/// <para>
/// The default values are the <em>default profile</em>, which §13.1 makes
/// normative: a reader operates in it unless the user has explicitly raised a
/// limit, and §15 validates against it alone. A raised profile is a deliberate
/// act by the user for a particular publication, never something the reader
/// decides on its own, and §13.1 requires the reader to say so when one is in
/// effect — which is what <see cref="IsDefaultProfile"/> exists to answer.
/// </para>
/// <para>
/// The image and XML limits are declared here so that the profile is one
/// object rather than several, but nothing enforces them yet: they belong to
/// the image pipeline and the XML reader.
/// </para>
/// </remarks>
public sealed record ResourceLimits
{
    /// <summary>The default profile of §13.1.</summary>
    public static ResourceLimits Default { get; } = new();

    /// <summary>Total uncompressed size, all entries together.</summary>
    public long TotalUncompressedBytes { get; init; } = 4L * 1024 * 1024 * 1024;

    /// <summary>Total uncompressed size, as a multiple of the archive's own size.</summary>
    public int ArchiveSizeMultiple { get; init; } = 100;

    /// <summary>Per-entry compression ratio above which an entry is rejected.</summary>
    public int MaxCompressionRatio { get; init; } = 100;

    /// <summary>
    /// Uncompressed size at or below which the ratio rule does not apply. A
    /// small entry that expands sharply costs nothing; the rule exists for
    /// entries whose expansion is worth defending against.
    /// </summary>
    public long RatioFloorBytes { get; init; } = 1024 * 1024;

    /// <summary>Number of ZIP entries.</summary>
    public int MaxEntries { get; init; } = 10_000;

    /// <summary>Size of a single core XML document. Not enforced yet.</summary>
    public long MaxCoreDocumentBytes { get; init; } = 16L * 1024 * 1024;

    /// <summary>XML element nesting depth. Not enforced yet.</summary>
    public int MaxXmlDepth { get; init; } = 100;

    /// <summary>Pixels in one page resource. Not enforced yet.</summary>
    public long MaxPixelsPerPage { get; init; } = 100_000_000;

    /// <summary>Pixels along either side of a page resource. Not enforced yet.</summary>
    public int MaxPixelsPerSide { get; init; } = 65_535;

    /// <summary>
    /// Whether this is the default profile. §13.1 requires a reader running
    /// with a raised limit to report that the publication lies outside it.
    /// </summary>
    public bool IsDefaultProfile => Equals(Default);

    /// <summary>
    /// The largest total uncompressed size allowed for an archive of the given
    /// size: §13.1 imposes both a fixed ceiling and a multiple of the archive,
    /// so the lower of the two governs.
    /// </summary>
    public long TotalBudgetFor(long archiveSizeBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(archiveSizeBytes);

        // A non-positive multiple allows nothing through. It is a nonsensical
        // profile rather than a permissive one, and the division below has no
        // answer for it.
        if (ArchiveSizeMultiple <= 0)
            return 0;

        long byMultiple = archiveSizeBytes > TotalUncompressedBytes / ArchiveSizeMultiple ? TotalUncompressedBytes : archiveSizeBytes * ArchiveSizeMultiple;

        return Math.Min(TotalUncompressedBytes, byMultiple);
    }

    /// <summary>
    /// Whether one entry's declared sizes breach the ratio rule.
    /// </summary>
    public bool IsRatioExcessive(long compressedBytes, long uncompressedBytes)
    {
        if (uncompressedBytes <= RatioFloorBytes)
            return false;

        // A stored entry has a ratio of 1:1; a zero-length compressed entry
        // with content is malformed, and treating it as excessive is the safe
        // reading of a division that has no answer.
        if (compressedBytes <= 0)
            return true;

        return uncompressedBytes > compressedBytes * (long)MaxCompressionRatio;
    }
}
