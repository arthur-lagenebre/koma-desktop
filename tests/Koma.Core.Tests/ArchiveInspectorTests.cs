using System.IO.Compression;
using Koma.Core.Packaging;

namespace Koma.Core.Tests;

/// <summary>
/// The container pass: §3 paths and uniqueness, §13.1 declared sizes.
/// </summary>
public sealed class ArchiveInspectorTests
{
    /// <summary>
    /// Builds an archive whose entries are named as given. Content is
    /// incompressible random bytes unless a size is asked for, so that ratios
    /// stay ordinary and only the case under test is unusual.
    /// </summary>
    private static MemoryStream BuildArchive(params string[] names)
    {
        var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string name in names)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
                using Stream stream = entry.Open();
                stream.Write("payload"u8);
            }
        }

        buffer.Position = 0;
        return buffer;
    }

    private static InspectionResult Inspect(MemoryStream buffer, ResourceLimits? limits = null)
    {
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);

        return ArchiveInspector.Inspect(archive, buffer.Length, limits);
    }

    [Fact]
    public void AcceptsAnOrdinaryPackage()
    {
        using MemoryStream buffer = BuildArchive("META-INF/container.xml", "koma/manifest.xml", "images/page-001.webp");

        InspectionResult result = Inspect(buffer);

        Assert.True(result.IsAcceptable);
        Assert.Equal(3, result.EntryCount);
        Assert.Equal(21, result.DeclaredUncompressedBytes);
    }

    [Fact]
    public void ReportsTraversalWithTheCorpusCode()
    {
        using MemoryStream buffer = BuildArchive("koma/manifest.xml", "images/../../etc/passwd");

        InspectionResult result = Inspect(buffer);

        ContainerViolation violation = Assert.Single(result.Violations);
        Assert.Equal(ContainerViolationCode.PathTraversal, violation.Code);
        Assert.Equal("images/../../etc/passwd", violation.EntryName);
    }

    [Fact]
    public void ReportsCaseFoldedDuplicates()
    {
        // The corpus case L1-case-fold-duplicate.koma in shape: two entries
        // that differ only in case are one logical name under §3.
        using MemoryStream buffer = BuildArchive("koma/manifest.xml", "koma/Manifest.xml");

        InspectionResult result = Inspect(buffer);

        ContainerViolation violation = Assert.Single(result.Violations);
        Assert.Equal(ContainerViolationCode.DuplicateLogicalEntry, violation.Code);
        Assert.Equal("koma/Manifest.xml", violation.EntryName);
    }

    [Fact]
    public void ReportsEveryDefectRatherThanTheFirst()
    {
        // A reader that stops at the first fault makes a malformed package take
        // as many attempts to diagnose as it has faults.
        using MemoryStream buffer = BuildArchive("/absolute.xml", "../escape.xml", "back\\slash.xml");

        InspectionResult result = Inspect(buffer);

        Assert.Equal(3, result.Violations.Count);
        Assert.Contains(result.Violations, v => v.Code == ContainerViolationCode.AbsolutePath);
        Assert.Contains(result.Violations, v => v.Code == ContainerViolationCode.PathTraversal);
        Assert.Contains(result.Violations, v => v.Code == ContainerViolationCode.PathBackslash);
    }

    [Fact]
    public void DoesNotReportADuplicateForANameItAlreadyRejected()
    {
        // An invalid name is not entered into the uniqueness map, so a package
        // with two identical bad names reports two name faults and no spurious
        // duplicate on top.
        using MemoryStream buffer = BuildArchive("../a.xml", "../b.xml");

        InspectionResult result = Inspect(buffer);

        Assert.Equal(2, result.Violations.Count);
        Assert.DoesNotContain(result.Violations, v => v.Code == ContainerViolationCode.DuplicateLogicalEntry);
    }

    [Fact]
    public void ReportsAnEntryCountAboveTheProfile()
    {
        using MemoryStream buffer = BuildArchive("a.xml", "b.xml", "c.xml");

        InspectionResult result = Inspect(buffer, ResourceLimits.Default with { MaxEntries = 2 });

        ContainerViolation violation = Assert.Single(result.Violations);
        Assert.Equal(ContainerViolationCode.EntryCountLimit, violation.Code);
        Assert.Null(violation.EntryName);
    }

    [Fact]
    public void ReportsATotalAboveTheBudget()
    {
        using MemoryStream buffer = BuildArchive("a.xml", "b.xml");

        // 21 bytes declared across two entries; a 10-byte ceiling is under it.
        InspectionResult result = Inspect(buffer, ResourceLimits.Default with { TotalUncompressedBytes = 10 });

        Assert.Contains(result.Violations, v => v.Code == ContainerViolationCode.UncompressedSizeLimit);
    }

    [Fact]
    public void ReportsAnExcessiveCompressionRatio()
    {
        var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Ballast: incompressible, so the archive is large enough that the
            // 100× total rule cannot be what fires. Without it the two rules
            // trigger together and the ratio rule looks redundant — which is the
            // point of having it. A bomb hidden inside an otherwise legitimate
            // package breaches the ratio and nothing else.
            byte[] noise = new byte[256 * 1024];
            Random.Shared.NextBytes(noise);

            ZipArchiveEntry ballast = archive.CreateEntry("images/page-001.webp", CompressionLevel.Optimal);
            using (Stream stream = ballast.Open())
                stream.Write(noise);

            // 2 MiB of zeros deflates to about 2 KB: past the 1 MiB floor and far
            // past 100:1. Not a real bomb, just the shape of one.
            ZipArchiveEntry bomb = archive.CreateEntry("images/page-002.webp", CompressionLevel.Optimal);
            using (Stream stream = bomb.Open())
                stream.Write(new byte[2 * 1024 * 1024]);
        }

        buffer.Position = 0;
        InspectionResult result = Inspect(buffer);

        ContainerViolation violation = Assert.Single(result.Violations);
        Assert.Equal(ContainerViolationCode.CompressionRatioLimit, violation.Code);
        Assert.Equal("images/page-002.webp", violation.EntryName);

        buffer.Dispose();
    }

    [Fact]
    public void AcceptsOrdinaryCompressionOfTheSameSize()
    {
        var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Incompressible data of the same uncompressed size. Above the
            // floor, but the ratio is about 1:1.
            byte[] noise = new byte[2 * 1024 * 1024];
            Random.Shared.NextBytes(noise);

            ZipArchiveEntry entry = archive.CreateEntry("images/page.webp", CompressionLevel.Optimal);
            using Stream stream = entry.Open();
            stream.Write(noise);
        }

        buffer.Position = 0;
        InspectionResult result = Inspect(buffer);

        Assert.True(result.IsAcceptable);

        buffer.Dispose();
    }

    [Fact]
    public void ArchiveMultipleGovernsASmallArchive()
    {
        ResourceLimits limits = ResourceLimits.Default with { ArchiveSizeMultiple = 1 };

        // Below the 4 GiB ceiling, the multiple is what binds.
        Assert.Equal(2179, limits.TotalBudgetFor(2179));
    }

    [Fact]
    public void ANonPositiveMultipleYieldsAZeroBudget()
    {
        // Nonsense profile, but it must not divide by zero.
        Assert.Equal(0, (ResourceLimits.Default with { ArchiveSizeMultiple = 0 }).TotalBudgetFor(1024));
    }
}
