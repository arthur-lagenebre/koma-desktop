using Koma.Core.Model;

namespace Koma.Core.Rendering;

/// <summary>
/// The page-list labels that fall on a spread (§9.2).
/// </summary>
public static class PageLabels
{
    /// <summary>
    /// The labels of the items a spread shows, in reading order.
    /// </summary>
    /// <remarks>
    /// Collected left to right across the screen, whole-resource labels before
    /// the halves of a <c>page-span="2"</c> resource, then turned round for a
    /// right-to-left publication: its reading starts on the right (§10.2), and
    /// so do its page numbers.
    /// </remarks>
    public static IReadOnlyList<string> Of(Spread spread, IReadOnlyList<PageTarget> pageList, ReadingDirection direction)
    {
        ArgumentNullException.ThrowIfNull(spread);
        ArgumentNullException.ThrowIfNull(pageList);

        List<string> labels = [];

        foreach (string item in SpreadLayout.Items(spread))
            labels.AddRange(pageList.Where(t => t.Item == item).OrderBy(t => Position(t.Side)).Select(t => t.Label));

        if (direction == ReadingDirection.RightToLeft)
            labels.Reverse();

        return labels;
    }

    private static int Position(PhysicalSide? side) => side switch
    {
        null => 0,
        PhysicalSide.Left => 1,
        _ => 2
    };
}
