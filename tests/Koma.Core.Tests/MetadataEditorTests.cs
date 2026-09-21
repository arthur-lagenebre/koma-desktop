using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Koma.Core.Model;
using Koma.Core.Packaging;
using Koma.Core.Rendering;
using Koma.Core.Writing;
using Koma.TestSupport;

namespace Koma.Core.Tests;

/// <summary>
/// Editing the metadata of a publication: what changes, what must not, and
/// the file on disk either edited whole or left alone.
/// </summary>
public sealed class MetadataEditorTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 8, 30, 0, TimeSpan.Zero);

    private readonly string folder = Directory.CreateTempSubdirectory("koma-edit").FullName;

    [Fact]
    public void ChangesWhatItIsAskedTo()
    {
        XDocument edited = MetadataEditor.Apply(Minimal(), new MetadataEdit("Le rivage", "en", ReadingDirection.LeftToRight, new SeriesEdit("Rivage", "3", "12")), Now);
        PublicationMetadata metadata = Read(edited);

        Assert.Equal("Le rivage", metadata.MainTitle.Text);
        Assert.Equal("en", metadata.ContentLanguage);
        Assert.Equal(ReadingDirection.LeftToRight, metadata.Direction);

        XElement collection = edited.Root!.Element(M("Collections"))!.Element(M("Collection"))!;
        Assert.Equal("Rivage", collection.Element(M("Name"))!.Value);
        string[] attributes = ["type", "position", "total"];

        Assert.Equal(attributes, collection.Attributes().Select(a => a.Name.LocalName));
    }

    [Fact]
    public void ReadsBackTheFieldsAFormStartsFrom()
    {
        XDocument edited = MetadataEditor.Apply(Minimal(), new MetadataEdit(Series: new SeriesEdit("Rivage", "HS2")), Now);

        Assert.Equal(new MetadataEdit("Corpus de conformite", "fr", ReadingDirection.RightToLeft, null), MetadataEditor.Read(Minimal()));
        Assert.Equal(new SeriesEdit("Rivage", "HS2", null), MetadataEditor.Read(edited).Series);
    }

    [Fact]
    public void PutsANewSectionWhereTheSpecificationPlacesIt()
    {
        // valid-minimal has no Collections and no Publication: they go after
        // Languages, and before Reading, as §7 orders the children.
        XDocument edited = MetadataEditor.Apply(Minimal(), new MetadataEdit(Series: new SeriesEdit("Rivage")), Now);

        string[] order = ["Identifiers", "Titles", "Languages", "Collections", "Publication", "Reading", "Content", "Accessibility"];

        Assert.Equal(order, edited.Root!.Elements().Select(e => e.Name.LocalName));
    }

    [Fact]
    public void LeavesEverythingElseAsItWas()
    {
        XDocument original = Minimal();
        XDocument edited = MetadataEditor.Apply(original, new MetadataEdit(Title: "Le rivage"), Now);

        Assert.True(XNode.DeepEquals(original.Root!.Element(M("Accessibility")), edited.Root!.Element(M("Accessibility"))));
        Assert.True(XNode.DeepEquals(original.Root.Element(M("Identifiers")), edited.Root.Element(M("Identifiers"))));
        Assert.Equal("Corpus de conformite", Read(original).MainTitle.Text);
    }

    [Fact]
    public void StampsTheModifiedDateToTheSecond()
    {
        // §7.2.1: a producer that changes a core document updates the date. A
        // date alone would give two edits on one day one release identity.
        XDocument once = MetadataEditor.Apply(Minimal(), new MetadataEdit(Title: "Un"), Now);
        XDocument twice = MetadataEditor.Apply(once, new MetadataEdit(Title: "Deux"), Now.AddMinutes(5));

        XElement[] dates = [.. twice.Root!.Element(M("Publication"))!.Elements(M("Date"))];

        Assert.Equal("2026-09-21T08:35:00Z", Assert.Single(dates).Value);
    }

    [Fact]
    public void MovesTheDocumentLanguageOnlyWhenItSaidTheSame()
    {
        // valid-minimal says fr twice. A document whose own language differs
        // from its content language was set that way on purpose.
        XDocument same = MetadataEditor.Apply(Minimal(), new MetadataEdit(Language: "en"), Now);
        XDocument apart = Minimal();
        apart.Root!.SetAttributeValue(XNamespace.Xml + "lang", "de");
        XDocument edited = MetadataEditor.Apply(apart, new MetadataEdit(Language: "en"), Now);

        Assert.Equal("en", same.Root!.Attribute(XNamespace.Xml + "lang")!.Value);
        Assert.Equal("de", edited.Root!.Attribute(XNamespace.Xml + "lang")!.Value);
    }

    [Theory]
    [InlineData("   ", null)]
    [InlineData(null, "français")]
    [InlineData(null, "en_GB")]
    public void RefusesAValueTheSpecificationDoesNotAllow(string? title, string? language)
    {
        Assert.Throws<ArgumentException>(() => MetadataEditor.Apply(Minimal(), new MetadataEdit(title, language), Now));
    }

    [Fact]
    public void RewritesThePackageWithOnlyItsMetadataChanged()
    {
        string path = Copy("valid-minimal.koma");
        Dictionary<string, byte[]> before = Entries(path);

        PublicationEditor.EditMetadata(path, new MetadataEdit(Title: "Le rivage"), Now);

        Dictionary<string, byte[]> after = Entries(path);
        PackageOpenResult result = PackageOpener.Open(File.OpenRead(path));

        using KomaPackage? package = result.Package;

        Assert.Equal(PackageOpenOutcome.Opened, result.Outcome);
        Assert.Equal("Le rivage", package!.Metadata.MainTitle.Text);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        Assert.All(before.Where(e => e.Key != CorePaths.Metadata), e => Assert.Equal(e.Value, after[e.Key]));
        Assert.False(File.Exists(path + ".writing"));
    }

    [Fact]
    public void LeavesTheFileAloneWhenTheEditIsRefused()
    {
        string path = Copy("valid-minimal.koma");
        byte[] before = File.ReadAllBytes(path);

        Assert.Throws<ArgumentException>(() => PublicationEditor.EditMetadata(path, new MetadataEdit(Title: " "), Now));

        Assert.Equal(before, File.ReadAllBytes(path));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temporary folder left behind is the operating system's to
            // clean up.
        }
    }

    private static XDocument Minimal()
    {
        using ZipArchive archive = ZipFile.OpenRead(Corpus.Package("valid-minimal.koma"));
        using Stream stream = archive.GetEntry(CorePaths.Metadata)!.Open();

        return XDocument.Load(stream);
    }

    private static PublicationMetadata Read(XDocument document)
    {
        var violations = new List<ContainerViolation>();
        PublicationMetadata? metadata = MetadataReader.Read(XDocument.Parse(Encoding.UTF8.GetString(CanonicalXml.Write(document))), CorePaths.Metadata, KomaVersion.Supported, violations);

        Assert.NotNull(metadata);

        return metadata;
    }

    private string Copy(string package)
    {
        string path = Path.Combine(folder, package);
        File.Copy(Corpus.Package(package), path);

        return path;
    }

    private static Dictionary<string, byte[]> Entries(string path)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            using Stream stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            entries[entry.FullName] = buffer.ToArray();
        }

        return entries;
    }

    private static XName M(string name) => XName.Get(name, "urn:koma:metadata");
}
