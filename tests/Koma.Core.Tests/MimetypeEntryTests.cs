using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Koma.Core.Packaging;

namespace Koma.Core.Tests;

/// <summary>
/// Byte-level assertions on the layout §2.1 fixes for the start of a KOMA file.
/// </summary>
/// <remarks>
/// These tests check the fields the specification actually constrains, not the
/// whole 62-byte prefix. Bytes 10–13 hold the DOS modification time and date;
/// §2.1 fixes their <em>position</em> but not their value, and §14.2 asks only
/// that an authoring tool SHOULD use a fixed timestamp. Asserting on them would
/// be testing our own packaging convention while pretending to test the format.
/// </remarks>
public sealed class MimetypeEntryTests
{
    // CRC-32 of the 24 ASCII bytes of the media type. §2.1 requires the real
    // value in the local header, so a placeholder or zero is a failure.
    private const uint MediaTypeCrc32 = 0x13F6B350;

    private static byte[] BuildArchive()
    {
        using var buffer = new MemoryStream();

        using (ZipArchive archive = KomaArchive.Create(buffer, leaveOpen: true))
        {
            // A second entry, so that the test measures a realistic archive
            // rather than a degenerate one-entry case.
            ZipArchiveEntry other = archive.CreateEntry("koma/package.xml");
            using Stream stream = other.Open();
            stream.Write("<package/>"u8);
        }

        return buffer.ToArray();
    }

    [Fact]
    public void LocalHeader_StartsAtOffsetZero()
    {
        byte[] bytes = BuildArchive();

        Assert.True(bytes.Length >= KomaMediaType.HeaderLength, $"Archive is {bytes.Length} bytes, shorter than the 62 §2.1 fixes.");

        // Local file header signature. §2.1 forbids prepended data such as a
        // self-extracting stub, so this must be the very start of the file.
        Assert.Equal(0x04034B50u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
    }

    [Fact]
    public void CompressionMethod_IsStore()
    {
        byte[] bytes = BuildArchive();

        ushort method = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8, 2));

        Assert.True(method == 0, $"§2.1 requires Store (0); the archive uses method {method}. Method 8 means CompressionLevel.NoCompression produced Deflate, and System.IO.Compression cannot write a conforming mimetype entry.");
    }

    [Fact]
    public void GeneralPurposeFlags_ForbidEncryptionAndDataDescriptor()
    {
        byte[] bytes = BuildArchive();

        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6, 2));

        Assert.True((flags & 0x0001) == 0, "§2.1 forbids encryption; bit 0 is set.");
        Assert.True((flags & 0x0008) == 0, "§2.1 forbids a data descriptor; bit 3 is set, so the CRC and sizes are deferred instead of being present in the local header.");
    }

    [Fact]
    public void LocalHeader_CarriesCorrectCrcAndSizes()
    {
        byte[] bytes = BuildArchive();

        uint crc = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(14, 4));
        uint compressed = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(18, 4));
        uint uncompressed = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(22, 4));

        Assert.Equal(MediaTypeCrc32, crc);
        Assert.Equal((uint)KomaMediaType.SniffLength, uncompressed);

        // Stored means the two sizes agree. If they differ, the entry was
        // compressed regardless of what the method field claims.
        Assert.Equal(uncompressed, compressed);
    }

    [Fact]
    public void LocalHeader_HasNoExtraFields()
    {
        byte[] bytes = BuildArchive();

        ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(26, 2));
        ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28, 2));

        Assert.Equal((ushort)KomaMediaType.EntryName.Length, nameLength);

        // Any extra field shifts the media type away from offset 38 and breaks
        // the sniffing guarantee §2.1 grants to consumers.
        Assert.True(extraLength == 0, $"§2.1 forbids extra fields on this entry; found {extraLength} bytes.");
    }

    [Fact]
    public void EntryName_OccupiesOffsets30To37()
    {
        byte[] bytes = BuildArchive();

        string name = Encoding.ASCII.GetString(bytes, 30, KomaMediaType.EntryName.Length);

        Assert.Equal(KomaMediaType.EntryName, name);
    }

    [Fact]
    public void MediaType_OccupiesOffsets38To61()
    {
        byte[] bytes = BuildArchive();

        ReadOnlySpan<byte> actual = bytes.AsSpan(KomaMediaType.SniffOffset, KomaMediaType.SniffLength);

        Assert.True(actual.SequenceEqual(KomaMediaType.Utf8), $"Expected '{KomaMediaType.Value}' at offset {KomaMediaType.SniffOffset}, found '{Encoding.ASCII.GetString(actual)}'.");
    }

    [Fact]
    public void Archive_RemainsReadableAsAnOrdinaryZip()
    {
        using var stream = new MemoryStream(BuildArchive());
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        // Entries here follow the central directory, not physical order, so
        // this complements the offset assertions rather than replacing them.
        // What it proves is that the entry survives a round trip intact and
        // that the archive is not malformed for ordinary ZIP tools.
        ZipArchiveEntry? entry = archive.GetEntry(KomaMediaType.EntryName);
        Assert.NotNull(entry);
        Assert.Equal(KomaMediaType.SniffLength, entry.Length);

        using Stream content = entry.Open();
        using var reader = new StreamReader(content, Encoding.ASCII);
        Assert.Equal(KomaMediaType.Value, reader.ReadToEnd());
    }

    [Fact]
    public void Sniff_RecognisesAnArchiveWeWrote()
    {
        using var stream = new MemoryStream(BuildArchive());

        Assert.True(KomaMediaType.Sniff(stream));
    }

    [Fact]
    public void Sniff_RejectsAnOrdinaryZip()
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry("readme.txt");
            using Stream stream = entry.Open();
            stream.Write("not a koma file"u8);
        }

        buffer.Position = 0;

        Assert.False(KomaMediaType.Sniff(buffer));
    }

    [Fact]
    public void Create_RejectsANonSeekableStream()
    {
        using var stream = new NonSeekableStream();

        // A forward-only stream would get a data descriptor, which §2.1 forbids.
        Assert.Throws<ArgumentException>(() => KomaArchive.Create(stream));
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }
}
