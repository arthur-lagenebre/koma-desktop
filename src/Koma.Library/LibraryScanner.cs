using Koma.Core.Model;
using Koma.Core.Packaging;
using Koma.Imaging;
using SkiaSharp;

namespace Koma.Library;

/// <param name="Done">Publications looked at, of <paramref name="Total"/>.</param>
public sealed record LibraryScanProgress(int Done, int Total, string Path);

/// <summary>
/// Brings the library up to date with the folders it watches.
/// </summary>
/// <remarks>
/// A publication is opened again only when its path, size or modification
/// time has changed. Opening one costs a decoded cover, so a library of a
/// thousand books is scanned once and read from the index thereafter.
/// </remarks>
public static class LibraryScanner
{
    // Tall enough for a cover in a grid on a dense display, small enough that
    // a thousand of them stay a few tens of megabytes.
    private const int ThumbnailHeight = 400;

    private static readonly EnumerationOptions Walk = new() { RecurseSubdirectories = true, IgnoreInaccessible = true };

    public static LibraryIndex Scan(LibraryIndex index, LibraryStore store, IProgress<LibraryScanProgress>? progress = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(store);

        string[] files = [.. index.Folders.SelectMany(Publications).Distinct(StringComparer.Ordinal)];

        // An index described under an older format is missing what this one
        // records, so nothing in it counts as up to date.
        bool current = index.Format == LibraryIndex.CurrentFormat;
        Dictionary<string, LibraryEntry> known = index.Entries.GroupBy(e => e.Path, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var entries = new List<LibraryEntry>(files.Length);

        foreach (string file in files)
        {
            token.ThrowIfCancellationRequested();

            var found = new FileInfo(file);
            known.TryGetValue(file, out LibraryEntry? existing);

            // Same size and time: the file has not changed, and neither has
            // anything the index says about it, the reading position included.
            entries.Add(current && existing is not null && existing.Size == found.Length && existing.Modified == new DateTimeOffset(found.LastWriteTimeUtc) ? existing : Describe(found, existing, store));
            progress?.Report(new LibraryScanProgress(entries.Count, files.Length, file));
        }

        var kept = new HashSet<string>(entries.Select(e => e.Path), StringComparer.Ordinal);

        // What is no longer in a watched folder leaves, and its cover with it.
        foreach (LibraryEntry gone in index.Entries.Where(e => !kept.Contains(e.Path)))
            store.DeleteThumbnail(gone.Thumbnail);

        return index with { Entries = entries, Format = LibraryIndex.CurrentFormat };
    }

    private static IEnumerable<string> Publications(string folder)
    {
        try
        {
            return [.. Directory.EnumerateFiles(folder, "*.koma", Walk)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A folder that has gone, or that is not ours to read, empties
            // rather than failing the scan of the others.
            return [];
        }
    }

    private static LibraryEntry Describe(FileInfo file, LibraryEntry? previous, LibraryStore store)
    {
        store.DeleteThumbnail(previous?.Thumbnail);

        // The reading position survives a changed file: the reader was in this
        // publication, and the pages it names are most likely still there.
        var entry = new LibraryEntry
        {
            Path = file.FullName,
            Size = file.Length,
            Modified = new DateTimeOffset(file.LastWriteTimeUtc),
            LastItem = previous?.LastItem,
            LastPage = previous?.LastPage ?? 0,
            LastOpened = previous?.LastOpened
        };

        PackageOpenResult result;

        try
        {
            result = PackageOpener.Open(file.OpenRead());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return entry with { Unreadable = e.Message };
        }

        using KomaPackage? package = result.Package;

        if (package is null)
            return entry with { Unreadable = Refusal(result) };

        return entry with
        {
            Title = package.Metadata.MainTitle.Text,
            Direction = package.Metadata.Direction,
            Series = package.Metadata.Series?.Name,
            SeriesPosition = package.Metadata.Series?.Position,
            PageCount = package.Manifest.Spine.Count,
            Thumbnail = Cover(package, store)
        };
    }

    private static string Refusal(PackageOpenResult result)
    {
        if (result.Outcome == PackageOpenOutcome.UnsupportedVersion)
            return $"KOMA {result.DeclaredVersion?.ToString() ?? "of an unknown version"}, which this build does not read (§5.0).";

        ContainerViolation? first = result.Violations.FirstOrDefault(v => v.Severity == ViolationSeverity.Error);

        return first is null ? "The package could not be opened." : $"{first.Code} — {first.Message}";
    }

    private static string? Cover(KomaPackage package, LibraryStore store)
    {
        ManifestItem? cover = package.Manifest.FrontCover;

        if (cover is null)
            return null;

        // Through the loader, so that §16 decides: a cover the reader would
        // withhold is not a cover the library shows either.
        using LoadedPage page = PageLoader.Load(package, cover);

        if (page.Bitmap is null)
            return null;

        using SKBitmap thumbnail = Scale(page.Bitmap);
        using SKImage image = SKImage.FromBitmap(thumbnail);
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);

        return store.WriteThumbnail(png.ToArray());
    }

    private static SKBitmap Scale(SKBitmap bitmap)
    {
        int height = Math.Min(ThumbnailHeight, bitmap.Height);
        int width = Math.Max(1, (int)Math.Round(bitmap.Width * (double)height / bitmap.Height));
        SKBitmap? scaled = bitmap.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

        // Resize answers null when it cannot allocate; the full-size cover is
        // a worse thumbnail than a scaled one, and a better one than none.
        return scaled ?? bitmap.Copy();
    }
}
