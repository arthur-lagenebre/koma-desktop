using System.IO.Compression;

namespace Koma.Imaging.Tests;

/// <summary>
/// Finds the upstream corpus packages and reads page images out of them.
/// </summary>
/// <remarks>
/// Straight from the ZIP, past the opener: several of these packages are ones
/// the opener must refuse, and their pages are fixtures precisely because of
/// what makes them invalid. Pillow wrote them, so the decoder under test is
/// never reading its own output.
/// </remarks>
internal static class CorpusPage
{
    public static string PathOf(string package) => Path.Combine(PackagesDirectory(), package);

    public static byte[] Read(string package, string entry)
    {
        string path = PathOf(package);
        using ZipArchive archive = ZipFile.OpenRead(path);
        ZipArchiveEntry found = archive.GetEntry(entry) ?? throw new FileNotFoundException($"{entry} is not in {package}.", path);
        using Stream stream = found.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string PackagesDirectory()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "external", "koma", "corpus", "packages");

            if (Directory.Exists(candidate))
                return candidate;
        }

        throw new DirectoryNotFoundException("external/koma/corpus/packages is not above the test assembly. Was the submodule initialised?");
    }
}
