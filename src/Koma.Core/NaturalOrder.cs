namespace Koma.Core;

/// <summary>
/// Orders text the way a reader would: page2 before page10, volume 9 before
/// volume 10.
/// </summary>
/// <remarks>
/// <para>
/// Runs of digits compare as numbers and everything else as lowercase text,
/// because the names people give files and volumes are seldom padded. It is
/// the order <c>tools/cbz_to_koma.py</c> sorts a CBZ in, and the one the
/// shelf numbers a series in, where HS2 and 3.5 are volume numbers too.
/// </para>
/// <para>
/// Numbers of any length compare without being parsed: once the leading
/// zeros are gone the longer run is the larger number, and runs of one
/// length compare as text. 007 and 7 are equal, as they are to a reader.
/// </para>
/// <para>
/// A missing value sorts first. Declared for nullable strings, so that it
/// orders a key that may be absent, such as a volume number, as readily as
/// a file name, which never is.
/// </para>
/// </remarks>
public sealed class NaturalOrder : IComparer<string?>
{
    public static NaturalOrder Instance { get; } = new();

    private NaturalOrder()
    {
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
            return 0;

        if (x is null)
            return -1;

        if (y is null)
            return 1;

        int l = 0;
        int r = 0;

        while (l < x.Length && r < y.Length)
        {
            if (char.IsAsciiDigit(x[l]) && char.IsAsciiDigit(y[r]))
            {
                int endLeft = EndOfRun(x, l);
                int endRight = EndOfRun(y, r);
                int numbers = CompareNumbers(x.AsSpan(l, endLeft - l), y.AsSpan(r, endRight - r));

                if (numbers != 0)
                    return numbers;

                l = endLeft;
                r = endRight;
                continue;
            }

            int letters = char.ToLowerInvariant(x[l]).CompareTo(char.ToLowerInvariant(y[r]));

            if (letters != 0)
                return letters;

            l++;
            r++;
        }

        return (x.Length - l).CompareTo(y.Length - r);
    }

    private static int EndOfRun(string text, int start)
    {
        int end = start;

        while (end < text.Length && char.IsAsciiDigit(text[end]))
            end++;

        return end;
    }

    private static int CompareNumbers(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        left = left.TrimStart('0');
        right = right.TrimStart('0');

        return left.Length != right.Length ? left.Length.CompareTo(right.Length) : left.SequenceCompareTo(right);
    }
}
