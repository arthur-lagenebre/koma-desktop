using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Koma.Core.Model;
using Koma.Core.Rendering;

namespace Koma.Core.Writing;

/// <summary>
/// The series a publication belongs to (§7.5), as an editor sets it.
/// </summary>
/// <param name="Position">Kept as text, as §7.5 keeps it: HS2 and 3.5 are numbers too.</param>
public sealed record SeriesEdit(string Name, string? Position = null, string? Total = null);

/// <summary>
/// What a publication says about reading it (§7.13).
/// </summary>
/// <param name="AccessModes">How the publication is taken in: visual, textual, and so on.</param>
/// <param name="Hazards">What it may do to a reader, or the absence of it.</param>
/// <param name="Summary">A sentence for a reader deciding whether they can read it.</param>
public sealed record AccessibilityEdit(IReadOnlyList<string> AccessModes, IReadOnlyList<string> Hazards, string? Summary);

/// <summary>
/// What an edit changes. A <see langword="null"/> field is left as it is.
/// </summary>
public sealed record MetadataEdit(string? Title = null, string? Language = null, ReadingDirection? Direction = null, SeriesEdit? Series = null, AccessibilityEdit? Accessibility = null);

/// <summary>
/// Applies an edit to <c>metadata.xml</c>, touching nothing it was not asked
/// to change.
/// </summary>
/// <remarks>
/// <para>
/// The document is edited in place rather than rebuilt from a model: the
/// model knows a fraction of §7, and a rights statement, an accessibility
/// summary or an extension it does not read must come out as it went in.
/// </para>
/// <para>
/// Every edit updates <c>Publication/Date[@event="modified"]</c>, which §7.2.1
/// requires of a producer that changes a core document, and adds it when it
/// is missing, which §7.2.1 recommends. It carries the time to the second: a
/// date alone would give two edits on one day the same release identity.
/// </para>
/// </remarks>
public static partial class MetadataEditor
{
    private const string Namespace = "urn:koma:metadata";

    /// <summary>The children of <c>Metadata</c>, in the order §7 and its schema give them.</summary>
    private static readonly string[] Order =
    [
        "Identifiers", "Titles", "Languages", "Collections", "Contributors", "Descriptions", "Publication",
        "Subjects", "Entities", "Reading", "Content", "Accessibility", "Ratings", "Links", "Rights", "Provenance", "Extensions"
    ];

    /// <summary>
    /// What the document says now, in the fields an edit can change: the
    /// values an editing form starts from.
    /// </summary>
    public static MetadataEdit Read(XDocument metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        XElement root = metadata.Root ?? throw new ArgumentException("The document has no root element.", nameof(metadata));
        XElement? series = root.Element(X("Collections"))?.Elements(X("Collection")).FirstOrDefault(c => (string?)c.Attribute("type") == "series");
        string? seriesName = series?.Element(X("Name"))?.Value.Trim();

        return new MetadataEdit(
            root.Element(X("Titles"))?.Elements(X("Title")).FirstOrDefault(t => (string?)t.Attribute("type") == "main")?.Value.Trim(),
            root.Element(X("Languages"))?.Elements(X("Language")).FirstOrDefault(l => (string?)l.Attribute("role") == "content")?.Value.Trim(),
            (string?)root.Element(X("Reading"))?.Attribute("direction") == "rtl" ? ReadingDirection.RightToLeft : ReadingDirection.LeftToRight,
            seriesName is null ? null : new SeriesEdit(seriesName, (string?)series!.Attribute("position"), (string?)series.Attribute("total")),
            ReadAccessibility(root));
    }

    private static AccessibilityEdit ReadAccessibility(XElement root)
    {
        XElement? section = root.Element(X("Accessibility"));

        return new AccessibilityEdit(
            [.. section?.Elements(X("AccessMode")).Select(m => m.Value.Trim()) ?? []],
            [.. section?.Elements(X("AccessibilityHazard")).Select(h => h.Value.Trim()) ?? []],
            section?.Element(X("AccessibilitySummary"))?.Value.Trim() ?? string.Empty);
    }

    /// <summary>
    /// The edited document. The one given is not changed.
    /// </summary>
    /// <exception cref="ArgumentException">A value §4.3 does not allow.</exception>
    public static XDocument Apply(XDocument metadata, MetadataEdit edit, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(edit);

        Check(edit);

        var edited = new XDocument(metadata);
        XElement root = edited.Root ?? throw new ArgumentException("The document has no root element.", nameof(metadata));

        if (edit.Title is { } title)
            Required(root, "Titles", "Title", "type", "main").Value = title.Trim();

        if (edit.Language is { } language)
            SetLanguage(root, language.Trim());

        if (edit.Direction is { } direction)
            (root.Element(X("Reading")) ?? throw new ArgumentException("The metadata has no Reading element (§7.11).", nameof(metadata))).SetAttributeValue("direction", direction == ReadingDirection.RightToLeft ? "rtl" : "ltr");

        if (edit.Series is { } series)
            SetSeries(root, series);

        if (edit.Accessibility is { } accessibility)
            SetAccessibility(root, accessibility);

        SetModified(root, now);

        return edited;
    }

    private static void Check(MetadataEdit edit)
    {
        // Normalized and non-empty (§4.3): whitespace alone is no title.
        if (edit.Title is not null && edit.Title.Trim().Length == 0)
            throw new ArgumentException("A main title cannot be empty (§7.3).", nameof(edit));

        if (edit.Language is not null && !LanguageTag().IsMatch(edit.Language.Trim()))
            throw new ArgumentException($"'{edit.Language}' is not a language tag (§4.3).", nameof(edit));

        if (edit.Series is not null && edit.Series.Name.Trim().Length == 0)
            throw new ArgumentException("A series needs a name (§7.5).", nameof(edit));
    }

    /// <remarks>
    /// The root's <c>xml:lang</c> moves with the content language only when
    /// the two said the same thing: a converter writes them together, but an
    /// author may have declared a document language on purpose.
    /// </remarks>
    private static void SetLanguage(XElement root, string language)
    {
        XElement content = Required(root, "Languages", "Language", "role", "content");
        XAttribute? declared = root.Attribute(XNamespace.Xml + "lang");

        if (declared is not null && declared.Value == content.Value.Trim())
            declared.Value = language;

        content.Value = language;
    }

    private static void SetSeries(XElement root, SeriesEdit series)
    {
        XElement? collection = root.Element(X("Collections"))?.Elements(X("Collection")).FirstOrDefault(c => (string?)c.Attribute("type") == "series");

        if (collection is null)
        {
            collection = new XElement(X("Collection"), new XAttribute("type", "series"), new XElement(X("Name"), series.Name.Trim()));
            Container(root, "Collections").AddFirst(collection);
        }
        else
        {
            XElement name = collection.Element(X("Name")) ?? new XElement(X("Name"));

            if (name.Parent is null)
                collection.AddFirst(name);

            name.Value = series.Name.Trim();
        }

        // Kept after type and before the names, where the converter puts
        // them, so that an edited package reads like a converted one.
        Numbering(collection, "position", series.Position);
        Numbering(collection, "total", series.Total);
    }

    private static void Numbering(XElement collection, string attribute, string? value)
    {
        string? text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        XAttribute? existing = collection.Attribute(attribute);

        if (existing is not null)
        {
            if (text is null)
                existing.Remove();
            else
                existing.Value = text;

            return;
        }

        if (text is null)
            return;

        // An attribute added later would land after the others; the set is
        // rebuilt in the order the converter writes, unknown ones last.
        XAttribute[] attributes = [.. collection.Attributes(), new XAttribute(attribute, text)];
        collection.ReplaceAttributes(attributes.OrderBy(a => Rank(a.Name.LocalName)));
    }

    private static int Rank(string attribute) => attribute switch
    {
        "type" => 0,
        "position" => 1,
        "total" => 2,
        "relation" => 3,
        _ => 4
    };

    /// <summary>
    /// Writes what §7.13 asks of a publication about reading it, and leaves
    /// alone what it does not model.
    /// </summary>
    /// <remarks>
    /// The section holds more than three kinds of child — sufficient access
    /// modes, features, conformance, certification — and an editor that
    /// rebuilt it whole would drop them. Only the three it knows are
    /// replaced, each back in the order §7.13 gives.
    /// </remarks>
    private static void SetAccessibility(XElement root, AccessibilityEdit edit)
    {
        string[] modes = [.. edit.AccessModes.Select(m => m.Trim()).Where(m => m.Length > 0)];
        string[] hazards = [.. edit.Hazards.Select(h => h.Trim()).Where(h => h.Length > 0)];
        string summary = (edit.Summary ?? string.Empty).Trim();

        if (modes.Concat(hazards).FirstOrDefault(t => !KomaTokens.IsToken(t)) is { } malformed)
            throw new ArgumentException($"'{malformed}' is not a token (§4.3).", nameof(edit));

        XElement? section = root.Element(X("Accessibility"));

        // Nothing to say: the section goes, and the publication is back to
        // saying nothing about reading it, which §7.13 only warns about.
        if (modes.Length == 0 && hazards.Length == 0 && summary.Length == 0)
        {
            section?.Remove();
            return;
        }

        if (modes.Length == 0)
            throw new ArgumentException("An accessibility section names at least one access mode (§7.13).", nameof(edit));

        section ??= Container(root, "Accessibility");

        var rebuilt = new List<XElement>();

        rebuilt.AddRange(modes.Select(m => new XElement(X("AccessMode"), m)));
        rebuilt.AddRange(section.Elements(X("AccessModeSufficient")));
        rebuilt.AddRange(section.Elements(X("AccessibilityFeature")));
        rebuilt.AddRange(hazards.Select(h => new XElement(X("AccessibilityHazard"), h)));

        if (summary.Length > 0)
            rebuilt.Add(Summary(root, summary));

        rebuilt.AddRange(section.Elements(X("ConformsTo")));
        rebuilt.AddRange(section.Elements(X("Certification")));

        section.ReplaceNodes(rebuilt);
    }

    /// <summary>A summary in the language the document is written in (§4.4).</summary>
    private static XElement Summary(XElement root, string text)
    {
        var summary = new XElement(X("AccessibilitySummary"), text);

        if ((string?)root.Attribute(XNamespace.Xml + "lang") is { } language)
            summary.SetAttributeValue(XNamespace.Xml + "lang", language);

        return summary;
    }

    private static void SetModified(XElement root, DateTimeOffset now)
    {
        string stamp = now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        XElement publication = Container(root, "Publication");
        XElement? modified = publication.Elements(X("Date")).FirstOrDefault(d => (string?)d.Attribute("event") == "modified");

        if (modified is null)
            publication.Add(new XElement(X("Date"), new XAttribute("event", "modified"), stamp));
        else
            modified.Value = stamp;
    }

    private static XElement Required(XElement root, string container, string element, string attribute, string value) =>
        root.Element(X(container))?.Elements(X(element)).FirstOrDefault(e => (string?)e.Attribute(attribute) == value)
        ?? throw new ArgumentException($"The metadata has no {element} {attribute}=\"{value}\", which §7 requires.", nameof(root));

    /// <summary>The child of <c>Metadata</c> with this name, created where §7 places it when missing.</summary>
    private static XElement Container(XElement root, string name)
    {
        if (root.Element(X(name)) is { } existing)
            return existing;

        var created = new XElement(X(name));
        int rank = Array.IndexOf(Order, name);
        XElement? next = root.Elements().FirstOrDefault(e => e.Name.Namespace == Namespace && Array.IndexOf(Order, e.Name.LocalName) > rank);

        if (next is null)
            root.Add(created);
        else
            next.AddBeforeSelf(created);

        return created;
    }

    // xsd:language, which is what the schema checks a LanguageTag against.
    [GeneratedRegex("^[a-zA-Z]{1,8}(-[a-zA-Z0-9]{1,8})*$")]
    private static partial Regex LanguageTag();

    private static XName X(string name) => XName.Get(name, Namespace);
}
