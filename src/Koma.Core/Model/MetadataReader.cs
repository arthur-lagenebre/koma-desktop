using System.Collections.ObjectModel;
using Koma.Core.Packaging;
using Koma.Core.Rendering;
using System.Xml.Linq;

namespace Koma.Core.Model;

/// <summary>
/// What <c>metadata.xml</c> says that changes how a publication is read.
/// </summary>
/// <remarks>
/// A fraction of §7. The sections this reader does not touch — identifiers,
/// titles, contributors, rights, provenance — describe the work rather than its
/// presentation, and nothing in the reader needs them yet. They will be added
/// when something asks for them, not before.
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

        string[] hazards = [.. Values(root, "Accessibility", "AccessibilityHazard")];
        string[] warnings = [.. Attributes(root, "Ratings", "ContentWarning", "type")];

        CheckHazards(hazards, warnings, entryName, violations);

        return new PublicationMetadata
        {
            Version = version,
            Direction = direction == "rtl" ? ReadingDirection.RightToLeft : ReadingDirection.LeftToRight,
            Spread = spread switch { "none" => SpreadPolicy.None, "force" => SpreadPolicy.Force, _ => SpreadPolicy.Auto },
            AccessibilityHazards = hazards.AsReadOnly(),
            ContentWarnings = warnings.AsReadOnly(),
            ContentLanguage = root.Elements(XName.Get("Languages", Namespace)).Elements(XName.Get("Language", Namespace)).FirstOrDefault(l => l.Attribute("role")?.Value == "content")?.Value.Trim()
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

    private static ContainerViolation Invalid(string entryName, string message) => new(ContainerViolationCode.SchemaInvalidMetadata, entryName, message);
}
