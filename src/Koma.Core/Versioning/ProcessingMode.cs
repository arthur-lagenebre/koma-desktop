namespace Koma.Core.Versioning;

/// <summary>
/// The three processing modes of §5.3.
/// </summary>
public enum ProcessingMode
{
    /// <summary>
    /// The reader must not process the document as KOMA of its own major
    /// version. §5.3 says it SHOULD be reported as an unsupported version
    /// rather than as an invalid publication; §5.0 raises that to a MUST when
    /// a major version of 0 is involved.
    /// </summary>
    /// <remarks>
    /// This is the distinction that has to survive all the way to the user
    /// interface. A file the reader cannot open because it is from a different
    /// era of the format is not a broken file, and telling someone their
    /// publication is invalid when it is merely unsupported is a wrong answer,
    /// not a rough edge.
    /// </remarks>
    UnsupportedMajor,

    /// <summary>
    /// Unknown core elements, unknown core attributes, invalid open-vocabulary
    /// tokens and closed-vocabulary violations are all errors.
    /// </summary>
    Strict,

    /// <summary>
    /// The document uses a newer minor version of the same major version. The
    /// reader ignores what it does not know, keeps enforcing everything it can
    /// evaluate on the rest, and surfaces once per publication that information
    /// was dropped.
    /// </summary>
    /// <remarks>
    /// Unreachable while this build supports a <c>0.x</c> version: §5.0 forbids
    /// entering forward-compatible mode for major version 0. It exists so the
    /// portal reads as §5.3 is written, rather than encoding today's version as
    /// if it were a permanent property of the format.
    /// </remarks>
    ForwardCompatible
}
