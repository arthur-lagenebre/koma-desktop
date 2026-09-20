using System.Collections.Frozen;
using System.Xml.Linq;
using Koma.Core.Packaging;
using Koma.Core.Versioning;

namespace Koma.Core.Model;

/// <summary>
/// Every open vocabulary of the specification (§4.5), and what a token
/// outside its core set means.
/// </summary>
/// <remarks>
/// <para>
/// One table rather than a check in each reader: the rule is the same for all
/// twenty-five, and a vocabulary added to the specification is a line here.
/// The readers keep what their model needs of a token — a landmark of an
/// unknown type is not in the model — and leave judging it to this class.
/// </para>
/// <para>
/// A private-use token is legal and noted once. Any other token outside the
/// core set is <c>unknown-token</c>, an error in strict mode, which is how an
/// identical <c>0.x</c> is read. §16 lets a reading system read past it, since
/// §4.5.1 gives every one a fallback, so the opener does not refuse on it.
/// </para>
/// </remarks>
public static class OpenVocabularies
{
    private const string Metadata = "urn:koma:metadata";
    private const string Manifest = "urn:koma:manifest";
    private const string Navigation = "urn:koma:navigation";

    private static readonly Vocabulary[] All =
    [
        Of(Metadata, "Identifier", "scheme", false, ["uuid", "isbn-10", "isbn-13", "ean-13", "issn", "doi", "uri", "proprietary"]),
        Of(Metadata, "Title", "type", false, ["main", "subtitle", "original", "alternative", "short", "sort"]),
        Of(Metadata, "Language", "role", false, ["content", "original", "translation", "secondary"]),
        Of(Metadata, "Collection", "type", false, ["series", "subseries", "cycle", "story-arc", "publisher-collection", "franchise", "universe", "anthology", "other"]),
        Of(Metadata, "Collection", "relation", false, ["main", "special", "other"]),
        Of(Metadata, "Name", "type", false, ["name", "given", "family", "middle", "prefix", "suffix", "pseudonym", "mononym", "alternative"]),
        Of(Metadata, "Contributor", "roles", true, ["writer", "script-writer", "adapter", "artist", "penciller", "inker", "colorist", "letterer", "cover-artist", "translator", "editor", "designer", "photographer", "consultant", "other"]),
        Of(Metadata, "Description", "type", false, ["summary", "synopsis", "blurb", "note", "edition-note", "series-note", "other"]),
        Of(Metadata, "Date", "event", false, ["publication", "first-publication", "creation", "digitization", "modified"]),
        Of(Metadata, "Subject", "type", false, ["genre", "theme", "keyword", "setting", "time-period", "audience", "other"]),
        Of(Metadata, "Entity", "type", false, ["character", "team", "organization", "location", "vehicle", "object", "event", "other"]),
        Of(Metadata, "Entity", "role", false, ["protagonist", "antagonist", "supporting", "cameo", "narrator"]),
        Of(Metadata, "Content", "original-medium", false, ["print", "digital", "webtoon", "mixed", "unknown"]),
        Of(Metadata, "Source", "type", false, ["print", "digital", "microform", "original-artwork", "periodical", "other"]),
        Of(Metadata, "Method", null, false, ["flatbed-scan", "sheet-fed-scan", "overhead-scan", "photography", "born-digital", "other"]),
        Of(Metadata, "Processing", null, true, ["deskew", "despeckle", "crop", "level-adjust", "colour-correction", "denoise", "upscale", "recompression", "other"]),
        Of(Metadata, "AccessMode", null, false, ["visual", "textual", "auditory", "tactile"]),
        Of(Metadata, "AccessModeSufficient", null, true, ["visual", "textual", "auditory", "tactile"]),
        Of(Metadata, "AccessibilityFeature", null, false, ["alternative-text", "long-description", "reading-order", "structural-navigation", "page-navigation", "table-of-contents", "high-contrast-display", "none"]),
        Of(Metadata, "AccessibilityHazard", null, false, ["flashing", "no-flashing-hazard", "motion-simulation", "no-motion-simulation-hazard", "sound", "no-sound-hazard", "none", "unknown"]),
        Of(Metadata, "ContentWarning", "type", false, ["violence", "gore", "sexual-content", "nudity", "language", "drug-use", "self-harm", "flashing-images", "other"]),
        Of(Metadata, "Link", "rel", false, ["homepage", "publisher", "author", "series", "purchase", "record", "errata", "license", "source", "related-publication", "other"]),
        Of(Manifest, "Item", "roles", true, ["front-cover", "inner-cover", "title-page", "table-of-contents", "recap", "story", "interlude", "illustration", "advertisement", "editorial", "letters", "preview", "credits", "bonus", "blank", "back-cover", "other"]),
        Of(Navigation, "Landmark", "type", false, ["front-cover", "inner-cover", "title-page", "table-of-contents", "body-start", "story-start", "credits", "glossary", "appendix", "bonus", "preview", "back-cover"]),
        Of(Navigation, "Region", "type", false, ["panel", "group", "inset", "caption", "other"])
    ];

    /// <summary>
    /// Checks the open-vocabulary tokens of one core document, after its
    /// reader has accepted it.
    /// </summary>
    public static void Check(XDocument document, string entryName, ProcessingMode mode, List<ContainerViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(violations);

        string? documentNamespace = document.Root?.Name.NamespaceName;
        var noted = new HashSet<string>(StringComparer.Ordinal);

        foreach (Vocabulary vocabulary in All.Where(v => v.Namespace == documentNamespace))
        {
            foreach (XElement element in document.Descendants(XName.Get(vocabulary.Element, vocabulary.Namespace)))
            {
                string? value = vocabulary.Attribute is null ? element.Value : element.Attribute(vocabulary.Attribute)?.Value;

                if (value is null)
                    continue;

                string[] tokens = vocabulary.IsList ? value.Split(';') : [value.Trim()];

                foreach (string token in tokens.Where(t => !vocabulary.Core.Contains(t)))
                    Judge(token, vocabulary, entryName, mode, noted, violations);
            }
        }
    }

    private static void Judge(string token, Vocabulary vocabulary, string entryName, ProcessingMode mode, HashSet<string> noted, List<ContainerViolation> violations)
    {
        string where = vocabulary.Attribute is null ? vocabulary.Element : $"{vocabulary.Element}/@{vocabulary.Attribute}";

        // The readers check the lexical form of what their model uses; the
        // metadata reader does not, so a value that is not a Token at all
        // is caught here, against the schema that requires one.
        if (!KomaTokens.IsToken(token))
        {
            violations.Add(new ContainerViolation(SchemaCode(vocabulary.Namespace), entryName, $"{where} is '{token}', which is not a Token (§4.3)."));
            return;
        }

        // Once per distinct token and document: it is the vocabulary that is
        // private or unknown, not each use of it.
        if (!noted.Add(token))
            return;

        if (KomaTokens.IsPrivateUse(token))
            violations.Add(new ContainerViolation(ContainerViolationCode.PrivateUseToken, entryName, $"'{token}' in {where} is a private-use token; §4.5.1 applies and its meaning is the producer's alone.") { Severity = ViolationSeverity.Warning });
        else if (mode == ProcessingMode.Strict)
            violations.Add(new ContainerViolation(ContainerViolationCode.UnknownToken, entryName, $"'{token}' in {where} is neither a core token nor a private-use one (§4.5); read with the fallback of §4.5.1."));
    }

    private static string SchemaCode(string documentNamespace) => documentNamespace switch
    {
        Metadata => ContainerViolationCode.SchemaInvalidMetadata,
        Manifest => ContainerViolationCode.SchemaInvalidManifest,
        _ => ContainerViolationCode.SchemaInvalidNavigation
    };

    private static Vocabulary Of(string documentNamespace, string element, string? attribute, bool isList, string[] core) => new(documentNamespace, element, attribute, isList, core.ToFrozenSet(StringComparer.Ordinal));

    private sealed record Vocabulary(string Namespace, string Element, string? Attribute, bool IsList, FrozenSet<string> Core);
}
