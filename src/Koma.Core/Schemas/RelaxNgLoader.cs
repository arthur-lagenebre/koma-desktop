using System.Xml.Linq;

namespace Koma.Core.Schemas;

/// <summary>
/// Turns the XML syntax of a RELAX NG grammar into patterns, simplifying on
/// the way (RELAX NG §4): optional, zeroOrMore and the implicit groups become
/// the few forms the derivatives know.
/// </summary>
internal static class RelaxNgLoader
{
    private const string Rng = "http://relaxng.org/ns/structure/1.0";

    public static Pattern Load(XDocument schema)
    {
        XElement grammar = schema.Root ?? throw new NotSupportedException("The schema has no root element.");

        if (grammar.Name != XName.Get("grammar", Rng))
            throw new NotSupportedException("The schema is not a RELAX NG grammar.");

        var definitions = new Dictionary<string, Pattern>(StringComparer.Ordinal);
        var references = new List<RefPattern>();
        Pattern? start = null;

        foreach (XElement child in Children(grammar))
        {
            switch (child.Name.LocalName)
            {
                case "start":
                    start = Sequence(Children(child), references);
                    break;
                case "define":
                    definitions[Required(child, "name")] = Sequence(Children(child), references);
                    break;
                default:
                    throw Unsupported(child);
            }
        }

        foreach (RefPattern reference in references)
            reference.Target = definitions.GetValueOrDefault(reference.Name) ?? throw new NotSupportedException($"The schema refers to '{reference.Name}', which it does not define.");

        return start ?? throw new NotSupportedException("The schema has no start.");
    }

    private static Pattern Read(XElement element, List<RefPattern> references)
    {
        List<XElement> children = [.. Children(element)];

        return element.Name.LocalName switch
        {
            "element" => new ElementPattern(ReadNameClass(children[0]), children.Count > 1 ? Sequence(children.Skip(1), references) : EmptyPattern.Instance),
            "attribute" => new AttributePattern(ReadNameClass(children[0]), children.Count > 1 ? Sequence(children.Skip(1), references) : TextPattern.Instance),
            "group" => Sequence(children, references),
            "choice" => children.Select(c => Read(c, references)).Aggregate(RelaxNgSchema.Choice),
            "optional" => RelaxNgSchema.Choice(Sequence(children, references), EmptyPattern.Instance),
            "zeroOrMore" => RelaxNgSchema.Choice(RelaxNgSchema.OneOrMore(Sequence(children, references)), EmptyPattern.Instance),
            "oneOrMore" => RelaxNgSchema.OneOrMore(Sequence(children, references)),
            "empty" => EmptyPattern.Instance,
            "text" => TextPattern.Instance,
            "notAllowed" => NotAllowedPattern.Instance,
            "ref" => Reference(element, references),
            "value" => ReadValue(element),
            "data" => ReadData(element, children, references),
            _ => throw Unsupported(element)
        };
    }

    /// <summary>Children in sequence form an implicit group (RELAX NG §4.12).</summary>
    private static Pattern Sequence(IEnumerable<XElement> children, List<RefPattern> references)
    {
        Pattern[] patterns = [.. children.Select(c => Read(c, references))];

        return patterns.Length == 0 ? throw new NotSupportedException("A pattern has no content.") : patterns.Aggregate(RelaxNgSchema.Group);
    }

    private static RefPattern Reference(XElement element, List<RefPattern> references)
    {
        var reference = new RefPattern(Required(element, "name"));
        references.Add(reference);

        return reference;
    }

    /// <remarks>
    /// A value without a type is RELAX NG's own <c>token</c> (§4.16), not a
    /// type of the inherited library: <c>version="0.9 "</c> matches "0.9".
    /// </remarks>
    private static ValuePattern ReadValue(XElement element)
    {
        string? type = (string?)element.Attribute("type");
        var datatype = type is null ? new Datatype(string.Empty, "token") : new Datatype(Library(element), type);

        return new ValuePattern(datatype, element.Value);
    }

    private static DataPattern ReadData(XElement element, List<XElement> children, List<RefPattern> references)
    {
        var datatype = new Datatype(Library(element), Required(element, "type"));
        var patterns = new List<System.Text.RegularExpressions.Regex>();
        Pattern? except = null;

        foreach (XElement child in children)
        {
            if (child.Name.LocalName == "param")
            {
                if (Required(child, "name") != "pattern")
                    throw new NotSupportedException($"The facet '{child.Attribute("name")!.Value}' is not implemented; only pattern is.");

                patterns.Add(XsdDatatypes.Pattern(child.Value));
            }
            else if (child.Name.LocalName == "except")
            {
                except = Children(child).Select(c => Read(c, references)).Aggregate(RelaxNgSchema.Choice);
            }
            else
            {
                throw Unsupported(child);
            }
        }

        XsdDatatypes.Check(datatype);

        return new DataPattern(datatype, [.. patterns], except);
    }

    private static NameClass ReadNameClass(XElement element) => element.Name.LocalName switch
    {
        "name" => new QNameClass(Required(element, "ns"), element.Value.Trim()),
        "anyName" => new AnyNameClass(Except(element)),
        "nsName" => new NsNameClass(Required(element, "ns"), Except(element)),
        "choice" => Children(element).Select(ReadNameClass).Aggregate((a, b) => new NameClassChoice(a, b)),
        _ => throw Unsupported(element)
    };

    private static NameClass? Except(XElement element)
    {
        XElement? except = Children(element).FirstOrDefault(c => c.Name.LocalName == "except");

        return except is null ? null : Children(except).Select(ReadNameClass).Aggregate((a, b) => new NameClassChoice(a, b));
    }

    /// <summary>The datatype library in scope: the nearest datatypeLibrary attribute (RELAX NG §4.3).</summary>
    private static string Library(XElement element) => element.AncestorsAndSelf().Select(e => (string?)e.Attribute("datatypeLibrary")).FirstOrDefault(l => l is not null) ?? string.Empty;

    // Annotations in other namespaces, such as a:documentation, are not
    // part of the grammar (RELAX NG §4.1).
    private static IEnumerable<XElement> Children(XElement element) => element.Elements().Where(e => e.Name.NamespaceName == Rng);

    private static string Required(XElement element, string attribute) => (string?)element.Attribute(attribute) ?? throw new NotSupportedException($"{element.Name.LocalName} has no {attribute} attribute.");

    private static NotSupportedException Unsupported(XElement element) => new($"The schema uses {element.Name.LocalName}, which this validator does not implement.");
}
