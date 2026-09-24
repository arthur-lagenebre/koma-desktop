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
        Assert.Equal(SpreadPosition.Left, PageEditor.Read(manifest, null, "p002").SpreadPosition);
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

        Assert.Equal("Alix quitte le port.", PageEditor.Read(described, null, "p002").AlternativeText);
        Assert.Null(Item(cleared, "p001").Element(XName.Get("Accessibility", "urn:koma:manifest")));
    }

    [Fact]
    public void MarksAPageDecorativeAndRefusesToDescribeItToo()
    {
        (XDocument manifest, _) = Apply("p002", new PageEdit(Decorative: true));

        Assert.True(PageEditor.Read(manifest, null, "p002").Decorative);

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
        PageEdit current = PageEditor.Read(Manifest(), Navigation(), "p001");

        string[] roles = ["front-cover"];

        Assert.Equal(roles, current.Roles);
        Assert.Equal((1, SpreadPosition.Auto, "Couverture.", false, string.Empty), (current.PageSpan, current.SpreadPosition, current.AlternativeText, current.Decorative, current.Chapter));
    }

    [Fact]
    public void OpensAChapterAtAPageAndClosesItAgain()
    {
        (XDocument manifest, XDocument? opened) = Apply("p003", new PageEdit(Chapter: "Chapitre 2"));

        // Entries follow the spine: a table of contents running in another
        // order than the pages would send a reader backwards.
        string[] chapters = ["p002 Chapitre 1", "p003 Chapitre 2"];
        Assert.Equal(chapters, Entries(opened!));

        // The language of the navigation document is the label's (§4.4).
        Assert.Equal("fr", (string?)opened!.Root!.Descendants(XName.Get("Label", "urn:koma:navigation")).Last().Attribute(XNamespace.Xml + "lang"));
        Assert.Equal("Chapitre 2", PageEditor.Read(manifest, opened, "p003").Chapter);

        (_, XDocument? closed) = PageEditor.Apply(Manifest(), Navigation(), "p002", new PageEdit(Chapter: ""));

        // Nothing left in the section, so the section goes.
        Assert.Empty(Entries(closed!));
        Assert.Null(closed!.Root!.Element(XName.Get("TableOfContents", "urn:koma:navigation")));
    }

    [Fact]
    public void WritesTheNumbersPrintedOnAPage()
    {
        // valid-page-list numbers p002 as 1; p004 spans a spread and carries
        // the two numbers printed on it.
        (_, XDocument? renumbered) = Apply("p002", new PageEdit(PrintedPages: ["iv"]));
        (_, XDocument? spread) = Apply("p004", new PageEdit(PrintedPages: ["6", "7"]));

        Assert.Contains("p002 iv", Labels(renumbered!));
        Assert.Contains("p004 6 left", Labels(spread!));
        Assert.Contains("p004 7 right", Labels(spread!));

        // Only a page drawn across a spread carries two numbers (§9.2).
        Assert.Throws<ArgumentException>(() => Apply("p002", new PageEdit(PrintedPages: ["6", "7"])));
        Assert.Throws<ArgumentException>(() => Apply("p002", new PageEdit(PrintedPages: ["1", "2", "3"])));
    }

    [Fact]
    public void TakesAPageOutOfTheListAndTheListWithTheLastOne()
    {
        (_, XDocument? navigation) = Apply("p002", new PageEdit(PrintedPages: []));

        string[] renumbered = ["iv"];

        Assert.DoesNotContain("p002", Labels(navigation!).Select(l => l.Split(' ')[0]));
        Assert.Equal(renumbered, PageEditor.Read(Manifest(), Apply("p002", new PageEdit(PrintedPages: renumbered)).Navigation, "p002").PrintedPages);

        // A list with nothing left in it goes, rather than stay empty (§9.2).
        XDocument? emptied = Navigation();

        foreach (string page in new[] { "p001", "p002", "p003", "p004" })
            (_, emptied) = PageEditor.Apply(Manifest(), emptied, page, new PageEdit(PrintedPages: []));

        Assert.Null(emptied!.Root!.Element(XName.Get("PageList", "urn:koma:navigation")));
    }

    [Fact]
    public void TakesAPageOutOfEverythingThatNamedIt()
    {
        (XDocument manifest, XDocument? navigation, string href) = PageEditor.Remove(Manifest(), Navigation(), "p003");

        Assert.Equal("pages/003.png", href);
        Assert.DoesNotContain("p003", Spine(manifest));
        Assert.DoesNotContain(manifest.Root!.Descendants(XName.Get("Item", "urn:koma:manifest")), i => (string?)i.Attribute("id") == "p003");

        // A chapter opening on a page that is gone opens on nothing, and a
        // printed number labels no page.
        Assert.DoesNotContain("p003", Entries(navigation!).Select(e => e.Split(' ')[0]));
        Assert.DoesNotContain("p003", Labels(navigation!).Select(l => l.Split(' ')[0]));
    }

    [Fact]
    public void RefusesToTakeOutTheCover()
    {
        // §8.4 wants exactly one front cover, and giving the cover to another
        // page first is the editor's decision, not this method's.
        Assert.Throws<ArgumentException>(() => PageEditor.Remove(Manifest(), Navigation(), "p001"));

        XDocument manifest = Manifest();
        XDocument? navigation = Navigation();

        foreach (string page in new[] { "p002", "p003", "p004" })
            (manifest, navigation, _) = PageEditor.Remove(manifest, navigation, page);

        // Down to the cover alone, which is still the cover.
        Assert.Single(Spine(manifest));
        Assert.Throws<ArgumentException>(() => PageEditor.Remove(manifest, navigation, "p001"));
    }

    [Fact]
    public void MovesAPageAndTakesTheNavigationWithIt()
    {
        (XDocument manifest, XDocument? navigation) = PageEditor.Move(Manifest(), Navigation(), "p004", 1);

        string[] order = ["p001", "p004", "p002", "p003"];
        Assert.Equal(order, Spine(manifest));

        // A table of contents running in another order than the pages would
        // send a reader backwards, so it follows.
        string[] listed = ["p001", "p004", "p002", "p003"];
        Assert.Equal(listed, Labels(navigation!).Select(l => l.Split(' ')[0]).Distinct());

        Assert.Throws<ArgumentException>(() => PageEditor.Move(Manifest(), Navigation(), "p004", 9));
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
    public void RewritesThePackageWithoutThePageAndWithoutItsFile()
    {
        string path = Copy("valid-page-list.koma");

        PublicationEditor.RemovePage(path, "p003", Now);

        PackageOpenResult result = PackageOpener.Open(File.OpenRead(path));

        using KomaPackage? package = result.Package;

        Assert.Equal(PackageOpenOutcome.Opened, result.Outcome);
        Assert.Null(package!.Manifest.Item("p003"));

        using ZipArchive archive = ZipFile.OpenRead(path);

        // The file goes with its declaration: a page nothing declares is a
        // fault of its own (§8).
        Assert.Null(archive.GetEntry("pages/003.png"));
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

    private static string[] Spine(XDocument manifest) => [.. manifest.Root!.Descendants(XName.Get("ItemRef", "urn:koma:manifest")).Select(r => (string?)r.Attribute("item") ?? string.Empty)];

    private static string[] Labels(XDocument navigation) => [.. navigation.Root!.Descendants(XName.Get("PageTarget", "urn:koma:navigation")).Select(t => string.Join(' ', new[] { (string?)t.Attribute("item"), (string?)t.Attribute("label"), (string?)t.Attribute("spread-position") }.Where(v => v is not null)))];

    private static string[] Entries(XDocument navigation) => [.. navigation.Root!.Descendants(XName.Get("Entry", "urn:koma:navigation")).Select(e => $"{(string?)e.Attribute("item")} {e.Element(XName.Get("Label", "urn:koma:navigation"))?.Value}")];

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
