using System.Buffers.Binary;

namespace Koma.Core.Packaging;

/// <summary>
/// Why the end of the central directory could not be read.
/// </summary>
public enum ZipDirectoryProblem
{
    None,
    /// <summary>No end-of-central-directory record was found.</summary>
    NotAZip,
    /// <summary>A record was found but the bytes it points at are not there.</summary>
    Truncated,
    /// <summary>The archive spans more than one disk, which §3 forbids.</summary>
    Multipart
}

/// <summary>
/// What the end of the central directory says about an archive.
/// </summary>
public sealed record ZipDirectoryInfo
{
    /// <summary>Entries the archive declares.</summary>
    public required long EntryCount { get; init; }
    /// <summary>Size of the central directory in bytes.</summary>
    public required long CentralDirectorySize { get; init; }
    /// <summary>Whether the ZIP64 records were used.</summary>
    public required bool IsZip64 { get; init; }
}

/// <summary>
/// Reads the end of a ZIP archive without opening it.
/// </summary>
/// <remarks>
/// <para>
/// §13.1 bounds the number of entries and asks that exceeding a limit fail in a
/// controlled way rather than allocate without bound.
/// <see cref="System.IO.Compression.ZipArchive"/> cannot serve that: it walks
/// the whole central directory when it is constructed, so by the time its
/// entry count can be read, the allocation the limit was meant to prevent has
/// already happened. Asking the archive how big it is, is too late.
/// </para>
/// <para>
/// The end-of-central-directory record carries the count, sits in the last
/// kilobytes of the file, and can be read on its own. That is what this type
/// does, so that the count can be refused before anything is built from it.
/// </para>
/// </remarks>
public static class ZipDirectory
{
    private const uint EndOfCentralDirectory = 0x06054B50;
    private const uint Zip64Locator = 0x07064B50;
    private const uint Zip64EndOfCentralDirectory = 0x06064B50;

    // 22 bytes of record, plus a trailing comment of up to 65 535 bytes that
    // the record itself sits before. Nothing else may follow it.
    private const int RecordLength = 22;
    private const int MaxComment = 0xFFFF;
    private const int LocatorLength = 20;

    /// <summary>
    /// Reads the entry count and central directory size.
    /// </summary>
    /// <remarks>
    /// The stream position is restored before returning, so the caller can go
    /// on to open the archive from the same stream.
    /// </remarks>
    public static bool TryRead(Stream stream, out ZipDirectoryInfo? info, out ZipDirectoryProblem problem)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
            throw new ArgumentException("The stream must be seekable.", nameof(stream));

        info = null;
        long position = stream.Position;

        try
        {
            return TryReadCore(stream, out info, out problem);
        }
        finally
        {
            stream.Position = position;
        }
    }

    private static bool TryReadCore(Stream stream, out ZipDirectoryInfo? info, out ZipDirectoryProblem problem)
    {
        info = null;

        long length = stream.Length;

        if (length < RecordLength)
        {
            problem = ZipDirectoryProblem.NotAZip;
            return false;
        }

        int window = (int)Math.Min(length, RecordLength + MaxComment);
        byte[] tail = new byte[window];
        stream.Position = length - window;
        stream.ReadExactly(tail);

        int at = FindRecord(tail);

        if (at < 0)
        {
            problem = ZipDirectoryProblem.NotAZip;
            return false;
        }

        ReadOnlySpan<byte> record = tail.AsSpan(at);

        // §3 forbids multipart archives. Both disk fields must be zero, and a
        // value of 0xFFFF means the real one lives in the ZIP64 record.
        ushort disk = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
        ushort directoryDisk = BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);

        if ((disk != 0 && disk != 0xFFFF) || (directoryDisk != 0 && directoryDisk != 0xFFFF))
        {
            problem = ZipDirectoryProblem.Multipart;
            return false;
        }

        long entries = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
        long size = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
        long offset = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
        bool zip64 = false;

        // A saturated field does not prove ZIP64 on its own, but it is the only
        // signal the classic record gives, and a genuine archive of exactly
        // 65 535 entries also carries the ZIP64 records.
        if (entries == 0xFFFF || size == 0xFFFFFFFFL || offset == 0xFFFFFFFFL)
        {
            long recordStart = length - window + at;

            if (!TryReadZip64(stream, recordStart, ref entries, ref size, out problem))
                return false;

            zip64 = true;
        }

        if (entries < 0 || size < 0)
        {
            problem = ZipDirectoryProblem.Truncated;
            return false;
        }

        info = new ZipDirectoryInfo
        {
            EntryCount = entries,
            CentralDirectorySize = size,
            IsZip64 = zip64
        };

        problem = ZipDirectoryProblem.None;
        return true;
    }

    private static bool TryReadZip64(Stream stream, long recordStart, ref long entries, ref long size, out ZipDirectoryProblem problem)
    {
        problem = ZipDirectoryProblem.None;

        // The locator sits immediately before the classic record.
        long locatorStart = recordStart - LocatorLength;

        if (locatorStart < 0)
        {
            problem = ZipDirectoryProblem.Truncated;
            return false;
        }

        byte[] locator = new byte[LocatorLength];
        stream.Position = locatorStart;
        stream.ReadExactly(locator);

        if (BinaryPrimitives.ReadUInt32LittleEndian(locator) != Zip64Locator)
        {
            // Saturated fields with no locator behind them. The archive claims
            // ZIP64 sizes and does not carry the records that would define them.
            problem = ZipDirectoryProblem.Truncated;
            return false;
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(locator.AsSpan(16)) > 1)
        {
            problem = ZipDirectoryProblem.Multipart;
            return false;
        }

        long at = BinaryPrimitives.ReadInt64LittleEndian(locator.AsSpan(8));

        if (at < 0 || at + 56 > stream.Length)
        {
            problem = ZipDirectoryProblem.Truncated;
            return false;
        }

        byte[] record = new byte[56];
        stream.Position = at;
        stream.ReadExactly(record);

        if (BinaryPrimitives.ReadUInt32LittleEndian(record) != Zip64EndOfCentralDirectory)
        {
            problem = ZipDirectoryProblem.Truncated;
            return false;
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(16)) != 0
            || BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(20)) != 0)
        {
            problem = ZipDirectoryProblem.Multipart;
            return false;
        }

        entries = BinaryPrimitives.ReadInt64LittleEndian(record.AsSpan(32));
        size = BinaryPrimitives.ReadInt64LittleEndian(record.AsSpan(40));

        return true;
    }

    /// <summary>
    /// Finds the record in the tail of the file.
    /// </summary>
    /// <remarks>
    /// Searched backwards, and the comment length is checked against what
    /// actually follows: the four signature bytes can occur inside a comment,
    /// inside compressed data, or inside a file that embeds another archive,
    /// and the last plausible one is the real record.
    /// </remarks>
    private static int FindRecord(ReadOnlySpan<byte> tail)
    {
        for (int at = tail.Length - RecordLength; at >= 0; at--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail[at..]) != EndOfCentralDirectory)
                continue;

            int comment = BinaryPrimitives.ReadUInt16LittleEndian(tail[(at + 20)..]);

            if (at + RecordLength + comment == tail.Length)
                return at;
        }

        return -1;
    }
}
