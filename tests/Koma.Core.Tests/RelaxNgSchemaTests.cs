using System.IO.Compression;
using System.Xml.Linq;
using Koma.Core.Schemas;
using Koma.TestSupport;

namespace Koma.Core.Tests;

/// <summary>
/// The RELAX NG validator, held to the schemas' own suite: the reference
/// instances it must accept, the documents it must reject, and every core
/// document of the corpus.
/// </summary>
/// <remarks>
/// The rejected documents are the ones <c>tests/test_schemas.py</c> publishes
/// under <c>schemas/0.9/examples/invalid</c>, which libxml2 rejects upstream:
/// both validators are graded on the same files.
/// </remarks>
public sealed class RelaxNgSchemaTests
{
    private static readonly string Schemas = Path.Combine(Corpus.Root(), "..", "schemas", "0.9");

    private static readonly (string Entry, string Schema)[] Documents =
    [
        ("META-INF/container.xml", "container"),
        ("koma/manifest.xml", "manifest"),
        ("koma/metadata.xml", "metadata"),
        ("koma/nav.xml", "navigation")
    ];

    private static readonly Dictionary<string, RelaxNgSchema> Loaded = new[] { "container", "metadata", "manifest", "navigation" }
        .ToDictionary(name => name, name => RelaxNgSchema.Load(XDocument.Load(Path.Combine(Schemas, $"koma-{name}-0.9.rng"))), StringComparer.Ordinal);

    [Theory]
    [InlineData("container", "container.xml")]
    [InlineData("metadata", "metadata.xml")]
    [InlineData("manifest", "manifest.xml")]
    [InlineData("navigation", "nav.xml")]
    public void AcceptsTheReferenceInstances(string schema, string instance)
    {
        Assert.Null(Loaded[schema].Validate(XDocument.Load(Path.Combine(Schemas, "examples", instance))));
    }

    [Fact]
    public void RejectsEveryDocumentTheSchemasMustReject()
    {
        string[] documents = [.. Directory.EnumerateFiles(Path.Combine(Schemas, "examples", "invalid"), "*.xml", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];
        string[] accepted = [.. documents.Where(path => Loaded[Path.GetFileName(Path.GetDirectoryName(path))!].Validate(XDocument.Load(path)) is null).Select(path => Path.GetFileName(path))];

        Assert.Equal(36, documents.Length);
        Assert.Empty(accepted);
    }

    [Fact]
    public void AgreesWithTheSchemasOnEveryCoreDocumentOfTheCorpus()
    {
        // The corpus exercises layers 1, 3 and 4, so its documents are valid
        // against the schemas, with one exception: G7 puts the rule against
        // AlternativeText on a decorative page in the schema as well.
        var rejected = new List<string>();

        foreach (string package in Directory.EnumerateFiles(Path.Combine(Corpus.Root(), "packages"), "*.koma").Order(StringComparer.Ordinal))
        {
            using ZipArchive archive = ZipFile.OpenRead(package);

            foreach ((string entry, string schema) in Documents)
            {
                if (archive.GetEntry(entry) is not { } found)
                    continue;

                using Stream stream = found.Open();

                if (Loaded[schema].Validate(XDocument.Load(stream)) is not null)
                    rejected.Add($"{Path.GetFileName(package)}:{entry}");
            }
        }

        string[] expected = ["L3-decorative-with-alt-text.koma:koma/manifest.xml"];

        Assert.Equal(expected, rejected);
    }

    [Theory]
    [InlineData("\u00a0", true)]
    [InlineData(" \t\n", false)]
    public void KnowsWhatXmlSchemaCallsWhitespace(string title, bool valid)
    {
        // XML Schema's \s is four characters. .NET's covers every Unicode
        // space, and would call a title of one no-break space empty, which
        // the schema does not.
        XDocument metadata = XDocument.Load(Path.Combine(Schemas, "examples", "metadata.xml"));
        XNamespace m = "urn:koma:metadata";
        metadata.Root!.Element(m + "Titles")!.Element(m + "Title")!.Value = title;

        Assert.Equal(valid, Loaded["metadata"].Validate(metadata) is null);
    }

    [Fact]
    public void SaysWhereADocumentStoppedMatching()
    {
        XDocument rejected = XDocument.Load(Path.Combine(Schemas, "examples", "invalid", "metadata", "reading-direction-ttb.xml"));

        Assert.Equal("/Metadata/Reading", Loaded["metadata"].Validate(rejected));
    }

    [Fact]
    public void RefusesASchemaItCannotValidateFaithfully()
    {
        // A construct it does not know is refused when the schema loads,
        // rather than accepting whatever it would have checked.
        XDocument schema = XDocument.Parse("""
            <grammar xmlns="http://relaxng.org/ns/structure/1.0">
              <start><element><name ns="">a</name><interleave><text/><empty/></interleave></element></start>
            </grammar>
            """);

        Assert.Throws<NotSupportedException>(() => RelaxNgSchema.Load(schema));
    }
}
