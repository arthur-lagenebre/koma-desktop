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

        MetadataEdit current = MetadataEditor.Read(Minimal());

        Assert.Equal(("Corpus de conformite", "fr", ReadingDirection.RightToLeft, (SeriesEdit?)null), (current.Title, current.Language, current.Direction, current.Series));
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
    public void SaysHowAPublicationIsReadAndWhatItMayDo()
    {
        XDocument edited = MetadataEditor.Apply(Minimal(), new MetadataEdit(Accessibility: new AccessibilityEdit(["visual", "textual"], ["no-flashing-hazard"], "Pages decrites.")), Now);
        XElement section = edited.Root!.Element(M("Accessibility"))!;

        string[] written = ["AccessMode", "AccessMode", "AccessibilityHazard", "AccessibilitySummary"];
        Assert.Equal(written, section.Elements().Select(e => e.Name.LocalName));

        // The summary is written in the language of the document (§4.4).
        Assert.Equal("fr", (string?)section.Element(M("AccessibilitySummary"))!.Attribute(XNamespace.Xml + "lang"));

        AccessibilityEdit read = MetadataEditor.Read(edited).Accessibility!;
        string[] modes = ["visual", "textual"];
        Assert.Equal(modes, read.AccessModes);
        Assert.Equal("Pages decrites.", read.Summary);
    }

    [Fact]
    public void KeepsWhatTheAccessibilitySectionSaysBesides()
    {
        // Conformance and certification are §7.13's too, and an editor that
        // rebuilt the section whole would drop them.
        XDocument original = Minimal();
        original.Root!.Element(M("Accessibility"))!.Add(new XElement(M("ConformsTo"), new XAttribute("identifier", "EPUB Accessibility 1.1 WCAG 2.2 AA")));

        XDocument edited = MetadataEditor.Apply(original, new MetadataEdit(Accessibility: new AccessibilityEdit(["visual"], [], "")), Now);

        Assert.Single(edited.Root!.Element(M("Accessibility"))!.Elements(M("ConformsTo")));
    }

    [Fact]
    public void TakesTheAccessibilitySectionAwayWhenThereIsNothingToSay()
    {
        XDocument edited = MetadataEditor.Apply(Minimal(), new MetadataEdit(Accessibility: new AccessibilityEdit([], [], "")), Now);

        Assert.Null(edited.Root!.Element(M("Accessibility")));

        // A section that says something says at least how it is read.
        Assert.Throws<ArgumentException>(() => MetadataEditor.Apply(Minimal(), new MetadataEdit(Accessibility: new AccessibilityEdit([], ["no-sound-hazard"], "")), Now));
    }

    [Fact]
    public void WritesWhoThePublicationIsTheWorkOfAndWhatItIsAbout()
    {
        var edit = new MetadataEdit(
            Contributors: [new ContributorEdit("Froideval", ["writer"]), new ContributorEdit("Glenat", ["editor"], Organization: true)],
            Subjects: [new SubjectEdit("genre", "Fantastique"), new SubjectEdit("keyword", "demons")],
            Publisher: "Glenat");

        XDocument edited = MetadataEditor.Apply(Minimal(), edit, Now);
        XElement root = edited.Root!;

        string[] named = ["Froideval", "Glenat"];
        Assert.Equal(named, root.Element(M("Contributors"))!.Elements(M("Contributor")).Select(c => c.Element(M("Name"))!.Value));
        Assert.Equal("organization", (string?)root.Element(M("Contributors"))!.Elements(M("Contributor")).Last().Attribute("type"));
        Assert.Equal("Glenat", root.Element(M("Publication"))!.Element(M("Publisher"))!.Value);

        // §7.9 reads a subject with no type as a keyword, so a keyword says
        // nothing and the others say what they are.
        XElement[] subjects = [.. root.Element(M("Subjects"))!.Elements(M("Subject"))];
        Assert.Equal("genre", (string?)subjects[0].Attribute("type"));
        Assert.Null(subjects[1].Attribute("type"));

        MetadataEdit read = MetadataEditor.Read(edited);

        Assert.Equal("Glenat", read.Publisher);
        Assert.Equal(2, read.Contributors!.Count);
        Assert.Equal("Fantastique", read.Subjects![0].Text);
    }

    [Fact]
    public void WritesWhatThePublicationSaysOfItselfAndWhereItComesFrom()
    {
        var edit = new MetadataEdit(
            Descriptions: [new DescriptionEdit("summary", "Nemo refait surface.")],
            Publisher: "Soleil",
            Imprint: "1800",
            Place: "Toulon");

        XDocument edited = MetadataEditor.Apply(Minimal(), edit, Now);
        XElement publication = edited.Root!.Element(M("Publication"))!;

        // §7.8 fixes the order of what it holds, and a section out of order
        // is a section the schema refuses. The modified date of §7.2.1 lands
        // in the same section, after the three.
        string[] order = ["Publisher", "Imprint", "Place", "Date"];
        Assert.Equal(order, publication.Elements().Select(e => e.Name.LocalName));

        Assert.Equal("summary", (string?)edited.Root!.Element(M("Descriptions"))!.Element(M("Description"))!.Attribute("type"));
        Assert.Equal("Nemo refait surface.", MetadataEditor.Read(edited).Descriptions![0].Text);

        // §7.7 wants a type on every description.
        Assert.Throws<ArgumentException>(() => MetadataEditor.Apply(Minimal(), new MetadataEdit(Descriptions: [new DescriptionEdit(string.Empty, "Sans type")]), Now));
    }

    [Fact]
    public void WritesTheStoryTheWarningsTheLinksAndTheRights()
    {
        var edit = new MetadataEdit(
            Entities: [new EntityEdit("Nemo", "character", "protagonist"), new EntityEdit("Nautilus", "vehicle")],
            Warnings: [new WarningEdit("violence", "Combats")],
            Links: [new LinkEdit("purchase", "https://example.org/nemo", "Chez l'éditeur")],
            Rights: new RightsEdit("© 2026 Soleil", "CC-BY-4.0", "Reproduction interdite."));

        XDocument edited = MetadataEditor.Apply(Minimal(), edit, Now);
        XElement root = edited.Root!;

        Assert.Equal("protagonist", (string?)root.Element(M("Entities"))!.Elements(M("Entity")).First().Attribute("role"));

        // A part in the story is optional (§7.10), so an entity without one
        // says nothing rather than saying nothing useful.
        Assert.Null(root.Element(M("Entities"))!.Elements(M("Entity")).Last().Attribute("role"));

        Assert.Equal("violence", (string?)root.Element(M("Ratings"))!.Element(M("ContentWarning"))!.Attribute("type"));
        Assert.Equal("https://example.org/nemo", (string?)root.Element(M("Links"))!.Element(M("Link"))!.Attribute("href"));

        // §7.16 gives the order: copyright, licence, statement.
        string[] rights = ["Copyright", "License", "Statement"];
        Assert.Equal(rights, root.Element(M("Rights"))!.Elements().Select(e => e.Name.LocalName));

        MetadataEdit read = MetadataEditor.Read(edited);

        Assert.Equal("Nemo", read.Entities![0].Name);
        Assert.Equal("CC-BY-4.0", read.Rights!.License);
    }

    [Fact]
    public void KeepsTheRatingsAConversionWroteWhenTheWarningsChange()
    {
        // §7.14 holds both, and this editor knows only one of them: the other
        // must survive an edit rather than be lost to it.
        XDocument original = Minimal();
        XNamespace m = "urn:koma:metadata";

        original.Root!.Add(new XElement(m + "Ratings", new XElement(m + "Rating", new XAttribute("scheme", "cero"), new XAttribute("value", "B"))));

        XDocument edited = MetadataEditor.Apply(original, new MetadataEdit(Warnings: [new WarningEdit("gore")]), Now);
        XElement section = edited.Root!.Element(M("Ratings"))!;

        // §7.14 puts the ratings before the warnings.
        string[] order = ["Rating", "ContentWarning"];
        Assert.Equal(order, section.Elements().Select(e => e.Name.LocalName));
    }

    [Fact]
    public void EmptyingAListTakesItsSectionAway()
    {
        // An editor that could not clear a list could not undo a bad
        // conversion.
        XDocument written = MetadataEditor.Apply(Minimal(), new MetadataEdit(Subjects: [new SubjectEdit("genre", "Fantastique")], Publisher: "Glenat"), Now);
        XDocument cleared = MetadataEditor.Apply(written, new MetadataEdit(Subjects: [], Publisher: ""), Now);

        Assert.Null(cleared.Root!.Element(M("Subjects")));
        Assert.Null(cleared.Root.Element(M("Publication"))?.Element(M("Publisher")));

        // §7.6 wants a role for whoever is named.
        Assert.Throws<ArgumentException>(() => MetadataEditor.Apply(Minimal(), new MetadataEdit(Contributors: [new ContributorEdit("Anonyme", [])]), Now));
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
