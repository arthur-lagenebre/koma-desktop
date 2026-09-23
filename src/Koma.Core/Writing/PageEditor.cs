using System.Xml.Linq;
using Koma.Core.Model;
using Koma.Core.Rendering;

namespace Koma.Core.Writing;

/// <summary>
/// What an edit changes about one page. A <see langword="null"/> field is
/// left as it is.
/// </summary>
/// <param name="Roles">The roles of §8.4, at least one.</param>
/// <param name="PageSpan">1 for a page, 2 for a page drawn across a spread (§8.5).</param>
/// <param name="SpreadPosition">Where the page sits in its spread (§8.8).</param>
/// <param name="AlternativeText">The text of §8.7; empty removes it.</param>
/// <param name="Decorative">Whether the page carries no information of its own (§8.7).</param>
public sealed record PageEdit(
    IReadOnlyList<string>? Roles = null,
    int? PageSpan = null,
    SpreadPosition? SpreadPosition = null,
    string? AlternativeText = null,
    bool? Decorative = null);

/// <summary>
/// Edits what the manifest says about one page, and keeps the navigation in
/// step with it.
/// </summary>
/// <remarks>
/// <para>
/// The roles decide the landmarks (§9.3), so a page that becomes the cover or
/// the start of the story moves its landmark with it. Without that, a package
/// would say one thing in the manifest and another in its navigation.
/// </para>
/// <para>
/// §8.4 allows exactly one front cover: naming a new one demotes the old to
/// <c>inner-cover</c>, rather than refusing an edit whose intent is plain.
/// </para>
/// </remarks>
public static class PageEditor
{
    private const string Manifest = "urn:koma:manifest";
    private const string Navigation = "urn:koma:navigation";

    private const string FrontCover = "front-cover";
    private const string InnerCover = "inner-cover";
    private const string Story = "story";

    /// <summary>The roles that make a landmark of the same name, in the order §9.3 lists them.</summary>
    private static readonly string[] LandmarkRoles = [FrontCover, InnerCover, "title-page", "back-cover"];

    /// <summary>What the manifest says about a page now: the values an editing form starts from.</summary>
    public static PageEdit Read(XDocument manifest, string item)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(item);

        XElement found = Item(manifest, item);
        XElement? accessibility = found.Element(XName.Get("Accessibility", Manifest));

        return new PageEdit(
            [.. Tokens(found)],
            (string?)found.Attribute("page-span") == "2" ? 2 : 1,
            Position((string?)Reference(manifest, item).Attribute("spread-position")),
            accessibility?.Element(XName.Get("AlternativeText", Manifest))?.Value.Trim() ?? string.Empty,
            (string?)accessibility?.Attribute("decorative") == "true");
    }

    /// <summary>
    /// The edited manifest, and the navigation with its landmarks in step.
    /// The documents given are not changed.
    /// </summary>
    /// <exception cref="ArgumentException">A value §8 does not allow.</exception>
    public static (XDocument Manifest, XDocument? Navigation) Apply(XDocument manifest, XDocument? navigation, string item, PageEdit edit)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(edit);

        var edited = new XDocument(manifest);
        XElement page = Item(edited, item);

        if (edit.Roles is { } roles)
            SetRoles(edited, page, roles);

        if (edit.PageSpan is { } span)
            SetSpan(page, span);

        if (edit.SpreadPosition is { } position)
            SetPosition(Reference(edited, item), position);

        if (edit.AlternativeText is not null || edit.Decorative is not null)
            SetAccessibility(page, edit);

        // §8.8: a page drawn across a spread fills it, so it cannot be pinned
        // to a side. Checked on the result, since either half of it may have
        // arrived in this edit or been there already.
        if ((string?)page.Attribute("page-span") == "2" && (string?)Reference(edited, item).Attribute("spread-position") is { } pinned && pinned != "auto")
            throw new ArgumentException($"A page of span 2 fills its spread and cannot sit on the {pinned} (§8.8).", nameof(edit));

        return (edited, navigation is null ? null : Landmarks(navigation, edited));
    }

    private static void SetRoles(XDocument manifest, XElement page, IReadOnlyList<string> roles)
    {
        string[] wanted = [.. roles.Select(r => r.Trim()).Where(r => r.Length > 0)];

        if (wanted.Length == 0)
            throw new ArgumentException("A page carries at least one role (§8.4).", nameof(roles));

        if (wanted.FirstOrDefault(r => !KomaTokens.IsToken(r)) is { } malformed)
            throw new ArgumentException($"'{malformed}' is not a token (§4.3).", nameof(roles));

        if (wanted.Length != wanted.Distinct(StringComparer.Ordinal).Count())
            throw new ArgumentException("A role is repeated (§4.3).", nameof(roles));

        // §8.4 allows one front cover: the page that had it takes the inner
        // cover instead, which is what it has become.
        if (wanted.Contains(FrontCover, StringComparer.Ordinal))
        {
            foreach (XElement other in Items(manifest).Where(i => i != page && Tokens(i).Contains(FrontCover, StringComparer.Ordinal)))
                other.SetAttributeValue("roles", string.Join(' ', Tokens(other).Select(r => r == FrontCover ? InnerCover : r).Distinct(StringComparer.Ordinal)));
        }

        page.SetAttributeValue("roles", string.Join(' ', wanted));
    }

    private static void SetSpan(XElement page, int span)
    {
        if (span is not (1 or 2))
            throw new ArgumentException($"A page spans one or two pages, not {span} (§8.5).", nameof(span));

        // Written only when it is 2, as §8.5 gives 1 as the default.
        page.SetAttributeValue("page-span", span == 2 ? "2" : null);
    }

    private static void SetPosition(XElement reference, SpreadPosition position)
    {
        reference.SetAttributeValue("spread-position", position switch
        {
            Rendering.SpreadPosition.Left => "left",
            Rendering.SpreadPosition.Right => "right",
            Rendering.SpreadPosition.Center => "center",
            _ => null
        });
    }

    private static void SetAccessibility(XElement page, PageEdit edit)
    {
        XElement? accessibility = page.Element(XName.Get("Accessibility", Manifest));
        string? text = edit.AlternativeText?.Trim() ?? accessibility?.Element(XName.Get("AlternativeText", Manifest))?.Value.Trim();
        bool decorative = edit.Decorative ?? (string?)accessibility?.Attribute("decorative") == "true";

        if (decorative && !string.IsNullOrEmpty(text))
            throw new ArgumentException("A decorative page carries no alternative text (§8.7); clear the text or the mark.", nameof(edit));

        accessibility?.Remove();

        if (!decorative && string.IsNullOrEmpty(text))
            return;

        var written = new XElement(XName.Get("Accessibility", Manifest), new XAttribute("decorative", decorative ? "true" : "false"));

        if (!string.IsNullOrEmpty(text))
            written.Add(new XElement(XName.Get("AlternativeText", Manifest), text));

        // After the checksum and before any extension, where §8 places it.
        if (page.Element(XName.Get("Checksum", Manifest)) is { } checksum)
            checksum.AddAfterSelf(written);
        else
            page.AddFirst(written);
    }

    /// <summary>
    /// The navigation with its landmarks rebuilt from the roles: the first
    /// page of each landmark role, then the first page of the story as
    /// <c>body-start</c> (§9.3).
    /// </summary>
    private static XDocument Landmarks(XDocument navigation, XDocument manifest)
    {
        var edited = new XDocument(navigation);
        XElement? landmarks = edited.Root?.Element(XName.Get("Landmarks", Navigation));

        if (landmarks is null)
            return edited;

        // A label someone wrote for a landmark of this type is kept, the type
        // being what it labelled.
        Dictionary<string, XElement[]> labels = landmarks.Elements(XName.Get("Landmark", Navigation))
            .GroupBy(l => (string?)l.Attribute("type") ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (XElement[])[.. g.First().Elements(XName.Get("Label", Navigation))], StringComparer.Ordinal);

        var rebuilt = new List<XElement>();

        foreach (string role in LandmarkRoles)
        {
            if (Items(manifest).FirstOrDefault(i => Tokens(i).Contains(role, StringComparer.Ordinal)) is { } page)
                rebuilt.Add(Landmark(role, page, labels));
        }

        if (Items(manifest).FirstOrDefault(i => Tokens(i).Contains(Story, StringComparer.Ordinal)) is { } story)
            rebuilt.Add(Landmark("body-start", story, labels));

        // §9.3 wants at least one landmark in the section: with none, the
        // section goes rather than stay empty.
        if (rebuilt.Count == 0)
            landmarks.Remove();
        else
            landmarks.ReplaceNodes(rebuilt);

        return edited;
    }

    private static XElement Landmark(string type, XElement page, Dictionary<string, XElement[]> labels) =>
        new(XName.Get("Landmark", Navigation),
            new XAttribute("type", type),
            new XAttribute("item", (string?)page.Attribute("id") ?? string.Empty),
            labels.GetValueOrDefault(type, []).Select(l => new XElement(l)));

    private static IEnumerable<XElement> Items(XDocument manifest) => manifest.Root?.Element(XName.Get("Resources", Manifest))?.Elements(XName.Get("Item", Manifest)) ?? [];

    private static XElement Item(XDocument manifest, string item) =>
        Items(manifest).FirstOrDefault(i => (string?)i.Attribute("id") == item)
        ?? throw new ArgumentException($"The manifest declares no item '{item}'.", nameof(item));

    private static XElement Reference(XDocument manifest, string item) =>
        manifest.Root?.Element(XName.Get("Spine", Manifest))?.Elements(XName.Get("ItemRef", Manifest)).FirstOrDefault(r => (string?)r.Attribute("item") == item)
        ?? throw new ArgumentException($"The spine does not carry '{item}' (§8.8).", nameof(item));

    private static string[] Tokens(XElement item) => ((string?)item.Attribute("roles") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static SpreadPosition Position(string? attribute) => attribute switch
    {
        "left" => Rendering.SpreadPosition.Left,
        "right" => Rendering.SpreadPosition.Right,
        "center" => Rendering.SpreadPosition.Center,
        _ => Rendering.SpreadPosition.Auto
    };
}
