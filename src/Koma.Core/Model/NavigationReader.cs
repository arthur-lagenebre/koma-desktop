using System.Collections.ObjectModel;
using System.Xml.Linq;
using Koma.Core.Packaging;
using Koma.Core.Rendering;

namespace Koma.Core.Model;

/// <summary>
/// A text of <c>nav.xml</c>, in the language §4.4 gives it.
/// </summary>
/// <param name="Text">Stripped of leading and trailing whitespace, as §4.3 requires of <c>Normalized</c>.</param>
/// <param name="Language">
/// The nearest <c>xml:lang</c>, else the content language of the metadata;
/// <see langword="null"/> when undetermined, which an empty <c>xml:lang</c> says outright.
/// </param>
public sealed record NavigationLabel(string Text, string? Language)
{
    /// <summary>
    /// The label to show a reader, from their languages in order of preference.
    /// </summary>
    /// <remarks>
    /// An exact tag wins over a shared primary language, so that a reader
    /// asking for <c>pt-PT</c> is not given <c>pt-BR</c> when both exist. With
    /// no match at all, the first label is still better than none: every
    /// label names the same target.
    /// </remarks>
    public static NavigationLabel? Choose(IReadOnlyList<NavigationLabel> labels, IEnumerable<string> languages)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(languages);

        foreach (string language in languages)
        {
            NavigationLabel? exact = labels.FirstOrDefault(l => string.Equals(l.Language, language, StringComparison.OrdinalIgnoreCase));

            if (exact is not null)
                return exact;

            NavigationLabel? related = labels.FirstOrDefault(l => l.Language is not null && string.Equals(Primary(l.Language), Primary(language), StringComparison.OrdinalIgnoreCase));

            if (related is not null)
                return related;
        }

        return labels.Count > 0 ? labels[0] : null;
    }

    private static string Primary(string tag) => tag.Split('-')[0];
}

/// <summary>One entry of the table of contents (§9.1).</summary>
public sealed record TocEntry(string Item, ReadOnlyCollection<NavigationLabel> Labels, ReadOnlyCollection<TocEntry> Children);

/// <summary>
/// One entry of the page list (§9.2).
/// </summary>
/// <param name="Side">
/// The half of a <c>page-span="2"</c> resource the label denotes, or
/// <see langword="null"/> for the whole resource.
/// </param>
public sealed record PageTarget(string Item, string Label, PhysicalSide? Side);

/// <summary>One landmark of a core type (§9.3).</summary>
public sealed record Landmark(string Type, string Item, ReadOnlyCollection<NavigationLabel> Labels);

/// <summary>
/// What <c>nav.xml</c> offers a reader (§9).
/// </summary>
/// <remarks>
/// Regions (§9.4) are not read: §16 lets a reading system omit guided region
/// navigation, and this one does, for now.
/// </remarks>
public sealed record PublicationNavigation(ReadOnlyCollection<TocEntry> TableOfContents, ReadOnlyCollection<PageTarget> PageList, ReadOnlyCollection<Landmark> Landmarks);

/// <summary>
/// Reads <c>nav.xml</c> (§9).
/// </summary>
/// <remarks>
/// Structure the model depends on is checked here and reported against the
/// schema, since that is where §17 places it. Targets are checked by
/// <see cref="CoreDocumentChecks.CheckNavigationTargets"/>, which predates
/// this reader and covers every section, regions included.
/// </remarks>
public static class NavigationReader
{
    private const string Namespace = "urn:koma:navigation";

    private static readonly XName XmlLang = XNamespace.Xml + "lang";

    /// <summary>The sections of §9, in the order it requires.</summary>
    private static readonly string[] Sections = ["TableOfContents", "PageList", "Landmarks", "Regions", "Extensions"];

    private static readonly HashSet<string> CoreLandmarkTypes = new(StringComparer.Ordinal)
    {
        "front-cover",
        "inner-cover",
        "title-page",
        "table-of-contents",
        "body-start",
        "story-start",
        "credits",
        "glossary",
        "appendix",
        "bonus",
        "preview",
        "back-cover"
    };

    /// <param name="contentLanguage">
    /// The content language of the metadata, which §4.4 makes the document
    /// language when <c>nav.xml</c> declares none.
    /// </param>
    public static PublicationNavigation? Read(XDocument document, string entryName, KomaVersion packageVersion, string? contentLanguage, List<ContainerViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(violations);

        XElement? root = document.Root;

        if (root is null || root.Name != Core("Navigation"))
        {
            violations.Add(Invalid(entryName, $"The root element is not Navigation in {Namespace} (§9)."));
            return null;
        }

        string? declared = root.Attribute("version")?.Value;

        if (!KomaVersion.TryParse(declared, out KomaVersion version) || version != packageVersion)
        {
            violations.Add(Invalid(entryName, $"Declares version '{declared}' where the container declares {packageVersion}; §4.2 requires every core document to agree."));
            return null;
        }

        if (!CheckSections(root, entryName, violations))
            return null;

        ReadOnlyCollection<TocEntry>? contents = ReadEntries(Section(root, "TableOfContents").Elements(Core("Entry")), contentLanguage, entryName, violations);
        ReadOnlyCollection<PageTarget>? pages = ReadPageList(Section(root, "PageList"), entryName, violations);
        ReadOnlyCollection<Landmark>? landmarks = ReadLandmarks(Section(root, "Landmarks"), contentLanguage, entryName, violations);

        if (contents is null || pages is null || landmarks is null)
            return null;

        return new PublicationNavigation(contents, pages, landmarks);
    }

    /// <summary>
    /// §9: the sections in their order, each at most once, and at least one
    /// that navigates: <c>Extensions</c> alone is not a navigation document.
    /// </summary>
    private static bool CheckSections(XElement root, string entryName, List<ContainerViolation> violations)
    {
        int last = -1;
        bool navigates = false;

        foreach (XElement section in root.Elements().Where(e => e.Name.NamespaceName == Namespace))
        {
            int index = Array.IndexOf(Sections, section.Name.LocalName);

            if (index < 0)
            {
                violations.Add(Invalid(entryName, $"Navigation has an unknown child, {section.Name.LocalName} (§9)."));
                return false;
            }

            if (index <= last)
            {
                violations.Add(Invalid(entryName, $"{section.Name.LocalName} is repeated or out of order; §9 requires {string.Join(", ", Sections)}, each at most once."));
                return false;
            }

            last = index;
            navigates |= index < 4;
        }

        if (!navigates)
            violations.Add(Invalid(entryName, "Navigation has none of TableOfContents, PageList, Landmarks and Regions (§9)."));

        return navigates;
    }

    private static ReadOnlyCollection<TocEntry>? ReadEntries(IEnumerable<XElement> elements, string? contentLanguage, string entryName, List<ContainerViolation> violations)
    {
        var entries = new List<TocEntry>();

        foreach (XElement element in elements)
        {
            string? item = element.Attribute("item")?.Value;
            ReadOnlyCollection<NavigationLabel>? labels = ReadLabels(element, contentLanguage, entryName, violations);

            if (labels is null)
                return null;

            if (item is null || labels.Count == 0)
            {
                violations.Add(Invalid(entryName, "Every Entry needs an item and at least one Label (§9.1)."));
                return null;
            }

            // Depth is bounded before this runs: the XML reader enforces the
            // nesting limit of §13.1 on the whole document.
            ReadOnlyCollection<TocEntry>? children = ReadEntries(element.Elements(Core("Entry")), contentLanguage, entryName, violations);

            if (children is null)
                return null;

            entries.Add(new TocEntry(item, labels, children));
        }

        return entries.AsReadOnly();
    }

    private static ReadOnlyCollection<PageTarget>? ReadPageList(IEnumerable<XElement> section, string entryName, List<ContainerViolation> violations)
    {
        var targets = new List<PageTarget>();

        foreach (XElement element in section.Elements(Core("PageTarget")))
        {
            string? item = element.Attribute("item")?.Value;
            string? label = element.Attribute("label")?.Value.Trim();
            string? side = element.Attribute("spread-position")?.Value;

            if (item is null || string.IsNullOrEmpty(label))
            {
                violations.Add(Invalid(entryName, "Every PageTarget needs an item and a non-empty label (§9.2)."));
                return null;
            }

            // Closed, and narrower than ItemRef/@spread-position: §9.2 admits
            // left and right only, and absence means the whole resource.
            if (side is not (null or "left" or "right"))
            {
                violations.Add(Invalid(entryName, $"PageTarget/@spread-position is '{side}'; §9.2 allows left or right only."));
                return null;
            }

            targets.Add(new PageTarget(item, label, side switch { "left" => PhysicalSide.Left, "right" => PhysicalSide.Right, _ => null }));
        }

        return targets.AsReadOnly();
    }

    private static ReadOnlyCollection<Landmark>? ReadLandmarks(IEnumerable<XElement> section, string? contentLanguage, string entryName, List<ContainerViolation> violations)
    {
        var landmarks = new List<Landmark>();
        var types = new HashSet<string>(StringComparer.Ordinal);
        var privateUse = new HashSet<string>(StringComparer.Ordinal);

        foreach (XElement element in section.Elements(Core("Landmark")))
        {
            string? type = element.Attribute("type")?.Value;
            string? item = element.Attribute("item")?.Value;

            if (type is null || item is null || !KomaTokens.IsToken(type))
            {
                violations.Add(Invalid(entryName, $"Every Landmark needs an item and a type that is a Token; found type '{type}' (§9.3, §4.3)."));
                return null;
            }

            ReadOnlyCollection<NavigationLabel>? labels = ReadLabels(element, contentLanguage, entryName, violations);

            if (labels is null)
                return null;

            // One per type, whatever the type: the rule is about the document
            // as written, before any fallback removes an entry from it.
            if (!types.Add(type))
            {
                violations.Add(new ContainerViolation(ContainerViolationCode.LandmarkDuplicateType, entryName, $"Two landmarks have the type '{type}' (§9.3)."));
                continue;
            }

            // §4.5.1: an unrecognised type drops the entry. A private-use one
            // is legal and noted once, as roles are, since the producer may not
            // expect it to vanish.
            if (!CoreLandmarkTypes.Contains(type))
            {
                if (KomaTokens.IsPrivateUse(type) && privateUse.Add(type))
                    violations.Add(new ContainerViolation(ContainerViolationCode.PrivateUseToken, entryName, $"'{type}' is a private-use landmark type; §4.5.1 ignores the landmark.") { Severity = ViolationSeverity.Warning });

                continue;
            }

            landmarks.Add(new Landmark(type, item, labels));
        }

        return landmarks.AsReadOnly();
    }

    private static ReadOnlyCollection<NavigationLabel>? ReadLabels(XElement owner, string? contentLanguage, string entryName, List<ContainerViolation> violations)
    {
        var labels = new List<NavigationLabel>();

        foreach (XElement label in owner.Elements(Core("Label")))
        {
            string text = label.Value.Trim();

            if (text.Length == 0)
            {
                violations.Add(Invalid(entryName, $"A Label of {owner.Name.LocalName} is empty (§9)."));
                return null;
            }

            labels.Add(new NavigationLabel(text, LanguageOf(label, contentLanguage)));
        }

        return labels.AsReadOnly();
    }

    /// <summary>
    /// §4.4: the nearest <c>xml:lang</c>, the root's included, else the
    /// content language. An empty one means undetermined, and stops the
    /// search there rather than falling through to an outer declaration.
    /// </summary>
    private static string? LanguageOf(XElement element, string? contentLanguage)
    {
        for (XElement? current = element; current is not null; current = current.Parent)
        {
            XAttribute? lang = current.Attribute(XmlLang);

            if (lang is not null)
                return lang.Value.Length == 0 ? null : lang.Value;
        }

        return contentLanguage;
    }

    private static IEnumerable<XElement> Section(XElement root, string name) => root.Elements(Core(name));

    private static XName Core(string name) => XName.Get(name, Namespace);

    private static ContainerViolation Invalid(string entryName, string message) => new(ContainerViolationCode.SchemaInvalidNavigation, entryName, message);
}
