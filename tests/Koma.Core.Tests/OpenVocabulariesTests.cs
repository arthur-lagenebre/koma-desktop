using System.Xml.Linq;
using Koma.Core.Model;
using Koma.Core.Packaging;
using Koma.Core.Versioning;

namespace Koma.Core.Tests;

/// <summary>
/// The open vocabularies of §4.5, judged in each document that holds one.
/// </summary>
public sealed class OpenVocabulariesTests
{
    private const string Entry = "koma/metadata.xml";

    [Fact]
    public void AcceptsCoreTokensSilently()
    {
        List<ContainerViolation> violations = Check("""
            <Metadata xmlns="urn:koma:metadata" version="0.9">
              <Title type="main">T</Title>
              <Contributor type="person" roles="writer;artist"><Name type="family">N</Name></Contributor>
              <AccessModeSufficient>visual;textual</AccessModeSufficient>
            </Metadata>
            """);

        Assert.Empty(violations);
    }

    [Fact]
    public void NotesAPrivateUseTokenOnce()
    {
        // Once per token and document: the vocabulary is private, not each use.
        List<ContainerViolation> violations = Check("""
            <Metadata xmlns="urn:koma:metadata" version="0.9">
              <Title type="x-working">T</Title>
              <Title type="x-working">U</Title>
            </Metadata>
            """);

        ContainerViolation noted = Assert.Single(violations);
        Assert.Equal(ContainerViolationCode.PrivateUseToken, noted.Code);
        Assert.Equal(ViolationSeverity.Warning, noted.Severity);
    }

    [Theory]
    [InlineData("""<Title type="headline">T</Title>""")]
    [InlineData("""<Contributor type="person" roles="writer;inkist"><Name>N</Name></Contributor>""")]
    [InlineData("""<AccessibilityFeature>subtitles</AccessibilityFeature>""")]
    [InlineData("""<Title type="x--draft">T</Title>""")]
    public void FaultsAnUnknownTokenInStrictMode(string element)
    {
        // The last is a Token but not a private-use one: the prefix must be
        // followed by a letter or a digit.
        List<ContainerViolation> violations = Check($"""<Metadata xmlns="urn:koma:metadata" version="0.9">{element}</Metadata>""");

        ContainerViolation unknown = Assert.Single(violations);
        Assert.Equal(ContainerViolationCode.UnknownToken, unknown.Code);
        Assert.Equal(ViolationSeverity.Error, unknown.Severity);
    }

    [Fact]
    public void LeavesAnUnknownTokenToItsFallbackInForwardCompatibleMode()
    {
        // §5.3: only strict mode makes it an error.
        List<ContainerViolation> violations = Check("""<Metadata xmlns="urn:koma:metadata" version="0.9"><Title type="headline">T</Title></Metadata>""", ProcessingMode.ForwardCompatible);

        Assert.Empty(violations);
    }

    [Fact]
    public void FaultsAValueThatIsNotATokenAgainstTheSchema()
    {
        List<ContainerViolation> violations = Check("""<Metadata xmlns="urn:koma:metadata" version="0.9"><Title type="Main Title">T</Title></Metadata>""");

        Assert.Equal(ContainerViolationCode.SchemaInvalidMetadata, Assert.Single(violations).Code);
    }

    [Fact]
    public void JudgesTheVocabulariesOfTheDocumentItIsGiven()
    {
        // A navigation document is judged on landmark and region types, and
        // not against a metadata vocabulary that shares an element name.
        var violations = new List<ContainerViolation>();
        XDocument navigation = XDocument.Parse("""
            <Navigation xmlns="urn:koma:navigation" version="0.9">
              <Landmarks><Landmark type="prologue" item="p001"/></Landmarks>
            </Navigation>
            """);

        OpenVocabularies.Check(navigation, CorePaths.Navigation, ProcessingMode.Strict, violations);

        Assert.Equal(ContainerViolationCode.UnknownToken, Assert.Single(violations).Code);
    }

    private static List<ContainerViolation> Check(string xml, ProcessingMode mode = ProcessingMode.Strict)
    {
        var violations = new List<ContainerViolation>();
        OpenVocabularies.Check(XDocument.Parse(xml), Entry, mode, violations);

        return violations;
    }
}
