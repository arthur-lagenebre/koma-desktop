using System.Globalization;
using Koma.Core;

namespace Koma.Library;

/// <summary>How the shelf orders what it shows.</summary>
public enum ShelfOrder
{
    /// <summary>By title, then by file name.</summary>
    Title,

    /// <summary>Grouped by series, each series in the order of its volume numbers.</summary>
    Series,

    /// <summary>The publication read last first; those never opened after.</summary>
    RecentlyRead
}

/// <param name="Heading">What the group is called on the shelf, or <see langword="null"/> for none.</param>
public sealed record ShelfGroup(string? Heading, IReadOnlyList<LibraryEntry> Entries);

/// <summary>
/// What the shelf shows, in what order and under which headings, apart from
/// how it is drawn.
/// </summary>
/// <remarks>
/// Publications that cannot be opened always come last, under a heading of
/// their own: they are worth seeing, and not worth being searched past.
/// </remarks>
public static class ShelfArrangement
{
    public const string NoSeries = "No series";

    public const string Unreadable = "Could not be opened";

    /// <param name="query">
    /// Text the title, the series or the file name must contain, case and
    /// accents aside: "eleve" finds "Élève", as a French reader expects.
    /// </param>
    /// <param name="culture">The culture titles sort in; the current one when omitted.</param>
    public static IReadOnlyList<ShelfGroup> Arrange(IEnumerable<LibraryEntry> entries, string? query, ShelfOrder order, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        culture ??= CultureInfo.CurrentCulture;

        StringComparer titles = StringComparer.Create(culture, CompareOptions.IgnoreCase);
        LibraryEntry[] shown = [.. entries.Where(e => Matches(e, query?.Trim(), culture))];
        LibraryEntry[] readable = [.. shown.Where(e => e.Unreadable is null)];
        var groups = new List<ShelfGroup>();

        IOrderedEnumerable<LibraryEntry> ByTitle(IEnumerable<LibraryEntry> some) => some
            .OrderBy(e => e.Title ?? string.Empty, titles)
            .ThenBy(e => Path.GetFileName(e.Path), NaturalOrder.Instance);

        switch (order)
        {
            case ShelfOrder.Series:
                foreach (IGrouping<string, LibraryEntry> series in readable.Where(e => e.Series is not null).GroupBy(e => e.Series!, titles).OrderBy(g => g.Key, titles))
                {
                    // Numbered volumes in the order of their numbers, the
                    // unnumbered after them by title.
                    // By number, then by file name, which carries the number
                    // when the publication does not: a volume numbered in its
                    // name is still numbered.
                    groups.Add(new ShelfGroup(series.Key, [.. series
                        .OrderBy(e => e.SeriesPosition is null)
                        .ThenBy(e => e.SeriesPosition, NaturalOrder.Instance)
                        .ThenBy(e => Path.GetFileName(e.Path), NaturalOrder.Instance)
                        .ThenBy(e => e.Title ?? string.Empty, titles)]));
                }

                LibraryEntry[] loose = [.. ByTitle(readable.Where(e => e.Series is null))];

                if (loose.Length > 0)
                    groups.Add(new ShelfGroup(NoSeries, loose));

                break;

            case ShelfOrder.RecentlyRead:
                groups.Add(new ShelfGroup(null, [.. readable
                    .OrderBy(e => e.LastOpened is null)
                    .ThenByDescending(e => e.LastOpened)
                    .ThenBy(e => e.Title ?? string.Empty, titles)]));
                break;

            default:
                groups.Add(new ShelfGroup(null, [.. ByTitle(readable)]));
                break;
        }

        LibraryEntry[] refused = [.. shown.Where(e => e.Unreadable is not null).OrderBy(e => Path.GetFileName(e.Path), NaturalOrder.Instance)];

        if (refused.Length > 0)
            groups.Add(new ShelfGroup(Unreadable, refused));

        return [.. groups.Where(g => g.Entries.Count > 0)];
    }

    private static bool Matches(LibraryEntry entry, string? query, CultureInfo culture)
    {
        if (string.IsNullOrEmpty(query))
            return true;

        return new[] { entry.Title, entry.Series, Path.GetFileName(entry.Path) }
            .Any(text => text is not null && culture.CompareInfo.IndexOf(text, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
    }
}
