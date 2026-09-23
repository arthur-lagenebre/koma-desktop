using System.Collections.ObjectModel;
using Koma.Core.Packaging;
using Koma.Core.Rendering;
using System.Xml.Linq;

namespace Koma.Core.Model;

/// <summary>
/// One title of §7.3, in the language §4.4 gives it.
/// </summary>
/// <param name="Type">An open vocabulary; <c>main</c> is the one §7.3 requires.</param>
public sealed record PublicationTitle(string Text, string Type, string? Language)
{
    /// <summary>The type §7.3 requires exactly one title to carry.</summary>
    public const string Main = "main";
}

/// <summary>
/// The series of §7.5 a publication belongs to.
/// </summary>
/// <param name="Position">Text, as §7.5 keeps it: HS2 and 3.5 are volume numbers too.</param>
public sealed record PublicationSeries(string Name, string? Position, string? Total);

/// <summary>
/// What <c>metadata.xml</c> says that changes how a publication is read.
/// </summary>
/// <remarks>
/// A fraction of §7. The sections this reader does not touch — identifiers,
/// contributors, rights, provenance — describe the work rather than its
/// presentation, and nothing needs them yet. Titles are here because a
/// library lists publications by name.
/// </remarks>
public sealed record PublicationMetadata
{
    public required KomaVersion Version { get; init; }

    /// <summary>§7.11. Required, with no default: neither value is safe to assume.</summary>
    public required ReadingDirection Direction { get; init; }

    /// <summary>§7.11, defaulting to <see cref="SpreadPolicy.Auto"/>.</summary>
    public SpreadPolicy Spread { get; init; } = SpreadPolicy.Auto;

    /// <summary>§7.13, as declared.</summary>
    public required ReadOnlyCollection<string> AccessibilityHazards { get; init; }

    /// <summary>§7.14 content warnings, which §7.13 makes hazards agree with.</summary>
    public required ReadOnlyCollection<string> ContentWarnings { get; init; }

    /// <summary>§7.3 titles, of which exactly one is of type <c>main</c>.</summary>
    public required ReadOnlyCollection<PublicationTitle> Titles { get; init; }

    /// <summary>
    /// The title a publication is listed under.
    /// </summary>
    /// <remarks>
    /// §7.3 allows exactly one of type <c>main</c>, in one language, and the
    /// reader refuses metadata without it, so there is always one to find and
    /// never a choice to make between several.
    /// </remarks>
    public PublicationTitle MainTitle => Titles.First(t => t.Type == PublicationTitle.Main);

    /// <summary>The first collection of type <c>series</c>, if there is one (§7.5).</summary>
    public PublicationSeries? Series { get; init; }

    /// <summary>
    /// The first <c>Language role="content"</c> (§7.4), which §4.4 makes the
    /// language of any core document that declares none of its own.
    /// </summary>
    public string? ContentLanguage { get; init; }
}

/// <summary>
/// Reads <c>metadata.xml</c> (§7).
/// </summary>
public static class MetadataReader
{
    private const string Namespace = "urn:koma:metadata";

    /// <summary>
    /// Content warnings and the hazard that contradicts each, per §7.13.
    /// </summary>
    private static readonly Dictionary<string, string> ContradictedBy = new(StringComparer.Ordinal)
    {
        ["flashing-images"] = "no-flashing-hazard",
        ["motion-simulation"] = "no-motion-simulation-hazard",
        ["sound"] = "no-sound-hazard"
    };

    public static PublicationMetadata? Read(XDocument document, string entryName, KomaVersion packageVersion, List<ContainerViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(violations);

        XElement? root = document.Root;

        if (root is null || root.Name != XName.Get("Metadata", Namespace))
        {
            violations.Add(Invalid(entryName, $"The root element is not Metadata in {Namespace} (§7)."));
            return null;
        }

        string? declared = root.Attribute("version")?.Value;

        if (!KomaVersion.TryParse(declared, out KomaVersion version) || version != packageVersion)
        {
            violations.Add(Invalid(entryName, $"Declares version '{declared}' where the container declares {packageVersion}; §4.2 requires every core document to agree."));
            return null;
        }

        XElement? reading = root.Element(XName.Get("Reading", Namespace));

        if (reading is null)
        {
            violations.Add(Invalid(entryName, "Metadata has no Reading (§7.11)."));
            return null;
        }

        // §7.11 gives direction no default, and says why: no default is safe
        // for a format carrying both Western and Japanese reading orders. A
        // reader that guessed would silently paginate half the corpus backwards.
        string? direction = reading.Attribute("direction")?.Value;

        if (direction is not ("ltr" or "rtl"))
        {
            violations.Add(Invalid(entryName, $"Reading/@direction is '{direction}'; §7.11 requires ltr or rtl and gives no default."));
            return null;
        }

        string spread = reading.Attribute("spread")?.Value ?? "auto";

        if (spread is not ("none" or "auto" or "force"))
        {
            violations.Add(Invalid(entryName, $"Reading/@spread is '{spread}', outside the closed vocabulary of §7.11."));
            return null;
        }

        string? contentLanguage = ContentLanguageOf(root);
        ReadOnlyCollection<PublicationTitle>? titles = ReadTitles(root, contentLanguage, entryName, violations);

        if (titles is null)
            return null;

        string[] hazards = [.. Values(root, "Accessibility", "AccessibilityHazard")];
        string[] warnings = [.. Attributes(root, "Ratings", "ContentWarning", "type")];

        CheckHazards(hazards, warnings, entryName, violations);

        // §7.13: saying nothing about accessibility is not a fault, but it is
        // worth saying that nothing was said.
        if (root.Element(XName.Get("Accessibility", Namespace)) is null)
            violations.Add(new ContainerViolation(ContainerViolationCode.NoPublicationAccessibility, entryName, "The publication declares no accessibility metadata (§7.13).") { Severity = ViolationSeverity.Warning });

        return new PublicationMetadata
        {
            Version = version,
            Direction = direction == "rtl" ? ReadingDirection.RightToLeft : ReadingDirection.LeftToRight,
            Spread = spread switch { "none" => SpreadPolicy.None, "force" => SpreadPolicy.Force, _ => SpreadPolicy.Auto },
            AccessibilityHazards = hazards.AsReadOnly(),
            ContentWarnings = warnings.AsReadOnly(),
            Titles = titles,
            Series = SeriesOf(root),
            ContentLanguage = contentLanguage
        };
    }

    /// <summary>
    /// §7.13: a hazard and its <c>no-…-hazard</c> counterpart cannot both hold,
    /// and a declared hazard must agree with the content warnings of §7.14.
    /// </summary>
    private static void CheckHazards(string[] hazards, string[] warnings, string entryName, List<ContainerViolation> violations)
    {
        foreach (string hazard in hazards)
        {
            string opposite = hazard.StartsWith("no-", StringComparison.Ordinal) && hazard.EndsWith("-hazard", StringComparison.Ordinal) ? hazard[3..^7] : $"no-{hazard}-hazard";

            if (hazards.Contains(opposite, StringComparer.Ordinal))
            {
                violations.Add(new ContainerViolation(ContainerViolationCode.AccessibilityHazardConflict, entryName, $"'{hazard}' and '{opposite}' are both declared (§7.13)."));
                return;
            }
        }

        foreach (string warning in warnings)
        {
            if (ContradictedBy.TryGetValue(warning, out string? contradicted) && hazards.Contains(contradicted, StringComparer.Ordinal))
                violations.Add(new ContainerViolation(ContainerViolationCode.AccessibilityHazardConflict, entryName, $"The publication declares '{contradicted}' and a '{warning}' content warning (§7.13)."));
        }
    }

    private static IEnumerable<string> Values(XElement root, string section, string child) => root.Elements(XName.Get(section, Namespace)).Elements(XName.Get(child, Namespace)).Select(e => e.Value.Trim());

    private static IEnumerable<string> Attributes(XElement root, string section, string child, string attribute) => root.Elements(XName.Get(section, Namespace)).Elements(XName.Get(child, Namespace)).Select(e => e.Attribute(attribute)?.Value).Where(v => v is not null)!;

    /// <summary>
    /// §7.3: every title has a type, and exactly one of them is the main one.
    /// </summary>
    /// <remarks>
    /// The count is the part no schema expresses, and the part a library
    /// leans on: a publication has one name to be listed under, whatever
    /// else it is also called.
    /// </remarks>
    private static ReadOnlyCollection<PublicationTitle>? ReadTitles(XElement root, string? contentLanguage, string entryName, List<ContainerViolation> violations)
    {
        var titles = new List<PublicationTitle>();

        foreach (XElement element in root.Elements(XName.Get("Titles", Namespace)).Elements(XName.Get("Title", Namespace)))
        {
            string? type = element.Attribute("type")?.Value;
            string text = element.Value.Trim();

            if (type is null || text.Length == 0)
            {
                violations.Add(Invalid(entryName, "Every Title needs a type and text (§7.3)."));
                return null;
            }

            titles.Add(new PublicationTitle(text, type, KomaLanguage.Of(element, contentLanguage)));
        }

        int main = titles.Count(t => t.Type == PublicationTitle.Main);

        if (main != 1)
        {
            violations.Add(Invalid(entryName, $"{main} titles are of type main; §7.3 requires exactly one."));
            return null;
        }

        return titles.AsReadOnly();
    }

    /// <remarks>
    /// The first series only: a publication may sit in several collections,
    /// but a shelf files a volume in one place, and the first is the one its
    /// producer put first.
    /// </remarks>
    private static PublicationSeries? SeriesOf(XElement root)
    {
        XElement? series = root.Elements(XName.Get("Collections", Namespace)).Elements(XName.Get("Collection", Namespace)).FirstOrDefault(c => (string?)c.Attribute("type") == "series");
        string? name = series?.Element(XName.Get("Name", Namespace))?.Value.Trim();

        return string.IsNullOrEmpty(name) ? null : new PublicationSeries(name, (string?)series!.Attribute("position"), (string?)series.Attribute("total"));
    }

    private static string? ContentLanguageOf(XElement root) => root.Elements(XName.Get("Languages", Namespace)).Elements(XName.Get("Language", Namespace)).FirstOrDefault(l => l.Attribute("role")?.Value == "content")?.Value.Trim();

    private static ContainerViolation Invalid(string entryName, string message) => new(ContainerViolationCode.SchemaInvalidMetadata, entryName, message);
}
