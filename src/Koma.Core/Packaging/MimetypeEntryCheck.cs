using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Koma.Core.Packaging;

/// <summary>
/// Checks the <c>mimetype</c> entry against §2.1, field by field.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="KomaMediaType.Sniff"/> answers a different question. It reads the
/// 24 bytes at offset 38 and says whether a file announces itself as KOMA,
/// which is what scanning a library needs and all it needs. It cannot say
/// <em>why</em> a file fails, because every §2.1 defect moves the media type
/// away from that offset and so looks alike from there: an entry that is
/// deflated, one that is not first, and one whose content is wrong all read as
/// "not the media type".
/// </para>
/// <para>
/// The corpus distinguishes them — <c>mimetype-position</c>,
/// <c>mimetype-compression</c> and <c>mimetype-content</c> are three codes —
/// and a reader that collapses them refuses the right packages while telling
/// their author the wrong thing. So this reads the local file header instead of
/// the offset alone.
/// </para>
/// </remarks>
public static class MimetypeEntryCheck
{
    private const uint LocalFileHeader = 0x04034B50;

    /// <summary>
    /// Checks the entry.
    /// </summary>
    /// <param name="stream">The package. Seekable; its position is restored.</param>
    /// <param name="archive">The same package, open, to tell an absent entry from a misplaced one.</param>
    /// <returns><see langword="null"/> when §2.1 is satisfied.</returns>
    public static ContainerViolation? Check(Stream stream, ZipArchive archive)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(archive);

        long position = stream.Position;

        try
        {
            return CheckCore(stream, archive);
        }
        finally
        {
            stream.Position = position;
        }
    }

    private static ContainerViolation? CheckCore(Stream stream, ZipArchive archive)
    {
        // Absent is not misplaced. An ordinary ZIP has no mimetype entry at
        // all, and saying its mimetype is in the wrong position would describe
        // a file that does not exist.
        if (archive.GetEntry(KomaMediaType.EntryName) is null)
            return Content("The package has no mimetype entry (§2.1).");

        if (stream.Length < KomaMediaType.HeaderLength)
            return Content($"The file is shorter than the {KomaMediaType.HeaderLength} bytes §2.1 fixes.");

        byte[] head = new byte[KomaMediaType.HeaderLength];
        stream.Position = 0;
        stream.ReadExactly(head);

        if (BinaryPrimitives.ReadUInt32LittleEndian(head) != LocalFileHeader)
            return Position("The file does not begin with a local file header, so something precedes the first entry (§2.1).");

        ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(26, 2));

        if (nameLength != KomaMediaType.EntryName.Length || !Encoding.ASCII.GetString(head, 30, KomaMediaType.EntryName.Length).Equals(KomaMediaType.EntryName, StringComparison.Ordinal))
            return Position("The mimetype entry is not the first physical entry (§2.1).");

        ushort method = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(8, 2));

        if (method != 0)
            return Compression($"The mimetype entry uses compression method {method}; §2.1 requires Store.");

        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(6, 2));

        if ((flags & 0x0001) != 0)
            return Content("The mimetype entry is encrypted (§2.1).");

        // A data descriptor defers the CRC and sizes past the entry, so the
        // local header no longer carries them. Named before the content is
        // compared, as the reference validator names it: the content check
        // would pass, the bytes at 38 being where they belong.
        if ((flags & 0x0008) != 0)
            return new ContainerViolation(ContainerViolationCode.MimetypeDataDescriptor, KomaMediaType.EntryName, "The mimetype entry defers its CRC and sizes to a data descriptor (§2.1).");

        ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(28, 2));

        // Named for what it is rather than for its effect: an extra field
        // pushes the media type off byte 38, which the content check would
        // report as the wrong bytes.
        if (extraLength != 0)
            return new ContainerViolation(ContainerViolationCode.MimetypeExtraField, KomaMediaType.EntryName, $"The mimetype entry carries {extraLength} bytes of extra fields (§2.1).");

        uint uncompressed = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(22, 4));

        if (uncompressed != KomaMediaType.SniffLength)
            return Content($"The mimetype entry declares {uncompressed} bytes; the media type is {KomaMediaType.SniffLength}.");

        ReadOnlySpan<byte> mediaType = head.AsSpan(KomaMediaType.SniffOffset, KomaMediaType.SniffLength);

        if (!mediaType.SequenceEqual(KomaMediaType.Utf8))
            return Content($"The bytes at offset {KomaMediaType.SniffOffset} are not '{KomaMediaType.Value}' (§2.1).");

        return null;
    }

    private static ContainerViolation Content(string message) => new(ContainerViolationCode.MimetypeContent, KomaMediaType.EntryName, message);
    private static ContainerViolation Position(string message) => new(ContainerViolationCode.MimetypePosition, KomaMediaType.EntryName, message);
    private static ContainerViolation Compression(string message) => new(ContainerViolationCode.MimetypeCompression, KomaMediaType.EntryName, message);
}
