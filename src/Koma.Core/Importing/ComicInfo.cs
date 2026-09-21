using System.Collections.ObjectModel;
using System.Xml;
using System.Xml.Linq;

namespace Koma.Core.Importing;

/// <summary>
/// The fields of a <c>ComicInfo.xml</c>, as the reference converter reads
/// them: the text of each child element by its local name, and the
/// attributes of each <c>Page</c>.
/// </summary>
/// <remarks>
/// ComicInfo has several versions and no namespace anyone agrees on, so a
/// field is its local name and nothing more. Empty fields are absent: a tag
/// with nothing in it says nothing.
/// </remarks>
public sealed class ComicInfo
{
    public static ComicInfo Empty { get; } = new(new Dictionary<string, string>(), []);

    private ComicInfo(Dictionary<string, string> fields, List<ReadOnlyDictionary<string, string>> pages)
    {
        Fields = fields.AsReadOnly();
        Pages = pages.AsReadOnly();
    }

    public ReadOnlyDictionary<string, string> Fields { get; }

    /// <summary>The <c>Page</c> entries, attribute by attribute, in document order.</summary>
    public ReadOnlyCollection<ReadOnlyDictionary<string, string>> Pages { get; }

    /// <summary>A field's text, or <see langword="null"/> when it is absent or empty.</summary>
    public string? this[string field] => Fields.GetValueOrDefault(field);

    /// <summary>
    /// Reads a ComicInfo, or answers an empty one for an archive without it
    /// or with one that is not well-formed, saying so in the notes.
    /// </summary>
    public static ComicInfo Parse(byte[]? document, List<string> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);

        if (document is null || document.Length == 0)
            return Empty;

        XDocument parsed;

        try
        {
            using var stream = new MemoryStream(document);
            parsed = XDocument.Load(stream);
        }
        catch (XmlException)
        {
            notes.Add("ComicInfo.xml is not well-formed and was ignored");
            return Empty;
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var pages = new List<ReadOnlyDictionary<string, string>>();

        foreach (XElement child in parsed.Root?.Elements() ?? [])
        {
            string name = child.Name.LocalName;

            if (name == "Pages")
            {
                pages.AddRange(child.Elements().Select(page => page.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value, StringComparer.Ordinal).AsReadOnly()));
                continue;
            }

            // The text before any child element, as the reference converter
            // takes it: a field with children and no text of its own is not a
            // value.
            string text = (child.FirstNode as XText)?.Value.Trim() ?? string.Empty;

            if (text.Length > 0)
                fields[name] = text;
        }

        return new ComicInfo(fields, pages);
    }
}
