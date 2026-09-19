using System.Xml.Linq;
using Koma.Core.Packaging;

namespace Koma.Core.Model;

/// <summary>
/// Checks that apply to a core document whatever it is, and the navigation
/// targets of §9.
/// </summary>
/// <remarks>
/// These are here rather than in <see cref="ManifestReader"/> because neither
/// depends on a document's shape. §4.6 governs the four extension points
/// identically, and §9 makes every navigation target an <c>IDREF</c> into the
/// spine whichever section carries it, so both are written once and applied to
/// whatever the package contains.
/// </remarks>
public static class CoreDocumentChecks
{
    private static readonly string[] CoreNamespaces =
    [
        "urn:koma:container",
        "urn:koma:metadata",
        "urn:koma:manifest",
        "urn:koma:navigation"
    ];

    /// <summary>
    /// §4.6: foreign content uses a foreign namespace and lives inside
    /// <c>Extensions</c>.
    /// </summary>
    /// <remarks>
    /// The check is on what sits inside an extension point, not on where
    /// foreign content sits: an element in no namespace at all is the case the
    /// corpus exercises, and it is the dangerous one, because a consumer that
    /// matched on local name alone would take it for core content.
    /// </remarks>
    public static void CheckExtensions(XDocument document, string entryName, List<ContainerViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(violations);

        if (document.Root is null)
            return;

        foreach (XElement extensions in document.Root.Descendants().Where(IsExtensionPoint))
        {
            foreach (XElement foreign in extensions.Descendants().Where(e => !IsForeign(e.Name.NamespaceName)))
                violations.Add(new ContainerViolation(ContainerViolationCode.UnnamespacedElementInExtensions, entryName, $"'{foreign.Name.LocalName}' inside Extensions is in {Describe(foreign.Name.NamespaceName)}; §4.6 requires a foreign namespace."));
        }
    }

    /// <summary>
    /// §9: every navigation target is an <c>IDREF</c> to an item in the spine.
    /// </summary>
    /// <remarks>
    /// Targets are matched by the presence of an <c>item</c> attribute rather
    /// than by element name. §9 states the rule once for the whole document and
    /// each section spells its target differently — <c>Entry</c>,
    /// <c>PageTarget</c>, <c>Landmark</c>, <c>Region</c> — so a check written
    /// per section would need revisiting whenever a section is added.
    /// </remarks>
    public static void CheckNavigationTargets(XDocument navigation, Manifest manifest, string entryName, List<ContainerViolation> violations)
    {
        if (violations.Any(v => v.Code is ContainerViolationCode.FrontCoverNotInSpine or ContainerViolationCode.SpineTargetMissing))
            return;

        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(violations);

        if (navigation.Root is null)
            return;

        var inSpine = new HashSet<string>(manifest.Spine.Select(r => r.Item), StringComparer.Ordinal);

        foreach (XElement element in navigation.Root.DescendantsAndSelf())
        {
            string? target = element.Attribute("item")?.Value;

            if (target is null || inSpine.Contains(target))
                continue;

            string reason = manifest.Item(target) is null ? "which no Item declares" : "which is not in the spine";

            violations.Add(new ContainerViolation(ContainerViolationCode.NavigationTargetOutsideSpine, entryName, $"{element.Name.LocalName} targets '{target}', {reason} (§9)."));
        }
    }

    private static bool IsExtensionPoint(XElement element) =>
        element.Name.LocalName == "Extensions" && CoreNamespaces.Contains(element.Name.NamespaceName, StringComparer.Ordinal);

    private static bool IsForeign(string ns) =>
        ns.Length > 0 && !CoreNamespaces.Contains(ns, StringComparer.Ordinal);

    private static string Describe(string ns) => ns.Length == 0 ? "no namespace" : $"'{ns}'";
}
