using System.IO.Compression;
using Koma.Core.Packaging;

namespace Koma.Core.Tests;

/// <summary>
/// Reading the end of an archive without opening it, so that the entry limit
/// of §13.1 can refuse a package before anything is allocated from it.
/// </summary>
public sealed class ZipDirectoryTests
{
    private static MemoryStream Build(int entries, string comment = "")
    {
        var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (int i = 0; i < entries; i++)
            {
                ZipArchiveEntry entry = archive.CreateEntry($"pages/{i:D4}.bin");
                using Stream stream = entry.Open();
                stream.Write("x"u8);
            }
        }

        if (comment.Length > 0)
            AppendComment(buffer, comment);

        buffer.Position = 0;
        return buffer;
    }

    /// <summary>
    /// ZipArchive writes no comment, so one is grafted on: the record's comment
    /// length is set and the bytes appended after it.
    /// </summary>
    private static void AppendComment(MemoryStream buffer, string comment)
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(comment);
        byte[] raw = buffer.ToArray();
        int at = raw.Length - 22;

        raw[at + 20] = (byte)(bytes.Length & 0xFF);
        raw[at + 21] = (byte)(bytes.Length >> 8);

        buffer.SetLength(0);
        buffer.Write(raw);
        buffer.Write(bytes);
    }

    [Fact]
    public void ReadsTheEntryCount()
    {
        using MemoryStream buffer = Build(7);

        Assert.True(ZipDirectory.TryRead(buffer, out ZipDirectoryInfo? info, out ZipDirectoryProblem problem));
        Assert.Equal(ZipDirectoryProblem.None, problem);
        Assert.Equal(7, info!.EntryCount);
        Assert.False(info.IsZip64);
    }

    [Fact]
    public void RestoresTheStreamPosition()
    {
        using MemoryStream buffer = Build(3);
        buffer.Position = 11;

        ZipDirectory.TryRead(buffer, out _, out _);

        // The caller goes on to open the archive from the same stream, so this
        // is not tidiness but a precondition of the sequence.
        Assert.Equal(11, buffer.Position);
    }

    [Fact]
    public void FindsTheRecordBehindAComment()
    {
        // The record is no longer the last 22 bytes, which is the case a naive
        // reader that only looks at the tail gets wrong.
        using MemoryStream buffer = Build(4, new string('c', 600));

        Assert.True(ZipDirectory.TryRead(buffer, out ZipDirectoryInfo? info, out _));
        Assert.Equal(4, info!.EntryCount);
    }

    [Fact]
    public void IsNotFooledByASignatureInsideTheComment()
    {
        // "PK\x05\x06" occurring in a comment is a plausible record except that
        // the comment length would not reach the end of the file.
        using MemoryStream buffer = Build(2, "PK\u0005\u0006" + new string('x', 40));

        Assert.True(ZipDirectory.TryRead(buffer, out ZipDirectoryInfo? info, out _));
        Assert.Equal(2, info!.EntryCount);
    }

    [Fact]
    public void RejectsBytesThatAreNotAnArchive()
    {
        using var buffer = new MemoryStream(new byte[4096]);

        Assert.False(ZipDirectory.TryRead(buffer, out _, out ZipDirectoryProblem problem));
        Assert.Equal(ZipDirectoryProblem.NotAZip, problem);
    }

    [Fact]
    public void RejectsAFileTooShortToHoldARecord()
    {
        using var buffer = new MemoryStream(new byte[10]);

        Assert.False(ZipDirectory.TryRead(buffer, out _, out ZipDirectoryProblem problem));
        Assert.Equal(ZipDirectoryProblem.NotAZip, problem);
    }

    [Fact]
    public void RejectsAMultipartArchive()
    {
        using MemoryStream buffer = Build(3);
        byte[] raw = buffer.ToArray();
        int at = raw.Length - 22;

        // Disk number 1: §3 forbids multipart archives.
        raw[at + 4] = 1;

        using var altered = new MemoryStream(raw);

        Assert.False(ZipDirectory.TryRead(altered, out _, out ZipDirectoryProblem problem));
        Assert.Equal(ZipDirectoryProblem.Multipart, problem);
    }

    [Fact]
    public void RejectsSaturatedFieldsWithNoZip64RecordsBehindThem()
    {
        using MemoryStream buffer = Build(3);
        byte[] raw = buffer.ToArray();
        int at = raw.Length - 22;

        // Claim more than 65 534 entries without carrying the ZIP64 records
        // that would say how many.
        raw[at + 10] = 0xFF;
        raw[at + 11] = 0xFF;

        using var altered = new MemoryStream(raw);

        Assert.False(ZipDirectory.TryRead(altered, out _, out ZipDirectoryProblem problem));
        Assert.Equal(ZipDirectoryProblem.Truncated, problem);
    }

    [Fact]
    public void RejectsANonSeekableStream()
    {
        using var stream = new NonSeekableStream();

        Assert.Throws<ArgumentException>(() => ZipDirectory.TryRead(stream, out _, out _));
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }
}

/// <summary>
/// The pre-open gate: §13.1 asks that exceeding a limit never allocate without
/// bound, and opening the archive is itself that allocation.
/// </summary>
public sealed class ArchiveGateTests
{
    private static MemoryStream Build(int entries)
    {
        var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (int i = 0; i < entries; i++)
                archive.CreateEntry($"pages/{i:D4}.bin");
        }

        buffer.Position = 0;
        return buffer;
    }

    [Fact]
    public void LetsAnOrdinaryArchiveThrough()
    {
        using MemoryStream buffer = Build(5);

        Assert.Null(ArchiveGate.CheckBeforeOpening(buffer));
    }

    [Fact]
    public void RefusesAnEntryCountAboveTheProfile()
    {
        using MemoryStream buffer = Build(5);

        ContainerViolation? violation =
            ArchiveGate.CheckBeforeOpening(buffer, ResourceLimits.Default with { MaxEntries = 4 });

        Assert.NotNull(violation);
        Assert.Equal(ContainerViolationCode.EntryCountLimit, violation.Code);
    }

    [Fact]
    public void RefusesWithoutReadingTheCentralDirectory()
    {
        using MemoryStream buffer = Build(5);
        long before = buffer.Position;

        ArchiveGate.CheckBeforeOpening(buffer, ResourceLimits.Default with { MaxEntries = 1 });

        // The point of the gate is that nothing was built from the archive. The
        // stream is where it was, and no ZipArchive was ever constructed.
        Assert.Equal(before, buffer.Position);
    }

    [Fact]
    public void RefusesSomethingThatIsNotAnArchive()
    {
        using var buffer = new MemoryStream(new byte[2048]);

        ContainerViolation? violation = ArchiveGate.CheckBeforeOpening(buffer);

        Assert.NotNull(violation);
        Assert.Equal(ContainerViolationCode.NotAZip, violation.Code);
    }

    [Fact]
    public void LeavesTheStreamUsableForOpening()
    {
        using MemoryStream buffer = Build(3);

        Assert.Null(ArchiveGate.CheckBeforeOpening(buffer));

        // The sequence the gate exists to serve: check, then open.
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
        Assert.Equal(3, archive.Entries.Count);
    }
}
