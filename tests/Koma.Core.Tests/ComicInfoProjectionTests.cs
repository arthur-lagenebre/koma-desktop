using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Koma.Core.Importing;
using Koma.Core.Rendering;
using Koma.Core.Writing;
using Koma.TestSupport;

namespace Koma.Core.Tests;

/// <summary>
/// ComicInfo projected onto the metadata of §7, held to the reference
/// converter's output to the byte.
/// </summary>
/// <remarks>
/// The expected documents are what <c>tools/cbz_to_koma.py</c> writes for
/// these fixtures, whose pages pass through untouched, so their digests and
/// the identifier derived from them are the same in both tools. Regenerate
/// them with <c>python tools/cbz_to_koma.py examples/manga.cbz out.koma</c>
/// in the specification repository if the converter changes.
/// </remarks>
public sealed class ComicInfoProjectionTests
{
    private const string Manga = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Metadata xmlns="urn:koma:metadata" version="0.9" xml:lang="fr">
          <Identifiers>
            <Identifier scheme="uuid" primary="true">urn:uuid:a6c64b49-206e-5150-9ef3-450eb4f13108</Identifier>
          </Identifiers>
          <Titles>
            <Title type="main">La traversee</Title>
          </Titles>
          <Languages>
            <Language role="content">fr</Language>
          </Languages>
          <Collections>
            <Collection type="series" position="3" total="12">
              <Name>Chroniques du Rivage</Name>
            </Collection>
          </Collections>
          <Contributors>
            <Contributor type="person" roles="writer;penciller">
              <Name>Claire Dupont</Name>
            </Contributor>
            <Contributor type="person" roles="writer">
              <Name>Marc Olivier</Name>
            </Contributor>
            <Contributor type="person" roles="colorist">
              <Name>Yuki Tanaka</Name>
            </Contributor>
          </Contributors>
          <Descriptions>
            <Description type="summary">Alix quitte le port.</Description>
          </Descriptions>
          <Publication>
            <Publisher>Editions Exemple</Publisher>
            <Date event="publication">2019-04-17</Date>
            <Date event="modified">2026-09-07</Date>
          </Publication>
          <Subjects>
            <Subject type="genre">Science-fiction</Subject>
            <Subject type="genre">Aventure</Subject>
          </Subjects>
          <Reading direction="rtl" spread="auto"/>
          <Content color-mode="monochrome"/>
          <Ratings>
            <Rating scheme="comicinfo-agerating" value="Teen"/>
          </Ratings>
          <Links>
            <Link rel="other" href="https://example.org/rivage-3"/>
          </Links>
          <Provenance>
            <Note>Converted from a CBZ archive.</Note>
          </Provenance>
        </Metadata>

        """;

    private const string Bare = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Metadata xmlns="urn:koma:metadata" version="0.9" xml:lang="und">
          <Identifiers>
            <Identifier scheme="uuid" primary="true">urn:uuid:2eec1530-ac3b-523f-a62a-1dc1bc4561f9</Identifier>
          </Identifiers>
          <Titles>
            <Title type="main">Untitled</Title>
          </Titles>
          <Languages>
            <Language role="content">und</Language>
          </Languages>
          <Publication>
            <Date event="modified">2026-09-07</Date>
          </Publication>
          <Reading direction="ltr" spread="auto"/>
          <Provenance>
            <Note>Converted from a CBZ archive.</Note>
          </Provenance>
        </Metadata>

        """;

    [Fact]
    public void ProjectsAFullComicInfoAsTheReferenceConverterDoes()
    {
        (string metadata, ReadingDirection direction, List<string> notes) = Project("manga.cbz");

        Assert.Equal(Manga, metadata);
        Assert.Equal(ReadingDirection.RightToLeft, direction);

        string[] expected = ["ComicInfo does not distinguish people from organizations; every credit is written as type=\"person\": Claire Dupont, Marc Olivier, Yuki Tanaka"];
        Assert.Equal(expected, notes);
    }

    [Fact]
    public void AssumesWhatAnArchiveWithoutComicInfoCannotSay()
    {
        (string metadata, ReadingDirection direction, List<string> notes) = Project("bare.cbz");

        Assert.Equal(Bare, metadata);
        Assert.Equal(ReadingDirection.LeftToRight, direction);

        string[] expected =
        [
            "no title in ComicInfo, using 'Untitled'",
            "no language in ComicInfo, using the undetermined tag 'und'",
            "ComicInfo Manga='Unknown' does not state a reading direction; assuming ltr."
        ];
        Assert.Equal(expected, notes);
    }

    [Theory]
    [InlineData("<ComicInfo><Series>Rivage</Series><Number>3</Number></ComicInfo>", "Rivage 3", "no Title in ComicInfo; built 'Rivage 3' from Series and Number")]
    [InlineData("<ComicInfo><Series>Rivage</Series></ComicInfo>", "Rivage", "no Title in ComicInfo; using the series name 'Rivage'")]
    [InlineData("<ComicInfo><Series>L'aube</Series></ComicInfo>", "L'aube", "no Title in ComicInfo; using the series name \"L'aube\"")]
    public void BuildsATitleFromTheSeriesWhenThereIsNone(string comicInfo, string title, string note)
    {
        var notes = new List<string>();
        ComicInfo parsed = ComicInfo.Parse(Encoding.UTF8.GetBytes(comicInfo), notes);

        var metadata = ComicInfoProjection.Metadata(parsed, new ConversionOptions(Language: "fr", Direction: ReadingDirection.LeftToRight), [], notes).Metadata;

        Assert.Contains($"<Title type=\"main\">{title}</Title>", Encoding.UTF8.GetString(CanonicalXml.Write(metadata)), StringComparison.Ordinal);
        Assert.Equal(note, notes[0]);
    }

    [Theory]
    [InlineData("3", "7", "3")]
    [InlineData(null, "7", "7")]
    [InlineData(null, null, null)]
    public void TakesAVolumeNumberFromTheArchiveWhenComicInfoGivesNone(string? stated, string? fromName, string? expected)
    {
        // A collection often numbers its files and not its metadata, and what
        // ComicInfo does say is never overruled by a file name.
        var notes = new List<string>();
        string number = stated is null ? string.Empty : $"<Number>{stated}</Number>";
        ComicInfo parsed = ComicInfo.Parse(Encoding.UTF8.GetBytes($"<ComicInfo><Series>Rivage</Series>{number}</ComicInfo>"), notes);

        XDocument metadata = ComicInfoProjection.Metadata(parsed, new ConversionOptions(Language: "fr", Direction: ReadingDirection.LeftToRight, Number: fromName), [], notes).Metadata;
        XNamespace m = "urn:koma:metadata";

        Assert.Equal(expected, (string?)metadata.Root!.Element(m + "Collections")!.Element(m + "Collection")!.Attribute("position"));
        Assert.Equal(stated is null && fromName is not null, notes.Any(n => n.Contains("taken from the name of the archive", StringComparison.Ordinal)));
    }

    [Fact]
    public void SaysHowThePublicationIsReadWhenTheConversionIsTold()
    {
        // No CBZ says how its pages are taken in, so §7.13 can only carry
        // what the importer was told, and the notes say so.
        var notes = new List<string>();
        ComicInfo parsed = ComicInfo.Parse(Encoding.UTF8.GetBytes("<ComicInfo><Series>Rivage</Series></ComicInfo>"), notes);
        var options = new ConversionOptions(Language: "fr", Direction: ReadingDirection.LeftToRight, AccessModes: ["visual"], AccessibilityHazards: ["no-flashing-hazard"]);

        XDocument metadata = ComicInfoProjection.Metadata(parsed, options, [], notes).Metadata;
        XNamespace m = "urn:koma:metadata";
        XElement section = metadata.Root!.Element(m + "Accessibility")!;

        string[] written = ["AccessMode", "AccessibilityHazard"];
        Assert.Equal(written, section.Elements().Select(e => e.Name.LocalName));
        Assert.Contains(notes, n => n.Contains("accessibility declared by the importer", StringComparison.Ordinal));

        // §7.13 wants an access mode in the section, so hazards alone have
        // nowhere to sit and the section is not written at all.
        XDocument silent = ComicInfoProjection.Metadata(parsed, options with { AccessModes = [], AccessibilityHazards = ["sound"] }, [], notes).Metadata;

        Assert.Null(silent.Root!.Element(m + "Accessibility"));
    }

    [Fact]
    public void NamesTheFieldsItHasNowhereToPut()
    {
        var notes = new List<string>();
        ComicInfo parsed = ComicInfo.Parse("<ComicInfo><Title>T</Title><StoryArc>A</StoryArc><PageCount>9</PageCount><Characters>B</Characters></ComicInfo>"u8.ToArray(), notes);

        ComicInfoProjection.Metadata(parsed, new ConversionOptions(Language: "fr", Direction: ReadingDirection.LeftToRight), [], notes);

        Assert.Equal("ComicInfo fields with no KOMA equivalent, kept only in the ComicInfo passthrough: Characters, StoryArc", notes[0]);
        Assert.StartsWith("ComicInfo fields recomputed from the package rather than trusted: PageCount.", notes[1], StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoresAComicInfoThatIsNotWellFormedAndSaysSo()
    {
        var notes = new List<string>();

        string[] expected = ["ComicInfo.xml is not well-formed and was ignored"];

        Assert.Same(ComicInfo.Empty, ComicInfo.Parse("<ComicInfo><Title>"u8.ToArray(), notes));
        Assert.Equal(expected, notes);
    }

    [Fact]
    public void DerivesVersionFiveUuidsAsRfc9562Does()
    {
        // The reference value is Python's uuid.uuid5, which the converter
        // uses: one mismatched byte order and every identifier would differ.
        var url = new Guid("6ba7b811-9dad-11d1-80b4-00c04fd430c8");

        Assert.Equal(new Guid("527dda32-a0de-5105-a042-cb475b5f7d11"), ComicInfoProjection.NameBasedUuid(url, "https://example.org/"));
    }

    private static (string Metadata, ReadingDirection Direction, List<string> Notes) Project(string cbz)
    {
        using ZipArchive archive = ZipFile.OpenRead(Corpus.Example(cbz));
        CbzContents contents = CbzReader.Read(archive);
        var notes = new List<string>();

        // Every page of these fixtures passes through untouched, so its
        // digest is the digest of the bytes the archive holds.
        string[] digests = [.. contents.Pages.Select(page => Convert.ToHexStringLower(SHA256.HashData(Read(archive, page.Entry))))];
        var options = new ConversionOptions(Modified: ComicInfoProjection.ModifiedDate(archive));

        (var metadata, ReadingDirection direction) = ComicInfoProjection.Metadata(ComicInfo.Parse(contents.ComicInfo, notes), options, digests, notes);

        return (Encoding.UTF8.GetString(CanonicalXml.Write(metadata)), direction, notes);
    }

    private static byte[] Read(ZipArchive archive, string entry)
    {
        using Stream stream = archive.GetEntry(entry)!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        return buffer.ToArray();
    }
}
