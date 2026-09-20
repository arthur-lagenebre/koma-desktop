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

    public DateTimeOffset? LastOpened { get; init; }
}

/// <summary>
/// The library: the folders watched, and what the last scan found in them.
/// </summary>
public sealed record LibraryIndex
{
    public static LibraryIndex Empty { get; } = new();

    public IReadOnlyList<string> Folders { get; init; } = [];

    public IReadOnlyList<LibraryEntry> Entries { get; init; } = [];
}
