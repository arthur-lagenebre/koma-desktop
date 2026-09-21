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
/// Every other entry is copied as it was, pages included, and the whole goes
/// through <see cref="PackageWriter"/>: an edited package is as reproducible
/// as a written one, whatever order the original held its entries in.
/// </para>
/// </remarks>
public static class PackageRewriter
{
    public static void Rewrite(string path, IReadOnlyDictionary<string, byte[]> replacements)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(replacements);

        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        using (ZipArchive archive = ZipFile.OpenRead(path))
        {
            foreach (ZipArchiveEntry entry in archive.Entries.Where(e => e.Name.Length > 0 && e.FullName != KomaMediaType.EntryName))
            {
                using Stream stream = entry.Open();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                entries[entry.FullName] = buffer.ToArray();
            }
        }

        foreach ((string name, byte[] data) in replacements)
            entries[name] = data;

        string temporary = path + ".writing";

        try
        {
            using (FileStream output = File.Create(temporary))
                PackageWriter.Write(output, entries);

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
