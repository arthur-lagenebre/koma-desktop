using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Koma.Core;

/// <summary>
/// A serialized KOMA version, <c>major.minor</c> per §5.1.
/// </summary>
/// <remarks>
/// Both parts are the <c>Integer</c> of §4: unsigned decimal, no sign, no
/// leading zeros, no whitespace. <c>0.09</c> and <c>00.9</c> are therefore not
/// alternative spellings of a valid version but malformed documents, and
/// <see cref="TryParse"/> rejects them.
/// </remarks>
public readonly record struct KomaVersion(int Major, int Minor) : IComparable<KomaVersion>
{
    /// <summary>
    /// The version this build implements. §5.1 requires a KOMA 0.9
    /// implementation to support <c>version="0.9"</c>, and defines an
    /// implementation's supported version as the highest it fully implements.
    /// </summary>
    public static KomaVersion Supported => new(0, 9);

    /// <summary>
    /// Whether this is a pre-release version. §5.0 gives major version 0 its
    /// own compatibility rules: no forward compatibility, and any difference
    /// between two <c>0.x</c> versions is disqualifying.
    /// </summary>
    public bool IsPreRelease => Major == 0;

    /// <summary>
    /// Parses the serialized form, enforcing the <c>Integer</c> lexical rules
    /// of §4 rather than accepting anything numerically equivalent.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out KomaVersion version)
    {
        version = default;

        if (string.IsNullOrEmpty(text))
            return false;

        int separator = text.IndexOf('.', StringComparison.Ordinal);

        // Exactly one separator: "1" and "1.0.0" are both malformed.
        if (separator <= 0 || separator == text.Length - 1)
            return false;

        ReadOnlySpan<char> majorText = text.AsSpan(0, separator);
        ReadOnlySpan<char> minorText = text.AsSpan(separator + 1);

        if (minorText.Contains('.'))
            return false;

        if (!TryParsePart(majorText, out int major) || !TryParsePart(minorText, out int minor))
            return false;

        version = new KomaVersion(major, minor);
        return true;
    }

    private static bool TryParsePart(ReadOnlySpan<char> text, out int value)
    {
        value = 0;

        // NumberStyles.None already rejects a sign and surrounding whitespace.
        // Leading zeros it would accept, so §4 forces an explicit check.
        if (text.Length > 1 && text[0] == '0')
            return false;

        foreach (char c in text)
        {
            // char.IsDigit is true for non-ASCII decimal digits; §4 says decimal
            // in the plain sense, so Arabic-Indic digits are not a valid spelling.
            if (c is < '0' or > '9')
                return false;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Parses the serialized form, or throws.
    /// </summary>
    public static KomaVersion Parse(string text)
    {
        if (!TryParse(text, out KomaVersion version))
            throw new FormatException($"'{text}' is not a KOMA version of the form major.minor (§5.1).");

        return version;
    }

    /// <summary>
    /// Orders by major, then minor. Ordering is meaningful only within a shared
    /// major version of 1 or above; §5.0 makes comparison between differing
    /// <c>0.x</c> versions disqualifying rather than a matter of degree.
    /// </summary>
    public int CompareTo(KomaVersion other)
    {
        int byMajor = Major.CompareTo(other.Major);

        return byMajor != 0 ? byMajor : Minor.CompareTo(other.Minor);
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}");

    public static bool operator <(KomaVersion left, KomaVersion right) => left.CompareTo(right) < 0;

    public static bool operator <=(KomaVersion left, KomaVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >(KomaVersion left, KomaVersion right) => left.CompareTo(right) > 0;

    public static bool operator >=(KomaVersion left, KomaVersion right) => left.CompareTo(right) >= 0;
}
