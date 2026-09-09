using System.Text;

namespace Koma.Core.Packaging;

/// <summary>
/// Why an entry name fails the ZIP profile of §3.
/// </summary>
public enum EntryNameProblem
{
    None,

    /// <summary>Empty, or whitespace only.</summary>
    Empty,

    /// <summary>Starts with <c>/</c>, or carries a drive letter.</summary>
    Absolute,

    /// <summary>Contains a backslash, which §3 forbids outright.</summary>
    Backslash,

    /// <summary>Contains an empty segment: a leading, trailing or doubled <c>/</c>.</summary>
    EmptySegment,

    /// <summary>Contains a <c>.</c> segment.</summary>
    CurrentDirectorySegment,

    /// <summary>Contains a <c>..</c> segment.</summary>
    ParentDirectorySegment,

    /// <summary>Not in Unicode NFC.</summary>
    NotNormalized,
}

/// <summary>
/// The entry naming rules of §3: UTF-8, <c>/</c> separators, Unicode NFC,
/// package-root-relative, with absolute paths, dot segments, backslashes and
/// traversal constructs forbidden, and logical names unique after normalization
/// and case folding.
/// </summary>
public static class KomaEntryName
{
    /// <summary>
    /// Checks a single name against §3.
    /// </summary>
    public static bool TryValidate(string? name, out EntryNameProblem problem)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            problem = EntryNameProblem.Empty;
            return false;
        }

        // Checked before segmentation: a backslash is forbidden as a character,
        // not merely as a separator, so a name containing one is rejected even
        // where it could not denote a directory.
        if (name.Contains('\\', StringComparison.Ordinal))
        {
            problem = EntryNameProblem.Backslash;
            return false;
        }

        if (name[0] == '/' || HasDriveLetter(name))
        {
            problem = EntryNameProblem.Absolute;
            return false;
        }

        foreach (Range range in name.AsSpan().Split('/'))
        {
            ReadOnlySpan<char> segment = name.AsSpan()[range];

            if (segment.IsEmpty)
            {
                problem = EntryNameProblem.EmptySegment;
                return false;
            }

            if (segment.SequenceEqual("."))
            {
                problem = EntryNameProblem.CurrentDirectorySegment;
                return false;
            }

            if (segment.SequenceEqual(".."))
            {
                problem = EntryNameProblem.ParentDirectorySegment;
                return false;
            }
        }

        // §3 requires the stored name to be NFC already. A name in NFD denotes
        // the same logical path but is a different byte sequence, and accepting
        // it would mean two producers disagreeing on what the package contains.
        // This needs full ICU data, which is why InvariantGlobalization is
        // pinned to false in Directory.Build.props.
        if (!name.IsNormalized(NormalizationForm.FormC))
        {
            problem = EntryNameProblem.NotNormalized;
            return false;
        }

        problem = EntryNameProblem.None;
        return true;
    }

    /// <summary>
    /// A drive-letter prefix such as <c>C:/</c>.
    /// </summary>
    /// <remarks>
    /// §3 forbids absolute paths without saying whether a Windows drive letter
    /// counts, since it does not begin with a separator. Treating it as one
    /// follows from the same clause's ban on traversal constructs, and Windows
    /// is this application's primary target, so the permissive reading is the
    /// dangerous one.
    /// </remarks>
    private static bool HasDriveLetter(string name) =>
        name.Length >= 2 && name[1] == ':' && char.IsAsciiLetter(name[0]);

    /// <summary>
    /// The form in which two names are compared for uniqueness under §3.
    /// </summary>
    /// <remarks>
    /// <para>
    /// §3 requires logical names to be unique after Unicode normalization and
    /// case folding. Normalization is unambiguous; case folding is not. Unicode
    /// defines both a simple and a full folding, and they disagree: full
    /// folding maps <c>ß</c> to <c>ss</c>, so <c>straße.jpg</c> and
    /// <c>STRASSE.jpg</c> collide under full folding and not under simple.
    /// </para>
    /// <para>
    /// This uses simple folding, via invariant upper-casing, which is what .NET
    /// offers without a dependency. Upper rather than lower because it maps
    /// both Greek sigmas onto one, where lower-casing keeps them apart.
    /// </para>
    /// </remarks>
    public static string FoldForUniqueness(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return name.Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }

    /// <summary>
    /// Whether two names are the same logical name under §3.
    /// </summary>
    public static bool AreSameLogicalName(string left, string right) =>
        string.Equals(FoldForUniqueness(left), FoldForUniqueness(right), StringComparison.Ordinal);
}
