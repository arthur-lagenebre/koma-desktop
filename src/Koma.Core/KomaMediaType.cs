namespace Koma.Core;

/// <summary>
/// The KOMA media type and the byte offsets §2.1 fixes for it.
/// </summary>
public static class KomaMediaType
{
    /// <summary>The media type, exactly as it must appear in the archive.</summary>
    public const string Value = "application/vnd.koma+zip";

    /// <summary>Name of the ZIP entry that carries it.</summary>
    public const string EntryName = "mimetype";

    /// <summary>
    /// Offset at which the media type begins, per §2.1: the local file header
    /// occupies 0–29 and the entry name occupies 30–37.
    /// </summary>
    public const int SniffOffset = 38;

    /// <summary>Length in bytes. ASCII, so one byte per character.</summary>
    public const int SniffLength = 24;

    /// <summary>Offset just past the media type; the first 62 bytes of the file.</summary>
    public const int HeaderLength = SniffOffset + SniffLength;

    /// <summary>
    /// The media type as ASCII bytes. §2.1 forbids a BOM, whitespace or a
    /// trailing newline, so this is the entire content of the entry.
    /// </summary>
    public static ReadOnlySpan<byte> Utf8 => "application/vnd.koma+zip"u8;

    /// <summary>
    /// Reads the 24 bytes at offset 38 and reports whether they are the KOMA
    /// media type. §2.1 permits this without opening the archive, which is what
    /// makes scanning a large library cheap.
    /// </summary>
    /// <remarks>
    /// A true result means the file announces itself as KOMA. It says nothing
    /// about whether the publication is valid, or whether its version is one
    /// this build supports — see the version portal of §5.0.
    /// </remarks>
    public static bool Sniff(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
            throw new ArgumentException("The stream must be seekable.", nameof(stream));

        if (stream.Length < HeaderLength)
            return false;

        Span<byte> buffer = stackalloc byte[SniffLength];

        stream.Seek(SniffOffset, SeekOrigin.Begin);
        stream.ReadExactly(buffer);

        return buffer.SequenceEqual(Utf8);
    }
}
