using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Compression;
using Koma.Core.Model;

namespace Koma.Core.Importing;

/// <summary>
/// One page of a CBZ, as the archive holds it.
/// </summary>
/// <param name="Entry">The name it has in the CBZ.</param>
/// <param name="Facts">
/// What its header says — media type, size, animation, EXIF orientation — or
/// <see langword="null"/> for a format a package cannot carry (GIF, BMP,
/// TIFF), which has to be decoded to be known and re-encoded to be kept.
/// </param>
public sealed record CbzPage(string Entry, PageImageFacts? Facts);

/// <summary>
/// What a CBZ turns out to contain.
/// </summary>
/// <param name="Pages">The images, in reading order.</param>
/// <param name="ComicInfo">The bytes of <c>ComicInfo.xml</c>, when the archive carries one.</param>
/// <param name="Skipped">
/// Entries left behind because nothing about them says image: a readme, a
/// checksum file, a scan log.
/// </param>
public sealed record CbzContents(ReadOnlyCollection<CbzPage> Pages, byte[]? ComicInfo, ReadOnlyCollection<string> Skipped);

/// <summary>
/// Reads a CBZ: which entries are pages, and in which order.
/// </summary>
/// <remarks>
/// <para>
/// A CBZ is a folder of images in a zip, with no manifest and no order but
/// the one its file names imply. Everything decided here is decided the way
/// <c>tools/cbz_to_koma.py</c> decides it, so that the two tools convert one
/// archive into one publication.
/// </para>
/// <para>
/// Nothing is read from an extension but whether the entry is worth opening:
/// §8.1 takes a media type from the bytes, and a CBZ full of PNG data named
/// <c>.jpg</c> is a common enough thing.
/// </para>
/// </remarks>
public static class CbzReader
{
    /// <summary>Extensions worth opening, which says nothing about what is inside.</summary>
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".jpe",
        ".png",
        ".webp",
        ".gif",
        ".bmp",
        ".tif",
        ".tiff"
    };

    public static CbzContents Read(ZipArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);

        var pages = new List<CbzPage>();
        var skipped = new List<string>();
        byte[]? comicInfo = null;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string name = entry.Name;

            // A directory entry has no name of its own. The dot files and the
            // __MACOSX folder are what an archiver adds, never what an author
            // drew.
            if (name.Length == 0 || name.StartsWith('.') || entry.FullName.Contains("__MACOSX", StringComparison.Ordinal))
                continue;

            if (string.Equals(name, "ComicInfo.xml", StringComparison.OrdinalIgnoreCase))
            {
                comicInfo = Read(entry);
                continue;
            }

            if (!ImageExtensions.Contains(Path.GetExtension(name)))
            {
                skipped.Add(entry.FullName);
                continue;
            }

            pages.Add(new CbzPage(entry.FullName, PageImageReader.TryRead(Read(entry))));
        }

        pages.Sort((left, right) => NaturalOrder.Instance.Compare(left.Entry, right.Entry));

        return new CbzContents(pages.AsReadOnly(), comicInfo, skipped.AsReadOnly());
    }

    /// <summary>
    /// The name a page takes in the package (§8.1.1).
    /// </summary>
    /// <remarks>
    /// Three digits at least, and more only when the count needs them, so
    /// that a 165-page album numbers 001 to 165 and its names sort the way
    /// its pages are read.
    /// </remarks>
    public static string PageName(int index, int count, string mediaType) => $"pages/{Number(index, count)}{Extension(mediaType)}";

    /// <summary>The item id of a page, numbered as its name is.</summary>
    public static string PageId(int index, int count) => $"p{Number(index, count)}";

    private static string Number(int index, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, index + 1);

        int width = Math.Max(3, count.ToString(CultureInfo.InvariantCulture).Length);

        return (index + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
    }

    private static string Extension(string mediaType) => mediaType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => throw new ArgumentOutOfRangeException(nameof(mediaType), mediaType, "§16 has three page formats, and this is none of them.")
    };

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        return buffer.ToArray();
    }
}
