using Koma.Core.Packaging;

namespace Koma.Core.Tests;

/// <summary>
/// The default profile of §13.1 and its three arithmetic rules.
/// </summary>
public sealed class ResourceLimitsTests
{
    [Fact]
    public void Default_MatchesTheProfileWrittenInTheSpecification()
    {
        ResourceLimits limits = ResourceLimits.Default;

        Assert.Equal(4L * 1024 * 1024 * 1024, limits.TotalUncompressedBytes);
        Assert.Equal(100, limits.ArchiveSizeMultiple);
        Assert.Equal(100, limits.MaxCompressionRatio);
        Assert.Equal(1024 * 1024, limits.RatioFloorBytes);
        Assert.Equal(10_000, limits.MaxEntries);
        Assert.Equal(16L * 1024 * 1024, limits.MaxCoreDocumentBytes);
        Assert.Equal(100, limits.MaxXmlDepth);
        Assert.Equal(100_000_000, limits.MaxPixelsPerPage);
        Assert.Equal(65_535, limits.MaxPixelsPerSide);
    }

    [Fact]
    public void IsDefaultProfile_DistinguishesARaisedLimit()
    {
        Assert.True(ResourceLimits.Default.IsDefaultProfile);

        // §13.1 requires the reader to report that a publication read under a
        // raised limit lies outside the default profile, so this has to be
        // answerable from the profile alone.
        ResourceLimits raised = ResourceLimits.Default with { MaxEntries = 20_000 };

        Assert.False(raised.IsDefaultProfile);
    }

    [Fact]
    public void TotalBudget_TakesTheLowerOfTheTwoRules()
    {
        ResourceLimits limits = ResourceLimits.Default;

        // A small archive is governed by the multiple: 1 MiB × 100.
        Assert.Equal(100L * 1024 * 1024, limits.TotalBudgetFor(1024 * 1024));

        // A large one is governed by the fixed ceiling: 1 GiB × 100 would be
        // 100 GiB, well past 4 GiB.
        Assert.Equal(limits.TotalUncompressedBytes, limits.TotalBudgetFor(1024L * 1024 * 1024));
    }

    [Fact]
    public void TotalBudget_DoesNotOverflowOnAHugeArchive()
    {
        // long.MaxValue × 100 would wrap and yield a negative budget, which
        // would let everything through.
        long budget = ResourceLimits.Default.TotalBudgetFor(long.MaxValue);

        Assert.Equal(ResourceLimits.Default.TotalUncompressedBytes, budget);
    }

    [Fact]
    public void RatioRule_IgnoresEntriesAtOrBelowTheFloor()
    {
        ResourceLimits limits = ResourceLimits.Default;

        // 1 MiB from 10 bytes is a ratio far past 100:1, but §13.1 exempts it:
        // the expansion is not worth defending against.
        Assert.False(limits.IsRatioExcessive(10, limits.RatioFloorBytes));
    }

    [Fact]
    public void RatioRule_RejectsAboveTheFloor()
    {
        ResourceLimits limits = ResourceLimits.Default;

        // Just past the floor, at a ratio of about 1000:1.
        Assert.True(limits.IsRatioExcessive(2048, (limits.RatioFloorBytes + 1) * 2));
    }

    [Fact]
    public void RatioRule_AcceptsOrdinaryCompression()
    {
        ResourceLimits limits = ResourceLimits.Default;

        // 4 MiB of artwork deflating to 2 MiB. Nothing suspicious about it.
        Assert.False(limits.IsRatioExcessive(2 * 1024 * 1024, 4 * 1024 * 1024));
    }

    [Fact]
    public void RatioRule_RejectsAnImpossibleZeroLengthEntry()
    {
        // Content out of nothing. The division has no answer, and the safe
        // reading is the one that refuses.
        Assert.True(ResourceLimits.Default.IsRatioExcessive(0, 8 * 1024 * 1024));
    }

    [Theory]
    [InlineData(65_535, 1, false)]
    [InlineData(65_536, 1, true)]
    [InlineData(1, 65_536, true)]
    [InlineData(10_000, 10_000, false)]
    [InlineData(10_001, 10_000, true)]
    public void PixelRule_TreatsBothLimitsAsInclusive(int width, int height, bool exceeds)
    {
        Assert.Equal(exceeds, ResourceLimits.Default.ExceedsPixelLimits(width, height));
    }

    [Fact]
    public void PixelRule_DoesNotOverflowAtTheSideLimit()
    {
        // Within the side limit both ways, and 65 535 squared overflows an int
        // into a negative area, which would pass any per-page limit.
        Assert.True(ResourceLimits.Default.ExceedsPixelLimits(65_535, 65_535));
    }
}

/// <summary>
/// Enforcement during decompression, per §13.1: declared sizes are chosen by
/// the producer and are not to be trusted.
/// </summary>
public sealed class BoundedReadStreamTests
{
    private static byte[] Payload(int length) => new byte[length];

    [Fact]
    public void ReadsThroughWhenTheEntryHonoursItsDeclaration()
    {
        using var source = new MemoryStream(Payload(1000));
        using var bounded = new BoundedReadStream(source, 1000, "images/page.webp");
        using var sink = new MemoryStream();

        bounded.CopyTo(sink);

        Assert.Equal(1000, sink.Length);
        Assert.Equal(1000, bounded.Consumed);
    }

    [Fact]
    public void ThrowsWhenTheEntryProducesMoreThanDeclared()
    {
        // The archive claims a kilobyte and delivers a megabyte. Every check
        // made before the first byte was read passed.
        using var source = new MemoryStream(Payload(1024 * 1024));
        using var bounded = new BoundedReadStream(source, 1024, "images/page.webp");
        using var sink = new MemoryStream();

        DeclaredSizeExceededException thrown = Assert.Throws<DeclaredSizeExceededException>(() => bounded.CopyTo(sink));

        Assert.Equal("images/page.webp", thrown.EntryName);
        Assert.Equal(1024, thrown.DeclaredBytes);
    }

    [Fact]
    public void StopsWithinOneByteOfTheBudget()
    {
        // The failure must arrive before an unbounded amount has been buffered,
        // which is what "never in unbounded allocation" asks for.
        using var source = new MemoryStream(Payload(64 * 1024 * 1024));
        using var bounded = new BoundedReadStream(source, 4096, "images/page.webp");
        using var sink = new MemoryStream();

        Assert.Throws<DeclaredSizeExceededException>(() => bounded.CopyTo(sink));
        Assert.True(bounded.Consumed <= 4097, $"Read {bounded.Consumed} bytes against a 4096-byte budget.");
    }

    [Fact]
    public void AcceptsAnEntryShorterThanDeclared()
    {
        // Under-delivering is a different defect, caught by the CRC rather than
        // by a resource limit. This stream is not the place to fail it.
        using var source = new MemoryStream(Payload(100));
        using var bounded = new BoundedReadStream(source, 1000, "images/page.webp");
        using var sink = new MemoryStream();

        bounded.CopyTo(sink);

        Assert.Equal(100, bounded.Consumed);
    }

    [Fact]
    public void HandlesAZeroLengthBudget()
    {
        using var source = new MemoryStream(Payload(1));
        using var bounded = new BoundedReadStream(source, 0, "koma/empty");
        using var sink = new MemoryStream();

        Assert.Throws<DeclaredSizeExceededException>(() => bounded.CopyTo(sink));
    }
}
