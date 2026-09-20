using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Koma.Core.Packaging;
using Koma.Core.Versioning;

namespace Koma.Core.Tests;

/// <summary>
/// Opening a package, in the order §2.1, §5.0 and §13.1 require.
/// </summary>
public sealed class PackageOpenerTests
{
    private const string Container = """
        <Container xmlns="urn:koma:container" version="0.9">
          <RootFiles>
            <RootFile full-path="koma/manifest.xml"
                      media-type="application/vnd.koma.manifest+xml"/>
          </RootFiles>
        </Container>
        """;

    private const string Manifest = """
        <Manifest xmlns="urn:koma:manifest" version="0.9" metadata="koma/metadata.xml">
          <Resources>
            <Item id="p001" href="pages/001.jpg" media-type="image/jpeg"
                  width="1600" height="2400" roles="front-cover"/>
          </Resources>
          <Spine>
            <ItemRef item="p001"/>
          </Spine>
        </Manifest>
        """;

    private const string Metadata = """
        <Metadata xmlns="urn:koma:metadata" version="0.9">
          <Reading direction="ltr" spread="auto"/>
        </Metadata>
        """;

    private const string Navigation = """
        <Navigation xmlns="urn:koma:navigation" version="0.9">
          <Landmarks>
            <Landmark type="front-cover" item="p001"/>
          </Landmarks>
        </Navigation>
        """;

    /// <summary>
    /// A package that opens, unless an argument replaces part of it.
    /// </summary>
    private static MemoryStream Build(string container = Container, string? manifest = Manifest, params (string Name, string Content)[] extra)
    {
        var buffer = new MemoryStream();

        using (ZipArchive archive = KomaArchive.Create(buffer, leaveOpen: true))
        {
            Write(archive, "META-INF/container.xml", container);

            if (manifest is not null)
            {
                Write(archive, "koma/manifest.xml", manifest);
                Write(archive, "koma/metadata.xml", Metadata);
            }

            foreach ((string name, string content) in extra)
                Write(archive, name, content);
        }

        buffer.Position = 0;
        return buffer;
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using Stream stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    private static PackageOpenResult Open(MemoryStream buffer, ResourceLimits? limits = null) => PackageOpener.Open(buffer, limits, leaveOpen: true);

    private static string DeclaringNavigation() => Manifest.Replace("metadata=\"koma/metadata.xml\"", "metadata=\"koma/metadata.xml\" navigation=\"koma/nav.xml\"", StringComparison.Ordinal);

    [Fact]
    public void OpensAWellFormedPackage()
    {
        using MemoryStream buffer = Build();

        PackageOpenResult result = Open(buffer);

        Assert.Equal(PackageOpenOutcome.Opened, result.Outcome);
        Assert.DoesNotContain(result.Violations, v => v.Severity == ViolationSeverity.Error);

        using KomaPackage? package = result.Package;
        Assert.NotNull(package);
        Assert.Equal(new KomaVersion(0, 9), package.Version);
        Assert.Equal(ProcessingMode.Strict, package.Mode);
        Assert.False(package.Manifest.DeclaresNavigation);
        Assert.Null(package.Navigation);
    }

    [Fact]
    public void ReportsAnOlderPreReleaseAsUnsupportedRatherThanInvalid()
    {
        // §5.0: a reader supporting 0.9 MUST reject 0.8, and MUST say that the
        // version is unsupported rather than that the publication is invalid.
        using MemoryStream buffer = Build(Container.Replace("0.9", "0.8", StringComparison.Ordinal));

        PackageOpenResult result = Open(buffer);

        Assert.Equal(PackageOpenOutcome.UnsupportedVersion, result.Outcome);
        Assert.Equal(new KomaVersion(0, 8), result.DeclaredVersion);

        // The point of the distinction: nothing is claimed to be wrong with a
        // file this build simply cannot read.
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void ReportsAStableVersionAsUnsupportedToo()
    {
        // The clause of §5.3 is "either version has a major version of 0", so a
        // 1.0 package is out of reach of this build as much as a 0.8 one.
        using MemoryStream buffer = Build(Container.Replace("version=\"0.9\"", "version=\"1.0\"", StringComparison.Ordinal));

        PackageOpenResult result = Open(buffer);

        Assert.Equal(PackageOpenOutcome.UnsupportedVersion, result.Outcome);
        Assert.Equal(new KomaVersion(1, 0), result.DeclaredVersion);
    }

    [Fact]
    public void DoesNotLookForFaultsInAPackageItCannotRead()
    {
        // Unsupported version and a path traversal in the same package. The
        // version is decided first, so the traversal is never reported: judging
        // a file from another era of the format against this one's rules would
        // produce findings that mean nothing.
        using MemoryStream buffer = Build(Container.Replace("0.9", "0.8", StringComparison.Ordinal), Manifest, ("../escape.txt", "x"));

        PackageOpenResult result = Open(buffer);

        Assert.Equal(PackageOpenOutcome.UnsupportedVersion, result.Outcome);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void RejectsAZipThatIsNotAPackage()
    {
        var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            archive.CreateEntry("readme.txt");

        buffer.Position = 0;

        PackageOpenResult result = PackageOpener.Open(buffer, leaveOpen: true);

        Assert.Equal(PackageOpenOutcome.Rejected, result.Outcome);
        Assert.Equal(ContainerViolationCode.MimetypeContent, Assert.Single(result.Violations).Code);

        buffer.Dispose();
    }

    [Fact]
    public void RejectsSomethingThatIsNotAnArchive()
    {
        using var buffer = new MemoryStream(new byte[4096]);

        PackageOpenResult result = PackageOpener.Open(buffer, leaveOpen: true);

        Assert.Equal(ContainerViolationCode.NotAZip, Assert.Single(result.Violations).Code);
    }

    [Fact]
    public void RejectsAPackageWithNoContainer()
    {
        var buffer = new MemoryStream();

        using (ZipArchive archive = KomaArchive.Create(buffer, leaveOpen: true))
            Write(archive, "koma/manifest.xml", Manifest);

        buffer.Position = 0;

        PackageOpenResult result = PackageOpener.Open(buffer, leaveOpen: true);

        Assert.Equal(ContainerViolationCode.MissingRequiredXml, Assert.Single(result.Violations).Code);

        buffer.Dispose();
    }

    [Fact]
    public void RejectsAContainerThatIsNotWellFormed()
    {
        using MemoryStream buffer = Build("<Container xmlns=\"urn:koma:container\" version=\"0.9\">");

        PackageOpenResult result = Open(buffer);

        Assert.Equal(ContainerViolationCode.XmlNotWellFormed, Assert.Single(result.Violations).Code);
    }

    [Fact]
    public void RefusesADoctype()
    {
        // A DTD is an amplification vector and an external entity is a
        // file-read primitive. The parser is configured to refuse both, so this
        // never reaches the point of being a KOMA question.
        const string withDoctype = """
            <!DOCTYPE Container [<!ENTITY x "y">]>
            <Container xmlns="urn:koma:container" version="0.9"/>
            """;

        using MemoryStream buffer = Build(withDoctype);

        PackageOpenResult result = Open(buffer);

        Assert.Equal(ContainerViolationCode.XmlNotWellFormed, Assert.Single(result.Violations).Code);
    }

    [Fact]
    public void RejectsAWrongRootElement()
    {
        using MemoryStream buffer = Build("<Package xmlns=\"urn:koma:container\" version=\"0.9\"/>");

        PackageOpenResult result = Open(buffer);

        Assert.Equal(ContainerViolationCode.SchemaInvalidContainer, Assert.Single(result.Violations).Code);
    }

    [Fact]
    public void RejectsARootElementInTheWrongNamespace()
    {
        using MemoryStream buffer = Build("<Container xmlns=\"urn:koma:manifest\" version=\"0.9\"/>");

        PackageOpenResult result = Open(buffer);

        Assert.Equal(ContainerViolationCode.SchemaInvalidContainer, Assert.Single(result.Violations).Code);
    }

    [Theory]
    [InlineData("0.09")]
    [InlineData("")]
    [InlineData("nine")]
    public void RejectsAMalformedVersion(string version)
    {
        // §5.1 defers to the Integer of §4: a version that is malformed is a
        // defective document, not an unsupported one.
        using MemoryStream buffer = Build(Container.Replace("version=\"0.9\"", $"version=\"{version}\"", StringComparison.Ordinal));

        PackageOpenResult result = Open(buffer);

        Assert.Equal(PackageOpenOutcome.Rejected, result.Outcome);
        Assert.Equal(ContainerViolationCode.SchemaInvalidContainer, Assert.Single(result.Violations).Code);
    }

    [Fact]
    public void RejectsTwoRootFiles()
    {
        const string two = """
            <Container xmlns="urn:koma:container" version="0.9">
              <RootFiles>
                <RootFile full-path="koma/manifest.xml"
                          media-type="application/vnd.koma.manifest+xml"/>
                <RootFile full-path="koma/other.xml"
                          media-type="application/vnd.koma.manifest+xml"/>
              </RootFiles>
            </Container>
            """;

        using MemoryStream buffer = Build(two);

        PackageOpenResult result = Open(buffer);
         
        Assert.Equal(ContainerViolationCode.SchemaInvalidContainer, Assert.Single(result.Violations).Code);
    }

    [Fact]
    public void RejectsAWrongMediaTypeLiteral()
    {
        // §2 says these literals are fixed strings, never resolved, so an
        // approximation is not a near miss but a different value.
        using MemoryStream buffer = Build(Container.Replace("application/vnd.koma.manifest+xml", "application/xml", StringComparison.Ordinal));

        PackageOpenResult result = Open(buffer);

        Assert.Equal(ContainerViolationCode.SchemaInvalidContainer, Assert.Single(result.Violations).Code);
    }

    [Fact]
    public void RejectsARootFilePathThatEscapesThePackage()
    {
        using MemoryStream buffer = Build(Container.Replace("koma/manifest.xml", "../../etc/passwd", StringComparison.Ordinal));

        PackageOpenResult result = Open(buffer);

        Assert.Equal(ContainerViolationCode.SchemaInvalidContainer, Assert.Single(result.Violations).Code);
    }

    [Fact]
    public void RejectsAPackageWithNoManifest()
    {
        using MemoryStream buffer = Build(Container, manifest: null);

        PackageOpenResult result = Open(buffer);

        ContainerViolation violation = Assert.Single(result.Violations);
        Assert.Equal(ContainerViolationCode.MissingRequiredXml, violation.Code);
        Assert.Equal("koma/manifest.xml", violation.EntryName);
    }

    [Fact]
    public void RejectsARootFileNamingAnotherManifest()
    {
        // §1 fixes the path, so a RootFile naming another one is not followed
        // even though koma/manifest.xml is there to be read.
        using MemoryStream buffer = Build(Container.Replace("koma/manifest.xml", "koma/root.xml", StringComparison.Ordinal));

        PackageOpenResult result = Open(buffer);

        Assert.Equal(ContainerViolationCode.SchemaInvalidContainer, Assert.Single(result.Violations).Code);
    }

    [Theory]
    [InlineData("metadata=\"koma/meta.xml\"")]
    [InlineData("metadata=\"koma/metadata.xml\" navigation=\"koma/toc.xml\"")]
    public void RejectsAManifestNamingAnotherCoreDocument(string attributes)
    {
        using MemoryStream buffer = Build(Container, Manifest.Replace("metadata=\"koma/metadata.xml\"", attributes, StringComparison.Ordinal));

        PackageOpenResult result = Open(buffer);

        Assert.Equal(ContainerViolationCode.SchemaInvalidManifest, Assert.Single(result.Violations).Code);
    }

    [Fact]
    public void OpensADeclaredNavigationDocumentWithoutWarning()
    {
        using MemoryStream buffer = Build(Container, DeclaringNavigation(), (CorePaths.Navigation, Navigation));

        PackageOpenResult result = Open(buffer);

        Assert.Equal(PackageOpenOutcome.Opened, result.Outcome);
        Assert.DoesNotContain(result.Violations, v => v.Code == ContainerViolationCode.NoNavigationDocument);

        using KomaPackage? package = result.Package;
        Assert.NotNull(package);
        Assert.True(package.Manifest.DeclaresNavigation);
        Assert.NotNull(package.Navigation);
        Assert.Equal("front-cover", Assert.Single(package.Navigation.Landmarks).Type);
    }

    [Fact]
    public void RejectsADeclaredNavigationDocumentThatIsAbsent()
    {
        using MemoryStream buffer = Build(Container, DeclaringNavigation());

        PackageOpenResult result = Open(buffer);

        Assert.Equal(PackageOpenOutcome.Rejected, result.Outcome);
        Assert.Equal(ContainerViolationCode.NavigationDeclarationMismatch, Assert.Single(result.Violations).Code);
    }

    [Fact]
    public void RejectsAnUndeclaredNavigationDocument()
    {
        // Reading the file anyway would be believing the package over the
        // manifest, which is a choice §8 does not leave to the reader.
        using MemoryStream buffer = Build(Container, Manifest, (CorePaths.Navigation, Navigation));

        PackageOpenResult result = Open(buffer);

        Assert.Equal(PackageOpenOutcome.Rejected, result.Outcome);
        Assert.Equal(ContainerViolationCode.NavigationDeclarationMismatch, Assert.Single(result.Violations).Code);
    }

    [Fact]
    public void ReportsContainerAndArchiveFaultsTogether()
    {
        // Once the version is settled, faults accumulate: a package with two
        // problems should take one attempt to diagnose, not two.
        using MemoryStream buffer = Build(Container, manifest: null, extra: ("../escape.txt", "x"));

        PackageOpenResult result = Open(buffer);

        Assert.Equal(2, result.Violations.Count);
        Assert.Contains(result.Violations, v => v.Code == ContainerViolationCode.PathTraversal);
        Assert.Contains(result.Violations, v => v.Code == ContainerViolationCode.MissingRequiredXml);
    }

    [Fact]
    public void RejectsAnXmlDocumentNestedTooDeeply()
    {
        var deep = new StringBuilder("<Container xmlns=\"urn:koma:container\" version=\"0.9\">");

        for (int i = 0; i < 8; i++)
            deep.Append("<a>");

        for (int i = 0; i < 8; i++)
            deep.Append("</a>");

        deep.Append("</Container>");

        using MemoryStream buffer = Build(deep.ToString());

        PackageOpenResult result = Open(buffer, ResourceLimits.Default with { MaxXmlDepth = 4 });

        Assert.Equal(ContainerViolationCode.XmlNestingLimit, Assert.Single(result.Violations).Code);
    }

    [Fact]
    public void RejectsACoreDocumentAboveTheSizeLimit()
    {
        using MemoryStream buffer = Build();

        PackageOpenResult result = Open(buffer, ResourceLimits.Default with { MaxCoreDocumentBytes = 8 });

        Assert.Equal(ContainerViolationCode.XmlDocumentSizeLimit, Assert.Single(result.Violations).Code);
    }

    [Fact]
    public void AnOpenPackageReadsItsOwnDocuments()
    {
        using MemoryStream buffer = Build();

        using KomaPackage? package = Open(buffer).Package;
        Assert.NotNull(package);

        XDocument? manifest = package.TryLoadXml(CorePaths.Manifest, out ContainerViolation? violation);

        Assert.Null(violation);
        Assert.NotNull(manifest);
        Assert.Equal(XName.Get("Manifest", "urn:koma:manifest"), manifest.Root!.Name);
    }

    [Fact]
    public void AnOpenPackageBoundsTheResourcesItHandsOut()
    {
        using MemoryStream buffer = Build(Container, Manifest, ("pages/001.bin", "abcdef"));

        using KomaPackage? package = Open(buffer).Package;
        Assert.NotNull(package);

        using Stream? resource = package.TryOpenResource("pages/001.bin");
        Assert.NotNull(resource);
        Assert.IsType<BoundedReadStream>(resource);

        using var reader = new StreamReader(resource, Encoding.UTF8);
        Assert.Equal("abcdef", reader.ReadToEnd());
    }

    [Fact]
    public void AnOpenPackageReportsAMissingResourceRatherThanThrowing()
    {
        using MemoryStream buffer = Build();

        using KomaPackage? package = Open(buffer).Package;
        Assert.NotNull(package);

        Assert.Null(package.TryOpenResource("pages/nothing.webp"));
    }
}
