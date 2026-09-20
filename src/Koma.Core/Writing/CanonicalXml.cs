using System.Text;
using System.Xml.Linq;

namespace Koma.Core.Writing;

/// <summary>
/// Writes a core document in the canonical form of §14.1.
/// </summary>
/// <remarks>
/// <para>
/// One element per line, two spaces per level of depth, every attribute on
/// the line of its start tag, an element with no content self-closing, an
/// element with text on one line with it, and a final LF. UTF-8 without a
/// BOM, LF endings, and the declaration §14.1 fixes.
/// </para>
/// <para>
/// Attribute order is not decided here: §14.1 gives it element by element,
/// and this writer keeps the order of the document it is handed. It says how
/// a document is laid out, not what belongs in it.
/// </para>
/// <para>
/// Namespace declarations are written where the document carries them, which
/// §14.1 allows on the root and on foreign content inside <c>Extensions</c>.
/// </para>
/// </remarks>
public static class CanonicalXml
{
    private const string Declaration = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";
    private const string Indent = "  ";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The document as the bytes a package carries.</summary>
    public static byte[] Write(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        XElement root = document.Root ?? throw new ArgumentException("The document has no root element.", nameof(document));
        var text = new StringBuilder();

        text.Append(Declaration).Append('\n');
        Write(root, depth: 0, text);

        return Utf8.GetBytes(text.ToString());
    }

    private static void Write(XElement element, int depth, StringBuilder text)
    {
        string name = Name(element);
        string start = name + Declarations(element) + Attributes(element);
        XElement[] children = [.. element.Elements()];

        for (int i = 0; i < depth; i++)
            text.Append(Indent);

        if (children.Length == 0)
        {
            string content = element.Value.Trim();

            // §14.1 leaves no insignificant whitespace inside an element that
            // carries Normalized text, so the text stays on this line.
            text.Append('<').Append(start);
            text.Append(content.Length == 0 ? "/>" : $">{EscapeText(content)}</{name}>");
            text.Append('\n');

            return;
        }

        text.Append('<').Append(start).Append(">\n");

        foreach (XElement child in children)
            Write(child, depth + 1, text);

        for (int i = 0; i < depth; i++)
            text.Append(Indent);

        text.Append("</").Append(name).Append(">\n");
    }

    /// <remarks>
    /// What the element declares itself, and nothing inherited. An element in
    /// no namespace under a default namespace has to undeclare it, or it
    /// would be read as belonging to that namespace; a parser does not always
    /// keep that declaration as an attribute, so it is written back here.
    /// </remarks>
    private static string Declarations(XElement element)
    {
        var text = new StringBuilder();
        bool undeclares = false;

        foreach (XAttribute attribute in element.Attributes().Where(a => a.IsNamespaceDeclaration))
        {
            string prefix = attribute.Name.LocalName;

            text.Append(prefix == "xmlns" ? $" xmlns=\"{EscapeAttribute(attribute.Value)}\"" : $" xmlns:{prefix}=\"{EscapeAttribute(attribute.Value)}\"");
            undeclares |= prefix == "xmlns" && attribute.Value.Length == 0;
        }

        if (element.Name.Namespace == XNamespace.None && !undeclares && element.Parent is { } parent && parent.Name.Namespace != XNamespace.None)
            text.Append(" xmlns=\"\"");

        return text.ToString();
    }

    private static string Attributes(XElement element)
    {
        var text = new StringBuilder();

        foreach (XAttribute attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
            text.Append(' ').Append(Name(attribute)).Append("=\"").Append(EscapeAttribute(attribute.Value)).Append('"');

        return text.ToString();
    }

    private static string Name(XElement element)
    {
        string? prefix = element.GetPrefixOfNamespace(element.Name.Namespace);

        return prefix is null ? element.Name.LocalName : $"{prefix}:{element.Name.LocalName}";
    }

    private static string Name(XAttribute attribute)
    {
        XNamespace space = attribute.Name.Namespace;

        if (space == XNamespace.None)
            return attribute.Name.LocalName;

        // An unprefixed attribute is in no namespace, so a namespaced one
        // always has a prefix to be written under.
        string prefix = attribute.Parent?.GetPrefixOfNamespace(space) ?? throw new ArgumentException($"{attribute.Name} is in a namespace with no prefix to write it under.", nameof(attribute));

        return $"{prefix}:{attribute.Name.LocalName}";
    }

    private static string EscapeText(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);

    // A quotation mark closes an attribute value, and nothing else here.
    private static string EscapeAttribute(string value) => EscapeText(value).Replace("\"", "&quot;", StringComparison.Ordinal);
}
