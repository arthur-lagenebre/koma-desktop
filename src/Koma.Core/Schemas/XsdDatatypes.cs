using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Koma.Core.Schemas;

/// <summary>
/// The datatypes the KOMA schemas use: RELAX NG's own <c>string</c> and
/// <c>token</c>, and eight of XML Schema's.
/// </summary>
/// <remarks>
/// A schema naming any other type is refused when it loads, rather than
/// having that type accept everything.
/// </remarks>
internal static partial class XsdDatatypes
{
    private const string XmlSchema = "http://www.w3.org/2001/XMLSchema-datatypes";

    private static readonly HashSet<string> Implemented = new(StringComparer.Ordinal)
    {
        "string",
        "NCName",
        "language",
        "anyURI",
        "date",
        "dateTime",
        "gYear",
        "gYearMonth"
    };

    public static void Check(Datatype type)
    {
        if (type.Library != XmlSchema || !Implemented.Contains(type.Name))
            throw new NotSupportedException($"The datatype {type.Name} of '{type.Library}' is not implemented.");
    }

    /// <summary>Whether a value is in the type and matches every pattern facet.</summary>
    public static bool Allows(Datatype type, Regex[] patterns, string value)
    {
        // Every type but string collapses whitespace before it looks.
        string text = type.Name == "string" ? value : Collapse(value);

        return Lexical(type.Name, text) && patterns.All(p => p.IsMatch(text));
    }

    /// <summary>
    /// A <c>value</c> pattern: exact for <c>string</c>, whitespace collapsed
    /// on both sides for <c>token</c> and every XML Schema type the schemas
    /// compare values of.
    /// </summary>
    public static bool Equal(Datatype type, string expected, string value) => type is { Library: "", Name: "string" } ? expected == value : Collapse(expected) == Collapse(value);

    /// <summary>
    /// An XML Schema pattern facet as a .NET expression, anchored at both
    /// ends as XML Schema anchors every pattern.
    /// </summary>
    /// <remarks>
    /// XML Schema's <c>\s</c> is four characters — space, tab, carriage return
    /// and line feed — where .NET's is every Unicode space, the no-break space
    /// included: a title of one no-break space is not empty to the schema, and
    /// must not be here. <c>\S</c> and <c>.</c> are translated to match, inside
    /// character classes as well as outside them.
    /// </remarks>
    public static Regex Pattern(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var translated = new StringBuilder();
        bool inClass = false;

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];

            if (c == '\\' && i + 1 < pattern.Length)
            {
                char escaped = pattern[++i];

                translated.Append(escaped switch
                {
                    's' => inClass ? @" \t\n\r" : @"[ \t\n\r]",
                    'S' => inClass ? @"\u0000-\u0008\u000B\u000C\u000E-\u001F\u0021-\uFFFF" : @"[^ \t\n\r]",
                    'i' or 'c' or 'I' or 'C' => throw new NotSupportedException($"The pattern escape \\{escaped} is not implemented."),
                    _ => $"\\{escaped}"
                });

                continue;
            }

            if (c == '[' && !inClass)
                inClass = true;
            else if (c == ']' && inClass)
                inClass = false;

            // Outside a class, XML Schema's dot stops at both line ends.
            translated.Append(c == '.' && !inClass ? @"[^\n\r]" : c.ToString());
        }

        return new Regex($@"\A(?:{translated})\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private static bool Lexical(string name, string text) => name switch
    {
        "string" or "anyURI" => true,
        "NCName" => IsNCName(text),
        "language" => Language().IsMatch(text),
        "date" => Date().Match(text) is { Success: true } m && IsDay(m),
        "dateTime" => DateAndTime().Match(text) is { Success: true } m && IsDay(m) && IsTime(m),
        "gYear" => Year().IsMatch(text),
        "gYearMonth" => YearMonth().Match(text) is { Success: true } m && int.Parse(m.Groups["month"].Value, CultureInfo.InvariantCulture) is >= 1 and <= 12,
        _ => false
    };

    private static bool IsNCName(string text)
    {
        try
        {
            XmlConvert.VerifyNCName(text);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static bool IsDay(Match match)
    {
        // The last four digits are enough for the leap rule, 10 000 being a
        // multiple of 400, and they keep a year of any length from overflowing.
        string digits = match.Groups["year"].Value.TrimStart('-');
        int year = int.Parse(digits[^4..], CultureInfo.InvariantCulture);
        int month = int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
        int day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);

        if (month is < 1 or > 12 || day < 1)
            return false;

        // The proleptic Gregorian leap rule, which holds for years .NET's
        // calendar does not reach.
        bool leap = year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);
        int length = month == 2 ? (leap ? 29 : 28) : month is 4 or 6 or 9 or 11 ? 30 : 31;

        return day <= length;
    }

    private static bool IsTime(Match match)
    {
        int hour = int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture);
        int minute = int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture);
        int second = int.Parse(match.Groups["second"].Value, CultureInfo.InvariantCulture);

        // XML Schema 1.0 allows 24:00:00 for the end of a day, and nothing
        // else past 23:59:59.
        return hour < 24 ? minute < 60 && second < 60 : minute == 0 && second == 0 && !match.Groups["fraction"].Success;
    }

    private static string Collapse(string value) => Spaces().Replace(value, " ").Trim(' ');

    [GeneratedRegex(@"[ \t\n\r]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\A[a-zA-Z]{1,8}(-[a-zA-Z0-9]{1,8})*\z")]
    private static partial Regex Language();

    [GeneratedRegex(@"\A(?<year>-?(?!0000)[0-9]{4,})-(?<month>[0-9]{2})-(?<day>[0-9]{2})(Z|[+-](0[0-9]|1[0-4]):[0-5][0-9])?\z")]
    private static partial Regex Date();

    [GeneratedRegex(@"\A(?<year>-?(?!0000)[0-9]{4,})-(?<month>[0-9]{2})-(?<day>[0-9]{2})T(?<hour>[0-9]{2}):(?<minute>[0-9]{2}):(?<second>[0-9]{2})(?<fraction>\.[0-9]+)?(Z|[+-](0[0-9]|1[0-4]):[0-5][0-9])?\z")]
    private static partial Regex DateAndTime();

    [GeneratedRegex(@"\A-?(?!0000)[0-9]{4,}(Z|[+-](0[0-9]|1[0-4]):[0-5][0-9])?\z")]
    private static partial Regex Year();

    [GeneratedRegex(@"\A(?<year>-?(?!0000)[0-9]{4,})-(?<month>[0-9]{2})(Z|[+-](0[0-9]|1[0-4]):[0-5][0-9])?\z")]
    private static partial Regex YearMonth();
}
