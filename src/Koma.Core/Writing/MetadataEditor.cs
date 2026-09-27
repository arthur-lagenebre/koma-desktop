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
/// One of the people or organizations a publication is the work of (§7.6).
/// </summary>
/// <param name="Name">The name as it is written.</param>
/// <param name="Roles">What they did, from the vocabulary of §7.6; at least one.</param>
/// <param name="Organization">Whether it is an organization rather than a person.</param>
public sealed record ContributorEdit(string Name, IReadOnlyList<string> Roles, bool Organization = false);

/// <summary>
/// One of the texts a publication carries about itself (§7.7).
/// </summary>
/// <param name="Type">The kind of text, from the vocabulary of §7.7.</param>
/// <param name="Text">The text itself, in the language of the document.</param>
public sealed record DescriptionEdit(string Type, string Text);

/// <summary>
/// What a publication is about (§7.9): a genre, a theme, a keyword.
/// </summary>
/// <param name="Type">The kind of subject, from the vocabulary of §7.9.</param>
/// <param name="Text">The subject itself, in the language of the document.</param>
public sealed record SubjectEdit(string Type, string Text);

/// <summary>
/// Someone or something the story is about (§7.10): a character, a team, a
/// place.
/// </summary>
/// <param name="Name">The name as it is written.</param>
/// <param name="Type">What it is, from the vocabulary of §7.10.</param>
/// <param name="Role">What it does in the story, or empty.</param>
public sealed record EntityEdit(string Name, string Type, string Role = "");

/// <summary>
/// How somebody else classified the publication (§7.14).
/// </summary>
/// <param name="Scheme">Whose classification it is: cero, esrb, pegi, a publisher's own.</param>
/// <param name="Value">What that scheme says: B, T, 16.</param>
/// <param name="Region">Where it applies, as two capitals, or empty.</param>
public sealed record RatingEdit(string Scheme, string Value, string Region = "");

/// <summary>
/// A warning about what a publication holds (§7.14).
/// </summary>
/// <param name="Type">What is being warned about, from the vocabulary of §7.14.</param>
/// <param name="Text">A word about it, or empty.</param>
public sealed record WarningEdit(string Type, string Text = "");

/// <summary>
/// Somewhere else about this publication (§7.15).
/// </summary>
/// <param name="Relation">What is at the other end, from the vocabulary of §7.15.</param>
/// <param name="Href">The address itself.</param>
/// <param name="Text">What to call it, or empty.</param>
public sealed record LinkEdit(string Relation, string Href, string Text = "");

/// <summary>
/// When something happened to the publication (§7.8).
/// </summary>
/// <param name="Event">What happened, from the vocabulary of §7.8.</param>
/// <param name="Value">The date, as §4.3 writes one: a year, a month, a day.</param>
public sealed record DateEdit(string Event, string Value);

/// <summary>
/// How big the paper was (§7.8), in millimetres.
/// </summary>
public sealed record PhysicalFormatEdit(string Width, string Height);

/// <summary>
/// What may be done with a publication (§7.16).
/// </summary>
/// <param name="Copyright">The copyright line, or empty.</param>
/// <param name="License">The licence, by its identifier, or empty.</param>
/// <param name="Statement">Anything else worth saying, or empty.</param>
public sealed record RightsEdit(string Copyright, string License, string Statement);

/// <summary>
/// What an edit changes. A <see langword="null"/> field is left as it is.
/// </summary>
/// <remarks>
/// A list given empty empties the section it stands for; a list left
/// <see langword="null"/> leaves it alone. The difference matters: an editor
/// that could not clear a list could not undo a bad conversion.
/// </remarks>
public sealed record MetadataEdit(
    string? Title = null,
    string? Language = null,
    ReadingDirection? Direction = null,
    SeriesEdit? Series = null,
    AccessibilityEdit? Accessibility = null,
    IReadOnlyList<ContributorEdit>? Contributors = null,
    IReadOnlyList<SubjectEdit>? Subjects = null,
    IReadOnlyList<DescriptionEdit>? Descriptions = null,
    IReadOnlyList<EntityEdit>? Entities = null,
    IReadOnlyList<RatingEdit>? Ratings = null,
    IReadOnlyList<WarningEdit>? Warnings = null,
    IReadOnlyList<LinkEdit>? Links = null,
    RightsEdit? Rights = null,
    string? Publisher = null,
    string? Imprint = null,
    string? Place = null,
    string? Edition = null,
    IReadOnlyList<DateEdit>? Dates = null,
    PhysicalFormatEdit? PhysicalFormat = null);

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
    /// <summary>What §7.8 holds, in the order it holds it.</summary>
    /// <summary>A region as §7.14 writes one: two capitals, as ISO 3166 does.</summary>
    [GeneratedRegex("^[A-Z]{2}$")]
    private static partial Regex Region();

    private static readonly string[] PublicationOrder = ["Publisher", "Imprint", "Place", "Edition", "Date", "PhysicalFormat"];

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
            ReadAccessibility(root),
            [.. root.Element(X("Contributors"))?.Elements(X("Contributor")).Select(c => new ContributorEdit(
                c.Element(X("Name"))?.Value.Trim() ?? string.Empty,
                [.. ((string?)c.Attribute("roles") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)],
                (string?)c.Attribute("type") == "organization")) ?? []],
            [.. root.Element(X("Subjects"))?.Elements(X("Subject")).Select(s => new SubjectEdit(
                (string?)s.Attribute("type") ?? "keyword",
                s.Value.Trim())) ?? []],
            [.. root.Element(X("Descriptions"))?.Elements(X("Description")).Select(d => new DescriptionEdit(
                (string?)d.Attribute("type") ?? "summary",
                d.Value.Trim())) ?? []],
            [.. root.Element(X("Entities"))?.Elements(X("Entity")).Select(e => new EntityEdit(
                e.Element(X("Name"))?.Value.Trim() ?? string.Empty,
                (string?)e.Attribute("type") ?? "character",
                (string?)e.Attribute("role") ?? string.Empty)) ?? []],
            [.. root.Element(X("Ratings"))?.Elements(X("Rating")).Select(r => new RatingEdit(
                (string?)r.Attribute("scheme") ?? string.Empty,
                (string?)r.Attribute("value") ?? string.Empty,
                (string?)r.Attribute("region") ?? string.Empty)) ?? []],
            [.. root.Element(X("Ratings"))?.Elements(X("ContentWarning")).Select(w => new WarningEdit(
                (string?)w.Attribute("type") ?? "other",
                w.Value.Trim())) ?? []],
            [.. root.Element(X("Links"))?.Elements(X("Link")).Select(l => new LinkEdit(
                (string?)l.Attribute("rel") ?? "other",
                (string?)l.Attribute("href") ?? string.Empty,
                l.Value.Trim())) ?? []],
            new RightsEdit(
                root.Element(X("Rights"))?.Element(X("Copyright"))?.Value.Trim() ?? string.Empty,
                (string?)root.Element(X("Rights"))?.Element(X("License"))?.Attribute("identifier") ?? string.Empty,
                root.Element(X("Rights"))?.Element(X("Statement"))?.Value.Trim() ?? string.Empty),
            root.Element(X("Publication"))?.Element(X("Publisher"))?.Value.Trim() ?? string.Empty,
            root.Element(X("Publication"))?.Element(X("Imprint"))?.Value.Trim() ?? string.Empty,
            root.Element(X("Publication"))?.Element(X("Place"))?.Value.Trim() ?? string.Empty,
            root.Element(X("Publication"))?.Element(X("Edition"))?.Value.Trim() ?? string.Empty,
            [.. root.Element(X("Publication"))?.Elements(X("Date")).Where(d => (string?)d.Attribute("event") != "modified").Select(d => new DateEdit(
                (string?)d.Attribute("event") ?? "publication",
                d.Value.Trim())) ?? []],
            new PhysicalFormatEdit(
                (string?)root.Element(X("Publication"))?.Element(X("PhysicalFormat"))?.Attribute("trim-width") ?? string.Empty,
                (string?)root.Element(X("Publication"))?.Element(X("PhysicalFormat"))?.Attribute("trim-height") ?? string.Empty));
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

        if (edit.Contributors is { } contributors)
            SetContributors(root, contributors);

        if (edit.Subjects is { } subjects)
            SetSubjects(root, subjects);

        if (edit.Descriptions is { } descriptions)
            SetDescriptions(root, descriptions);

        if (edit.Entities is { } entities)
            SetEntities(root, entities);

        if (edit.Ratings is { } ratings)
            SetRatings(root, ratings);

        if (edit.Warnings is { } warnings)
            SetWarnings(root, warnings);

        if (edit.Links is { } links)
            SetLinks(root, links);

        if (edit.Rights is { } rights)
            SetRights(root, rights);

        if (edit.Publisher is { } publisher)
            SetPublication(root, "Publisher", publisher);

        if (edit.Imprint is { } imprint)
            SetPublication(root, "Imprint", imprint);

        if (edit.Place is { } place)
            SetPublication(root, "Place", place);

        if (edit.Edition is { } edition)
            SetPublication(root, "Edition", edition);

        if (edit.Dates is { } dates)
            SetDates(root, dates);

        if (edit.PhysicalFormat is { } format)
            SetPhysicalFormat(root, format);

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

    /// <summary>
    /// Writes who the publication is the work of (§7.6), in the order given.
    /// </summary>
    /// <remarks>
    /// The order is the one shown, since §7.6 gives none and a list of
    /// contributors is read as an order of billing.
    /// </remarks>
    private static void SetContributors(XElement root, IReadOnlyList<ContributorEdit> contributors)
    {
        ContributorEdit[] written = [.. contributors.Where(c => !string.IsNullOrWhiteSpace(c.Name))];

        if (written.Length == 0)
        {
            root.Element(X("Contributors"))?.Remove();
            return;
        }

        foreach (ContributorEdit contributor in written)
        {
            string[] roles = [.. contributor.Roles.Select(r => r.Trim()).Where(r => r.Length > 0)];

            if (roles.Length == 0)
                throw new ArgumentException($"'{contributor.Name}' did what? §7.6 wants a role.", nameof(contributors));

            if (roles.FirstOrDefault(r => !KomaTokens.IsToken(r)) is { } malformed)
                throw new ArgumentException($"'{malformed}' is not a token (§4.3).", nameof(contributors));
        }

        Container(root, "Contributors").ReplaceNodes(written.Select(c => new XElement(
            X("Contributor"),
            new XAttribute("type", c.Organization ? "organization" : "person"),
            new XAttribute("roles", string.Join(' ', c.Roles.Select(r => r.Trim()).Where(r => r.Length > 0))),
            Named(root, c.Name.Trim()))));
    }

    /// <summary>Writes what the publication is about (§7.9).</summary>
    private static void SetSubjects(XElement root, IReadOnlyList<SubjectEdit> subjects)
    {
        SubjectEdit[] written = [.. subjects.Where(s => !string.IsNullOrWhiteSpace(s.Text))];

        if (written.Length == 0)
        {
            root.Element(X("Subjects"))?.Remove();
            return;
        }

        if (written.Select(s => s.Type.Trim()).FirstOrDefault(t => t.Length > 0 && !KomaTokens.IsToken(t)) is { } malformed)
            throw new ArgumentException($"'{malformed}' is not a token (§4.3).", nameof(subjects));

        Container(root, "Subjects").ReplaceNodes(written.Select(s =>
        {
            var subject = new XElement(X("Subject"), s.Text.Trim());

            // §7.9 reads a subject with no type as a keyword, so a keyword
            // says nothing and the others say what they are.
            if (s.Type.Trim() is { Length: > 0 } type && type != "keyword")
                subject.SetAttributeValue("type", type);

            if ((string?)root.Attribute(XNamespace.Xml + "lang") is { } language)
                subject.SetAttributeValue(XNamespace.Xml + "lang", language);

            return subject;
        }));
    }

    /// <summary>Writes who and what the story is about (§7.10).</summary>
    private static void SetEntities(XElement root, IReadOnlyList<EntityEdit> entities)
    {
        EntityEdit[] written = [.. entities.Where(e => !string.IsNullOrWhiteSpace(e.Name))];

        if (written.Length == 0)
        {
            root.Element(X("Entities"))?.Remove();
            return;
        }

        Tokens(written.Select(e => e.Type).Concat(written.Select(e => e.Role)), nameof(entities), typeRequired: written.Select(e => e.Type));

        Container(root, "Entities").ReplaceNodes(written.Select(e =>
        {
            var entity = new XElement(X("Entity"), new XAttribute("type", e.Type.Trim()), Named(root, e.Name.Trim()));

            if (e.Role.Trim() is { Length: > 0 } role)
                entity.SetAttributeValue("role", role);

            return entity;
        }));
    }

    /// <summary>
    /// Writes how others classified the publication (§7.14), keeping the
    /// warnings beside them.
    /// </summary>
    /// <remarks>
    /// A scheme is somebody else's vocabulary — cero, esrb, pegi, a
    /// publisher's own — so nothing here checks what it holds beyond its
    /// being said at all: refusing a scheme this project has not heard of
    /// would make it the judge of a table it does not keep.
    /// </remarks>
    private static void SetRatings(XElement root, IReadOnlyList<RatingEdit> ratings)
    {
        RatingEdit[] written = [.. ratings.Where(r => !string.IsNullOrWhiteSpace(r.Scheme) || !string.IsNullOrWhiteSpace(r.Value))];
        XElement? section = root.Element(X("Ratings"));
        XElement[] warnings = [.. section?.Elements(X("ContentWarning")) ?? []];

        foreach (RatingEdit rating in written)
        {
            if (string.IsNullOrWhiteSpace(rating.Scheme) || string.IsNullOrWhiteSpace(rating.Value))
                throw new ArgumentException("A rating is a scheme and what that scheme says (§7.14); one of these has only half.", nameof(ratings));

            // §7.14: two capitals, as ISO 3166 writes a country.
            if (rating.Region.Trim() is { Length: > 0 } region && !Region().IsMatch(region))
                throw new ArgumentException($"'{region}' is no region; §7.14 wants two capitals.", nameof(ratings));
        }

        if (written.Length == 0 && warnings.Length == 0)
        {
            section?.Remove();
            return;
        }

        // §7.14 puts the ratings before the warnings, and a section out of
        // order is a section the schema refuses.
        Container(root, "Ratings").ReplaceNodes(written.Select(r =>
        {
            var rating = new XElement(X("Rating"), new XAttribute("scheme", r.Scheme.Trim()), new XAttribute("value", r.Value.Trim()));

            if (r.Region.Trim() is { Length: > 0 } region)
                rating.SetAttributeValue("region", region);

            return rating;
        }).Concat(warnings));
    }

    /// <summary>
    /// Writes what a reader is warned about (§7.14), keeping the ratings
    /// beside them.
    /// </summary>
    private static void SetWarnings(XElement root, IReadOnlyList<WarningEdit> warnings)
    {
        WarningEdit[] written = [.. warnings.Where(w => !string.IsNullOrWhiteSpace(w.Type))];
        XElement? section = root.Element(X("Ratings"));
        XElement[] ratings = [.. section?.Elements(X("Rating")) ?? []];

        if (written.Length == 0 && ratings.Length == 0)
        {
            section?.Remove();
            return;
        }

        Tokens(written.Select(w => w.Type), nameof(warnings), typeRequired: written.Select(w => w.Type));

        // §7.14 puts the ratings before the warnings, and a section out of
        // order is a section the schema refuses.
        Container(root, "Ratings").ReplaceNodes(ratings.Concat(written.Select(w =>
        {
            var warning = new XElement(X("ContentWarning"), new XAttribute("type", w.Type.Trim()));

            if (w.Text.Trim() is { Length: > 0 } text)
            {
                warning.Value = text;

                if ((string?)root.Attribute(XNamespace.Xml + "lang") is { } language)
                    warning.SetAttributeValue(XNamespace.Xml + "lang", language);
            }

            return warning;
        })));
    }

    /// <summary>Writes where else this publication is spoken of (§7.15).</summary>
    private static void SetLinks(XElement root, IReadOnlyList<LinkEdit> links)
    {
        LinkEdit[] written = [.. links.Where(l => !string.IsNullOrWhiteSpace(l.Href))];

        if (written.Length == 0)
        {
            root.Element(X("Links"))?.Remove();
            return;
        }

        Tokens(written.Select(l => l.Relation), nameof(links), typeRequired: written.Select(l => l.Relation));

        Container(root, "Links").ReplaceNodes(written.Select(l =>
        {
            var link = new XElement(X("Link"), new XAttribute("rel", l.Relation.Trim()), new XAttribute("href", l.Href.Trim()));

            if (l.Text.Trim() is { Length: > 0 } text)
                link.Value = text;

            return link;
        }));
    }

    /// <summary>Writes what may be done with the publication (§7.16).</summary>
    private static void SetRights(XElement root, RightsEdit rights)
    {
        string copyright = rights.Copyright.Trim();
        string license = rights.License.Trim();
        string statement = rights.Statement.Trim();

        if (copyright.Length == 0 && license.Length == 0 && statement.Length == 0)
        {
            root.Element(X("Rights"))?.Remove();
            return;
        }

        var written = new List<XElement>();

        // In the order §7.16 gives: copyright, licence, statement.
        if (copyright.Length > 0)
            written.Add(Named(root, copyright, "Copyright"));

        if (license.Length > 0)
            written.Add(new XElement(X("License"), new XAttribute("identifier", license)));

        if (statement.Length > 0)
            written.Add(Named(root, statement, "Statement"));

        Container(root, "Rights").ReplaceNodes(written);
    }

    /// <summary>
    /// Refuses anything that is not a token (§4.3), and anything that should
    /// have been one and is empty.
    /// </summary>
    private static void Tokens(IEnumerable<string> values, string parameter, IEnumerable<string> typeRequired)
    {
        if (values.Select(v => v.Trim()).FirstOrDefault(v => v.Length > 0 && !KomaTokens.IsToken(v)) is { } malformed)
            throw new ArgumentException($"'{malformed}' is not a token (§4.3).", parameter);

        if (typeRequired.Any(v => string.IsNullOrWhiteSpace(v)))
            throw new ArgumentException("§7 wants a type on each of these, and one of them has none.", parameter);
    }

    /// <summary>Writes what a publication says about itself (§7.7).</summary>
    private static void SetDescriptions(XElement root, IReadOnlyList<DescriptionEdit> descriptions)
    {
        DescriptionEdit[] written = [.. descriptions.Where(d => !string.IsNullOrWhiteSpace(d.Text))];

        if (written.Length == 0)
        {
            root.Element(X("Descriptions"))?.Remove();
            return;
        }

        if (written.Select(d => d.Type.Trim()).FirstOrDefault(t => t.Length == 0 || !KomaTokens.IsToken(t)) is { } malformed)
            throw new ArgumentException($"'{malformed}' is not a token, and §7.7 wants a type on every description (§4.3).", nameof(descriptions));

        Container(root, "Descriptions").ReplaceNodes(written.Select(d =>
        {
            var description = new XElement(X("Description"), new XAttribute("type", d.Type.Trim()), d.Text.Trim());

            if ((string?)root.Attribute(XNamespace.Xml + "lang") is { } language)
                description.SetAttributeValue(XNamespace.Xml + "lang", language);

            return description;
        }));
    }

    /// <summary>
    /// Writes one of the plain texts of §7.8 — the publisher, the imprint,
    /// the place — or takes it away.
    /// </summary>
    /// <remarks>
    /// The order §7.8 gives is kept by writing a missing element in its
    /// place, since a section out of order is a section the schema refuses.
    /// </remarks>
    private static void SetPublication(XElement root, string element, string text)
    {
        string value = text.Trim();
        XElement? section = root.Element(X("Publication"));

        if (value.Length == 0)
        {
            section?.Element(X(element))?.Remove();

            // §7.8 has no empty Publication to offer, so a section with
            // nothing left in it goes.
            if (section is { } emptied && !emptied.Elements().Any())
                emptied.Remove();

            return;
        }

        section = Container(root, "Publication");

        if (section.Element(X(element)) is { } existing)
        {
            existing.Value = value;
            return;
        }

        int rank = Array.IndexOf(PublicationOrder, element);
        XElement? next = section.Elements().FirstOrDefault(e => Array.IndexOf(PublicationOrder, e.Name.LocalName) > rank);

        if (next is null)
            section.Add(Named(root, value, element));
        else
            next.AddBeforeSelf(Named(root, value, element));
    }

    /// <summary>
    /// Writes when things happened to the publication (§7.8), keeping the
    /// modified date of §7.2.1 where it is.
    /// </summary>
    private static void SetDates(XElement root, IReadOnlyList<DateEdit> dates)
    {
        DateEdit[] written = [.. dates.Where(d => !string.IsNullOrWhiteSpace(d.Value))];
        XElement? section = root.Element(X("Publication"));

        // The release identity of §7.2.1 is stamped at every write and is
        // nobody's to move: it is carried over whatever the form says.
        XElement[] stamped = [.. section?.Elements(X("Date")).Where(d => (string?)d.Attribute("event") == "modified") ?? []];

        Tokens(written.Select(d => d.Event), nameof(dates), typeRequired: written.Select(d => d.Event));

        XElement[] all = [.. written.Select(d => new XElement(X("Date"), new XAttribute("event", d.Event.Trim()), d.Value.Trim())), .. stamped];

        foreach (XElement existing in section?.Elements(X("Date")).ToArray() ?? [])
            existing.Remove();

        if (all.Length == 0)
        {
            if (section is { } emptied && !emptied.Elements().Any())
                emptied.Remove();

            return;
        }

        Place(Container(root, "Publication"), "Date", all);
    }

    /// <summary>Writes how big the paper was (§7.8), or takes the size away.</summary>
    private static void SetPhysicalFormat(XElement root, PhysicalFormatEdit format)
    {
        string width = format.Width.Trim();
        string height = format.Height.Trim();
        XElement? section = root.Element(X("Publication"));

        if (width.Length == 0 && height.Length == 0)
        {
            section?.Element(X("PhysicalFormat"))?.Remove();

            if (section is { } emptied && !emptied.Elements().Any())
                emptied.Remove();

            return;
        }

        // §7.8 wants both sides: a width without a height describes nothing.
        if (width.Length == 0 || height.Length == 0)
            throw new ArgumentException("A physical format has a width and a height (§7.8).", nameof(format));

        var written = new XElement(
            X("PhysicalFormat"),
            new XAttribute("trim-width", width),
            new XAttribute("trim-height", height),
            new XAttribute("unit", "mm"));

        section?.Element(X("PhysicalFormat"))?.Remove();
        Place(Container(root, "Publication"), "PhysicalFormat", [written]);
    }

    /// <summary>
    /// Puts elements where §7.8 holds them, since a section out of order is a
    /// section the schema refuses.
    /// </summary>
    private static void Place(XElement section, string element, XElement[] written)
    {
        int rank = Array.IndexOf(PublicationOrder, element);
        XElement? next = section.Elements().FirstOrDefault(e => Array.IndexOf(PublicationOrder, e.Name.LocalName) > rank);

        if (next is null)
            section.Add(written);
        else
            next.AddBeforeSelf(written);
    }

    /// <summary>A name in the language the document is written in (§4.4).</summary>
    private static XElement Named(XElement root, string name, string element = "Name")
    {
        var written = new XElement(X(element), name);

        if ((string?)root.Attribute(XNamespace.Xml + "lang") is { } language)
            written.SetAttributeValue(XNamespace.Xml + "lang", language);

        return written;
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

        if (modified is not null)
        {
            modified.Value = stamp;
            return;
        }

        // At its rank and not at the end: §7.8 puts the dates before the
        // physical format, and a section out of order is a section the schema
        // refuses. Nothing showed it while no publication carried a format.
        Place(publication, "Date", [new XElement(X("Date"), new XAttribute("event", "modified"), stamp)]);
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
