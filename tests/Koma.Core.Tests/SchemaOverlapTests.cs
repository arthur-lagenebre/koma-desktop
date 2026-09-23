using System.IO.Compression;
using System.Xml.Linq;
using Koma.Core.Model;
using Koma.Core.Packaging;
using Koma.Core.Schemas;
using Koma.Core.Versioning;
using Koma.TestSupport;

namespace Koma.Core.Tests;

/// <summary>
/// What the readers refuse by hand, against what the schemas refuse.
/// </summary>
/// <remarks>
/// <para>
/// The readers check what the schemas already check, on purpose: the schema
/// is skipped in forward-compatible mode (§5.0), where a document of a later
/// minor version may hold what the 0.9 schemas do not know; a reader is
/// public API, and nothing obliges its caller to validate first; and a
/// message naming the attribute and its section is worth more to whoever has
/// to fix the file than a path into the document.
/// </para>
/// <para>
/// These tests keep that redundancy honest. The first says the schema catches
/// what the readers catch, so the duplication is deliberate and not a
/// misunderstanding; the second says what only the readers catch, which is
/// what would be lost by trusting the schema alone.
/// </para>
/// </remarks>
public sealed class SchemaOverlapTests
{
    [Theory]
    [InlineData(CorePaths.Manifest, "Item", "media-type", "image/gif")]
    [InlineData(CorePaths.Manifest, "Item", "page-span", "3")]
    [InlineData(CorePaths.Manifest, "Item", "background-color", "#FFF")]
    [InlineData(CorePaths.Manifest, "Item", "width", "01200")]
    [InlineData(CorePaths.Manifest, "Item", "roles", "Story")]
    [InlineData(CorePaths.Manifest, "ItemRef", "spread-position", "middle")]
    [InlineData(CorePaths.Manifest, "Manifest", "version", "0.9.1")]
    [InlineData(CorePaths.Manifest, "Resources", null, null)]
    [InlineData(CorePaths.Metadata, "Reading", "direction", "ttb")]
    [InlineData(CorePaths.Metadata, "Reading", "spread", "dual")]
    [InlineData(CorePaths.Metadata, "Title", "type", "")]
    [InlineData(CorePaths.Metadata, "Reading", null, null)]
    [InlineData(CorePaths.Navigation, "PageTarget", "spread-position", "center")]
    [InlineData(CorePaths.Navigation, "Label", null, null)]
    public void TheSchemaCatchesWhatTheReadersCatch(string entry, string element, string? attribute, string? value)
    {
        XDocument document = Mutated(entry, element, attribute, value);

        Assert.NotNull(KomaSchemas.Check(document, entry));
        Assert.Contains(Read(document, entry, KomaVersion.Supported), v => v.Code == Code(entry));
    }

    [Theory]
    [InlineData(CorePaths.Manifest, "duplicate item id")]
    [InlineData(CorePaths.Manifest, "href outside pages/")]
    [InlineData(CorePaths.Manifest, "version disagreeing with the container")]
    [InlineData(CorePaths.Metadata, "two main titles")]
    public void OnlyTheReadersCatchWhatNoSchemaExpresses(string entry, string defect)
    {
        // Counting and agreeing across documents are beyond RELAX NG: an id
        // is declared NCName and not ID, Path says nothing of pages/, and no
        // schema sees the version the container declared.
        XDocument document = BeyondTheSchema(entry, defect);

        // The container of the third case declares 1.0, which the manifest,
        // saying 0.9, disagrees with.
        KomaVersion declared = defect == "version disagreeing with the container" ? new KomaVersion(1, 0) : KomaVersion.Supported;

        Assert.Null(KomaSchemas.Check(document, entry));
        Assert.Contains(Read(document, entry, declared), v => v.Code == Code(entry));
    }

    private static XDocument Mutated(string entry, string element, string? attribute, string? value)
    {
        XDocument document = Document(entry);
        XElement found = First(document, element);

        if (attribute is null)
            found.Remove();
        else
            found.SetAttributeValue(attribute, value);

        return document;
    }

    private static XDocument BeyondTheSchema(string entry, string defect)
    {
        XDocument document = Document(entry);

        switch (defect)
        {
            case "duplicate item id":
                XElement[] items = [.. document.Root!.Descendants().Where(e => e.Name.LocalName == "Item")];
                items[1].SetAttributeValue("id", items[0].Attribute("id")!.Value);
                break;
            case "href outside pages/":
                First(document, "Item").SetAttributeValue("href", "extras/001.jpg");
                break;
            case "version disagreeing with the container":
                // Nothing to change: the document says 0.9, and the container
                // this is read against says 1.0.
                break;
            default:
                XElement titles = First(document, "Titles");
                titles.Add(new XElement(titles.Name.Namespace + "Title", new XAttribute("type", "main"), "Un second titre principal"));
                break;
        }

        return document;
    }

    private static List<ContainerViolation> Read(XDocument document, string entry, KomaVersion version)
    {
        var violations = new List<ContainerViolation>();

        if (entry == CorePaths.Manifest)
            ManifestReader.Read(document, entry, version, violations);
        else if (entry == CorePaths.Metadata)
            MetadataReader.Read(document, entry, version, violations);
        else
            NavigationReader.Read(document, entry, version, "fr", violations);

        return violations;
    }

    private static XElement First(XDocument document, string element) => document.Root!.DescendantsAndSelf().First(e => e.Name.LocalName == element);

    private static string Code(string entry) => entry switch
    {
        CorePaths.Manifest => ContainerViolationCode.SchemaInvalidManifest,
        CorePaths.Metadata => ContainerViolationCode.SchemaInvalidMetadata,
        _ => ContainerViolationCode.SchemaInvalidNavigation
    };

    /// <summary>One core document of a package the corpus calls valid.</summary>
    private static XDocument Document(string entry)
    {
        using ZipArchive archive = ZipFile.OpenRead(Corpus.Package("valid-page-list.koma"));
        using Stream stream = archive.GetEntry(entry)!.Open();

        return XDocument.Load(stream);
    }
}
