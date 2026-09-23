using System.IO.Compression;
using System.Xml.Linq;
using Koma.Core.Model;
using Koma.Core.Packaging;
using Koma.Core.Rendering;
using Koma.Core.Writing;
using Koma.TestSupport;

namespace Koma.Core.Tests;

/// <summary>
/// Editing what the manifest says about a page, and the landmarks that follow
/// from it.
/// </summary>
public sealed class PageEditorTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 9, 15, 0, TimeSpan.Zero);

    private readonly string folder = Directory.CreateTempSubdirectory("koma-pages").FullName;

    [Fact]
    public void MovesTheCoverAndDemotesTheOldOne()
    {
        // §8.4 allows exactly one front cover: naming a new one is an intent
        // plain enough to act on rather than refuse.
        (XDocument manifest, XDocument? navigation) = Apply("p003", new PageEdit(Roles: ["front-cover"]));

        Assert.Equal("inner-cover", Roles(manifest, "p001"));
        Assert.Equal("front-cover", Roles(manifest, "p003"));

        // The landmarks say what the roles say, or the package would say two
        // different things.
        string[] landmarks = ["front-cover p003", "inner-cover p001", "body-start p002"];
        Assert.Equal(landmarks, Landmarks(navigation!));
    }

    [Fact]
    public void WritesADoublePageAndTakesItBack()
    {
        (XDocument spread, _) = Apply("p002", new PageEdit(PageSpan: 2));
        (XDocument single, _) = Apply("p004", new PageEdit(PageSpan: 1));

        // §8.5 gives one as the default, so a single page says nothing.
        Assert.Equal("2", (string?)Item(spread, "p002").Attribute("page-span"));
        Assert.Null(Item(single, "p004").Attribute("page-span"));
    }

    [Fact]
    public void PinsAPageToASideOfItsSpread()
    {
        (XDocument manifest, _) = Apply("p002", new PageEdit(SpreadPosition: SpreadPosition.Left));

        Assert.Equal("left", (string?)Reference(manifest, "p002").Attribute("spread-position"));
        Assert.Equal(SpreadPosition.Left, PageEditor.Read(manifest, "p002").SpreadPosition);
    }

    [Fact]
    public void RefusesToPinAPageThatFillsItsSpread()
    {
        // p004 spans two pages; §8.8 leaves it no side to sit on.
        Assert.Throws<ArgumentException>(() => Apply("p004", new PageEdit(SpreadPosition: SpreadPosition.Right)));
    }

    [Fact]
    public void DescribesAPageForAReaderWhoCannotSeeIt()
    {
        (XDocument described, _) = Apply("p002", new PageEdit(AlternativeText: "Alix quitte le port."));
        (XDocument cleared, _) = Apply("p001", new PageEdit(AlternativeText: ""));

        Assert.Equal("Alix quitte le port.", PageEditor.Read(described, "p002").AlternativeText);
        Assert.Null(Item(cleared, "p001").Element(XName.Get("Accessibility", "urn:koma:manifest")));
    }

    [Fact]
    public void MarksAPageDecorativeAndRefusesToDescribeItToo()
    {
        (XDocument manifest, _) = Apply("p002", new PageEdit(Decorative: true));

        Assert.True(PageEditor.Read(manifest, "p002").Decorative);

        // §8.7: a page that carries no information carries no description of
        // it either, which G7 of the schema also refuses.
        Assert.Throws<ArgumentException>(() => Apply("p001", new PageEdit(Decorative: true)));
    }

    [Theory]
    [InlineData("Story")]
    [InlineData("")]
    public void RefusesRolesTheSpecificationDoesNotAllow(string role)
    {
        // A token is lowercase (§4.3), and a page carries at least one role.
        Assert.Throws<ArgumentException>(() => Apply("p002", new PageEdit(Roles: role.Length == 0 ? [] : [role])));
    }

    [Fact]
    public void ReadsBackWhatAFormStartsFrom()
    {
        PageEdit current = PageEditor.Read(Manifest(), "p001");

        string[] roles = ["front-cover"];

        Assert.Equal(roles, current.Roles);
        Assert.Equal((1, SpreadPosition.Auto, "Couverture.", false), (current.PageSpan, current.SpreadPosition, current.AlternativeText, current.Decorative));
    }

    [Fact]
    public void RewritesThePackageAndStampsTheRelease()
    {
        string path = Copy("valid-page-list.koma");

        PublicationEditor.EditPage(path, "p003", new PageEdit(Roles: ["advertisement"]), Now);

        PackageOpenResult result = PackageOpener.Open(File.OpenRead(path));

        using KomaPackage? package = result.Package;

        Assert.Equal(PackageOpenOutcome.Opened, result.Outcome);
        string[] roles = ["advertisement"];

        Assert.Equal(roles, package!.Manifest.Item("p003")!.Roles);

        // §7.2.1: a core document changed is a new release, and the date says so.
        Assert.Contains("2026-09-22T09:15:00Z", Text(path, CorePaths.Metadata), StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesThePackageAloneWhenTheEditIsRefused()
    {
        string path = Copy("valid-page-list.koma");
        byte[] before = File.ReadAllBytes(path);

        Assert.Throws<ArgumentException>(() => PublicationEditor.EditPage(path, "p001", new PageEdit(Roles: ["Front-Cover"]), Now));

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
            // The operating system's to clean up.
        }
    }

    private static (XDocument Manifest, XDocument? Navigation) Apply(string item, PageEdit edit) => PageEditor.Apply(Manifest(), Navigation(), item, edit);

    private static XDocument Manifest() => Document(CorePaths.Manifest);

    private static XDocument Navigation() => Document(CorePaths.Navigation);

    private static XDocument Document(string entry)
    {
        using ZipArchive archive = ZipFile.OpenRead(Corpus.Package("valid-page-list.koma"));
        using Stream stream = archive.GetEntry(entry)!.Open();

        return XDocument.Load(stream);
    }

    private static XElement Item(XDocument manifest, string item) => manifest.Root!.Descendants(XName.Get("Item", "urn:koma:manifest")).First(i => (string?)i.Attribute("id") == item);

    private static XElement Reference(XDocument manifest, string item) => manifest.Root!.Descendants(XName.Get("ItemRef", "urn:koma:manifest")).First(r => (string?)r.Attribute("item") == item);

    private static string Roles(XDocument manifest, string item) => (string?)Item(manifest, item).Attribute("roles") ?? string.Empty;

    private static string[] Landmarks(XDocument navigation) => [.. navigation.Root!.Descendants(XName.Get("Landmark", "urn:koma:navigation")).Select(l => $"{(string?)l.Attribute("type")} {(string?)l.Attribute("item")}")];

    private static string Text(string path, string entry)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        using var reader = new StreamReader(archive.GetEntry(entry)!.Open());

        return reader.ReadToEnd();
    }

    private string Copy(string package)
    {
        string path = Path.Combine(folder, package);
        File.Copy(Corpus.Package(package), path);

        return path;
    }
}
