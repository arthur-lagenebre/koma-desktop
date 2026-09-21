using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Koma.Core.Schemas;

/// <summary>A RELAX NG pattern, after simplification (RELAX NG §4).</summary>
/// <remarks>
/// Combinators compare by value, so that a choice between two equal
/// derivatives collapses to one and derivatives do not grow without bound.
/// Element and Ref compare by reference: they are where the grammar recurses,
/// and a value comparison would never finish.
/// </remarks>
internal abstract record Pattern;

internal sealed record EmptyPattern : Pattern
{
    public static readonly EmptyPattern Instance = new();
}

internal sealed record NotAllowedPattern : Pattern
{
    public static readonly NotAllowedPattern Instance = new();
}

internal sealed record TextPattern : Pattern
{
    public static readonly TextPattern Instance = new();
}

internal sealed record ChoicePattern(Pattern Left, Pattern Right) : Pattern;

internal sealed record GroupPattern(Pattern Left, Pattern Right) : Pattern;

internal sealed record OneOrMorePattern(Pattern Inner) : Pattern;

/// <summary>What remains of an element once its start tag is read: its content, then what follows it.</summary>
internal sealed record AfterPattern(Pattern Content, Pattern Rest) : Pattern;

internal sealed record AttributePattern(NameClass Name, Pattern Value) : Pattern;

internal sealed record ValuePattern(Datatype Type, string Value) : Pattern;

internal sealed record DataPattern(Datatype Type, Regex[] Patterns, Pattern? Except) : Pattern;

internal sealed record ElementPattern(NameClass Name, Pattern Content) : Pattern
{
    public bool Equals(ElementPattern? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

/// <summary>A reference to a definition, resolved once every definition is read.</summary>
internal sealed record RefPattern(string Name) : Pattern
{
    public Pattern? Target { get; set; }

    public bool Equals(RefPattern? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

/// <summary>A RELAX NG name class.</summary>
internal abstract record NameClass
{
    public abstract bool Contains(string ns, string local);
}

internal sealed record AnyNameClass(NameClass? Except) : NameClass
{
    public override bool Contains(string ns, string local) => Except is null || !Except.Contains(ns, local);
}

internal sealed record NsNameClass(string Ns, NameClass? Except) : NameClass
{
    public override bool Contains(string ns, string local) => ns == Ns && (Except is null || !Except.Contains(ns, local));
}

internal sealed record QNameClass(string Ns, string Local) : NameClass
{
    public override bool Contains(string ns, string local) => ns == Ns && local == Local;
}

internal sealed record NameClassChoice(NameClass Left, NameClass Right) : NameClass
{
    public override bool Contains(string ns, string local) => Left.Contains(ns, local) || Right.Contains(ns, local);
}

/// <param name="Library">The datatype library URI; empty for RELAX NG's own.</param>
internal sealed record Datatype(string Library, string Name);
