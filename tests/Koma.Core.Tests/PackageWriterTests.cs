using System.IO.Compression;
using System.Text;
using Koma.Core.Packaging;
using Koma.Core.Writing;

namespace Koma.Core.Tests;

/// <summary>
/// Writing a package: the entry §2.1 fixes, the order §14.2 asks for, and
/// bytes that do not move between two writes of the same publication.
/// </summary>
public sealed class PackageWriterTests
{
    private const string Container = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Container xmlns="urn:koma:container" version="0.9">
          <RootFiles>
            <RootFile full-path="koma/manifest.xml" media-type="application/vnd.koma.manifest+xml"/>
          </RootFiles>
        </Container>
        """;

    private const string Manifest = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Manifest xmlns="urn:koma:manifest" version="0.9" metadata="koma/metadata.xml">
          <Resources>
            <Item id="p001" href="pages/001.png" media-type="image/png" width="1" height="1" roles="front-cover"/>
          </Resources>
          <Spine>
            <ItemRef item="p001"/>
          </Spine>
        </Manifest>
        """;

    private const string Metadata = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Metadata xmlns="urn:koma:metadata" version="0.9">
          <Identifiers>
            <Identifier scheme="uuid" primary="true">urn:uuid:6f9619ff-8b86-d011-b42d-00c04fc964ff</Identifier>
          </Identifiers>
          <Titles>
            <Title type="main">Une page</Title>
          </Titles>
          <Languages>
            <Language role="content">fr</Language>
          </Languages>
          <Reading direction="ltr" spread="auto"/>
        </Metadata>
        """;

    // A 1 by 1 PNG: the writer never looks inside a page, and the resource
    // checks that would are a pass of their own.
    private static readonly byte[] Page = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGO4c+cOAAUsApUYk4oFAAAAAElFTkSuQmCC");

    [Fact]
    public void WritesAPackageTheOpenerAccepts()
    {
        using MemoryStream buffer = Write(Publication());

        PackageOpenResult result = PackageOpener.Open(buffer, leaveOpen: true);

        using KomaPackage? package = result.Package;

        Assert.Equal(PackageOpenOutcome.Opened, result.Outcome);
        Assert.NotNull(package);
        Assert.Equal("Une page", package.Metadata.MainTitle.Text);
    }

    [Fact]
    public void PutsTheMimetypeFirstAndLeavesItStored()
    {
        using MemoryStream buffer = Write(Publication());
        using ZipArchive archive = new(buffer, ZipArchiveMode.Read);

        ZipArchiveEntry first = archive.Entries[0];

        Assert.Equal(KomaMediaType.EntryName, first.FullName);
        Assert.Equal(first.Length, first.CompressedLength);
    }

    [Fact]
    public void OrdersEntriesByTheirUtf8Bytes()
    {
        // é in NFC is C3 A9, above every ASCII letter, and the order does not
        // follow the caller's dictionary or any culture's alphabet.
        Dictionary<string, byte[]> entries = Publication();
        entries["extras/été.txt"] = "été"u8.ToArray();
        entries["extras/Zurich.txt"] = "Zurich"u8.ToArray();
        entries["extras/avant.txt"] = "avant"u8.ToArray();

        using MemoryStream buffer = Write(entries);
        using ZipArchive archive = new(buffer, ZipArchiveMode.Read);

        string[] expected =
        [
            KomaMediaType.EntryName,
            "META-INF/container.xml",
            "extras/Zurich.txt",
            "extras/avant.txt",
            "extras/été.txt",
            "koma/manifest.xml",
            "koma/metadata.xml",
            "pages/001.png"
        ];

        Assert.Equal(expected, archive.Entries.Select(e => e.FullName));
    }

    [Fact]
    public void WritesTheSamePublicationToTheSameBytes()
    {
        // §14.2: one order, one fixed timestamp, nothing added. Two writes a
        // moment apart are the test a clock would fail.
        using MemoryStream first = Write(Publication());
        using MemoryStream second = Write(Publication());

        Assert.Equal(first.ToArray(), second.ToArray());
    }

    [Theory]
    [InlineData("/pages/001.png")]
    [InlineData("pages/../001.png")]
    [InlineData("mimetype")]
    public void RefusesAnEntryNameThePackageCouldNotCarry(string name)
    {
        // An absolute path, a traversal, and the one entry §2.1 reserves.
        Dictionary<string, byte[]> entries = Publication();
        entries[name] = Page;

        Assert.Throws<ArgumentException>(() => Write(entries));
    }

    [Fact]
    public void RefusesTwoNamesThatAreOneEntry()
    {
        Dictionary<string, byte[]> entries = Publication();
        entries["pages/001.PNG"] = Page;

        Assert.Throws<ArgumentException>(() => Write(entries));
    }

    private static Dictionary<string, byte[]> Publication() => new(StringComparer.Ordinal)
    {
        ["META-INF/container.xml"] = Encoding.UTF8.GetBytes(Container),
        ["koma/manifest.xml"] = Encoding.UTF8.GetBytes(Manifest),
        ["koma/metadata.xml"] = Encoding.UTF8.GetBytes(Metadata),
        ["pages/001.png"] = Page
    };

    private static MemoryStream Write(IReadOnlyDictionary<string, byte[]> entries)
    {
        var buffer = new MemoryStream();
        PackageWriter.Write(buffer, entries, leaveOpen: true);
        buffer.Position = 0;

        return buffer;
    }
}
