using System.Xml.Linq;
using Koma.Core.Model;
using Koma.Core.Packaging;
using Koma.Core.Rendering;

namespace Koma.Core.Tests;

/// <summary>
/// <c>nav.xml</c> read into what a reader navigates by (§9), with the label
/// languages of §4.4.
/// </summary>
public sealed class NavigationReaderTests
{
    private const string Entry = CorePaths.Navigation;

    private static readonly KomaVersion Version = KomaVersion.Supported;

    [Fact]
    public void ReadsTheTableOfContentsAsATree()
    {
        PublicationNavigation navigation = Read("""
            <Navigation xmlns="urn:koma:navigation" version="0.9" xml:lang="fr">
              <TableOfContents>
                <Entry item="p002">
                  <Label>  Chapitre 1  </Label>
                  <Label xml:lang="en">Chapter 1</Label>
                  <Entry item="p012"><Label>La traversée</Label></Entry>
                </Entry>
                <Entry item="p020"><Label>Chapitre 2</Label></Entry>
              </TableOfContents>
            </Navigation>
            """);

        Assert.Equal(2, navigation.TableOfContents.Count);

        TocEntry first = navigation.TableOfContents[0];

        Assert.Equal("p002", first.Item);
        Assert.Equal(new NavigationLabel("Chapitre 1", "fr"), first.Labels[0]);
        Assert.Equal(new NavigationLabel("Chapter 1", "en"), first.Labels[1]);
        Assert.Equal("p012", Assert.Single(first.Children).Item);
        Assert.Empty(navigation.TableOfContents[1].Children);
    }

    [Fact]
    public void TakesTheContentLanguageWhenNothingDeclaresOne()
    {
        // §4.4: no xml:lang anywhere, so the first content language of the
        // metadata is the document language.
        PublicationNavigation navigation = Read("""
            <Navigation xmlns="urn:koma:navigation" version="0.9">
              <TableOfContents><Entry item="p002"><Label>第一話</Label></Entry></TableOfContents>
            </Navigation>
            """, contentLanguage: "ja");

        Assert.Equal("ja", navigation.TableOfContents[0].Labels[0].Language);
    }

    [Fact]
    public void TreatsAnEmptyLanguageAsUndetermined()
    {
        // Empty says undetermined outright; it does not fall through to the
        // root's language.
        PublicationNavigation navigation = Read("""
            <Navigation xmlns="urn:koma:navigation" version="0.9" xml:lang="fr">
              <TableOfContents><Entry item="p002"><Label xml:lang="">42</Label></Entry></TableOfContents>
            </Navigation>
            """);

        Assert.Null(navigation.TableOfContents[0].Labels[0].Language);
    }

    [Fact]
    public void ReadsThePageListWithItsHalves()
    {
        PublicationNavigation navigation = Read("""
            <Navigation xmlns="urn:koma:navigation" version="0.9">
              <PageList>
                <PageTarget item="p012" label="iv"/>
                <PageTarget item="p013" label="12bis" spread-position="right"/>
                <PageTarget item="p013" label="13" spread-position="left"/>
              </PageList>
            </Navigation>
            """);

        PageTarget[] expected =
        [
            new("p012", "iv", null),
            new("p013", "12bis", PhysicalSide.Right),
            new("p013", "13", PhysicalSide.Left)
        ];

        Assert.Equal(expected, navigation.PageList);
    }

    [Theory]
    [InlineData("""<PageTarget item="p004" label="3" spread-position="right"/><PageTarget item="p004" label="4" spread-position="right"/>""")]
    [InlineData("""<PageTarget item="p002" label="1"/><PageTarget item="p002" label="1bis"/>""")]
    public void RejectsTwoLabelsForOneHalf(string targets)
    {
        // The second case is two whole-resource labels: §9.2 counts an absent
        // spread-position as a value of its own.
        var violations = new List<ContainerViolation>();
        PublicationNavigation navigation = Read($"""<Navigation xmlns="urn:koma:navigation" version="0.9"><PageList>{targets}</PageList></Navigation>""", violations: violations);

        Assert.Single(navigation.PageList);
        Assert.Equal(ContainerViolationCode.PageTargetDuplicate, Assert.Single(violations).Code);
    }

    [Fact]
    public void KeepsCoreLandmarksAndDropsTheRest()
    {
        // §4.5.1 ignores a landmark whose type is not recognised. A private-use
        // type is legal and noted; any other token is dropped as quietly.
        var violations = new List<ContainerViolation>();
        PublicationNavigation navigation = Read("""
            <Navigation xmlns="urn:koma:navigation" version="0.9">
              <Landmarks>
                <Landmark type="front-cover" item="p001"/>
                <Landmark type="x-splash" item="p003"/>
                <Landmark type="prologue" item="p004"/>
                <Landmark type="body-start" item="p005"><Label xml:lang="fr">Début</Label></Landmark>
              </Landmarks>
            </Navigation>
            """, violations: violations);

        string[] types = ["front-cover", "body-start"];

        Assert.Equal(types, navigation.Landmarks.Select(l => l.Type));
        Assert.Equal(ContainerViolationCode.PrivateUseToken, Assert.Single(violations).Code);
        Assert.Equal(ViolationSeverity.Warning, violations[0].Severity);
    }

    [Fact]
    public void RejectsTwoLandmarksOfOneType()
    {
        var violations = new List<ContainerViolation>();

        Read("""
            <Navigation xmlns="urn:koma:navigation" version="0.9">
              <Landmarks>
                <Landmark type="body-start" item="p002"/>
                <Landmark type="body-start" item="p003"/>
              </Landmarks>
            </Navigation>
            """, violations: violations);

        Assert.Equal(ContainerViolationCode.LandmarkDuplicateType, Assert.Single(violations).Code);
    }

    [Theory]
    [InlineData("<Landmarks><Landmark type=\"front-cover\" item=\"p001\"/></Landmarks><TableOfContents><Entry item=\"p002\"><Label>1</Label></Entry></TableOfContents>")]
    [InlineData("<Landmarks><Landmark type=\"front-cover\" item=\"p001\"/></Landmarks><Landmarks><Landmark type=\"body-start\" item=\"p002\"/></Landmarks>")]
    [InlineData("<Extensions/>")]
    [InlineData("<TableOfContents><Entry item=\"p002\"/></TableOfContents>")]
    [InlineData("<TableOfContents><Entry item=\"p002\"><Label>  </Label></Entry></TableOfContents>")]
    [InlineData("<PageList><PageTarget item=\"p002\" label=\"2\" spread-position=\"center\"/></PageList>")]
    [InlineData("<Landmarks><Landmark type=\"Front Cover\" item=\"p001\"/></Landmarks>")]
    public void RejectsWhatTheModelCannotStandOn(string sections)
    {
        // Out of order, repeated, Extensions alone, an entry with no label, an
        // empty label, a half the page list does not have, a type that is not
        // a token.
        var violations = new List<ContainerViolation>();
        XDocument document = Document($"""<Navigation xmlns="urn:koma:navigation" version="0.9">{sections}</Navigation>""");

        Assert.Null(NavigationReader.Read(document, Entry, Version, null, violations));
        Assert.Equal(ContainerViolationCode.SchemaInvalidNavigation, Assert.Single(violations).Code);
    }

    [Fact]
    public void RejectsAnotherVersion()
    {
        var violations = new List<ContainerViolation>();
        XDocument document = Document("""<Navigation xmlns="urn:koma:navigation" version="0.8"><Landmarks><Landmark type="front-cover" item="p001"/></Landmarks></Navigation>""");

        Assert.Null(NavigationReader.Read(document, Entry, Version, null, violations));
        Assert.Equal(ContainerViolationCode.SchemaInvalidNavigation, Assert.Single(violations).Code);
    }

    [Theory]
    [InlineData("en-GB", "Chapter 1")]
    [InlineData("pt-PT", "Capítulo 1 (PT)")]
    [InlineData("pt", "Capítulo 1 (BR)")]
    [InlineData("de", "Chapitre 1")]
    public void Choose_PrefersAnExactTagThenTheSameLanguageThenTheFirst(string preferred, string expected)
    {
        NavigationLabel[] labels =
        [
            new("Chapitre 1", "fr"),
            new("Chapter 1", "en"),
            new("Capítulo 1 (BR)", "pt-BR"),
            new("Capítulo 1 (PT)", "pt-PT")
        ];
        string[] languages = [preferred];

        Assert.Equal(expected, NavigationLabel.Choose(labels, languages)?.Text);
    }

    private static PublicationNavigation Read(string xml, string? contentLanguage = null, List<ContainerViolation>? violations = null)
    {
        violations ??= [];
        PublicationNavigation? navigation = NavigationReader.Read(Document(xml), Entry, Version, contentLanguage, violations);

        Assert.NotNull(navigation);

        return navigation;
    }

    private static XDocument Document(string xml) => XDocument.Parse(xml);
}
