using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Compression;

namespace Koma.Core.Packaging;

/// <summary>
/// What an inspection found.
/// </summary>
public sealed record InspectionResult
{
    public required ReadOnlyCollection<ContainerViolation> Violations { get; init; }

    /// <summary>Number of entries in the central directory.</summary>
    public required int EntryCount { get; init; }

    /// <summary>
    /// Total uncompressed size the central directory declares. A claim by the
    /// producer, not a measurement — see <see cref="BoundedReadStream"/>.
    /// </summary>
    public required long DeclaredUncompressedBytes { get; init; }

    /// <summary>Whether the package may be read.</summary>
    public bool IsAcceptable => Violations.Count == 0;
}

/// <summary>
/// Checks a package against the ZIP profile of §3 and the resource limits of
/// §13.1, before any entry is decompressed.
/// </summary>
/// <remarks>
/// <para>
/// This pass reads only the central directory, so everything it reports rests
/// on what the producer declared. It is necessary and never sufficient: §13.1
/// requires the same limits to hold during decompression, which is the job of
/// <see cref="BoundedReadStream"/>.
/// </para>
/// <para>
/// <strong>Known gap.</strong> <see cref="ZipArchive"/> materialises every
/// entry of the central directory when it is constructed, so by the time the
/// entry count can be read, the allocation §13.1 wants bounded has already
/// happened. Enforcing <see cref="ResourceLimits.MaxEntries"/> before that
/// means reading the end-of-central-directory record directly, including its
/// ZIP64 form and the trailing-comment scan. Until that exists, the count is
/// checked but the allocation is not prevented.
/// </para>
/// <para>
/// The mimetype rules of §2.1 are not checked here: they are byte offsets in
/// the file, not properties of an entry, and belong with the code that reads
/// those bytes.
/// </para>
/// </remarks>
public static class ArchiveInspector
{
    public static InspectionResult Inspect(ZipArchive archive, long archiveSizeBytes, ResourceLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentOutOfRangeException.ThrowIfNegative(archiveSizeBytes);

        ResourceLimits profile = limits ?? ResourceLimits.Default;
        var violations = new List<ContainerViolation>();

        // Every entry is examined even once a defect is found: a reader that
        // stops at the first one makes a malformed package take as many
        // attempts to diagnose as it has faults.
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        long declaredTotal = 0;
        long budget = profile.TotalBudgetFor(archiveSizeBytes);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string name = entry.FullName;

            if (!KomaEntryName.TryValidate(name, out EntryNameProblem problem))
                violations.Add(NameViolation(name, problem));
            else
                CheckUniqueness(seen, name, violations);

            declaredTotal = AddClamped(declaredTotal, entry.Length);

            if (profile.IsRatioExcessive(entry.CompressedLength, entry.Length))
                violations.Add(new ContainerViolation(ContainerViolationCode.CompressionRatioLimit, name, string.Create(CultureInfo.InvariantCulture, $"Declares {entry.Length} bytes from {entry.CompressedLength}, above the {profile.MaxCompressionRatio}:1 limit of §13.1.")));
        }

        int entryCount = archive.Entries.Count;

        if (entryCount > profile.MaxEntries)
            violations.Add(new ContainerViolation(ContainerViolationCode.EntryCountLimit, null, string.Create(CultureInfo.InvariantCulture, $"{entryCount} entries, above the {profile.MaxEntries} of §13.1.")));

        if (declaredTotal > budget)
            violations.Add(new ContainerViolation(ContainerViolationCode.UncompressedSizeLimit, null, string.Create(CultureInfo.InvariantCulture, $"Declares {declaredTotal} uncompressed bytes, above the {budget} allowed for an archive of {archiveSizeBytes} bytes (§13.1).")));

        return new InspectionResult
        {
            Violations = violations.AsReadOnly(),
            EntryCount = entryCount,
            DeclaredUncompressedBytes = declaredTotal,
        };
    }

    private static void CheckUniqueness(Dictionary<string, string> seen, string name, List<ContainerViolation> violations)
    {
        string folded = KomaEntryName.FoldForUniqueness(name);

        if (seen.TryGetValue(folded, out string? first))
        {
            violations.Add(new ContainerViolation(
                ContainerViolationCode.DuplicateLogicalEntry,
                name,
                $"Is the same logical name as '{first}' after normalization and case folding (§3)."));

            return;
        }

        seen[folded] = name;
    }

    private static ContainerViolation NameViolation(string name, EntryNameProblem problem)
    {
        (string code, string message) = problem switch
        {
            EntryNameProblem.Absolute =>
                (ContainerViolationCode.AbsolutePath, "Is an absolute path (§3)."),
            EntryNameProblem.ParentDirectorySegment =>
                (ContainerViolationCode.PathTraversal, "Contains a '..' segment (§3)."),
            EntryNameProblem.CurrentDirectorySegment =>
                (ContainerViolationCode.PathTraversal, "Contains a '.' segment (§3)."),
            EntryNameProblem.Backslash =>
                (ContainerViolationCode.PathBackslash, "Contains a backslash (§3)."),
            EntryNameProblem.EmptySegment =>
                (ContainerViolationCode.PathEmptySegment, "Contains an empty path segment (§3)."),
            EntryNameProblem.NotNormalized =>
                (ContainerViolationCode.PathNotNormalized, "Is not in Unicode NFC (§3)."),
            _ =>
                (ContainerViolationCode.PathEmpty, "Is empty (§3)."),
        };

        return new ContainerViolation(code, name, message);
    }

    /// <summary>
    /// Adds without wrapping. A central directory declaring sizes near
    /// <see cref="long.MaxValue"/> would otherwise overflow the running total
    /// into a negative number and slip under the budget.
    /// </summary>
    private static long AddClamped(long total, long addend)
    {
        if (addend <= 0)
            return total;

        return total > long.MaxValue - addend ? long.MaxValue : total + addend;
    }
}
