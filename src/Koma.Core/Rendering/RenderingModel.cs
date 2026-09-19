namespace Koma.Core.Rendering;

/// <summary>Reading direction, from <c>Reading/@direction</c>.</summary>
public enum ReadingDirection
{
    LeftToRight,
    RightToLeft
}

/// <summary>The <c>spread</c> policy of §10.1.</summary>
public enum SpreadPolicy
{
    /// <summary>Single mode, whatever the viewport.</summary>
    None,
    /// <summary>The reading system chooses.</summary>
    Auto,
    /// <summary>Spread mode wherever the viewport allows it.</summary>
    Force
}

/// <summary>The declared <c>spread-position</c> of a spine entry.</summary>
public enum SpreadPosition
{
    Auto,
    Left,
    Right,
    Center
}

/// <summary>A physical side of the display.</summary>
public enum PhysicalSide
{
    Left,
    Right
}

/// <summary>How an entry occupies a spread, per §10.3.</summary>
public enum EffectivePositionKind
{
    /// <summary>The entry takes the whole spread.</summary>
    Full,
    /// <summary>The entry is pinned to one physical side.</summary>
    Fixed,
    /// <summary>The entry takes whichever side pairing gives it.</summary>
    Flow
}

/// <summary>An entry's effective position.</summary>
/// <param name="Kind">Full, fixed or flow.</param>
/// <param name="Side">The pinned side, for <see cref="EffectivePositionKind.Fixed"/> only.</param>
public readonly record struct EffectivePosition(EffectivePositionKind Kind, PhysicalSide? Side);

/// <summary>
/// One entry of the spine, reduced to what §10 needs.
/// </summary>
public sealed record SpineEntry
{
    /// <summary>The item this entry points at.</summary>
    public required string Item { get; init; }

    /// <summary>1, or 2 for an item spanning both halves.</summary>
    public int PageSpan { get; init; } = 1;

    public SpreadPosition SpreadPosition { get; init; } = SpreadPosition.Auto;

    /// <summary>Core and private-use role tokens carried by the item.</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];
}

/// <summary>One displayed unit.</summary>
public abstract record Spread;

/// <summary>Single mode: one item on its own, no sides.</summary>
public sealed record SingleSpread(string Item) : Spread;

/// <summary>Spread mode: one item occupying both halves.</summary>
public sealed record CenteredSpread(string Item) : Spread;

/// <summary>
/// Spread mode: the two physical halves, either of which may be empty.
/// </summary>
/// <remarks>
/// §10.4 requires an empty half to be filled with the accompanying item's
/// <c>background-color</c>, or <c>#FFFFFF</c> when it declares none. That is a
/// compositing rule, so the colour is not carried here; what matters to
/// pagination is that the half is empty and stays so.
/// </remarks>
public sealed record PairSpread(string? Left, string? Right) : Spread;
