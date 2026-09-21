using System.IO.Compression;
using Koma.Core.Importing;
using Koma.TestSupport;

namespace Koma.Core.Tests;

/// <summary>
/// Which entries of a CBZ are pages, and in which order, held to what
/// <c>tools/cbz_to_koma.py</c> decides for the same three archives.
/// </summary>
/// <remarks>
/// The expected orders are the converter's own, read from its
/// <c>read_cbz</c> on these fixtures: the two tools have to agree on the
/// pages before they can agree on anything built from them.
/// </remarks>
public sealed class CbzReaderTests
{
    [Theory]
    [InlineData("manga.cbz", new[] { "page1.jpg", "page2.jpg", "page10.jpg", "page11.jpg", "page12.jpg" })]
    [InlineData("bare.cbz", new[] { "001.png", "002.png", "003.png" })]
    [InlineData("messy.cbz", new[] { "002.gif", "003.bmp", "cover.jpg" })]
    public void OrdersPagesAsTheReferenceConverterDoes(string cbz, string[] expected)
    {
        CbzContents contents = Read(cbz);

        Assert.Equal(expected, contents.Pages.Select(p => p.Entry));
    }

    [Fact]
    public void KeepsComicInfoApartFromThePages()
    {
        Assert.NotNull(Read("manga.cbz").ComicInfo);
        Assert.Null(Read("bare.cbz").ComicInfo);
    }

    [Fact]
    public void LeavesBehindWhatAnArchiverAddsAndWhatIsNoImage()
    {
        // messy.cbz carries __MACOSX/._x and .DS_Store, which are dropped
        // without a word, and a readme, which is set aside and named.
        CbzContents contents = Read("messy.cbz");

        string[] skipped = ["readme.txt"];

        Assert.Equal(skipped, contents.Skipped);
        Assert.DoesNotContain(contents.Pages, p => p.Entry.Contains("__MACOSX", StringComparison.Ordinal) || p.Entry.StartsWith('.'));
    }

    [Fact]
    public void ReadsWhatTheHeaderOfEachPageSays()
    {
        CbzContents manga = Read("manga.cbz");
        CbzContents messy = Read("messy.cbz");

        // A double page, known from its header without a pixel decoded.
        Assert.Equal((1600, 1200), (manga.Pages[2].Facts!.Width, manga.Pages[2].Facts!.Height));
        Assert.Equal("image/jpeg", manga.Pages[2].Facts!.MediaType);

        // The cover is a JPEG turned by EXIF, and the GIF and the BMP are
        // formats a package cannot carry: they wait to be decoded.
        Assert.Equal(6, messy.Pages[2].Facts!.ExifOrientation);
        Assert.Null(messy.Pages[0].Facts);
        Assert.Null(messy.Pages[1].Facts);
    }

    [Theory]
    [InlineData(0, 5, "image/jpeg", "pages/001.jpg", "p001")]
    [InlineData(164, 165, "image/png", "pages/165.png", "p165")]
    [InlineData(9, 1000, "image/webp", "pages/0010.webp", "p0010")]
    [InlineData(999, 1000, "image/png", "pages/1000.png", "p1000")]
    public void NamesPagesWithAsManyDigitsAsTheCountNeeds(int index, int count, string mediaType, string name, string id)
    {
        Assert.Equal(name, CbzReader.PageName(index, count, mediaType));
        Assert.Equal(id, CbzReader.PageId(index, count));
    }

    [Fact]
    public void RefusesToNameAPageInAFormatAPackageCannotCarry()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CbzReader.PageName(0, 1, "image/gif"));
    }

    private static CbzContents Read(string cbz)
    {
        using ZipArchive archive = ZipFile.OpenRead(Corpus.Example(cbz));

        return CbzReader.Read(archive);
    }
}
