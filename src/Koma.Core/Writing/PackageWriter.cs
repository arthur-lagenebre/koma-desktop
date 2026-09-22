using System.IO.Compression;
using System.Text;
using Koma.Core.Packaging;

namespace Koma.Core.Writing;

/// <summary>
/// Writes a package: the <c>mimetype</c> entry §2.1 fixes, then the entries
/// in the order §14.2 asks of a reproducible archive.
/// </summary>
/// <remarks>
/// <para>
/// The same publication written twice gives the same bytes, which is what
/// §14.2 is for: entries in one order, one fixed timestamp, nothing added.
/// Reproducibility is a SHOULD and never affects publication identity
/// (§7.2.1), so nothing here reads it back to check.
/// </para>
/// <para>
/// Names are checked against §3 before anything is written. An archive is
/// awkward to repair and a producer of invalid names is a bug to be told
/// about, not a file to be fixed later.
/// </para>
/// </remarks>
public static class PackageWriter
{
    /// <summary>
    /// §14.2: entries sorted by byte-wise comparison of their UTF-8 NFC form.
    /// </summary>
    /// <remarks>
    /// Byte-wise and not by culture: a sort that depends on where the file is
    /// written is not a fixed order at all.
    /// </remarks>
    private static readonly IComparer<string> ByLogicalName = Comparer<string>.Create(Compare);

    /// <summary>
    /// Writes a package to a stream.
    /// </summary>
    /// <param name="entries">
    /// Every entry but <c>mimetype</c>, by logical name. Their order here
    /// does not matter: §14.2 fixes the order they are written in.
    /// </param>
    /// <param name="leaveOpen">Whether to leave <paramref name="destination"/> open.</param>
    public static void Write(Stream destination, IReadOnlyDictionary<string, byte[]> entries, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(entries);

        Write(destination, entries.ToDictionary(e => e.Key, e => Source(e.Value), StringComparer.Ordinal), leaveOpen);
    }

    /// <summary>
    /// Writes a package whose entries are read as they are written.
    /// </summary>
    /// <param name="entries">
    /// Every entry but <c>mimetype</c>, by logical name, each opening a
    /// stream to read it from. One is opened at a time and closed before the
    /// next, so that a package of pages is copied through a buffer rather
    /// than held whole in memory.
    /// </param>
    public static void Write(Stream destination, IReadOnlyDictionary<string, Func<Stream>> entries, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(entries);

        Check(entries);

        using ZipArchive archive = KomaArchive.Create(destination, leaveOpen);

        foreach (string name in entries.Keys.Order(ByLogicalName))
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            entry.LastWriteTime = KomaArchive.FixedTimestamp;

            using Stream written = entry.Open();
            using Stream source = entries[name]() ?? throw new ArgumentException($"'{name}' opened no stream.", nameof(entries));

            source.CopyTo(written);
        }
    }

    private static Func<Stream> Source(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        return () => new MemoryStream(data, writable: false);
    }

    private static int Compare(string? left, string? right) => Utf8(left).AsSpan().SequenceCompareTo(Utf8(right));

    private static byte[] Utf8(string? name) => Encoding.UTF8.GetBytes((name ?? string.Empty).Normalize(NormalizationForm.FormC));

    private static void Check(IReadOnlyDictionary<string, Func<Stream>> entries)
    {
        var folded = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach ((string name, Func<Stream> open) in entries)
        {
            ArgumentNullException.ThrowIfNull(open);

            if (!KomaEntryName.TryValidate(name, out EntryNameProblem problem))
                throw new ArgumentException($"'{name}' is not an entry name §3 allows: {problem}.", nameof(entries));

            if (KomaEntryName.AreSameLogicalName(name, KomaMediaType.EntryName))
                throw new ArgumentException("The mimetype entry is written by this method and must not be given to it (§2.1).", nameof(entries));

            // §3: two names that fold to one are one entry, whatever a
            // dictionary of strings thinks of them.
            if (!folded.TryAdd(KomaEntryName.FoldForUniqueness(name), name))
                throw new ArgumentException($"'{name}' and '{folded[KomaEntryName.FoldForUniqueness(name)]}' are the same logical name after normalization and case folding (§3).", nameof(entries));
        }
    }
}
