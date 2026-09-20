namespace Koma.Core.Model;

/// <summary>
/// The lexical forms of §4.3 and §4.5 that more than one core document uses.
/// </summary>
internal static class KomaTokens
{
    /// <summary>
    /// <c>[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?</c>, per §4.3.
    /// </summary>
    public static bool IsToken(string token)
    {
        if (token.Length is 0 or > 63)
            return false;

        if (!IsTokenEdge(token[0]) || !IsTokenEdge(token[^1]))
            return false;

        foreach (char c in token)
        {
            if (!IsTokenEdge(c) && c != '-')
                return false;
        }

        return true;
    }

    /// <summary>
    /// <c>x-[a-z0-9]([a-z0-9-]{0,59}[a-z0-9])?</c>, per §4.5: what follows the
    /// prefix is itself a token, two characters shorter.
    /// </summary>
    public static bool IsPrivateUse(string token) => token.StartsWith("x-", StringComparison.Ordinal) && IsToken(token[2..]) && token.Length <= 63;

    private static bool IsTokenEdge(char c) => c is >= 'a' and <= 'z' or >= '0' and <= '9';
}
