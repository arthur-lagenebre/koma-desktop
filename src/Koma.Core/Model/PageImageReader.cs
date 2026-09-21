using System.Buffers.Binary;

namespace Koma.Core.Model;

/// <summary>
/// What a page resource says about itself, read from its own bytes.
/// </summary>
public sealed record PageImageFacts
{
    /// <summary>The media type the byte signature implies, never the declared one.</summary>
    public required string MediaType { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>Whether the file carries animation (§8.1).</summary>
    public bool IsAnimated { get; init; }

    /// <summary>
    /// The EXIF orientation tag, when one is present. §8.2 requires it to be
    /// <c>1</c> or absent, the producer having applied the rotation already.
    /// </summary>
    public int? ExifOrientation { get; init; }

    /// <summary>
    /// Whether a JPEG carries four components, CMYK or YCCK, which §8.1 puts
    /// outside the base profile: a producer converts such a page to RGB.
    /// </summary>
    public bool IsCmyk { get; init; }
}

/// <summary>
/// Reads the header of a page resource (§8.1, §8.2).
/// </summary>
/// <remarks>
/// <para>
/// Headers only, by hand, for the three media types §8.1 admits. An imaging
/// library would decode the pixels, which is exactly what must not happen here:
/// the question is what the file declares about itself, and a decoder that
/// silently corrects a malformed header answers a different one. It would also
/// put a dependency in <c>Koma.Core</c> for something that is a few hundred
/// bytes of parsing.
/// </para>
/// <para>
/// Nothing here trusts a length it has not checked against the buffer. These
/// are attacker-controlled bytes, and a header is the part an attacker writes
/// most cheaply.
/// </para>
/// </remarks>
public static class PageImageReader
{
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string WebP = "image/webp";

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Reads what the bytes say, or <see langword="null"/> when they are not
    /// one of the three page media types, or are truncated.
    /// </summary>
    public static PageImageFacts? TryRead(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(PngSignature))
            return ReadPng(bytes);

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ReadJpeg(bytes);

        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
            return ReadWebP(bytes);

        return null;
    }

    /// <summary>
    /// PNG: the IHDR chunk is required to come first, so the dimensions sit at
    /// a fixed offset. <c>acTL</c> anywhere marks an animated PNG.
    /// </summary>
    private static PageImageFacts? ReadPng(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 33 || !bytes[12..16].SequenceEqual("IHDR"u8))
            return null;

        return new PageImageFacts
        {
            MediaType = Png,
            Width = BinaryPrimitives.ReadInt32BigEndian(bytes[16..]),
            Height = BinaryPrimitives.ReadInt32BigEndian(bytes[20..]),
            IsAnimated = HasPngChunk(bytes, "acTL"u8)
        };
    }

    private static bool HasPngChunk(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> type)
    {
        int at = 8;

        while (at + 8 <= bytes.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(bytes[at..]);

            if (bytes.Slice(at + 4, 4).SequenceEqual(type))
                return true;

            // Length, type, data, CRC. A chunk claiming more than the file
            // holds ends the walk rather than running past it.
            if (length > int.MaxValue - 12 || at + 12 + (int)length > bytes.Length)
                return false;

            at += 12 + (int)length;
        }

        return false;
    }

    /// <summary>
    /// JPEG: the segments are walked for a start-of-frame, which carries the
    /// dimensions, and for an APP1 EXIF segment.
    /// </summary>
    private static PageImageFacts? ReadJpeg(ReadOnlySpan<byte> bytes)
    {
        int at = 2;
        int? orientation = null;

        while (at + 4 <= bytes.Length)
        {
            if (bytes[at] != 0xFF)
                return null;

            byte marker = bytes[at + 1];
            int length = BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 2)..]);

            if (length < 2 || at + 2 + length > bytes.Length)
                return null;

            ReadOnlySpan<byte> payload = bytes.Slice(at + 4, length - 2);

            if (marker == 0xE1 && payload.Length > 6 && payload[..6].SequenceEqual("Exif\0\0"u8))
                orientation = ReadExifOrientation(payload[6..]);

            // SOF0 through SOF15, less the four markers in that range that are
            // not frame headers: DHT, JPG, DAC and the restart markers.
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                // Precision, height, width, then the component count.
                if (payload.Length < 6)
                    return null;

                return new PageImageFacts
                {
                    MediaType = Jpeg,
                    Height = BinaryPrimitives.ReadUInt16BigEndian(payload[1..]),
                    Width = BinaryPrimitives.ReadUInt16BigEndian(payload[3..]),
                    ExifOrientation = orientation,
                    IsCmyk = payload[5] == 4
                };
            }

            // Start of scan: the entropy-coded data follows and there is no
            // frame header left to find.
            if (marker == 0xDA)
                return null;

            at += 2 + length;
        }

        return null;
    }

    /// <summary>
    /// The orientation tag of the first IFD, or <see langword="null"/>.
    /// </summary>
    private static int? ReadExifOrientation(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8)
            return null;

        bool little = tiff[0] == 'I' && tiff[1] == 'I';

        if (!little && !(tiff[0] == 'M' && tiff[1] == 'M'))
            return null;

        uint offset = Read32(tiff[4..], little);

        if (offset > int.MaxValue || offset + 2 > (uint)tiff.Length)
            return null;

        ReadOnlySpan<byte> ifd = tiff[(int)offset..];
        int count = Read16(ifd, little);

        if (2 + (count * 12) > ifd.Length)
            return null;

        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = ifd.Slice(2 + (i * 12), 12);

            // 0x0112 is Orientation; type 3 is SHORT, and its value sits in
            // the entry itself rather than at an offset.
            if (Read16(entry, little) == 0x0112 && Read16(entry[2..], little) == 3)
                return Read16(entry[8..], little);
        }

        return null;
    }

    /// <summary>
    /// WebP: a lossy, lossless or extended file. Only the extended form can be
    /// animated, and it is the only one whose canvas size is stated outright.
    /// </summary>
    private static PageImageFacts? ReadWebP(ReadOnlySpan<byte> bytes)
    {
        int at = 12;

        while (at + 8 <= bytes.Length)
        {
            ReadOnlySpan<byte> tag = bytes.Slice(at, 4);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(at + 4)..]);

            if (size > int.MaxValue - 8 || at + 8 + (int)size > bytes.Length)
                return null;

            ReadOnlySpan<byte> payload = bytes.Slice(at + 8, (int)size);

            if (tag.SequenceEqual("VP8X"u8) && payload.Length >= 10)
            {
                return new PageImageFacts
                {
                    MediaType = WebP,
                    Width = Read24(payload[4..]) + 1,
                    Height = Read24(payload[7..]) + 1,
                    IsAnimated = (payload[0] & 0x02) != 0
                };
            }

            if (tag.SequenceEqual("VP8 "u8) && payload.Length >= 10)
            {
                return new PageImageFacts
                {
                    MediaType = WebP,
                    Width = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]) & 0x3FFF,
                    Height = BinaryPrimitives.ReadUInt16LittleEndian(payload[8..]) & 0x3FFF
                };
            }

            if (tag.SequenceEqual("VP8L"u8) && payload.Length >= 5)
            {
                uint packed = BinaryPrimitives.ReadUInt32LittleEndian(payload[1..]);

                return new PageImageFacts
                {
                    MediaType = WebP,
                    Width = (int)(packed & 0x3FFF) + 1,
                    Height = (int)((packed >> 14) & 0x3FFF) + 1
                };
            }

            // Chunks are padded to an even length.
            at += 8 + (int)size + ((int)size & 1);
        }

        return null;
    }

    private static int Read24(ReadOnlySpan<byte> bytes) => bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);

    private static int Read16(ReadOnlySpan<byte> bytes, bool little) => little ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes);

    private static uint Read32(ReadOnlySpan<byte> bytes, bool little) => little ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes);
}
