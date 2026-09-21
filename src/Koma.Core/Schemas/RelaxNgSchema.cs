using System.Xml.Linq;

namespace Koma.Core.Schemas;

/// <summary>
/// A RELAX NG schema, loaded from its XML syntax, and the validation of a
/// document against it.
/// </summary>
/// <remarks>
/// <para>
/// Validation by derivatives, after James Clark's "An algorithm for RELAX NG
/// validation" (2002): the pattern is rewritten by each start tag, attribute,
/// text and end tag, and the document is valid when what remains can match
/// nothing. No automaton is built, and the whole fits in a few functions.
/// </para>
/// <para>
/// It implements what the KOMA schemas use and refuses the rest when it
/// loads a schema, rather than validating a construct it does not know:
/// patterns, name classes with exceptions, and the eight XML Schema
/// datatypes of <see cref="XsdDatatypes"/>. No interleave, no list, no
/// external references.
/// </para>
/// <para>
/// The algorithm was checked, before it was written here, against libxml2
/// on the four reference instances, the documents the schemas must reject,
/// every core document of the corpus and three thousand random mutations of
/// the reference instances, without one disagreement.
/// </para>
/// </remarks>
public sealed class RelaxNgSchema
{
    private static readonly Pattern Empty = EmptyPattern.Instance;
    private static readonly Pattern NotAllowed = NotAllowedPattern.Instance;
    private static readonly Pattern Text = TextPattern.Instance;

    private readonly Pattern start;

    private RelaxNgSchema(Pattern start)
    {
        this.start = start;
    }

    /// <summary>Loads a schema from its XML syntax, as <c>tools/build_schemas.py</c> writes it.</summary>
    /// <exception cref="NotSupportedException">The schema uses a construct this validator does not implement.</exception>
    public static RelaxNgSchema Load(XDocument schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return new RelaxNgSchema(RelaxNgLoader.Load(schema));
    }

    /// <summary>
    /// Validates a document.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when the document is valid, else where it
    /// stopped matching: the path of the innermost element at fault.
    /// </returns>
    public string? Validate(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        XElement root = document.Root ?? throw new ArgumentException("The document has no root element.", nameof(document));
        var validation = new Validation();
        Pattern rest = validation.ChildDeriv(start, root);

        return Nullable(rest) ? null : validation.Fault ?? "/" + root.Name.LocalName;
    }

    /// <summary>One document's pass, which remembers where it first went wrong.</summary>
    private sealed class Validation
    {
        private readonly Stack<string> path = new();

        public string? Fault { get; private set; }

        public Pattern ChildDeriv(Pattern pattern, XElement element)
        {
            path.Push(element.Name.LocalName);

            string ns = element.Name.NamespaceName;
            Pattern derived = StartTagOpenDeriv(pattern, ns, element.Name.LocalName);

            foreach (XAttribute attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
                derived = AttDeriv(derived, attribute.Name.NamespaceName, attribute.Name.LocalName, attribute.Value);

            derived = StartTagCloseDeriv(derived);
            derived = ChildrenDeriv(derived, element);
            derived = EndTagDeriv(derived);

            // The first element to fail is the innermost: its children are
            // derived before its end tag.
            if (derived == NotAllowed && pattern != NotAllowed && Fault is null)
                Fault = "/" + string.Join('/', path.Reverse());

            path.Pop();

            return derived;
        }

        private Pattern ChildrenDeriv(Pattern pattern, XElement element)
        {
            List<object> children = Children(element);

            if (children.Count == 0)
                children.Add(string.Empty);

            if (children is [string only])
            {
                Pattern derived = TextDeriv(pattern, only);

                return IsWhitespace(only) ? Choice(pattern, derived) : derived;
            }

            foreach (object child in children)
            {
                if (child is string text)
                {
                    // Whitespace between elements is not text (RELAX NG §6.2.7).
                    if (!IsWhitespace(text))
                        pattern = TextDeriv(pattern, text);
                }
                else
                {
                    pattern = ChildDeriv(pattern, (XElement)child);
                }
            }

            return pattern;
        }

        /// <summary>The element's children, adjacent text merged, comments and processing instructions dropped.</summary>
        private static List<object> Children(XElement element)
        {
            var children = new List<object>();

            foreach (XNode node in element.Nodes())
            {
                if (node is XElement child)
                    children.Add(child);
                else if (node is XText text && children.Count > 0 && children[^1] is string previous)
                    children[^1] = previous + text.Value;
                else if (node is XText first)
                    children.Add(first.Value);
            }

            return children;
        }
    }

    private static bool Nullable(Pattern pattern) => Deref(pattern) switch
    {
        EmptyPattern or TextPattern => true,
        GroupPattern g => Nullable(g.Left) && Nullable(g.Right),
        ChoicePattern c => Nullable(c.Left) || Nullable(c.Right),
        OneOrMorePattern o => Nullable(o.Inner),
        _ => false
    };

    private static Pattern StartTagOpenDeriv(Pattern pattern, string ns, string local) => Deref(pattern) switch
    {
        ElementPattern e => e.Name.Contains(ns, local) ? After(e.Content, Empty) : NotAllowed,
        ChoicePattern c => Choice(StartTagOpenDeriv(c.Left, ns, local), StartTagOpenDeriv(c.Right, ns, local)),
        OneOrMorePattern o => ApplyAfter(p => Group(p, Choice(o, Empty)), StartTagOpenDeriv(o.Inner, ns, local)),
        GroupPattern g => GroupOpen(g, ns, local),
        AfterPattern a => ApplyAfter(p => After(p, a.Rest), StartTagOpenDeriv(a.Content, ns, local)),
        _ => NotAllowed
    };

    private static Pattern GroupOpen(GroupPattern g, string ns, string local)
    {
        Pattern first = ApplyAfter(p => Group(p, g.Right), StartTagOpenDeriv(g.Left, ns, local));

        return Nullable(g.Left) ? Choice(first, StartTagOpenDeriv(g.Right, ns, local)) : first;
    }

    private static Pattern ApplyAfter(Func<Pattern, Pattern> f, Pattern pattern) => Deref(pattern) switch
    {
        AfterPattern a => After(a.Content, f(a.Rest)),
        ChoicePattern c => Choice(ApplyAfter(f, c.Left), ApplyAfter(f, c.Right)),
        _ => NotAllowed
    };

    private static Pattern AttDeriv(Pattern pattern, string ns, string local, string value) => Deref(pattern) switch
    {
        AfterPattern a => After(AttDeriv(a.Content, ns, local, value), a.Rest),
        ChoicePattern c => Choice(AttDeriv(c.Left, ns, local, value), AttDeriv(c.Right, ns, local, value)),
        GroupPattern g => Choice(Group(AttDeriv(g.Left, ns, local, value), g.Right), Group(g.Left, AttDeriv(g.Right, ns, local, value))),
        OneOrMorePattern o => Group(AttDeriv(o.Inner, ns, local, value), Choice(o, Empty)),
        AttributePattern at => at.Name.Contains(ns, local) && ValueMatch(at.Value, value) ? Empty : NotAllowed,
        _ => NotAllowed
    };

    private static bool ValueMatch(Pattern pattern, string value) => (Nullable(pattern) && IsWhitespace(value)) || Nullable(TextDeriv(pattern, value));

    private static Pattern StartTagCloseDeriv(Pattern pattern) => Deref(pattern) switch
    {
        AfterPattern a => After(StartTagCloseDeriv(a.Content), a.Rest),
        ChoicePattern c => Choice(StartTagCloseDeriv(c.Left), StartTagCloseDeriv(c.Right)),
        GroupPattern g => Group(StartTagCloseDeriv(g.Left), StartTagCloseDeriv(g.Right)),
        OneOrMorePattern o => OneOrMore(StartTagCloseDeriv(o.Inner)),
        AttributePattern => NotAllowed,
        Pattern other => other
    };

    private static Pattern TextDeriv(Pattern pattern, string text) => Deref(pattern) switch
    {
        TextPattern => Text,
        ChoicePattern c => Choice(TextDeriv(c.Left, text), TextDeriv(c.Right, text)),
        GroupPattern g => GroupText(g, text),
        AfterPattern a => After(TextDeriv(a.Content, text), a.Rest),
        OneOrMorePattern o => Group(TextDeriv(o.Inner, text), Choice(o, Empty)),
        ValuePattern v => XsdDatatypes.Equal(v.Type, v.Value, text) ? Empty : NotAllowed,
        DataPattern d => XsdDatatypes.Allows(d.Type, d.Patterns, text) && (d.Except is null || !Nullable(TextDeriv(d.Except, text))) ? Empty : NotAllowed,
        _ => NotAllowed
    };

    private static Pattern GroupText(GroupPattern g, string text)
    {
        Pattern first = Group(TextDeriv(g.Left, text), g.Right);

        return Nullable(g.Left) ? Choice(first, TextDeriv(g.Right, text)) : first;
    }

    private static Pattern EndTagDeriv(Pattern pattern) => Deref(pattern) switch
    {
        ChoicePattern c => Choice(EndTagDeriv(c.Left), EndTagDeriv(c.Right)),
        AfterPattern a => Nullable(a.Content) ? a.Rest : NotAllowed,
        _ => NotAllowed
    };

    // RELAX NG §6.2.7 and §4.2: space, tab, carriage return and line feed.
    private static bool IsWhitespace(string text) => text.AsSpan().TrimStart(" \t\r\n").IsEmpty;

    private static Pattern Deref(Pattern pattern)
    {
        while (pattern is RefPattern reference)
            pattern = reference.Target ?? throw new InvalidOperationException($"The definition '{reference.Name}' was never resolved.");

        return pattern;
    }

    // The constructors keep the patterns small: every derivative goes
    // through them, and without these rules a document of a few hundred
    // elements would build patterns of millions of nodes.
    internal static Pattern Choice(Pattern left, Pattern right)
    {
        if (left == NotAllowed)
            return right;

        if (right == NotAllowed || left == right)
            return left;

        return new ChoicePattern(left, right);
    }

    internal static Pattern Group(Pattern left, Pattern right)
    {
        if (left == NotAllowed || right == NotAllowed)
            return NotAllowed;

        if (left == Empty)
            return right;

        return right == Empty ? left : new GroupPattern(left, right);
    }

    internal static Pattern OneOrMore(Pattern inner) => inner == NotAllowed ? NotAllowed : new OneOrMorePattern(inner);

    private static Pattern After(Pattern content, Pattern rest) => content == NotAllowed || rest == NotAllowed ? NotAllowed : new AfterPattern(content, rest);
}
