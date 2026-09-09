namespace Koma.Core.Versioning;

/// <summary>
/// Selects the processing mode of §5.3. The first thing any reader does with a
/// publication, before it looks at the data model at all.
/// </summary>
public static class VersionPortal
{
    /// <summary>
    /// Compares a document version with a reader's supported version.
    /// </summary>
    /// <param name="document">The version the document declares.</param>
    /// <param name="supported">
    /// The highest version the reader fully implements (§5.1). Defaults to
    /// <see cref="KomaVersion.Supported"/>.
    /// </param>
    public static ProcessingMode SelectMode(KomaVersion document, KomaVersion? supported = null)
    {
        KomaVersion reader = supported ?? KomaVersion.Supported;

        // §5.3, first branch. Two conditions, and the second is easy to
        // under-read: it is not "both are 0.x and differ" but "either side has
        // a major version of 0 and the versions are not identical". A 0.9
        // document read by a 1.0 implementation lands here too, as does a 1.0
        // document read by this 0.9 build.
        if (document.Major != reader.Major)
            return ProcessingMode.UnsupportedMajor;

        if ((document.IsPreRelease || reader.IsPreRelease) && document != reader)
            return ProcessingMode.UnsupportedMajor;

        // §5.0: identical 0.x versions are processed in strict mode, and
        // forward-compatible mode is never entered for major version 0. With
        // the majors equal and both pre-release, equality is all that is left.
        if (document.IsPreRelease)
            return ProcessingMode.Strict;

        // §5.3, remaining two branches: same major version of 1 or above.
        return document.Minor > reader.Minor ? ProcessingMode.ForwardCompatible : ProcessingMode.Strict;
    }

    /// <summary>
    /// Whether the reader may process the document at all.
    /// </summary>
    /// <remarks>
    /// A convenience over <see cref="SelectMode"/> for callers that only need
    /// the gate. Callers that report to a user should use the mode itself, so
    /// that an unsupported version can be reported as such rather than folded
    /// into a generic failure.
    /// </remarks>
    public static bool CanProcess(KomaVersion document, KomaVersion? supported = null) => SelectMode(document, supported) != ProcessingMode.UnsupportedMajor;
}
