using System.Collections.ObjectModel;
using Koma.Core.Rendering;

namespace Koma.Core.Model;

/// <summary>
/// One declared page resource (§8.1).
/// </summary>
public sealed record ManifestItem
{
    public required string Id { get; init; }

    /// <summary>Package-relative path, below <c>pages/</c> (§8.1).</summary>
    public required string Href { get; init; }

    public required string MediaType { get; init; }

    /// <summary>Exact final raster dimensions after orientation normalization (§8.2).</summary>
    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>Role tokens, open vocabulary (§8.4).</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>1, or 2 when one raster image physically contains two pages (§8.5).</summary>
    public int PageSpan { get; init; } = 1;

    public string? BackgroundColor { get; init; }

    /// <summary>The SHA-256 digest of §8.6, when the item declares one.</summary>
    public string? Sha256 { get; init; }

    /// <summary>Whether the page is purely decorative (§8.7).</summary>
    public bool IsDecorative { get; init; }
}

/// <summary>One entry of the spine (§8.8).</summary>
public sealed record SpineItemRef
{
    public required string Item { get; init; }

    public SpreadPosition SpreadPosition { get; init; } = SpreadPosition.Auto;
}

/// <summary>
/// <c>manifest.xml</c>: the declared resources and the reading order (§8).
/// </summary>
public sealed record Manifest
{
    public required KomaVersion Version { get; init; }

    /// <summary>
    /// Whether <c>@navigation</c> is present. §1 fixes where the navigation
    /// document lives, so presence is all the attribute can say.
    /// </summary>
    public bool DeclaresNavigation { get; init; }

    public required ReadOnlyCollection<ManifestItem> Items { get; init; }

    /// <summary>The only normative reading order (§8.8).</summary>
    public required ReadOnlyCollection<SpineItemRef> Spine { get; init; }

    /// <summary>Looks an item up by id.</summary>
    /// <summary>
    /// The front cover, which §8.4 requires exactly one item to carry and the
    /// reader refuses a manifest without.
    /// </summary>
    public ManifestItem? FrontCover => Items.FirstOrDefault(i => i.Roles.Contains(FrontCoverRole, StringComparer.Ordinal));

    /// <summary>The role of §8.4 that names the cover.</summary>
    public const string FrontCoverRole = "front-cover";

    public ManifestItem? Item(string id) => Items.FirstOrDefault(i => i.Id == id);

    /// <summary>
    /// The spine as the pairing algorithm of §10.4 expects it.
    /// </summary>
    /// <remarks>
    /// This is the join between the two halves of the reader. §10.3 decides an
    /// entry's effective position from the spread position carried by the
    /// <c>ItemRef</c> and the page span and roles carried by the <c>Item</c>,
    /// so the two are brought together here rather than in the paginator, which
    /// has no business knowing how a manifest is shaped.
    /// </remarks>
    public IReadOnlyList<SpineEntry> ToSpineEntries()
    {
        var entries = new List<SpineEntry>(Spine.Count);

        foreach (SpineItemRef reference in Spine)
        {
            ManifestItem? item = Item(reference.Item);

            if (item is null)
                continue;

            entries.Add(new SpineEntry { Item = item.Id, PageSpan = item.PageSpan, SpreadPosition = reference.SpreadPosition, Roles = item.Roles });
        }

        return entries;
    }
}
