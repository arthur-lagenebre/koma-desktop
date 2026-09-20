using Koma.Core.Rendering;
using Koma.TestSupport;
using SkiaSharp;

namespace Koma.Library.Tests;

/// <summary>
/// The library over a folder of corpus packages: what a scan finds, what it
/// keeps, and what it forgets.
/// </summary>
public sealed class LibraryTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("koma-library").FullName;
    private readonly string books;
    private readonly LibraryStore store;

    public LibraryTests()
    {
        books = Path.Combine(root, "books");
        Directory.CreateDirectory(books);
        store = new LibraryStore(Path.Combine(root, "data"));
    }

    [Fact]
    public void Scan_DescribesAPublicationAndKeepsItsCover()
    {
        Add("valid-minimal.koma");

        LibraryEntry entry = Assert.Single(Scan().Entries);

        Assert.Equal("Corpus de conformite", entry.Title);
        Assert.Equal(4, entry.PageCount);
        Assert.Equal(ReadingDirection.RightToLeft, entry.Direction);
        Assert.Null(entry.Unreadable);
        Assert.NotNull(entry.Thumbnail);

        // The cover is 800 by 1200; the thumbnail keeps its proportions.
        using SKBitmap? thumbnail = SKBitmap.Decode(store.ThumbnailPath(entry.Thumbnail));

        Assert.NotNull(thumbnail);
        Assert.Equal((267, 400), (thumbnail.Width, thumbnail.Height));
    }

    [Fact]
    public void Scan_RecordsWhyAPublicationCannotBeOpened()
    {
        Add("L1-absolute-path.koma");

        LibraryEntry entry = Assert.Single(Scan().Entries);

        Assert.NotNull(entry.Unreadable);
        Assert.Null(entry.Title);
        Assert.Null(entry.Thumbnail);
    }

    [Fact]
    public void Scan_LeavesAnUntouchedPublicationAloneWithItsReadingPosition()
    {
        Add("valid-minimal.koma");

        LibraryIndex first = Scan();
        LibraryIndex read = first with { Entries = [first.Entries[0] with { LastItem = "p003" }] };

        LibraryEntry entry = Assert.Single(Scan(read).Entries);

        // Same cover file: the publication was not opened again.
        Assert.Equal("p003", entry.LastItem);
        Assert.Equal(first.Entries[0].Thumbnail, entry.Thumbnail);
    }

    [Fact]
    public void Scan_DescribesAChangedPublicationAgainAndKeepsThePosition()
    {
        string path = Add("valid-minimal.koma");
        LibraryIndex first = Scan();
        LibraryIndex read = first with { Entries = [first.Entries[0] with { LastItem = "p002" }] };

        File.Copy(Corpus.Package("valid-page-list.koma"), path, overwrite: true);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        LibraryEntry entry = Assert.Single(Scan(read).Entries);

        Assert.Equal("p002", entry.LastItem);
        Assert.NotEqual(first.Entries[0].Thumbnail, entry.Thumbnail);
        Assert.False(File.Exists(store.ThumbnailPath(first.Entries[0].Thumbnail!)));
    }

    [Fact]
    public void Scan_ForgetsWhatHasLeftTheFolder()
    {
        string path = Add("valid-minimal.koma");
        LibraryIndex first = Scan();

        File.Delete(path);

        Assert.Empty(Scan(first).Entries);
        Assert.False(File.Exists(store.ThumbnailPath(first.Entries[0].Thumbnail!)));
    }

    [Fact]
    public void Scan_IgnoresWhatIsNotAPublicationAndReadsBelowTheFolder()
    {
        File.WriteAllText(Path.Combine(books, "notes.txt"), "not a publication");
        Directory.CreateDirectory(Path.Combine(books, "series"));
        File.Copy(Corpus.Package("valid-minimal.koma"), Path.Combine(books, "series", "one.koma"));

        Assert.Single(Scan().Entries);
    }

    [Fact]
    public void Store_RoundTripsTheIndex()
    {
        Add("valid-minimal.koma");
        LibraryIndex scanned = Scan();

        store.Save(scanned);
        LibraryIndex loaded = store.Load();

        Assert.Equal(scanned.Folders, loaded.Folders);
        Assert.Equal(scanned.Entries, loaded.Entries);
        Assert.False(File.Exists(store.IndexPath + ".writing"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{ not json")]
    public void Store_AnswersAnEmptyIndexWhenItCannotReadOne(string? content)
    {
        if (content is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(store.IndexPath)!);
            File.WriteAllText(store.IndexPath, content);
        }

        Assert.Empty(store.Load().Entries);

        // A damaged index is left where it is: the next save replaces it, and
        // until then its reading positions are not thrown away.
        Assert.Equal(content is not null, File.Exists(store.IndexPath));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temporary folder left behind is the operating system's to
            // clean up; failing the test over it would say nothing useful.
        }
    }

    private string Add(string package)
    {
        string path = Path.Combine(books, package);
        File.Copy(Corpus.Package(package), path);

        return path;
    }

    private LibraryIndex Scan(LibraryIndex? index = null) => LibraryScanner.Scan(index ?? LibraryIndex.Empty with { Folders = [books] }, store);
}
