using System.IO.Compression;

namespace Koma.Core.Packaging;

/// <summary>
/// Creates ZIP archives that satisfy the <c>mimetype</c> rules of §2.1.
/// </summary>
/// <remarks>
/// <para>
/// §2.1 constrains this entry more tightly than any other: it must be the first
/// physical entry, stored rather than deflated, unencrypted, free of extra
/// fields, and it must carry a correct CRC-32 and both size fields in its local
/// header rather than deferring them to a data descriptor. Together those rules
/// fix the first 62 bytes of the file.
/// </para>
/// <para>
/// The requirement is about physical position, so it cannot be checked after
/// the fact and cannot be repaired. It also cannot be checked <em>during</em>
/// writing: <see cref="ZipArchive.Entries"/> throws in
/// <see cref="ZipArchiveMode.Create"/>. Rather than guard against a mistake
/// that is invisible until the file is read back, this type removes the
/// opportunity to make it — callers never open the archive themselves, so
/// nothing can precede the mimetype entry.
/// </para>
/// <para>
/// Whether <see cref="ZipArchive"/> can satisfy §2.1 at all is the open
/// question <c>MimetypeEntryTests</c> settles. Returning a
/// <see cref="ZipArchive"/> does tie callers to that implementation; the
/// alternative is an abstraction over two ZIP writers before knowing whether a
/// second one is needed, which seems the worse trade at this stage. If the
/// tests fail, an interface goes here and this is the only file that changes.
/// </para>
/// </remarks>
public static class KomaArchive
{
    /// <summary>
    /// Opens a new archive for writing, with the <c>mimetype</c> entry already
    /// written as the first physical entry.
    /// </summary>
    /// <param name="stream">
    /// Destination. It must be seekable: on a forward-only stream
    /// <see cref="ZipArchive"/> falls back to a data descriptor, which §2.1
    /// forbids for this entry.
    /// </param>
    /// <param name="leaveOpen">
    /// Whether to leave <paramref name="stream"/> open once the archive is
    /// disposed.
    /// </param>
    public static ZipArchive Create(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
            throw new ArgumentException("The destination stream must be seekable, otherwise the mimetype entry gets a data descriptor, which §2.1 forbids.", nameof(stream));

        var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen);

        try
        {
            WriteMimetype(archive);
        }
        catch
        {
            archive.Dispose();
            throw;
        }

        return archive;
    }

    private static void WriteMimetype(ZipArchive archive)
    {
        // NoCompression is what asks for method 0 (Store). Whether the runtime
        // honours that, and whether it refrains from emitting extra fields, is
        // exactly what MimetypeEntryTests measures.
        ZipArchiveEntry entry = archive.CreateEntry(KomaMediaType.EntryName, CompressionLevel.NoCompression);

        using Stream stream = entry.Open();
        stream.Write(KomaMediaType.Utf8);
    }
}
