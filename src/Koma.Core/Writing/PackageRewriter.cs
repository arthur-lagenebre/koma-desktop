using System.IO.Compression;
using Koma.Core.Packaging;

namespace Koma.Core.Writing;

/// <summary>
/// Rewrites a package with some of its entries replaced.
/// </summary>
/// <remarks>
/// <para>
/// Written beside the original and moved over it only once complete, so that
/// a crash or a full disk leaves the publication as it was rather than half
/// of a new one. The move is an atomic replace on one volume.
/// </para>
/// <para>
/// Every other entry is copied as it was, pages included, one at a time from
/// the original to the new file: correcting a title in a 200-page album costs
/// a buffer, not the album. The whole goes through
/// <see cref="PackageWriter"/>, so an edited package is as reproducible as a
/// written one, whatever order the original held its entries in.
/// </para>
/// </remarks>
public static class PackageRewriter
{
    /// <param name="dropped">
    /// Entries to leave out of the new file: a page taken out of a
    /// publication goes with the manifest that declared it.
    /// </param>
    public static void Rewrite(string path, IReadOnlyDictionary<string, byte[]> replacements, IReadOnlySet<string>? dropped = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(replacements);

        string temporary = path + ".writing";

        try
        {
            // The original stays open while the new file is written from it,
            // and is closed before the move: Windows replaces no file in use.
            using (ZipArchive archive = ZipFile.OpenRead(path))
            {
                var entries = new Dictionary<string, Func<Stream>>(StringComparer.Ordinal);

                foreach (ZipArchiveEntry entry in archive.Entries.Where(e => e.Name.Length > 0 && e.FullName != KomaMediaType.EntryName && dropped?.Contains(e.FullName) != true))
                    entries[entry.FullName] = entry.Open;

                foreach ((string name, byte[] data) in replacements)
                    entries[name] = () => new MemoryStream(data, writable: false);

                using FileStream output = File.Create(temporary);
                PackageWriter.Write(output, entries);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            // The original is untouched until the move; what is left to undo
            // is the copy that did not make it.
            File.Delete(temporary);
            throw;
        }
    }
}
