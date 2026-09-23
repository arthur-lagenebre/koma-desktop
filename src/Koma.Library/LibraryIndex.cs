using Koma.Core.Rendering;

namespace Koma.Library;

/// <summary>
/// One publication as the library knows it, without opening it again.
/// </summary>
/// <remarks>
/// Everything here but the reading position is a cache of what the file says:
/// losing the index costs a scan, not a reader's place in a book.
/// </remarks>
public sealed record LibraryEntry
{
    /// <summary>The file, as the scan found it.</summary>
    public required string Path { get; init; }

    /// <summary>Size and time as of the last scan: what tells a changed file from an untouched one.</summary>
    public required long Size { get; init; }

    public required DateTimeOffset Modified { get; init; }

    /// <summary>The main title of §7.3, or <see langword="null"/> when the publication could not be opened.</summary>
    public string? Title { get; init; }

    /// <summary>Entries in the spine, which is a count of pages and not of spreads: spreads depend on the window.</summary>
    public int PageCount { get; init; }

    public ReadingDirection Direction { get; init; }

    /// <summary>The series of §7.5, which the shelf groups volumes by.</summary>
    public string? Series { get; init; }

    /// <summary>The volume's place in its series, as text: HS2 and 3.5 are numbers too.</summary>
    public string? SeriesPosition { get; init; }

    /// <summary>How many volumes the series holds, when the publication says.</summary>
    public string? SeriesTotal { get; init; }

    /// <summary>The cover thumbnail's file name, in the store's own folder.</summary>
    public string? Thumbnail { get; init; }

    /// <summary>Why the publication could not be opened, when it could not.</summary>
    public string? Unreadable { get; init; }

    /// <summary>
    /// Where the reader left off: the first item of the spread last shown.
    /// </summary>
    /// <remarks>
    /// An item and not a spread number. Pagination changes with the shape of
    /// the window (§10.1), so a number recorded in one window names another
    /// page in the next; an item is the same page whatever the pagination.
    /// </remarks>
    public string? LastItem { get; init; }

    /// <summary>
    /// Where <see cref="LastItem"/> stood in the spine when it was recorded,
    /// counting from one, or zero when nothing was recorded.
    /// </summary>
    /// <remarks>
    /// For showing a reader how far in they are, and nothing else. The item
    /// is what reopens the publication in the right place; this number is a
    /// convenience that a reordered spine would make wrong, which is why it
    /// is not what the position is kept as.
    /// </remarks>
    public int LastPage { get; init; }

    public DateTimeOffset? LastOpened { get; init; }

    /// <summary>
    /// How the reader last had this publication fitted to the window, and at
    /// what zoom; zero for a publication never read, which starts fitted to
    /// the page.
    /// </summary>
    /// <remarks>
    /// Per publication rather than once for the application: a dense manga is
    /// read at the width of the screen and a large-format album whole, and a
    /// reader who alternates between them should not have to say so twice.
    /// </remarks>
    public FitMode Fit { get; init; }

    public double Zoom { get; init; }
}

/// <summary>
/// The library: the folders watched, and what the last scan found in them.
/// </summary>
public sealed record LibraryIndex
{
    /// <summary>
    /// The version of what an entry records. An index written by an earlier
    /// version lacks what later ones added — the series, for one — and its
    /// entries are described again at the next scan, positions kept.
    /// </summary>
    public const int CurrentFormat = 3;

    public static LibraryIndex Empty { get; } = new() { Format = CurrentFormat };

    /// <summary>
    /// What <see cref="CurrentFormat"/> the entries were described under; 1
    /// for an index from before the field existed.
    /// </summary>
    public int Format { get; init; } = 1;

    public IReadOnlyList<string> Folders { get; init; } = [];

    public IReadOnlyList<LibraryEntry> Entries { get; init; } = [];

    /// <summary>How the shelf was last ordered, which it opens on again.</summary>
    public ShelfOrder Order { get; init; }
}
