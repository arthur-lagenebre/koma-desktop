namespace Koma.Core.Rendering;

/// <summary>
/// Turns a spine into the sequence of displayed spreads, per §10.
/// </summary>
/// <remarks>
/// <para>
/// §10 opens by saying what it is for: two conforming reading systems must
/// produce the same pagination for the same publication. So this is a
/// transcription of the normative pseudocode rather than a rewriting of it,
/// down to the order of the branches. Where a shorter formulation exists, it is
/// not used: the value here is that the code and §10.4 can be read side by side.
/// </para>
/// <para>
/// <see cref="Pseudocode"/> carries that text verbatim, and a test compares it
/// with the specification in the submodule. The reference implementation does
/// the same, for the same reason: an implementation that merely agrees with the
/// prose is not enough when the prose ships an algorithm.
/// </para>
/// </remarks>
public static class SpreadPaginator
{
    /// <summary>
    /// The pseudocode of §10.4, verbatim.
    /// </summary>
    public const string Pseudocode = """
        buffer := empty
        for each entry E in spine order:
            p := effective position of E (§10.3)
            if p is full:
                if buffer is not empty: emit(buffer); buffer := empty
                emit(spread containing only E, centered)
            else if p is fixed:
                if the requested side is occupied in buffer:
                    emit(buffer); buffer := empty
                place E on the requested side of buffer
                if buffer is full: emit(buffer); buffer := empty
            else:                                    # flow
                if buffer is not empty and the leading side is free:
                    emit(buffer); buffer := empty    # spine-order invariant
                if the leading side of buffer is free:
                    place E on the leading side
                else:
                    place E on the trailing side
                if buffer is full: emit(buffer); buffer := empty
        if buffer is not empty: emit(buffer)

        """;

    /// <summary>The core role token that rule 4 of §10.3 looks for.</summary>
    private const string FrontCover = "front-cover";

    /// <summary>
    /// Paginates a spine.
    /// </summary>
    /// <param name="spine">The entries, in spine order.</param>
    /// <param name="direction">From <c>Reading/@direction</c>.</param>
    /// <param name="policy">From <c>Reading/@spread</c>.</param>
    /// <param name="viewportFitsTwo">
    /// Whether the viewport can present two items side by side. §10.1 leaves
    /// this to the reading system: with <see cref="SpreadPolicy.Force"/> it is
    /// the only thing that can send the display back to single mode, and with
    /// <see cref="SpreadPolicy.Auto"/> the section suggests spread mode when the
    /// viewport is wider than tall. Both are answered before calling here.
    /// </param>
    public static IReadOnlyList<Spread> Paginate(IReadOnlyList<SpineEntry> spine, ReadingDirection direction, SpreadPolicy policy, bool viewportFitsTwo = true)
    {
        ArgumentNullException.ThrowIfNull(spine);

        if (!UsesSpreadMode(policy, viewportFitsTwo))
            return [.. spine.Select(e => new SingleSpread(e.Item))];

        return PaginateSpreads(spine, direction);
    }

    /// <summary>
    /// §10.1. In single mode a <c>page-span="2"</c> item is shown whole: the
    /// section allows a reading system to offer splitting it, but never by
    /// default, so pagination does not know about the option.
    /// </summary>
    private static bool UsesSpreadMode(SpreadPolicy policy, bool viewportFitsTwo) => policy switch
    {
        SpreadPolicy.None => false,
        SpreadPolicy.Force => viewportFitsTwo,
        _ => viewportFitsTwo
    };

    private static List<Spread> PaginateSpreads(IReadOnlyList<SpineEntry> spine, ReadingDirection direction)
    {
        PhysicalSide leading = LeadingSide(direction);
        PhysicalSide trailing = Other(leading);

        var emitted = new List<Spread>();
        var buffer = new Buffer();

        foreach (SpineEntry entry in spine)
        {
            EffectivePosition p = Effective(entry);

            if (p.Kind == EffectivePositionKind.Full)
            {
                if (!buffer.IsEmpty)
                    Emit(emitted, buffer);

                emitted.Add(new CenteredSpread(entry.Item));
            }
            else if (p.Kind == EffectivePositionKind.Fixed)
            {
                PhysicalSide requested = p.Side!.Value;

                if (buffer.IsOccupied(requested))
                    Emit(emitted, buffer);

                buffer.Place(requested, entry.Item);

                if (buffer.IsFull)
                    Emit(emitted, buffer);
            }
            else
            {
                // The spine-order invariant. The buffer is emitted rather than
                // back-filled whenever its leading half is already free but
                // something sits in the trailing one: filling the leading half
                // would put this entry on the side read before an entry that
                // precedes it in the spine.
                if (!buffer.IsEmpty && !buffer.IsOccupied(leading))
                    Emit(emitted, buffer);

                buffer.Place(buffer.IsOccupied(leading) ? trailing : leading, entry.Item);

                if (buffer.IsFull)
                    Emit(emitted, buffer);
            }
        }

        if (!buffer.IsEmpty)
            Emit(emitted, buffer);

        return emitted;
    }

    /// <summary>§10.2.</summary>
    public static PhysicalSide LeadingSide(ReadingDirection direction) => direction == ReadingDirection.LeftToRight ? PhysicalSide.Left : PhysicalSide.Right;

    private static PhysicalSide Other(PhysicalSide side) => side == PhysicalSide.Left ? PhysicalSide.Right : PhysicalSide.Left;

    /// <summary>
    /// §10.3, in the order the section gives. The order is the rule: an item
    /// that is both <c>page-span="2"</c> and pinned to a side is full, and the
    /// pin never applies.
    /// </summary>
    public static EffectivePosition Effective(SpineEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Rule 1. The schema admits only 1 and 2, so a larger value is a
        // defect layer 2 catches; treating it as full is the reading that does
        // not silently paginate a defective publication as if it were sound.
        if (entry.PageSpan >= 2)
            return new EffectivePosition(EffectivePositionKind.Full, null);

        // Rule 2.
        if (entry.SpreadPosition == SpreadPosition.Center)
            return new EffectivePosition(EffectivePositionKind.Full, null);

        // Rule 3.
        if (entry.SpreadPosition == SpreadPosition.Left)
            return new EffectivePosition(EffectivePositionKind.Fixed, PhysicalSide.Left);

        if (entry.SpreadPosition == SpreadPosition.Right)
            return new EffectivePosition(EffectivePositionKind.Fixed, PhysicalSide.Right);

        // Rule 4. The usual single-cover presentation, without producer action.
        // A producer who wants the cover paired sets an explicit side, which
        // rule 3 has already answered above.
        if (entry.SpreadPosition == SpreadPosition.Auto && entry.Roles.Contains(FrontCover, StringComparer.Ordinal))
            return new EffectivePosition(EffectivePositionKind.Full, null);

        // Rule 5.
        return new EffectivePosition(EffectivePositionKind.Flow, null);
    }

    private static void Emit(List<Spread> emitted, Buffer buffer)
    {
        emitted.Add(new PairSpread(buffer.Left, buffer.Right));
        buffer.Clear();
    }

    /// <summary>
    /// At most one leading-side item and one trailing-side item, as §10.4 says.
    /// </summary>
    private sealed class Buffer
    {
        public string? Left { get; private set; }

        public string? Right { get; private set; }

        public bool IsEmpty => Left is null && Right is null;

        public bool IsFull => Left is not null && Right is not null;

        public bool IsOccupied(PhysicalSide side) =>
            side == PhysicalSide.Left ? Left is not null : Right is not null;

        public void Place(PhysicalSide side, string item)
        {
            if (side == PhysicalSide.Left)
                Left = item;
            else
                Right = item;
        }

        public void Clear()
        {
            Left = null;
            Right = null;
        }
    }
}
