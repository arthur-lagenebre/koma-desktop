using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Koma.Core.Writing;
using Koma.TestSupport;

namespace Koma.Core.Tests;

/// <summary>
/// The canonical serialization of §14.1, held to the corpus.
/// </summary>
/// <remarks>
/// Every core document of every corpus package is written in this form
/// upstream, and <c>tools/canonical.py</c> is the reference serializer. This
/// writer agrees with it or it does not: 42 packages give more shapes than
/// any fixture written here would, foreign content and undeclared namespaces
/// included.
/// </remarks>
public sealed class CanonicalXmlTests
{
    [Theory]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Reading xmlns=\"urn:koma:metadata\" direction=\"rtl\"/>\n")]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Titles xmlns=\"urn:koma:metadata\">\n  <Title type=\"main\">Le rivage</Title>\n</Titles>\n")]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Titles xmlns=\"urn:koma:metadata\">\n  <Title type=\"a&quot;b\">&amp;&lt;&gt;</Title>\n</Titles>\n")]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Descriptions xmlns=\"urn:koma:metadata\">\n  <Description type=\"summary\">Un.\n\nDeux.</Description>\n</Descriptions>\n")]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Extensions xmlns=\"urn:koma:metadata\">\n  <shelf xmlns=\"urn:example:shelf\" row=\"3\"/>\n</Extensions>\n")]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Extensions xmlns=\"urn:koma:metadata\">\n  <x xmlns=\"\"/>\n</Extensions>\n")]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Metadata xmlns=\"urn:koma:metadata\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" version=\"0.9\">\n  <Extensions>\n    <dc:rights>Libre</dc:rights>\n  </Extensions>\n</Metadata>\n")]
    public void WritesBackWhatItIsGiven(string document)
    {
        // An empty element, text on its element's line, escaping, text on
        // several lines, foreign content, an element in no namespace, and a
        // prefixed declaration.
        Assert.Equal(document, Rewrite(Encoding.UTF8.GetBytes(document)));
    }

    [Fact]
    public void WritesEveryCoreDocumentOfTheCorpus()
    {
        string[] packages = [.. Directory.EnumerateFiles(Path.Combine(Corpus.Root(), "packages"), "*.koma").Order(StringComparer.Ordinal)];

        Assert.Equal(67, packages.Length);

        foreach (string package in packages.Where(p => !NoProducerWroteThem.Contains(Path.GetFileName(p))))
        {
            foreach (byte[] written in CoreDocuments(package))
                Assert.Equal(Encoding.UTF8.GetString(written), Rewrite(written));
        }
    }

    [Fact]
    public void WritesUtf8WithoutABomAndEndsWithOneNewline()
    {
        byte[] written = CanonicalXml.Write(XDocument.Parse("<Reading xmlns=\"urn:koma:metadata\" direction=\"rtl\"/>"));

        Assert.NotEqual<byte>([0xEF, 0xBB, 0xBF], written[..3]);
        Assert.Equal((byte)'\n', written[^1]);
        Assert.NotEqual((byte)'\n', written[^2]);
        Assert.DoesNotContain((byte)'\r', written);
    }

    /// <summary>
    /// The packages whose documents §14.1 has nothing to say about.
    /// </summary>
    /// <remarks>
    /// A case built to break a rule about documents — one that is not
    /// well-formed, one carrying a document type declaration, one past a
    /// limit of §13.1 — holds a document no conforming producer wrote, and
    /// its layout is not the corpus's claim. The upstream suite exempts the
    /// same three codes.
    /// </remarks>
    private static readonly HashSet<string> NoProducerWroteThem = Exempt();

    private static HashSet<string> Exempt()
    {
        string[] codes = ["xml-not-well-formed", "xml-document-size-limit", "xml-nesting-limit"];

        using JsonDocument expectations = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Corpus.Root(), "expected.json")));

        return [.. expectations.RootElement.GetProperty("cases").EnumerateArray()
            .Where(c => c.TryGetProperty("code", out JsonElement code) && code.ValueKind == JsonValueKind.String && codes.Contains(code.GetString()))
            .Select(c => c.GetProperty("package").GetString()!)];
    }

    /// <summary>
    /// The bytes of every core document a package hands over.
    /// </summary>
    /// <remarks>
    /// A file that is no archive holds none, and a split archive hands over
    /// nothing: .NET reads such a central directory and refuses the entries
    /// behind it, which is the fault the case is about and not a layout to
    /// judge.
    /// </remarks>
    private static List<byte[]> CoreDocuments(string package)
    {
        var documents = new List<byte[]>();

        try
        {
            using ZipArchive archive = ZipFile.OpenRead(package);

            foreach (ZipArchiveEntry entry in archive.Entries.Where(IsCoreDocument))
            {
                using Stream stream = entry.Open();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);

                documents.Add(buffer.ToArray());
            }
        }
        catch (InvalidDataException)
        {
            return [];
        }

        return documents;
    }

    // §14.1 binds the core documents; a ComicInfo.xml carried over from a CBZ
    // is never normative for KOMA (§1) and is copied as it was found.
    private static bool IsCoreDocument(ZipArchiveEntry entry) => entry.FullName == "META-INF/container.xml" || (entry.FullName.StartsWith("koma/", StringComparison.Ordinal) && entry.FullName.EndsWith(".xml", StringComparison.Ordinal));

    private static string Rewrite(byte[] document) => Encoding.UTF8.GetString(CanonicalXml.Write(XDocument.Parse(Encoding.UTF8.GetString(document))));
}
