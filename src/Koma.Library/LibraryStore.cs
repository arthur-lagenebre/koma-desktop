using System.Text.Json;
using System.Text.Json.Serialization;

namespace Koma.Library;

/// <summary>
/// Where the library lives on disk: one JSON file and a folder of thumbnails.
/// </summary>
/// <remarks>
/// <para>
/// A file rather than a database, because the index is a cache. What it holds
/// is rebuilt by a scan, apart from the reading positions, and a few thousand
/// publications are a few hundred kilobytes read once at startup.
/// </para>
/// <para>
/// The index is written whole to a temporary file and moved over the old one,
/// so that a crash mid-write leaves the previous index rather than half of a
/// new one.
/// </para>
/// </remarks>
public sealed class LibraryStore
{
    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string root;

    /// <param name="root">The folder holding <c>library.json</c> and the thumbnails.</param>
    public LibraryStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        this.root = root;
    }

    /// <summary>The store of the current user, under their application data.</summary>
    public static LibraryStore ForCurrentUser() => new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "koma-desktop"));

    public string IndexPath => Path.Combine(root, "library.json");

    public string ThumbnailFolder => Path.Combine(root, "thumbnails");

    /// <summary>
    /// The index as it was last written, or an empty one.
    /// </summary>
    /// <remarks>
    /// An index that cannot be read is treated as absent rather than as an
    /// error: it is a cache, and a scan rebuilds it. It is left on disk all
    /// the same, until the next save replaces it, so that a reading position
    /// is not thrown away over a fault that might be the disk's.
    /// </remarks>
    public LibraryIndex Load()
    {
        try
        {
            using FileStream file = File.OpenRead(IndexPath);

            return JsonSerializer.Deserialize<LibraryIndex>(file, Format) ?? LibraryIndex.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return LibraryIndex.Empty;
        }
    }

    public void Save(LibraryIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);

        Directory.CreateDirectory(root);

        string temporary = IndexPath + ".writing";

        using (FileStream file = File.Create(temporary))
            JsonSerializer.Serialize(file, index, Format);

        File.Move(temporary, IndexPath, overwrite: true);
    }

    /// <summary>Writes a cover thumbnail and answers the name to record.</summary>
    public string WriteThumbnail(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);

        Directory.CreateDirectory(ThumbnailFolder);

        string name = $"{Guid.NewGuid():n}.png";
        File.WriteAllBytes(ThumbnailPath(name), png);

        return name;
    }

    public string ThumbnailPath(string name) => Path.Combine(ThumbnailFolder, name);

    /// <summary>Removes a thumbnail whose entry has gone or been rebuilt.</summary>
    public void DeleteThumbnail(string? name)
    {
        if (name is null)
            return;

        try
        {
            File.Delete(ThumbnailPath(name));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A thumbnail left behind costs a few kilobytes; failing a scan
            // over one would cost the whole library.
        }
    }
}
