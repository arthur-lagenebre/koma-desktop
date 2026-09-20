using System.Xml.Linq;

namespace Koma.Core.Model;

/// <summary>
/// The language of a text in a core document (§4.4), and the choice between
/// texts that say the same thing in several.
/// </summary>
internal static class KomaLanguage
{
    private static readonly XName XmlLang = XNamespace.Xml + "lang";

    /// <summary>
    /// The nearest <c>xml:lang</c>, the root's included, else the language of
    /// the document.
    /// </summary>
    /// <remarks>
    /// An empty <c>xml:lang</c> says undetermined outright, and stops the
    /// search there rather than falling through to an outer declaration.
    /// </remarks>
    public static string? Of(XElement element, string? documentLanguage)
    {
        for (XElement? current = element; current is not null; current = current.Parent)
        {
            XAttribute? lang = current.Attribute(XmlLang);

            if (lang is not null)
                return lang.Value.Length == 0 ? null : lang.Value;
        }

        return documentLanguage;
    }

    /// <summary>
    /// The text to show a reader, from their languages in order of preference.
    /// </summary>
    /// <remarks>
    /// An exact tag wins over a shared primary language, so that a reader
    /// asking for <c>pt-PT</c> is not given <c>pt-BR</c> when both exist. With
    /// no match at all, the first is still better than none: all of them name
    /// the same thing.
    /// </remarks>
    public static T? Choose<T>(IReadOnlyList<T> texts, Func<T, string?> language, IEnumerable<string> preferred) where T : class
    {
        foreach (string wanted in preferred)
        {
            T? exact = texts.FirstOrDefault(t => string.Equals(language(t), wanted, StringComparison.OrdinalIgnoreCase));

            if (exact is not null)
                return exact;

            T? related = texts.FirstOrDefault(t => language(t) is not null && string.Equals(Primary(language(t)!), Primary(wanted), StringComparison.OrdinalIgnoreCase));

            if (related is not null)
                return related;
        }

        return texts.Count > 0 ? texts[0] : null;
    }

    private static string Primary(string tag) => tag.Split('-')[0];
}
