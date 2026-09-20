using System.Xml.Linq;
using Koma.Core.Model;
using Koma.Core.Packaging;

namespace Koma.Core.Tests;

/// <summary>
/// The titles of §7.3, with the languages of §4.4.
/// </summary>
public sealed class MetadataReaderTests
{
    private const string Entry = CorePaths.Metadata;

    private static readonly KomaVersion Version = KomaVersion.Supported;

    [Fact]
    public void ReadsTitlesWithTheirTypesAndLanguages()
    {
        PublicationMetadata metadata = Read("""
            <Metadata xmlns="urn:koma:metadata" version="0.9" xml:lang="fr">
              <Titles>
                <Title type="main">  Chroniques du Rivage  </Title>
                <Title type="original" xml:lang="ja">海辺の年代記</Title>
              </Titles>
              <Languages><Language role="content">fr</Language></Languages>
              <Reading direction="ltr" spread="auto"/>
            </Metadata>
            """);

        Assert.Equal(new PublicationTitle("Chroniques du Rivage", "main", "fr"), metadata.MainTitle);
        Assert.Equal("ja", metadata.Titles[1].Language);
    }

    [Fact]
    public void TakesTheContentLanguageWhenTheDocumentDeclaresNone()
    {
        PublicationMetadata metadata = Read("""
            <Metadata xmlns="urn:koma:metadata" version="0.9">
              <Titles><Title type="main">海辺の年代記</Title></Titles>
              <Languages><Language role="content">ja</Language></Languages>
              <Reading direction="rtl" spread="auto"/>
            </Metadata>
            """);

        Assert.Equal("ja", metadata.MainTitle.Language);
    }

    [Theory]
    [InlineData("""<Titles><Title type="subtitle">S</Title></Titles>""")]
    [InlineData("""<Titles><Title type="main">A</Title><Title type="main">B</Title></Titles>""")]
    [InlineData("""<Titles><Title>A</Title></Titles>""")]
    [InlineData("""<Titles><Title type="main">   </Title></Titles>""")]
    public void RejectsTitlesThatLeaveNoNameToListUnder(string titles)
    {
        // No main title, two of them, one with no type, one with no text.
        var violations = new List<ContainerViolation>();
        XDocument document = XDocument.Parse($"""<Metadata xmlns="urn:koma:metadata" version="0.9">{titles}<Reading direction="ltr" spread="auto"/></Metadata>""");

        Assert.Null(MetadataReader.Read(document, Entry, Version, violations));
        Assert.Equal(ContainerViolationCode.SchemaInvalidMetadata, Assert.Single(violations).Code);
    }

    private static PublicationMetadata Read(string xml)
    {
        var violations = new List<ContainerViolation>();
        PublicationMetadata? metadata = MetadataReader.Read(XDocument.Parse(xml), Entry, Version, violations);

        Assert.Empty(violations);
        Assert.NotNull(metadata);

        return metadata;
    }
}
