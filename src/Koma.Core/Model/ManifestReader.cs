using System.Collections.ObjectModel;
using System.Globalization;
using System.Xml.Linq;
using Koma.Core.Packaging;
using Koma.Core.Rendering;

namespace Koma.Core.Model;

/// <summary>
/// Reads <c>manifest.xml</c> and checks it against itself (§8).
/// </summary>
/// <remarks>
/// <para>
/// Everything here is decided from the manifest alone. The checks that need
/// another core document — a navigation target outside the spine, an
/// accessibility hazard contradicting the metadata — belong elsewhere, and so
/// do the ones that need the archive, such as an item whose resource is absent.
/// Keeping that line drawn is what lets this run on a document rather than on a
/// package.
/// </para>
/// <para>
/// This is not schema validation. §17 puts the schemas at layer 2 and this at
/// layer 3, and without a RELAX NG validator in .NET the structural checks
/// below stand in for the former where the two overlap. They are reported as
/// <c>schema-invalid:manifest</c> for that reason.
/// </para>
/// </remarks>
public static class ManifestReader
{
    private const string Namespace = "urn:koma:manifest";
    private const string FrontCover = "front-cover";
    private const string PagesPrefix = "pages/";

    private static readonly string[] PageMediaTypes = ["image/jpeg", "image/png", "image/webp"];

    /// <summary>
    /// Reads a manifest document.
    /// </summary>
    /// <param name="document">The parsed <c>manifest.xml</c>.</param>
    /// <param name="entryName">Its path, for the violations.</param>
    /// <param name="packageVersion">The version <c>container.xml</c> declared.</param>
    /// <param name="violations">Receives every defect found; not cleared.</param>
    /// <returns>The manifest, or <see langword="null"/> when it could not be read.</returns>
    public static Manifest? Read(XDocument document, string entryName, KomaVersion packageVersion, List<ContainerViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(violations);

        XElement? root = document.Root;

        if (root is null || root.Name != XName.Get("Manifest", Namespace))
        {
            violations.Add(Invalid(entryName, $"The root element is not Manifest in {Namespace} (§8)."));
            return null;
        }

        // §4.2: every core document declares the same version. A disagreement
        // is a defective publication, not an unsupported one: the portal of
        // §5.0 has already accepted the package on what container.xml said.
        string? declared = root.Attribute("version")?.Value;

        if (!KomaVersion.TryParse(declared, out KomaVersion version))
        {
            violations.Add(Invalid(entryName, $"'{declared}' is not a version of the form major.minor (§5.1)."));
            return null;
        }

        if (version != packageVersion)
        {
            violations.Add(Invalid(entryName, $"Declares {version} where the container declares {packageVersion}; §4.2 requires every core document to agree."));
            return null;
        }

        if (!HasFixedPath(root, "metadata", CorePaths.Metadata, required: true, entryName, violations))
            return null;

        if (!HasFixedPath(root, "navigation", CorePaths.Navigation, required: false, entryName, violations))
            return null;

        List<ManifestItem>? items = ReadItems(root, entryName, violations);

        if (items is null)
            return null;

        List<SpineItemRef>? spine = ReadSpine(root, entryName, violations);

        if (spine is null)
            return null;

        var manifest = new Manifest
        {
            Version = version,
            DeclaresNavigation = root.Attribute("navigation") is not null,
            Items = items.AsReadOnly(),
            Spine = spine.AsReadOnly()
        };

        CheckSpineAgainstItems(manifest, entryName, violations);
        CheckFrontCover(manifest, entryName, violations);
        CheckResourcesInSpine(manifest, entryName, violations);
        CheckPrivateUseTokens(manifest, entryName, violations);

        return manifest;
    }

    private static List<ManifestItem>? ReadItems(XElement root, string entryName, List<ContainerViolation> violations)
    {
        XElement? resources = root.Element(XName.Get("Resources", Namespace));

        if (resources is null)
        {
            violations.Add(Invalid(entryName, "Manifest has no Resources (§8)."));
            return null;
        }

        var items = new List<ManifestItem>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (XElement element in resources.Elements(XName.Get("Item", Namespace)))
        {
            ManifestItem? item = ReadItem(element, entryName, violations);

            if (item is null)
                return null;

            if (!ids.Add(item.Id))
            {
                violations.Add(Invalid(entryName, $"Item id '{item.Id}' is declared more than once; §4.3 makes it an ID."));
                return null;
            }

            items.Add(item);
        }

        return items;
    }

    private static ManifestItem? ReadItem(XElement element, string entryName, List<ContainerViolation> violations)
    {
        string? id = element.Attribute("id")?.Value;
        string? href = element.Attribute("href")?.Value;
        string? mediaType = element.Attribute("media-type")?.Value;

        if (string.IsNullOrEmpty(id))
        {
            violations.Add(Invalid(entryName, "An Item has no id (§8.1)."));
            return null;
        }

        if (!IsPath(href))
        {
            violations.Add(Invalid(entryName, $"Item '{id}' has an href that is not a Path (§4.3)."));
            return null;
        }

        // §8.1: every KOMA 0.9 Item references an image below pages/.
        if (!href!.StartsWith(PagesPrefix, StringComparison.Ordinal))
        {
            violations.Add(Invalid(entryName, $"Item '{id}' references '{href}', which is not below {PagesPrefix} (§8.1)."));
            return null;
        }

        if (mediaType is null || !PageMediaTypes.Contains(mediaType, StringComparer.Ordinal))
        {
            violations.Add(Invalid(entryName, $"Item '{id}' declares media type '{mediaType}', outside the closed vocabulary of §8.1."));
            return null;
        }

        if (!TryPositiveInteger(element.Attribute("width")?.Value, out int width) || !TryPositiveInteger(element.Attribute("height")?.Value, out int height))
        {
            violations.Add(Invalid(entryName, $"Item '{id}' has width or height that is not a PositiveInteger (§4.3)."));
            return null;
        }

        string[]? roles = ReadTokenList(element.Attribute("roles")?.Value, id, "roles", entryName, violations);

        if (roles is null)
            return null;

        int pageSpan = 1;
        string? span = element.Attribute("page-span")?.Value;

        if (span is not null && span != "1" && span != "2")
        {
            violations.Add(Invalid(entryName, $"Item '{id}' declares page-span '{span}'; §8.5 allows 1 or 2."));
            return null;
        }

        if (span == "2")
            pageSpan = 2;

        XElement? accessibility = element.Element(XName.Get("Accessibility", Namespace));
        bool decorative = accessibility?.Attribute("decorative")?.Value == "true";

        if (decorative && accessibility!.Element(XName.Get("AlternativeText", Namespace)) is not null)
            violations.Add(new ContainerViolation(ContainerViolationCode.DecorativeWithAlternativeText, entryName, $"Item '{id}' is decorative and carries AlternativeText (§8.7)."));

        return new ManifestItem
        {
            Id = id,
            Href = href,
            MediaType = mediaType,
            Width = width,
            Height = height,
            Roles = roles,
            PageSpan = pageSpan,
            BackgroundColor = element.Attribute("background-color")?.Value,
            Sha256 = element.Element(XName.Get("Checksum", Namespace))?.Value.Trim(),
            IsDecorative = decorative
        };
    }

    private static List<SpineItemRef>? ReadSpine(XElement root, string entryName, List<ContainerViolation> violations)
    {
        XElement? spine = root.Element(XName.Get("Spine", Namespace));

        if (spine is null)
        {
            violations.Add(Invalid(entryName, "Manifest has no Spine (§8.8)."));
            return null;
        }

        var entries = new List<SpineItemRef>();

        foreach (XElement element in spine.Elements(XName.Get("ItemRef", Namespace)))
        {
            string? item = element.Attribute("item")?.Value;

            if (string.IsNullOrEmpty(item))
            {
                violations.Add(Invalid(entryName, "An ItemRef has no item (§8.8)."));
                return null;
            }

            string position = element.Attribute("spread-position")?.Value ?? "auto";

            if (position is not ("auto" or "left" or "right" or "center"))
            {
                violations.Add(Invalid(entryName, $"ItemRef '{item}' declares spread-position '{position}', outside the closed vocabulary of §8.8."));
                return null;
            }

            entries.Add(new SpineItemRef { Item = item, SpreadPosition = ToSpreadPosition(position) });
        }

        return entries;
    }

    /// <summary>
    /// The spine against the resources it points at (§8.8, §8.5).
    /// </summary>
    private static void CheckSpineAgainstItems(Manifest manifest, string entryName, List<ContainerViolation> violations)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (SpineItemRef reference in manifest.Spine)
        {
            ManifestItem? item = manifest.Item(reference.Item);

            if (item is null)
            {
                violations.Add(new ContainerViolation(ContainerViolationCode.SpineTargetMissing, entryName, $"The spine references '{reference.Item}', which no Item declares (§8.8)."));
                continue;
            }

            if (!seen.Add(reference.Item))
                violations.Add(new ContainerViolation(ContainerViolationCode.SpineDuplicateItem, entryName, $"Item '{reference.Item}' appears more than once in the spine (§8.8)."));

            // §8.8. A two-page image has no side to be pinned to: it occupies
            // both, and §10.3 makes it full before a side could apply.
            if (item.PageSpan == 2 && reference.SpreadPosition is SpreadPosition.Left or SpreadPosition.Right)
                violations.Add(new ContainerViolation(ContainerViolationCode.Span2SpreadPosition, entryName, $"Item '{reference.Item}' spans two pages and is pinned to a side (§8.8)."));
        }
    }

    /// <summary>
    /// Exactly one front cover, in the spine, and first by preference (§8.4).
    /// </summary>
    private static void CheckFrontCover(Manifest manifest, string entryName, List<ContainerViolation> violations)
    {
        // §8.4 says the core token, so a private-use token naming itself a
        // cover does not count and the publication has none.
        ManifestItem[] covers = [.. manifest.Items.Where(i => i.Roles.Contains(FrontCover, StringComparer.Ordinal))];

        if (covers.Length == 0)
        {
            violations.Add(new ContainerViolation(ContainerViolationCode.FrontCoverMissing, entryName, $"No Item carries the core role '{FrontCover}' (§8.4)."));
            return;
        }

        if (covers.Length > 1)
        {
            violations.Add(new ContainerViolation(ContainerViolationCode.FrontCoverDuplicate, entryName, $"{covers.Length} Items carry the core role '{FrontCover}'; §8.4 requires exactly one."));
            return;
        }

        string cover = covers[0].Id;
        int at = IndexInSpine(manifest, cover);

        if (at < 0)
        {
            violations.Add(new ContainerViolation(ContainerViolationCode.FrontCoverNotInSpine, entryName, $"The front cover '{cover}' is not in the spine (§8.4)."));
            return;
        }

        if (at > 0)
            violations.Add(new ContainerViolation(ContainerViolationCode.FrontCoverNotFirst, entryName, $"The front cover '{cover}' is entry {at + 1} of the spine; §8.4 recommends the first.") { Severity = ViolationSeverity.Warning });
    }

    /// <summary>
    /// §8.8 allows a resource outside the spine and asks that it be noticed.
    /// </summary>
    private static void CheckResourcesInSpine(Manifest manifest, string entryName, List<ContainerViolation> violations)
    {
        if (violations.Any(v => v.Code is ContainerViolationCode.SpineTargetMissing or ContainerViolationCode.FrontCoverNotInSpine))
            return;

        var inSpine = new HashSet<string>(manifest.Spine.Select(r => r.Item), StringComparer.Ordinal);

        foreach (ManifestItem item in manifest.Items.Where(i => !inSpine.Contains(i.Id)))
            violations.Add(new ContainerViolation(ContainerViolationCode.ResourceOutsideSpine, entryName, $"Item '{item.Id}' is not in the spine and is not a reading resource (§8.8).") { Severity = ViolationSeverity.Warning });
    }

    /// <summary>
    /// §4.5: a private-use token is legitimate and a reader MUST NOT reject a
    /// document for carrying one.
    /// </summary>
    /// <remarks>
    /// Noted rather than faulted, because §4.5 also says such a token has no
    /// globally defined meaning: this reader will fall back on §4.5.1 and show
    /// the page as though the token were absent, which the producer may not
    /// expect. Reported once per distinct token, not once per item, since it
    /// is the vocabulary that is private and not each use of it.
    /// </remarks>
    private static void CheckPrivateUseTokens(Manifest manifest, string entryName, List<ContainerViolation> violations)
    {
        IEnumerable<string> tokens = manifest.Items.SelectMany(i => i.Roles).Where(t => t.StartsWith("x-", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal);

        foreach (string token in tokens)
            violations.Add(new ContainerViolation(ContainerViolationCode.PrivateUseToken, entryName, $"'{token}' is a private-use role token; §4.5.1 applies and its meaning is the producer's alone.") { Severity = ViolationSeverity.Warning });
    }

    private static int IndexInSpine(Manifest manifest, string id)
    {
        for (int i = 0; i < manifest.Spine.Count; i++)
        {
            if (manifest.Spine[i].Item == id)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// A <c>TokenList</c> per §4.3.
    /// </summary>
    /// <remarks>
    /// The separator is <c>;</c>, not whitespace. §4.3 explains the choice —
    /// one convention for lists whose values may later admit spaces — and a
    /// reader arriving from formats that split on space gets it wrong in a way
    /// nothing reports: <c>front-cover;bonus</c> becomes a single token, the
    /// core role no longer matches, and the publication quietly has no cover.
    /// </remarks>
    private static string[]? ReadTokenList(string? value, string id, string attribute, string entryName, List<ContainerViolation> violations)
    {
        if (value is null)
            return [];

        if (value.Length == 0 || value.Any(char.IsWhiteSpace))
        {
            violations.Add(Invalid(entryName, $"Item '{id}' has a {attribute} value that is empty or contains whitespace (§4.3)."));
            return null;
        }

        string[] tokens = value.Split(';');
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string token in tokens)
        {
            if (!KomaTokens.IsToken(token))
            {
                violations.Add(Invalid(entryName, $"Item '{id}' has '{token}' in {attribute}, which is not a Token (§4.3)."));
                return null;
            }

            if (!seen.Add(token))
            {
                violations.Add(new ContainerViolation(ContainerViolationCode.TokenListDuplicate, entryName, $"Item '{id}' repeats the token '{token}' in {attribute} (§4.3)."));
                return null;
            }
        }

        return tokens;
    }

    /// <summary>
    /// An attribute naming a core document, which §1 allows one value only.
    /// </summary>
    private static bool HasFixedPath(XElement root, string name, string path, bool required, string entryName, List<ContainerViolation> violations)
    {
        string? value = root.Attribute(name)?.Value;

        if (value == path || (value is null && !required))
            return true;

        string message = value is null ? $"Manifest/@{name} is required (§8)." : $"Manifest/@{name} is '{value}'; §8 requires {path}.";
        violations.Add(Invalid(entryName, message));

        return false;
    }

    /// <summary>
    /// A <c>Path</c> per §4.3: a package-relative entry name, never
    /// percent-decoded, so the characters that would make it look like a URL
    /// reference are forbidden outright.
    /// </summary>
    private static bool IsPath(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        if (value.AsSpan().IndexOfAny('%', '?', '#') >= 0)
            return false;

        return KomaEntryName.TryValidate(value, out _);
    }

    /// <summary>
    /// §4.3, with the lexical strictness of Integer: no sign, no leading zeros.
    /// </summary>
    private static bool TryPositiveInteger(string? value, out int number)
    {
        number = 0;

        if (string.IsNullOrEmpty(value) || value[0] == '0')
            return false;

        foreach (char c in value)
        {
            if (c is < '0' or > '9')
                return false;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;
    }

    private static SpreadPosition ToSpreadPosition(string value) => value switch
    {
        "left" => SpreadPosition.Left,
        "right" => SpreadPosition.Right,
        "center" => SpreadPosition.Center,
        _ => SpreadPosition.Auto
    };

    private static ContainerViolation Invalid(string entryName, string message) => new(ContainerViolationCode.SchemaInvalidManifest, entryName, message);
}
