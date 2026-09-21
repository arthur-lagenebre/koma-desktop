using System.IO.Compression;
using System.Text;
using Koma.Core.Importing;
using Koma.Core.Model;
using Koma.Core.Packaging;
using Koma.Core.Writing;
using Koma.TestSupport;

namespace Koma.Imaging.Tests;

/// <summary>
/// A CBZ converted end to end, held to what <c>tools/cbz_to_koma.py</c>
/// writes for the same archives.
/// </summary>
/// <remarks>
/// The expected documents and notes are the reference converter's output on
/// these fixtures, notes less its command-line options. manga.cbz and
/// bare.cbz pass through untouched, so their packages agree entry for entry;
/// messy.cbz has three pages to re-encode, so its documents agree and its
/// page bytes cannot.
/// </remarks>
public sealed class CbzConverterTests
{
    private const string MangaManifest = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Manifest xmlns="urn:koma:manifest" version="0.9" metadata="koma/metadata.xml" navigation="koma/nav.xml">
          <Resources>
            <Item id="p001" href="pages/001.jpg" media-type="image/jpeg" width="800" height="1200" roles="front-cover"/>
            <Item id="p002" href="pages/002.jpg" media-type="image/jpeg" width="800" height="1200" roles="story"/>
            <Item id="p003" href="pages/003.jpg" media-type="image/jpeg" width="1600" height="1200" roles="story" page-span="2"/>
            <Item id="p004" href="pages/004.jpg" media-type="image/jpeg" width="800" height="1200" roles="advertisement"/>
            <Item id="p005" href="pages/005.jpg" media-type="image/jpeg" width="800" height="1200" roles="back-cover"/>
          </Resources>
          <Spine>
            <ItemRef item="p001"/>
            <ItemRef item="p002"/>
            <ItemRef item="p003"/>
            <ItemRef item="p004"/>
            <ItemRef item="p005"/>
          </Spine>
        </Manifest>

        """;

    private const string MangaNavigation = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Navigation xmlns="urn:koma:navigation" version="0.9">
          <Landmarks>
            <Landmark type="front-cover" item="p001"/>
            <Landmark type="back-cover" item="p005"/>
            <Landmark type="body-start" item="p002"/>
          </Landmarks>
        </Navigation>

        """;

    private const string MessyManifest = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Manifest xmlns="urn:koma:manifest" version="0.9" metadata="koma/metadata.xml" navigation="koma/nav.xml">
          <Resources>
            <Item id="p001" href="pages/001.png" media-type="image/png" width="600" height="900" roles="front-cover"/>
            <Item id="p002" href="pages/002.png" media-type="image/png" width="600" height="900" roles="story"/>
            <Item id="p003" href="pages/003.jpg" media-type="image/jpeg" width="800" height="1200" roles="story"/>
          </Resources>
          <Spine>
            <ItemRef item="p001"/>
            <ItemRef item="p002"/>
            <ItemRef item="p003"/>
          </Spine>
        </Manifest>

        """;

    [Fact]
    public void ConvertsAnUntouchedArchiveEntryForEntry()
    {
        CbzConversion conversion = Convert("manga.cbz", new ConversionOptions());

        string[] names = ["ComicInfo.xml", "META-INF/container.xml", "koma/manifest.xml", "koma/metadata.xml", "koma/nav.xml", "pages/001.jpg", "pages/002.jpg", "pages/003.jpg", "pages/004.jpg", "pages/005.jpg"];

        Assert.Equal(names, conversion.Entries.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(MangaManifest, Text(conversion, "koma/manifest.xml"));
        Assert.Equal(MangaNavigation, Text(conversion, "koma/nav.xml"));
        Assert.Contains("urn:uuid:a6c64b49-206e-5150-9ef3-450eb4f13108", Text(conversion, "koma/metadata.xml"), StringComparison.Ordinal);

        // The pages and the ComicInfo are the archive's own bytes.
        Assert.Equal(Source("manga.cbz", "page10.jpg"), conversion.Entries["pages/003.jpg"]);
        Assert.Equal(Source("manga.cbz", "ComicInfo.xml"), conversion.Entries["ComicInfo.xml"]);
    }

    [Fact]
    public void AgreesOnTheDocumentsOfAnArchiveWhosePagesAreReEncoded()
    {
        CbzConversion conversion = Convert("messy.cbz", new ConversionOptions());

        Assert.Equal(MessyManifest, Text(conversion, "koma/manifest.xml"));
    }

    [Theory]
    [InlineData("manga.cbz", new[]
    {
        "modified date taken from the archive timestamps (2026-09-07)",
        "ComicInfo does not distinguish people from organizations; every credit is written as type=\"person\": Claire Dupont, Marc Olivier, Yuki Tanaka",
        "pages renumbered to pages/001..005 in archive order; the source names are not preserved",
        "ComicInfo Page/@Image counts from 0 while page files count from 1: Image=\"0\" is pages/001, Image=\"N\" is page N+1"
    })]
    [InlineData("bare.cbz", new[]
    {
        "modified date taken from the archive timestamps (2026-09-07)",
        "no FrontCover in ComicInfo; 001.png taken as the cover",
        "2 of 3 pages carry no ComicInfo type and default to 'story'",
        "no title in ComicInfo, using 'Untitled'",
        "no language in ComicInfo, using the undetermined tag 'und'",
        "ComicInfo Manga='Unknown' does not state a reading direction; assuming ltr.",
        "pages renumbered to pages/001..003 in archive order; the source names are not preserved"
    })]
    [InlineData("messy.cbz", new[]
    {
        "modified date taken from the archive timestamps (2026-09-07)",
        "002.gif: animated image, only the first frame is kept",
        "002.gif: GIF re-encoded as PNG",
        "003.bmp: BMP re-encoded as PNG",
        "cover.jpg: EXIF orientation 6 applied to the pixels",
        "no FrontCover in ComicInfo; 002.gif taken as the cover",
        "2 of 3 pages carry no ComicInfo type and default to 'story'",
        "no title in ComicInfo, using 'Untitled'",
        "no language in ComicInfo, using the undetermined tag 'und'",
        "ComicInfo Manga='Unknown' does not state a reading direction; assuming ltr.",
        "pages renumbered to pages/001..003 in archive order; the source names are not preserved"
    })]
    public void SaysWhatItAssumedInTheReferenceConvertersWords(string cbz, string[] notes)
    {
        Assert.Equal(notes, Convert(cbz, new ConversionOptions()).Notes);
    }

    [Theory]
    [InlineData("manga.cbz")]
    [InlineData("bare.cbz")]
    [InlineData("messy.cbz")]
    public void WritesAPackageThatOpensAndWhosePagesPassTheirChecks(string cbz)
    {
        // With checksums, so that §8.6 is checked too: a re-encoded page must
        // carry the digest of the bytes written, not of the ones it came from.
        CbzConversion conversion = Convert(cbz, new ConversionOptions(Checksums: true));

        using var buffer = new MemoryStream();
        PackageWriter.Write(buffer, conversion.Entries, leaveOpen: true);
        buffer.Position = 0;

        PackageOpenResult result = PackageOpener.Open(buffer, leaveOpen: true);

        using KomaPackage? package = result.Package;

        Assert.Equal(PackageOpenOutcome.Opened, result.Outcome);
        Assert.NotNull(package);
        Assert.Empty(PageResourceChecks.CheckAll(package));
        Assert.DoesNotContain(result.Violations, v => v.Severity == ViolationSeverity.Error);
    }

    [Fact]
    public void RefusesAnArchiveWithAPageItCannotDecode()
    {
        // Skia reads no TIFF, where Pillow does: the conversion stops and says
        // which page, rather than giving a publication with a page missing.
        using MemoryStream cbz = Archive(("001.tif", [0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00]));
        using var archive = new ZipArchive(cbz, ZipArchiveMode.Read);

        InvalidDataException refused = Assert.Throws<InvalidDataException>(() => CbzConverter.Convert(archive, new ConversionOptions()));

        Assert.StartsWith("001.tif:", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAnArchiveWithNoImage()
    {
        using MemoryStream cbz = Archive(("readme.txt", "nothing to read"u8.ToArray()));
        using var archive = new ZipArchive(cbz, ZipArchiveMode.Read);

        Assert.Throws<InvalidDataException>(() => CbzConverter.Convert(archive, new ConversionOptions()));
    }

    private static CbzConversion Convert(string cbz, ConversionOptions options)
    {
        using ZipArchive archive = ZipFile.OpenRead(Corpus.Example(cbz));

        return CbzConverter.Convert(archive, options);
    }

    private static string Text(CbzConversion conversion, string entry) => Encoding.UTF8.GetString(conversion.Entries[entry]);

    private static byte[] Source(string cbz, string entry)
    {
        using ZipArchive archive = ZipFile.OpenRead(Corpus.Example(cbz));
        using Stream stream = archive.GetEntry(entry)!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        return buffer.ToArray();
    }

    private static MemoryStream Archive(params (string Name, byte[] Data)[] entries)
    {
        var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] data) in entries)
            {
                using Stream stream = archive.CreateEntry(name).Open();
                stream.Write(data);
            }
        }

        buffer.Position = 0;

        return buffer;
    }
}
